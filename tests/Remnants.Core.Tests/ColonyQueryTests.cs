using Remnants.Core.Colony;

namespace Remnants.Core.Tests;

/// <summary>Read-only queries the HUD shows (food pressure, production tooltips).</summary>
public class ColonyQueryTests
{
    private static ColonySim Fresh() => NewGame.Create(TestData.Real, 1, ScavengeTestData.Real);

    [Fact]
    public void Food_per_day_counts_everyone_at_home_by_life_stage_and_food_days_divide_the_stock()
    {
        var sim = Fresh();
        var food = sim.Data.Rules.Food;
        var expected = sim.State.Survivors.Sum(s => s.IsInfant ? food.InfantPerTick : s.IsChild ? food.ChildPerTick : food.AdultPerTick)
                       * sim.Data.Rules.TicksPerDay;
        Assert.Equal(expected, sim.FoodPerDay, 6);
        Assert.Equal(sim.FoodStock / expected, sim.FoodDays, 6);

        // A day of it really goes: run 24 ticks with nothing producing and the stock drops by about FoodPerDay.
        var before = sim.FoodStock;
        sim.Advance(sim.Data.Rules.TicksPerDay);
        Assert.Equal(expected, before - sim.FoodStock, 3);
    }

    [Fact]
    public void Away_members_do_not_count_and_nobody_home_means_food_lasts_forever()
    {
        var sim = Fresh();
        foreach (var s in sim.State.Survivors) s.Away = true;
        Assert.Equal(0, sim.FoodPerDay);
        Assert.True(double.IsPositiveInfinity(sim.FoodDays));
    }

    [Fact]
    public void Output_per_tick_matches_what_a_tick_adds_to_the_stockpile()
    {
        var sim = Fresh();
        foreach (var (res, amount) in sim.Data.Buildings["woodcutter"].Level(1).Cost) sim.State.Stockpile.Add(res, amount);
        var id = sim.Execute(new Construct("woodcutter")).EntityId!.Value;
        var building = sim.State.FindBuilding(id)!;
        Assert.Equal(0, sim.OutputPerTick(building));
        var adult = sim.State.Survivors.First(s => !s.IsChild);
        Assert.True(sim.Execute(new AssignJob(adult.Id, id)).Ok);

        var perTick = sim.OutputPerTick(building);
        Assert.True(perTick > 0);
        var before = sim.State.Stockpile.Get("wood");
        sim.Tick();
        Assert.Equal(perTick, sim.State.Stockpile.Get("wood") - before, 6);

        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId);
        Assert.Equal(0, sim.OutputPerTick(hall));
    }
}
