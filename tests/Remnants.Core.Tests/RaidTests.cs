using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Raids;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public static class RaidTestData
{
    private static readonly Lazy<RaidData> RealData = new(() =>
        RaidDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")) ?? throw new InvalidOperationException("no raids.json"));

    public static RaidData Real => RealData.Value;

    /// <summary>An empty colony (Town Hall 1, plenty of stock) with raids on.</summary>
    public static ColonySim Colony(int townHallLevel = 1, GameData? data = null)
    {
        var sim = TestData.EmptyColony(townHallLevel, new() { ["wood"] = 500, ["stone"] = 500, ["scrap"] = 500, ["cloth"] = 100, ["electronics"] = 100, ["food"] = 200 });
        if (data is not null) sim = new ColonySim(data, sim.State, ScavengeTestData.Real);
        else sim = new ColonySim(sim.Data, sim.State, ScavengeTestData.Real);
        sim.Raids = Real;
        return sim;
    }

    /// <summary>A free margin cell on the given side (in the middle of that edge band).</summary>
    public static (int X, int Y) Edge(ColonySim sim, Side side, int inset = 1, int offset = 0)
    {
        var g = DefenseGrid.Of(sim, Real);
        return side switch
        {
            Side.North => (g.Width / 2 + offset, inset),
            Side.South => (g.Width / 2 + offset, g.Height - 1 - inset),
            Side.West => (inset, g.Height / 2 + offset),
            _ => (g.Width - 1 - inset, g.Height / 2 + offset),
        };
    }

    public static PlacedDefense Place(this ColonySim sim, string id, (int X, int Y) at, Side facing = Side.South)
    {
        var r = sim.Execute(new PlaceDefense(id, at.X, at.Y, facing));
        Assert.True(r.Ok, r.Message);
        return sim.State.Defense.Find(r.EntityId!.Value)!;
    }

    public static Raid Raid(ColonySim sim, ThreatKind kind, int size, Side side, long inTicks = 10)
    {
        var raid = new Raid
        {
            Id = sim.State.Defense.NextRaidId++, Kind = kind, Size = size, Approach = side, SetOutTick = sim.State.Tick,
            ArrivalTick = sim.State.Tick + inTicks, WarnTick = sim.State.Tick, Stage = RaidStage.Warned, EstimateMin = size, EstimateMax = size,
        };
        sim.State.Defense.Raid = raid;
        return raid;
    }
}

public class RaidDataTests
{
    [Fact]
    public void Real_raid_data_validates_against_the_colony_and_scavenge_data()
    {
        RaidTestData.Real.Validate(TestData.Real, ScavengeTestData.Real);
        Assert.Equal(3, RaidTestData.Real.Def.Threats.Count);
        var sim = RaidTestData.Colony();
        var ungated = RaidTestData.Real.Defenses.Keys.Where(id => DefenseSchematics.GateOf(sim, id) is null).Order().ToList();
        Assert.Equal(["alarm_bell", "gate_wood", "spike_strip", "wall_wood", "watchtower_1"], ungated);
    }

    [Fact]
    public void A_defense_schematic_for_an_unknown_defense_is_an_error()
    {
        var data = TestData.Real;
        var bad = new Remnants.Core.Schematics.SchematicDef
        {
            Id = "sch_x", Name = "X", Unlocks = [new() { Kind = Remnants.Core.Schematics.UnlockKind.Defense, Id = "nope" }],
            Sources = [Remnants.Core.Schematics.SchematicSource.Loot],
        };
        var catalog = new Remnants.Core.Schematics.SchematicCatalog(new Remnants.Core.Schematics.SchematicFile
        {
            Rules = data.Schematics.Rules, Traders = data.Schematics.Traders.Values.ToList(), Schematics = data.Schematics.All.Values.Append(bad).ToList(),
        });
        Assert.Throws<GameDataException>(() => RaidTestData.Real.Validate(WithCatalog(data, catalog)));
    }

    private static GameData WithCatalog(GameData d, Remnants.Core.Schematics.SchematicCatalog catalog) =>
        new(d.Resources.Values, d.Jobs.Values, d.Buildings.Values, d.Rules, d.Recipes.Values, d.Progression, d.Cybernetics?.Def, catalog, d.Weather,
            d.Food?.Def, d.Electronics);

    [Fact]
    public void Engineer_and_Sentry_perks_load_with_their_hooks()
    {
        Assert.Equal(0.25, TestData.Real.Perks["engineer"].Effects.Hooks[PerkHooks.TrapCostReduction]);
        Assert.Equal(0.25, TestData.Real.Perks["sentry"].Effects.Hooks[PerkHooks.PostDefenseBonus]);
        Assert.Equal(Skill.Firearms, TestData.Real.Jobs["guard"].Skill);
    }
}

public class DefenseGridTests
{
    [Fact]
    public void Plots_map_onto_blocks_inside_the_margin()
    {
        var sim = RaidTestData.Colony();
        var g = DefenseGrid.Of(sim, RaidTestData.Real);
        Assert.Equal(6 * 4 + 8, g.Width);
        Assert.Equal(4 * 4 + 8, g.Height);
        Assert.Equal(-1, g.PlotAt(0, 0));
        Assert.Equal(0, g.PlotAt(4, 4));
        Assert.Equal(7, g.PlotAt(4 + 4, 4 + 4));
        Assert.All(g.PlotCells(7), c => Assert.Equal(7, g.PlotAt(c.X, c.Y)));
        Assert.Equal(Side.North, g.SideOf(g.Width / 2, 0));
        Assert.Equal(Side.South, g.SideOf(g.Width / 2, g.Height - 1));
        Assert.Equal(Side.East, g.SideOf(g.Width - 1, g.Height / 2));
    }

    [Fact]
    public void A_turret_covers_its_arc_only()
    {
        var def = RaidTestData.Real.Defense("defense_turret");
        var t = new PlacedDefense { X = 10, Y = 10, Facing = Side.North };
        Assert.True(DefenseGrid.Covers(t, def, 10, 5));
        Assert.False(DefenseGrid.Covers(t, def, 10, 14));
        Assert.False(DefenseGrid.Covers(t, def, 10, 3));
    }
}

public class DefensePlacementTests
{
    [Fact]
    public void Placing_pays_the_cost_and_refuses_built_plots_taken_cells_and_locked_defenses()
    {
        var sim = RaidTestData.Colony();
        var wood = sim.State.Stockpile.Get("wood");
        var at = RaidTestData.Edge(sim, Side.North);
        var wall = sim.Place("wall_wood", at);
        Assert.Equal(60, wall.Hp);
        Assert.Equal(wood - 6, sim.State.Stockpile.Get("wood"));
        Assert.Equal(CommandError.CellOccupied, sim.Execute(new PlaceDefense("spike_strip", at.X, at.Y)).Error);
        var hall = DefenseGrid.Of(sim, RaidTestData.Real).PlotCenter(0);
        Assert.Equal(CommandError.CellOccupied, sim.Execute(new PlaceDefense("spike_strip", hall.X, hall.Y)).Error);
        Assert.Equal(CommandError.InvalidCell, sim.Execute(new PlaceDefense("spike_strip", -1, 0)).Error);
        Assert.Equal(CommandError.SchematicMissing, sim.Execute(new PlaceDefense("barricade", 1, 1)).Error);
        Assert.True(Remnants.Core.Schematics.Schematics.Grant(sim, "sch_barricade", Remnants.Core.Schematics.SchematicSource.Debug).Outcome == Remnants.Core.Schematics.ReceiveOutcome.Learned);
        Assert.True(sim.Execute(new PlaceDefense("barricade", 1, 1)).Ok);
        Assert.Equal(CommandError.TownHallLevelTooLow, sim.ExecuteGranted("sch_defense_turret", new PlaceDefense("defense_turret", 2, 2)).Error);
    }

    [Fact]
    public void An_Engineer_at_home_makes_traps_cheaper()
    {
        var sim = RaidTestData.Colony();
        var s = sim.AddSurvivor();
        var def = RaidTestData.Real.Defense("spike_strip");
        Assert.Equal(4, DefenseHandlers.Cost(sim, def, def.Cost)["wood"]);
        s.Perks.Add("engineer");
        Assert.Equal(3, DefenseHandlers.Cost(sim, def, def.Cost)["wood"]);
        Assert.Equal(6, DefenseHandlers.Cost(sim, RaidTestData.Real.Defense("wall_wood"), RaidTestData.Real.Defense("wall_wood").Cost)["wood"]);
    }

    [Fact]
    public void Upgrade_pays_the_difference_and_remove_refunds_half()
    {
        var sim = RaidTestData.Colony(townHallLevel: 2);
        Remnants.Core.Schematics.Schematics.Grant(sim, "sch_wall_scrap", Remnants.Core.Schematics.SchematicSource.Debug);
        var wall = sim.Place("wall_wood", (1, 1));
        var scrap = sim.State.Stockpile.Get("scrap");
        Assert.True(sim.Execute(new UpgradeDefense(wall.Id)).Ok);
        Assert.Equal("wall_scrap", wall.DefId);
        Assert.Equal(120, wall.Hp);
        Assert.Equal(scrap - 8, sim.State.Stockpile.Get("scrap"));
        Assert.Equal(CommandError.SchematicMissing, sim.Execute(new UpgradeDefense(wall.Id)).Error);
        var wood = sim.State.Stockpile.Get("wood");
        Assert.True(sim.Execute(new RemoveDefense(wall.Id)).Ok);
        Assert.Equal(wood + 3, sim.State.Stockpile.Get("wood"));
        Assert.Empty(sim.State.Defense.Defenses);
    }
}

file static class GrantExtensions
{
    public static CommandResult ExecuteGranted(this ColonySim sim, string schematic, IColonyCommand c)
    {
        Remnants.Core.Schematics.Schematics.Grant(sim, schematic, Remnants.Core.Schematics.SchematicSource.Debug);
        return sim.Execute(c);
    }
}

public class GuardTests
{
    [Fact]
    public void A_guard_leaves_their_job_and_a_job_ends_guard_duty()
    {
        var sim = RaidTestData.Colony();
        var farm = sim.Build("farm");
        var s = sim.AddSurvivor();
        Assert.True(sim.Execute(new AssignJob(s.Id, farm.Id)).Ok);
        var tower = sim.Place("watchtower_1", (2, 2));
        Assert.True(sim.Execute(new AssignGuard(s.Id, tower.Id)).Ok);
        Assert.Null(s.AssignedBuildingId);
        Assert.Equal(CommandError.NoFreeSlot, sim.Execute(new AssignGuard(sim.AddSurvivor().Id, tower.Id)).Error);
        Assert.True(sim.Execute(new AssignJob(s.Id, farm.Id)).Ok);
        Assert.Null(Guards.PostOf(sim.State, s.Id));
    }

    [Fact]
    public void Guards_on_duty_train_Firearms_and_Perception_and_earn_xp()
    {
        var sim = RaidTestData.Colony();
        var s = sim.AddSurvivor();
        var tower = sim.Place("watchtower_1", (2, 2));
        sim.Execute(new AssignGuard(s.Id, tower.Id));
        var perception = s.Get(AttributeKind.Perception);
        sim.Advance(10);
        Assert.True(s.Get(Skill.Firearms) > 0);
        Assert.True(s.Get(AttributeKind.Perception) > perception);
        Assert.True(s.Xp > 0);
    }

    [Fact]
    public void Night_watch_is_night_work_slept_off_in_the_day()
    {
        var state = new ColonyState { Tick = 20 };
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall" });
        var sim = new ColonySim(TestData.RealWithNights, state, ScavengeTestData.Real) { Raids = RaidTestData.Real };
        state.Stockpile.Add("wood", 100);
        state.Stockpile.Add("stone", 100);
        var s = sim.AddSurvivor();
        var tower = sim.Place("watchtower_1", (2, 2));
        sim.Execute(new AssignGuard(s.Id, tower.Id));
        sim.Execute(new SetNightWatch(tower.Id, true));
        Assert.True(sim.OnShift(s));
        sim.Advance(10); // 20:00 → 06:00
        Assert.True(sim.IsTired(s));
        var debt = s.SleepDebt;
        Assert.False(sim.OnShift(s));
        sim.Advance(12); // 06:00 → 18:00, sleeps 08–16
        Assert.True(s.SleepDebt < debt);
    }
}

public class RaidSchedulerTests
{
    [Fact]
    public void No_raid_comes_in_the_grace_period_and_the_first_is_small()
    {
        var sim = RaidTestData.Colony();
        for (var i = 0; i < 3; i++) sim.AddSurvivor();
        var t = RaidTestData.Real.Def.Scheduler;
        sim.Advance(t.GraceDays * 24 - 1);
        Assert.Null(sim.State.Defense.Raid);
        Assert.True(sim.State.Defense.Pressure[ThreatKind.Husk] > 0);
        foreach (var b in new[] { "farm", "woodcutter", "quarry", "shelter" }) sim.Build(b);
        for (var i = 0; i < 24 * 10 && sim.State.Defense.Raid is null; i++) sim.Tick();
        var raid = Assert.IsType<Raid>(sim.State.Defense.Raid);
        Assert.True(raid.Size <= t.FirstRaidMaxSize);
        Assert.True(raid.ArrivalTick >= sim.State.Tick + t.TravelHours - 1);
    }

    [Fact]
    public void A_tower_warns_earlier_and_narrows_the_estimate()
    {
        var sim = RaidTestData.Colony();
        var raids = RaidTestData.Real;
        var bare = RaidWarning.LeadHours(sim, raids);
        var tower = sim.Place("watchtower_1", (2, 2));
        var s = sim.AddSurvivor();
        sim.Execute(new AssignGuard(s.Id, tower.Id));
        Assert.Equal(bare + 6 + 2, RaidWarning.LeadHours(sim, raids));
        var (min0, max0) = RaidWarning.Estimate(raids, 8, bare);
        var (min1, max1) = RaidWarning.Estimate(raids, 8, RaidWarning.LeadHours(sim, raids));
        Assert.True(max1 - min1 < max0 - min0);
    }

    [Fact]
    public void A_raid_is_seen_within_the_lead_time_and_resolves_on_arrival()
    {
        var sim = RaidTestData.Colony();
        for (var i = 0; i < 3; i++) sim.AddSurvivor();
        var raid = RaidSystem.Draw(sim, RaidTestData.Real, ThreatKind.Husk, 100);
        raid.Stage = RaidStage.Approaching;
        sim.State.Defense.Raid = raid;
        sim.Advance((int)(raid.ArrivalTick - sim.State.Tick - 3));
        Assert.Equal(RaidStage.Approaching, raid.Stage);
        sim.Advance(2);
        Assert.Equal(RaidStage.Warned, raid.Stage);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.RaidWarned);
        sim.Advance(3);
        Assert.Null(sim.State.Defense.Raid);
        Assert.Equal(raid.Id, sim.State.Defense.LastReport!.RaidId);
        Assert.Equal(1, sim.State.Defense.RaidsSurvived);
    }

    [Fact]
    public void Raids_are_deterministic()
    {
        string Run()
        {
            var sim = NewGame.Create(TestData.RealWithNights, 7, ScavengeTestData.Real);
            sim.Raids = RaidTestData.Real;
            foreach (var b in new[] { "farm", "woodcutter", "quarry" })
            {
                foreach (var (r, a) in sim.Data.Buildings[b].Level(1).Cost) sim.State.Stockpile.Add(r, a);
                sim.Execute(new Construct(b));
            }
            sim.Advance(24 * 14);
            return SaveGame.Save(sim.State);
        }
        var a = Run();
        Assert.Equal(a, Run());
        Assert.Contains("\"raidsSurvived\"", a);
    }
}

public class AutoResolveTests
{
    private static ColonySim Defended(int adults = 3)
    {
        var sim = RaidTestData.Colony();
        for (var i = 0; i < adults; i++) sim.AddSurvivor().Set(Skill.Firearms, 20);
        return sim;
    }

    [Fact]
    public void Traps_on_the_approach_spring_and_walls_soak()
    {
        var sim = Defended();
        var raid = RaidTestData.Raid(sim, ThreatKind.Husk, 6, Side.North);
        var before = AutoResolve.Assess(sim, RaidTestData.Real, raid);
        var strip = sim.Place("spike_strip", RaidTestData.Edge(sim, Side.North, 1));
        sim.Place("spike_strip", RaidTestData.Edge(sim, Side.South, 1)); // wrong side: does not spring
        var traps = AutoResolve.Assess(sim, RaidTestData.Real, raid);
        Assert.Equal(5, traps.TrapDamage);
        Assert.Equal([strip], traps.Traps);
        for (var x = 0; x < 4; x++) sim.Place("wall_wood", RaidTestData.Edge(sim, Side.North, 2, x));
        var walls = AutoResolve.Assess(sim, RaidTestData.Real, raid);
        Assert.True(walls.Soak > 0);
        Assert.True(walls.Attack < traps.Attack && traps.Attack < before.Attack);
    }

    [Fact]
    public void An_open_gate_lets_them_past_the_walls()
    {
        var sim = Defended();
        var raid = RaidTestData.Raid(sim, ThreatKind.Raider, 6, Side.South);
        for (var x = 0; x < 4; x++) sim.Place("wall_wood", RaidTestData.Edge(sim, Side.South, 2, x));
        sim.Place("gate_wood", RaidTestData.Edge(sim, Side.South, 2, 4));
        var open = AutoResolve.Assess(sim, RaidTestData.Real, raid);
        Assert.True(sim.Execute(new CloseGates(true)).Ok);
        var shut = AutoResolve.Assess(sim, RaidTestData.Real, raid);
        Assert.True(shut.Soak > open.Soak);
    }

    [Fact]
    public void Gear_from_the_inventory_and_guard_posts_make_defenders_stronger()
    {
        var sim = Defended(1);
        var raid = RaidTestData.Raid(sim, ThreatKind.Husk, 4, Side.North);
        var bare = AutoResolve.Assess(sim, RaidTestData.Real, raid).Defense;
        sim.State.Inventory.Add("pistol");
        var armed = AutoResolve.Assess(sim, RaidTestData.Real, raid);
        Assert.Equal("pistol", armed.Defenders[0].Weapon);
        Assert.True(armed.Defense > bare);
        var tower = sim.Place("watchtower_1", RaidTestData.Edge(sim, Side.North, 3));
        sim.Execute(new AssignGuard(sim.State.Survivors[0].Id, tower.Id));
        Assert.True(AutoResolve.Assess(sim, RaidTestData.Real, raid).Defense > armed.Defense);
    }

    [Fact]
    public void A_repelled_raid_leaves_loot_and_rearmable_traps_wait_to_be_reset()
    {
        var sim = Defended(4);
        sim.State.Tick = 12;
        var raid = RaidTestData.Raid(sim, ThreatKind.Raider, 2, Side.South, inTicks: 1);
        var strip = sim.Place("spike_strip", RaidTestData.Edge(sim, Side.South, 1));
        sim.Advance(2);
        var report = sim.State.Defense.LastReport!;
        Assert.True(report.Repelled, string.Join("; ", report.Breakdown));
        Assert.True(report.Stolen.Count == 0, $"p={report.Pressure} a={report.Attack} d={report.Defense} " + string.Join("; ", report.Breakdown));
        Assert.True(strip.Sprung);
        Assert.Contains("Spike Strip", report.TrapsSprung);
        Assert.True(sim.Execute(new RearmTrap(strip.Id)).Ok);
        Assert.False(strip.Sprung);
    }

    [Fact]
    public void An_overwhelming_raid_steals_hurts_and_wrecks_and_repairs_bring_it_back()
    {
        var sim = Defended(1);
        var farm = sim.Build("farm");
        var raid = RaidTestData.Raid(sim, ThreatKind.Raider, 10, Side.North, inTicks: 1);
        sim.Advance(2);
        var report = sim.State.Defense.LastReport!;
        Assert.False(report.Repelled);
        Assert.NotEmpty(report.Stolen);
        Assert.NotEmpty(report.Damaged);
        var wrecked = sim.State.Buildings.FirstOrDefault(b => RaidSystem.IsRubble(sim, b));
        Assert.NotNull(wrecked);
        if (wrecked!.DefId == "farm")
            Assert.Equal(CommandError.Wrecked, sim.Execute(new AssignJob(sim.AddSurvivor().Id, farm.Id)).Error);
        Assert.True(sim.Execute(new QueueRepair(RepairTarget.Building, wrecked.Id)).Ok);
        sim.Advance(200);
        Assert.False(RaidSystem.IsRubble(sim, wrecked));
        Assert.Equal(0, RaidSystem.Damage(sim.State, wrecked));
    }

    [Fact]
    public void Linked_carry_off_defenders_when_they_win()
    {
        var sim = Defended(3);
        RaidTestData.Raid(sim, ThreatKind.Linked, 10, Side.North, inTicks: 1);
        sim.Advance(2);
        var report = sim.State.Defense.LastReport!;
        Assert.False(report.Repelled);
        Assert.NotEmpty(report.Taken);
        Assert.Contains(sim.State.Memorial, m => m.Cause.Contains("Linked"));
    }

    [Fact]
    public void Sheltered_children_are_never_hurt()
    {
        var sim = Defended(1);
        var kids = Enumerable.Range(0, 4).Select(_ => sim.AddSurvivor(LifeStage.Child)).ToList();
        RaidTestData.Raid(sim, ThreatKind.Raider, 10, Side.North, inTicks: 1);
        Assert.True(sim.Execute(new ShelterChildren(true)).Ok);
        sim.Advance(2);
        Assert.All(kids, k => Assert.False(k.IsInjured));
    }
}

public class EarlyRaidTests
{
    [Fact]
    public void A_fresh_colony_with_a_few_traps_and_a_guard_holds_its_first_raid()
    {
        var holds = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var sim = NewGame.Create(TestData.RealWithNights, seed, ScavengeTestData.Real);
            sim.Raids = RaidTestData.Real;
            var raids = RaidTestData.Real;
            sim.State.Stockpile.Add("wood", 60);
            sim.State.Stockpile.Add("scrap", 10);
            var tower = sim.Place("watchtower_1", RaidTestData.Edge(sim, Side.North, 3));
            var guard = sim.State.Survivors.First(s => !s.IsChild);
            sim.Execute(new AssignGuard(guard.Id, tower.Id));
            foreach (var side in Enum.GetValues<Side>()) sim.Place("spike_strip", RaidTestData.Edge(sim, side, 1));
            sim.Place("alarm_bell", RaidTestData.Edge(sim, Side.South, 3));
            var raid = RaidSystem.Draw(sim, raids, ThreatKind.Husk, 150);
            sim.State.Defense.Raid = raid;
            sim.Advance((int)(raid.ArrivalTick - sim.State.Tick) + 1);
            var report = sim.State.Defense.LastReport!;
            Assert.Empty(report.Dead);
            if (report.Repelled) holds++;
        }
        Assert.True(holds >= 9, $"held {holds}/10");
    }
}

public class RaidSaveTests
{
    [Fact]
    public void Defenses_guards_and_a_raid_on_its_way_survive_a_save()
    {
        var sim = RaidTestData.Colony();
        var s = sim.AddSurvivor();
        var tower = sim.Place("watchtower_1", (2, 2));
        sim.Execute(new AssignGuard(s.Id, tower.Id));
        RaidTestData.Raid(sim, ThreatKind.Raider, 4, Side.East);
        sim.State.Defense.BuildingDamage[1] = 12;
        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(sim.Data, json, ScavengeTestData.Real);
        Assert.Equal(json, SaveGame.Save(loaded.State));
        Assert.Equal(tower.Id, loaded.State.Defense.Guards[s.Id]);
        Assert.Equal(ThreatKind.Raider, loaded.State.Defense.Raid!.Kind);
    }

    [Fact]
    public void An_older_save_loads_with_no_defenses_and_a_fresh_gap()
    {
        var sim = RaidTestData.Colony();
        sim.State.Tick = 500;
        var json = SaveGame.Save(sim.State).Replace($"\"version\": {SaveGame.Version}", $"\"version\": {SaveGame.RaidsVersion - 1}");
        var loaded = SaveGame.Load(sim.Data, json);
        Assert.Empty(loaded.State.Defense.Defenses);
        Assert.Equal(500, loaded.State.Defense.LastRaidTick);
    }
}

public class RaidBattleTests
{
    private static ColonySim Colony(int adults = 3)
    {
        var sim = RaidTestData.Colony();
        for (var i = 0; i < adults; i++) sim.AddSurvivor().Set(Skill.Firearms, 30);
        return sim;
    }

    /// <summary>Ends every turn of the party (and its turret) until the fight is over; the AI plays the attackers.</summary>
    private static Remnants.Core.Scavenge.ExtractionResult Play(Remnants.Core.Scavenge.ScavengeSim battle)
    {
        for (var i = 0; i < 2000 && !battle.ResultFor(ColonySim.ExpeditionPlayerId).Complete; i++)
        {
            var u = battle.State.CurrentUnit;
            if (u is null) break;
            battle.Execute(new Remnants.Core.Scavenge.EndTurn(ColonySim.ExpeditionPlayerId, u.Id));
        }
        return battle.ResultFor(ColonySim.ExpeditionPlayerId);
    }

    [Fact]
    public void The_colony_map_is_the_defense_grid_with_buildings_walls_towers_and_turrets()
    {
        var sim = Colony();
        var g = DefenseGrid.Of(sim, RaidTestData.Real);
        var wall = sim.Place("wall_wood", RaidTestData.Edge(sim, Side.North, 2));
        var tower = sim.Place("watchtower_1", RaidTestData.Edge(sim, Side.North, 3, 3));
        sim.State.Buildings[0].Level = 2;
        Remnants.Core.Schematics.Schematics.Grant(sim, "sch_defense_turret", Remnants.Core.Schematics.SchematicSource.Debug);
        var turret = sim.Place("defense_turret", RaidTestData.Edge(sim, Side.North, 3, -3), Side.North);
        var map = RaidBattle.ColonyMap(sim, RaidTestData.Real, Side.North);
        Assert.Equal(g.Width, map.Width);
        Assert.Equal(g.Height, map.Height);
        Assert.Equal('#', map.Rows[wall.Y][wall.X]);
        Assert.Equal(1, RaidBattle.TowerCount(map));
        var hall = Assert.Single(map.Structures);
        Assert.Equal(RaidBattle.HallTag, hall.Tag);
        Assert.Equal(100, hall.Hp);
        Assert.True(Assert.Single(map.Devices).Party);
        Assert.Equal(RaidBattle.ApproachMarker, map.Markers[map.Rows[0][0].ToString()]);
        Assert.Contains('E', map.Rows[^1]);
        _ = tower;
        _ = turret;
    }

    [Fact]
    public void A_fought_raid_spawns_its_waves_puts_guards_on_their_posts_and_feeds_the_consequences()
    {
        var sim = Colony();
        sim.State.Inventory.Add("pistol", 3);
        var tower = sim.Place("watchtower_1", RaidTestData.Edge(sim, Side.North, 3));
        var guard = sim.State.Survivors[0];
        sim.Execute(new AssignGuard(guard.Id, tower.Id));
        var raid = RaidTestData.Raid(sim, ThreatKind.Husk, 4, Side.North, inTicks: 1);
        Assert.True(sim.Execute(new ChooseFight(true)).Ok);
        sim.Advance(1);
        Assert.Equal(RaidStage.Battle, raid.Stage);
        var battle = RaidBattle.Open(sim);
        Assert.Same(battle.State, raid.Battle);
        var unit = battle.State.Units.Single(u => u.Origin?.Id == guard.Id);
        Assert.Equal((tower.X, tower.Y), (unit.Pos.X, unit.Pos.Y));
        Assert.Contains(battle.State.Mission!.Raised, p => p.X == tower.X && p.Y == tower.Y);
        Assert.All(battle.State.Units.Where(u => u.Kind == Remnants.Core.Scavenge.UnitKind.Survivor), u => Assert.Equal("pistol", u.Equipment[Items.EquipSlot.Weapon]));
        var result = Play(battle);
        Assert.True(result.Complete);
        Assert.Contains(battle.State.Units, u => u.Tag == "raider");
        Assert.True(sim.Execute(new ApplyRaidBattle(result)).Ok);
        var report = sim.State.Defense.LastReport!;
        Assert.True(report.Fought);
        Assert.Null(sim.State.Defense.Raid);
        Assert.Equal(result.Mission!.Outcome == Remnants.Core.Scavenge.MissionOutcome.Done, report.Repelled);
        // Downed defenders are dragged to safety, and their wounds come home dressed.
        Assert.DoesNotContain(battle.State.Units, u => u.Kind == Remnants.Core.Scavenge.UnitKind.Survivor && u.Status == Remnants.Core.Scavenge.UnitStatus.Dead && u.Hp > -10);
        Assert.All(sim.State.Survivors.SelectMany(s => s.Body.Parts.Values).Where(p => p.Condition == PartCondition.Injured),
            p => Assert.True(p.Severity <= RaidTestData.Real.Def.Battle.WoundCap + 1e-9));
    }

    [Fact]
    public void A_battle_survives_a_save_and_nobody_opening_it_falls_back_to_auto_resolve()
    {
        var sim = Colony();
        var raid = RaidTestData.Raid(sim, ThreatKind.Raider, 3, Side.South, inTicks: 1);
        sim.Execute(new ChooseFight(true));
        sim.Advance(1);
        var battle = RaidBattle.Open(sim);
        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(sim.Data, json, ScavengeTestData.Real);
        loaded.Raids = RaidTestData.Real;
        Assert.Equal(json, SaveGame.Save(loaded.State));
        Assert.Equal(battle.State.Units.Count, RaidBattle.Open(loaded).State.Units.Count);

        var other = Colony();
        RaidTestData.Raid(other, ThreatKind.Husk, 3, Side.North, inTicks: 1);
        other.Execute(new ChooseFight(true));
        other.Advance(1 + RaidTestData.Real.Def.Battle.MaxWaitHours);
        Assert.Null(other.State.Defense.Raid);
        Assert.False(other.State.Defense.LastReport!.Fought);
        _ = raid;
    }

    [Fact]
    public void A_raid_cannot_be_fought_without_a_town_hall()
    {
        var sim = Colony();
        sim.State.Buildings.Clear();
        RaidTestData.Raid(sim, ThreatKind.Husk, 3, Side.North);
        Assert.Equal(CommandError.CannotFight, sim.Execute(new ChooseFight(true)).Error);
    }

    [Fact]
    public void Story_perimeter_missions_fight_on_the_colony_when_it_has_the_mast_else_the_handmade_map()
    {
        var story = Remnants.Core.Story.StoryDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "story"));
        var sim = NewGame.Create(TestData.Real, 5, ScavengeTestData.Real);
        sim.Story = story;
        sim.Raids = RaidTestData.Real;
        Remnants.Core.Story.Campaign.Start(sim);
        sim.State.Buildings.Single(b => b.DefId == "town_hall").Level = 2;
        sim.State.Campaign!.Progress("m5").Attempts = 1;
        Assert.Equal("perimeter_th2", Remnants.Core.Story.Campaign.Setup(sim, "m5").Map!.Id);
        sim.Build("radio_mast");
        Remnants.Core.Schematics.Schematics.Grant(sim, "sch_watchtower", Remnants.Core.Schematics.SchematicSource.Debug);
        sim.Build("watchtower");
        var setup = Remnants.Core.Story.Campaign.Setup(sim, "m5");
        Assert.StartsWith("colony_", setup.Map!.Id);
        Assert.Equal(1, setup.Towers);
        var run = Remnants.Core.Scavenge.NewScavenge.Create(ScavengeTestData.Real, "outskirts", 1, [ScavengeTestData.Party(1, 4)], null, setup);
        Assert.Contains(run.State.Units, u => u.Tag == "mast");
        Assert.NotEmpty(run.State.Mission!.Marker("treeline"));
        var result = Play(run);
        Assert.True(result.Complete);
    }
}

public class RaidIntegrationTests
{
    [Fact]
    public void Bad_weather_shortens_the_warning()
    {
        var sim = new ColonySim(TestData.RealWithWeather, NewGame.Create(TestData.RealWithWeather, 3, ScavengeTestData.Real).State, ScavengeTestData.Real)
            { Raids = RaidTestData.Real };
        var clear = Enumerable.Range(0, 24 * 120).Select(t => (long)t).FirstOrDefault(t => RaidWeather.At(sim, t) == "clear", -1);
        var bad = Enumerable.Range(0, 24 * 120).Select(t => (long)t)
            .FirstOrDefault(t => RaidWeather.At(sim, t) is { } k && RaidTestData.Real.Def.Weather.ContainsKey(k), -1);
        Assert.True(clear >= 0 && bad >= 0, "the weather has clear spells and rain, fog or storms within 120 days");
        Assert.Equal(1, RaidWeather.Modifiers(sim, RaidTestData.Real, clear).Warning);
        Assert.True(RaidWeather.Modifiers(sim, RaidTestData.Real, bad).Warning < 1);
    }

    [Fact]
    public void A_defense_cabled_to_a_powered_building_gets_its_bonus()
    {
        var sim = RaidTestData.Colony();
        Remnants.Core.Schematics.Schematics.Grant(sim, "sch_floodlight_post", Remnants.Core.Schematics.SchematicSource.Debug);
        var hall = sim.State.Buildings[0];
        var near = DefenseGrid.Of(sim, RaidTestData.Real).PlotCenter(hall.Plot);
        var light = sim.Place("floodlight_post", (near.X, near.Y + 3));
        var far = sim.Place("floodlight_post", RaidTestData.Edge(sim, Side.East));
        Assert.False(DefensePower.IsPowered(sim, light));
        sim.State.Power.Powered.Add(hall.Id);
        Assert.True(DefensePower.IsPowered(sim, light));
        Assert.False(DefensePower.IsPowered(sim, far));
        Assert.Equal(1.5, DefensePower.Multiplier(sim, light, RaidTestData.Real.Defense("floodlight_post")));
    }

    [Fact]
    public void Husks_trample_the_standing_crops_and_raiders_take_pantry_food()
    {
        var sim = RaidTestData.Colony();
        var farm = sim.Build("farm");
        farm.HarvestProgress["potato"] = 10;
        Assert.Equal(5, CropDamage.Trample(sim, 0.5));
        Assert.Equal(5, farm.HarvestProgress["potato"], 6);
        Pantry.Add(sim.State, sim.Data, "potato", 20);
        var before = Pantry.FoodValue(sim);
        var taken = CropDamage.Steal(sim, 0.5);
        Assert.True(taken > 0);
        Assert.True(Pantry.FoodValue(sim) <= before - taken + 1e-9);
    }

    [Fact]
    public void Defense_schematics_from_an_older_save_move_into_the_schematics_catalogue()
    {
        var sim = RaidTestData.Colony();
        sim.State.Defense.KnownSchematics.Add("sch_barricade");
        var json = SaveGame.Save(sim.State).Replace($"\"version\": {SaveGame.Version}", $"\"version\": {SaveGame.RaidsVersion}");
        var loaded = SaveGame.Load(sim.Data, json, ScavengeTestData.Real);
        loaded.Raids = RaidTestData.Real;
        Assert.Contains("sch_barricade", loaded.State.Schematics.Known);
        Assert.Empty(loaded.State.Defense.KnownSchematics);
        Assert.True(DefenseSchematics.Unlocked(loaded, "barricade"));
    }

    [Fact]
    public void Gear_mods_count_in_a_defender_s_strength()
    {
        var plain = RaidTestData.Colony();
        plain.AddSurvivor();
        plain.State.Inventory.Add("pistol");
        var modded = RaidTestData.Colony();
        modded.AddSurvivor();
        modded.State.Inventory.Add("pistol+mk2+mod_iron_sight+mod_suppressor");
        var a = AutoResolve.Assess(plain, RaidTestData.Real, RaidTestData.Raid(plain, ThreatKind.Husk, 3, Side.North));
        var b = AutoResolve.Assess(modded, RaidTestData.Real, RaidTestData.Raid(modded, ThreatKind.Husk, 3, Side.North));
        Assert.Equal("pistol+mk2+mod_iron_sight+mod_suppressor", b.Defenders[0].Weapon);
        Assert.True(b.Defense > a.Defense);
    }
}
