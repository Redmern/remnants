using Remnants.Core.Colony;
using Remnants.Core.Raids;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;

namespace Remnants.Core.Tests;

/// <summary>
/// Real old saves (Fixtures/saves), written by the game's own code at the commits that made them, not edited copies of
/// today's: colony-v1.json (80b13f7: a Woodcutter staffed), colony-v2.json (6b1a2d4^: a Workshop on its one-recipe
/// system and an expedition out with its scavenge run seven rounds in, i.e. a v1 run inside) and scavenge-v1.json (that
/// run alone). Each goes through every migration to the current version with all of today's data on (weather, nights,
/// food, medicine, raids, vehicles, loot, schematics), plays on, and saves and loads again as current.
/// </summary>
public class SaveMigrationChainTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "saves", name));

    private static readonly RaidData Raids = RaidDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"))!;

    private static ColonySim LoadOld(string name)
    {
        var sim = SaveGame.Load(TestData.RealWithWeather, Fixture(name), ScavengeTestData.Real);
        sim.Raids = Raids;
        return sim;
    }

    [Theory]
    [InlineData("colony-v1.json", 1)]
    [InlineData("colony-v2.json", 2)]
    public void An_old_colony_migrates_to_the_current_version_and_plays_on(string fixture, int version)
    {
        Assert.Contains($"\"version\": {version}", Fixture(fixture));
        var sim = LoadOld(fixture);
        var people = sim.State.Survivors.Count;
        Assert.True(people >= 4);
        Assert.Contains(sim.State.Buildings, b => b.DefId == "woodcutter");
        Assert.NotEmpty(sim.State.Vehicles); // the starting handcart (vehicles migration)
        Assert.True(sim.State.Schematics.Migrated);

        var tick = sim.State.Tick;
        for (var i = 0; i < 3 * sim.Data.Rules.TicksPerDay; i++) sim.Tick();
        Assert.Equal(tick + 3 * sim.Data.Rules.TicksPerDay, sim.State.Tick);
        Assert.NotEmpty(sim.State.Survivors);

        var again = SaveGame.Save(sim.State);
        Assert.Contains($"\"version\": {SaveGame.Version}", again);
        var reloaded = SaveGame.Load(TestData.RealWithWeather, again, ScavengeTestData.Real);
        Assert.Equal(sim.State.Tick, reloaded.State.Tick);
        Assert.Equal(sim.State.Survivors.Count, reloaded.State.Survivors.Count);
    }

    [Fact]
    public void A_v2_workshop_keeps_making_its_recipe_as_an_order()
    {
        var sim = LoadOld("colony-v2.json");
        var workshop = sim.State.Buildings.Single(b => b.DefId == "workshop");
        Assert.Contains(workshop.Queue, o => o.RecipeId == "herbal_remedy");
    }

    [Fact]
    public void A_v2_expedition_resumes_its_run_mid_mission()
    {
        var sim = LoadOld("colony-v2.json");
        var run = sim.State.Expedition?.Scavenge;
        Assert.NotNull(run);
        Assert.Equal(7, run.Round);
        PlayRounds(new ScavengeSim(ScavengeTestData.Real, run), 3);
        var reloaded = SaveGame.Load(TestData.RealWithWeather, SaveGame.Save(sim.State), ScavengeTestData.Real);
        Assert.Equal(run.Round, reloaded.State.Expedition!.Scavenge!.Round);
    }

    [Fact]
    public void A_v1_scavenge_run_migrates_and_plays_on()
    {
        Assert.Contains("\"version\": 1", Fixture("scavenge-v1.json"));
        var run = ScavengeSave.Load(ScavengeTestData.Real, Fixture("scavenge-v1.json"));
        Assert.Equal(7, run.State.Round);
        Assert.All(run.State.Units, u => Assert.Null(u.Plan)); // an older run has no plans pending
        PlayRounds(run, 3);
        var again = ScavengeSave.Save(run.State);
        Assert.Contains($"\"version\": {ScavengeSave.Version}", again);
        Assert.Equal(run.State.Round, ScavengeSave.Load(ScavengeTestData.Real, again).State.Round);
    }

    /// <summary>Ends the turn of whoever is up until <paramref name="rounds"/> more rounds have passed (the AI acts in between).</summary>
    private static void PlayRounds(ScavengeSim run, int rounds)
    {
        var until = run.State.Round + rounds;
        for (var i = 0; i < 200 && run.State.Round < until && run.State.CurrentUnit is { PlayerControlled: true } u; i++)
            Assert.True(run.Execute(new EndTurn(u.PlayerId!.Value, u.Id)).Ok);
        Assert.True(run.State.Round >= until || run.State.CurrentUnit is null, $"stuck at round {run.State.Round}");
    }
}
