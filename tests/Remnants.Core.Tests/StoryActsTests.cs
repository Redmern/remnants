using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Story;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.StoryTestData;

namespace Remnants.Core.Tests;

/// <summary>Acts 2–4: unique survivors, choices with long-term effects, the Offer, chains, endings, the Codex, co-op story.</summary>
public class StoryActsTests
{
    private static ColonySim Joined(params string[] who)
    {
        var sim = NewStory();
        foreach (var id in who) Assert.NotNull(StoryCharacters.Join(sim, id));
        return sim;
    }

    [Fact]
    public void A_character_joins_as_a_unique_survivor_with_stats_perks_and_trait_hooks_and_stays()
    {
        var sim = Joined("cole");
        var cole = StoryCharacters.SurvivorOf(sim, "cole")!;
        Assert.Equal("Cole", cole.Name);
        Assert.Equal(Sex.Male, cole.Sex);
        Assert.Equal(50, cole.Get(Skill.Firearms));
        Assert.Contains("steady_aim", cole.Perks);
        Assert.Contains("quartermaster", cole.Traits);
        Assert.True(cole.TraitHooks[PerkHooks.MoodFoodSurplus] > 0);
        for (var i = 0; i < 3 * sim.Data.Rules.TicksPerDay; i++) sim.Tick();
        Assert.Same(cole, StoryCharacters.SurvivorOf(sim, "cole"));
        Assert.False(sim.State.Campaign!.Character("cole").Dead);
    }
}
