using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>A survivor who made it out, as they are now (injuries, health) and what they bring.</summary>
/// <see cref="FieldXp"/>: XP earned out there beyond skill gains (hacks, drone flights), paid out at home.
/// <see cref="ChargesUsed"/>: charges spent per device item id (they come home drained and go on the charging bench).
public sealed record ReturnedSurvivor(Survivor Survivor, bool Evacuated, Dictionary<EquipSlot, string> Equipment, List<string> Pack, double FieldXp = 0,
    Dictionary<string, int>? ChargesUsed = null);

/// <summary>
/// What a party's run feeds back into the colony (M5): who came home and in what shape, recruits, the dead,
/// and the carried loot. Final once <see cref="Complete"/> is true. <see cref="Secured"/>: items a cargo drone ferried out
/// (home whatever happens to the party; in <see cref="Loot"/> too).
/// </summary>
public sealed record ExtractionResult(
    int PlayerId,
    bool Complete,
    List<ReturnedSurvivor> Returned,
    List<ReturnedSurvivor> Recruits,
    List<Survivor> Deaths,
    SortedDictionary<string, int> Loot,
    ObjectiveResult? Mission = null,
    List<string>? Secured = null)
{
    /// <summary>The party's parked vehicle at the end (null when it came on foot). Its stash is in <see cref="Loot"/> unless it was lost.</summary>
    public VehicleResult? Vehicle { get; init; }
    /// <summary>
    /// Co-op: schematics allied players carried out; every ally who got home receives a copy (knowledge copies).
    /// Not the player's own (those are in the packs).
    /// </summary>
    public List<string> SharedSchematics { get; init; } = new();

    /// <summary>What the player left in their stash at the exit: it waits there for the next run to this zone.</summary>
    public List<string> Stash { get; init; } = new();

    /// <summary>Cards the party found in the run (its pouch): only when someone got out; they join the collection.</summary>
    public List<string> Cards { get; init; } = new();

    /// <summary>What the party learned of enemy types (only when someone got out): it adds to the colony's bestiary.</summary>
    public SortedDictionary<string, BestiaryEntry> Bestiary { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Consumable cards the party played: gone from the collection whatever became of the party.</summary>
    public List<string> ConsumedCards { get; init; } = new();

    /// <summary>Schematics the side saw but did not bring home (left in a cache, on a terminal it failed to hack): id → where.</summary>
    public SortedDictionary<string, string> HeardOf { get; init; } = new(StringComparer.Ordinal);

    internal static ExtractionResult Build(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        var party = state.FindParty(playerId) ?? throw new ArgumentException($"No party for player {playerId}.");
        var mine = state.Units.Where(u => u.PlayerId == playerId).ToList();
        bool Out(Unit u) => u.Status is UnitStatus.Extracted or UnitStatus.Evacuated;

        var returned = mine.Where(u => u.Kind == UnitKind.Survivor && Out(u)).Select(ToReturned).ToList();
        // A story character who walks out with the party (an escort) goes home to the story, not into the colony; nor does
        // anything the map marks as no recruit (M6's trucks drive on to Marrow's camp).
        var recruits = mine.Where(u => u.Kind == UnitKind.Npc && u.CharacterId is null && !u.NoRecruit && Out(u)).Select(ToReturned).ToList();
        var deaths = mine.Where(u => u.Kind == UnitKind.Survivor && u.Status == UnitStatus.Dead).Select(u => Snapshot(u, 0)).ToList();

        var loot = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in returned.Concat(recruits).SelectMany(r => r.Pack).Concat(party.Secured))
            loot[id] = loot.GetValueOrDefault(id) + 1;
        // The vehicle drives home with its stash when someone made it out to drive it.
        var vehicle = ParkedVehicles.Result(state, playerId);
        if (vehicle is { Wrecked: false } && returned.Count > 0)
            foreach (var id in vehicle.Stash) loot[id] = loot.GetValueOrDefault(id) + 1;

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
        return new ExtractionResult(playerId, party.Done, returned, recruits, deaths, loot, MissionRules.Result(state, playerId),
            party.Secured.Count > 0 ? new(party.Secured) : null)
        {
            SharedSchematics = shared, HeardOf = heard, Vehicle = vehicle, Stash = FieldLoot.StashOf(state, playerId),
            Cards = returned.Count > 0 ? new(party.CardPouch) : [],
            Bestiary = returned.Count > 0 ? new(party.Bestiary, StringComparer.Ordinal) : new(StringComparer.Ordinal),
            ConsumedCards = SquadDecks.Of(state, playerId) is { } deck ? new(deck.Consumed) : [],
        };
    }

    private static ReturnedSurvivor ToReturned(Unit u) =>
        new(Snapshot(u, Math.Clamp(u.Hp, 1, Survivor.MaxHealth)), u.Status == UnitStatus.Evacuated, new(u.Equipment), new(u.Pack), u.FieldXp,
            u.ChargesUsed.Count > 0 ? new(u.ChargesUsed, StringComparer.Ordinal) : null);

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
