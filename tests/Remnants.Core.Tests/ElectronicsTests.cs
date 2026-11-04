using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// The electronics data: components, devices, upgrades and disassembly at the Lab, the schematics adapter, the new perks.
/// </summary>
public class ElectronicsDataTests
{
    [Fact]
    public void The_component_chain_and_devices_are_lab_recipes()
    {
        var data = TestData.Real;
        Assert.NotNull(data.Electronics);
        foreach (var id in new[] { "wiring", "circuit_board", "antenna", "sensor_module", "microchip", "network_chip", "medic_drone", "cargo_drone",
                     "combat_drone", "network_deck", "scanner_mk2", "scanner_mk3", "jammer_mk2", "emp_grenade", "holo_decoy", "squad_radio", "rangefinder" })
            Assert.Equal("electronics_lab", data.Recipes[id].Building);
        Assert.True(data.Recipes["microchip"].NeedsPower);
        Assert.False(data.Recipes["wiring"].NeedsPower);
        Assert.Contains("power_cell", data.Recipes["holo_decoy"].ItemInputs.Keys);  // the cybernetics battery is the basic battery
        Assert.True(Real.Items["network_chip"].NetworkGrade);
    }

    [Fact]
    public void The_default_set_is_open_and_advanced_tech_is_behind_schematics()
    {
        var data = TestData.Real;
        var catalog = data.Schematics;
        foreach (var id in new[] { "wiring", "circuit_board", "power_cell", "flare", "motion_sensor", "hacking_kit", "handheld_radio", "noise_decoy" })
            Assert.Null(catalog.GateOfRecipe(id));
        Assert.Null(catalog.GateOfBuildingLevel("fuel_generator", 1));
        Assert.Equal("sch_solar_array", catalog.GateOfBuildingLevel("solar_array", 1));
        Assert.Equal("sch_emp_grenade", catalog.GateOfRecipe("emp_grenade"));
        Assert.Equal("sch_scanner_mk2", catalog.GateOfRecipe("scanner_mk2"));
        Assert.Equal(ItemKind.Schematic, Real.Items["sch_network_deck"].Kind);
        Assert.True(catalog.All["sch_network_deck"].Encrypted);
        foreach (var s in catalog.All.Values.Where(s => s.Id is "sch_network_deck" or "sch_emp_grenade" or "sch_medic_drone"))
            Assert.Contains(s.Sources, src => src != Remnants.Core.Schematics.SchematicSource.Story);  // nothing is story-only
    }

    [Fact]
    public void Every_listed_device_strips_into_components()
    {
        var data = TestData.Real;
        var strip = data.Recipes["disassemble_scanner"];
        Assert.Equal("Disassembly", strip.Group);
        Assert.Equal(1, strip.ItemInputs["scanner"]);
        Assert.Equal("sensor_module", strip.Item);
        Assert.Equal(2, strip.Byproducts["circuit_board"]);
        Assert.True(strip.ResearchChance > 0);
        Assert.Contains(data.Recipes.Values, r => r.Id == "disassemble_linked_circuitry" && r.Item == "microchip");
    }

    [Fact]
    public void The_new_tech_perks_do_not_replace_the_workshop_tinkerer()
    {
        var data = TestData.Real;
        Assert.Equal(0.25, data.Perks["circuit_tinkerer"].Effects.Hooks[PerkHooks.DisassemblyYieldBonus]);
        Assert.True(data.Perks["electrician"].Effects.Hooks[PerkHooks.PowerEfficiency] > 0);
        Assert.Contains("crafting", data.Perks["tinkerer"].Effects.CraftQuality.Keys);
        Assert.Equal("tech", data.Perks["electrician"].Discipline);
    }

    [Fact]
    public void Device_quality_scales_radius_battery_and_radio_range()
    {
        Assert.Equal(10, Real.Items["scanner_mk2@fine"].Device!.Radius);
        Assert.Equal(6, Real.Items["scanner_mk2@crude"].Device!.Radius);
        Assert.True(Real.Items["medic_drone@fine"].Drone!.Battery > Real.Items["medic_drone"].Drone!.Battery);
        Assert.True(Real.Items["squad_radio@fine"].Radio!.Range > Real.Items["squad_radio"].Radio!.Range);
        Assert.Equal(DroneRole.Medic, Real.Items["medic_drone@crude"].Drone!.Role);
    }
}

/// <summary>The colony grid: opt-in, fuel and solar, batteries, priorities, brownouts and the breaker, bonuses, charging.</summary>
public class PowerTests
{
    private static ColonySim Colony(bool nights = false, Dictionary<string, double>? stock = null)
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = 2 });
        foreach (var (res, amount) in stock ?? new() { ["wood"] = 200, ["electronics"] = 50, ["scrap"] = 50 }) state.Stockpile.Add(res, amount);
        if (nights) state.Tick = 12;
        return new ColonySim(nights ? TestData.RealWithNights : TestData.Real, state, Real);
    }

    /// <summary>Ticks until the clock shows this hour.</summary>
    private static int HoursUntil(ColonySim sim, int hour) =>
        ((hour - DayNight.Hour(sim.State.Tick, sim.Data.Rules)) % sim.Data.Rules.TicksPerDay + sim.Data.Rules.TicksPerDay) % sim.Data.Rules.TicksPerDay;

    private static Survivor Worker(ColonySim sim, Building at, double skill = 40)
    {
        var s = sim.AddSurvivor();
        s.Set(Skill.Electronics, skill);
        Assert.True(sim.Execute(new AssignJob(s.Id, at.Id)).Ok);
        return s;
    }

    [Fact]
    public void Without_a_generator_nothing_is_powered_and_the_lab_still_works()
    {
        var sim = Colony();
        var lab = sim.Build("electronics_lab");
        Worker(sim, lab);
        Assert.True(sim.Execute(new QueueRecipe(lab.Id, "wiring", 3)).Ok);
        sim.Advance(10);
        Assert.False(sim.IsPowered(lab));
        Assert.True(sim.State.Inventory.Count("wiring") >= 2);
        Assert.DoesNotContain(sim.State.Events, e => e.Type is ColonyEventType.Brownout or ColonyEventType.BreakerTripped);
    }

    [Fact]
    public void A_fuel_generator_feeds_a_worked_lab_burns_wood_and_speeds_it_up()
    {
        var plain = Colony();
        var lab0 = plain.Build("electronics_lab");
        Worker(plain, lab0);
        plain.Execute(new QueueRecipe(lab0.Id, "circuit_board", Repeat: true));
        plain.State.Inventory.Add("wiring", 50);

        var sim = Colony();
        var lab = sim.Build("electronics_lab");
        var gen = sim.Build("fuel_generator");
        Worker(sim, lab);
        sim.Execute(new QueueRecipe(lab.Id, "circuit_board", Repeat: true));
        sim.State.Inventory.Add("wiring", 50);
        var wood = sim.State.Stockpile.Get("wood");

        plain.Advance(1);
        sim.Advance(1);
        Assert.True(sim.IsPowered(lab));
        Assert.Equal(2, sim.State.Power.Used, 3);
        Assert.Equal(2, sim.State.Power.Output[gen.Id], 3);
        Assert.Equal(wood - 2 * 0.15, sim.State.Stockpile.Get("wood"), 6);
        Assert.Equal(plain.State.FindBuilding(lab0.Id)!.Queue[0].Progress * 1.25, lab.Queue[0].Progress, 6);
    }

    [Fact]
    public void An_idle_lab_draws_nothing()
    {
        var sim = Colony();
        var lab = sim.Build("electronics_lab");
        sim.Build("fuel_generator");
        sim.Advance(2);
        Assert.Equal(0, sim.State.Power.Demand);
        Assert.False(sim.IsPowered(lab));
    }

    [Fact]
    public void Tier_three_parts_wait_for_power()
    {
        var sim = Colony(stock: new() { ["wood"] = 200, ["electronics"] = 50, ["scrap"] = 50 });
        var lab = sim.Build("electronics_lab");
        lab.Level = 3;
        Worker(sim, lab, 50);
        sim.State.Schematics.Known.Add("sch_microchip");
        sim.State.Inventory.Add("circuit_board", 5);
        Assert.True(sim.Execute(new QueueRecipe(lab.Id, "microchip")).Ok);
        sim.Advance(30);
        Assert.Equal(0, sim.State.Inventory.Count("microchip"));
        Assert.Equal(CraftStatus.NeedsPower, sim.CraftStatus(lab));

        var gen = sim.Build("fuel_generator");
        sim.Advance(30);
        Assert.Equal(1, sim.State.Inventory.Count("microchip"));
        Assert.True(gen.Id > 0);
    }

    [Fact]
    public void Solar_works_by_day_and_a_battery_carries_it_into_the_night()
    {
        var sim = Colony(nights: true);
        sim.State.Schematics.Known.Add("sch_solar_array");
        sim.State.Schematics.Known.Add("sch_battery_bank");
        var solar = sim.Build("solar_array");
        sim.Build("battery_bank");
        sim.Advance(1);  // 12:00
        Assert.Equal(3, sim.State.Power.Output[solar.Id], 6);
        Assert.True(sim.State.Power.Stored > 0, "nothing draws: spare solar charges the bank");
        // Run into the night: the bank is full of the day's sun and solar makes nothing.
        sim.Advance(HoursUntil(sim, 22));
        Assert.Equal(0, sim.State.Power.Output[solar.Id], 6);
        var stored = sim.State.Power.Stored;
        Assert.True(stored >= 19, $"stored {stored}");
        // A night shift at the lab runs on the battery.
        var lab = sim.Build("electronics_lab");
        Worker(sim, lab);
        sim.Execute(new SetNightShift(lab.Id, true));
        sim.Execute(new QueueRecipe(lab.Id, "wiring", Repeat: true));
        sim.Advance(1);
        Assert.True(sim.IsPowered(lab));
        Assert.Equal(stored - 2, sim.State.Power.Stored, 6);
    }

    [Fact]
    public void A_brownout_sheds_the_lowest_priority_consumer_first()
    {
        var sim = Colony();
        var bay = sim.Build("med_bay");
        bay.Level = 3;
        var lab = sim.Build("electronics_lab");
        lab.Level = 3;  // draws 4
        sim.Build("fuel_generator");  // 6
        Worker(sim, lab);
        var medic = sim.AddSurvivor();
        sim.Execute(new AssignJob(medic.Id, bay.Id));
        sim.Execute(new QueueRecipe(lab.Id, "wiring", Repeat: true));
        sim.Advance(1);
        Assert.True(sim.IsPowered(bay));
        Assert.True(sim.IsPowered(lab));
        Assert.False(sim.State.Power.Brownout);

        // A second lab-sized draw: switch the generator down a level's worth by running out of wood.
        sim.State.Stockpile.Amounts["wood"] = 0.6;  // 4 power of wood at 0.15 per power
        sim.Advance(1);
        Assert.True(sim.State.Power.Brownout);
        Assert.True(sim.IsPowered(bay), "the Med Bay comes first");
        Assert.False(sim.IsPowered(lab), "the lab (4) no longer fits after the bay (2)");
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Brownout);
    }

    [Fact]
    public void A_sustained_overload_trips_the_breaker()
    {
        var sim = Colony(stock: new() { ["wood"] = 0.3, ["electronics"] = 50, ["scrap"] = 50 });
        var lab = sim.Build("electronics_lab");
        lab.Level = 3;
        sim.State.Stockpile.Amounts["wood"] = 0.3;  // 2 power of wood: 4 wanted is twice that
        sim.Build("fuel_generator");
        sim.State.Stockpile.Amounts["wood"] = 0.3;
        Worker(sim, lab);
        sim.Execute(new QueueRecipe(lab.Id, "wiring", Repeat: true));
        var rules = sim.Data.Electronics!.Power;
        for (var i = 0; i < rules.OverloadTicks; i++)
        {
            sim.State.Stockpile.Amounts["wood"] = 0.3;
            sim.Advance(1);
        }
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.BreakerTripped);
        Assert.True(sim.State.Power.BreakerUntil > sim.State.Tick - 1);
    }

    [Fact]
    public void Switching_a_generator_off_cuts_the_grid()
    {
        var sim = Colony();
        var lab = sim.Build("electronics_lab");
        var gen = sim.Build("fuel_generator");
        var house = sim.Build("shelter");
        Worker(sim, lab);
        sim.Execute(new QueueRecipe(lab.Id, "wiring", Repeat: true));
        Assert.True(sim.Execute(new SetPower(gen.Id, false)).Ok);
        sim.Advance(1);
        Assert.False(sim.IsPowered(lab));
        Assert.Equal(CommandError.NotOnGrid, sim.Execute(new SetPower(house.Id, false)).Error);
        Assert.True(sim.Execute(new SetPower(gen.Id, true)).Ok);
        sim.Advance(1);
        Assert.True(sim.IsPowered(lab));
        Assert.Contains(sim.PowerNodes(), n => n.BuildingId == gen.Id && n.Role == PowerRole.Generator && n.Fed);
    }

    [Fact]
    public void An_electrician_at_home_cuts_every_draw()
    {
        var sim = Colony();
        var lab = sim.Build("electronics_lab");
        sim.Build("fuel_generator");
        Worker(sim, lab);
        sim.Execute(new QueueRecipe(lab.Id, "wiring", Repeat: true));
        var sparky = sim.AddSurvivor();
        sparky.Perks.Add("electrician");
        sim.Advance(1);
        Assert.Equal(2 * (1 - 0.2), sim.State.Power.Demand, 6);
        sparky.Away = true;
        sim.Advance(1);
        Assert.Equal(2, sim.State.Power.Demand, 6);
    }

    [Fact]
    public void A_powered_level_three_med_bay_improves_surgery_odds()
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = 3 });
        state.Stockpile.Add("wood", 100);
        var sim = new ColonySim(TestData.Real, state, Real);
        var bay = sim.Build("med_bay");
        bay.Level = 3;
        var medic = sim.AddSurvivor();
        medic.Set(Skill.Medicine, 40);
        sim.Execute(new AssignJob(medic.Id, bay.Id));
        var before = Cybernetics.SuccessChance(sim, bay, 2, false);
        sim.Build("fuel_generator");
        sim.Advance(1);
        Assert.True(sim.IsPowered(bay));
        Assert.Equal(before + 0.05, Cybernetics.SuccessChance(sim, bay, 2, false), 6);
    }

    [Fact]
    public void A_powered_floodlight_lifts_the_night_shift()
    {
        var sim = Colony(nights: true);
        var cutter = sim.Build("woodcutter");
        cutter.Floodlight = true;
        var w = sim.AddSurvivor();
        sim.Execute(new AssignJob(w.Id, cutter.Id));
        sim.Execute(new SetNightShift(cutter.Id, true));
        sim.Advance(HoursUntil(sim, 21));
        var unlit = Schedule.Crew(sim.State, sim.Data, cutter).Single().Factor;
        sim.Build("fuel_generator");
        sim.Advance(1);
        Assert.Contains(cutter.Id, sim.State.Power.Lit);
        Assert.Equal(unlit * 1.15, Schedule.Crew(sim.State, sim.Data, cutter).Single().Factor, 6);
    }

    [Fact]
    public void Schematic_buildings_need_their_schematic_studied()
    {
        var sim = Colony(stock: new() { ["stone"] = 100, ["scrap"] = 100, ["electronics"] = 100, ["wood"] = 100 });
        Assert.Equal(CommandError.SchematicNeeded, sim.Execute(new Construct("solar_array")).Error);
        Assert.True(Remnants.Core.Schematics.Schematics.Grant(sim, "sch_solar_array", Remnants.Core.Schematics.SchematicSource.Loot).Learned);
        Assert.True(sim.Execute(new Construct("solar_array")).Ok);
    }

    [Fact]
    public void Drained_devices_charge_on_the_bench_faster_with_power()
    {
        var slow = Colony();
        var fast = Colony();
        fast.Build("fuel_generator");
        foreach (var sim in new[] { slow, fast }) sim.State.Charging.Add(new ChargingItem { ItemId = "motion_sensor_mk2", Work = 9 });
        fast.Advance(9);
        slow.Advance(9);
        Assert.Equal(1, fast.State.Inventory.Count("motion_sensor_mk2"));
        Assert.Empty(fast.State.Charging);
        Assert.Contains(fast.State.Events, e => e.Type == ColonyEventType.DeviceCharged);
        Assert.Single(slow.State.Charging);
        Assert.Equal(9 * 0.2, slow.State.Charging[0].Done, 6);
        Assert.NotNull(Power.ChargeEta(slow.State, slow.Data, slow.State.Charging[0]));
    }

    [Fact]
    public void The_grid_and_the_charging_bench_survive_a_save_and_old_saves_load_without_them()
    {
        var sim = Colony();
        var gen = sim.Build("fuel_generator");
        sim.Execute(new SetPower(gen.Id, false));
        sim.State.Charging.Add(new ChargingItem { ItemId = "motion_sensor_mk2", Work = 6, Done = 2 });
        var loaded = SaveGame.Load(sim.Data, SaveGame.Save(sim.State), Real).State;
        Assert.Contains(gen.Id, loaded.Power.Off);
        Assert.Equal(2, loaded.Charging.Single().Done);

        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveGame.Save(sim.State))!;
        json["colony"]!.AsObject().Remove("power");
        json["colony"]!.AsObject().Remove("charging");
        var old = SaveGame.Load(sim.Data, json.ToJsonString(), Real).State;
        Assert.Empty(old.Power.Off);
        Assert.Empty(old.Charging);
    }
}

/// <summary>Disassembly at the bench: components out, Circuit Tinkerer and skill for more, a chance of Research Data.</summary>
public class DisassemblyTests
{
    private static (ColonySim Sim, Building Lab, Survivor Worker) Bench(double skill, params string[] perks)
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = 2 });
        state.AddFood(1000);
        var sim = new ColonySim(TestData.Real, state, Real);
        sim.Build("shelter");
        var lab = sim.Build("electronics_lab");
        var w = sim.AddSurvivor();
        w.Set(Skill.Electronics, skill);
        foreach (var p in perks) w.Perks.Add(p);
        sim.Execute(new AssignJob(w.Id, lab.Id));
        return (sim, lab, w);
    }

    [Fact]
    public void Stripping_a_scanner_gives_its_components()
    {
        var (sim, lab, _) = Bench(0);
        sim.State.Inventory.Add("scanner@crude");
        Assert.True(sim.Execute(new QueueRecipe(lab.Id, "disassemble_scanner")).Ok);
        sim.Advance(20);
        Assert.Equal(0, sim.State.Inventory.Count("scanner@crude"));
        Assert.Equal(1, sim.State.Inventory.Count("sensor_module"));
        Assert.Equal(2, sim.State.Inventory.Count("circuit_board"));
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Disassembled);
    }

    [Fact]
    public void A_circuit_tinkerer_gets_more_out_of_the_same_junk()
    {
        int Yield(params string[] perks)
        {
            var (sim, lab, _) = Bench(0, perks);
            sim.State.Inventory.Add("broken_gadget", 40);
            sim.Execute(new QueueRecipe(lab.Id, "disassemble_broken_gadget", Repeat: true));
            for (var i = 0; i < 5000 && sim.State.Inventory.Count("broken_gadget") > 0; i++) sim.Tick();
            sim.Advance(50);
            Assert.Equal(0, sim.State.Inventory.Count("broken_gadget"));
            return sim.State.Inventory.Count("wiring") + sim.State.Inventory.Count("circuit_board");
        }
        var plain = Yield();
        var tinkerer = Yield("circuit_tinkerer");
        Assert.InRange(plain, 80, 86);  // a little from the skill the work trains
        Assert.InRange(tinkerer, plain + 8, 120);
    }

    [Fact]
    public void Stripping_linked_cores_turns_up_research_data()
    {
        var (sim, lab, _) = Bench(0);
        sim.State.Inventory.Add("linked_core", 30);
        sim.Execute(new QueueRecipe(lab.Id, "disassemble_linked_core", Repeat: true));
        for (var i = 0; i < 10000 && sim.State.Inventory.Count("linked_core") > 0; i++) sim.Tick();
        sim.Advance(80);
        Assert.InRange(sim.State.Inventory.Count("linked_circuitry"), 60, 66);
        Assert.InRange(sim.State.Stockpile.Get(sim.Data.Schematics.Rules.ResearchDataResource), 1, 15);  // a 20% chance over 30 batches
    }
}

/// <summary>Field devices: charges, the EMP, decoys, radios, the rangefinder and the Network deck's trace.</summary>
public class FieldDeviceTests
{
    [Fact]
    public void A_sensor_kit_has_charges_and_goes_home_drained()
    {
        var u = SurvivorUnit(1, new Pos(2, 2), pack: ["motion_sensor_mk2"]);
        u.Skills[Skill.Firearms] = 0;
        var sim = Arena(12, 8, [u]);
        for (var i = 0; i < 3; i++)
        {
            u.Ap = 5;
            Assert.True(sim.Execute(new UseItem(1, u.Id, "motion_sensor_mk2", At: new Pos(1 + i, 3))).Ok);
        }
        Assert.Contains("motion_sensor_mk2", u.Pack);
        Assert.Equal(0, ElectronicWarfare.ChargesLeft(Real, u, "motion_sensor_mk2"));
        Assert.Equal(ScavengeError.NotUsable, sim.Execute(new UseItem(1, u.Id, "motion_sensor_mk2", At: new Pos(4, 4))).Error);
        Assert.Equal(3, sim.State.Sensors.Count);

        u.Status = UnitStatus.Extracted;
        sim.UpdateParties();
        var result = sim.ResultFor(1);
        Assert.Equal(3, result.Returned.Single().ChargesUsed!["motion_sensor_mk2"]);
    }

    [Fact]
    public void Drained_kits_come_home_onto_the_charging_bench()
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = 2 });
        state.AddFood(100);
        var sim = new ColonySim(TestData.Real, state, Real);
        var s = sim.AddSurvivor();
        state.Inventory.Add("motion_sensor_mk2", 2);
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [new ExpeditionMember(s.Id, Pack: ["motion_sensor_mk2", "motion_sensor_mk2"])])).Ok,
            "launch");
        sim.Advance((int)(state.Expedition!.ArrivalTick - state.Tick));
        var scav = sim.OpenExpeditionScavenge();
        var unit = scav.State.Units.First(u => u.Kind == UnitKind.Survivor);
        unit.ChargesUsed["motion_sensor_mk2"] = 4;
        unit.Status = UnitStatus.Extracted;
        scav.UpdateParties();
        var r = sim.Execute(new ApplyExtractionResult(scav.ResultFor(ColonySim.ExpeditionPlayerId)));
        Assert.True(r.Ok, r.Message);
        sim.Advance((int)Math.Max(0, (state.Expedition?.ReturnTick ?? state.Tick) - state.Tick) + 1);
        Assert.Equal(2, state.Charging.Count + state.Inventory.Count("motion_sensor_mk2"));
        Assert.True(state.Charging.Count >= 1, "both kits were drained (3 + 1 charges)");
    }

    [Fact]
    public void An_emp_knocks_out_the_linked_drones_and_cameras_but_not_husks()
    {
        var thrower = SurvivorUnit(1, new Pos(1, 4), pack: ["emp_grenade"]);
        var op = SurvivorUnit(1, new Pos(1, 6), pack: ["scout_drone"]);
        var linked = EnemyUnit("linked", new Pos(5, 4));
        var husk = EnemyUnit("husk", new Pos(5, 5));
        ZoneDevice camera = null!;
        var sim = Arena(12, 10, [thrower, op, linked, husk], setup: (d, s) => camera = Hacking.Place(d, s, DeviceKind.Camera, new Pos(5, 2)));
        // The operator's drone is out near the blast.
        sim.State.TurnOrder.Remove(op.Id);
        sim.State.TurnOrder.Insert(0, op.Id);
        Assert.True(sim.Execute(new DeployDrone(1, op.Id, new Pos(2, 5))).Ok);
        Assert.True(sim.Execute(new FlyDrone(1, op.Id, new Pos(4, 5))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, op.Id)).Ok);
        while (sim.State.CurrentUnitId != thrower.Id) sim.Execute(new EndTurn(1, sim.State.CurrentUnitId!.Value));

        thrower.Ap = 5;
        Assert.True(sim.Execute(new UseItem(1, thrower.Id, "emp_grenade", At: new Pos(5, 4))).Ok);
        Assert.DoesNotContain("emp_grenade", thrower.Pack);
        Assert.True(Devices.IsJammed(sim, linked), "off the relay");
        Assert.True(linked.EmpUntilRound >= sim.State.Round);
        Assert.True(sim.State.FindUnit(camera.UnitId!.Value)!.Status == UnitStatus.Dead);
        Assert.Null(op.DroneId);
        Assert.Contains(sim.State.Caches, c => c.Pos == new Pos(4, 5) && c.Items.Contains("scout_drone"));
        Assert.Equal(0, husk.EmpUntilRound);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.EmpBurst);
    }

    [Fact]
    public void A_stunned_linked_loses_its_turn()
    {
        var thrower = SurvivorUnit(1, new Pos(1, 4), pack: ["emp_grenade"]);
        var linked = EnemyUnit("linked", new Pos(5, 4));
        var sim = Arena(12, 10, [thrower, linked]);
        Assert.True(sim.Execute(new UseItem(1, thrower.Id, "emp_grenade", At: new Pos(5, 4))).Ok);
        var pos = linked.Pos;
        var hp = thrower.Hp;
        Assert.True(sim.Execute(new EndTurn(1, thrower.Id)).Ok);  // the Linked's turn passes stunned
        Assert.Equal(pos, linked.Pos);
        Assert.Equal(hp, thrower.Hp);
    }

    [Fact]
    public void A_holo_decoy_draws_the_husks_and_flickers_out()
    {
        var u = SurvivorUnit(1, new Pos(1, 1), pack: ["holo_decoy"]);
        var husk = EnemyUnit("husk", new Pos(9, 6));
        var sim = Arena(12, 8, [u, husk]);
        Assert.True(sim.Execute(new UseItem(1, u.Id, "holo_decoy", At: new Pos(4, 4))).Ok);
        var decoy = sim.State.Units.Single(x => x.Kind == UnitKind.Decoy);
        Assert.Equal(u.Side, decoy.Side);
        Assert.False(decoy.PlayerControlled);
        Assert.Equal(AiMode.Hunting, husk.Ai.Mode);
        Assert.Equal(new Pos(4, 4), husk.Ai.LastKnown);
        Assert.Equal(ScavengeError.ItemNotCarried, sim.Execute(new UseItem(1, u.Id, "holo_decoy", At: new Pos(5, 4))).Error);
        for (var i = 0; i < 3 && !sim.State.Finished; i++) Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.NotEqual(UnitStatus.Active, decoy.Status);
        Assert.DoesNotContain(decoy.Id, sim.State.TurnOrder);
    }

    [Fact]
    public void A_noise_decoy_sends_idle_enemies_to_the_tile()
    {
        var u = SurvivorUnit(1, new Pos(1, 1), pack: ["noise_decoy"]);
        var husk = EnemyUnit("husk", new Pos(9, 6));
        var far = EnemyUnit("husk", new Pos(11, 0));
        var sim = Arena(30, 8, [u, husk, far]);
        far.Pos = new Pos(29, 0);
        Assert.True(sim.Execute(new UseItem(1, u.Id, "noise_decoy", At: new Pos(6, 6))).Ok);
        Assert.Equal(new Pos(6, 6), husk.Ai.LastKnown);
        Assert.Equal(AiMode.Idle, far.Ai.Mode);
    }

    [Fact]
    public void Two_radios_on_a_side_coordinate_fire_and_add_pings()
    {
        var a = SurvivorUnit(1, new Pos(1, 1), weapon: "pistol", pack: ["squad_radio"]);
        var b = SurvivorUnit(1, new Pos(2, 1), pack: ["handheld_radio"]);
        var linked = EnemyUnit("husk", new Pos(5, 1));
        var sim = Arena(12, 8, [a, b, linked]);
        var shot = sim.Profile(a).Actions.First(x => x.Pattern != AttackPattern.Melee);
        Assert.Equal(0.05, ElectronicWarfare.RadioAccuracy(sim, a), 6);
        b.Pack.Clear();
        Assert.Equal(0, ElectronicWarfare.RadioAccuracy(sim, a));
        Assert.Equal(Real.Rules.Coop.PingsPerTurn + 2, Real.Rules.Coop.PingsPerTurn + ElectronicWarfare.ExtraPings(Real, a));
        Assert.NotNull(shot);
    }

    [Fact]
    public void A_squad_radio_on_overwatch_fires_at_what_a_teammate_sees()
    {
        // The watcher is half-blind (no perception) but a teammate with good eyes calls the target in.
        var watcher = SurvivorUnit(1, new Pos(1, 4), attributes: 50, weapon: "pistol", pack: ["squad_radio"]);
        watcher.Attributes[AttributeKind.Perception] = 0;
        var spotter = SurvivorUnit(1, new Pos(1, 5), attributes: 90, tool: "binoculars");
        var linked = EnemyUnit("linked", new Pos(7, 4));
        var sim = Arena(14, 10, [watcher, spotter, linked]);
        Assert.True(ElectronicWarfare.SharesOverwatch(sim, watcher, linked) == Detection.SideSees(Real, sim.State, watcher.Side, linked));
        watcher.Pack.Clear();
        Assert.False(ElectronicWarfare.SharesOverwatch(sim, watcher, linked));
    }

    [Fact]
    public void A_rangefinder_steadies_ranged_shots_only()
    {
        var plain = SurvivorUnit(1, new Pos(1, 1), weapon: "pistol");
        var ranged = SurvivorUnit(1, new Pos(1, 1), weapon: "pistol", tool: "rangefinder");
        var p0 = ProfileDeriver.Derive(Real, plain).Actions.First(a => a.Pattern != AttackPattern.Melee);
        var p1 = ProfileDeriver.Derive(Real, ranged).Actions.First(a => a.Pattern != AttackPattern.Melee);
        Assert.Equal(p0.Accuracy + 0.06, p1.Accuracy, 6);
        var m0 = ProfileDeriver.Derive(Real, plain).Actions.First(a => a.Pattern == AttackPattern.Melee);
        var m1 = ProfileDeriver.Derive(Real, ranged).Actions.First(a => a.Pattern == AttackPattern.Melee);
        Assert.Equal(m0.Accuracy, m1.Accuracy, 6);
    }

    [Fact]
    public void A_network_deck_hacks_hard_but_can_be_traced()
    {
        var always = new ScavengeData(Real.Items.Values.Where(i => i.BaseId is null)
                .Select(i => i.Id == "network_deck" ? i with { HackTool = i.HackTool! with { TraceChance = 1 } } : i),
            Real.Enemies.Values, Real.Zones.Values, Real.Rules, Real.PerkHooks, Real.Cybernetics?.Def, Real.Schematics, Real.Electronics);
        var hacker = SurvivorUnit(1, new Pos(1, 1), tool: "network_deck");
        var linked = EnemyUnit("linked", new Pos(10, 6));
        ZoneDevice terminal = null!;
        var sim = Arena(12, 8, [hacker, linked], data: always, setup: (d, s) => terminal = Hacking.Place(d, s, DeviceKind.Terminal, new Pos(4, 1)));
        Assert.True(sim.Execute(new Hack(1, hacker.Id, terminal.Id)).Ok);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Traced);
        Assert.Equal(AiMode.Hunting, linked.Ai.Mode);
        Assert.Equal(hacker.Id, linked.Ai.TargetId);
        Assert.True(Hacking.Tool(Real, hacker)!.HackTool!.Range >= 4);
    }
}

public class ElectronicsSaveTests
{
    [Fact]
    public void A_run_keeps_its_decoys_charges_stuns_and_secured_cargo_and_older_runs_load_without_them()
    {
        var u = SurvivorUnit(1, new Pos(1, 1), pack: ["holo_decoy", "motion_sensor_mk2"]);
        var linked = EnemyUnit("linked", new Pos(8, 6));
        var sim = Arena(12, 8, [u, linked]);
        Assert.True(sim.Execute(new UseItem(1, u.Id, "holo_decoy", At: new Pos(3, 3))).Ok);
        Assert.True(sim.Execute(new UseItem(1, u.Id, "motion_sensor_mk2", At: new Pos(2, 2))).Ok);
        linked.EmpUntilRound = 4;
        sim.State.Parties[0].Secured.Add("rifle");

        var back = ScavengeSave.Load(Real, ScavengeSave.Save(sim.State)).State;
        Assert.Equal(UnitKind.Decoy, back.Units.Single(x => x.Kind == UnitKind.Decoy).Kind);
        Assert.Equal(1, back.FindUnit(u.Id)!.ChargesUsed["motion_sensor_mk2"]);
        Assert.Equal(4, back.FindUnit(linked.Id)!.EmpUntilRound);
        Assert.Equal(["rifle"], back.Parties[0].Secured);

        var plain = Arena(12, 8, [SurvivorUnit(1, new Pos(1, 1)), EnemyUnit("husk", new Pos(8, 6))]);
        var old = ScavengeSave.Save(plain.State).Replace($"\"version\": {ScavengeSave.Version}", "\"version\": 7");
        Assert.Contains("Unsupported scavenge save version", Assert.Throws<System.Text.Json.JsonException>(() => ScavengeSave.Load(Real, old)).Message); // old runs reset
    }
}

/// <summary>The drone variants: medic, cargo and combat.</summary>
public class DroneRoleTests
{
    private static Unit Launch(ScavengeSim sim, Unit op, Pos at)
    {
        var r = sim.Execute(new DeployDrone(op.PlayerId!.Value, op.Id, at));
        Assert.True(r.Ok, r.Message);
        return sim.State.FindUnit(op.DroneId!.Value)!;
    }

    [Fact]
    public void A_medic_drone_bandages_a_hurt_teammate_at_range()
    {
        var op = SurvivorUnit(1, new Pos(1, 1), pack: ["medic_drone", "bandage"]);
        var hurt = SurvivorUnit(1, new Pos(6, 1));
        hurt.Hp = 50;
        var sim = Arena(12, 8, [op, hurt]);
        var drone = Launch(sim, op, new Pos(2, 1));
        Assert.Equal(12, drone.MaxHp);
        op.Ap = 9;
        Assert.Equal(ScavengeError.TooFar, sim.Execute(new DroneHeal(1, op.Id, hurt.Id)).Error);
        Assert.True(sim.Execute(new FlyDrone(1, op.Id, new Pos(5, 1))).Ok);
        Assert.True(sim.Execute(new DroneHeal(1, op.Id, hurt.Id)).Ok);
        Assert.Equal(60, hurt.Hp, 6);
        Assert.DoesNotContain("bandage", op.Pack);
        Assert.Equal(ScavengeError.ItemNotCarried, sim.Execute(new DroneHeal(1, op.Id, hurt.Id)).Error);
    }

    [Fact]
    public void A_scout_drone_cannot_heal()
    {
        var op = SurvivorUnit(1, new Pos(1, 1), pack: ["scout_drone", "bandage"]);
        var hurt = SurvivorUnit(1, new Pos(3, 1));
        var sim = Arena(12, 8, [op, hurt]);
        Launch(sim, op, new Pos(2, 1));
        Assert.Equal(ScavengeError.NoDrone, sim.Execute(new DroneHeal(1, op.Id, hurt.Id)).Error);
    }

    [Fact]
    public void A_cargo_drone_ferries_loot_out_and_it_is_home_even_if_the_party_falls()
    {
        var rows = new[] { "......E.....", "............", "............" };
        var op = SurvivorUnit(1, new Pos(1, 1), pack: ["cargo_drone"]);
        var sim = Arena(12, 3, [op], rows, setup: (d, s) => s.Caches.Add(new LootCache { Id = s.NextCacheId++, Pos = new Pos(4, 1), Items = ["scrap", "med_kit", "flare", "rifle"] }));
        Launch(sim, op, new Pos(2, 1));
        Assert.True(sim.Execute(new FlyDrone(1, op.Id, new Pos(4, 2))).Ok);
        var cache = sim.State.Caches.Single();
        (op.Ap, op.Fatigue) = (9, -20);
        Assert.True(sim.Execute(new DroneLoad(1, op.Id, cache.Id, "rifle")).Ok);
        Assert.True(sim.Execute(new DroneLoad(1, op.Id, cache.Id, "med_kit")).Ok);
        Assert.Equal(ScavengeError.NotOnExit, sim.Execute(new DroneFerry(1, op.Id)).Error);
        Assert.True(sim.Execute(new FlyDrone(1, op.Id, new Pos(6, 1))).Ok);
        Assert.True(sim.Execute(new DroneFerry(1, op.Id)).Ok);
        Assert.Null(op.DroneId);

        op.Status = UnitStatus.Dead;
        sim.UpdateParties();
        var result = sim.ResultFor(1);
        Assert.Equal(["rifle", "med_kit", "cargo_drone"], result.Secured!);
        Assert.Equal(1, result.Loot["rifle"]);
    }

    [Fact]
    public void A_combat_drone_needs_drone_operator_and_shoots_what_it_sees()
    {
        var rookie = SurvivorUnit(1, new Pos(1, 1), pack: ["combat_drone"]);
        var sim0 = Arena(12, 8, [rookie]);
        Assert.Equal(ScavengeError.MissingPerk, sim0.Execute(new DeployDrone(1, rookie.Id, new Pos(2, 1))).Error);

        var op = SurvivorUnit(1, new Pos(1, 1), pack: ["combat_drone"]);
        op.Hooks = Real.HooksFor(["drone_operator_1"]);
        op.Skills[Skill.DronePiloting] = 60;
        var husk = EnemyUnit("husk", new Pos(6, 1));
        var sim = Arena(12, 8, [op, husk]);
        var drone = Launch(sim, op, new Pos(2, 1));
        Assert.Contains(husk, DroneRoles.FireTargets(sim, op));
        op.Ap = 9;
        Assert.True(sim.Execute(new DroneFire(1, op.Id, husk.Id)).Ok);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.DroneFired);
        Assert.True(drone.Skills[Skill.Firearms] >= 60);
    }
}
