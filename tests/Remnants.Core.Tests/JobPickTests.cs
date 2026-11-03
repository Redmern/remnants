using Remnants.Core.Colony;

namespace Remnants.Core.Tests;

/// <summary>The job menus' commands (<see cref="JobPicks"/>): a pick adds a job, a move replaces, leaving drops one.</summary>
public class JobPickTests
{
    private static ColonySim Colony()
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = 3 });
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "house", Level = 3, Plot = 1 });
        state.AddFood(5000);
        return new ColonySim(TestData.Real, state, ScavengeTestData.Real);
    }

    [Fact]
    public void Picking_a_second_workplace_keeps_the_first_job()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var wood = sim.Build("woodcutter");
        var farm = sim.Build("farm");

        Assert.True(sim.Execute(JobPicks.Add(s.Id, wood.Id)).Ok);
        Assert.True(sim.Execute(JobPicks.Add(s.Id, farm.Id)).Ok);

        Assert.Equal([wood.Id, farm.Id], s.Jobs);
        Assert.Contains(s, sim.State.WorkersAt(wood.Id));
        Assert.Contains(s, sim.State.WorkersAt(farm.Id));
    }

    [Fact]
    public void Moving_is_a_separate_pick_that_leaves_the_other_jobs()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var wood = sim.Build("woodcutter");
        var farm = sim.Build("farm");
        sim.Execute(JobPicks.Add(s.Id, wood.Id));

        Assert.True(sim.Execute(JobPicks.Move(s.Id, farm.Id)).Ok);

        Assert.Equal([farm.Id], s.Jobs);
    }

    [Fact]
    public void Leaving_one_workplace_keeps_the_other_jobs()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var wood = sim.Build("woodcutter");
        var farm = sim.Build("farm");
        sim.Execute(JobPicks.Add(s.Id, wood.Id));
        sim.Execute(JobPicks.Add(s.Id, farm.Id));

        Assert.True(sim.Execute(JobPicks.Leave(s.Id, wood.Id)).Ok);

        Assert.Equal([farm.Id], s.Jobs);
    }

    [Fact]
    public void A_pick_at_the_cap_is_refused_out_loud_and_changes_nothing()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var jobs = new[] { "woodcutter", "quarry", "scrapyard", "farm" }.Select(sim.Build).ToList();
        foreach (var b in jobs.Take(3)) sim.Execute(JobPicks.Add(s.Id, b.Id));

        Assert.True(JobPicks.AtCap(sim.State, sim.Data, s));
        var refused = sim.Execute(JobPicks.Add(s.Id, jobs[3].Id));

        Assert.Equal(CommandError.TooManyActivities, refused.Error);
        Assert.False(string.IsNullOrWhiteSpace(refused.Message));
        Assert.Equal(jobs.Take(3).Select(b => b.Id), s.Jobs);
        // The move stays open at the cap.
        Assert.True(sim.Execute(JobPicks.Move(s.Id, jobs[3].Id)).Ok);
        Assert.Equal([jobs[3].Id], s.Jobs);
    }

    [Fact]
    public void The_summary_names_every_job_with_its_share()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var wood = sim.Build("woodcutter");
        var farm = sim.Build("farm");
        sim.Execute(JobPicks.Add(s.Id, wood.Id));
        Assert.Equal(sim.Data.Buildings["woodcutter"].Name, JobPicks.Summary(sim.State, sim.Data, s));

        sim.Execute(JobPicks.Add(s.Id, farm.Id));

        Assert.Equal($"{sim.Data.Buildings["woodcutter"].Name} 50% · {sim.Data.Buildings["farm"].Name} 50%",
            JobPicks.Summary(sim.State, sim.Data, s));
        Assert.All(JobPicks.Jobs(sim.State, sim.Data, s), j => Assert.Equal(0.5, j.Share));
    }
}
