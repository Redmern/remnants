using Remnants.Core.Colony;
using Remnants.Core.Raids;

namespace Remnants.Core.Tests;

/// <summary>Vehicles and colony raids: a recalled party brings its vehicle home; a raid resolves with vehicles parked at home.</summary>
public class VehicleRaidTests
{
    private static Vehicle Give(ColonySim sim, string tier)
    {
        var def = sim.Data.VehicleTiers[tier];
        var v = new Vehicle { Id = sim.State.NextVehicleId++, TierId = tier, Name = def.Name, Condition = def.MaxCondition };
        sim.State.Vehicles.Add(v);
        return v;
    }

    [Fact]
    public void A_party_recalled_for_a_raid_brings_its_vehicle_home_and_half_its_fuel()
    {
        var sim = RaidTestData.Colony(townHallLevel: 3);
        sim.State.Stockpile.Add("fuel", 40);
        var truck = Give(sim, "truck");
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("suburb_ruins", [LoopTestData.Member(a)], VehicleId: truck.Id)).Ok);
        var fuel = sim.State.Expedition!.Fuel;
        RaidTestData.Raid(sim, ThreatKind.Husk, 4, Side.North, inTicks: 20);

        var r = sim.Execute(new RecallExpedition());
        Assert.True(r.Ok, r.Message);
        Assert.Null(sim.State.Expedition);
        Assert.False(truck.Away);
        Assert.Equal(40 - fuel + Math.Floor(fuel / 2), sim.State.Stockpile.Get("fuel"), 6);
    }

    [Fact]
    public void A_raid_resolves_with_vehicles_parked_at_home()
    {
        var sim = RaidTestData.Colony(townHallLevel: 2);
        for (var i = 0; i < 3; i++) sim.AddSurvivor();
        var cart = Give(sim, "buggy");
        RaidTestData.Raid(sim, ThreatKind.Raider, 4, Side.South, inTicks: 1);
        sim.Advance(6);
        Assert.Null(sim.State.Defense.Raid);
        Assert.Contains(cart, sim.State.Vehicles);
        Assert.False(cart.Away);
    }
}

/// <summary>Vehicle parts and fuel between players: a part's quality is in its item id, so a trade carries it.</summary>
public class VehicleTradeTests
{
    [Fact]
    public void Quality_parts_and_fuel_trade_at_their_value()
    {
        var rules = Remnants.Core.Trade.TradeRules.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var items = ScavengeTestData.Real;
        var offer = new[]
        {
            new Remnants.Core.Trade.TradeGood(Remnants.Core.Trade.GoodKind.Item, "wheel@fine", 2),
            new Remnants.Core.Trade.TradeGood(Remnants.Core.Trade.GoodKind.Resource, "fuel", 10),
        };
        Assert.Null(Remnants.Core.Trade.TradeGoods.Validate(offer, rules, TestData.Real, items));
        var fine = Remnants.Core.Schematics.TradeValues.Of(TestData.Real, items, "wheel@fine");
        var crude = Remnants.Core.Schematics.TradeValues.Of(TestData.Real, items, "wheel@crude");
        Assert.True(fine > crude && crude > 0);
        Assert.True(Remnants.Core.Schematics.TradeValues.OfResource(TestData.Real, "fuel") > 0);
    }
}

public class VehiclePowerTests
{
    [Fact]
    public void A_solar_charger_at_home_feeds_the_charging_bench_and_not_while_away()
    {
        var sim = LoopTestData.Colony();
        Assert.Equal(0, Power.VehicleChargeRate(sim.State, sim.Data));
        var buggy = new Vehicle { Id = sim.State.NextVehicleId++, TierId = "buggy", Name = "Buggy", Condition = 60 };
        buggy.Upgrades.Add(FittedUpgrade.Standard("solar_charger"));
        sim.State.Vehicles.Add(buggy);
        Assert.Equal(0.5, Power.VehicleChargeRate(sim.State, sim.Data), 6);
        buggy.Away = true;
        Assert.Equal(0, Power.VehicleChargeRate(sim.State, sim.Data));
    }
}
