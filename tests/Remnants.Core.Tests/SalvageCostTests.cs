namespace Remnants.Core.Tests;

public class SalvageCostTests
{
    [Fact]
    public void Salvaging_any_node_takes_a_single_AP()
    {
        var data = ScavengeTestData.Real;
        Assert.All(data.Salvage.Nodes, n => Assert.Equal(1, n.ApCost));
        Assert.True(data.Rules.Turn.BaseAp >= data.Salvage.Nodes.Max(n => n.ApCost));
    }
}
