using Remnants.Core.Random;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>Attack reach, hit chance and damage: injuries to body parts, HP, Downed and Dead.</summary>
public static class Combat
{
    /// <summary>
    /// Whether <paramref name="action"/> can reach <paramref name="target"/> from <paramref name="from"/>: the target is one
    /// of its <see cref="Tiles"/> and, past the tiles next to the attacker, in line of sight.
    /// </summary>
    public static ScavengeError CheckReach(ScavengeData data, TileMap map, Pos from, ActionProfile action, Pos target)
    {
        if (!map.InBounds(target) || !InPattern(action, from, null, target)) return ScavengeError.OutOfRange;
        return Clear(data, map, from, target) ? ScavengeError.None : ScavengeError.NoLineOfSight;
    }

    /// <summary>
    /// The tiles on the map the action's pattern covers from <paramref name="from"/> (aimed toward <paramref name="facing"/>
    /// for patterns that care), line of sight not checked. Never the attacker's own tile.
    /// </summary>
    public static IEnumerable<Pos> Tiles(TileMap map, ActionProfile action, Pos from, Pos? facing)
    {
        for (var dy = -action.Reach; dy <= action.Reach; dy++)
            for (var dx = -action.Reach; dx <= action.Reach; dx++)
            {
                var p = new Pos(from.X + dx, from.Y + dy);
                if (map.InBounds(p) && InPattern(action, from, facing, p)) yield return p;
            }
    }

    /// <summary>The <see cref="Tiles"/> the action can actually hit: those that pass <see cref="CheckReach"/>.</summary>
    public static IEnumerable<Pos> Hittable(ScavengeData data, TileMap map, ActionProfile action, Pos from, Pos? facing) =>
        Tiles(map, action, from, facing).Where(p => Clear(data, map, from, p));

    /// <summary>
    /// Every tile <paramref name="unit"/> could hit right now with any of its attacks, from where it stands and facing
    /// where it faces. Empty for a unit that cannot act (downed, dead, gone).
    /// </summary>
    public static HashSet<Pos> ThreatZone(ScavengeSim sim, Unit unit)
    {
        var zone = new HashSet<Pos>();
        if (!unit.IsActive) return zone;
        foreach (var action in sim.Profile(unit).Actions)
            zone.UnionWith(Hittable(sim.Data, sim.State.Map, action, unit.Pos, unit.Facing));
        return zone;
    }

    /// <summary>
    /// The tiles a strike in <paramref name="mode"/> covers from <paramref name="from"/> toward <paramref name="direction"/>
    /// (one of the 8 steps), on the map, line of sight not checked: a stab, the straight line of the action's reach
    /// (<see cref="ActionProfile.DiagonalRange"/> along a diagonal); a slash, the front tile, then its two flanks. Nothing for
    /// a mode the action lacks.
    /// </summary>
    public static IEnumerable<Pos> Covered(TileMap map, ActionProfile action, StrikeMode mode, Pos from, Pos direction)
    {
        var dir = new Pos(Math.Sign(direction.X), Math.Sign(direction.Y));
        if (dir == default || action.Modes is not { } modes || !modes.Contains(mode)) yield break;
        var diagonal = dir.X != 0 && dir.Y != 0;
        IEnumerable<Pos> tiles = mode switch
        {
            StrikeMode.Stab => Enumerable.Range(1, diagonal ? action.DiagonalRange : action.Reach).Select(i => new Pos(from.X + dir.X * i, from.Y + dir.Y * i)),
            _ => diagonal
                ? [from + dir, new Pos(from.X + dir.X, from.Y), new Pos(from.X, from.Y + dir.Y)]
                : dir.X != 0
                    ? [from + dir, new Pos(from.X + dir.X, from.Y - 1), new Pos(from.X + dir.X, from.Y + 1)]
                    : [from + dir, new Pos(from.X - 1, from.Y + dir.Y), new Pos(from.X + 1, from.Y + dir.Y)],
        };
        foreach (var p in tiles)
            if (map.InBounds(p)) yield return p;
    }

    /// <summary>Whether <paramref name="target"/> lies in the action's pattern from <paramref name="from"/>.</summary>
    private static bool InPattern(ActionProfile action, Pos from, Pos? facing, Pos target)
    {
        if (from == target) return false;
        var d = target - from;
        return action.Pattern switch
        {
            AttackPattern.Melee => from.Chebyshev(target) <= action.Reach,
            AttackPattern.Ranged => from.DistanceSquared(target) <= action.Reach * action.Reach,
            AttackPattern.Line => d.X == 0 || d.Y == 0 ? from.Chebyshev(target) <= action.Reach
                : Math.Abs(d.X) == Math.Abs(d.Y) && from.Chebyshev(target) <= action.DiagonalRange,
            _ => false,
        };
    }

    /// <summary>Line of sight, needed only past the tiles next to the attacker.</summary>
    internal static bool Clear(ScavengeData data, TileMap map, Pos from, Pos target) =>
        from.Chebyshev(target) <= 1 || map.HasLineOfSight(data, from, target);

    /// <summary>AP the attack costs from <paramref name="from"/> at <paramref name="target"/>: more up close for a scope.</summary>
    public static int ApCost(ActionProfile action, Pos from, Pos target) =>
        action.ApCost + (action.CloseApPenalty > 0 && from.Chebyshev(target) <= action.CloseRange ? action.CloseApPenalty : 0);

    /// <summary>Cover a target gets from the tile next to it on the attacker's side. Nothing at point-blank range.</summary>
    public static double CoverAgainst(ScavengeData data, TileMap map, Pos target, Pos attacker)
    {
        if (target.Chebyshev(attacker) <= 1) return 0;
        var shield = target + new Pos(Math.Sign(attacker.X - target.X), Math.Sign(attacker.Y - target.Y));
        return map.InBounds(shield) ? data.Terrain(map[shield]).Cover : 0;
    }

    /// <summary>
    /// Action accuracy minus the target's dodge and cover (plus its brace bonus), minus range falloff past the first tile,
    /// plus an ambush bonus and a bane's hit bonus, clamped to the rules' min/max. An ambush may also take the dodge away.
    /// </summary>
    public static double HitChance(ScavengeData data, TileMap map, Pos from, ActionProfile action, Pos target, UnitProfile targetProfile,
        AmbushInfo? ambush = null, BaneDef? bane = null)
    {
        var c = data.Rules.Combat;
        var chance = action.Accuracy
            - (ambush is { IgnoreDodge: true } ? Math.Min(0, targetProfile.Dodge) : targetProfile.Dodge)
            - CoverAgainst(data, map, target, from)
            - targetProfile.CoverBonus
            - Math.Max(0, Math.Sqrt(from.DistanceSquared(target)) - 1) * c.RangeFalloff
            + (ambush?.HitBonus ?? 0)
            + (bane?.HitBonus ?? 0);
        return Math.Clamp(chance, c.MinHitChance, c.MaxHitChance);
    }

    /// <summary>The hit chance of this attack as it would resolve now, ambush included (for previews and the AI).</summary>
    public static double ChanceToHit(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target, Pos? from = null)
    {
        var ambush = AmbushAgainst(sim.Data, sim.State, attacker, target, from);
        return HitChance(sim.Data, sim.State.Map, from ?? attacker.Pos,
            action with { Accuracy = action.Accuracy + MarkBonus(sim, attacker, target) + OutplayBonus(sim, attacker, target, ambush) + attacker.CardHit - WeatherPenalty(sim, action) + ElectronicWarfare.RadioAccuracy(sim, attacker) },
            target.Pos, sim.Profile(target), ambush, Bane(sim.Data, action, target));
    }

    /// <summary>What the weather takes off a ranged attack's hit chance (dust in the air; melee is unaffected).</summary>
    public static double WeatherPenalty(ScavengeSim sim, ActionProfile action) =>
        action.Pattern == AttackPattern.Melee ? 0 : FieldEnvironment.Effects(sim.Data, sim.State).RangedAccuracyPenalty;

    /// <summary>
    /// The ambush hit bonus for an attack on the unit whose telegraphed attack the attacker dodged (see
    /// <see cref="Unit.OutplayedId"/>), cut so that with an <paramref name="ambush"/>'s bonus they add up to at most
    /// rules.combat.maxHitBonus.
    /// </summary>
    public static double OutplayBonus(ScavengeSim sim, Unit attacker, Unit target, AmbushInfo? ambush = null)
    {
        if (attacker.OutplayedId != target.Id || sim.State.Round > attacker.OutplayedUntilRound) return 0;
        return Math.Min(sim.Data.Rules.Fog.Ambush.HitBonus, Math.Max(0, sim.Data.Rules.Combat.MaxHitBonus - (ambush?.HitBonus ?? 0)));
    }

    /// <summary>
    /// <paramref name="dodger"/> stepped off the tile <paramref name="attacker"/>'s telegraphed blow came down on: the dodger
    /// gets the outplay hit bonus on it and (rules.combat.outplay) a free counter, both through the next round, and the
    /// attacker is exposed as long. Without <paramref name="counter"/> (the blow dodged was itself a free counter) no
    /// counter is earned back.
    /// </summary>
    internal static void Dodged(ScavengeSim sim, Unit dodger, Unit attacker, bool counter = true)
    {
        var state = sim.State;
        dodger.OutplayedId = attacker.Id;
        dodger.OutplayedUntilRound = state.Round + 1;
        dodger.FreeCounterId = counter && sim.Data.Rules.Combat.Outplay.FreeCounter ? attacker.Id : null;
        Expose(sim, attacker, dodger.Side);
        state.Log(new(state.Round, ScavengeEventType.IntentFizzled, $"{dodger.Name} dodges {attacker.Name}'s blow: {attacker.Name} is exposed.",
            attacker.Id, dodger.Id, attacker.Pos));
    }

    /// <summary>
    /// The unit was outplayed (its blow dodged, its plan disrupted): the next hit by <paramref name="side"/> lands harder,
    /// through the next round.
    /// </summary>
    internal static void Expose(ScavengeSim sim, Unit unit, string side)
    {
        unit.ExposedUntilRound = sim.State.Round + 1;
        unit.ExposedToSide = side;
    }

    /// <summary>Whether the unit is exposed: a telegraphed blow of its was dodged (<see cref="Unit.ExposedUntilRound"/>).</summary>
    public static bool Exposed(ScavengeSim sim, Unit unit) => unit.ExposedUntilRound > 0 && sim.State.Round <= unit.ExposedUntilRound;

    /// <summary>Whether a hit by <paramref name="attacker"/> cashes the target's exposure in: it is of the side that dodged.</summary>
    public static bool ExposedTo(ScavengeSim sim, Unit target, Unit attacker) => Exposed(sim, target) && target.ExposedToSide == attacker.Side;

    /// <summary>What a drone's mark on the target adds to the attacker's side's hit chance.</summary>
    public static double MarkBonus(ScavengeSim sim, Unit attacker, Unit target) =>
        Drones.IsMarked(sim.State, target.Id, attacker.Side) ? sim.Data.Rules.Drone.MarkHitBonus : 0;

    /// <summary>
    /// Whether an attack on <paramref name="target"/> would come from the fog: its side does not see the attacker (at
    /// <paramref name="from"/>, default where it stands). Null when it would not. A braced target gets no bonus against
    /// it; an ally next to the target ("watching each other's backs") takes off <see cref="AmbushRules.BacksReduction"/>.
    /// </summary>
    public static AmbushInfo? AmbushAgainst(ScavengeData data, ScavengeState state, Unit attacker, Unit target, Pos? from = null)
    {
        if (!attacker.HostileTo(target)) return null;
        bool Hidden()
        {
            if (from is not { } at || at == attacker.Pos) return !Detection.SideSees(data, state, target.Side, attacker);
            var moved = attacker.Pos;
            attacker.Pos = at;
            try { return !Detection.SideSees(data, state, target.Side, attacker); }
            finally { attacker.Pos = moved; }
        }
        if (!Hidden()) return null;

        var rules = data.Rules.Fog.Ambush;
        if (target.Stance == Stance.Brace) return new AmbushInfo(0, 1, false, Braced: true, Backs: false);
        var backs = state.Units.Any(u => u.Id != target.Id && u.IsActive && u.Side == target.Side && u.Pos.Chebyshev(target.Pos) <= 1);
        var keep = backs ? 1 - rules.BacksReduction : 1;
        return new AmbushInfo(rules.HitBonus * keep, 1 + (rules.DamageMultiplier - 1) * keep, rules.IgnoreDodge, Braced: false, Backs: backs);
    }

    /// <summary>
    /// Pays the AP (not for an overwatch reaction, which instead loses accuracy), makes noise, and strikes the target; an
    /// area action then strikes every other unit hostile to the attacker within <see cref="ActionProfile.Area"/> of the
    /// target (by id, each with its own roll). Whether each strike comes from the fog is judged before anything resolves.
    /// Afterwards the attacker is revealed to every side it hit at for <see cref="ActionProfile.Reveal"/> rounds, those
    /// sides get an attack contact pointing at it, every other side in earshot a noise contact, and overwatch may react.
    /// </summary>
    /// <param name="free">No AP paid, at full accuracy (a card's strike).</param>
    internal static void Resolve(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target, bool reaction = false, bool free = false)
    {
        if (!reaction && !free) attacker.Ap -= ApCost(action, attacker.Pos, target.Pos);
        attacker.Facing = Face(attacker.Pos, target.Pos);
        var targets = new List<Unit> { target };
        targets.AddRange(Splash(sim, attacker, action, target.Pos, targets));
        Hit(sim, attacker, action, targets, reaction);
    }

    /// <summary>The other hostiles of the attacker an area action catches within <see cref="ActionProfile.Area"/> of <paramref name="center"/>.</summary>
    internal static IEnumerable<Unit> Splash(ScavengeSim sim, Unit attacker, ActionProfile action, Pos center, IReadOnlyCollection<Unit> struck) =>
        action.Area <= 0 ? [] : sim.State.Units
            .Where(u => struck.All(s => s.Id != u.Id) && u.IsActive && attacker.HostileTo(u) && u.Pos.Chebyshev(center) <= action.Area)
            .OrderBy(u => u.Id).ToList();

    /// <summary>
    /// Makes noise and strikes every one of <paramref name="targets"/> with its own roll (the AP already paid, the attacker
    /// already facing), then gives the attacker away as <see cref="Resolve"/> says.
    /// </summary>
    internal static void Hit(ScavengeSim sim, Unit attacker, ActionProfile action, List<Unit> targets, bool reaction = false)
    {
        sim.MakeNoise(attacker, action.Noise);
        var ambushes = targets.Select(t => AmbushAgainst(sim.Data, sim.State, attacker, t)).ToList();
        foreach (var t in targets) Bestiary.Fought(sim, attacker, t);
        var penalty = reaction ? sim.Data.Rules.Fog.Overwatch.AccuracyPenalty : 0;
        for (var i = 0; i < targets.Count; i++)
            Strike(sim, attacker, action with { Accuracy = action.Accuracy - penalty }, targets[i], ambushes[i], reaction);
        AfterAttack(sim, attacker, action, targets);
        GearEffects.Feedback(sim, attacker, action);
    }

    /// <summary>The direction from one tile to another as a sign per axis.</summary>
    public static Pos Face(Pos from, Pos to) => new(Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));

    /// <summary>
    /// Where a blow from <paramref name="attacker"/> comes at a target on <paramref name="target"/> facing
    /// <paramref name="facing"/>, by the attacker's true bearing against the facing: within 67.5° of straight ahead the
    /// front (the step ahead and its two diagonals, up close), to 112.5° the side, further the rear. A target facing
    /// nowhere is struck from the front.
    /// </summary>
    public static FacingArc ArcOf(Pos target, Pos? facing, Pos attacker)
    {
        if (facing is not { } f || f == default || attacker == target) return FacingArc.Front;
        var off = Math.Abs(Math.Atan2(attacker.Y - target.Y, attacker.X - target.X) - Math.Atan2(Math.Sign(f.Y), Math.Sign(f.X))) * 180 / Math.PI;
        off = Math.Min(off, 360 - off);
        return off <= 67.5 + 1e-9 ? FacingArc.Front : off <= 112.5 + 1e-9 ? FacingArc.Side : FacingArc.Rear;
    }

    /// <summary>The arc a blow from <paramref name="from"/> comes at <paramref name="target"/> (a structure has no back: the front).</summary>
    public static FacingArc ArcOf(Unit target, Pos from) => target.Structure ? FacingArc.Front : ArcOf(target.Pos, target.Facing, from);

    /// <summary>What a blow from that arc does to the damage (rules.combat.facing).</summary>
    public static double ArcDamage(FacingRules rules, FacingArc arc) => arc switch
    {
        FacingArc.Side => rules.SideDamage,
        FacingArc.Rear => rules.RearDamage,
        _ => 1,
    };

    /// <summary>
    /// Suppressing fire: no damage roll, but the target is pinned and loses AP next turn. Needs a ranged action that
    /// reaches the target. Gives the attacker away like any shot.
    /// </summary>
    internal static void Suppress(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target)
    {
        attacker.Ap -= action.ApCost;
        sim.MakeNoise(attacker, action.Noise);
        target.Suppressed = target.CalmUntilRound < sim.State.Round;
        Bestiary.Fought(sim, attacker, target);
        sim.State.Log(new(sim.State.Round, ScavengeEventType.Suppressed,
            $"{attacker.Name} pins {target.Name} down with suppressing fire ({action.Name}).", attacker.Id, target.Id, target.Pos,
            Anonymous: $"{target.Name} is pinned down by fire from the fog."));
        AfterAttack(sim, attacker, action, [target]);
    }

    private static void AfterAttack(ScavengeSim sim, Unit attacker, ActionProfile action, List<Unit> targets)
    {
        var state = sim.State;
        var data = sim.Data;
        var struck = new HashSet<string>();
        foreach (var t in targets.Where(t => t.Side != attacker.Side))
        {
            if (struck.Add(t.Side) && !Detection.SideSees(data, state, t.Side, attacker))
                Detection.AddContact(data, state, t.Side, attacker.Pos, ContactKind.Attack, attacker.Id, t.Pos);
            if (action.Reveal > 0) Detection.Reveal(state, attacker, t.Side, state.Round + action.Reveal);
        }
        foreach (var side in Detection.PartySides(state).Where(s => s != attacker.Side && !struck.Contains(s)))
            if (state.Units.Any(u => u.Side == side && u.IsActive && u.Pos.Chebyshev(attacker.Pos) <= action.Noise)
                && !Detection.SideSees(data, state, side, attacker))
                Detection.AddContact(data, state, side, attacker.Pos, ContactKind.Noise, attacker.Id);
        sim.CheckOverwatch(attacker);
    }

    private static void Strike(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target, AmbushInfo? ambush, bool reaction = false)
    {
        var data = sim.Data;
        var state = sim.State;
        var bane = Bane(data, action, target);
        // A card's hit bonus is for the unit's own next attack (not a reaction shot): the first strike uses it up.
        var cardHit = reaction ? 0 : attacker.CardHit;
        var chance = HitChance(data, state.Map, attacker.Pos, action with { Accuracy = action.Accuracy + MarkBonus(sim, attacker, target) + OutplayBonus(sim, attacker, target, ambush) + cardHit + ElectronicWarfare.RadioAccuracy(sim, attacker) },
            target.Pos, sim.Profile(target), ambush, bane);
        if (!reaction) attacker.CardHit = 0;
        // A dodge pays off once: whichever attack on that unit comes first (planned, reaction or splash) uses it up.
        if (attacker.OutplayedId == target.Id) attacker.OutplayedId = null;
        if (ambush is { } a)
            state.Log(new(state.Round, ScavengeEventType.Ambushed,
                $"{attacker.Name} ambushes {target.Name} from the fog{a.Describe()}.", attacker.Id, target.Id, target.Pos,
                Anonymous: $"{target.Name} was ambushed from the fog{a.Describe()}."));

        var roll = sim.Rng.NextDouble();
        if (roll >= chance)
        {
            state.Log(new(state.Round, ScavengeEventType.Missed,
                // The log is shared and cannot know a colony's bestiary: an NPC enemy's chance shows to the nearest 5%.
                $"{attacker.Name} missed {target.Name} ({action.Name}, {(Bestiary.Subject(target) ? Bestiary.ShownChance(data.Rules.Bestiary, chance, BestiaryTier.Unknown) : chance):P0}).", attacker.Id, target.Id, target.Pos,
                Anonymous: $"An attack from the fog missed {target.Name}."));
            return;
        }

        var arc = ArcOf(target, attacker.Pos);
        var exposed = ExposedTo(sim, target, attacker);
        if (exposed) (target.ExposedUntilRound, target.ExposedToSide) = (0, null);
        var stacked = Math.Min(data.Rules.Combat.MaxDamageMultiplier,
            (ambush?.DamageMultiplier ?? 1) * ArcDamage(data.Rules.Combat.Facing, arc) * (exposed ? data.Rules.Combat.Outplay.ExposedDamage : 1));
        var raw = (action.MinDamage + (action.MaxDamage - action.MinDamage) * sim.Rng.NextDouble()) * stacked;
        var part = PickHitLocation(sim, target);
        var defense = sim.Profile(target);
        var armor = Math.Max(0, defense.ArmorAgainst(Body.RegionOf(part), action.Pattern == AttackPattern.Melee) - action.ArmorPierce);
        if (bane is not null)
        {
            raw *= bane.DamageMultiplier;
            armor = Math.Max(0, armor - bane.ArmorPierce);
        }
        var tiers = data.Rules.Combat.Tiers;
        var tier = TierOf(tiers, chance, roll, armor, CoverAgainst(data, state.Map, target.Pos, attacker.Pos) + defense.CoverBonus, target.Guard);
        target.Guard = 0;
        var damage = Math.Max(1, raw - armor);
        if (tier == HitTier.Graze) damage = Math.Max(1, damage * tiers.GrazeDamage);
        var where = (target.Structure ? target.Name : $"{target.Name}'s {part}")
                    + arc switch { FacingArc.Side => " from the side", FacingArc.Rear => " from behind", _ => "" }
                    + (exposed ? " (exposed)" : "");
        var (verb, how) = tier switch
        {
            HitTier.Graze => ("grazed", "grazed"),
            HitTier.Solid => ("landed a solid hit on", "hit hard"),
            _ => ("hit", "hit"),
        };
        state.Log(new(state.Round, ScavengeEventType.Attacked,
            $"{attacker.Name} {verb} {where} for {damage:0} ({action.Name}).", attacker.Id, target.Id, target.Pos,
            Anonymous: target.Structure ? $"{target.Name} was {how} for {damage:0} from the fog." : $"{target.Name} was {how} in the {part} for {damage:0} from the fog.",
            Tier: tier, Amount: damage));
        var severity = action.InjuryMultiplier * (1 - Math.Clamp(defense.InjuryResist, 0, 1)) * (tier == HitTier.Solid ? tiers.SolidSeverity : 1);
        ApplyDamage(sim, target, part, damage, severity, attacker, injure: tier != HitTier.Graze);
        if (bane is { Stun: true } && target.IsActive && !target.Suppressed)
        {
            target.Suppressed = true;
            state.Log(new(state.Round, ScavengeEventType.Suppressed, $"{target.Name} is stunned ({action.Name}).", attacker.Id, target.Id, target.Pos,
                Anonymous: $"{target.Name} is stunned."));
        }
        if (bane is { DisruptRounds: > 0 } && target.IsActive && Network.IsLinked(data, target))
        {
            target.OffRelayUntilRound = Math.Max(target.OffRelayUntilRound, state.Round + bane.DisruptRounds - 1);
            state.Log(new(state.Round, ScavengeEventType.RelayDisrupted, $"{target.Name} is knocked off the relay.", attacker.Id, target.Id, target.Pos,
                Anonymous: $"{target.Name} is knocked off the relay."));
        }
    }

    /// <summary>
    /// The tier of a landed hit (<paramref name="roll"/> under <paramref name="chance"/>) by its margin, shifted down a tier
    /// each by enough <paramref name="armor"/> on the hit region and enough <paramref name="cover"/>, and by
    /// <paramref name="guard"/> tiers more (a defense card), never below a graze.
    /// </summary>
    public static HitTier TierOf(HitTierRules rules, double chance, double roll, double armor, double cover, int guard = 0)
    {
        var margin = chance > 0 ? (chance - roll) / chance : 0;
        var tier = margin < rules.GrazeBelow ? HitTier.Graze : margin >= rules.SolidFrom ? HitTier.Solid : HitTier.Hit;
        var shift = (armor + 1e-9 >= rules.ArmorShiftFrom ? 1 : 0) + (cover + 1e-9 >= rules.CoverShiftFrom ? 1 : 0) + Math.Max(0, guard);
        return (HitTier)Math.Max((int)HitTier.Graze, (int)tier - shift);
    }

    /// <summary>
    /// The action's banes against an enemy carrying their tags, merged into one (hit bonuses and armor pierce add, damage
    /// bonuses add up, the longest relay knock-off, any stun), else null.
    /// </summary>
    public static BaneDef? Bane(ScavengeData data, ActionProfile action, Unit target)
    {
        if (target.EnemyDefId is not { } id) return null;
        var tags = data.Enemies[id].Tags;
        var matching = (action.Bane is { } b ? [b] : Enumerable.Empty<BaneDef>()).Concat(action.ExtraBanes ?? []).Where(x => tags.Contains(x.Tag)).ToList();
        if (matching.Count <= 1) return matching.FirstOrDefault();
        return new BaneDef
        {
            Tag = matching[0].Tag,
            HitBonus = matching.Sum(x => x.HitBonus),
            DamageMultiplier = 1 + matching.Sum(x => x.DamageMultiplier - 1),
            ArmorPierce = matching.Sum(x => x.ArmorPierce),
            DisruptRounds = matching.Max(x => x.DisruptRounds),
            Stun = matching.Any(x => x.Stun),
        };
    }

    /// <summary>Weighted by rules.combat.hitLocations; a hit on a missing limb lands on the torso.</summary>
    private static BodyPart PickHitLocation(ScavengeSim sim, Unit target)
    {
        var weights = sim.Data.Rules.Combat.HitLocations.OrderBy(kv => kv.Key).ToList();
        var roll = sim.Rng.NextDouble() * weights.Sum(kv => kv.Value);
        var part = weights[^1].Key;
        foreach (var (p, w) in weights)
        {
            if (roll < w) { part = p; break; }
            roll -= w;
        }
        return target.Body[part].Condition == PartCondition.Missing ? BodyPart.Torso : part;
    }

    /// <summary>
    /// Damage to one body part: injury severity, possible limb loss, then HP and Downed/Dead (by <paramref name="attacker"/>,
    /// for loot perks). Without <paramref name="injure"/> (a graze) only the HP drops.
    /// </summary>
    public static void ApplyDamage(ScavengeSim sim, Unit target, BodyPart part, double damage, double severityScale = 1, Unit? attacker = null,
        bool injure = true)
    {
        var data = sim.Data;
        var state = sim.State;
        var c = data.Rules.Combat;

        var bodyPart = target.Body[part];
        if (injure && !target.Structure && bodyPart.Condition is PartCondition.Healthy or PartCondition.Injured)
        {
            var severity = bodyPart.Severity + damage / c.PartToughness[Body.RegionOf(part)] * severityScale;
            target.Body.Injure(part, severity);
            state.Log(new(state.Round, ScavengeEventType.Injured,
                $"{target.Name}'s {part} is injured ({target.Body[part].Severity:P0}).", target.Id, Pos: target.Pos));
            var limb = Body.RegionOf(part) is BodyRegion.Arms or BodyRegion.Legs;
            if (limb && severity >= 1 && sim.Rng.NextDouble() < c.LimbLossChance)
            {
                target.Body.Lose(part);
                state.Log(new(state.Round, ScavengeEventType.LimbLost, $"{target.Name} lost their {part}.", target.Id, Pos: target.Pos));
            }
            else if (target.Kind == UnitKind.Survivor
                     && Treatment.MaybeFracture(data.Medicine, target.Body, part, SideRoll.Next(sim.Rng, state.Round, target.Id, (long)part, (long)(damage * 1000))))
                state.Log(new(state.Round, ScavengeEventType.Fractured, $"{target.Name}'s {part} is broken.", target.Id, Pos: target.Pos));
        }

        var wasDowned = target.Status == UnitStatus.Downed;
        target.Hp -= damage;
        if (target.Hp > 0) return;

        if (wasDowned || !target.CanBeDowned || target.Hp <= c.InstantDeathHp)
        {
            sim.Kill(target, ScavengeEventType.Died, target.Structure ? $"{target.Name} is destroyed." : $"{target.Name} is dead.", attacker);
            return;
        }
        if (state.Mission?.Def.RescueDowned == true && target.Kind == UnitKind.Survivor && target.PlayerId is not null)
        {
            target.Hp = 1;
            target.Status = UnitStatus.Evacuated;
            target.CarriedById = null;
            target.Plan = null;
            state.Log(new(state.Round, ScavengeEventType.Downed, $"{target.Name} is down and dragged to safety.", target.Id, Pos: target.Pos));
            sim.UpdateParties();
            return;
        }
        target.Status = UnitStatus.Downed;
        PlannedActions.Cancel(sim, target, "down");
        target.BleedOut = c.BleedOutRounds;
        target.Stabilized = false;
        target.Ap = 0;
        state.Log(new(state.Round, ScavengeEventType.Downed,
            $"{target.Name} is down and bleeding out ({c.BleedOutRounds} rounds).", target.Id, Pos: target.Pos));
    }
}

/// <summary>Where a blow comes at its target from, against where the target faces (<see cref="Combat.ArcOf(Pos, Pos?, Pos)"/>).</summary>
public enum FacingArc { Front, Side, Rear }

/// <summary>How a landed hit resolves: a graze (a little HP, no injury), a plain hit, or a solid one (worse injury).</summary>
public enum HitTier { Graze, Hit, Solid }

/// <summary>
/// An attack from the fog: what it adds to the hit chance and damage, and whether the target loses its dodge.
/// <see cref="Braced"/>: the target was braced, so the attack is unseen but gains nothing. <see cref="Backs"/>: an ally
/// next to the target cut the bonuses.
/// </summary>
public sealed record AmbushInfo(double HitBonus, double DamageMultiplier, bool IgnoreDodge, bool Braced, bool Backs)
{
    /// <summary>" (+20% hit)", " (braced)", " (+10% hit, backs covered)" for the log.</summary>
    public string Describe() =>
        Braced ? " (braced: no bonus)" : $" (+{HitBonus * 100:0}% hit{(Backs ? ", backs covered" : "")})";
}
