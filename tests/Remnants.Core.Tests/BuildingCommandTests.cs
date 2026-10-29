using Remnants.Core.Colony;

namespace Remnants.Core.Tests;

public class BuildingCommandTests
{
    [Fact]
    public void Construct_spends_the_level_1_cost_from_data()
    {
        var sim = TestData.EmptyColony(stock: new() { ["wood"] = 100, ["stone"] = 100 });
        var cost = sim.Data.Buildings["woodcutter"].Level(1).Cost;

        var result = sim.Execute(new Construct("woodcutter"));

        Assert.True(result.Ok, result.Message);
        var building = sim.State.FindBuilding(result.EntityId!.Value)!;
        Assert.Equal(("woodcutter", 1), (building.DefId, building.Level));
        Assert.Equal(100 - cost["wood"], sim.State.Stockpile.Get("wood"));
        Assert.Equal(100 - cost["stone"], sim.State.Stockpile.Get("stone"));
    }

    [Fact]
    public void Construct_fails_without_resources_and_changes_nothing()
    {
        var sim = TestData.EmptyColony(stock: new() { ["wood"] = 1 });
        var result = sim.Execute(new Construct("woodcutter"));

        Assert.Equal(CommandError.CannotAfford, result.Error);
        Assert.Equal(1, sim.State.Stockpile.Get("wood"));
        Assert.Single(sim.State.Buildings);
    }

    [Fact]
    public void Unknown_building_type_is_rejected()
    {
        Assert.Equal(CommandError.UnknownBuildingType, TestData.EmptyColony().Execute(new Construct("castle")).Error);
    }

    [Fact]
    public void Only_one_town_hall()
    {
        Assert.Equal(CommandError.AlreadyExists, TestData.EmptyColony().Execute(new Construct("town_hall")).Error);
    }

    [Fact]
    public void Nothing_but_a_town_hall_can_be_built_without_one()
    {
        var sim = new ColonySim(TestData.Real, new ColonyState());
        sim.State.Stockpile.Add("wood", 1000);
        sim.State.Stockpile.Add("stone", 1000);
        Assert.Equal(CommandError.NoTownHall, sim.Execute(new Construct("woodcutter")).Error);
        Assert.True(sim.Execute(new Construct("town_hall")).Ok);
    }

    [Fact]
    public void Upgrade_spends_the_next_level_cost_and_raises_level()
    {
        var sim = TestData.EmptyColony(townHallLevel: 3);
        var quarry = sim.Build("quarry");
        var cost = sim.Data.Buildings["quarry"].Level(2).Cost;
        foreach (var (res, amount) in cost) sim.State.Stockpile.Add(res, amount);

        var result = sim.Execute(new Upgrade(quarry.Id));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(2, quarry.Level);
        Assert.All(cost.Keys, res => Assert.Equal(0, sim.State.Stockpile.Get(res), 9));
    }

    [Fact]
    public void Town_hall_level_caps_other_buildings()
    {
        var sim = TestData.EmptyColony(townHallLevel: 1, stock: new() { ["wood"] = 1000, ["stone"] = 1000, ["scrap"] = 1000 });
        var farm = sim.Build("farm");

        Assert.Equal(CommandError.TownHallLevelTooLow, sim.Execute(new Upgrade(farm.Id)).Error);

        var townHall = sim.State.Buildings.Single(b => b.DefId == "town_hall");
        Assert.True(sim.Execute(new Upgrade(townHall.Id)).Ok);
        Assert.True(sim.Execute(new Upgrade(farm.Id)).Ok);
        Assert.Equal(2, farm.Level);
    }

    [Fact]
    public void Cannot_upgrade_past_max_level()
    {
        var sim = TestData.EmptyColony(townHallLevel: 3, stock: new() { ["wood"] = 1000, ["stone"] = 1000, ["scrap"] = 1000 });
        var townHall = sim.State.Buildings.Single(b => b.DefId == "town_hall");
        Assert.Equal(CommandError.MaxLevel, sim.Execute(new Upgrade(townHall.Id)).Error);
    }

    [Fact]
    public void Upgrade_without_resources_fails()
    {
        var sim = TestData.EmptyColony(townHallLevel: 3);
        var farm = sim.Build("farm");
        Assert.Equal(CommandError.CannotAfford, sim.Execute(new Upgrade(farm.Id)).Error);
        Assert.Equal(1, farm.Level);
    }

    [Fact]
    public void Level_sets_worker_slots()
    {
        var sim = TestData.EmptyColony(townHallLevel: 3, stock: new() { ["wood"] = 1000, ["stone"] = 1000 });
        var farm = sim.Build("farm");
        var slots = sim.Data.Buildings["farm"].Levels.Select(l => l.WorkerSlots).ToList();
        var workers = Enumerable.Range(0, slots[1] + 1).Select(_ => sim.AddSurvivor()).ToList();

        for (var i = 0; i < slots[0]; i++) Assert.True(sim.Execute(new AssignJob(workers[i].Id, farm.Id)).Ok);
        Assert.Equal(CommandError.NoFreeSlot, sim.Execute(new AssignJob(workers[slots[0]].Id, farm.Id)).Error);

        Assert.True(sim.Execute(new Upgrade(farm.Id)).Ok);
        Assert.True(sim.Execute(new AssignJob(workers[slots[0]].Id, farm.Id)).Ok);
        Assert.Equal(slots[1], sim.State.WorkersAt(farm.Id).Count());
    }
}

public class JobCommandTests
{
    [Fact]
    public void Assign_moves_a_worker_between_buildings()
    {
        var sim = TestData.EmptyColony();
        var s = sim.AddSurvivor();
        var farm = sim.Build("farm");
        var quarry = sim.Build("quarry");

        Assert.True(sim.Execute(new AssignJob(s.Id, farm.Id)).Ok);
        Assert.True(sim.Execute(new AssignJob(s.Id, quarry.Id)).Ok);

        Assert.Equal(quarry.Id, s.AssignedBuildingId);
        Assert.Empty(sim.State.WorkersAt(farm.Id));
    }

    [Fact]
    public void Assign_validates_survivor_building_and_job()
    {
        var sim = TestData.EmptyColony();
        var s = sim.AddSurvivor();
        var townHall = sim.State.Buildings.Single();

        Assert.Equal(CommandError.UnknownSurvivor, sim.Execute(new AssignJob(999, townHall.Id)).Error);
        Assert.Equal(CommandError.UnknownBuilding, sim.Execute(new AssignJob(s.Id, 999)).Error);
        Assert.Equal(CommandError.NoJobAtBuilding, sim.Execute(new AssignJob(s.Id, townHall.Id)).Error);
    }

    [Fact]
    public void Unassign_makes_idle_and_rejects_idle()
    {
        var sim = TestData.EmptyColony();
        var s = sim.AddSurvivor();
        var farm = sim.Build("farm");
        sim.Execute(new AssignJob(s.Id, farm.Id));

        Assert.True(sim.Execute(new Unassign(s.Id)).Ok);
        Assert.Null(s.AssignedBuildingId);
        Assert.Equal(CommandError.NotAssigned, sim.Execute(new Unassign(s.Id)).Error);
        Assert.Equal(CommandError.UnknownSurvivor, sim.Execute(new Unassign(999)).Error);
    }
}
