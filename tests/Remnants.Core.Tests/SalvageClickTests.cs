using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>A click on a salvage node on the map salvages it when the unit stands next to it (<see cref="FieldSalvage.ClickedNode"/>).</summary>
public class SalvageClickTests
{
    private static (ScavengeSim Sim, Unit Worker, SalvageNode Node) NodeArena(Pos worker)
    {
        var unit = SurvivorUnit(1, worker);
        SalvageNode node = null!;
        var sim = Arena(6, 4, [unit], rows: ["......", "......", ".r....", "......"],
            setup: (d, s) => node = FieldSalvage.Add(d, s, d.Salvage.Node("rubble")!, new Pos(1, 2), new DeterministicRng(1)));
        return (sim, unit, node);
    }

    [Fact]
    public void Clicking_a_node_next_to_the_unit_picks_it_and_the_salvage_it_sends_works()
    {
        var (sim, worker, node) = NodeArena(new Pos(1, 1));

        var clicked = FieldSalvage.ClickedNode(sim.State, worker, node.Pos);

        Assert.Same(node, clicked);
        var ap = worker.Ap;
        Assert.True(sim.Execute(new Salvage(worker.PlayerId!.Value, worker.Id, clicked!.Id)).Ok);
        Assert.Equal(ap - sim.Data.Salvage.Node("rubble")!.ApCost, worker.Ap);
    }

    [Fact]
    public void A_node_out_of_reach_a_depleted_one_or_an_empty_tile_is_no_salvage_click()
    {
        var (sim, worker, node) = NodeArena(new Pos(4, 0));
        Assert.Null(FieldSalvage.ClickedNode(sim.State, worker, node.Pos));

        worker.Pos = new Pos(1, 1);
        Assert.Null(FieldSalvage.ClickedNode(sim.State, worker, new Pos(2, 2)));

        node.LotsLeft = 0;
        Assert.Null(FieldSalvage.ClickedNode(sim.State, worker, node.Pos));
    }
}
