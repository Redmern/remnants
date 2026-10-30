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
    /// <summary>The party's parked vehicle at the end (null when it came on foot). Its stash is in <see cref="Loot"/> unless it was lost.</summary>
    public VehicleResult? Vehicle { get; init; }

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
        // The vehicle drives home with its stash when someone made it out to drive it.
        var vehicle = ParkedVehicles.Result(state, playerId);
        if (vehicle is { Wrecked: false } && returned.Count > 0)
            foreach (var id in vehicle.Stash) loot[id] = loot.GetValueOrDefault(id) + 1;

        return new ExtractionResult(playerId, party.Done, returned, recruits, deaths, loot, MissionRules.Result(state, playerId)) { Vehicle = vehicle };
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
