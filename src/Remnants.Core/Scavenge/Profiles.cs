using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// One concrete way a unit can move this turn. <see cref="Source"/> names what gave it its pattern
/// (an item name or a body state such as "missing legs"); null for the plain base walk.
/// The primary mode's walk pays MP per terrain cost; any other mode's move costs a flat <see cref="ApCost"/> in MP.
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
    BaneDef? Bane = null,
    double ArmorPierce = 0,
    double InjuryMultiplier = 1,
    int CloseRange = 0,
    int CloseApPenalty = 0,
    IReadOnlyList<BaneDef>? ExtraBanes = null,
    double NetworkFeedback = 0,
    int? DiagonalReach = null,
    IReadOnlyList<StrikeMode>? Modes = null,
    int Knockback = 0)
{
    /// <summary>Reach along a diagonal.</summary>
    public int DiagonalRange => DiagonalReach ?? Reach;

    /// <summary>Whether the action strikes tiles in a <see cref="StrikeMode"/> pattern rather than the one tile aimed at.</summary>
    public bool Patterned => Modes is { Count: > 0 };
}

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
    double CoverBonus = 0,
    IReadOnlyDictionary<BodyRegion, double>? RegionArmor = null,
    double InjuryResist = 0,
    double MeleeArmor = 0,
    int MaxMp = 0,
    int MaxStamina = 0)
{
    public ActionProfile? Action(string id) => Actions.FirstOrDefault(a => a.Id == id);

    /// <summary>
    /// Armor against a hit on a region: <see cref="Armor"/> (gear that protects every part) plus what covers that region,
    /// plus <see cref="MeleeArmor"/> against melee.
    /// </summary>
    public double ArmorAgainst(BodyRegion region, bool melee) =>
        Armor + (RegionArmor?.GetValueOrDefault(region) ?? 0) + (melee ? MeleeArmor : 0);
}

/// <summary>
/// Derives a <see cref="UnitProfile"/>. Pure: the same unit and data always give the same profile,
/// and nothing about it is stored, so gear swaps and injuries take effect immediately.
/// </summary>
public static class ProfileDeriver
{
    [ThreadStatic] private static Dictionary<Unit, UnitProfile>? _memo;
    [ThreadStatic] private static ScavengeData? _memoData;

    /// <summary>
    /// Remembers each unit's profile until the scope is disposed. Only around read-only queries (sight and detection
    /// checks, AI position scoring) where no unit changes, so the profile cannot go stale. Scopes nest.
    /// </summary>
    public static MemoScope Memo(ScavengeData data)
    {
        if (_memo is not null) return default;
        _memo = new Dictionary<Unit, UnitProfile>(ReferenceEqualityComparer.Instance);
        _memoData = data;
        return new MemoScope(true);
    }

    public readonly struct MemoScope(bool owner) : IDisposable
    {
        public void Dispose()
        {
            if (!owner) return;
            _memo = null;
            _memoData = null;
        }
    }

    public static UnitProfile Derive(ScavengeData data, Unit unit)
    {
        if (_memo is null || !ReferenceEquals(data, _memoData)) return Compute(data, unit);
        if (!_memo.TryGetValue(unit, out var profile)) _memo[unit] = profile = Compute(data, unit);
        return profile;
    }

    private static UnitProfile Compute(ScavengeData data, Unit unit)
    {
        var rules = data.Rules;
        var fitted = data.FittedParts(unit.Body).ToList();
        // Fitted prosthetics count like worn gear: their stats always, their moves and attacks (they are the body).
        var equipped = Equipped(data, unit).Concat(fitted.Select(f => f.Item)).ToList();
        var working = Equipped(data, unit).Where(i => MeetsRequirement(unit.Body, i.Requires)).ToList();
        var active = working.Concat(fitted.Select(f => f.Item)).ToList();

        var load = unit.AllItems.Sum(id => data.Items[id].Weight);
        var capacity = rules.Carry.Base + unit.Get(AttributeKind.Strength) * rules.Carry.PerStrength + equipped.Sum(i => i.CarryBonus)
            + unit.Hook(PerkHooks.CarryBonus);
        var agility = unit.Get(AttributeKind.Agility);
        var fog = rules.Fog;
        var head = unit.Body.RegionEfficiency(BodyRegion.Head);
        var enemy = unit.EnemyDefId is { } enemyId ? data.Enemies[enemyId] : null;

        var sight = rules.Sight.Base + unit.Get(AttributeKind.Perception) * rules.Sight.PerPerception + equipped.Sum(i => i.SightBonus)
            + unit.Hook(PerkHooks.SightBonus);
        var detection = fog.Detection.Base + (unit.Get(AttributeKind.Perception) - 50) * fog.Detection.PerPerception + active.Sum(i => i.Detection)
            + unit.Hook(PerkHooks.DetectionBonus);
        if (unit.Lookout)
        {
            sight += fog.Lookout.SightBonus;
            detection += fog.Lookout.DetectionBonus + unit.Get(AttributeKind.Perception) * fog.Lookout.DetectionPerPerception;
        }
        if (unit.Stance == Stance.Overwatch) detection += fog.Overwatch.DetectionBonus;
        if (unit.Raised)
        {
            sight += rules.Story.RaisedSight;
            detection += rules.Story.RaisedDetection;
        }
        var stealth = fog.Stealth.Base
            + (agility - 50) * fog.Stealth.PerAgility
            - load * fog.Stealth.PerKg
            + equipped.Sum(i => i.Stealth)
            + (enemy?.Stealth ?? 0)
            + (unit.Body.RegionEfficiency(BodyRegion.Legs) - 1) * fog.Stealth.LegEfficiency
            + unit.Hook(PerkHooks.StealthBonus);

        var movement = DeriveMovement(data, unit, active, capacity > 0 ? load / capacity : double.PositiveInfinity);
        return new UnitProfile(
            Movement: movement,
            Actions: DeriveActions(data, unit, working, fitted),
            MaxAp: MaxAp((enemy?.Ap ?? rules.Turn.BaseAp) + equipped.Sum(i => i.ApBonus), Treatment.FieldApPenalty(data.Medicine, unit.Body)),
            Initiative: agility * rules.Turn.InitiativePerAgility + equipped.Sum(i => i.Initiative) - load * rules.Turn.InitiativePerLoad,
            Sight: Math.Max(1, sight * head),
            CarryCapacity: capacity,
            Load: load,
            Armor: equipped.Where(i => i.Coverage is null).Sum(i => i.Armor) + unit.Hook(PerkHooks.ArmorBonus),
            Dodge: (agility - 50) * rules.Combat.DodgePerAgility + equipped.Sum(i => i.Dodge),
            Stealth: stealth,
            Detection: detection * head,
            CoverBonus: (unit.Stance == Stance.Brace ? fog.Brace.CoverBonus + unit.Hook(PerkHooks.BraceCoverBonus) : 0) + unit.CardCover,
            RegionArmor: RegionArmor(equipped),
            InjuryResist: 1 - equipped.Aggregate(1.0, (keep, i) => keep * (1 - Math.Clamp(i.InjuryResist, 0, 1))),
            MeleeArmor: equipped.Sum(i => i.MeleeArmor),
            MaxMp: enemy?.Mp ?? MaxMp(rules, movement.Primary.Range),
            MaxStamina: enemy?.Stamina ?? rules.Turn.BaseStamina);
    }

    /// <summary>Armor of gear with coverage, per region it covers (null when no gear has coverage).</summary>
    private static Dictionary<BodyRegion, double>? RegionArmor(List<ItemDef> equipped)
    {
        var covering = equipped.Where(i => i.Coverage is not null).ToList();
        if (covering.Count == 0) return null;
        return Enum.GetValues<BodyRegion>().ToDictionary(r => r, r => covering.Sum(i => i.Armor * Math.Clamp(i.Coverage!.GetValueOrDefault(r), 0, 1)));
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
        if (data.Cybernetics is { } cyber)
            foreach (var (set, parts) in cyber.ActiveSets(unit.Body))
                if (parts.All(p => unit.Body[p].Efficiency > 0)) Apply(set.Effects.Movement, set.Name);

        range -= rules.Encumbrance.Where(s => loadRatio + 1e-9 >= s.Load).Select(s => s.RangePenalty).DefaultIfEmpty(0).Max();
        var carrying = unit.CarryingId is not null;
        if (carrying)
        {
            range -= data.Rules.Coop.CarryRangePenalty;
            extraModes.Clear();
        }
        range = LegReach(range + unit.CardMove, unit.Body);

        foreach (var rule in rules.Body.Where(r => BodyMatches(unit.Body, r)))
            Apply(rule.Modifier, $"{rule.Condition} {rule.Region}".ToLowerInvariant());

        var primary = Math.Max(rules.MinRange, (int)range);
        if (caps.Count > 0) primary = Math.Min(primary, caps.Min());

        var modes = new List<MoveMode> { new(pattern, primary, rules.ApCost, false, carrying ? "carrying" : patternSource) };
        // The same move from two sources (a jumper leg and the jumper pair) is listed once, at its cheapest.
        modes.AddRange(extraModes
            .GroupBy(m => (m.Mode.Pattern, m.Mode.Range, m.Mode.IgnoresTerrain))
            .Select(g => g.OrderBy(m => m.Mode.ApCost).First())
            .Select(m => new MoveMode(m.Mode.Pattern, m.Mode.Range, m.Mode.ApCost * data.Rules.Turn.MpPerRange, m.Mode.IgnoresTerrain, m.Source)));
        return new MovementProfile(modes, costs);
    }

    /// <summary>
    /// A move reach after the legs' injuries: scaled by leg efficiency, rounded down. The walk uses it; so can any other
    /// move reach (a declared path, a card's dash).
    /// </summary>
    public static int LegReach(double range, Body body) =>
        (int)Math.Floor(range * body.RegionEfficiency(BodyRegion.Legs) + 1e-9);

    /// <summary>
    /// Whether the arms are too hurt for arm-locked attacks (and cards): the best arm, or both averaged for two-handed,
    /// below <c>combat.armLockBelow</c>. Injury efficiency counts as the rest of the profile sees it (painkillers help).
    /// </summary>
    public static bool ArmsLocked(ScavengeData data, Body body, bool twoHanded)
    {
        var left = body.PartEfficiency(BodyPart.LeftArm);
        var right = body.PartEfficiency(BodyPart.RightArm);
        return (twoHanded ? (left + right) / 2 : Math.Max(left, right)) + 1e-9 < data.Rules.Combat.ArmLockBelow;
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
    /// AP a round: the gear's, less what conditions and pain take (medicine.json); never below 1.
    /// </summary>
    private static int MaxAp(int gear, int medical) => Math.Max(1, gear - medical);

    /// <summary>
    /// MP a round: the base for a unit of baseline range (an average walker, attributes 50), shifted per step of range its
    /// agility, load, legs and gear give it above or below that.
    /// </summary>
    private static int MaxMp(ScavengeRules rules, int range)
    {
        var baseline = rules.Movement.BaseRange + (int)Math.Floor(50.0 / rules.Movement.AgilityPerRange);
        return Math.Max(1, rules.Turn.BaseMp + (range - baseline) * rules.Turn.MpPerRange);
    }

    /// <summary>
    /// Weapon actions, attacks built into cybernetic arms, plus bare hands. Accuracy adds skill and Perception, then scales with the arms
    /// (best arm, or both arms for two-handed) and, for ranged attacks, the head (eyes).
    /// A two-handed action needs two working arms; any action needs at least one; an arm-locked one, arms not too hurt.
    /// </summary>
    private static List<ActionProfile> DeriveActions(ScavengeData data, Unit unit, List<ItemDef> working, List<(BodyPart Part, ItemDef Item)> fitted)
    {
        var combat = data.Rules.Combat;
        var left = unit.Body.PartEfficiency(BodyPart.LeftArm);
        var right = unit.Body.PartEfficiency(BodyPart.RightArm);
        var result = new List<ActionProfile>();

        void Add(ActionDef a, string? itemId, double? arm = null)
        {
            if (a.TwoHanded && (left <= 0 || right <= 0)) return;
            if (a.ArmLock && ArmsLocked(data, unit.Body, a.TwoHanded)) return;
            var arms = arm ?? (a.TwoHanded ? (left + right) / 2 : Math.Max(left, right));
            if (arms <= 0) return;

            var accuracy = a.Accuracy
                + unit.Get(a.Skill) * combat.SkillAccuracy
                + (unit.Get(AttributeKind.Perception) - 50) * combat.PerceptionAccuracy;
            accuracy *= arms;
            if (a.Pattern != AttackPattern.Melee) accuracy *= unit.Body.RegionEfficiency(BodyRegion.Head);
            accuracy += unit.Hook(PerkHooks.AccuracyBonus);
            if (a.Pattern != AttackPattern.Melee) accuracy += unit.Equipment.Values.Sum(id => data.Items.TryGetValue(id, out var gear) ? gear.RangedAccuracy : 0);
            accuracy -= GearEffects.MovedPenalty(data, unit, a);

            var damageScale = 1 + (unit.Get(AttributeKind.Strength) - 50) / 100 * a.StrengthScaling;
            if (a.Pattern == AttackPattern.Melee) damageScale *= arms * (1 + unit.Hook(PerkHooks.MeleeDamageBonus));
            damageScale = Math.Max(0, damageScale);

            result.Add(new ActionProfile(a.Id, a.Name, itemId, a.Pattern, a.Reach,
                a.MinDamage * damageScale, a.MaxDamage * damageScale, a.ApCost, Math.Max(0, accuracy), a.Noise, a.Area, a.Reveal, a.Bane,
                a.ArmorPierce, a.InjuryMultiplier, a.CloseRange, a.CloseApPenalty, a.ExtraBanes, a.NetworkFeedback, a.DiagonalReach, a.Modes, a.Knockback));
        }

        foreach (var item in working)
            foreach (var a in item.Actions)
                Add(a, item.Id);
        // A cybernetic arm's own attack (blade, gun) works at that arm's efficiency.
        foreach (var (part, item) in fitted)
            foreach (var a in item.Actions)
                Add(a, item.Id, unit.Body.PartEfficiency(part));
        // Drones and devices have no fists.
        if (result.All(r => r.Id != combat.Unarmed.Id) && unit.Kind is not (UnitKind.Drone or UnitKind.Device or UnitKind.Decoy)) Add(combat.Unarmed, null);
        return result;
    }
}
