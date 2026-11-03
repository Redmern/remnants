using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;
using Xunit;

namespace Remnants.Core.Tests;

public class ConstructionTests
{
    private static GameData Sites => TestData.RealWithConstruction;

    /// <summary>The shipped data with the clock on and construction on (no weather).</summary>
    private static readonly Lazy<GameData> SitesWithNights = new(() =>
    {
        var d = TestData.RealWithNights;
        var rules = d.Rules with { Construction = Sites.Rules.Construction };
        return new GameData(d.Resources.Values, d.Jobs.Values, d.Buildings.Values, rules, d.Recipes.Values, d.Progression, d.Cybernetics?.Def, d.Schematics,
            d.Weather, d.Food?.Def, d.Electronics, d.Medicine?.Def, d.Vehicles);
    });

    private static ColonySim Colony(GameData? data = null, int townHall = 3)
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = townHall });
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "house", Level = 3, Plot = 1 });
        state.AddFood(500);
        return new ColonySim(data ?? Sites, state);
    }

    private static int Place(ColonySim sim, string defId)
    {
        foreach (var (res, amount) in sim.Data.Buildings[defId].Level(1).Cost) sim.State.Stockpile.Add(res, amount);
        var r = sim.Execute(new Construct(defId));
        Assert.True(r.Ok, r.Message);
        return r.EntityId!.Value;
    }

    private static int Ticks(ColonySim sim, Func<bool> until, int max = 2000)
    {
        for (var t = 1; t <= max; t++)
        {
            sim.Tick();
            if (until()) return t;
        }
        throw new Xunit.Sdk.XunitException("Never happened.");
    }

    [Fact]
    public void Construct_places_a_site_that_takes_the_plot_and_the_materials_but_no_building_yet()
    {
        var sim = Colony();
        var id = Place(sim, "woodcutter");

        Assert.Null(sim.State.FindBuilding(id));
        var site = sim.Site(id)!;
        Assert.Equal("woodcutter", site.DefId);
        Assert.Equal(1, site.Level);
        Assert.Equal(0, sim.State.Stockpile.Get("wood"));
        Assert.False(sim.IsPlotFree(site.Plot));
        Assert.Equal(sim.Data.Buildings["woodcutter"].Level(1).Cost.Values.Sum(), site.Reserved().Values.Sum());
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.SiteStarted && e.BuildingId == id);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ColonyEventType.Constructed);
    }

    [Fact]
    public void Nobody_builds_an_unstaffed_site_with_no_idle_hands()
    {
        var sim = Colony();
        var id = Place(sim, "woodcutter");
        sim.Advance(48);
        Assert.Equal(0, sim.Site(id)!.Work);
        Assert.Equal(0, sim.Site(id)!.DeliveredTotal);
    }

    [Fact]
    public void Two_builders_put_up_a_woodcutter_shed_in_a_couple_of_hours()
    {
        var sim = Colony();
        var id = Place(sim, "woodcutter");
        foreach (var s in new[] { sim.AddSurvivor(), sim.AddSurvivor() })
            Assert.True(sim.Execute(new AssignBuilder(s.Id, id)).Ok);

        var hours = Ticks(sim, () => sim.State.FindBuilding(id) is not null);

        Assert.InRange(hours, 1, 4);
        Assert.Null(sim.Site(id));
        Assert.Equal(1, sim.State.FindBuilding(id)!.Level);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Constructed && e.BuildingId == id);
        // The builders are free again and trained a little.
        Assert.All(sim.State.Survivors, s => Assert.Null(sim.State.Construction.SiteOf(s.Id)));
        Assert.All(sim.State.Survivors, s => Assert.True(s.Get(Skill.Construction) > 0));
    }

    [Fact]
    public void Work_never_runs_ahead_of_the_materials_hauled()
    {
        var sim = Colony();
        var id = Place(sim, "house");
        var s = sim.AddSurvivor();
        sim.Execute(new AssignBuilder(s.Id, id));
        var site = sim.Site(id)!;
        for (var t = 0; t < 40 && sim.Site(id) is not null; t++)
        {
            sim.Tick();
            Assert.True(site.Progress <= site.DeliveredShare + 1e-9, $"work {site.Progress:0.00} past delivered {site.DeliveredShare:0.00}");
        }
        Assert.True(site.DeliveredTotal > 0);
    }

    [Fact]
    public void Upgrades_take_days_and_the_building_keeps_working_at_its_old_level()
    {
        var sim = Colony(SitesWithNights.Value);
        var cutter = sim.Instantly(() => { Place(sim, "woodcutter"); return sim.State.Buildings.Last(); });
        var worker = sim.AddSurvivor();
        sim.Execute(new AssignJob(worker.Id, cutter.Id));
        foreach (var (res, amount) in sim.Data.Buildings["woodcutter"].Level(2).Cost) sim.State.Stockpile.Add(res, amount);
        Assert.True(sim.Execute(new Upgrade(cutter.Id)).Ok);
        var builder = sim.AddSurvivor();
        Assert.True(sim.Execute(new AssignBuilder(builder.Id, cutter.Id)).Ok);
        Assert.Equal(CommandError.UnderConstruction, sim.Execute(new Upgrade(cutter.Id)).Error);

        var wood = sim.State.Stockpile.Get("wood");
        var hours = Ticks(sim, () => cutter.Level == 2);

        // One builder of average skill: about one to two days of work for level 2.
        Assert.InRange(hours / 24.0, 1, 2.5);
        Assert.True(sim.State.Stockpile.Get("wood") > wood, "the woodcutter kept cutting while it was upgraded");

        foreach (var (res, amount) in sim.Data.Buildings["woodcutter"].Level(3).Cost) sim.State.Stockpile.Add(res, amount);
        Assert.True(sim.Execute(new Upgrade(cutter.Id)).Ok);
        var assigned = sim.Execute(new AssignBuilder(builder.Id, cutter.Id));
        Assert.True(assigned.Ok, assigned.Message);
        var l3 = Ticks(sim, () => cutter.Level == 3, 5000);
        Assert.InRange(l3 / 24.0, 3, 6);
    }

    [Fact]
    public void Level_one_buildings_are_quick_and_upgrades_scale_with_level()
    {
        foreach (var def in Sites.Buildings.Values)
        {
            Assert.InRange(def.Level(1).BuildWork, 1, 6);
            for (var l = 2; l <= def.MaxLevel; l++)
                Assert.True(def.Level(l).BuildWork > def.Level(l - 1).BuildWork * 1.5, $"{def.Id} L{l}");
        }
    }

    [Fact]
    public void Cancel_gives_back_what_was_not_hauled_and_most_of_what_was()
    {
        var sim = Colony();
        var id = Place(sim, "house");
        var site = sim.Site(id)!;
        site.Delivered["wood"] = 40;
        var r = sim.Execute(new CancelConstruction(id));
        Assert.True(r.Ok, r.Message);
        Assert.Null(sim.Site(id));
        Assert.True(sim.IsPlotFree(site.Plot));
        // The wood not hauled + 80% of the 40 hauled; all the stone.
        var cost = sim.Data.Buildings["house"].Level(1).Cost;
        Assert.Equal(cost["wood"] - 40 + 32, sim.State.Stockpile.Get("wood"));
        Assert.Equal(cost["stone"], sim.State.Stockpile.Get("stone"));
    }

    [Fact]
    public void A_paused_site_stands_still_and_its_builders_count_as_off_shift()
    {
        var sim = Colony();
        var id = Place(sim, "house");
        var s = sim.AddSurvivor();
        sim.Execute(new AssignBuilder(s.Id, id));
        Assert.True(sim.OnShift(s));
        sim.Execute(new PauseConstruction(id, true));
        Assert.False(sim.OnShift(s));
        sim.Advance(10);
        Assert.Equal(0, sim.Site(id)!.DeliveredTotal);
        sim.Execute(new PauseConstruction(id, false));
        sim.Advance(2);
        Assert.True(sim.Site(id)!.DeliveredTotal > 0);
    }

    [Fact]
    public void Idle_hands_help_at_half_rate_at_the_highest_priority_site_and_never_at_low()
    {
        var sim = Colony();
        var house = Place(sim, "house");
        var farm = Place(sim, "farm");
        var idle = sim.AddSurvivor();
        sim.Execute(new SetConstructionPriority(farm, SitePriority.High));
        sim.Tick();
        Assert.Contains(idle.Id, sim.Site(farm)!.Helpers);
        Assert.Empty(sim.Site(house)!.Helpers);

        sim.Execute(new SetConstructionPriority(farm, SitePriority.Low));
        sim.Tick();
        Assert.Contains(idle.Id, sim.Site(house)!.Helpers);

        sim.Execute(new SetConstructionPriority(house, SitePriority.Low));
        sim.Tick();
        Assert.Null(sim.State.Construction.SiteOf(idle.Id));
    }

    [Fact]
    public void Children_build_at_the_child_rate()
    {
        var sim = Colony();
        var adult = sim.AddSurvivor();
        var child = sim.AddSurvivor(LifeStage.Child);
        child.Set(AttributeKind.Strength, 50);
        foreach (var a in Enum.GetValues<AttributeKind>()) child.Set(a, 50);
        Assert.Equal(ConstructionSystem.WorkRate(sim, adult) * sim.Data.Rules.Output.ChildOutputMultiplier, ConstructionSystem.WorkRate(sim, child), 9);
        var id = Place(sim, "farm");
        Assert.True(sim.Execute(new AssignBuilder(child.Id, id)).Ok);
    }

    [Fact]
    public void Builders_keep_their_jobs_and_unassign_takes_them_off_the_site()
    {
        var sim = Colony();
        var farm = sim.Build("farm");
        var site = Place(sim, "house");
        var s = sim.AddSurvivor();
        sim.Execute(new AssignJob(s.Id, farm.Id));
        Assert.True(sim.Execute(new AssignBuilder(s.Id, site)).Ok);
        Assert.Equal(farm.Id, s.AssignedBuildingId);
        Assert.Contains(s.Id, sim.Site(site)!.Builders);

        sim.Advance(1);
        Assert.Contains(s.Id, sim.Site(site)!.Builders);

        Assert.True(sim.Execute(new Unassign(s.Id, site)).Ok);
        Assert.Null(sim.State.Construction.SiteOf(s.Id));
        Assert.Equal(farm.Id, s.AssignedBuildingId);

        sim.Execute(new Unassign(s.Id));
        sim.Execute(new AssignBuilder(s.Id, site));
        Assert.True(sim.Execute(new Unassign(s.Id)).Ok);
        Assert.Null(sim.State.Construction.SiteOf(s.Id));
    }

    [Fact]
    public void Builders_on_one_site_grow_closer_like_coworkers()
    {
        var sim = Colony();
        var id = Place(sim, "house");
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        b.Sex = Sex.Female;
        sim.Execute(new AssignBuilder(a.Id, id));
        sim.Execute(new AssignBuilder(b.Id, id));
        var rules = sim.Data.Rules.Relationships;
        var together = Bonds.AffinityPerTick(a, b, PairStance.Neutral, rules, id, id);
        Assert.Equal(Bonds.AffinityPerTick(a, b, PairStance.Neutral, rules) + rules.SharedWorkplacePerTick, together, 9);
        sim.Tick();
        Assert.Equal(together, Bonds.Affinity(sim.State, a.Id, b.Id), 6);
    }

    [Fact]
    public void The_help_breakdown_shows_a_site_and_its_time_left()
    {
        var sim = Colony();
        var id = Place(sim, "house");
        sim.Execute(new AssignBuilder(sim.AddSurvivor().Id, id));
        var b = Remnants.Core.Help.Breakdowns.For(Remnants.Core.Help.Breakdowns.ConstructionSite,
            new Remnants.Core.Help.HelpContext { Colony = sim, Site = sim.Site(id) });
        Assert.NotNull(b);
        Assert.Contains("left", b!.Total);
        Assert.Contains(b.Rows, r => r.Label == "Builders" && r.Value.StartsWith("1 /"));
        Assert.Null(Remnants.Core.Help.Breakdowns.For(Remnants.Core.Help.Breakdowns.ConstructionSite, new Remnants.Core.Help.HelpContext { Colony = sim }));
    }

    [Fact]
    public void A_site_takes_only_so_many_builders()
    {
        var sim = Colony();
        var id = Place(sim, "farm");
        var max = sim.MaxBuilders(sim.Site(id)!);
        for (var i = 0; i < max; i++) Assert.True(sim.Execute(new AssignBuilder(sim.AddSurvivor().Id, id)).Ok);
        Assert.Equal(CommandError.NoFreeSlot, sim.Execute(new AssignBuilder(sim.AddSurvivor().Id, id)).Error);
    }

    [Fact]
    public void Builders_keep_the_schedule_and_only_a_lit_upgrade_builds_at_night()
    {
        var sim = Colony(SitesWithNights.Value);
        var id = Place(sim, "house");
        var s = sim.AddSurvivor();
        sim.Execute(new AssignBuilder(s.Id, id));
        Assert.Equal(CommandError.NeedsLight, sim.Execute(new SetSiteNightShift(id, true)).Error);
        // 06:00 is the first hour of the day shift; 21:00 is off.
        sim.State.Tick = 6;
        Assert.True(sim.OnShift(s));
        sim.State.Tick = 21;
        Assert.False(sim.OnShift(s));
        var before = sim.Site(id)!.DeliveredTotal;
        sim.Tick();
        Assert.Equal(before, sim.Site(id)!.DeliveredTotal);

        var lit = sim.Instantly(() => sim.Build("workshop")); // indoors: lit
        foreach (var (res, amount) in sim.Data.Buildings["workshop"].Level(2).Cost) sim.State.Stockpile.Add(res, amount);
        sim.Execute(new Upgrade(lit.Id));
        Assert.True(sim.Execute(new SetSiteNightShift(lit.Id, true)).Ok);
        Assert.True(sim.Execute(new AssignBuilder(s.Id, lit.Id)).Ok);
        sim.State.Tick = 21;
        Assert.True(sim.OnShift(s));
    }

    [Fact]
    public void A_dust_storm_halts_building_and_rain_slows_it()
    {
        var data = TestData.RealWithWeather;
        var s = TestData.Survivor();
        Assert.Equal(0, ConstructionSystem.WeatherFactor(data, new WeatherSpell(0, "dust_storm", 0, 10, 1), s, masked: true));
        Assert.Equal(0.8, ConstructionSystem.WeatherFactor(data, new WeatherSpell(0, "rain", 0, 10, 1), s, masked: false), 9);
        Assert.Equal(1, ConstructionSystem.WeatherFactor(data, new WeatherSpell(0, "clear", 0, 10, 0), s, masked: false), 9);
    }

    [Fact]
    public void The_engineer_perk_builds_faster()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var plain = ConstructionSystem.WorkRate(sim, s);
        s.Perks.Add("engineer");
        Assert.Equal(plain * 1.15, ConstructionSystem.WorkRate(sim, s), 9);
    }

    [Fact]
    public void Leaving_on_an_expedition_or_for_a_bed_ends_building()
    {
        var sim = Colony();
        var id = Place(sim, "house");
        var s = sim.AddSurvivor();
        sim.Execute(new AssignBuilder(s.Id, id));
        s.Away = true;
        sim.Tick();
        Assert.Empty(sim.Site(id)!.Builders);
    }

    [Fact]
    public void A_raid_hit_knocks_the_work_back()
    {
        var site = new ConstructionSite { WorkNeeded = 10, Work = 5 };
        Assert.Equal(30, ConstructionSystem.SiteHp(site, 60), 9);
        var hit = ConstructionSystem.Hit(site, 60, 12);
        Assert.Equal(12, hit, 9);
        Assert.Equal(3, site.Work, 9);
        Assert.Equal(6, ConstructionSystem.SiteHp(site, 60) - 12, 9);
        ConstructionSystem.Hit(site, 60, 100);
        Assert.True(site.Work < 3);
    }

    [Fact]
    public void Sites_survive_a_save_and_the_run_is_deterministic()
    {
        string Run()
        {
            var sim = Colony();
            var id = Place(sim, "house");
            sim.Execute(new AssignBuilder(sim.AddSurvivor().Id, id));
            sim.AddSurvivor();
            sim.Advance(5);
            return SaveGame.Save(sim.State);
        }
        var json = Run();
        Assert.Equal(json, Run());
        var loaded = SaveGame.Load(Sites, json);
        var site = Assert.Single(loaded.State.Construction.Sites);
        Assert.True(site.Work > 0 || site.DeliveredTotal > 0);
        Assert.Single(site.Builders);
        Assert.Equal(json, SaveGame.Save(loaded.State));
    }

    [Fact]
    public void Sites_finish_when_a_save_with_them_loads_on_data_without_construction()
    {
        var sim = Colony();
        var id = Place(sim, "house");
        var loaded = SaveGame.Load(TestData.Real, SaveGame.Save(sim.State));
        Assert.Empty(loaded.State.Construction.Sites);
        Assert.NotNull(loaded.State.FindBuilding(id));
    }

    [Fact]
    public void Construction_off_builds_at_once_as_before()
    {
        var sim = Colony(TestData.Real);
        foreach (var (res, amount) in sim.Data.Buildings["farm"].Level(1).Cost) sim.State.Stockpile.Add(res, amount);
        var r = sim.Execute(new Construct("farm"));
        Assert.NotNull(sim.State.FindBuilding(r.EntityId!.Value));
        Assert.Empty(sim.State.Construction.Sites);
    }

    [Fact]
    public void The_build_menu_estimate_is_hours_for_a_shed_and_days_for_an_upgrade()
    {
        var sim = Colony(SitesWithNights.Value);
        sim.AddSurvivor();
        var cutter = sim.Data.Buildings["woodcutter"];
        Assert.InRange(sim.BuildHours(cutter, 1), 0.5, 6);
        Assert.InRange(sim.BuildHours(cutter, 2) / 24, 0.4, 2);
        Assert.True(sim.BuildHours(cutter, 3) > sim.BuildHours(cutter, 2) * 2);
    }

    [Fact]
    public void Forecast_says_why_a_site_stands_still_and_how_long_it_has_left()
    {
        var sim = Colony();
        var id = Place(sim, "house");
        var site = sim.Site(id)!;
        Assert.Equal("No builders", sim.SiteForecast(site).BlockedBy);
        sim.Execute(new AssignBuilder(sim.AddSurvivor().Id, id));
        var f = sim.SiteForecast(site);
        Assert.False(f.Blocked);
        Assert.NotNull(f.EtaHours);
        Assert.True(f.EtaHours > 5);
    }
}
