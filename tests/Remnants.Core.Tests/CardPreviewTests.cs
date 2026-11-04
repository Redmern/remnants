using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// The ability card targeting preview (<see cref="TacticsView.Targets"/>): every target the card could be aimed at, with
/// where things land, who is affected and what comes of it, or the reason it cannot be.
/// </summary>
public class CardPreviewTests
{
    private static (ScavengeSim Sim, Unit A, Unit B) Table(params string[] hand)
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(3, 2));
        var sim = Arena(10, 5, [a, b]);
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Hand = hand.ToList(), Points = 3, Budget = 3, HandSize = 5 });
        return (sim, a, b);
    }

    [Fact]
    public void A_shove_preview_shows_the_landing_tile_the_target_and_greys_out_the_rest_with_a_reason()
    {
        var (sim, a, b) = Table("shove");
        var far = SurvivorUnit(2, new(7, 2));
        far.Id = sim.State.NextUnitId++;
        sim.State.Units.Add(far);
        var options = TacticsView.Targets(sim, 1, a.Id, "shove");
        var onB = Assert.Single(options, o => o.UnitId == b.Id);
        Assert.True(onB.Ok, onB.Reason);
        Assert.Equal(new Pos(4, 2), onB.Landing);
        Assert.Contains(b.Id, onB.Affected);
        Assert.Contains("back", onB.Outcome);
        var onFar = Assert.Single(options, o => o.UnitId == far.Id);
        Assert.False(onFar.Ok);
        Assert.Contains("range", onFar.Reason);
        Assert.DoesNotContain(options, o => o.UnitId == a.Id && o.Ok);
    }

    [Fact]
    public void A_strike_preview_gives_the_hit_chance_and_damage()
    {
        var (sim, a, b) = Table("snap_strike");
        a.Skills[Survivors.Skill.Melee] = 40;
        var onB = Assert.Single(TacticsView.Targets(sim, 1, a.Id, "snap_strike"), o => o.UnitId == b.Id);
        Assert.True(onB.Ok, onB.Reason);
        Assert.Matches(@"\d+% to hit", onB.Outcome);
        Assert.Contains("damage", onB.Outcome);
    }

    [Fact]
    public void A_tile_card_offers_the_seen_tiles_in_range_and_lists_who_a_smoke_would_cover()
    {
        var (sim, a, b) = Table("smoke_bomb", "barricade");
        var smoke = TacticsView.Targets(sim, 1, a.Id, "smoke_bomb");
        Assert.All(smoke, o => Assert.InRange(o.Tile.Chebyshev(a.Pos), 0, 5));
        Assert.Contains("screen", Assert.Single(smoke, o => o.Tile == b.Pos).Reason); // it would cover a
        var past = Assert.Single(smoke, o => o.Tile == new Pos(4, 2));
        Assert.True(past.Ok);
        Assert.Contains(b.Id, past.Affected);
        Assert.DoesNotContain(a.Id, past.Affected);
        var barricade = TacticsView.Targets(sim, 1, a.Id, "barricade");
        Assert.Equal(9, barricade.Count); // a's tile and the ones round it
        Assert.False(Assert.Single(barricade, o => o.Tile == a.Pos).Ok);
        Assert.False(Assert.Single(barricade, o => o.Tile == b.Pos).Ok); // b stands there
    }

    [Fact]
    public void The_hand_counts_an_ability_card_playable_when_some_target_works()
    {
        var (sim, a, b) = Table("shove");
        Assert.Contains(a.Id, TacticsView.Hand(sim, 1)!.Cards.Single().PlayableBy);
        b.Pos = new(6, 2);
        Assert.DoesNotContain(a.Id, TacticsView.Hand(sim, 1)!.Cards.Single().PlayableBy);
    }

}
