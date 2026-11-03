namespace Remnants.Core.Tests;

public class SalvageCostTests
{
    [Fact]
    public void Salvaging_any_node_takes_a_single_AP_so_a_turn_holds_several()
    {
        var data = ScavengeTestData.Real;
        Assert.All(data.Salvage.Nodes, n => Assert.Equal(1, n.ApCost));
        Assert.True(data.Rules.Turn.BaseAp >= 3 * data.Salvage.Nodes.Max(n => n.ApCost));
    }
}
