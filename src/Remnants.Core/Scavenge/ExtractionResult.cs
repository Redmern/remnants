using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>A survivor who made it out, as they are now (injuries, health) and what they bring.</summary>
/// <see cref="FieldXp"/>: XP earned out there beyond skill gains (hacks, drone flights), paid out at home.
public sealed record ReturnedSurvivor(Survivor Survivor, bool Evacuated, Dictionary<EquipSlot, string> Equipment, List<string> Pack, double FieldXp = 0);

/// <summary>
/// What a party's run feeds back into the colony (M5): who came home and in what shape, recruits, the dead,
/// and the carried loot. Final once <see cref="Complete"/> is true.
/// </summary>
public sealed record ExtractionResult(
    int PlayerId,
    bool Complete,
    List<ReturnedSurvivor> Returned,
    List<ReturnedSurvivor> Recruits,
    List<Survivor> Deaths,
    SortedDictionary<string, int> Loot,
    ObjectiveResult? Mission = null)
{
    /// <summary>
    /// Co-op: schematics allied players carried out; every ally who got home receives a copy (knowledge copies).
    /// Not the player's own (those are in the packs).
    /// </summary>
    public List<string> SharedSchematics { get; init; } = new();

    /// <summary>Schematics the side saw but did not bring home (left in a cache, on a terminal it failed to hack): id → where.</summary>
    public SortedDictionary<string, string> HeardOf { get; init; } = new(StringComparer.Ordinal);

    internal static ExtractionResult Build(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        var party = state.FindParty(playerId) ?? throw new ArgumentException($"No party for player {playerId}.");
        var mine = state.Units.Where(u => u.PlayerId == playerId).ToList();
        bool Out(Unit u) => u.Status is UnitStatus.Extracted or UnitStatus.Evacuated;

        var returned = mine.Where(u => u.Kind == UnitKind.Survivor && Out(u)).Select(ToReturned).ToList();
        // A story character who walks out with the party (an escort) goes home to the story, not into the colony.
        var recruits = mine.Where(u => u.Kind == UnitKind.Npc && u.CharacterId is null && Out(u)).Select(ToReturned).ToList();
        var deaths = mine.Where(u => u.Kind == UnitKind.Survivor && u.Status == UnitStatus.Dead).Select(u => Snapshot(u, 0)).ToList();

        var loot = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in returned.Concat(recruits).SelectMany(r => r.Pack))
            loot[id] = loot.GetValueOrDefault(id) + 1;

        var shared = new List<string>();
        var heard = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (returned.Count > 0)
        {
            // Allies on the same side when they got out share what they carried (versus: no copies).
            foreach (var u in state.Units.Where(u => u.PlayerId != playerId && u.PlayerId > 0 && Out(u) && u.Side == party.Side))
                shared.AddRange(u.AllItems.Where(id => sim.Data.Items.GetValueOrDefault(id)?.Schematic is not null));
            var zone = sim.Data.Zones.GetValueOrDefault(state.ZoneId)?.Name ?? state.ZoneId;
            var explored = state.Explored.GetValueOrDefault(party.Side);
            foreach (var cache in state.Caches.Where(c => explored?.Contains(state.Map.Index(c.Pos)) == true))
                foreach (var id in cache.Items.Where(id => sim.Data.Items.GetValueOrDefault(id)?.Schematic is not null))
                    heard.TryAdd(id, $"left behind in {zone}");
            foreach (var id in state.HeardSchematics.GetValueOrDefault(party.Side) ?? [])
                heard.TryAdd(id, $"on a terminal in {zone}");
        }
        return new ExtractionResult(playerId, party.Done, returned, recruits, deaths, loot, MissionRules.Result(state, playerId)) { SharedSchematics = shared, HeardOf = heard };
    }

    private static ReturnedSurvivor ToReturned(Unit u) =>
        new(Snapshot(u, Math.Clamp(u.Hp, 1, Survivor.MaxHealth)), u.Status == UnitStatus.Evacuated, new(u.Equipment), new(u.Pack), u.FieldXp);

    /// <summary>The survivor as they entered, with the run's injuries, health and any attribute/skill gains applied.</summary>
    private static Survivor Snapshot(Unit u, double health)
    {
        var s = NewScavenge.Clone(u.Origin!);
        s.Body = NewScavenge.Clone(u.Body);
        foreach (var part in s.Body.Parts.Values) part.Interfered = false;
        foreach (var (attribute, value) in u.Attributes) s.Set(attribute, value);
        foreach (var (skill, value) in u.Skills) s.Set(skill, value);
        s.Health = health;
        return s;
    }
}
