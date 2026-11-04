using Remnants.Core.Colony;

namespace Remnants.Core.Scavenge;

/// <summary>How often the colony's parties saw, fought and killed an enemy type (each enemy counted once per run).</summary>
public sealed class BestiaryEntry
{
    public int Seen { get; set; }
    public int Fought { get; set; }
    public int Killed { get; set; }
}

/// <summary>What the colony knows of an enemy type: nothing, its basics, how it moves and acts, or everything.</summary>
public enum BestiaryTier { Unknown, Seen, Fought, Killed }

/// <summary>
/// Encounters each tier needs (scavenge.json <c>bestiary</c>): sightings, fights, kills; and <see cref="ChanceStep"/>, what
/// hit chances against a type not yet killed round to.
/// </summary>
public sealed record BestiaryRules
{
    public int Seen { get; init; } = 1;
    public int Fought { get; init; } = 2;
    public int Killed { get; init; } = 1;
    public double ChanceStep { get; init; } = 0.05;
}

/// <summary>
/// An enemy type as the bestiary shows it at a tier: the name always; <see cref="Weapons"/> and <see cref="Reach"/> once
/// seen; <see cref="Ap"/> and <see cref="Movement"/> once fought; HP, armor, dodge, attributes, skills and
/// <see cref="Behaviour"/> once killed. What the tier does not reveal is null.
/// </summary>
public sealed record BestiaryCard(
    string Id, string Name, BestiaryTier Tier,
    List<string>? Weapons = null, int? Reach = null,
    int? Ap = null, MoveMode? Movement = null,
    double? Hp = null, double? Armor = null, double? Dodge = null,
    Dictionary<Survivors.AttributeKind, double>? Attributes = null, Dictionary<Survivors.Skill, double>? Skills = null,
    string? Behaviour = null);

/// <summary>
/// The bestiary (NPC enemies only; other players are never in it). In a run each party counts the enemies it sees
/// (<see cref="Observe"/>, after every command), fights (attacks either way) and kills, each enemy once, by enemy type in
/// <see cref="Party.Bestiary"/>; what a party learned comes home with it (only if someone gets out) and adds to the
/// colony's (<see cref="ColonyState.Bestiary"/>). The tier (<see cref="TierOf"/>) gates the stats shown
/// (<see cref="Describe"/>); anyone in sight shows its position, facing, readied strike and weapon reach regardless.
/// </summary>
public static class Bestiary
{
    public static BestiaryTier TierOf(BestiaryRules rules, BestiaryEntry? entry) =>
        entry is null ? BestiaryTier.Unknown
        : entry.Killed >= rules.Killed ? BestiaryTier.Killed
        : entry.Fought >= rules.Fought ? BestiaryTier.Fought
        : entry.Seen >= rules.Seen ? BestiaryTier.Seen
        : BestiaryTier.Unknown;

    /// <summary>The tier the colony plus a run in progress know an enemy type at (the run's counts added in).</summary>
    public static BestiaryTier TierOf(BestiaryRules rules, IReadOnlyDictionary<string, BestiaryEntry> colony, IReadOnlyDictionary<string, BestiaryEntry>? run, string enemyId)
    {
        var c = colony.GetValueOrDefault(enemyId);
        var r = run?.GetValueOrDefault(enemyId);
        if (r is null) return TierOf(rules, c);
        return TierOf(rules, new BestiaryEntry { Seen = (c?.Seen ?? 0) + r.Seen, Fought = (c?.Fought ?? 0) + r.Fought, Killed = (c?.Killed ?? 0) + r.Killed });
    }

    /// <summary>
    /// What <paramref name="playerId"/>'s side knows of <paramref name="unit"/>: the colony's bestiary plus what its party
    /// learned this run. Anything that is not a bestiary subject (players, survivors, recruits) is known in full.
    /// </summary>
    public static BestiaryTier Knowledge(ScavengeSim sim, IReadOnlyDictionary<string, BestiaryEntry> colony, int playerId, Unit unit) =>
        Subject(unit) && unit.EnemyDefId is { } id
            ? TierOf(sim.Data.Rules.Bestiary, colony, sim.State.FindParty(playerId)?.Bestiary, id)
            : BestiaryTier.Killed;

    /// <summary>A unit's health as the side may see it: the numbers once the type is killed, otherwise how hurt it looks.</summary>
    public static string Health(Unit unit, BestiaryTier tier) =>
        tier >= BestiaryTier.Killed ? $"HP {unit.Hp:0}/{unit.MaxHp:0}" : $"HP {(unit.MaxHp > 0 ? unit.Hp / unit.MaxHp : 0) * 100:0}%";

    /// <summary>
    /// A hit chance as the side may see it: exact once the target's type is killed (and for players), otherwise to the
    /// nearest <see cref="BestiaryRules.ChanceStep"/> (half steps up), never a sure or hopeless one, so the hidden dodge
    /// and bracing cannot be read off it (user decision).
    /// </summary>
    public static double ShownChance(BestiaryRules rules, double chance, BestiaryTier tier)
    {
        var step = rules.ChanceStep;
        if (tier >= BestiaryTier.Killed || step <= 0) return chance;
        // The nudge keeps a half step that binary stores a hair low (0.425) rounding up.
        var buckets = Math.Round(chance / step + 1e-9, MidpointRounding.AwayFromZero);
        return Math.Clamp(buckets * step, step, 1 - step);
    }

    /// <summary>Whether a unit is a bestiary subject: an NPC enemy or a machine of the Network with an enemy definition.</summary>
    public static bool Subject(Unit u) => u.EnemyDefId is not null && u.PlayerId is null && u.Kind is UnitKind.Enemy or UnitKind.Device;

    /// <summary>Every party still in the run counts the subjects its side sees now that it had not seen yet.</summary>
    public static void Observe(ScavengeSim sim)
    {
        var state = sim.State;
        foreach (var party in state.Parties.Where(p => !p.Done))
            foreach (var id in Detection.SeenBy(sim.Data, state, party.Side).OrderBy(i => i))
                if (state.FindUnit(id) is { } u && Subject(u) && party.SeenIds.Add(id))
                    Entry(party, u).Seen++;
    }

    /// <summary>An attack between a party's unit and a subject, either way: that party has fought it.</summary>
    internal static void Fought(ScavengeSim sim, Unit a, Unit b)
    {
        foreach (var (mine, them) in new[] { (a, b), (b, a) })
            if (mine.PlayerId is { } pid && Subject(them) && sim.State.FindParty(pid) is { } party && party.FoughtIds.Add(them.Id))
                Entry(party, them).Fought++;
    }

    /// <summary>A party's unit killed a subject.</summary>
    internal static void Killed(ScavengeSim sim, Unit unit, Unit? killer)
    {
        if (killer?.PlayerId is { } pid && Subject(unit) && sim.State.FindParty(pid) is { } party && party.KilledIds.Add(unit.Id))
            Entry(party, unit).Killed++;
    }

    private static BestiaryEntry Entry(Party party, Unit u)
    {
        if (!party.Bestiary.TryGetValue(u.EnemyDefId!, out var e)) party.Bestiary[u.EnemyDefId!] = e = new BestiaryEntry();
        return e;
    }

    /// <summary>What a run taught (an extraction result's counts) into the colony's bestiary.</summary>
    public static void Merge(ColonyState colony, IReadOnlyDictionary<string, BestiaryEntry> learned)
    {
        foreach (var (id, add) in learned)
        {
            if (!colony.Bestiary.TryGetValue(id, out var e)) colony.Bestiary[id] = e = new BestiaryEntry();
            e.Seen += add.Seen;
            e.Fought += add.Fought;
            e.Killed += add.Killed;
        }
    }

    /// <summary>
    /// Every enemy type as the colony knows it, what it knows best first (then by name); a type never seen shows as "???".
    /// </summary>
    public static List<BestiaryCard> List(ScavengeData data, IReadOnlyDictionary<string, BestiaryEntry> known)
    {
        var rules = data.Rules.Bestiary;
        return data.Enemies.Values
            .Select(e => TierOf(rules, known.GetValueOrDefault(e.Id)) is var tier && tier == BestiaryTier.Unknown
                ? new BestiaryCard(e.Id, "???", tier)
                : Describe(data, e.Id, tier))
            .OrderByDescending(c => c.Tier).ThenBy(c => c.Tier == BestiaryTier.Unknown ? c.Id : c.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>An enemy type as the bestiary shows it at <paramref name="tier"/>.</summary>
    public static BestiaryCard Describe(ScavengeData data, string enemyId, BestiaryTier tier)
    {
        var def = data.Enemies[enemyId];
        var card = new BestiaryCard(def.Id, def.Name, tier);
        if (tier < BestiaryTier.Seen) return card;
        var unit = new Unit
        {
            Name = def.Name, Kind = UnitKind.Enemy, Side = Unit.EnemySide, EnemyDefId = def.Id, Hp = def.Hp, MaxHp = def.Hp,
            Attributes = Enum.GetValues<Survivors.AttributeKind>().ToDictionary(a => a, a => def.Attributes.GetValueOrDefault(a, 50)),
            Skills = new(def.Skills),
            Equipment = def.Equipment.Where(data.Items.ContainsKey).ToDictionary(id => data.Items[id].Slot!.Value, id => id),
        };
        var profile = ProfileDeriver.Derive(data, unit);
        card = card with
        {
            Weapons = profile.Actions.Select(a => a.Name).Distinct().ToList(),
            Reach = profile.Actions.Select(a => a.Reach).DefaultIfEmpty(0).Max(),
        };
        if (tier < BestiaryTier.Fought) return card;
        var walk = profile.Movement.Primary;
        card = card with { Ap = profile.MaxAp, Movement = walk };
        if (tier < BestiaryTier.Killed) return card;
        return card with
        {
            Hp = def.Hp, Armor = profile.Armor, Dodge = profile.Dodge, Attributes = new(unit.Attributes), Skills = new(def.Skills),
            Behaviour = def.Ai switch
            {
                AiKind.Linked => "Hunts in squads over the Network: all of them know what one sees, they pick the weakest, flank, " +
                                 "pin down from cover, fall back when hurt and wait unseen on overwatch.",
                AiKind.Device => "A machine: it never moves and telegraphs where it will fire.",
                _ => "Wanders until it sees or hears prey, then closes in and strikes; it telegraphs the charge.",
            },
        };
    }
}
