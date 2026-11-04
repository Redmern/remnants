using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>A unit plans at most one move, one action and one card a round, unless a card or perk raises a limit. Runs on the real rules.</summary>
public class PlanSlotTests
{
    private static (ScavengeSim Sim, Unit A, Unit B, SquadDeck Deck) Table(params string[] hand)
    {
        var a = SurvivorUnit(1, new(1, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(2, 2), weapon: "pipe");
        var sim = Arena(12, 6, [a, b], data: Real);
        var deck = new SquadDeck { PlayerId = 1, Hand = hand.ToList(), Points = 9, Budget = 9, HandSize = 5, DrawPerRound = 3 };
        sim.State.Decks.Add(deck);
        return (sim, a, b, deck);
    }

    private static ScavengeResult Play(ScavengeSim sim, Unit u, string card) => sim.Execute(new PlayCard(u.PlayerId!.Value, u.Id, card));

    [Fact]
    public void A_unit_has_one_ap_by_default()
    {
        var (_, a, _, _) = Table();
        Assert.Equal(1, Real.Rules.Turn.BaseAp);
        Assert.Equal(1, a.Ap);
    }

    [Fact]
    public void A_second_move_is_refused_and_pays_nothing()
    {
        var (sim, a, _, _) = Table();
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 4))).Ok);
        var (mp, stamina) = (a.Mp, Economy.Stamina(sim, a));
        Assert.Equal(ScavengeError.SlotFull, sim.Execute(new Move(1, a.Id, new Pos(1, 5))).Error);
        Assert.Equal((mp, stamina), (a.Mp, Economy.Stamina(sim, a)));
    }

    [Fact]
    public void A_second_action_is_refused_even_with_ap_left()
    {
        var data = With(r => r with { Turn = r.Turn with { BaseAp = 3 } });
        var a = SurvivorUnit(1, new(1, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(2, 2), weapon: "pipe");
        var sim = Arena(12, 6, [a, b], data: data);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.Equal(2, a.Ap);
        Assert.Equal(ScavengeError.SlotFull, sim.Execute(new Recover(1, a.Id)).Error);
        Assert.Equal(ScavengeError.SlotFull, sim.Execute(new Push(1, a.Id, new Pos(1, 0))).Error);
        Assert.Equal(ScavengeError.SlotFull, sim.Execute(new Dash(1, a.Id, new Pos(0, 1), 1)).Error);
        Assert.Equal(ScavengeError.SlotFull, sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Error);
    }

    [Fact]
    public void A_move_an_action_and_a_card_all_fit_in_a_round()
    {
        var (sim, a, b, _) = Table("parry");
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 3))).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "parry", Tile: new Pos(1, 3), Direction: new Pos(1, 0))).Ok);
        Assert.Equal(3, Plans.Of(sim.State, a)!.Steps.Count);
    }

    [Fact]
    public void A_second_card_is_refused_whether_or_not_the_first_acts_at_once()
    {
        var (sim, a, _, _) = Table("take_cover", "steady_aim");
        Assert.True(Play(sim, a, "take_cover").Ok);
        Assert.Equal(ScavengeError.SlotFull, Play(sim, a, "steady_aim").Error);
    }

    [Fact]
    public void The_slots_open_again_next_round()
    {
        var (sim, a, b, _) = Table("take_cover", "steady_aim");
        Assert.True(Play(sim, a, "take_cover").Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(new PlanSlots(1, 1, 1), Plans.Limits(sim, a));
        Assert.Equal(new PlanSlots(0, 0, 0), Plans.Used(sim.State, a));
        Assert.True(Play(sim, a, "steady_aim").Ok);
    }

    [Fact]
    public void A_card_costs_points_and_its_stamina_but_leaves_the_ap_for_the_action()
    {
        var (sim, a, b, deck) = Table("parry");
        var stamina = Economy.Stamina(sim, a);
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "parry", Tile: a.Pos, Direction: new Pos(1, 0))).Ok);
        Assert.Equal(1, a.Ap);
        Assert.True(deck.Points < 9);
        Assert.Equal(stamina - 2, Economy.Stamina(sim, a));
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.Equal(stamina - 4, Economy.Stamina(sim, a));
    }

    [Fact]
    public void Stamina_stays_one_per_mp_and_two_per_action()
    {
        var (sim, a, b, _) = Table();
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 4))).Ok);
        Assert.Equal(8, Economy.Stamina(sim, a));
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos + new Pos(0, 2), "swing")).Ok);
        Assert.Equal(6, Economy.Stamina(sim, a));
    }

    [Fact]
    public void Clearing_a_slot_refunds_it_and_leaves_the_others()
    {
        var (sim, a, b, deck) = Table("parry");
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 3))).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "parry", Tile: new Pos(1, 3), Direction: new Pos(1, 0))).Ok);

        Assert.True(sim.Execute(new UndoPlan(1, a.Id, Slot: PlanSlot.Move)).Ok);
        Assert.Equal(new PlanSlots(0, 1, 1), Plans.Used(sim.State, a));
        Assert.Equal(4, a.Mp);

        Assert.True(sim.Execute(new UndoPlan(1, a.Id, Slot: PlanSlot.Card)).Ok);
        Assert.Equal(new PlanSlots(0, 1, 0), Plans.Used(sim.State, a));
        Assert.Contains("parry", deck.Hand);
        Assert.Equal(9, deck.Points);

        Assert.True(sim.Execute(new UndoPlan(1, a.Id, Slot: PlanSlot.Action)).Ok);
        Assert.Equal(new PlanSlots(0, 0, 0), Plans.Used(sim.State, a));
        Assert.Equal(1, a.Ap);
        Assert.Equal(10, Economy.Stamina(sim, a));
        Assert.Equal(ScavengeError.NoPlan, sim.Execute(new UndoPlan(1, a.Id, Slot: PlanSlot.Action)).Error);
    }

    [Fact]
    public void Changing_the_move_brings_a_queued_aim_along()
    {
        var (sim, a, b, _) = Table();
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 3))).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new Pos(2, 3), "swing")).Ok);
        Assert.True(sim.Execute(new UndoPlan(1, a.Id, Slot: PlanSlot.Move)).Ok);
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 1))).Ok);
        var strike = Plans.Of(sim.State, a)!.Steps.Single(s => s.Strike is not null).Strike!;
        Assert.Equal(new Pos(1, 1), strike.From);
        Assert.Equal(new Pos(2, 1), strike.Tile);
        Assert.Contains(new Pos(2, 1), strike.Tiles);
    }

    [Fact]
    public void Changing_the_move_replans_a_queued_dash_from_the_new_tile()
    {
        var (sim, a, _, _) = Table();
        Assert.True(sim.Execute(new Dash(1, a.Id, new Pos(0, 1), 2)).Ok);
        Assert.Equal(new Pos(1, 4), Plans.Anchor(sim.State, a));
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(3, 1))).Ok);
        Assert.Equal(new Pos(3, 3), Plans.Anchor(sim.State, a));
        Assert.Equal(new Pos(3, 1), Plans.Of(sim.State, a)!.Steps.Single(s => s.Kind == PlanStepKind.Dash).From);
        Assert.Equal(PlanStepKind.Move, Plans.Of(sim.State, a)!.Steps[0].Kind);
    }

    [Fact]
    public void The_move_can_be_changed_with_the_mp_it_would_get_back()
    {
        var (sim, a, _, _) = Table();
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 5))).Ok);
        var choices = Plans.MoveChoices(sim, a);
        Assert.True(choices.ContainsKey(new Pos(1, 1)));
        Assert.False(choices.ContainsKey(new Pos(1, 5 + 1)));
        Assert.Equal(new Pos(1, 5), Plans.Anchor(sim.State, a));
    }

    [Fact]
    public void A_card_can_raise_the_move_limit()
    {
        var (sim, a, _, _) = Table("forced_march");
        Assert.True(Play(sim, a, "forced_march").Ok);
        Assert.Equal(2, Plans.Limits(sim, a).Moves);
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 3))).Ok);
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(3, 3))).Ok);
        Assert.Equal(ScavengeError.SlotFull, sim.Execute(new Move(1, a.Id, new Pos(4, 3))).Error);
    }

    [Fact]
    public void A_card_can_give_an_extra_action_with_the_ap_for_it()
    {
        var (sim, a, b, _) = Table("press_on");
        Assert.True(Play(sim, a, "press_on").Ok);
        Assert.Equal(2, a.Ap);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new Push(1, a.Id, new Pos(1, 0))).Ok);
        Assert.Equal(ScavengeError.SlotFull, sim.Execute(new Recover(1, a.Id)).Error);
    }

    [Fact]
    public void A_free_action_costs_no_ap_and_no_stamina_and_comes_back_when_cleared()
    {
        var (sim, a, b, _) = Table("quick_reflex");
        Assert.True(Play(sim, a, "quick_reflex").Ok);
        Assert.Equal(new PlanSlots(1, 2, 1), Plans.Limits(sim, a));
        var stamina = Economy.Stamina(sim, a);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.Equal((1, stamina), (a.Ap, Economy.Stamina(sim, a)));
        Assert.True(sim.Execute(new Push(1, a.Id, new Pos(1, 0))).Ok);
        Assert.Equal((0, stamina - 2), (a.Ap, Economy.Stamina(sim, a)));
        Assert.True(sim.Execute(new UndoPlan(1, a.Id, Slot: PlanSlot.Action)).Ok);
        Assert.Equal((1, stamina), (a.Ap, Economy.Stamina(sim, a)));
        Assert.Equal(1, a.FreeActions);
    }

    [Fact]
    public void A_card_can_open_another_card_slot()
    {
        var (sim, a, _, _) = Table("quick_hands", "take_cover", "steady_aim");
        Assert.True(Play(sim, a, "quick_hands").Ok);
        Assert.True(Play(sim, a, "take_cover").Ok);
        Assert.Equal(ScavengeError.SlotFull, Play(sim, a, "steady_aim").Error);
    }

    [Theory]
    [InlineData("extra_move", 2, 1, 1)]
    [InlineData("extra_action", 1, 2, 1)]
    [InlineData("extra_card", 1, 1, 2)]
    public void A_perk_hook_raises_a_limit(string hook, int moves, int actions, int cards)
    {
        var (sim, a, _, _) = Table();
        a.Hooks[hook] = 1;
        Assert.Equal(new PlanSlots(moves, actions, cards), Plans.Limits(sim, a));
    }

    [Fact]
    public void An_extra_action_perk_comes_with_the_ap_for_it()
    {
        var (sim, a, _, _) = Table();
        a.Hooks["extra_action"] = 1;
        Assert.Equal(2, sim.Profile(a).MaxAp);
    }

    [Fact]
    public void A_free_action_perk_gives_one_each_round()
    {
        var (sim, a, b, _) = Table();
        a.Hooks["free_action"] = 1;
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(1, a.FreeActions);
    }

    [Theory]
    [InlineData("husk")]
    [InlineData("linked")]
    [InlineData("linked_warden")]
    public void The_ai_plans_no_more_than_its_slots(string enemy)
    {
        var a = SurvivorUnit(1, new(1, 2), weapon: "pipe");
        var e1 = EnemyUnit(enemy, new(2, 2));
        var e2 = EnemyUnit(enemy, new(9, 4));
        var sim = Arena(12, 6, [a, e1, e2], data: Real);
        sim.Start();
        for (var round = 0; round < 6 && !sim.State.Finished && a.IsActive; round++)
        {
            foreach (var e in new[] { e1, e2 }.Where(e => e.IsActive))
            {
                var (used, limit) = (Plans.Used(sim.State, e), Plans.Limits(sim, e));
                Assert.InRange(used.Moves, 0, limit.Moves);
                Assert.InRange(used.Actions, 0, limit.Actions);
                Assert.InRange(used.Cards, 0, limit.Cards);
            }
            Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        }
    }

    [Fact]
    public void An_ai_next_to_its_target_plans_one_blow()
    {
        var a = SurvivorUnit(1, new(1, 2), weapon: "pipe");
        var husk = EnemyUnit("husk", new(2, 2));
        husk.Ai.Mode = AiMode.Hunting;
        var sim = Arena(12, 6, [a, husk], data: Real);
        sim.Start();
        Assert.Single(Plans.Of(sim.State, husk)!.Steps, s => s.Strike is not null);
    }

    [Fact]
    public void A_bot_does_not_try_a_second_move()
    {
        var (sim, a, b, _) = Table();
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 4))).Ok);
        Assert.Null(BotTactics.StepToward(sim, a, 1, [new Pos(11, 5)]));
        Assert.Null(BotTactics.Approach(sim, a, 1, [b]));
    }

    [Fact]
    public void A_bot_plays_no_second_card()
    {
        var (sim, a, _, _) = Table("take_cover", "field_dressing");
        a.Body.Injure(Survivors.BodyPart.Torso, 0.4);
        Assert.True(Play(sim, a, "take_cover").Ok);
        Assert.Null(BotTactics.Card(sim, a, 1));
    }

    [Fact]
    public void Slots_and_free_actions_survive_a_save_and_a_run_from_before_loads()
    {
        var (sim, a, b, _) = Table("quick_reflex");
        Assert.True(Play(sim, a, "quick_reflex").Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        var json = ScavengeSave.Save(sim.State);
        Assert.Contains($"\"version\": {ScavengeSave.Version}", json);
        var loaded = ScavengeSave.Load(Real, json);
        var back = loaded.State.FindUnit(a.Id)!;
        Assert.Equal((1, 2), (back.CardsPlayed, Plans.Limits(loaded, back).Actions));
        Assert.True(Plans.Of(loaded.State, back)!.Steps.Single().Free);

        var older = json.Replace($"\"version\": {ScavengeSave.Version}", $"\"version\": {ScavengeSave.SimultaneousVersion}");
        Assert.NotEqual(json, older);
        Assert.NotNull(ScavengeSave.Load(Real, older));
    }

    [Fact]
    public void Nothing_in_the_real_data_costs_more_ap_than_a_unit_has()
    {
        var limit = Real.Rules.Turn.BaseAp;
        var costs = Real.Items.Values.SelectMany(i => i.Actions.Where(x => x.CloseApPenalty == 0).Select(x => (i.Id, x.ApCost)))
            .Concat(Real.Items.Values.Where(i => i.Use is not null).Select(i => (i.Id, i.Use!.ApCost)))
            .Concat([("hack", Real.Rules.Hacking.ApCost), ("wreck", Real.Rules.Salvage.ApCost), ("push", Real.Rules.Combat.Push.ApCost),
                     ("dash", Real.Rules.Combat.Dash.ApCost), ("recover", Real.Rules.Turn.RecoverApCost)]);
        Assert.DoesNotContain(costs, c => c.Item2 > limit);
    }

    [Fact]
    public void A_heavy_weapon_still_resolves_late()
    {
        var a = SurvivorUnit(1, new(1, 2), weapon: "rifle");
        var sim = Arena(12, 6, [a, SurvivorUnit(2, new(8, 2))], data: Real);
        var aimed = sim.Profile(a).Action("aimed_shot")!;
        Assert.Equal(1, aimed.ApCost);
        var step = new PlanStep { Kind = PlanStepKind.Strike, Strike = new TileStrike { ActionId = aimed.Id } };
        Assert.Equal(Real.Rules.Priority.HeavyWeight, Priority.Action(sim, a, step) - Priority.Movement(sim, a), 6);
    }
}
