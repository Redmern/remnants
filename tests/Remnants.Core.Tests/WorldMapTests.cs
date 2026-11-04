using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;

namespace Remnants.Core.Tests;

public class WorldMapTests
{
    private static ColonySim Colony(double food = 200) => LoopTestData.Colony(food: food);

    private static ZoneDef Zone(string id) => ScavengeTestData.Real.Zones[id];

    [Fact]
    public void Intel_starts_as_the_data_says()
    {
        var sim = Colony();
        Assert.Equal(ZoneIntel.Scouted, sim.ZoneInfo("outskirts").Intel);
        Assert.Equal(ZoneIntel.Rumored, sim.ZoneInfo("suburb_ruins").Intel);
        Assert.Equal(ZoneIntel.Unknown, sim.ZoneInfo("core_spire").Intel);
        Assert.Equal(ScavengeTestData.Real.Zones.Count, sim.WorldZones().Count());
    }

    [Fact]
    public void Unknown_zones_cannot_be_launched_to()
    {
        var sim = Colony();
        var a = sim.AddSurvivor();
        Assert.Equal(CommandError.ZoneUnknown, sim.Execute(new LaunchExpedition("core_spire", [LoopTestData.Member(a)])).Error);
    }

    [Fact]
    public void Far_zones_cost_more_rations_and_the_mission_opens_on_arrival()
    {
        var sim = Colony();
        var a = sim.AddSurvivor();
        var zone = Zone("suburb_ruins");
        Assert.True(sim.Execute(new LaunchExpedition(zone.Id, [LoopTestData.Member(a)])).Ok);

        var rules = sim.Data.Rules.Expedition;
        Assert.Equal(200 - (rules.RationFoodPerMember + rules.RationFoodPerMemberPerDay * zone.TravelDays * 2), sim.FoodStock, 6);
        Assert.Equal(ExpeditionPhase.Outbound, sim.CurrentExpeditionPhase);
        Assert.Throws<InvalidOperationException>(() => sim.OpenExpeditionScavenge());

        sim.Advance((int)WorldMap.TravelTicks(sim.Data, zone));
        Assert.Equal(ExpeditionPhase.InZone, sim.CurrentExpeditionPhase);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.ExpeditionArrived);
        Assert.NotNull(sim.OpenExpeditionScavenge());
    }

    [Fact]
    public void A_finished_run_comes_home_after_the_return_trip()
    {
        var sim = Colony();
        var a = sim.AddSurvivor();
        var zone = Zone("suburb_ruins");
        Assert.True(sim.Execute(new LaunchExpedition(zone.Id, [LoopTestData.Member(a)])).Ok);
        var travel = (int)WorldMap.TravelTicks(sim.Data, zone);
        sim.Advance(travel);

        var result = LoopTestData.Result([LoopTestData.Returned(a, pack: "scrap")]);
        var applied = sim.Execute(new ApplyExtractionResult(result));
        Assert.True(applied.Ok, applied.Message);
        Assert.Equal(ExpeditionPhase.Returning, sim.CurrentExpeditionPhase);
        Assert.True(a.Away);
        Assert.Null(sim.State.LastReport);
        Assert.Equal(CommandError.PartyReturning, sim.Execute(new ApplyExtractionResult(result)).Error);
        Assert.Throws<InvalidOperationException>(() => sim.OpenExpeditionScavenge());

        // A save on the road carries the result home.
        sim = SaveGame.Load(sim.Data, SaveGame.Save(sim.State), ScavengeTestData.Real);
        sim.Advance(travel - 1);
        Assert.NotNull(sim.State.Expedition);
        sim.Advance(1);
        Assert.Null(sim.State.Expedition);
        Assert.False(sim.State.FindSurvivor(a.Id)!.Away);
        Assert.NotNull(sim.State.LastReport);
        Assert.Equal(ZoneIntel.Scouted, sim.ZoneInfo(zone.Id).Intel);
    }

    [Fact]
    public void Doorstep_zones_apply_at_once_as_before()
    {
        var sim = Colony();
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Ok);
        Assert.Equal(ExpeditionPhase.InZone, sim.CurrentExpeditionPhase);
        Assert.True(sim.Execute(new ApplyExtractionResult(LoopTestData.Result([LoopTestData.Returned(a)]))).Ok);
        Assert.Null(sim.State.Expedition);
    }

    [Fact]
    public void Visiting_scouts_the_zone_depletes_it_and_reveals_neighbours_which_then_replenishes()
    {
        var sim = Colony();
        var a = sim.AddSurvivor();
        var zone = Zone("outskirts");
        Assert.True(sim.Execute(new LaunchExpedition(zone.Id, [LoopTestData.Member(a)])).Ok);
        Assert.True(sim.Execute(new ApplyExtractionResult(LoopTestData.Result([LoopTestData.Returned(a)]))).Ok);

        var info = sim.ZoneInfo(zone.Id);
        Assert.Equal(1, info.Visits);
        Assert.Equal(zone.DepletionPerVisit, info.Depletion, 6);
        foreach (var id in zone.Reveals) Assert.NotEqual(ZoneIntel.Unknown, sim.ZoneInfo(id).Intel);

        // The next run there is generated with that depletion.
        Assert.True(sim.Execute(new LaunchExpedition(zone.Id, [LoopTestData.Member(a)])).Ok);
        Assert.Equal(zone.DepletionPerVisit, sim.State.Expedition!.Depletion, 6);
        Assert.True(sim.Execute(new ApplyExtractionResult(LoopTestData.Result([LoopTestData.Returned(a)]))).Ok);

        var before = sim.ZoneInfo(zone.Id).Depletion;
        sim.Advance(sim.Data.Rules.TicksPerDay);
        Assert.Equal(before - zone.ReplenishPerDay, sim.ZoneInfo(zone.Id).Depletion, 6);
    }

    [Fact]
    public void Rumors_lead_further_out_to_the_dense_linked_zones()
    {
        var data = ScavengeTestData.Real;
        Assert.Equal(LinkedPresence.None, WorldMap.Presence(data, Zone("outskirts")));
        Assert.Equal(LinkedPresence.Rare, WorldMap.Presence(data, Zone("suburb_ruins")));
        Assert.Equal(LinkedPresence.Dense, WorldMap.Presence(data, Zone("core_spire")));

        // Every zone can eventually be found: follow the reveal links from the start.
        var known = data.Zones.Values.Where(z => z.StartIntel != ZoneIntel.Unknown).Select(z => z.Id).ToHashSet();
        var queue = new Queue<string>(known);
        while (queue.TryDequeue(out var id))
            foreach (var next in data.Zones[id].Reveals)
                if (known.Add(next)) queue.Enqueue(next);
        Assert.Equal(data.Zones.Keys.ToHashSet(), known);

        // Farther zones are harder and pay better.
        var byDistance = data.Zones.Values.OrderBy(z => z.TravelDays).ToList();
        Assert.True(byDistance[^1].Threat > byDistance[0].Threat);
        Assert.True(byDistance[^1].LootTier > byDistance[0].LootTier);
    }

    [Fact]
    public void Version_4_saves_load_with_default_zones_and_an_arrived_party()
    {
        var sim = Colony();
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Ok);
        var json = SaveGame.Save(sim.State).Replace($"\"version\": {SaveGame.Version}", "\"version\": 4");
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var colony = node["colony"]!.AsObject();
        colony.Remove("zones");
        foreach (var key in new[] { "travelTicks", "arrivalTick", "depletion", "pendingResult", "returnTick" })
            colony["expedition"]!.AsObject().Remove(key);

        var loaded = SaveGame.Load(sim.Data, node.ToJsonString(), ScavengeTestData.Real);
        Assert.Empty(loaded.State.Zones);
        Assert.Equal(ExpeditionPhase.InZone, loaded.CurrentExpeditionPhase);
        Assert.Equal(ZoneIntel.Rumored, loaded.ZoneInfo("suburb_ruins").Intel);
    }

    [Fact]
    public void Version_5_saves_resume_a_scavenge_run_without_fog_state()
    {
        var sim = Colony();
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Ok);
        sim.OpenExpeditionScavenge();
        var node = System.Text.Json.Nodes.JsonNode.Parse(SaveGame.Save(sim.State))!;
        node["version"] = 5;
        var run = node["colony"]!["expedition"]!["scavenge"]!.AsObject();
        foreach (var key in new[] { "contacts", "reveals", "flares", "sensors", "nextSensorId" }) run.Remove(key);
        foreach (var unit in run["units"]!.AsArray())
            foreach (var key in new[] { "stance", "reactions", "lookout" }) unit!.AsObject().Remove(key);

        var loaded = SaveGame.Load(sim.Data, node.ToJsonString(), ScavengeTestData.Real);
        var scavenge = loaded.OpenExpeditionScavenge();
        Assert.Empty(scavenge.State.Contacts);
        Assert.True(SaveGame.Version >= 6);
    }

    [Fact]
    public void Saves_with_unknown_zone_status_are_rejected()
    {
        var sim = Colony();
        sim.State.Zones["moon"] = new ZoneStatus { Intel = ZoneIntel.Scouted };
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SaveGame.Load(sim.Data, SaveGame.Save(sim.State), ScavengeTestData.Real));
    }

    [Fact]
    public void A_scout_drone_scouts_a_rumored_zone_without_anyone_going()
    {
        var sim = Colony();
        var pilot = sim.AddSurvivor();
        Assert.Equal(CommandError.NoDrone, sim.Execute(new SendScoutDrone("suburb_ruins")).Error);
        sim.State.Inventory.Add("scout_drone");
        Assert.Equal(CommandError.AlreadyScouted, sim.Execute(new SendScoutDrone("outskirts")).Error);
        Assert.Equal(CommandError.ZoneUnknown, sim.Execute(new SendScoutDrone("core_spire")).Error);

        Assert.True(sim.Execute(new SendScoutDrone("suburb_ruins", pilot.Id)).Ok);
        Assert.False(sim.State.Inventory.Has("scout_drone"));
        Assert.False(pilot.Away, "nobody goes");
        var flight = Assert.Single(sim.State.DroneFlights);
        sim.Advance((int)(flight.ArriveTick - sim.State.Tick));
        Assert.Equal(ZoneIntel.Scouted, sim.ZoneInfo("suburb_ruins").Intel);
        foreach (var id in Zone("suburb_ruins").Reveals) Assert.NotEqual(ZoneIntel.Unknown, sim.ZoneInfo(id).Intel);
        Assert.True(pilot.Get(Survivors.Skill.DronePiloting) > 0);
        Assert.Equal(0, sim.ZoneInfo("suburb_ruins").Visits);

        // Threat 2 can lose it; if not, it is back in the stores after the flight home and the recharge.
        var lost = sim.State.DroneFlights.Count == 0;
        sim.Advance((int)(flight.ReturnTick - sim.State.Tick));
        Assert.Equal(!lost, sim.State.Inventory.Has("scout_drone"));
        Assert.Empty(sim.State.DroneFlights);
    }

    [Fact]
    public void A_drone_only_reaches_so_far_and_dangerous_zones_may_shoot_it_down()
    {
        var sim = Colony();
        sim.State.Inventory.Add("scout_drone");
        var far = ScavengeTestData.Real.Zones.Values.First(z => z.TravelDays > ScavengeTestData.Real.Rules.World.DroneScout.MaxTravelDays);
        sim.State.Zones[far.Id] = new ZoneStatus { Intel = ZoneIntel.Rumored };
        Assert.Equal(CommandError.OutOfDroneRange, sim.Execute(new SendScoutDrone(far.Id)).Error);

        var data = ScavengeTestData.Real;
        Assert.Equal(0, WorldMap.DroneLossChance(data, Zone("outskirts"), null));
        var risky = WorldMap.DroneLossChance(data, Zone("rail_depot"), null);
        Assert.True(risky > WorldMap.DroneLossChance(data, Zone("suburb_ruins"), null));
        var ace = new Survivors.Survivor();
        ace.Set(Survivors.Skill.DronePiloting, 60);
        Assert.True(WorldMap.DroneLossChance(data, Zone("rail_depot"), ace) < risky);
    }

    [Fact]
    public void Saves_carry_drones_in_flight()
    {
        var sim = Colony();
        sim.State.Inventory.Add("scout_drone");
        Assert.True(sim.Execute(new SendScoutDrone("suburb_ruins")).Ok);
        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(sim.Data, json, ScavengeTestData.Real);
        Assert.Single(loaded.State.DroneFlights);
        Assert.Equal(json, SaveGame.Save(loaded.State));
    }
}
