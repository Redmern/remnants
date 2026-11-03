using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

/// <summary>
/// Early resource scarcity: every gathered material starts slow at level 1 and grows with the building's level, the
/// worker's skill and the tools in the stockpile (jobs.json <c>tools</c>), on the shipped data.
/// </summary>
public class GatheringCurveTests
{
    /// <summary>Wood a fresh adult (stats 45, skill 0) cut per hour at a level 1 Woodcutter before this pass (0.96 · 0.419).</summary>
    private const double WoodcutterL1Before = 0.402;

    public static TheoryData<string, Skill, string, string> Gatherers => new()
    {
        { "woodcutter", Skill.Woodcutting, "hand_saw", "felling_axe" },
        { "quarry", Skill.Mining, "pickaxe", "rock_drill" },
        { "scrapyard", Skill.Crafting, "crowbar", "cutting_torch" },
    };

    private static (ColonySim Sim, Building Building, Survivor Worker) Worked(string defId, int level, double skill = 0, Skill? skillKind = null)
    {
        var sim = LoopTestData.Colony(townHallLevel: 3);
        var building = new Building { Id = sim.State.NextBuildingId++, DefId = defId, Level = level, Plot = 1 };
        sim.State.Buildings.Add(building);
        var worker = sim.AddSurvivor(attributes: 45);
        if (skillKind is { } kind) worker.Set(kind, skill);
        Assert.True(sim.Execute(new AssignJob(worker.Id, building.Id)).Ok);
        return (sim, building, worker);
    }

    private static double Output(string defId, int level, Skill skill, double skillLevel = 0, params string[] tools)
    {
        var (sim, building, _) = Worked(defId, level, skillLevel, skill);
        foreach (var tool in tools) sim.State.Inventory.Add(tool);
        return sim.OutputPerTick(building);
    }

    [Fact]
    public void A_level_1_woodcutter_cuts_clearly_less_than_before()
    {
        Assert.True(Output("woodcutter", 1, Skill.Woodcutting) < WoodcutterL1Before * 0.75);
    }

    [Theory]
    [MemberData(nameof(Gatherers))]
    public void Each_building_level_raises_output(string defId, Skill skill, string basicTool, string bestTool)
    {
        _ = (basicTool, bestTool);
        var l1 = Output(defId, 1, skill);
        var l2 = Output(defId, 2, skill);
        var l3 = Output(defId, 3, skill);

        Assert.True(l1 < l2 && l2 < l3, $"{defId}: {l1:0.###} / {l2:0.###} / {l3:0.###}");
        Assert.True(l3 >= 2 * l1, $"{defId}: level 3 should at least double level 1 ({l1:0.###} → {l3:0.###})");
    }

    [Theory]
    [MemberData(nameof(Gatherers))]
    public void A_skilled_worker_out_gathers_an_unskilled_one(string defId, Skill skill, string basicTool, string bestTool)
    {
        _ = (basicTool, bestTool);
        var unskilled = Output(defId, 1, skill, 0);
        var trained = Output(defId, 1, skill, 30);
        var expert = Output(defId, 1, skill, 70);

        Assert.True(unskilled < trained && trained < expert, $"{defId}: {unskilled:0.###} / {trained:0.###} / {expert:0.###}");
        Assert.True(expert >= 1.5 * unskilled, $"{defId}: skill 70 should be worth half again ({unskilled:0.###} → {expert:0.###})");
    }

    [Theory]
    [MemberData(nameof(Gatherers))]
    public void Tools_in_the_stockpile_raise_output_and_better_tools_more(string defId, Skill skill, string basicTool, string bestTool)
    {
        var bare = Output(defId, 1, skill);
        var basic = Output(defId, 1, skill, 0, basicTool);
        var best = Output(defId, 1, skill, 0, bestTool);

        Assert.True(bare < basic && basic < best, $"{defId}: {bare:0.###} / {basic:0.###} / {best:0.###}");
    }

    [Fact]
    public void Each_tool_serves_one_worker_and_the_best_goes_first()
    {
        var (sim, building, first) = Worked("woodcutter", 2);
        var second = sim.AddSurvivor(attributes: 45);
        Assert.True(sim.Execute(new AssignJob(second.Id, building.Id)).Ok);
        var bare = sim.OutputPerTick(building);

        sim.State.Inventory.Add("hand_saw");
        sim.State.Inventory.Add("felling_axe");
        var job = sim.Data.Jobs["woodcutting"];

        Assert.Equal(job.Tools["felling_axe"], WorkTools.Quality(sim.State, sim.Data, job, first), 9);
        Assert.Equal(job.Tools["hand_saw"], WorkTools.Quality(sim.State, sim.Data, job, second), 9);
        Assert.True(sim.OutputPerTick(building) > bare);

        Assert.True(sim.State.Inventory.TryTake("felling_axe"));
        Assert.Equal(job.Tools["hand_saw"], WorkTools.Quality(sim.State, sim.Data, job, first), 9);
        Assert.Equal(0, WorkTools.Quality(sim.State, sim.Data, job, second), 9);
    }

    [Fact]
    public void Production_adds_what_the_tools_make_to_the_stockpile()
    {
        var (bareSim, _, _) = Worked("woodcutter", 1);
        var (toolSim, _, _) = Worked("woodcutter", 1);
        toolSim.State.Inventory.Add("felling_axe");

        bareSim.Advance(10);
        toolSim.Advance(10);

        Assert.True(toolSim.State.Stockpile.Get("wood") > bareSim.State.Stockpile.Get("wood") * 1.3);
    }

    [Fact]
    public void Every_tool_can_be_made_at_the_workshop()
    {
        var data = TestData.Real;
        foreach (var job in new[] { "woodcutting", "mining", "scavenging" })
            foreach (var tool in data.Jobs[job].Tools.Keys)
                Assert.Contains(data.Recipes.Values, r => r.Item == tool && r.Building == "workshop");
    }
}
