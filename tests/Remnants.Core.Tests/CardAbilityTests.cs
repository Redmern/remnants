using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Ability cards (data/cards.json effects that change the board): displacement, swaps, steps, spins, AP, initiative,
/// barriers, smoke, strikes and disruption. Each effect is tried on a test card of its own, so the rules hold whatever
/// the starter cards are tuned to.
/// </summary>
public class CardAbilityTests
{
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    private static CardDef Card(string id, CardTarget target, CardEffect effect, int range = 0, int radius = 0, bool reaction = false) =>
        new() { Id = id, Name = id, Cost = 1, Target = target, Range = range, Radius = radius, Reaction = reaction, Effect = effect };

    /// <summary>The test cards, beside the real ones.</summary>
    private static readonly CardDef[] TestCards =
    [
        Card("t_push", CardTarget.Enemy, new() { Push = 2 }, range: 1),
        Card("t_push_far", CardTarget.Enemy, new() { Push = 2 }, range: 4),
        Card("t_pull", CardTarget.Enemy, new() { Pull = 2 }, range: 4),
        Card("t_pull_far", CardTarget.Enemy, new() { Pull = 9 }, range: 9),
    ];

    private static readonly ScavengeData Data = Sure.WithCards(Sure.Cards.Def with { Cards = [.. Sure.Cards.Def.Cards, .. TestCards] });

    /// <summary>An arena with P1's deck holding <paramref name="hand"/> and 3 points.</summary>
    private static ScavengeSim Table(IEnumerable<Unit> units, string[] hand, string[]? rows = null, int width = 10, int height = 5)
    {
        var sim = Arena(width, height, units, rows, data: Data);
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Hand = hand.ToList(), Points = 3, Budget = 3, HandSize = 5 });
        return sim;
    }

    private static ScavengeResult Play(ScavengeSim sim, Unit u, string card, Unit? target = null, Pos? tile = null) =>
        sim.Execute(new PlayCard(u.PlayerId!.Value, u.Id, card, target?.Id, Tile: tile));

    [Fact]
    public void Ability_cards_load_from_data_and_a_card_that_does_nothing_is_refused()
    {
        Assert.Equal(2, Data.Cards.Find("t_push")!.Effect.Push);
        Assert.True(Data.Cards.Find("t_push")!.Effect.IsAbility);
        Assert.False(Data.Cards.Find("steady_aim")!.Effect.IsAbility);
        Assert.Throws<Data.GameDataException>(() => new CardCatalog(new CardsDef { Cards = [Card("x", CardTarget.Self, new() { Push = 1 })] }));
        Assert.Throws<Data.GameDataException>(() => new CardCatalog(new CardsDef { Cards = [Card("x", CardTarget.Enemy, new() { Push = -1 })] }));
    }

    [Fact]
    public void A_push_moves_the_enemy_away_along_the_line_for_points_only()
    {
        var a = SurvivorUnit(1, new(2, 2));
        var b = SurvivorUnit(2, new(3, 2));
        var sim = Table([a, b], ["t_push"]);
        var ap = a.Ap;
        var r = Play(sim, a, "t_push", b);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(new Pos(5, 2), b.Pos);
        Assert.Equal(ap, a.Ap);
        Assert.Equal(2, SquadDecks.Of(sim.State, 1)!.Points);
        Assert.Equal(a.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void A_diagonal_push_follows_the_diagonal()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(2, 2));
        var sim = Table([a, b], ["t_push"]);
        Assert.True(Play(sim, a, "t_push", b).Ok);
        Assert.Equal(new Pos(4, 4), b.Pos);
    }

    [Theory]
    [InlineData(new[] { ".....#...." }, 4)] // a wall at x 5
    [InlineData(new[] { "......" }, 5)]     // the map edge (6 wide)
    public void A_push_stops_short_of_walls_and_the_map_edge(string[] row, int stopX)
    {
        var a = SurvivorUnit(1, new(2, 0));
        var b = SurvivorUnit(2, new(3, 0));
        var sim = Table([a, b], ["t_push_far"], rows: row, width: row[0].Length, height: 1);
        b.Pos = new(stopX - 1, 0);
        a.Pos = new(stopX - 2, 0);
        Assert.True(Play(sim, a, "t_push_far", b).Ok);
        Assert.Equal(new Pos(stopX, 0), b.Pos);
    }

    [Fact]
    public void A_push_stops_short_of_another_unit_and_never_shares_its_tile()
    {
        var a = SurvivorUnit(1, new(2, 2));
        var b = SurvivorUnit(2, new(3, 2));
        var c = SurvivorUnit(2, new(5, 2));
        var sim = Table([a, b, c], ["t_push"]);
        Assert.True(Play(sim, a, "t_push", b).Ok);
        Assert.Equal(new Pos(4, 2), b.Pos);
        Assert.Equal(new Pos(5, 2), c.Pos);
    }

    [Fact]
    public void A_push_with_nowhere_to_go_is_refused_and_costs_nothing()
    {
        var a = SurvivorUnit(1, new(2, 0));
        var b = SurvivorUnit(2, new(3, 0));
        var sim = Table([a, b], ["t_push"], rows: ["....#"], width: 5, height: 1);
        var r = Play(sim, a, "t_push", b);
        Assert.Equal(ScavengeError.InvalidTarget, r.Error);
        Assert.Equal(new Pos(3, 0), b.Pos);
        Assert.Equal(3, SquadDecks.Of(sim.State, 1)!.Points);
        Assert.Equal(["t_push"], SquadDecks.Of(sim.State, 1)!.Hand);
    }

    [Fact]
    public void A_pull_brings_the_enemy_in_and_stops_next_to_the_puller()
    {
        var a = SurvivorUnit(1, new(0, 2));
        var b = SurvivorUnit(2, new(4, 2));
        var sim = Table([a, b], ["t_pull", "t_pull_far"]);
        Assert.True(Play(sim, a, "t_pull", b).Ok);
        Assert.Equal(new Pos(2, 2), b.Pos);
        Assert.True(Play(sim, a, "t_pull_far", b).Ok);
        Assert.Equal(new Pos(1, 2), b.Pos);
    }

    [Fact]
    public void Unit_targets_are_checked_for_side_range_sight_and_presence()
    {
        var a = SurvivorUnit(1, new(0, 2));
        var friend = SurvivorUnit(1, new(1, 2));
        var far = SurvivorUnit(2, new(6, 4));
        var sim = Table([a, friend, far], ["t_push", "t_push_far"], rows: ["..........", "..........", "...#......", "..........", ".........."]);
        Assert.Equal(ScavengeError.InvalidTarget, Play(sim, a, "t_push", friend).Error);   // not an enemy
        Assert.Equal(ScavengeError.OutOfRange, Play(sim, a, "t_push", far).Error);         // range 1
        Assert.Equal(ScavengeError.InvalidTarget, Play(sim, a, "t_push_far").Error);       // no target
        far.Pos = new(4, 2);
        Assert.Equal(ScavengeError.NotVisible, Play(sim, a, "t_push_far", far).Error);     // nobody on the side sees it
        friend.Pos = new(5, 0);
        Assert.Equal(ScavengeError.NoLineOfSight, Play(sim, a, "t_push_far", far).Error);  // seen by the friend, but the wall at (3,2) is in a's way
        Assert.Equal(3, SquadDecks.Of(sim.State, 1)!.Points);
    }
}
