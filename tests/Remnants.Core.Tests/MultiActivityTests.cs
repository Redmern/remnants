using Remnants.Core.Colony;
using Remnants.Core.Data;
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
}
