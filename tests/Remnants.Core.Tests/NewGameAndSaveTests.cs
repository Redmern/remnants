using Remnants.Core.Colony;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class NewGameTests
{
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(12345UL)]
    public void Starts_with_three_adults_including_a_man_and_a_woman_and_two_children(ulong seed)
    {
        var sim = NewGame.Create(TestData.Real, seed);
        var adults = sim.State.Survivors.Where(s => s.LifeStage == LifeStage.Adult).ToList();
        var children = sim.State.Survivors.Where(s => s.LifeStage == LifeStage.Child).ToList();

        Assert.Equal(3, adults.Count);
        Assert.Equal(2, children.Count);
        Assert.Contains(adults, s => s.Sex == Sex.Male);
        Assert.Contains(adults, s => s.Sex == Sex.Female);
        Assert.Equal(5, sim.State.Survivors.Select(s => s.Name).Distinct().Count());
        Assert.All(children, c => Assert.True(c.AgeTicks < sim.Data.Rules.AdultAge * sim.Data.Rules.TicksPerYear));
    }

    [Fact]
    public void Starts_with_stock_a_level_1_town_hall_and_a_shelter_that_houses_everyone()
    {
        var sim = NewGame.Create(TestData.Real, 1);

        Assert.Contains(sim.State.Buildings, b => b is { DefId: "town_hall", Level: 1 });
        Assert.Contains(sim.State.Buildings, b => b is { DefId: "shelter", Level: 1 });
        foreach (var res in new[] { "wood", "stone", "food" }) Assert.True(sim.State.Stockpile.Get(res) > 0);
        Assert.All(sim.State.Survivors, s => Assert.True(s.HasBed));
    }

    [Fact]
    public void Same_seed_same_colony()
    {
        Assert.Equal(SaveGame.Save(NewGame.Create(TestData.Real, 9).State), SaveGame.Save(NewGame.Create(TestData.Real, 9).State));
        Assert.NotEqual(SaveGame.Save(NewGame.Create(TestData.Real, 9).State), SaveGame.Save(NewGame.Create(TestData.Real, 10).State));
    }
}

public class DeterminismAndSaveTests
{
    private static ColonySim PlayedColony(ulong seed, int ticks)
    {
        var sim = NewGame.Create(TestData.Real, seed);
        var farm = sim.Execute(new Construct("farm")).EntityId!.Value;
        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        sim.Execute(new AssignJob(adults[0].Id, farm));
        sim.Execute(new AssignJob(adults[1].Id, farm));
        sim.Advance(ticks);
        return sim;
    }

    [Fact]
    public void Same_seed_and_commands_give_identical_state()
    {
        Assert.Equal(SaveGame.Save(PlayedColony(5, 300).State), SaveGame.Save(PlayedColony(5, 300).State));
    }

    [Fact]
    public void Save_load_round_trip_preserves_state()
    {
        var sim = PlayedColony(5, 100);
        var someone = sim.State.Survivors[0];
        someone.Body.Lose(BodyPart.LeftArm);
        someone.Body.FitProsthetic(BodyPart.LeftArm, 1.2);
        sim.State.Survivors[1].Body.Injure(BodyPart.Head, 0.3);

        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(TestData.Real, json);

        Assert.Equal(json, SaveGame.Save(loaded.State));
        Assert.Equal(PartCondition.Prosthetic, loaded.State.Survivors[0].Body[BodyPart.LeftArm].Condition);
    }

    [Fact]
    public void Loaded_game_continues_exactly_like_the_original()
    {
        var original = PlayedColony(5, 100);
        var loaded = SaveGame.Load(TestData.Real, SaveGame.Save(original.State));

        original.Advance(200);
        loaded.Advance(200);

        Assert.Equal(SaveGame.Save(original.State), SaveGame.Save(loaded.State));
        Assert.Equal(original.Rng.NextULong(), loaded.Rng.NextULong());
    }

    [Fact]
    public void Wrong_save_version_is_rejected()
    {
        var json = SaveGame.Save(new ColonyState()).Replace("\"version\": 1", "\"version\": 99");
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SaveGame.Load(TestData.Real, json));
    }
}

public class SaveValidationTests
{
    [Fact]
    public void Save_with_a_building_removed_from_data_is_rejected()
    {
        var json = SaveGame.Save(NewGame.Create(TestData.Real, 1).State).Replace("\"shelter\"", "\"bunker\"");
        var ex = Assert.ThrowsAny<System.Text.Json.JsonException>(() => SaveGame.Load(TestData.Real, json));
        Assert.Contains("bunker", ex.Message);
    }

    [Fact]
    public void Save_with_a_level_above_data_max_is_rejected()
    {
        var state = NewGame.Create(TestData.Real, 1).State;
        state.Buildings[0].Level = 99;
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SaveGame.Load(TestData.Real, SaveGame.Save(state)));
    }
}
