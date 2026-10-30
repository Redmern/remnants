using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

/// <summary>Slots, part quality, wear and repairs, disassembly, the Mechanic perks, wreck salvage and the field upgrades.</summary>
public class VehicleUpgradeTests
{
    private static ColonySim Colony(Dictionary<string, int>? items = null)
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 300, items: items);
        foreach (var r in new[] { "wood", "scrap", "electronics", "cloth", "medicine", "fuel" }) sim.State.Stockpile.Add(r, 500);
        return sim;
    }

    private static Vehicle Give(ColonySim sim, string tier, params string[] upgrades)
    {
        var def = sim.Data.VehicleTiers[tier];
        var v = new Vehicle { Id = sim.State.NextVehicleId++, TierId = tier, Name = def.Name };
        foreach (var u in upgrades) v.Upgrades.Add(FittedUpgrade.Standard(u));
        v.Condition = sim.VehicleStats(v).MaxCondition;
        sim.State.Vehicles.Add(v);
        return v;
    }

    private static Building Garage(ColonySim sim, int level = 3)
    {
        var g = sim.Build("garage");
        g.Level = level;
        return g;
    }

    private static void Know(ColonySim sim) =>
        sim.State.KnownBlueprints.UnionWith(sim.Data.Vehicles.Upgrades.Select(u => u.Schematic).OfType<string>());

    [Fact]
    public void Every_tier_has_slots_the_handcart_one_utility_and_the_rig_the_most()
    {
        var tiers = TestData.Real.Vehicles.Tiers;
        Assert.Equal(new Dictionary<VehicleSlot, int> { [VehicleSlot.Utility] = 1 }, tiers[0].Slots);
        var rig = tiers.Single(t => t.Id == "armored_rig");
        Assert.All(tiers, t => Assert.True(t.Slots.Values.Sum() <= rig.Slots.Values.Sum()));
        Assert.All(TestData.Real.Vehicles.Upgrades, u => Assert.True(tiers.Any(t => t.Slots.GetValueOrDefault(u.Slot) > 0 && t.Tier >= u.MinTier), u.Id));
    }

    [Fact]
    public void Defaults_need_no_schematic_and_everything_else_comes_from_somewhere_besides_the_story()
    {
        var ups = TestData.Real.VehicleUpgrades;
        Assert.Null(ups["cargo_rack"].Schematic);
        Assert.Null(ups["fuel_cans"].Schematic);
        Assert.Null(TestData.Real.Recipes["spare_parts"].Blueprint);
        Assert.All(ups.Values.Where(u => u.Schematic is not null), u => Assert.True(ScavengeTestData.Real.Items[u.Schematic!].LootWeight > 0, u.Id));
    }

    [Fact]
    public void A_full_slot_must_be_emptied_first_and_the_garage_adds_a_utility_slot_at_level_3()
    {
        var sim = Colony(new() { ["spare_parts"] = 10, ["fuel_can"] = 4 });
        Know(sim);
        var garage = Garage(sim, level: 2);
        var bike = Give(sim, "motorbike");
        Assert.Equal(1, VehicleRules.SlotCount(sim, bike, VehicleSlot.Utility));
        Assert.True(sim.Execute(new FitUpgrade(bike.Id, "headlights")).Ok);
        Assert.Equal(CommandError.SlotFull, sim.Execute(new FitUpgrade(bike.Id, "fuel_cans")).Error);
        garage.Level = 3;
        Assert.Equal(2, VehicleRules.SlotCount(sim, bike, VehicleSlot.Utility));
        Assert.True(sim.Execute(new FitUpgrade(bike.Id, "fuel_cans")).Ok);
        // Engine slot: one engine upgrade at a time.
        Assert.True(sim.Execute(new FitUpgrade(bike.Id, "tuned_engine")).Ok);
        Assert.Equal(CommandError.SlotFull, sim.Execute(new FitUpgrade(bike.Id, "muffler")).Error);
        // A handcart has no engine slot at all.
        Assert.Equal(CommandError.UpgradeDoesNotFit, sim.Execute(new FitUpgrade(Give(sim, "handcart").Id, "tuned_engine")).Error);
    }

    [Fact]
    public void Part_quality_scales_the_upgrade_and_a_tuner_adds_to_engines()
    {
        var sim = Colony(new() { ["wheel@fine"] = 4, ["wheel@crude"] = 4, ["spare_parts"] = 6 });
        Know(sim);
        Garage(sim);
        var fine = Give(sim, "buggy");
        var crude = Give(sim, "buggy");
        Assert.True(sim.Execute(new FitUpgrade(fine.Id, "offroad_tyres")).Ok); // best parts first: the fine wheels
        Assert.True(sim.Execute(new FitUpgrade(crude.Id, "offroad_tyres")).Ok);
        Assert.Equal("fine", fine.Upgrades.Single().Quality);
        Assert.Equal("crude", crude.Upgrades.Single().Quality);
        Assert.True(sim.VehicleStats(fine).Speed > sim.VehicleStats(crude).Speed);
        Assert.Equal(3.0 * (1 + 0.05 * 1.25), sim.VehicleStats(fine).Speed, 6);

        var tuner = sim.AddSurvivor();
        tuner.Perks.Add("tuner");
        Assert.True(sim.Execute(new AssignJob(tuner.Id, VehicleRules.Garage(sim)!.Id)).Ok);
        Assert.True(sim.Execute(new FitUpgrade(fine.Id, "tuned_engine")).Ok);
        Assert.Equal(1 + 0.15, fine.Upgrades.Single(u => u.UpgradeId == "tuned_engine").Scale, 6);
    }

    [Fact]
    public void Crafted_parts_take_quality_from_the_mechanics_skill()
    {
        var sim = Colony();
        var garage = Garage(sim);
        var mechanic = sim.AddSurvivor(attributes: 70);
        mechanic.Set(Skill.Mechanics, 95);
        Assert.Equal("mechanics", sim.Data.Buildings["garage"].Job);
        Assert.Equal(Skill.Mechanics, sim.Data.Jobs["mechanics"].Skill);
        Assert.True(sim.Execute(new AssignJob(mechanic.Id, garage.Id)).Ok);
        Assert.True(sim.Execute(new QueueRecipe(garage.Id, "wheel", 6)).Ok);
        sim.Advance(120);
        Assert.Contains(sim.State.Inventory.Counts.Keys, id => id.StartsWith("wheel@fine"));
    }

    [Fact]
    public void Taking_an_upgrade_off_returns_some_parts_at_their_quality()
    {
        var sim = Colony(new() { ["armor_plate@fine"] = 4 });
        Know(sim);
        Garage(sim);
        var truck = Give(sim, "truck");
        Assert.True(sim.Execute(new FitUpgrade(truck.Id, "armor_plates")).Ok);
        Assert.Equal(CommandError.UnknownUpgrade, sim.Execute(new RemoveUpgrade(truck.Id, "winch")).Error);
        Assert.True(sim.Execute(new RemoveUpgrade(truck.Id, "armor_plates")).Ok);
        Assert.Equal(2, sim.State.Inventory.Count("armor_plate@fine"));
        Assert.Empty(truck.Upgrades);
        Assert.True(truck.Condition <= sim.VehicleStats(truck).MaxCondition);
    }

    [Fact]
    public void Worn_parts_break_but_stay_fitted_until_repaired_and_a_grease_monkey_saves_parts()
    {
        var sim = Colony(new() { ["spare_parts"] = 20 });
        Garage(sim);
        var bike = Give(sim, "motorbike", "headlights");
        bike.Upgrades[0].Condition = 0.1;
        Assert.True(VehicleRules.IsBroken(sim.Data, bike.Upgrades[0]));
        Assert.False(sim.VehicleStats(bike).Headlights);
        Assert.Single(bike.Upgrades);

        var plain = VehicleRules.PartsToRepair(sim, bike);
        Assert.Equal(2, plain); // (1 − 0.1) / 0.5, rounded up
        var monkey = sim.AddSurvivor();
        monkey.Perks.Add("grease_monkey");
        Assert.True(sim.Execute(new AssignJob(monkey.Id, VehicleRules.Garage(sim)!.Id)).Ok);
        Assert.Equal(2, VehicleRules.PartsToRepair(sim, bike)); // 1.8 × 0.7 = 1.26 → 2
        bike.Condition = 10;
        Assert.True(VehicleRules.PartsToRepair(sim, bike) < Math.Ceiling(3 + 1.8));
        Assert.True(sim.Execute(new RepairVehicle(bike.Id)).Ok);
        Assert.Equal(1, bike.Upgrades[0].Condition, 6);
        Assert.True(sim.VehicleStats(bike).Headlights);
    }

    [Fact]
    public void The_road_and_fights_wear_the_parts_too()
    {
        var sim = Colony();
        var truck = Give(sim, "truck", "cargo_rack");
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("suburb_ruins", [LoopTestData.Member(a)], VehicleId: truck.Id)).Ok);
        Assert.True(truck.Upgrades[0].Condition < 1);
    }

    [Fact]
    public void Road_upgrades_desert_tyres_and_ram_push_through_trailers_slow_smoke_hides_drone_pads_reach()
    {
        var sim = Colony();
        var bike = Give(sim, "motorbike");
        var desert = Give(sim, "motorbike", "desert_tyres");
        sim.WeatherAt = _ => "dust_storm";
        Assert.NotNull(sim.PlanTravel("suburb_ruins", bike.Id).Blocked);
        var through = sim.PlanTravel("suburb_ruins", desert.Id);
        Assert.Null(through.Blocked);
        Assert.Contains(through.Terms, t => t.Source == "blocked" && Math.Abs(t.SpeedMultiplier - 0.6) < 1e-9);
        sim.WeatherAt = _ => null;

        var truck = Give(sim, "truck");
        var hauling = Give(sim, "truck", "trailer");
        Assert.True(sim.VehicleStats(hauling).CargoKg > sim.VehicleStats(truck).CargoKg);
        Assert.True(sim.VehicleStats(hauling).Speed < sim.VehicleStats(truck).Speed);

        Assert.Equal(0, VehicleRules.ArrivalNoise(sim.Data, Give(sim, "truck", "smoke_launcher")));
        Assert.True(VehicleRules.ArrivalNoise(sim.Data, Give(sim, "truck", "turbo")) > sim.VehicleStats(truck).Noise);
        Assert.True(sim.VehicleStats(Give(sim, "truck", "biofuel_conversion")).FuelPerDay < sim.VehicleStats(truck).FuelPerDay);

        var reach = WorldMap.DroneReachDays(sim);
        Give(sim, "buggy", "drone_pad");
        // The best vehicle so far was a truck (1 day); a buggy (1 day) with a pad (1 more) beats it by a day.
        Assert.Equal(reach + 1, WorldMap.DroneReachDays(sim), 6);
        Assert.Equal(0, VehicleRules.RadioRangeBonus(sim));
        Give(sim, "buggy", "radio_booster");
        Assert.Equal(0.5, VehicleRules.RadioRangeBonus(sim), 6);
    }

    [Fact]
    public void A_medkit_locker_patches_the_party_up_for_the_ride_home()
    {
        var sim = Colony();
        var cart = Give(sim, "handcart", "medkit_locker");
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)], VehicleId: cart.Id)).Ok);
        var result = LoopTestData.Result([LoopTestData.Returned(a, s => s.Health = 50)]);
        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);
        Assert.Equal(65, a.Health, 6);
    }

    [Fact]
    public void Mechanic_perks_and_skill_load()
    {
        var data = TestData.Real;
        Assert.Contains(data.Progression.Skills, s => s.Skill == Skill.Mechanics);
        Assert.Equal(0.3, data.Perks["grease_monkey"].Effects.Hooks[PerkHooks.RepairPartsDiscount], 6);
        Assert.Equal(0.15, data.Perks["tuner"].Effects.Hooks[PerkHooks.EngineTuneBonus], 6);
        Assert.Contains(Skill.Mechanics, data.Progression.Study.Buildings["garage"].Skills);
    }

    // --- in the field ---

    private static ScavengeSim Run(FieldVehicle? vehicle, string zone = "suburb_ruins", ulong seed = 3)
    {
        var s = TestData.Survivor();
        return NewScavenge.Create(ScavengeTestData.Real, zone, seed, [new PartySetup(1, [new PartyMember(s)]) { Vehicle = vehicle }]);
    }

    private static FieldVehicle Field(string tier, params string[] upgrades)
    {
        var data = TestData.Real;
        var v = new Vehicle { TierId = tier, Name = data.VehicleTiers[tier].Name };
        foreach (var u in upgrades) v.Upgrades.Add(FittedUpgrade.Standard(u));
        v.Condition = VehicleRules.Stats(data, v).MaxCondition;
        return VehicleRules.Field(data, v);
    }

    /// <summary>A wreck in the run with the party's unit placed beside it (a wreck is drawn on any map with paired cover).</summary>
    private static (Pos A, Pos B) BesideWreck(ScavengeSim run, Unit unit)
    {
        var map = run.State.Map;
        var wreck = Salvaging.Wrecks(map).FirstOrDefault();
        if (wreck == default)
        {
            var p = map.AllPositions.First(q => map[q] == Terrain.Floor && map.InBounds(q + new Pos(1, 0)) && map[q + new Pos(1, 0)] == Terrain.Floor
                                                && run.State.UnitAt(q) is null && run.State.UnitAt(q + new Pos(1, 0)) is null && !map.IsExit(q));
            map[p] = map[p + new Pos(1, 0)] = Terrain.Cover;
            wreck = Salvaging.Wrecks(map).First(w => w.A == p);
        }
        unit.Pos = Pos.Neighbours8.Select(d => wreck.A + d).First(q => map.InBounds(q) && run.Data.Terrain(map[q]).Passable && run.State.UnitAt(q) is null);
        return wreck;
    }

    [Fact]
    public void Wrecks_are_the_paired_cover_tiles_and_strip_once_for_parts_loudly()
    {
        var run = Run(null);
        var unit = run.State.Units.Single(u => u.PlayerId == 1);
        var wreck = BesideWreck(run, unit);
        unit.Ap = 5;
        var caches = run.State.Caches.Count;
        var r = run.Execute(new Salvage(1, unit.Id, wreck.B));
        Assert.True(r.Ok, r.Message);
        Assert.Equal(3, unit.Ap);
        Assert.Equal(caches + 1, run.State.Caches.Count);
        var parts = run.State.Caches.Last().Items;
        Assert.Equal(2, parts.Count); // loot tier 2: two rolls
        // No vehicle at the edge: nothing too big to carry by hand.
        Assert.DoesNotContain(parts, id => ScavengeTestData.Real.Rules.Salvage.Table.Single(e => id.StartsWith(e.Item)).Big);
        Assert.Contains(run.State.Events, e => e.Type == ScavengeEventType.Salvaged);
        Assert.Equal(ScavengeError.NothingToSalvage, run.Execute(new Salvage(1, unit.Id, wreck.A)).Error);
    }

    [Fact]
    public void A_winch_adds_a_roll()
    {
        var run = Run(Field("truck", "winch"));
        var unit = run.State.Units.Single(u => u.PlayerId == 1);
        var wreck = BesideWreck(run, unit);
        unit.Ap = 5;
        Assert.True(run.Execute(new Salvage(1, unit.Id, wreck.A)).Ok);
        Assert.Equal(3, run.State.Caches.Last().Items.Count);
    }

    [Fact]
    public void The_roof_turret_fires_run_flats_halve_damage_the_workbench_mends_and_solar_charges()
    {
        var run = Run(Field("truck", "roof_turret", "runflat_tyres", "mobile_workbench", "solar_charger"));
        var parked = run.State.Vehicles.Single();
        Assert.True(parked.TurretDamage > 0 && parked.Workbench && parked.SolarCharger);
        var enemy = run.State.Units.First(u => u.Kind == UnitKind.Enemy && u.IsActive);
        foreach (var other in run.State.Units.Where(u => u.Kind == UnitKind.Enemy && u != enemy)) other.Status = UnitStatus.Dead;
        enemy.Pos = run.State.Map.AllPositions.First(p => p.Chebyshev(parked.Pos) == 1 && run.Data.Terrain(run.State.Map[p]).Passable && run.State.UnitAt(p) is null);
        var unit = run.State.Units.Single(u => u.PlayerId == 1);
        unit.Pos = run.State.Map.AllPositions.First(p => p.Chebyshev(parked.Pos) == 1 && run.Data.Terrain(run.State.Map[p]).Passable && run.State.UnitAt(p) is null);
        unit.DroneBattery = 1;
        parked.Hp = 50;
        var enemyHp = enemy.Hp;

        ParkedVehicles.RoundStarted(run);
        Assert.True(enemy.Hp < enemyHp || !enemy.IsActive);
        Assert.Contains(run.State.Events, e => e.Type == ScavengeEventType.VehicleTurret);
        var rules = run.Data.Rules.Vehicle;
        var hit = enemy.IsActive ? Math.Max(rules.MinDamage, rules.DamagePerEnemy - parked.Armor) * parked.DamageMultiplier : 0;
        Assert.Equal(50 + rules.WorkbenchRepairPerRound - hit, parked.Hp, 6);
        Assert.Equal(0.5, parked.DamageMultiplier, 6);
        Assert.Null(unit.DroneBattery);
    }

    [Fact]
    public void Only_a_workbench_lets_items_back_out_of_the_stash()
    {
        foreach (var (upgrades, ok) in new[] { (Array.Empty<string>(), false), (new[] { "mobile_workbench" }, true) })
        {
            var run = Run(Field("buggy", upgrades));
            var parked = run.State.Vehicles.Single();
            parked.Stash.Add("bandage");
            var unit = run.State.Units.Single(u => u.PlayerId == 1);
            unit.Pos = run.State.Map.AllPositions.First(p => p.Chebyshev(parked.Pos) == 1 && run.Data.Terrain(run.State.Map[p]).Passable && run.State.UnitAt(p) is null);
            unit.Ap = 5;
            Assert.Equal(ok, run.Execute(new TakeFromStash(1, unit.Id, "bandage")).Ok);
        }
    }
}
