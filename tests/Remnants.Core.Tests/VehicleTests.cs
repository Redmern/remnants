using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class VehicleTests
{
    private static ColonySim Colony(double food = 300, double fuel = 0, Dictionary<string, int>? items = null)
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: food, items: items);
        if (fuel > 0) sim.State.Stockpile.Add("fuel", fuel);
        return sim;
    }

    private static Vehicle Give(ColonySim sim, string tier, params string[] upgrades)
    {
        var tierDef = sim.Data.VehicleTiers[tier];
        var v = new Vehicle { Id = sim.State.NextVehicleId++, TierId = tier, Name = tierDef.Name, Condition = tierDef.MaxCondition };
        foreach (var u in upgrades) v.Upgrades.Add(u);
        v.Condition = sim.VehicleStats(v).MaxCondition;
        sim.State.Vehicles.Add(v);
        return v;
    }

    private static Building Garage(ColonySim sim, int level = 1)
    {
        var garage = sim.Build("garage");
        garage.Level = level;
        return garage;
    }

    [Fact]
    public void The_data_has_six_tiers_and_a_new_colony_starts_with_a_handcart()
    {
        var data = TestData.Real;
        Assert.Equal(["handcart", "bicycle_cart", "motorbike", "buggy", "truck", "armored_rig"], data.Vehicles.Tiers.Select(t => t.Id));
        Assert.Equal(Enumerable.Range(0, 6), data.Vehicles.Tiers.Select(t => t.Tier));
        var sim = NewGame.Create(data, 7, ScavengeTestData.Real);
        var cart = Assert.Single(sim.State.Vehicles);
        Assert.Equal("handcart", cart.TierId);
        Assert.Equal(data.VehicleTiers["handcart"].MaxCondition, cart.Condition);
    }

    [Fact]
    public void The_garage_builds_a_schematic_gated_vehicle_from_parts()
    {
        var sim = Colony(items: new() { ["wheel"] = 3, ["chassis_frame"] = 1 });
        sim.State.Stockpile.Add("wood", 50);
        sim.State.Stockpile.Add("scrap", 50);
        var garage = Garage(sim);
        var worker = sim.AddSurvivor(attributes: 70);
        Assert.True(sim.Execute(new AssignJob(worker.Id, garage.Id)).Ok);

        Assert.Equal(CommandError.RecipeLocked, sim.Execute(new QueueRecipe(garage.Id, "build_bicycle_cart")).Error);
        sim.State.KnownBlueprints.Add("sch_bicycle_cart");
        Assert.True(sim.Execute(new QueueRecipe(garage.Id, "build_bicycle_cart")).Ok);
        sim.Advance(200);

        var cart = Assert.Single(sim.State.Vehicles);
        Assert.Equal("bicycle_cart", cart.TierId);
        Assert.False(sim.State.Inventory.Has("wheel"));
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.VehicleBuilt);
    }

    [Fact]
    public void Garage_levels_and_the_still_need_their_schematics()
    {
        var sim = Colony();
        var garage = Garage(sim);
        sim.State.Stockpile.Add("wood", 500);
        sim.State.Stockpile.Add("stone", 500);
        sim.State.Stockpile.Add("scrap", 500);
        Assert.Equal(CommandError.RecipeLocked, sim.Execute(new Upgrade(garage.Id)).Error);
        Assert.Equal(CommandError.RecipeLocked, sim.Execute(new Construct("fuel_still")).Error);
        sim.State.KnownBlueprints.UnionWith(["sch_garage_2", "sch_biofuel_still"]);
        Assert.True(sim.Execute(new Upgrade(garage.Id)).Ok);
        Assert.True(sim.Execute(new Construct("fuel_still")).Ok);
    }

    [Fact]
    public void Studying_a_schematic_says_what_it_unlocks()
    {
        var sim = Colony(items: new() { ["sch_garage_2"] = 1 });
        Assert.True(sim.Execute(new StudyBlueprint("sch_garage_2")).Ok);
        Assert.Contains("Garage level 2", sim.State.Events[^1].Message);
    }

    [Fact]
    public void The_still_turns_food_into_fuel()
    {
        var sim = Colony();
        sim.State.KnownBlueprints.Add("sch_biofuel_still");
        var still = sim.Build("fuel_still");
        var worker = sim.AddSurvivor(attributes: 60);
        Assert.True(sim.Execute(new AssignJob(worker.Id, still.Id)).Ok);
        Assert.True(sim.Execute(new QueueRecipe(still.Id, "biofuel", Repeat: true)).Ok);
        sim.Advance(24);
        Assert.True(sim.State.Stockpile.Get("fuel") > 0);
    }

    [Fact]
    public void A_vehicle_cuts_travel_days_rations_and_burns_fuel()
    {
        var sim = Colony(fuel: 50);
        var bike = Give(sim, "motorbike");
        var zone = ScavengeTestData.Real.Zones["suburb_ruins"];
        var walk = sim.PlanTravel(zone.Id);
        var ride = sim.PlanTravel(zone.Id, bike.Id);
        Assert.Equal(zone.TravelDays, walk.Days, 6);
        Assert.Equal(zone.TravelDays / 2.5, ride.Days, 6);
        Assert.Equal(Math.Ceiling(2 * ride.Days * 2), ride.FuelRoundTrip);
        Assert.Contains(ride.Terms, t => t.Source == "vehicle" && t.SpeedMultiplier == 2.5);

        var a = sim.AddSurvivor();
        var food = sim.State.Stockpile.Get("food");
        var result = sim.Execute(new LaunchExpedition(zone.Id, [LoopTestData.Member(a)], VehicleId: bike.Id));
        Assert.True(result.Ok, result.Message);
        var rules = sim.Data.Rules.Expedition;
        Assert.Equal(food - (rules.RationFoodPerMember + rules.RationFoodPerMemberPerDay * ride.Days * 2), sim.State.Stockpile.Get("food"), 6);
        Assert.Equal(50 - ride.FuelRoundTrip, sim.State.Stockpile.Get("fuel"), 6);
        Assert.True(bike.Away);
        Assert.Equal(sim.State.Tick + ride.Ticks, sim.State.Expedition!.ArrivalTick);
        Assert.True(bike.Condition < sim.VehicleStats(bike).MaxCondition);
    }

    [Fact]
    public void Launch_checks_seats_fuel_condition_and_range()
    {
        var sim = Colony();
        var bike = Give(sim, "motorbike");
        var members = Enumerable.Range(0, 3).Select(_ => LoopTestData.Member(sim.AddSurvivor())).ToList();
        Assert.Equal(2, sim.MaxPartySizeWith(bike.Id));
        Assert.Equal(CommandError.NotEnoughSeats, sim.Execute(new LaunchExpedition("suburb_ruins", members, VehicleId: bike.Id)).Error);
        Assert.Equal(CommandError.CannotAfford, sim.Execute(new LaunchExpedition("suburb_ruins", members[..2], VehicleId: bike.Id)).Error);

        sim.State.Stockpile.Add("fuel", 100);
        bike.Condition = 1;
        Assert.Equal(CommandError.VehicleBroken, sim.Execute(new LaunchExpedition("suburb_ruins", members[..2], VehicleId: bike.Id)).Error);
        bike.Condition = sim.VehicleStats(bike).MaxCondition;

        foreach (var id in new[] { "suburb_ruins", "rail_depot", "network_relay" })
            sim.State.Zones[id] = new ZoneStatus { Intel = ZoneIntel.Rumored };
        var far = sim.PlanTravel("network_relay", bike.Id);
        Assert.NotNull(far.Blocked);
        Assert.Equal(CommandError.RoadBlocked, sim.Execute(new LaunchExpedition("network_relay", members[..2], VehicleId: bike.Id)).Error);
        var withCans = Give(sim, "motorbike", "fuel_cans");
        Assert.Null(sim.PlanTravel("network_relay", withCans.Id).Blocked);
    }

    [Fact]
    public void Night_driving_without_headlights_is_slower()
    {
        var sim = new ColonySim(TestData.RealWithNights, new ColonyState(), ScavengeTestData.Real);
        sim.State.Tick = 22; // 22:00, night
        var bike = Give(sim, "motorbike");
        var lit = Give(sim, "motorbike", "headlights");
        var dark = sim.PlanTravel("suburb_ruins", bike.Id);
        Assert.Contains(dark.Terms, t => t.Source == "night");
        Assert.Equal(1 / 2.5 / sim.Data.Vehicles.NightSpeedMultiplier, dark.Days, 6);
        Assert.DoesNotContain(sim.PlanTravel("suburb_ruins", lit.Id).Terms, t => t.Source == "night");

        sim.State.Tick = 9; // leaves and arrives by day
        Assert.DoesNotContain(sim.PlanTravel("suburb_ruins", bike.Id).Terms, t => t.Source == "night");
        // A bicycle cart has no motor and no lamps to miss.
        var cart = Give(sim, "bicycle_cart");
        sim.State.Tick = 22;
        Assert.DoesNotContain(sim.PlanTravel("suburb_ruins", cart.Id).Terms, t => t.Source == "night");
    }

    [Fact]
    public void Weather_is_a_hook_dust_storms_block_or_slow_and_mud_slows_wheels()
    {
        var sim = Colony(fuel: 100);
        var bike = Give(sim, "motorbike");
        var truck = Give(sim, "truck");
        var tyres = Give(sim, "buggy", "offroad_tyres");
        Assert.DoesNotContain(sim.PlanTravel("suburb_ruins", truck.Id).Terms, t => t.Source == "weather");

        sim.WeatherAt = _ => "dust_storm";
        Assert.NotNull(sim.PlanTravel("suburb_ruins", bike.Id).Blocked);
        var slow = sim.PlanTravel("suburb_ruins", truck.Id);
        Assert.Null(slow.Blocked);
        Assert.Equal(1 / (2.0 * 0.5), slow.Days, 6);
        var a = sim.AddSurvivor();
        Assert.Equal(CommandError.RoadBlocked, sim.Execute(new LaunchExpedition("suburb_ruins", [LoopTestData.Member(a)], VehicleId: bike.Id)).Error);
        // On foot the weather does not change the trip.
        Assert.Empty(sim.PlanTravel("suburb_ruins").Terms);

        sim.WeatherAt = _ => "rain";
        Assert.Contains(sim.PlanTravel("suburb_ruins", truck.Id).Terms, t => t.Source == "mud");
        Assert.DoesNotContain(sim.PlanTravel("suburb_ruins", tyres.Id).Terms, t => t.Source == "mud");
    }

    [Fact]
    public void Upgrades_are_fitted_at_the_garage_and_change_the_numbers()
    {
        var sim = Colony(items: new() { ["armor_plate"] = 4 });
        var buggy = Give(sim, "buggy");
        Assert.Equal(CommandError.NoGarage, sim.Execute(new FitUpgrade(buggy.Id, "headlights")).Error);
        var garage = Garage(sim);
        Assert.Equal(CommandError.CannotAfford, sim.Execute(new FitUpgrade(buggy.Id, "headlights")).Error);
        sim.State.Stockpile.Add("electronics", 3);
        sim.State.Stockpile.Add("scrap", 2);
        Assert.True(sim.Execute(new FitUpgrade(buggy.Id, "headlights")).Ok);
        Assert.Equal(CommandError.AlreadyFitted, sim.Execute(new FitUpgrade(buggy.Id, "headlights")).Error);
        Assert.Equal(CommandError.UpgradeDoesNotFit, sim.Execute(new FitUpgrade(Give(sim, "handcart").Id, "headlights")).Error);

        Assert.Equal(CommandError.RecipeLevelTooLow, sim.Execute(new FitUpgrade(buggy.Id, "armor_plates")).Error);
        garage.Level = 3;
        Assert.Equal(CommandError.RecipeLocked, sim.Execute(new FitUpgrade(buggy.Id, "armor_plates")).Error);
        sim.State.KnownBlueprints.Add("sch_armor_plates");
        var before = sim.VehicleStats(buggy);
        Assert.True(sim.Execute(new FitUpgrade(buggy.Id, "armor_plates")).Ok);
        var after = sim.VehicleStats(buggy);
        Assert.True(after.Armored && after.Armor > before.Armor && after.Speed < before.Speed);
        Assert.Equal(after.MaxCondition, buggy.Condition, 6);
        Assert.False(sim.State.Inventory.Has("armor_plate"));
    }

    [Fact]
    public void Repairs_use_spare_parts()
    {
        var sim = Colony(items: new() { ["spare_parts"] = 5 });
        Garage(sim);
        var truck = Give(sim, "truck");
        Assert.Equal(CommandError.NotDamaged, sim.Execute(new RepairVehicle(truck.Id)).Error);
        truck.Condition = 10;
        Assert.True(VehicleRules.Broken(sim.Data, truck));
        var needed = VehicleRules.PartsToRepair(sim.Data, truck);
        Assert.Equal(4, needed);
        Assert.True(sim.Execute(new RepairVehicle(truck.Id)).Ok);
        Assert.Equal(sim.VehicleStats(truck).MaxCondition, truck.Condition, 6);
        Assert.Equal(1, sim.State.Inventory.Count("spare_parts"));
    }

    [Fact]
    public void A_vehicle_at_home_carries_a_scout_drone_further()
    {
        var sim = Colony();
        var walk = WorldMap.DroneReachDays(sim);
        Give(sim, "buggy");
        Assert.Equal(walk + sim.Data.VehicleTiers["buggy"].DroneRangeDays, WorldMap.DroneReachDays(sim), 6);
    }

    // --- In the field ---

    private static (ColonySim Sim, ScavengeSim Run, Vehicle Vehicle, Survivor Member) Mission(string tier = "buggy", string zone = "outskirts",
        params string[] upgrades)
    {
        var sim = Colony(fuel: 100);
        var vehicle = Give(sim, tier, upgrades);
        var a = sim.AddSurvivor();
        var launch = sim.Execute(new LaunchExpedition(zone, [LoopTestData.Member(a, weapon: "pipe")], VehicleId: vehicle.Id));
        if (!launch.Ok)
        {
            sim.State.Inventory.Add("pipe");
            launch = sim.Execute(new LaunchExpedition(zone, [LoopTestData.Member(a, weapon: "pipe")], VehicleId: vehicle.Id));
        }
        Assert.True(launch.Ok, launch.Message);
        sim.Advance((int)sim.State.Expedition!.TravelTicks);
        return (sim, sim.OpenExpeditionScavenge(), vehicle, a);
    }

    private static Unit PartyUnit(ScavengeSim run) => run.State.Units.Single(u => u.PlayerId == ColonySim.ExpeditionPlayerId && u.Kind == UnitKind.Survivor);

    /// <summary>Puts the unit on a free walkable tile next to the vehicle.</summary>
    private static void NextToVehicle(ScavengeSim run, Unit unit)
    {
        var v = run.State.Vehicles.Single();
        unit.Pos = run.State.Map.AllPositions
            .Where(p => p.Chebyshev(v.Pos) == 1 && run.Data.Terrain(run.State.Map[p]).Passable && run.State.UnitAt(p) is null)
            .OrderBy(run.State.Map.Index).First();
    }

    [Fact]
    public void The_vehicle_parks_on_the_spawn_edge_as_cover()
    {
        var (_, run, vehicle, _) = Mission();
        var parked = Assert.Single(run.State.Vehicles);
        Assert.Equal("buggy", parked.TierId);
        Assert.Equal(Terrain.Vehicle, run.State.Map[parked.Pos]);
        Assert.False(run.Data.Terrain(Terrain.Vehicle).Passable);
        Assert.True(run.Data.Terrain(Terrain.ArmoredVehicle).Cover > run.Data.Terrain(Terrain.Vehicle).Cover);
        Assert.Equal(vehicle.Condition, parked.Hp, 6);
        var zone = ScavengeTestData.Real.Zones["outskirts"];
        Assert.Contains(parked.Pos, MapGenerator.EdgeTiles(run.State.Map, zone.SpawnEdge));
        Assert.All(run.State.Units, u => Assert.NotEqual(parked.Pos, u.Pos));
    }

    [Fact]
    public void An_armored_vehicle_parks_as_hard_cover()
    {
        var (_, run, _, _) = Mission("armored_rig");
        Assert.Equal(Terrain.ArmoredVehicle, run.State.Map[run.State.Vehicles.Single().Pos]);
    }

    [Fact]
    public void Loot_stashed_in_the_vehicle_goes_home_and_the_party_extracts_beside_it()
    {
        var (sim, run, vehicle, member) = Mission();
        var unit = PartyUnit(run);
        Assert.Equal(unit.Id, run.State.CurrentUnitId);
        unit.Pack.AddRange(["scrap", "wheel"]);
        // The party starts beside its vehicle; step away to see it is needed.
        var parkedAt = run.State.Vehicles.Single().Pos;
        unit.Pos = run.State.Map.AllPositions.First(p => p.Chebyshev(parkedAt) > 1 && run.State.Map[p] == Terrain.Floor
                                                       && !run.State.Map.IsExit(p) && run.State.UnitAt(p) is null);
        Assert.Equal(ScavengeError.TooFar, run.Execute(new StashItem(1, unit.Id, "wheel")).Error);
        NextToVehicle(run, unit);
        Assert.True(run.Execute(new StashItem(1, unit.Id, "wheel")).Ok);
        Assert.Equal(["wheel"], run.State.Vehicles.Single().Stash);
        Assert.True(run.Execute(new Extract(1, unit.Id)).Ok);

        var result = run.ResultFor(1);
        Assert.True(result.Complete);
        Assert.Equal(1, result.Loot["wheel"]);
        Assert.Equal(1, result.Loot["scrap"]);
        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);
        Assert.Equal(1, sim.State.Inventory.Count("wheel"));
        Assert.False(vehicle.Away);
        Assert.Equal("Buggy", sim.State.LastReport!.Vehicle);
        Assert.False(member.Away);
    }

    [Fact]
    public void The_stash_holds_only_the_cargo_limit()
    {
        var (_, run, _, _) = Mission("motorbike");
        var unit = PartyUnit(run);
        NextToVehicle(run, unit);
        unit.Pack.AddRange(["engine_small", "engine_small"]);
        unit.Ap = 10;
        Assert.True(run.Execute(new StashItem(1, unit.Id, "engine_small")).Ok);
        Assert.Equal(ScavengeError.CargoFull, run.Execute(new StashItem(1, unit.Id, "engine_small")).Error);
    }

    [Fact]
    public void Enemies_next_to_the_vehicle_damage_and_can_wreck_it_losing_the_stash_and_the_vehicle()
    {
        var (sim, run, vehicle, _) = Mission("handcart");
        var parked = run.State.Vehicles.Single();
        parked.Stash.Add("wheel");
        var husk = run.State.Units.First(u => u.Kind == UnitKind.Enemy);
        husk.Pos = run.State.Map.AllPositions.First(p => p.Chebyshev(parked.Pos) == 1 && run.Data.Terrain(run.State.Map[p]).Passable && run.State.UnitAt(p) is null);
        var hpBefore = parked.Hp;
        ParkedVehicles.RoundStarted(run);
        Assert.Equal(hpBefore - run.Data.Rules.Vehicle.DamagePerEnemy, parked.Hp, 6);
        parked.Hp = 1;
        ParkedVehicles.RoundStarted(run);
        Assert.True(parked.Wrecked);
        Assert.Empty(parked.Stash);
        Assert.Contains(run.State.Events, e => e.Type == ScavengeEventType.VehicleWrecked);

        // Nobody extracts at a wreck; the party walks out through an exit.
        var unit = PartyUnit(run);
        NextToVehicle(run, unit);
        Assert.Equal(ScavengeError.NotOnExit, run.Execute(new Extract(1, unit.Id)).Error);
        unit.Pos = run.State.Map.Exits.First(p => run.State.UnitAt(p) is null);
        Assert.True(run.Execute(new Extract(1, unit.Id)).Ok);
        Assert.True(sim.Execute(new ApplyExtractionResult(run.ResultFor(1))).Ok);
        Assert.DoesNotContain(vehicle, sim.State.Vehicles);
        Assert.Equal("Handcart", sim.State.LastReport!.LostVehicle);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.VehicleLost);
    }

    [Fact]
    public void A_noisy_vehicle_draws_more_husks()
    {
        var data = ScavengeTestData.Real;
        var party = new PartySetup(1, [new PartyMember(TestData.Survivor())]);
        int Husks(double multiplier) => NewScavenge.Create(data, "outskirts", 42, [party], new ZoneConditions(HuskMultiplier: multiplier))
            .State.Units.Count(u => u.Kind == UnitKind.Enemy && data.Enemies[u.EnemyDefId!].Ai == AiKind.Husk);
        Assert.True(Husks(1.6) > Husks(1));
    }

    [Fact]
    public void Seats_are_checked_when_a_run_is_made()
    {
        var data = ScavengeTestData.Real;
        var bike = new FieldVehicle("motorbike", "Motorbike", 2, 40, 40, 0, 25, false, Seats: 1);
        var a = TestData.Survivor();
        var b = TestData.Survivor();
        b.Id = 2;
        Assert.Throws<ArgumentException>(() => NewScavenge.Create(data, "outskirts", 1,
            [new PartySetup(1, [new PartyMember(a), new PartyMember(b)]) { Vehicle = bike }]));
    }

    [Fact]
    public void The_way_home_is_driven_and_wears_the_vehicle()
    {
        var sim = Colony(fuel: 100);
        var truck = Give(sim, "truck");
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("suburb_ruins", [LoopTestData.Member(a)], VehicleId: truck.Id)).Ok);
        var leg = sim.State.Expedition!.TravelTicks;
        Assert.Equal((long)Math.Round(sim.Data.Rules.TicksPerDay / 2.0), leg);
        sim.Advance((int)leg);
        var run = sim.OpenExpeditionScavenge();
        var unit = PartyUnit(run);
        NextToVehicle(run, unit);
        Assert.True(run.Execute(new Extract(1, unit.Id)).Ok);
        Assert.True(sim.Execute(new ApplyExtractionResult(run.ResultFor(1))).Ok);
        Assert.Equal(sim.State.Tick + leg, sim.State.Expedition!.ReturnTick);
        sim.Advance((int)leg);
        Assert.Null(sim.State.Expedition);
        Assert.False(truck.Away);
        var wear = sim.VehicleStats(truck).MaxCondition * sim.Data.Vehicles.WearPerTravelDay * 0.5 * 2;
        Assert.Equal(sim.VehicleStats(truck).MaxCondition - wear, truck.Condition, 6);
    }

    [Fact]
    public void Saves_carry_vehicles_and_older_saves_get_a_handcart()
    {
        var (sim, run, vehicle, _) = Mission();
        run.State.Vehicles.Single().Stash.Add("wheel");
        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(TestData.Real, json, ScavengeTestData.Real);
        Assert.Equal(json, SaveGame.Save(loaded.State));
        Assert.Equal(["wheel"], loaded.State.Expedition!.Scavenge!.Vehicles.Single().Stash);
        Assert.True(loaded.FindVehicle(vehicle.Id)!.Away);

        var old = LoopTestData.Colony();
        var v12 = SaveGame.Save(old.State).Replace("\"version\": 15", "\"version\": 14").Replace("\"version\":15", "\"version\":14");
        var migrated = SaveGame.Load(TestData.Real, v12, ScavengeTestData.Real);
        Assert.Equal("handcart", Assert.Single(migrated.State.Vehicles).TierId);
    }

    [Fact]
    public void A_scavenge_save_carries_the_parked_vehicle()
    {
        var (_, run, _, _) = Mission();
        var loaded = ScavengeSave.Load(ScavengeTestData.Real, ScavengeSave.Save(run.State));
        Assert.Equal(run.State.Vehicles.Single().Pos, loaded.State.Vehicles.Single().Pos);
    }
}
