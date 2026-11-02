using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Enemy AI telegraphs (plans), authored enemies play cards, and AI turns always end.</summary>
public class AiPlanTests
{
    [Fact]
    public void A_turret_telegraphs_only_its_aim()
    {
        ZoneDevice turret = null!;
        var u = SurvivorUnit(1, new Pos(1, 1));
        var sim = Arena(10, 8, [u], setup: (d, s) => turret = Hacking.Place(d, s, DeviceKind.Turret, new Pos(4, 1)));
        var gun = sim.State.FindUnit(turret.UnitId!.Value)!;
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        var plan = Assert.IsType<PlannedAction>(gun.Plan);
        Assert.Empty(plan.Path);
        Assert.Equal(u.Pos, plan.AttackTile);
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
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, b.Id)).Ok);
        Assert.Equal(b.Pos, l1.Plan?.AttackTile);
        Assert.Equal(b.Pos, l2.Plan?.AttackTile);
        Assert.Equal(b.Id, l1.Ai.TargetId);
    }

    [Fact]
    public void Planning_never_moves_the_unit_or_spends_its_ap_before_the_plan_resolves()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var husk = EnemyUnit("husk", new(5, 0));
        var sim = Arena(8, 3, [u, husk]);
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.Equal(new Pos(5, 0), husk.Pos);
        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == husk.Id && e.Type is ScavengeEventType.Moved or ScavengeEventType.Attacked or ScavengeEventType.Missed);
        Assert.Contains(sim.State.Events, e => e.UnitId == husk.Id && e.Type == ScavengeEventType.IntentDeclared);
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
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentDeclared);
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
    public void A_plan_is_checked_against_a_full_turn_of_ap_not_what_is_left()
    {
        var u = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var sim = Arena(10, 3, [u, SurvivorUnit(2, new(5, 1))]);
        Assert.True(sim.Execute(new Move(1, u.Id, new(1, 1))).Ok);
        u.Ap = 0; // spent this turn
        Assert.Equal(sim.Profile(u).MaxAp, PlannedActions.Budget(sim, u));
        Assert.True(sim.Execute(new DeclareIntent(1, u.Id, [new(3, 1), new(4, 1)], new Pos(5, 1), "swing")).Ok);
    }
}
