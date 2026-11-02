using Remnants.Core.Colony;
using System.Text.Json.Nodes;
using Remnants.Core.Data;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

/// <summary>A survivor may hold up to <c>rules.maxActivities</c> jobs, a class and a site at once; each gets 1/N of their effort.</summary>
public class MultiActivityTests
{
    private static ColonySim Colony(GameData? data = null, int townHall = 3)
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = townHall });
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "house", Level = 3, Plot = 1 });
        state.AddFood(5000);
        return new ColonySim(data ?? TestData.Real, state, ScavengeTestData.Real);
    }

    private static Building TownHall(ColonySim sim) => sim.State.Buildings.Single(b => b.DefId == "town_hall");

    [Fact]
    public void A_second_and_third_job_are_added_without_releasing_the_first()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var wood = sim.Build("woodcutter");
        var stone = sim.Build("quarry");
        var scrap = sim.Build("scrapyard");

        Assert.True(sim.Execute(new AssignJob(s.Id, wood.Id)).Ok);
        Assert.True(sim.Execute(new AssignJob(s.Id, stone.Id)).Ok);
        Assert.True(sim.Execute(new AssignJob(s.Id, scrap.Id)).Ok);

        Assert.Equal([wood.Id, stone.Id, scrap.Id], s.Jobs);
        Assert.Contains(s, sim.State.WorkersAt(wood.Id));
        Assert.Contains(s, sim.State.WorkersAt(scrap.Id));
        Assert.Equal(3, Activities.Count(sim.State, s));
    }

    [Fact]
    public void A_fourth_activity_is_refused_with_a_clear_error()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var jobs = new[] { "woodcutter", "quarry", "scrapyard", "farm" }.Select(sim.Build).ToList();
        foreach (var b in jobs.Take(3)) Assert.True(sim.Execute(new AssignJob(s.Id, b.Id)).Ok);

        var fourth = sim.Execute(new AssignJob(s.Id, jobs[3].Id));

        Assert.Equal(CommandError.TooManyActivities, fourth.Error);
        Assert.Contains("3", fourth.Message);
        Assert.Equal(3, s.Jobs.Count);
        Assert.Equal(CommandError.TooManyActivities,
            sim.Execute(new StartStudy(s.Id, TownHall(sim).Id, Skill.Melee)).Error);
    }

    [Fact]
    public void The_cap_lives_in_the_rules()
    {
        var two = TestData.Real;
        var data = new GameData(two.Resources.Values, two.Jobs.Values, two.Buildings.Values, two.Rules with { MaxActivities = 2 },
            two.Recipes.Values, two.Progression, two.Cybernetics?.Def, two.Schematics, two.Weather, two.Food?.Def, two.Electronics,
            two.Medicine?.Def, two.Vehicles);
        var sim = Colony(data);
        var s = sim.AddSurvivor();
        var a = sim.Build("woodcutter");
        var b = sim.Build("quarry");
        var c = sim.Build("scrapyard");
        sim.Execute(new AssignJob(s.Id, a.Id));
        sim.Execute(new AssignJob(s.Id, b.Id));

        Assert.Equal(CommandError.TooManyActivities, sim.Execute(new AssignJob(s.Id, c.Id)).Error);
        Assert.Equal(3, TestData.Real.Rules.MaxActivities);
    }

    private static readonly string[] Gatherers = ["woodcutter", "quarry", "scrapyard"];
    private static readonly string[] Produces = ["wood", "stone", "scrap"];

    /// <summary>One survivor holding the first <paramref name="n"/> gathering jobs; returns what each building made in one tick.</summary>
    private static (double[] Made, double[] Estimate, Survivor S) Work(int n)
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var buildings = Gatherers.Select(sim.Build).ToList();
        foreach (var b in buildings.Take(n)) Assert.True(sim.Execute(new AssignJob(s.Id, b.Id)).Ok);
        var before = Produces.Select(sim.State.Stockpile.Get).ToArray();
        var estimate = buildings.Select(sim.OutputPerTick).ToArray();
        sim.Advance(1);
        return (Produces.Select((r, i) => sim.State.Stockpile.Get(r) - before[i]).ToArray(), estimate, s);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Each_job_yields_its_single_job_output_split_n_ways(int n)
    {
        var alone = Enumerable.Range(0, n).Select(Single).ToArray();
        var (made, estimate, _) = Work(n);

        for (var i = 0; i < n; i++)
        {
            Assert.True(alone[i] > 0);
            Assert.Equal(alone[i] / n, estimate[i], 9);
            // Made in the tick: the same, but a later job runs on stats the earlier one just trained (a hair more).
            Assert.Equal(alone[i] / n, made[i], 4);
        }
        // In shares of a full job: all N together are one job's worth, not more.
        Assert.Equal(1, made.Take(n).Select((m, i) => m / alone[i]).Sum(), 3);
    }

    /// <summary>What the <paramref name="index"/>th gathering job alone makes in a tick.</summary>
    private static double Single(int index)
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var b = sim.Build(Gatherers[index]);
        sim.Execute(new AssignJob(s.Id, b.Id));
        var before = sim.State.Stockpile.Get(Produces[index]);
        sim.Advance(1);
        return sim.State.Stockpile.Get(Produces[index]) - before;
    }

    private static readonly Skill[] Trained = [Skill.Woodcutting, Skill.Mining, Skill.Crafting];

    [Fact]
    public void A_single_job_trains_and_earns_xp_as_before()
    {
        var (_, _, s) = Work(1);
        var rules = TestData.Real.Rules.Training;

        // Skill 0 and attributes at 50 (see AddSurvivor): the unchanged per-tick gains.
        Assert.Equal(rules.SkillPerTick, s.Get(Skill.Woodcutting), 9);
        Assert.Equal(rules.AttributePerTick * 0.6 * 0.5, s.Get(AttributeKind.Strength) - 50, 9);
        Assert.Equal(rules.AttributePerTick * 0.4 * 0.5, s.Get(AttributeKind.Endurance) - 50, 9);
        Assert.Equal(TestData.Real.Progression.Xp.PerWorkTick, s.Xp, 9);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Each_job_trains_and_earns_xp_at_one_nth(int n)
    {
        var (_, _, s) = Work(n);
        var rules = TestData.Real.Rules.Training;

        for (var i = 0; i < n; i++) Assert.Equal(rules.SkillPerTick / n, s.Get(Trained[i]), 9);
        // XP: 1/N of a work tick per job, so one work tick in all.
        Assert.Equal(TestData.Real.Progression.Xp.PerWorkTick, s.Xp, 9);
        // Attributes: all N jobs together train about as much as one job (each spreads AttributePerTick / N).
        var gained = Enum.GetValues<AttributeKind>().Sum(a => s.Get(a) - 50);
        Assert.Equal(rules.AttributePerTick * 0.5, gained, 4);
    }

    [Fact]
    public void Work_and_study_together_each_get_half()
    {
        var sim = Colony();
        var wood = sim.Build("woodcutter");
        var alone = sim.AddSurvivor();
        var busy = sim.AddSurvivor();
        Assert.True(sim.Execute(new AssignJob(busy.Id, wood.Id)).Ok);
        Assert.True(sim.Execute(new StartStudy(alone.Id, TownHall(sim).Id, Skill.Melee)).Ok);
        Assert.True(sim.Execute(new StartStudy(busy.Id, TownHall(sim).Id, Skill.Melee)).Ok);
        Assert.Equal(wood.Id, busy.AssignedBuildingId);
        var eta = sim.StudyEta(sim.ClassOf(alone)!)!.Value;

        sim.Advance(1);

        Assert.True(alone.Get(Skill.Melee) > 0);
        // Half (to a hair: the job ran first this tick and trained the attributes study rides on).
        Assert.Equal(alone.Get(Skill.Melee) / 2, busy.Get(Skill.Melee), 5);
        Assert.Equal(TestData.Real.Rules.Training.SkillPerTick / 2, busy.Get(Skill.Woodcutting), 9);
        var xp = TestData.Real.Progression.Xp;
        Assert.Equal(xp.PerStudyTick, alone.Xp, 9);
        Assert.Equal(xp.PerStudyTick / 2 + xp.PerWorkTick / 2, busy.Xp, 9);
        Assert.True(sim.StudyEta(sim.ClassOf(busy)!) > eta);
    }

    [Fact]
    public void A_mentor_who_also_works_earns_half_the_teaching_xp()
    {
        var sim = Colony();
        var wood = sim.Build("woodcutter");
        var student = sim.AddSurvivor();
        var mentor = sim.AddSurvivor();
        mentor.Set(Skill.Melee, 60);
        sim.Execute(new AssignJob(mentor.Id, wood.Id));
        Assert.True(sim.Execute(new StartStudy(student.Id, TownHall(sim).Id, Skill.Melee, MentorId: mentor.Id)).Ok);

        sim.Advance(1);

        var xp = TestData.Real.Progression.Xp;
        Assert.Equal(xp.PerTeachTick / 2 + xp.PerWorkTick / 2, mentor.Xp, 9);
    }

    [Fact]
    public void Work_and_build_together_each_get_half_and_three_way_a_third()
    {
        var sim = Colony(TestData.RealWithConstruction);
        var wood = sim.Build("woodcutter");
        var solo = sim.AddSurvivor();
        var two = sim.AddSurvivor();
        var three = sim.AddSurvivor();
        var sites = Enumerable.Range(0, 3).Select(_ => Place(sim, "house")).ToList();
        sim.Execute(new AssignJob(two.Id, wood.Id));
        sim.Execute(new AssignJob(three.Id, wood.Id));
        Assert.True(sim.Execute(new StartStudy(three.Id, TownHall(sim).Id, Skill.Melee)).Ok);
        Assert.True(sim.Execute(new AssignBuilder(solo.Id, sites[0])).Ok);
        Assert.True(sim.Execute(new AssignBuilder(two.Id, sites[1])).Ok);
        Assert.True(sim.Execute(new AssignBuilder(three.Id, sites[2])).Ok);
        Assert.Equal(3, Activities.Count(sim.State, three));

        sim.Advance(1);

        var full = sim.Site(sites[0])!.DeliveredTotal;
        Assert.True(full > 0);
        Assert.Equal(full / 2, sim.Site(sites[1])!.DeliveredTotal, 9);
        Assert.Equal(full / 3, sim.Site(sites[2])!.DeliveredTotal, 9);
    }

    private static int Place(ColonySim sim, string defId)
    {
        foreach (var (res, amount) in sim.Data.Buildings[defId].Level(1).Cost) sim.State.Stockpile.Add(res, amount);
        var r = sim.Execute(new Construct(defId));
        Assert.True(r.Ok, r.Message);
        return r.EntityId!.Value;
    }

    [Fact]
    public void Leaving_one_job_gives_the_others_a_bigger_share_next_tick()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var buildings = Gatherers.Select(sim.Build).ToList();
        foreach (var b in buildings) sim.Execute(new AssignJob(s.Id, b.Id));
        var third = sim.OutputPerTick(buildings[0]);

        Assert.True(sim.Execute(new Unassign(s.Id, buildings[2].Id)).Ok);

        Assert.Equal([buildings[0].Id, buildings[1].Id], s.Jobs);
        Assert.Equal(0.5, Activities.Share(sim.State, s), 9);
        Assert.Equal(third * 3 / 2, sim.OutputPerTick(buildings[0]), 9);
        var wood = sim.State.Stockpile.Get("wood");
        sim.Advance(1);
        Assert.Equal(third * 3 / 2, sim.State.Stockpile.Get("wood") - wood, 9);
        Assert.Equal(CommandError.NotAssigned, sim.Execute(new Unassign(s.Id, buildings[2].Id)).Error);
    }

    [Fact]
    public void Unassign_without_a_building_leaves_every_job()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        foreach (var b in Gatherers.Take(2).Select(sim.Build)) sim.Execute(new AssignJob(s.Id, b.Id));

        Assert.True(sim.Execute(new Unassign(s.Id)).Ok);

        Assert.Empty(s.Jobs);
    }

    [Fact]
    public void Admission_as_a_patient_still_clears_every_activity_and_refuses_new_ones()
    {
        var sim = Colony(TestData.RealWithConstruction);
        var bay = sim.Build("med_bay");
        var s = sim.AddSurvivor();
        foreach (var b in Gatherers.Take(2).Select(sim.Build)) sim.Execute(new AssignJob(s.Id, b.Id));
        var site = Place(sim, "house");
        Assert.True(sim.Execute(new AssignBuilder(s.Id, site)).Ok);
        s.Body.Injure(BodyPart.LeftLeg, 0.5);

        var admitted = sim.Execute(new AdmitPatient(s.Id, bay.Id));
        Assert.True(admitted.Ok, admitted.Message);

        Assert.Empty(s.Jobs);
        Assert.Null(sim.State.Construction.SiteOf(s.Id));
        Assert.Equal(0, Activities.Count(sim.State, s));
        Assert.Equal(CommandError.IsPatient, sim.Execute(new AssignJob(s.Id, sim.State.Buildings.First(b => b.DefId == "woodcutter").Id)).Error);
    }

    [Fact]
    public void Babies_hold_no_activity()
    {
        var sim = Colony();
        var baby = sim.AddSurvivor(LifeStage.Infant);
        Assert.Equal(CommandError.TooYoung, sim.Execute(new AssignJob(baby.Id, sim.Build("farm").Id)).Error);
        Assert.Empty(baby.Jobs);
    }

    [Fact]
    public void Several_activities_round_trip_through_a_save()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var buildings = Gatherers.Take(2).Select(sim.Build).ToList();
        foreach (var b in buildings) sim.Execute(new AssignJob(s.Id, b.Id));
        Assert.True(sim.Execute(new StartStudy(s.Id, TownHall(sim).Id, Skill.Melee)).Ok);

        var json = SaveGame.Save(sim.State);
        Assert.DoesNotContain("assignedBuildingId", json);
        var loaded = SaveGame.Load(sim.Data, json, ScavengeTestData.Real);

        var again = loaded.State.FindSurvivor(s.Id)!;
        Assert.Equal(s.Jobs, again.Jobs);
        Assert.Equal(3, Activities.Count(loaded.State, again));
        Assert.Equal(sim.OutputPerTick(buildings[0]), loaded.OutputPerTick(loaded.State.FindBuilding(buildings[0].Id)!), 12);
    }

    [Fact]
    public void A_save_from_before_multiple_activities_loads_its_one_job_unchanged()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var idle = sim.AddSurvivor();
        var wood = sim.Build("woodcutter");
        sim.Execute(new AssignJob(s.Id, wood.Id));
        // The same colony as the previous save version wrote it: one assignedBuildingId per survivor, no jobs list.
        var node = JsonNode.Parse(SaveGame.Save(sim.State))!;
        node["version"] = SaveGame.MultiActivityVersion - 1;
        foreach (var p in node["colony"]!["survivors"]!.AsArray().OfType<JsonObject>())
        {
            var jobs = p["jobs"]!.AsArray();
            p["assignedBuildingId"] = jobs.Count == 0 ? null : jobs[0]!.GetValue<int>();
            p.Remove("jobs");
        }

        var loaded = SaveGame.Load(sim.Data, node.ToJsonString(), ScavengeTestData.Real);

        Assert.Equal([wood.Id], loaded.State.FindSurvivor(s.Id)!.Jobs);
        Assert.Empty(loaded.State.FindSurvivor(idle.Id)!.Jobs);
        Assert.Equal(1, Activities.Share(loaded.State, loaded.State.FindSurvivor(s.Id)!));
        Assert.Equal(sim.OutputPerTick(wood), loaded.OutputPerTick(loaded.State.FindBuilding(wood.Id)!), 12);
    }

    [Fact]
    public void A_real_v1_save_keeps_its_woodcutter_staffed()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "saves", "colony-v1.json"));
        Assert.Contains("\"assignedBuildingId\": ", json);
        var sim = SaveGame.Load(TestData.RealWithWeather, json, ScavengeTestData.Real);

        var woodcutter = sim.State.Buildings.Single(b => b.DefId == "woodcutter");
        Assert.NotEmpty(sim.State.WorkersAt(woodcutter.Id));
        Assert.All(sim.State.Survivors, p => Assert.True(p.Jobs.Count <= 1));
    }
}
