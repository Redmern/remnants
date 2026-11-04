using Remnants.Core.Items;
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
        Card("t_nudge", CardTarget.Enemy, new() { Push = 1 }, range: 1),
        Card("t_pull", CardTarget.Enemy, new() { Pull = 2 }, range: 4),
        Card("t_pull_far", CardTarget.Enemy, new() { Pull = 9 }, range: 9),
        Card("t_swap", CardTarget.Ally, new() { Swap = true }, range: 2),
        Card("t_step", CardTarget.Tile, new() { Step = 2 }),
        Card("t_spin", CardTarget.Enemy, new() { Spin = true }, range: 1),
        Card("t_ap", CardTarget.Ally, new() { GrantAp = 1 }, range: 3),
        Card("t_advance", CardTarget.Unit, new() { AdvanceInitiative = true }),
        Card("t_delay", CardTarget.Enemy, new() { DelayInitiative = true }),
        Card("t_barrier", CardTarget.Tile, new() { PlaceBarrier = 2 }, range: 2),
        Card("t_smoke", CardTarget.Tile, new() { Smoke = 2 }, range: 6, radius: 1),
        Card("t_strike", CardTarget.Enemy, new() { Strike = true }),
        Card("t_disrupt", CardTarget.Enemy, new() { Disrupt = true }, range: 4, reaction: true),
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

    [Fact]
    public void A_displaced_unit_with_a_readied_strike_loses_it_and_is_exposed_to_the_shover()
    {
        var b = SurvivorUnit(2, new(3, 2), weapon: "pipe");
        var a = SurvivorUnit(1, new(2, 2));
        var sim = Table([b, a], ["t_push"]);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
        Assert.Equal(a.Id, sim.State.CurrentUnitId);
        Assert.True(Play(sim, a, "t_push", b).Ok);
        Assert.False(TileStrikes.Readied(sim.State, b));
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentCanceled && e.UnitId == b.Id);
        Assert.True(Combat.ExposedTo(sim, b, a));
    }

    [Fact]
    public void A_unit_shoved_onto_a_declared_strike_is_hit_by_it_even_by_its_own_side()
    {
        // e1 readies a knife stab on the empty (5,2); P1 shoves e1's ally e2 from (3,2) onto it.
        var e1 = SurvivorUnit(2, new(6, 2), weapon: "knife");
        var e2 = SurvivorUnit(2, new(3, 2));
        var a = SurvivorUnit(1, new(2, 2));
        var sim = Table([e1, e2, a], ["t_push"]);
        Assert.True(sim.Execute(new AttackTile(2, e1.Id, new(5, 2), "cut", StrikeMode.Stab)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, e2.Id)).Ok);
        Assert.True(Play(sim, a, "t_push", e2).Ok);
        Assert.Equal(new Pos(5, 2), e2.Pos);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok); // the sub-round ends: the stab comes down
        Assert.True(e2.Hp < e2.MaxHp, "friendly fire on the shoved unit");
    }

    [Fact]
    public void A_friendly_unit_that_walks_onto_a_strike_is_not_hit_by_its_own_side()
    {
        var e1 = SurvivorUnit(2, new(6, 2), weapon: "knife");
        var e2 = SurvivorUnit(2, new(3, 2));
        var a = SurvivorUnit(1, new(0, 0));
        var sim = Table([e1, e2, a], []);
        Assert.True(sim.Execute(new AttackTile(2, e1.Id, new(5, 2), "cut", StrikeMode.Stab)).Ok);
        Assert.True(sim.Execute(new Move(2, e2.Id, new(5, 2))).Ok);
        Assert.True(sim.Execute(new EndTurn(2, e2.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(e2.MaxHp, e2.Hp);
    }

    [Fact]
    public void A_swap_trades_places_with_an_ally_in_range()
    {
        var a = SurvivorUnit(1, new(1, 2));
        var f = SurvivorUnit(1, new(3, 2));
        var far = SurvivorUnit(1, new(6, 2));
        var sim = Table([a, f, far], ["t_swap", "t_swap"]);
        Assert.Equal(ScavengeError.OutOfRange, Play(sim, a, "t_swap", far).Error);
        Assert.Equal(ScavengeError.InvalidTarget, Play(sim, a, "t_swap", a).Error);
        Assert.True(Play(sim, a, "t_swap", f).Ok);
        Assert.Equal(new Pos(3, 2), a.Pos);
        Assert.Equal(new Pos(1, 2), f.Pos);
    }

    [Fact]
    public void A_step_moves_the_player_free_of_ap_and_turns_it_to_the_nearest_seen_enemy()
    {
        // A corridor: b stands in a's way, and a cannot step past a hostile.
        var a = SurvivorUnit(1, new(0, 1));
        var b = SurvivorUnit(2, new(1, 1));
        var sim = Table([a, b], ["t_step", "t_step"], rows: ["##########", "..........", "##########"], height: 3);
        var ap = a.Ap;
        Assert.Equal(ScavengeError.Unreachable, Play(sim, a, "t_step", tile: new(2, 1)).Error);
        Assert.Equal(ScavengeError.InvalidTile, Play(sim, a, "t_step", tile: new(1, 0)).Error); // a wall
        b.Pos = new(6, 1);
        Assert.Equal(ScavengeError.Unreachable, Play(sim, a, "t_step", tile: new(3, 1)).Error); // three steps
        Assert.True(Play(sim, a, "t_step", tile: new(2, 1)).Ok);
        Assert.Equal(new Pos(2, 1), a.Pos);
        Assert.Equal(ap, a.Ap);
        Assert.Equal(new Pos(1, 0), a.Facing);
    }

    [Fact]
    public void A_spin_turns_the_enemy_s_back_to_the_player()
    {
        var a = SurvivorUnit(1, new(2, 2));
        var b = SurvivorUnit(2, new(3, 2));
        b.Facing = new Pos(-1, 0);
        var sim = Table([a, b], ["t_spin"]);
        Assert.Equal(FacingArc.Front, Combat.ArcOf(b, a.Pos));
        Assert.True(Play(sim, a, "t_spin", b).Ok);
        Assert.Equal(FacingArc.Rear, Combat.ArcOf(b, a.Pos));
    }

    [Fact]
    public void Grant_ap_gives_an_ally_or_the_player_more_ap_this_turn()
    {
        var a = SurvivorUnit(1, new(1, 2));
        var f = SurvivorUnit(1, new(3, 2));
        var sim = Table([a, f], ["t_ap", "t_ap"]);
        var (apA, apF) = (a.Ap, f.Ap);
        Assert.True(Play(sim, a, "t_ap", f).Ok);
        Assert.True(Play(sim, a, "t_ap", a).Ok);
        Assert.Equal(apF + 1, f.Ap);
        Assert.Equal(apA + 1, a.Ap);
    }

    [Fact]
    public void Advancing_puts_a_unit_next_and_delaying_puts_it_last_in_the_round()
    {
        var a = SurvivorUnit(1, new(0, 0));
        var x = SurvivorUnit(2, new(5, 0));
        var y = SurvivorUnit(2, new(5, 2));
        var f = SurvivorUnit(1, new(0, 2));
        var sim = Table([a, x, y, f], ["t_advance", "t_delay"]);
        Assert.True(Play(sim, a, "t_advance", f).Ok);
        Assert.Equal([a.Id, f.Id, x.Id, y.Id], sim.State.TurnOrder);
        Assert.True(Play(sim, a, "t_delay", x).Ok);
        Assert.Equal([a.Id, f.Id, y.Id, x.Id], sim.State.TurnOrder);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.InitiativeChanged && e.TargetId == x.Id);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(f.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void A_unit_that_already_had_its_turn_this_sub_round_cannot_be_moved_so_nobody_gets_an_extra_one()
    {
        var x = SurvivorUnit(2, new(5, 0));
        var a = SurvivorUnit(1, new(0, 0));
        var f = SurvivorUnit(1, new(0, 2));
        var sim = Table([x, a, f], ["t_advance", "t_delay"]);
        Assert.True(sim.Execute(new EndTurn(2, x.Id)).Ok);
        Assert.Equal(ScavengeError.InvalidTarget, Play(sim, a, "t_advance", x).Error);
        Assert.Equal(ScavengeError.InvalidTarget, Play(sim, a, "t_delay", x).Error);
        Assert.Equal(ScavengeError.InvalidTarget, Play(sim, a, "t_advance", a).Error); // already up
        Assert.Equal([x.Id, a.Id, f.Id], sim.State.TurnOrder);
    }

    [Fact]
    public void A_reordered_round_is_forgotten_at_the_next_round_start()
    {
        List<int> NextRound(bool delay)
        {
            var a = SurvivorUnit(1, new(0, 0));
            var x = SurvivorUnit(2, new(5, 0));
            var f = SurvivorUnit(1, new(0, 2));
            var sim = Table([a, x, f], ["t_delay"]);
            if (delay) Assert.True(Play(sim, a, "t_delay", x).Ok);
            foreach (var u in sim.State.TurnOrder.ToList()) Assert.True(sim.Execute(new EndTurn(sim.State.CurrentUnit!.PlayerId!.Value, sim.State.CurrentUnitId!.Value)).Ok);
            Assert.Equal(2, sim.State.Round);
            return sim.State.TurnOrder;
        }
        Assert.Equal(NextRound(false), NextRound(true));
    }

    /// <summary>Ends every turn until <paramref name="round"/> starts.</summary>
    private static void RunTo(ScavengeSim sim, int round)
    {
        while (sim.State.Round < round)
            Assert.True(sim.Execute(new EndTurn(sim.State.CurrentUnit!.PlayerId!.Value, sim.State.CurrentUnitId!.Value)).Ok);
    }

    [Fact]
    public void A_barrier_is_cover_on_an_empty_tile_for_its_rounds_then_goes()
    {
        var a = SurvivorUnit(1, new(1, 2));
        var b = SurvivorUnit(2, new(8, 2));
        var sim = Table([a, b], ["t_barrier", "t_barrier", "t_barrier", "t_barrier"]);
        var map = sim.State.Map;
        Assert.Equal(ScavengeError.OutOfRange, Play(sim, a, "t_barrier", tile: new(4, 2)).Error);
        Assert.Equal(ScavengeError.InvalidTile, Play(sim, a, "t_barrier", tile: a.Pos).Error);
        var before = Combat.CoverAgainst(sim.Data, map, a.Pos, b.Pos);
        Assert.True(Play(sim, a, "t_barrier", tile: new(2, 2)).Ok);
        Assert.Equal(Terrain.Cover, map[new Pos(2, 2)]);
        Assert.Equal(Terrain.Floor, map.Tiles[map.Index(new Pos(2, 2))]); // the map itself is not changed
        Assert.True(Combat.CoverAgainst(sim.Data, map, a.Pos, b.Pos) > before);
        Assert.Equal(ScavengeError.InvalidTile, Play(sim, a, "t_barrier", tile: new(2, 2)).Error); // one there already
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.FieldEffectPlaced);

        RunTo(sim, 2);
        Assert.Equal(Terrain.Cover, map[new Pos(2, 2)]);
        RunTo(sim, 3);
        Assert.Equal(Terrain.Floor, map[new Pos(2, 2)]);
        Assert.Empty(sim.State.FieldEffects);
    }

    [Fact]
    public void Smoke_blocks_sight_through_and_into_it_for_its_rounds()
    {
        var a = SurvivorUnit(1, new(1, 2));
        var b = SurvivorUnit(2, new(7, 2));
        var c = SurvivorUnit(2, new(4, 0));
        var sim = Table([a, b, c], ["t_smoke"], width: 10, height: 5);
        Assert.True(Detection.SideSees(sim.Data, sim.State, a.Side, b));
        Assert.True(Play(sim, a, "t_smoke", tile: new(4, 2)).Ok);
        Assert.False(Detection.SideSees(sim.Data, sim.State, a.Side, b));      // through the cloud
        c.Pos = new(4, 1);
        Assert.False(Detection.SideSees(sim.Data, sim.State, a.Side, c));      // standing in it
        Assert.False(Combat.Clear(sim.Data, sim.State.Map, a.Pos, b.Pos));      // no shot through it
        c.Pos = new(2, 2);
        Assert.True(Detection.SideSees(sim.Data, sim.State, a.Side, c));       // next to a, outside it
        RunTo(sim, 3);
        Assert.True(Detection.SideSees(sim.Data, sim.State, a.Side, b));
    }

    [Fact]
    public void Barriers_and_smoke_survive_a_save()
    {
        var a = SurvivorUnit(1, new(1, 2));
        var b = SurvivorUnit(2, new(7, 2));
        var sim = Table([a, b], ["t_barrier", "t_smoke"]);
        Assert.True(Play(sim, a, "t_barrier", tile: new(2, 2)).Ok);
        Assert.True(Play(sim, a, "t_smoke", tile: new(4, 2)).Ok);
        var loaded = ScavengeSave.Load(sim.Data, ScavengeSave.Save(sim.State));
        Assert.Equal(2, loaded.State.FieldEffects.Count);
        Assert.Equal(Terrain.Cover, loaded.State.Map[new Pos(2, 2)]);
        Assert.False(Detection.SideSees(loaded.Data, loaded.State, a.Side, loaded.State.FindUnit(b.Id)!));
    }

    [Fact]
    public void A_strike_attacks_the_enemy_now_with_the_best_blow_in_reach_for_no_ap()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 2));
        var sim = Table([a, b], ["t_strike", "t_strike"]);
        var ap = a.Ap;
        Assert.Equal(ScavengeError.OutOfRange, Play(sim, a, "t_strike", b).Error);
        b.Pos = new(3, 2);
        Assert.True(Play(sim, a, "t_strike", b).Ok);
        Assert.True(b.Hp < b.MaxHp);
        Assert.Equal(ap, a.Ap);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Attacked && e.UnitId == a.Id);
    }

    [Fact]
    public void A_disrupt_reaction_breaks_a_seen_enemy_strike_on_another_unit_s_turn_and_exposes_the_striker()
    {
        var f = SurvivorUnit(1, new(0, 0));
        var b = SurvivorUnit(2, new(3, 2), weapon: "pipe");
        var g = SurvivorUnit(1, new(0, 4));
        var a = SurvivorUnit(1, new(2, 2));
        var sim = Table([f, b, g, a], ["t_disrupt", "t_disrupt"]);
        Assert.Equal(ScavengeError.NoIntent, Play(sim, a, "t_disrupt", b).Error); // no strike yet
        Assert.True(sim.Execute(new EndTurn(1, f.Id)).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
        Assert.Equal(g.Id, sim.State.CurrentUnitId); // g is up, a is not
        Assert.True(Play(sim, a, "t_disrupt", b).Ok);
        Assert.False(TileStrikes.Readied(sim.State, b));
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == b.Id);
        Assert.True(Combat.ExposedTo(sim, b, a));
    }

    [Fact]
    public void A_strike_lands_on_whoever_is_shoved_onto_its_tile_and_its_dodged_target_leaves_it_exposed()
    {
        // e1 readies a swing at f on (4,2). f steps off; a shoves e1's ally e2 onto (4,2): e1's blow comes down on e2.
        var e1 = SurvivorUnit(2, new(5, 2), attributes: 90, weapon: "pipe");
        var e2 = SurvivorUnit(2, new(3, 1));
        var f = SurvivorUnit(1, new(4, 2));
        var a = SurvivorUnit(1, new(2, 0));
        var sim = Table([e1, e2, f, a], ["t_nudge"]);
        Assert.True(sim.Execute(new AttackTile(2, e1.Id, new(4, 2), "swing")).Ok);
        Assert.True(sim.Execute(new EndTurn(2, e2.Id)).Ok);
        Assert.True(sim.Execute(new Move(1, f.Id, new(4, 4))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, f.Id)).Ok);
        Assert.True(Play(sim, a, "t_nudge", e2).Ok);
        Assert.Equal(new Pos(4, 2), e2.Pos);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.False(TileStrikes.Readied(sim.State, e1)); // it came down as the sub-round ended
        Assert.True(e2.Hp < e2.MaxHp);
        Assert.Equal(f.MaxHp, f.Hp);
        Assert.True(Combat.Exposed(sim, e1));
    }

    [Fact]
    public void Barriers_never_read_as_a_salvageable_wreck()
    {
        var a = SurvivorUnit(1, new(2, 2));
        var sim = Table([a], ["t_barrier", "t_barrier"]);
        var before = Salvaging.Wrecks(sim.State.Map).Count;
        Assert.True(Play(sim, a, "t_barrier", tile: new(3, 2)).Ok);
        Assert.True(Play(sim, a, "t_barrier", tile: new(4, 2)).Ok);
        Assert.Equal(before, Salvaging.Wrecks(sim.State.Map).Count);
    }

    [Fact]
    public void A_barrier_does_not_go_up_on_a_loot_pile()
    {
        var a = SurvivorUnit(1, new(2, 2));
        var sim = Table([a], ["t_barrier"]);
        FieldLoot.Pile(sim.State, new Pos(3, 2), LootPileKind.Remains, "x").Items.Add("bandage");
        Assert.Equal(ScavengeError.InvalidTile, Play(sim, a, "t_barrier", tile: new(3, 2)).Error);
    }

    [Fact]
    public void Forced_and_card_moves_trip_motion_sensors_like_any_move()
    {
        var a = SurvivorUnit(1, new(0, 1));
        var b = SurvivorUnit(2, new(9, 1));
        var sim = Table([a, b], ["t_step", "t_step"], rows: ["##########", "..........", "##########"], height: 3);
        sim.State.Sensors.Add(new Sensor(1, b.Side, new Pos(4, 1), 1, "motion_sensor"));
        Assert.True(Play(sim, a, "t_step", tile: new(2, 1)).Ok);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.SensorTripped);
        Assert.True(Play(sim, a, "t_step", tile: new(3, 1)).Ok);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.SensorTripped);
    }

    [Fact]
    public void The_swapper_is_not_left_open_to_its_own_side_s_strikes()
    {
        var a = SurvivorUnit(1, new(1, 2));
        var f = SurvivorUnit(1, new(2, 2));
        var sim = Table([a, f], ["t_swap"]);
        Assert.True(Play(sim, a, "t_swap", f).Ok);
        Assert.False(a.Displaced);
        Assert.True(f.Displaced);
    }

    [Fact]
    public void Grant_ap_cannot_wake_a_unit_that_has_spent_its_ap()
    {
        var a = SurvivorUnit(1, new(1, 2));
        var f = SurvivorUnit(1, new(3, 2));
        var sim = Table([a, f], ["t_ap"]);
        f.Ap = 0;
        Assert.Equal(ScavengeError.InvalidTarget, Play(sim, a, "t_ap", f).Error);
    }

    [Fact]
    public void Hurrying_the_next_unit_or_holding_back_the_last_is_allowed_and_changes_nothing()
    {
        // Refusing it would tell a client about units it cannot see queued in between.
        var a = SurvivorUnit(1, new(0, 0));
        var f = SurvivorUnit(1, new(0, 2));
        var x = SurvivorUnit(2, new(5, 0));
        var sim = Table([a, f, x], ["t_advance", "t_delay"]);
        Assert.True(Play(sim, a, "t_advance", f).Ok);
        Assert.True(Play(sim, a, "t_delay", x).Ok);
        Assert.Equal([a.Id, f.Id, x.Id], sim.State.TurnOrder);
    }

    [Fact]
    public void A_disrupt_also_calls_off_a_readied_tile_strike_and_exposes_the_striker()
    {
        var b = SurvivorUnit(2, new(3, 2), weapon: "knife");
        var f = SurvivorUnit(1, new(4, 2));
        var a = SurvivorUnit(1, new(1, 2));
        var sim = Table([b, f, a], ["t_disrupt"]);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, f.Pos, "cut", StrikeMode.Stab)).Ok);
        Assert.Equal(f.Id, sim.State.CurrentUnitId);
        Assert.True(Play(sim, a, "t_disrupt", b).Ok); // a reaction on f's turn
        Assert.False(TileStrikes.Readied(sim.State, b));
        Assert.True(Combat.ExposedTo(sim, b, a));
        Assert.True(sim.Execute(new EndTurn(1, f.Id)).Ok);
        Assert.Equal(f.MaxHp, f.Hp);
    }

    [Fact]
    public void Smoke_cannot_be_thrown_over_the_player_itself()
    {
        var a = SurvivorUnit(1, new(2, 2));
        var sim = Table([a], ["t_smoke", "t_smoke"]);
        Assert.Equal(ScavengeError.InvalidTile, Play(sim, a, "t_smoke", tile: a.Pos).Error);
        Assert.Equal(ScavengeError.InvalidTile, Play(sim, a, "t_smoke", tile: new(3, 2)).Error); // radius 1 would cover a
        Assert.True(Play(sim, a, "t_smoke", tile: new(4, 2)).Ok); // a screen between a and what is beyond
    }

    [Fact]
    public void Ally_cards_go_only_on_the_player_s_own_units_not_a_co_op_partner_s()
    {
        var a = SurvivorUnit(1, new(1, 2));
        var partner = SurvivorUnit(3, new(2, 2));
        partner.Side = a.Side; // allied: one team side
        var x = SurvivorUnit(2, new(8, 2));
        var sim = Table([a, partner, x], ["t_ap", "t_swap", "t_advance"]);
        Assert.Equal(ScavengeError.NotYourUnit, Play(sim, a, "t_ap", partner).Error);
        Assert.Equal(ScavengeError.NotYourUnit, Play(sim, a, "t_swap", partner).Error);
        Assert.Equal(ScavengeError.NotYourUnit, Play(sim, a, "t_advance", partner).Error);
    }

    [Fact]
    public void A_step_is_a_move_of_the_player_s_own_legs_hurt_legs_and_a_carried_body_hold_it()
    {
        var a = SurvivorUnit(1, new(0, 1));
        var b = SurvivorUnit(2, new(9, 1));
        var sim = Table([a, b], ["t_step"], rows: ["##########", "..........", "##########"], height: 3);
        a.Body.Injure(Survivors.BodyPart.LeftLeg, 1);
        a.Body.Injure(Survivors.BodyPart.RightLeg, 0.9);
        var reach = ProfileDeriver.LegReach(2, a.Body);
        Assert.True(reach < 2);
        Assert.Equal(ScavengeError.Unreachable, Play(sim, a, "t_step", tile: new(2, 1)).Error);
    }

    [Fact]
    public void A_step_is_the_player_s_own_move_and_draws_overwatch()
    {
        // b watches the corridor on overwatch; a steps within its sight and it fires.
        var b = SurvivorUnit(2, new(5, 1), weapon: "pistol");
        var a = SurvivorUnit(1, new(0, 1));
        var sim = Table([b, a], ["t_step"], rows: ["##########", "..........", "##########"], height: 3, width: 10);
        Assert.True(sim.Execute(new CommitOverwatch(2, b.Id, Arc: new Pos(-1, 0))).Ok);
        var before = sim.State.Events.Count;
        Assert.True(Play(sim, a, "t_step", tile: new(2, 1)).Ok);
        Assert.Contains(sim.State.Events.Skip(before), e => e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed && e.UnitId == b.Id && e.TargetId == a.Id);
    }

    [Fact]
    public void A_shove_only_counts_toward_friendly_fire_until_the_unit_s_next_turn()
    {
        // e2 is shoved onto e1's readied tile, then takes its own turn and stays: e1's blow (at the sub-round's end) passes it by.
        var e1 = SurvivorUnit(2, new(5, 2), weapon: "pipe");
        var a = SurvivorUnit(1, new(2, 0));
        var e2 = SurvivorUnit(2, new(3, 1));
        var sim = Table([e1, a, e2], ["t_nudge"]);
        Assert.True(sim.Execute(new AttackTile(2, e1.Id, new(4, 2), "swing")).Ok);
        Assert.True(Play(sim, a, "t_nudge", e2).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(e2.Id, sim.State.CurrentUnitId);
        Assert.False(e2.Displaced); // its own sub-turn began
        Assert.True(sim.Execute(new EndTurn(2, e2.Id)).Ok);
        Assert.False(TileStrikes.Readied(sim.State, e1));
        Assert.Equal(e2.MaxHp, e2.Hp);
    }

    [Fact]
    public void A_unit_shoved_into_an_overwatch_zone_is_not_fired_at()
    {
        // b (P2) watches the corridor; a (P1) shoves P3's e toward it: being moved is no move of e's own.
        var b = SurvivorUnit(2, new(5, 1), weapon: "pistol");
        var a = SurvivorUnit(1, new(0, 1));
        var e = SurvivorUnit(3, new(1, 1));
        var sim = Table([b, a, e], ["t_push"], rows: ["##########", "..........", "##########"], height: 3, width: 10);
        Assert.True(sim.Execute(new CommitOverwatch(2, b.Id, Arc: new Pos(-1, 0))).Ok);
        var before = sim.State.Events.Count;
        Assert.True(Play(sim, a, "t_push", e).Ok);
        Assert.Equal(new Pos(3, 1), e.Pos);
        Assert.DoesNotContain(sim.State.Events.Skip(before), x => x.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed && x.UnitId == b.Id);
    }
}
