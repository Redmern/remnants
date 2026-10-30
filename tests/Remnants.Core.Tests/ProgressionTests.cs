using System.Text.Json.Nodes;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class ProgressionTests
{
    private static XpRules Xp => TestData.Real.Progression.Xp;

    private static ColonySim Colony(int townHallLevel = 1)
    {
        var sim = new ColonySim(TestData.Real, TestData.EmptyColony(townHallLevel, new() { ["food"] = 500 }).State, ScavengeTestData.Real);
        sim.Build("shelter");
        return sim;
    }

    [Fact]
    public void Level_curve_grows_by_a_step_each_level()
    {
        var rules = new XpRules { PerLevel = 100, PerLevelStep = 50, MaxLevel = 5 };
        Assert.Equal(0, Progression.XpForLevel(1, rules));
        Assert.Equal(100, Progression.XpForLevel(2, rules));
        Assert.Equal(250, Progression.XpForLevel(3, rules));
        Assert.Equal(450, Progression.XpForLevel(4, rules));
        Assert.Equal(1, Progression.LevelFor(99, rules));
        Assert.Equal(3, Progression.LevelFor(250, rules));
        Assert.Equal(5, Progression.LevelFor(1e9, rules));
    }

    [Fact]
    public void Gaining_a_level_gives_a_perk_point_and_logs_it()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();

        var levels = Progression.GrantXp(sim.State, sim.Data, s, Progression.XpForLevel(3, Xp), XpSource.Mission);

        Assert.Equal(2, levels);
        Assert.Equal(3, s.Level);
        Assert.Equal(2 * Xp.PointsPerLevel, s.PerkPoints);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.LevelUp && e.SurvivorId == s.Id);
    }

    [Fact]
    public void Work_earns_xp_and_children_earn_more_but_infants_none()
    {
        var sim = Colony();
        var adult = sim.AddSurvivor();
        var child = sim.AddSurvivor(LifeStage.Child);
        var infant = sim.AddSurvivor(LifeStage.Infant);
        var farm = sim.Build("farm");
        sim.Execute(new AssignJob(adult.Id, farm.Id));
        sim.Execute(new AssignJob(child.Id, farm.Id));

        sim.Advance(10);

        Assert.Equal(10 * Xp.PerWorkTick, adult.Xp, 6);
        Assert.Equal(10 * Xp.PerWorkTick * Xp.ChildMultiplier, child.Xp, 6);
        Assert.Equal(0, infant.Xp);
    }

    [Fact]
    public void A_perk_every_few_days_of_focused_work()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        sim.Execute(new AssignJob(s.Id, sim.Build("farm").Id));
        var day = sim.Data.Rules.TicksPerDay;

        sim.Advance(day);
        Assert.Equal(0, s.PerkPoints);
        sim.Advance(7 * day);
        Assert.InRange(s.PerkPoints, 1, 3);
    }

    [Fact]
    public void Electronics_work_trains_hacking_and_drone_piloting_on_the_side()
    {
        var job = TestData.Real.Jobs["electronics"];
        var s = TestData.Survivor();

        Training.TrainTick(job, s, TestData.Real.Rules.Training);

        Assert.True(s.Get(Skill.Hacking) > 0);
        Assert.True(s.Get(Skill.DronePiloting) > 0);
        Assert.True(s.Get(Skill.Hacking) < s.Get(Skill.Electronics));
    }

    [Fact]
    public void GainSkill_has_diminishing_returns_and_reports_the_gain()
    {
        var s = TestData.Survivor();
        s.Set(Skill.Hacking, 50);

        var gained = s.GainSkill(Skill.Hacking, 2, XpSource.Field);

        Assert.Equal(1, gained, 9);
        Assert.Equal(51, s.Get(Skill.Hacking), 9);
        Assert.Equal(0, s.GainSkill(Skill.Hacking, -3));
    }

    [Fact]
    public void ChoosePerk_reports_typed_errors()
    {
        var sim = Colony();
        var s = sim.AddSurvivor(attributes: 50);

        Assert.Equal(CommandError.UnknownSurvivor, sim.Execute(new ChoosePerk(999, "hacking_1")).Error);
        Assert.Equal(CommandError.UnknownPerk, sim.Execute(new ChoosePerk(s.Id, "nope")).Error);
        Assert.Equal(CommandError.PerkRequirementsNotMet, sim.Execute(new ChoosePerk(s.Id, "hacking_1")).Error);
        s.Set(Skill.Hacking, 12);
        Assert.Equal(CommandError.NoPerkPoints, sim.Execute(new ChoosePerk(s.Id, "hacking_1")).Error);
        s.PerkPoints = 1;
        var ok = sim.Execute(new ChoosePerk(s.Id, "hacking_1"));
        Assert.True(ok.Ok, ok.Message);
        Assert.True(s.HasPerk("hacking_1"));
        Assert.Equal(0, s.PerkPoints);
        Assert.Equal(CommandError.AlreadyHasPerk, sim.Execute(new ChoosePerk(s.Id, "hacking_1")).Error);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.PerkLearned);
    }

    [Fact]
    public void Technique_perks_are_locked_until_the_schematic_is_known()
    {
        var sim = Colony(townHallLevel: 2);
        var s = sim.AddSurvivor(attributes: 50);
        s.Set(Skill.Hacking, 30);
        s.Perks.Add("hacking_1");
        s.PerkPoints = 1;

        var locked = sim.Execute(new ChoosePerk(s.Id, "network_ghost"));
        Assert.Equal(CommandError.PerkLocked, locked.Error);
        Assert.Equal(PerkBlock.Locked, sim.PerkStatus(s, sim.Data.Perks["network_ghost"]).Block);

        Schematics.Schematics.Grant(sim, "sch_network_ghost", Schematics.SchematicSource.Debug);
        Assert.True(sim.Execute(new ChoosePerk(s.Id, "network_ghost")).Ok);
    }

    [Fact]
    public void Perk_status_lists_what_is_missing()
    {
        var sim = Colony();
        var s = sim.AddSurvivor(attributes: 20);

        var status = sim.PerkStatus(s, sim.Data.Perks["hacking_2"]);

        Assert.Equal(PerkBlock.MissingPrerequisites, status.Block);
        Assert.Contains(status.Missing, m => m.StartsWith("Hacking 25"));
        Assert.Contains(status.Missing, m => m == "Perk: Hacking I");
        Assert.Contains(status.Missing, m => m.StartsWith("Level 3"));
        Assert.Contains(status.Missing, m => m.StartsWith("Electronics Lab"));
    }

    [Fact]
    public void Perk_effects_sum_hooks_and_colony_modifiers()
    {
        var s = TestData.Survivor();
        s.Perks.UnionWith(["hacking_1", "hacking_2", "network_ghost", "jury_rig", "quick_study"]);

        var fx = PerkEffects.For(s, TestData.Real);

        Assert.Equal(0.2, fx.Hook(PerkHooks.HackChanceBonus), 9);
        Assert.Equal(0.5, fx.Hook(PerkHooks.HackAlarmReduction), 9);
        Assert.True(fx.Has(PerkHooks.FieldRepair));
        Assert.False(fx.Has(PerkHooks.CanHackLinked));
        Assert.Equal(0.1, fx.JobOutputBonus("electronics"), 9);
        Assert.Equal(0.05, fx.CraftQualityBonus("electronics"), 9);
        Assert.Equal(0.25, fx.StudySpeed, 9);
    }

    [Fact]
    public void Every_hook_in_the_data_is_known_and_every_drone_and_tech_hook_is_used()
    {
        var used = TestData.Real.Perks.Values.SelectMany(p => p.Effects.Hooks.Keys).ToHashSet();
        foreach (var id in new[] { PerkHooks.HackChanceBonus, PerkHooks.HackAlarmReduction, PerkHooks.CanHackLinked, PerkHooks.FieldRepair,
                     PerkHooks.DroneRangeBonus, PerkHooks.DroneBatteryBonus, PerkHooks.DroneCanMark, PerkHooks.DronePayload })
            Assert.Contains(id, used);
        foreach (var d in TestData.Real.Disciplines.Keys) Assert.NotEmpty(TestData.Real.PerksOf(d));
    }

    [Fact]
    public void Unknown_hook_in_perk_data_is_rejected()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        string Read(string f) => File.ReadAllText(Path.Combine(dir, f));
        var perks = Read("perks.json").Replace("\"hack_alarm_reduction\"", "\"hack_alarm_reductoin\"");
        var ex = Assert.Throws<GameDataException>(() => GameDataLoader.LoadFromJson(
            Read("resources.json"), Read("jobs.json"), Read("buildings.json"), Read("rules.json"), Read("recipes.json"), perks));
        Assert.Contains("hack_alarm_reductoin", ex.Message);
    }

    [Fact]
    public void Job_output_perks_raise_output()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var farm = sim.Build("farm");
        sim.Execute(new AssignJob(s.Id, farm.Id));
        var before = sim.OutputPerTick(farm);

        s.Perks.Add("forager");

        Assert.Equal(before * 1.1, sim.OutputPerTick(farm), 9);
    }

    [Fact]
    public void Craft_quality_perks_raise_the_odds_of_fine_gear()
    {
        var sim = Colony(townHallLevel: 2);
        var lab = sim.Build("electronics_lab");
        var s = sim.AddSurvivor();
        s.Set(Skill.Electronics, 40);
        sim.Execute(new AssignJob(s.Id, lab.Id));
        var recipe = sim.Data.Recipes["binoculars"];
        double Fine() => sim.QualityChances(lab, recipe).Single(q => q.Tier == "fine").Chance;
        var before = Fine();

        s.Perks.UnionWith(["jury_rig", "bench_engineer"]);

        Assert.True(Fine() > before + 0.2, $"{before} -> {Fine()}");
    }

    [Fact]
    public void Missions_give_xp_to_those_who_come_home_and_more_for_field_practice()
    {
        var sim = LoopTestData.Colony();
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a), LoopTestData.Member(b)])).Ok);
        var threat = ScavengeTestData.Real.Zones["outskirts"].Threat;
        var result = LoopTestData.Result(returned: [LoopTestData.Returned(a, s => s.Set(Skill.Hacking, 2)), LoopTestData.Returned(b)]);

        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);

        var mission = Xp.PerMission + Xp.PerMissionThreat * threat;
        Assert.Equal(mission, b.Xp, 6);
        Assert.Equal(mission + 2 * Xp.PerFieldSkillPoint, a.Xp, 6);
    }

    [Fact]
    public void Save_round_trips_xp_perks_and_classes()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var mentor = sim.AddSurvivor();
        mentor.Set(Skill.Farming, 40);
        Progression.GrantXp(sim.State, sim.Data, s, 500, XpSource.Mission);
        s.Perks.Add("light_step");
        var th = sim.State.Buildings.Single(b => b.DefId == "town_hall");
        Assert.True(sim.Execute(new StartStudy(s.Id, th.Id, Skill.Farming, MentorId: mentor.Id)).Ok);

        var loaded = SaveGame.Load(sim.Data, SaveGame.Save(sim.State), ScavengeTestData.Real);

        var ls = loaded.State.FindSurvivor(s.Id)!;
        Assert.Equal(s.Xp, ls.Xp);
        Assert.Equal(s.Level, ls.Level);
        Assert.Equal(s.PerkPoints, ls.PerkPoints);
        Assert.Contains("light_step", ls.Perks);
        Assert.Single(loaded.State.Studies);
        Assert.Equal(SaveGame.Save(sim.State), SaveGame.Save(loaded.State));
    }

    [Fact]
    public void Version_7_saves_get_xp_for_their_skills_and_the_new_skills()
    {
        var sim = Colony();
        var veteran = sim.AddSurvivor();
        veteran.Set(Skill.Woodcutting, 60);
        veteran.Set(Skill.Farming, 40);
        var node = JsonNode.Parse(SaveGame.Save(sim.State))!;
        node["version"] = 7;
        var colony = node["colony"]!.AsObject();
        colony.Remove("studies");
        colony.Remove("nextStudyId");
        foreach (var s in colony["survivors"]!.AsArray())
        {
            foreach (var key in new[] { "xp", "level", "perkPoints", "perks" }) s!.AsObject().Remove(key);
            s!["skills"]!.AsObject().Remove("Hacking");
            s["skills"]!.AsObject().Remove("DronePiloting");
        }

        var loaded = SaveGame.Load(sim.Data, node.ToJsonString(), ScavengeTestData.Real);

        var v = loaded.State.FindSurvivor(veteran.Id)!;
        Assert.Equal(100 * Xp.MigrationPerSkillPoint, v.Xp, 6);
        Assert.Equal(Progression.LevelFor(v.Xp, Xp), v.Level);
        Assert.True(v.Level > 1);
        Assert.Equal((v.Level - 1) * Xp.PointsPerLevel, v.PerkPoints);
        Assert.Equal(0, v.Skills[Skill.Hacking]);
        Assert.True(v.Skills.ContainsKey(Skill.DronePiloting));
        Assert.Empty(loaded.State.Studies);
    }

    [Fact]
    public void Unknown_perk_in_a_save_is_rejected()
    {
        var sim = Colony();
        sim.AddSurvivor().Perks.Add("removed_perk");
        var ex = Assert.Throws<System.Text.Json.JsonException>(() => SaveGame.Load(sim.Data, SaveGame.Save(sim.State)));
        Assert.Contains("removed_perk", ex.Message);
    }

    [Fact]
    public void Perks_cannot_be_chosen_while_away_and_choosing_one_ends_its_class()
    {
        var sim = Colony(townHallLevel: 2);
        var lab = sim.Build("electronics_lab");
        var s = sim.AddSurvivor();
        var mentor = sim.AddSurvivor();
        mentor.Perks.Add("hacking_1");
        s.Set(Skill.Hacking, 12);
        s.PerkPoints = 1;
        Assert.True(sim.Execute(new StartStudy(s.Id, lab.Id, PerkId: "hacking_1", MentorId: mentor.Id)).Ok);

        s.Away = true;
        Assert.Equal(CommandError.SurvivorAway, sim.Execute(new ChoosePerk(s.Id, "hacking_1")).Error);
        s.Away = false;
        Assert.True(sim.Execute(new ChoosePerk(s.Id, "hacking_1")).Ok);
        Assert.Empty(sim.State.Studies);
    }
}
