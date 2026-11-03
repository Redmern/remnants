using Remnants.Core.Colony;

namespace Remnants.Core.Tests;

/// <summary>
/// Early resource scarcity: early buildings cost less wood than before, and the first builds are paid for by the
/// starting stock plus one scavenge run (the Outskirts' least haul), not by the stock alone.
/// </summary>
public class EarlyCostTests
{
    /// <summary>Level 1 wood costs before this pass.</summary>
    public static TheoryData<string, double> WoodBefore => new()
    {
        { "shelter", 20 }, { "farm", 15 }, { "woodcutter", 10 }, { "quarry", 15 }, { "scrapyard", 20 }, { "workshop", 25 },
        { "med_bay", 30 }, { "kitchen", 30 }, { "tailor", 30 }, { "campfire", 8 }, { "hunting_lodge", 25 },
        { "watchtower", 25 }, { "radio_mast", 15 },
    };

    /// <summary>The bot's (and the tips') first builds.</summary>
    private static readonly string[] FirstBuilds = ["farm", "woodcutter", "quarry", "workshop", "scrapyard"];

    [Theory]
    [MemberData(nameof(WoodBefore))]
    public void Early_buildings_cost_less_wood_than_before(string defId, double before)
    {
        Assert.True(TestData.Real.Buildings[defId].Level(1).Cost.GetValueOrDefault("wood") < before);
    }

    [Fact]
    public void The_first_builds_take_the_starting_stock_plus_one_scavenge()
    {
        var data = TestData.Real;
        var stock = data.Rules.NewGame.Stock;
        var haul = ScavengeTestData.Real.Zones["outskirts"].Haul;
        foreach (var res in new[] { "wood", "stone" })
        {
            var need = FirstBuilds.Sum(id => data.Buildings[id].Level(1).Cost.GetValueOrDefault(res));
            var least = stock.GetValueOrDefault(res) + (haul.TryGetValue(res, out var range) ? range[0] : 0);
            Assert.True(need <= least, $"{res}: first builds need {need}, the stock and one run give at least {least}");
        }
        var wood = FirstBuilds.Sum(id => data.Buildings[id].Level(1).Cost.GetValueOrDefault("wood"));
        Assert.True(wood > stock["wood"], "the starting wood alone should not cover every first build");
    }

    [Fact]
    public void A_new_colony_can_put_up_farm_woodcutter_and_quarry_at_once()
    {
        var sim = NewGame.Create(TestData.Real, 1, ScavengeTestData.Real);
        foreach (var id in new[] { "farm", "woodcutter", "quarry" })
            Assert.True(sim.Execute(new Construct(id)).Ok, id);
    }
}
