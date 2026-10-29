using Remnants.Core.Data;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Colony;

/// <summary>How much of a zone's enemy mix is Linked, as the world map shows it.</summary>
public enum LinkedPresence { None, Rare, Some, Dense }

/// <summary>
/// One zone as the world map shows it: the definition plus what the colony knows (<see cref="Intel"/>), how picked-over
/// it is, travel time and the food a member needs to go there and back. A rumored zone's exact numbers are not meant to
/// be shown (the UI hides them); they are here so Core stays the only place that computes them.
/// </summary>
public sealed record ZoneInfo(
    ZoneDef Zone,
    ZoneIntel Intel,
    double Depletion,
    int Visits,
    long TravelTicks,
    double RationsPerMember,
    LinkedPresence Linked);

/// <summary>
/// The world map rules on the colony side: zone intel (scouting by visiting, rumors from neighbours), depletion and
/// replenishment, travel time and rations, and the expedition's road out and home.
/// </summary>
public static class WorldMap
{
    public static ZoneIntel IntelOf(ColonyState state, ZoneDef zone) =>
        (ZoneIntel)Math.Max((int)zone.StartIntel, (int)(state.Zones.GetValueOrDefault(zone.Id)?.Intel ?? ZoneIntel.Unknown));

    public static long TravelTicks(GameData data, ZoneDef zone) => (long)Math.Round(zone.TravelDays * data.Rules.TicksPerDay);

    /// <summary>Food per member: the base ration plus the per-day ration for the trip out and back.</summary>
    public static double RationsPerMember(GameData data, ZoneDef zone)
    {
        var rules = data.Rules.Expedition;
        return rules.RationFoodPerMember + rules.RationFoodPerMemberPerDay * zone.TravelDays * 2;
    }

    public static LinkedPresence Presence(ScavengeData data, ZoneDef zone)
    {
        var total = zone.EnemyMix.Sum(e => e.Weight);
        var linked = zone.EnemyMix.Where(e => data.Enemies[e.Id].Ai == AiKind.Linked).Sum(e => e.Weight);
        if (total <= 0 || linked <= 0) return LinkedPresence.None;
        var share = linked / total;
        return share < 0.2 ? LinkedPresence.Rare : share < 0.5 ? LinkedPresence.Some : LinkedPresence.Dense;
    }

    public static ZoneInfo Info(ColonyState state, GameData data, ScavengeData scavenge, ZoneDef zone)
    {
        var status = state.Zones.GetValueOrDefault(zone.Id);
        return new ZoneInfo(zone, IntelOf(state, zone), status?.Depletion ?? 0, status?.Visits ?? 0,
            TravelTicks(data, zone), RationsPerMember(data, zone), Presence(scavenge, zone));
    }

    private static ZoneStatus Status(ColonyState state, string zoneId)
    {
        if (!state.Zones.TryGetValue(zoneId, out var status)) state.Zones[zoneId] = status = new ZoneStatus();
        return status;
    }

    /// <summary>A party came back from the zone: it is scouted, more picked-over, and its neighbours become rumors.</summary>
    internal static void Visited(ColonyState state, ScavengeData scavenge, ZoneDef zone)
    {
        var status = Status(state, zone.Id);
        status.Intel = ZoneIntel.Scouted;
        status.Visits++;
        status.LastVisitTick = state.Tick;
        status.Depletion = Math.Min(1, status.Depletion + zone.DepletionPerVisit);
        foreach (var id in zone.Reveals)
        {
            var other = scavenge.Zones[id];
            if (IntelOf(state, other) != ZoneIntel.Unknown) continue;
            Status(state, id).Intel = ZoneIntel.Rumored;
            state.Log(new(state.Tick, ColonyEventType.ZoneRevealed, $"The party heard of {other.Name} ({other.Region})."));
        }
    }

    /// <summary>
    /// Per tick (after the clock advanced): zones recover, a party on the road arrives, and a party walking home arrives with its result, which is
    /// applied then.
    /// </summary>
    internal static void Run(ColonySim sim)
    {
        var state = sim.State;
        if (sim.Scavenge is not { } scavenge) return;
        foreach (var (id, status) in state.Zones)
            if (status.Depletion > 0 && scavenge.Zones.TryGetValue(id, out var zone))
                status.Depletion = Math.Max(0, status.Depletion - zone.ReplenishPerDay / sim.Data.Rules.TicksPerDay);

        if (state.Expedition is not { } expedition) return;
        var name = scavenge.Zones.TryGetValue(expedition.ZoneId, out var z) ? z.Name : expedition.ZoneId;
        if (expedition.PendingResult is null && expedition.TravelTicks > 0 && state.Tick == expedition.ArrivalTick)
            state.Log(new(state.Tick, ColonyEventType.ExpeditionArrived, $"The party reached {name}: the mission can be played."));
        if (expedition.PendingResult is { } result && state.Tick >= expedition.ReturnTick)
            ExpeditionHandlers.ApplyNow(sim, expedition, result);
    }
}
