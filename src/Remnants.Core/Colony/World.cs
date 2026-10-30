using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

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

    /// <summary>Food per member: the base ration plus the per-day ration for the trip out and back (on foot).</summary>
    public static double RationsPerMember(GameData data, ZoneDef zone) => RationsPerMember(data, zone.TravelDays);

    /// <summary>Food per member for a trip of <paramref name="oneWayDays"/> each way (a vehicle makes it shorter).</summary>
    public static double RationsPerMember(GameData data, double oneWayDays)
    {
        var rules = data.Rules.Expedition;
        return rules.RationFoodPerMember + rules.RationFoodPerMemberPerDay * oneWayDays * 2;
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
        status.Visits++;
        status.LastVisitTick = state.Tick;
        status.Depletion = Math.Min(1, status.Depletion + zone.DepletionPerVisit);
        Scouted(state, scavenge, zone);
    }

    /// <summary>The zone is scouted (by a party or a drone) and its neighbours become rumors.</summary>
    private static void Scouted(ColonyState state, ScavengeData scavenge, ZoneDef zone)
    {
        Status(state, zone.Id).Intel = ZoneIntel.Scouted;
        foreach (var id in zone.Reveals)
        {
            var other = scavenge.Zones[id];
            if (IntelOf(state, other) != ZoneIntel.Unknown) continue;
            Status(state, id).Intel = ZoneIntel.Rumored;
            state.Log(new(state.Tick, ColonyEventType.ZoneRevealed, $"The party heard of {other.Name} ({other.Region})."));
        }
    }

    /// <summary>The drone item a scouting flight would take from the inventory (the first by id), or null.</summary>
    public static string? DroneItem(ColonyState state, ScavengeData scavenge) =>
        scavenge.Items.Values.Where(i => i.Drone is not null && state.Inventory.Has(i.Id)).Select(i => i.Id)
            .OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>
    /// How far out a scouting drone reaches: its battery (<see cref="DroneScoutRules.MaxTravelDays"/>), plus the best
    /// <see cref="Data.VehicleTierDef.DroneRangeDays"/> of a working vehicle at home, which drives it out to launch.
    /// </summary>
    public static double DroneReachDays(ColonySim sim) =>
        sim.Scavenge!.Rules.World.DroneScout.MaxTravelDays
        + sim.State.Vehicles.Where(v => !v.Away && !VehicleRules.Broken(sim.Data, v))
            .Select(v => sim.Data.VehicleTiers[v.TierId].DroneRangeDays).DefaultIfEmpty(0).Max();

    /// <summary>Ticks out to the zone (one way) for a scouting drone: at least a few hours even next door.</summary>
    public static long DroneLegTicks(GameData data, ScavengeData scavenge, ZoneDef zone) =>
        Math.Max(data.Rules.TicksPerDay / 4, (long)Math.Round(zone.TravelDays * scavenge.Rules.World.DroneScout.DaysPerTravelDay * data.Rules.TicksPerDay));

    /// <summary>Chance a scouting drone is shot down over the zone, given the pilot (null = autopilot).</summary>
    public static double DroneLossChance(ScavengeData scavenge, ZoneDef zone, Survivor? pilot)
    {
        var rules = scavenge.Rules.World.DroneScout;
        if (rules.LossByThreat.Length == 0) return 0;
        var base_ = rules.LossByThreat[Math.Clamp(zone.Threat, 0, rules.LossByThreat.Length - 1)];
        return Math.Clamp(base_ * (1 - (pilot?.Get(Skill.DronePiloting) ?? 0) * rules.LossReductionPerSkill), 0, 1);
    }

    /// <summary>
    /// Sends a scout drone over a rumored zone within its battery's reach (<see cref="DroneScoutRules.MaxTravelDays"/>). The
    /// drone leaves the inventory; at the zone it reports in (the zone is scouted, its neighbours become rumors) and may be
    /// shot down (<see cref="DroneLossChance"/>); otherwise it is back, recharged, after the flight home and
    /// <see cref="DroneScoutRules.RechargeDays"/>. A pilot at home flies it more safely and trains Drone Piloting.
    /// </summary>
    internal static CommandResult SendDrone(ColonySim sim, SendScoutDrone c)
    {
        var state = sim.State;
        if (sim.Scavenge is not { } scavenge) return ExpeditionHandlers.NoData();
        if (!scavenge.Zones.TryGetValue(c.ZoneId, out var zone)) return CommandResult.Fail(CommandError.UnknownZone, $"No zone '{c.ZoneId}'.");
        var rules = scavenge.Rules.World.DroneScout;
        var intel = IntelOf(state, zone);
        if (intel == ZoneIntel.Unknown) return CommandResult.Fail(CommandError.ZoneUnknown, "Nobody knows where that is to send a drone.");
        if (intel == ZoneIntel.Scouted || state.DroneFlights.Any(f => f.ZoneId == zone.Id && !f.Arrived))
            return CommandResult.Fail(CommandError.AlreadyScouted, $"{zone.Name} is already scouted.");
        var reach = DroneReachDays(sim);
        if (zone.TravelDays > reach + 1e-9)
            return CommandResult.Fail(CommandError.OutOfDroneRange, $"{zone.Name} is beyond a drone's battery ({reach:0.#} days out).");
        if (DroneItem(state, scavenge) is not { } item) return CommandResult.Fail(CommandError.NoDrone, "There is no scout drone in the stores.");
        Survivor? pilot = null;
        if (c.PilotId is { } pid)
        {
            pilot = state.FindSurvivor(pid);
            if (pilot is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {pid}.");
            if (pilot.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{pilot.Name} is away.");
            if (pilot.IsChild) return CommandResult.Fail(CommandError.NotAnAdult, $"{pilot.Name} is too young to fly a drone.");
        }

        state.Inventory.TryTake(item);
        var leg = DroneLegTicks(sim.Data, scavenge, zone);
        var flight = new DroneFlight
        {
            Id = state.NextFlightId++, ZoneId = zone.Id, ItemId = item, PilotId = pilot?.Id, LaunchTick = state.Tick,
            ArriveTick = state.Tick + leg,
            ReturnTick = state.Tick + 2 * leg + (long)Math.Round(rules.RechargeDays * sim.Data.Rules.TicksPerDay),
        };
        state.DroneFlights.Add(flight);
        var by = pilot is null ? "" : $", flown by {pilot.Name}";
        state.Log(new(state.Tick, ColonyEventType.DroneScouted, $"A scout drone takes off for {zone.Name}{by}."));
        return CommandResult.Success($"A scout drone is on its way to {zone.Name}.", flight.Id);
    }

    /// <summary>Per tick: drones over their zone report in (and may be lost); drones home again go back to the inventory.</summary>
    private static void RunDrones(ColonySim sim, ScavengeData scavenge)
    {
        var state = sim.State;
        var rules = scavenge.Rules.World.DroneScout;
        foreach (var flight in state.DroneFlights.OrderBy(f => f.Id).ToList())
        {
            var zone = scavenge.Zones.GetValueOrDefault(flight.ZoneId);
            var pilot = flight.PilotId is { } pid ? state.FindSurvivor(pid) : null;
            if (!flight.Arrived && state.Tick >= flight.ArriveTick)
            {
                flight.Arrived = true;
                if (zone is not null)
                {
                    Scouted(state, scavenge, zone);
                    state.Log(new(state.Tick, ColonyEventType.DroneScouted, $"The scout drone reports from {zone.Name}: the zone is scouted."));
                    if (pilot is not null)
                    {
                        pilot.GainSkill(Skill.DronePiloting, rules.PilotSkillGain, XpSource.Field);
                        Progression.GrantXp(state, sim.Data, pilot, rules.PilotXp, XpSource.Field);
                    }
                    if (sim.Rng.NextDouble() < DroneLossChance(scavenge, zone, pilot))
                    {
                        state.DroneFlights.Remove(flight);
                        state.Log(new(state.Tick, ColonyEventType.DroneLost, $"The scout drone was shot down over {zone.Name}."));
                        continue;
                    }
                }
            }
            if (flight.Arrived && state.Tick >= flight.ReturnTick)
            {
                state.DroneFlights.Remove(flight);
                state.Inventory.Add(flight.ItemId);
                state.Log(new(state.Tick, ColonyEventType.DroneReturned, "The scout drone is back and recharged."));
            }
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
        RunDrones(sim, scavenge);

        if (state.Expedition is not { } expedition) return;
        var name = scavenge.Zones.TryGetValue(expedition.ZoneId, out var z) ? z.Name : expedition.ZoneId;
        if (expedition.PendingResult is null && expedition.ArrivalTick > expedition.LaunchedTick && state.Tick == expedition.ArrivalTick)
            state.Log(new(state.Tick, ColonyEventType.ExpeditionArrived, $"The party reached {name}: the mission can be played."));
        if (expedition.PendingResult is { } result && state.Tick >= expedition.ReturnTick)
            ExpeditionHandlers.ApplyNow(sim, expedition, result);
    }
}
