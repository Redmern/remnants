using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

/// <summary>
/// Early zones pay in building materials (zones.json <c>haul</c>): a party that gets home from a plain run brings timber,
/// stone and scrap besides what it carried, less as the zone is picked over. Early on that beats the Woodcutter.
/// </summary>
public class ZoneHaulTests
{
    private static (ColonySim Sim, Survivor A, Survivor B) Launched(string zone)
    {
        var sim = LoopTestData.Colony(food: 200);
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        var launched = sim.Execute(new LaunchExpedition(zone, [new ExpeditionMember(a.Id), new ExpeditionMember(b.Id)]));
        Assert.True(launched.Ok, launched.Message);
        return (sim, a, b);
    }

    private static ColonySim Returned(string zone, double depletion = 0)
    {
        var (sim, a, b) = Launched(zone);
        sim.State.Expedition!.Depletion = depletion;
        var applied = sim.Execute(new ApplyExtractionResult(LoopTestData.Result(returned: [LoopTestData.Returned(a), LoopTestData.Returned(b)])));
        Assert.True(applied.Ok, applied.Message);
        // A far zone's party walks home first.
        for (var i = 0; i < 200 && sim.State.Expedition is not null; i++) sim.Advance(1);
        Assert.Null(sim.State.Expedition);
        return sim;
    }

    /// <summary>Wood two fresh adults (stats 45, no skill, no tools) cut at a level 1 Woodcutter in one day of work hours.</summary>
    private static double WoodcutterDay()
    {
        var sim = new ColonySim(TestData.RealWithNights, new ColonyState(), ScavengeTestData.Real);
        sim.State.Buildings.Add(new Building { Id = sim.State.NextBuildingId++, DefId = "town_hall", Level = 1 });
        var cutter = new Building { Id = sim.State.NextBuildingId++, DefId = "woodcutter", Level = 1, Plot = 1 };
        sim.State.Buildings.Add(cutter);
        for (var i = 0; i < 2; i++)
        {
            var s = sim.AddSurvivor(attributes: 45);
            Assert.True(sim.Execute(new AssignJob(s.Id, cutter.Id)).Ok);
        }
        return sim.OutputPerDay(cutter);
    }

    [Theory]
    [InlineData("outskirts")]
    [InlineData("scrapyard_flats")]
    public void A_first_run_into_an_early_zone_brings_home_wood(string zone)
    {
        var sim = Returned(zone);
        var haul = ScavengeTestData.Real.Zones[zone].Haul["wood"];

        Assert.InRange(sim.State.Stockpile.Get("wood"), haul[0], haul[1]);
        Assert.Equal((int)sim.State.Stockpile.Get("wood"), sim.State.LastReport!.Haul["wood"]);
    }

    [Theory]
    [InlineData("outskirts")]
    [InlineData("scrapyard_flats")]
    public void A_first_run_beats_the_same_time_at_a_level_1_woodcutter(string zone)
    {
        // The time a run takes: the road out and back, and at least a day for the run and getting over it.
        var def = ScavengeTestData.Real.Zones[zone];
        var days = Math.Max(1, 2 * def.TravelDays);
        var leastWood = def.Haul["wood"][0];

        Assert.True(leastWood >= 1.5 * WoodcutterDay() * days,
            $"{zone}: at least {leastWood} wood against {WoodcutterDay() * days:0.#} from the woodcutter in {days} days");
    }

    [Fact]
    public void A_picked_over_zone_hauls_less()
    {
        var fresh = Returned("outskirts").State.Stockpile.Get("wood");
        var pickedOver = Returned("outskirts", depletion: 0.6).State.Stockpile.Get("wood");

        Assert.True(pickedOver < fresh, $"{pickedOver} vs {fresh}");
    }

    [Fact]
    public void Nobody_home_means_no_haul()
    {
        var (sim, a, b) = Launched("outskirts");
        Assert.True(sim.Execute(new ApplyExtractionResult(LoopTestData.Result(deaths: [a, b]))).Ok);

        Assert.Equal(0, sim.State.Stockpile.Get("wood"));
        Assert.Empty(sim.State.LastReport!.Haul);
    }

    [Fact]
    public void Early_zones_haul_the_gathered_materials()
    {
        var zones = ScavengeTestData.Real.Zones;
        Assert.Contains("stone", zones["outskirts"].Haul.Keys);
        Assert.Contains("scrap", zones["scrapyard_flats"].Haul.Keys);
        Assert.Contains("stone", zones["scrapyard_flats"].Haul.Keys);
    }
}
