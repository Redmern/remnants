using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// One concrete way a unit can move this turn. <see cref="Source"/> names what gave it its pattern
/// (an item name or a body state such as "missing legs"); null for the plain base walk.
/// </summary>
public sealed record MoveMode(MovePattern Pattern, int Range, int ApCost, bool IgnoresTerrain, string? Source = null);

/// <summary>How a unit moves: its modes (the first is the primary walk) and what each terrain costs it.</summary>
public sealed record MovementProfile(IReadOnlyList<MoveMode> Modes, IReadOnlyDictionary<Terrain, int> TerrainCosts)
{
    public MoveMode Primary => Modes[0];

    /// <summary>Move cost of a terrain, or null if impassable.</summary>
    public int? Cost(Terrain terrain) => TerrainCosts.TryGetValue(terrain, out var c) ? c : null;
}

/// <summary>An attack as this unit can perform it right now, with its final numbers.</summary>
public sealed record ActionProfile(
    string Id,
    string Name,
    string? ItemId,
    AttackPattern Pattern,
    int Reach,
    double MinDamage,
    double MaxDamage,
    int ApCost,
    double Accuracy,
    int Noise,
    int Area = 0,
    int Reveal = 1,
    BaneDef? Bane = null);

/// <summary>
/// Everything the rules need about a unit, derived from attributes + carried gear + body state + stance.
/// <see cref="Stealth"/> and <see cref="Detection"/> decide who sees whom inside sight (<see cref="Detection"/>);
/// <see cref="CoverBonus"/> is extra cover from bracing.
/// </summary>
public sealed record UnitProfile(
    MovementProfile Movement,
    IReadOnlyList<ActionProfile> Actions,
    int MaxAp,
    double Initiative,
    double Sight,
    double CarryCapacity,
    double Load,
    double Armor,
    double Dodge,
    double Stealth = 0,
    double Detection = 0,
    double CoverBonus = 0)
{
    public ActionProfile? Action(string id) => Actions.FirstOrDefault(a => a.Id == id);
}

/// <summary>
/// Derives a <see cref="UnitProfile"/>. Pure: the same unit and data always give the same profile,
/// and nothing about it is stored, so gear swaps and injuries take effect immediately.
/// </summary>
public static class ProfileDeriver
{
    public static UnitProfile Derive(ScavengeData data, Unit unit)
    {
        var rules = data.Rules;
        var equipped = Equipped(data, unit).ToList();
        var working = equipped.Where(i => MeetsRequirement(unit.Body, i.Requires)).ToList();

        var load = unit.AllItems.Sum(id => data.Items[id].Weight);
        var capacity = rules.Carry.Base + unit.Get(AttributeKind.Strength) * rules.Carry.PerStrength + equipped.Sum(i => i.CarryBonus)
            + unit.Hook(PerkHooks.CarryBonus);
        var agility = unit.Get(AttributeKind.Agility);
        var fog = rules.Fog;
        var head = unit.Body.RegionEfficiency(BodyRegion.Head);
        var enemy = unit.EnemyDefId is { } enemyId ? data.Enemies[enemyId] : null;

        var sight = rules.Sight.Base + unit.Get(AttributeKind.Perception) * rules.Sight.PerPerception + equipped.Sum(i => i.SightBonus)
            + unit.Hook(PerkHooks.SightBonus);
        var detection = fog.Detection.Base + (unit.Get(AttributeKind.Perception) - 50) * fog.Detection.PerPerception + working.Sum(i => i.Detection)
            + unit.Hook(PerkHooks.DetectionBonus);
        if (unit.Lookout)
        {
            sight += fog.Lookout.SightBonus;
            detection += fog.Lookout.DetectionBonus + unit.Get(AttributeKind.Perception) * fog.Lookout.DetectionPerPerception;
        }
        if (unit.Stance == Stance.Overwatch) detection += fog.Overwatch.DetectionBonus;
        var stealth = fog.Stealth.Base
            + (agility - 50) * fog.Stealth.PerAgility
            - load * fog.Stealth.PerKg
            + equipped.Sum(i => i.Stealth)
            + (enemy?.Stealth ?? 0)
            + (unit.Body.RegionEfficiency(BodyRegion.Legs) - 1) * fog.Stealth.LegEfficiency
            + unit.Hook(PerkHooks.StealthBonus);

        return new UnitProfile(
            Movement: DeriveMovement(data, unit, working, capacity > 0 ? load / capacity : double.PositiveInfinity),
            Actions: DeriveActions(data, unit, working),
            MaxAp: Math.Max(1, rules.Turn.BaseAp + equipped.Sum(i => i.ApBonus)),
            Initiative: agility * rules.Turn.InitiativePerAgility + equipped.Sum(i => i.Initiative) - load * rules.Turn.InitiativePerLoad,
            Sight: Math.Max(1, sight * head),
            CarryCapacity: capacity,
            Load: load,
            Armor: equipped.Sum(i => i.Armor),
            Dodge: (agility - 50) * rules.Combat.DodgePerAgility + equipped.Sum(i => i.Dodge),
            Stealth: stealth,
            Detection: detection * head,
            CoverBonus: unit.Stance == Stance.Brace ? fog.Brace.CoverBonus + unit.Hook(PerkHooks.BraceCoverBonus) : 0);
    }

    /// <summary>Equipped item defs in slot order (Weapon, Body, Tool), the order modifiers apply in.</summary>
    private static IEnumerable<ItemDef> Equipped(ScavengeData data, Unit unit) =>
        unit.Equipment.OrderBy(kv => kv.Key).Select(kv => data.Items[kv.Value]);

    public static bool MeetsRequirement(Body body, BodyRequirement? requirement) =>
        requirement is null || body.RegionEfficiency(requirement.Region) + 1e-9 >= requirement.MinEfficiency;

    /// <summary>
    /// Primary range = base + Agility bonus + gear bonuses - encumbrance, scaled by leg efficiency,
    /// then body rules (missing leg, cybernetic legs), then the tightest cap. Gear may swap the pattern
    /// or add modes (a grappling hook's knight jump); body rules apply last so a missing leg always wins.
    /// </summary>
    private static MovementProfile DeriveMovement(ScavengeData data, Unit unit, List<ItemDef> working, double loadRatio)
    {
        var rules = data.Rules.Movement;
        var pattern = rules.BasePattern;
        var range = (double)rules.BaseRange + Math.Floor(unit.Get(AttributeKind.Agility) / rules.AgilityPerRange);
        string? patternSource = null;
        var caps = new List<int>();
        var extraModes = new List<(MoveModeDef Mode, string Source)>();
        var costs = Enum.GetValues<Terrain>()
            .Where(t => data.Terrain(t).Passable)
            .ToDictionary(t => t, t => data.Terrain(t).MoveCost);

        void Apply(MovementModifier? m, string source)
        {
            if (m is null) return;
            if (m.SetPattern is { } p) (pattern, patternSource) = (p, source);
            range += m.RangeBonus;
            if (m.MaxRange is { } cap) caps.Add(cap);
            extraModes.AddRange(m.AddModes.Select(mode => (mode, source)));
            foreach (var (terrain, cost) in m.TerrainCosts)
                if (costs.TryGetValue(terrain, out var current)) costs[terrain] = Math.Min(current, Math.Max(1, cost));
        }

        foreach (var item in working) Apply(item.Movement, item.Name);

        range -= rules.Encumbrance.Where(s => loadRatio + 1e-9 >= s.Load).Select(s => s.RangePenalty).DefaultIfEmpty(0).Max();
        var carrying = unit.CarryingId is not null;
        if (carrying)
        {
            range -= data.Rules.Coop.CarryRangePenalty;
            extraModes.Clear();
        }
        range = Math.Floor(range * unit.Body.RegionEfficiency(BodyRegion.Legs) + 1e-9);

        foreach (var rule in rules.Body.Where(r => BodyMatches(unit.Body, r)))
            Apply(rule.Modifier, $"{rule.Condition} {rule.Region}".ToLowerInvariant());

        var primary = Math.Max(rules.MinRange, (int)range);
        if (caps.Count > 0) primary = Math.Min(primary, caps.Min());

        var modes = new List<MoveMode> { new(pattern, primary, rules.ApCost, false, carrying ? "carrying" : patternSource) };
        modes.AddRange(extraModes.Select(m => new MoveMode(m.Mode.Pattern, m.Mode.Range, m.Mode.ApCost, m.Mode.IgnoresTerrain, m.Source)));
        return new MovementProfile(modes, costs);
    }

    private static bool BodyMatches(Body body, BodyMovementRule rule)
    {
        var count = body.Parts.Count(kv =>
            Body.RegionOf(kv.Key) == rule.Region
            && kv.Value.Condition == rule.Condition
            && (rule.Condition != PartCondition.Prosthetic || kv.Value.ProstheticQuality + 1e-9 >= rule.MinQuality));
        return count >= rule.MinCount;
    }

    /// <summary>
    /// Weapon actions plus bare hands. Accuracy adds skill and Perception, then scales with the arms
    /// (best arm, or both arms for two-handed) and, for ranged attacks, the head (eyes).
    /// A two-handed action needs two working arms; any action needs at least one.
    /// </summary>
    private static List<ActionProfile> DeriveActions(ScavengeData data, Unit unit, List<ItemDef> working)
    {
        var combat = data.Rules.Combat;
        var left = unit.Body[BodyPart.LeftArm].Efficiency;
        var right = unit.Body[BodyPart.RightArm].Efficiency;
        var result = new List<ActionProfile>();

        void Add(ActionDef a, string? itemId)
        {
            if (a.TwoHanded && (left <= 0 || right <= 0)) return;
            var arms = a.TwoHanded ? (left + right) / 2 : Math.Max(left, right);
            if (arms <= 0) return;

            var accuracy = a.Accuracy
                + unit.Get(a.Skill) * combat.SkillAccuracy
                + (unit.Get(AttributeKind.Perception) - 50) * combat.PerceptionAccuracy;
            accuracy *= arms;
            if (a.Pattern != AttackPattern.Melee) accuracy *= unit.Body.RegionEfficiency(BodyRegion.Head);
            accuracy += unit.Hook(PerkHooks.AccuracyBonus);

            var damageScale = 1 + (unit.Get(AttributeKind.Strength) - 50) / 100 * a.StrengthScaling;
            if (a.Pattern == AttackPattern.Melee) damageScale *= arms * (1 + unit.Hook(PerkHooks.MeleeDamageBonus));
            damageScale = Math.Max(0, damageScale);

            result.Add(new ActionProfile(a.Id, a.Name, itemId, a.Pattern, a.Reach,
                a.MinDamage * damageScale, a.MaxDamage * damageScale, a.ApCost, Math.Max(0, accuracy), a.Noise, a.Area, a.Reveal, a.Bane));
        }

        foreach (var item in working)
            foreach (var a in item.Actions)
                Add(a, item.Id);
        // Drones and devices have no fists.
        if (result.All(r => r.Id != combat.Unarmed.Id) && unit.Kind is not (UnitKind.Drone or UnitKind.Device)) Add(combat.Unarmed, null);
        return result;
    }
}
