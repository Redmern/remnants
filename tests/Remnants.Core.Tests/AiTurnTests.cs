using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Enemy AI acts live and telegraphs its blows as readied strikes, authored enemies play cards, and AI turns always end.</summary>
public class AiTurnTests
{
    [Fact]
    public void A_turret_telegraphs_only_its_aim()
    {
        ZoneDevice turret = null!;
        var u = SurvivorUnit(1, new Pos(1, 1));
        var sim = Arena(10, 8, [u], setup: (d, s) => turret = Hacking.Place(d, s, DeviceKind.Turret, new Pos(4, 1)));
        var gun = sim.State.FindUnit(turret.UnitId!.Value)!;
        ScavengeAi.TakeTurn(sim, gun);
        var strike = Strikes(sim).First(s => s.UnitId == gun.Id);
        Assert.Equal(new Pos(4, 1), gun.Pos);
        Assert.Equal(u.Pos, strike.Tile);
    }

    [Fact]
    public void A_linked_squad_telegraphs_at_one_shared_focus()
    {
        var a = SurvivorUnit(pos: new(2, 2));
        var b = SurvivorUnit(pos: new(2, 6));
        b.Hp = 60;
        var l1 = EnemyUnit("linked", new(8, 3));
        var l2 = EnemyUnit("linked", new(8, 5));
        var sim = Arena(11, 9, [a, b, l1, l2]);
        ScavengeAi.TakeTurn(sim, l1);
        ScavengeAi.TakeTurn(sim, l2);
        Assert.Equal(b.Pos, Strikes(sim).First(s => s.UnitId == l1.Id).Tile);
        Assert.Equal(b.Pos, Strikes(sim).First(s => s.UnitId == l2.Id).Tile);
        Assert.Equal(b.Id, l1.Ai.TargetId);
    }

    [Fact]
    public void An_enemy_plans_its_move_and_blow_and_nothing_happens_until_they_resolve()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var husk = EnemyUnit("husk", new(3, 0));
        var sim = Arena(8, 3, [u, husk]);
        var ap = husk.Ap;
        ScavengeAi.TakeTurn(sim, husk);
        Assert.True(Plans.Anchor(sim.State, husk).Chebyshev(u.Pos) < 3);
        Assert.Equal(new Pos(3, 0), husk.Pos);
        Assert.True(husk.Ap < ap);
        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == husk.Id && e.Type is ScavengeEventType.Moved or ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void An_enemy_with_a_target_in_reach_telegraphs_the_blow_too()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var near = EnemyUnit("husk", new(1, 0));
        var sim = Arena(8, 3, [u, near]);
        ScavengeAi.TakeTurn(sim, near);
        Assert.Equal(u.Pos, Strikes(sim).First(s => s.UnitId == near.Id).Tile);
        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == near.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);

        // A turret in reach still only telegraphs its aim.
        ZoneDevice turret = null!;
        var v = SurvivorUnit(1, new Pos(1, 1));
        var sim2 = Arena(10, 8, [v], setup: (d, s) => turret = Hacking.Place(d, s, DeviceKind.Turret, new Pos(4, 1)));
        ScavengeAi.TakeTurn(sim2, sim2.State.FindUnit(turret.UnitId!.Value)!);
        Assert.Contains(Strikes(sim2), s => s.UnitId == turret.UnitId);
        Assert.DoesNotContain(sim2.State.Events, e => e.UnitId == turret.UnitId && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void Ai_against_ai_rounds_always_end()
    {
        // A crowd of every AI kind against an overridden Linked and recruits on the player's side, the player's own unit
        // walled off so only the AI fights. Thirty rounds must pass, each EndTurn returning.
        var u = SurvivorUnit(1, new Pos(0, 0));
        var turned = EnemyUnit("linked", new Pos(6, 6));
        var recruit = NpcUnit(new Pos(7, 7));
        ZoneDevice turret = null!;
        var units = new List<Unit> { u, turned, recruit, EnemyUnit("husk", new Pos(12, 4)), EnemyUnit("husk_crawler", new Pos(12, 10)),
            EnemyUnit("linked", new Pos(13, 7)), EnemyUnit("linked", new Pos(13, 9)), EnemyUnit("tally_scav", new Pos(10, 12)), EnemyUnit("cole", new Pos(11, 2)) };
        var rows = Enumerable.Range(0, 14).Select(y => y switch
        {
            0 => ".#............",
            1 => "##............",
            _ => "..............",
        }).ToArray();
        var sim = Arena(14, 14, units, rows: rows, setup: (d, s) => turret = Hacking.Place(d, s, DeviceKind.Turret, new Pos(9, 5)));
        turned.Side = u.Side;
        turned.OverrideUntilRound = 99;
        recruit.Side = u.Side;
        recruit.PlayerId = 1;
        recruit.FollowingId = turned.Id;

        var start = sim.State.Round;
        for (var i = 0; i < 30 && !sim.State.Finished; i++)
        {
            Assert.Equal(u.Id, sim.State.CurrentUnitId);
            Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        }
        Assert.Equal(start + 30, sim.State.Round);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentResolved);
    }

    [Fact]
    public void Authored_enemies_bring_a_deck_and_play_their_cards_out_of_the_players_hearing()
    {
        var cole = Real.Enemies["cole"];
        Assert.NotEmpty(cole.Cards);
        Assert.NotEmpty(Real.Enemies["linked_warden"].Cards);
        Assert.Empty(Real.Enemies["husk"].Cards);

        var u = SurvivorUnit(1, new(0, 1));
        var boss = EnemyUnit("cole", new(4, 1));
        var sim = Arena(10, 3, [u, boss]);
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        var deck = Assert.Single(sim.State.Decks, d => d.PlayerId == EnemyCards.DeckId);
        Assert.Equal(cole.Cards.Count, deck.Hand.Count + deck.DrawPile.Count + deck.Used.Count + deck.Consumed.Count);
        var played = sim.State.Events.Where(e => e.Type == ScavengeEventType.CardPlayed && e.UnitId == boss.Id).ToList();
        Assert.NotEmpty(played);
        Assert.All(played, e => Assert.Equal(Unit.EnemySide, e.Side));
        Assert.DoesNotContain(FogLog.For(sim, u.Side, sim.State.Events), e => e.Type == ScavengeEventType.CardPlayed);
    }

    [Fact]
    public void An_authored_enemy_arriving_later_shuffles_its_cards_into_the_enemy_deck_once()
    {
        var warden = Real.Enemies["linked_warden"];
        var cole = Real.Enemies["cole"];
        var u = SurvivorUnit(1, new(0, 1));
        var first = EnemyUnit("linked_warden", new(6, 1));
        var sim = Arena(12, 3, [u, first]);
        EnemyCards.Play(sim, first);
        var deck = SquadDecks.Of(sim.State, EnemyCards.DeckId)!;
        int Total() => deck.Hand.Count + deck.DrawPile.Count + deck.Used.Count + deck.Consumed.Count;
        Assert.Equal(warden.Cards.Count, Total());

        var late = EnemyUnit("cole", new(9, 1)); // a story trigger brings him in
        late.Id = sim.State.Units.Max(x => x.Id) + 1;
        sim.State.Units.Add(late);
        var stream = sim.State.CardRngState;
        EnemyCards.Play(sim, late);
        Assert.Equal(warden.Cards.Count + cole.Cards.Count, Total());
        Assert.NotEqual(stream, sim.State.CardRngState); // shuffled in on the card stream
        EnemyCards.Play(sim, late);
        EnemyCards.Play(sim, first);
        Assert.Equal(warden.Cards.Count + cole.Cards.Count, Total()); // each brings its cards once
    }

    [Fact]
    public void Enemies_without_cards_get_no_deck()
    {
        var sim = Arena(10, 3, [SurvivorUnit(1, new(0, 1)), EnemyUnit("husk", new(6, 1))]);
        Assert.True(sim.Execute(new EndTurn(1, sim.State.Units[0].Id)).Ok);
        Assert.Empty(sim.State.Decks);
    }

    [Fact]
    public void An_enemy_naming_an_unknown_card_is_a_data_error()
    {
        var bad = Real.Enemies.Values.Select(e => e.Id == "cole" ? e with { Cards = ["no_such_card"] } : e);
        var ex = Assert.Throws<GameDataException>(() => new ScavengeData(Real.Items.Values.Where(i => i.BaseId is null), bad, Real.Zones.Values, Real.Rules,
            Real.PerkHooks, Real.Cybernetics?.Def, Real.Schematics, Real.Electronics, medicine: Real.Medicine?.Def, cards: Real.Cards.Def));
        Assert.Contains("no_such_card", ex.Message);
    }

    [Fact]
    public void Declaring_a_blow_in_reach_draws_nothing_from_the_run_rng()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var near = EnemyUnit("husk", new(1, 0));
        var sim = Arena(8, 3, [u, near]);
        var before = sim.Rng.State;
        ScavengeAi.TakeTurn(sim, near);
        Assert.Equal(u.Pos, Strikes(sim).First(s => s.UnitId == near.Id).Tile);
        Assert.Equal(before, sim.Rng.State);
    }
}
