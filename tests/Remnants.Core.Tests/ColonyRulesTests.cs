using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class ProductionTests
{
    [Theory]
    [InlineData("woodcutter", "wood")]
    [InlineData("quarry", "stone")]
    [InlineData("scrapyard", "scrap")]
    [InlineData("farm", "food")]
    public void Gathering_buildings_produce_through_assigned_workers(string building, string resource)
    {
        var sim = TestData.EmptyColony(stock: new() { ["food"] = 100 });
        var worker = sim.AddSurvivor();
        var b = sim.Build(building);
        var before = sim.State.Stockpile.Get(resource);

        sim.Tick();
        var idleDelta = sim.State.Stockpile.Get(resource) - before;

        sim.Execute(new AssignJob(worker.Id, b.Id));
        var expected = JobOutput.PerTick(sim.Data.Buildings[building], 1, sim.Data.Jobs[sim.Data.Buildings[building].Job!], worker, sim.Data.Rules.Output);
        var start = sim.State.Stockpile.Get(resource);
        sim.Tick();

        var foodEaten = resource == "food" ? sim.Data.Rules.Food.AdultPerTick : 0;
        Assert.Equal(-foodEaten, idleDelta, 9);
        Assert.Equal(expected - foodEaten, sim.State.Stockpile.Get(resource) - start, 9);
        Assert.True(expected > 0);
    }

    [Fact]
    public void Higher_level_produces_more()
    {
        double Produce(int level)
        {
            var sim = TestData.EmptyColony(townHallLevel: 3, stock: new() { ["wood"] = 1000, ["stone"] = 1000, ["food"] = 100 });
            var w = sim.AddSurvivor();
            var b = sim.Build("woodcutter");
            while (b.Level < level) Assert.True(sim.Execute(new Upgrade(b.Id)).Ok);
            sim.Execute(new AssignJob(w.Id, b.Id));
            var start = sim.State.Stockpile.Get("wood");
            sim.Tick();
            return sim.State.Stockpile.Get("wood") - start;
        }

        Assert.True(Produce(2) > Produce(1));
        Assert.True(Produce(3) > Produce(2));
    }
}

public class FoodTests
{
    [Fact]
    public void Everyone_eats_each_tick()
    {
        var sim = TestData.EmptyColony(stock: new() { ["food"] = 10 });
        sim.AddSurvivor();
        sim.AddSurvivor(LifeStage.Child);
        var food = sim.Data.Rules.Food;

        sim.Tick();

        Assert.Equal(10 - food.AdultPerTick - food.ChildPerTick, sim.State.Stockpile.Get("food"), 9);
    }

    [Fact]
    public void Starvation_damages_then_kills()
    {
        var sim = TestData.EmptyColony();
        sim.Build("shelter");
        var s = sim.AddSurvivor();
        var damage = sim.Data.Rules.Food.StarvationDamagePerTick;

        sim.Tick();
        Assert.Equal(Survivor.MaxHealth - damage, s.Health, 9);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Starving && e.SurvivorId == s.Id);

        var ticksToDeath = (int)Math.Ceiling(Survivor.MaxHealth / damage);
        sim.Advance(ticksToDeath - 2);
        Assert.Contains(s, sim.State.Survivors);
        sim.Tick();
        Assert.DoesNotContain(s, sim.State.Survivors);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Died && e.SurvivorId == s.Id);
    }

    [Fact]
    public void Fed_survivors_recover_health()
    {
        var sim = TestData.EmptyColony(stock: new() { ["food"] = 100 });
        var s = sim.AddSurvivor();
        s.Health = 50;
        sim.Tick();
        Assert.Equal(50 + sim.Data.Rules.Food.RecoveryPerTick, s.Health, 9);
    }

    [Fact]
    public void Children_are_fed_first_when_food_is_short()
    {
        var sim = TestData.EmptyColony();
        var adult = sim.AddSurvivor();
        var child = sim.AddSurvivor(LifeStage.Child);
        sim.State.Stockpile.Add("food", sim.Data.Rules.Food.ChildPerTick);

        sim.Tick();

        Assert.Equal(0, child.StarvingTicks);
        Assert.Equal(1, adult.StarvingTicks);
    }
}

public class HousingTests
{
    [Fact]
    public void Homeless_survivor_leaves_after_the_grace_timer()
    {
        var sim = TestData.EmptyColony(stock: new() { ["food"] = 1000 });
        var s = sim.AddSurvivor();
        var grace = sim.Data.Rules.Housing.HomelessGraceTicks;

        sim.Advance(grace);
        Assert.Contains(s, sim.State.Survivors);
        Assert.Equal(grace, s.HomelessTicks);

        sim.Tick();
        Assert.DoesNotContain(s, sim.State.Survivors);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Left && e.SurvivorId == s.Id);
    }

    [Fact]
    public void Finding_a_bed_resets_the_timer()
    {
        var sim = TestData.EmptyColony(stock: new() { ["food"] = 1000 });
        var s = sim.AddSurvivor();
        sim.Advance(10);

        sim.Build("shelter");
        sim.Tick();

        Assert.True(s.HasBed);
        Assert.Equal(0, s.HomelessTicks);
    }

    [Fact]
    public void Beds_are_capped_by_shelter_level_and_existing_residents_keep_theirs()
    {
        var sim = TestData.EmptyColony(townHallLevel: 2, stock: new() { ["food"] = 1000 });
        var shelter = sim.Build("shelter");
        var beds = sim.Data.Buildings["shelter"].Level(1).Beds;
        var residents = Enumerable.Range(0, beds).Select(_ => sim.AddSurvivor()).ToList();
        sim.Tick();

        var newcomer = sim.AddSurvivor();
        sim.Tick();
        Assert.All(residents, r => Assert.True(r.HasBed));
        Assert.False(newcomer.HasBed);

        foreach (var (res, amount) in sim.Data.Buildings["shelter"].Level(2).Cost) sim.State.Stockpile.Add(res, amount);
        Assert.True(sim.Execute(new Upgrade(shelter.Id)).Ok);
        sim.Tick();
        Assert.True(newcomer.HasBed);
        Assert.Equal(sim.Data.Buildings["shelter"].Level(2).Beds, sim.BedCapacity);
    }
}

public class AgingTests
{
    [Fact]
    public void Child_becomes_adult_at_the_adult_age()
    {
        var sim = TestData.EmptyColony(stock: new() { ["food"] = 1000 });
        sim.Build("shelter");
        var child = sim.AddSurvivor(LifeStage.Child);
        child.AgeTicks = sim.Data.Rules.AdultAge * sim.Data.Rules.TicksPerYear - 2;

        sim.Tick();
        Assert.Equal(LifeStage.Child, child.LifeStage);
        sim.Tick();
        Assert.Equal(LifeStage.Adult, child.LifeStage);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.BecameAdult);
    }
}

public class StockpileTests
{
    [Fact]
    public void Float_drift_does_not_block_an_exact_amount()
    {
        var stock = new Colony.Stockpile();
        for (var i = 0; i < 10; i++) stock.Add("food", 0.1);  // 0.9999999999999999
        Assert.True(stock.TryTake("food", 1.0));
        Assert.Equal(0, stock.Get("food"));
    }
}
