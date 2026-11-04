using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Playing cards from the squad hand: points, AP, requirements, effects, reactions, dead cards.</summary>
public class PlayCardTests
{
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>P1's a (and a second unit a2) against P2's b on an open floor; P1 holds exactly <paramref name="hand"/>.</summary>
    private static (ScavengeSim Sim, Unit A, Unit B, SquadDeck Deck) Table(params string[] hand) => TableOn(Sure, hand);

    private static (ScavengeSim Sim, Unit A, Unit B, SquadDeck Deck) TableOn(ScavengeData data, params string[] hand)
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 1), weapon: "pipe");
        var sim = Arena(10, 3, [a, b], data: data);
        var deck = new SquadDeck { PlayerId = 1, Hand = hand.ToList(), Points = 3, Budget = 3, HandSize = 5, DrawPerRound = 3,
            DrawPile = ["dash", "take_cover", "steady_aim", "sidestep"] };
        sim.State.Decks.Add(deck);
        return (sim, a, b, deck);
    }

    private static ScavengeResult Play(ScavengeSim sim, Unit u, string card, int? target = null, bool boost = false) =>
        sim.Execute(new PlayCard(u.PlayerId!.Value, u.Id, card, target, Boost: boost));

    [Fact]
    public void A_movement_card_adds_range_spends_its_points_and_goes_to_the_used_pile()
    {
        var (sim, a, _, deck) = Table("dash", "take_cover");
        var range = sim.Profile(a).Movement.Primary.Range;
        var ap = a.Ap;
        var r = Play(sim, a, "dash");
        Assert.True(r.Ok, r.Message);
        Assert.Equal(range + 1, sim.Profile(a).Movement.Primary.Range);
        Assert.Equal(2, deck.Points);
        Assert.Equal(ap, a.Ap); // free of AP
        Assert.Equal(["take_cover"], deck.Hand);
        Assert.Equal(["dash"], deck.Used);
        Assert.Equal(a.Id, sim.State.CurrentUnitId); // playing a card does not end the turn
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.CardPlayed && e.Side == Unit.PlayerSide(1));
    }

    [Fact]
    public void A_baseline_card_does_more_for_a_qualified_character_and_scales()
    {
        var (sim, a, _, _) = Table("dash");
        a.Attributes[AttributeKind.Agility] = 60;
        var range = sim.Profile(a).Movement.Primary.Range;
        Assert.True(Play(sim, a, "dash").Ok);
        Assert.Equal(range + 2, sim.Profile(a).Movement.Primary.Range);

        var (sim2, a2, _, _) = Table("field_dressing");
        a2.Skills[Skill.Medicine] = 40; // qualified (bonus .05) and scaled ×1.4
        a2.Body.Injure(BodyPart.LeftLeg, 0.6);
        Assert.True(Play(sim2, a2, "field_dressing").Ok);
        Assert.Equal(0.6 - (0.1 + 0.05) * 1.4, a2.Body[BodyPart.LeftLeg].Severity, 9);
    }

    [Fact]
    public void Hard_cards_need_a_qualified_character()
    {
        var (sim, a, _, deck) = Table("hold_the_line");
        Assert.Equal(ScavengeError.NotQualified, Play(sim, a, "hold_the_line").Error);
        Assert.Equal(3, deck.Points);
        a.Attributes[AttributeKind.Endurance] = 60;
        Assert.True(Play(sim, a, "hold_the_line").Ok);
        Assert.Equal(2, a.Guard);
    }

    [Fact]
    public void Points_ap_costs_and_boosts_are_checked_and_paid()
    {
        var (sim, a, _, deck) = Table("called_shot", "heavy_swing", "sprint");
        a.Skills[Skill.Firearms] = 40;
        var ap = a.Ap;
        Assert.True(Play(sim, a, "called_shot").Ok);
        Assert.Equal(ap - 1, a.Ap);
        Assert.Equal(1, deck.Points);
        Assert.True(Play(sim, a, "heavy_swing", boost: true).Ok);
        Assert.Equal(ap - 2, a.Ap);
        Assert.Equal(0, deck.Points);
        a.Attributes[AttributeKind.Agility] = 60;
        Assert.Equal(ScavengeError.NotEnoughPoints, Play(sim, a, "sprint").Error);
        Assert.Equal(ScavengeError.NoCard, Play(sim, a, "dash").Error);
    }

    [Fact]
    public void Not_enough_ap_for_a_boost_is_refused()
    {
        var (sim, a, _, _) = Table("heavy_swing");
        a.Ap = 0;
        Assert.Equal(ScavengeError.NotEnoughAp, Play(sim, a, "heavy_swing", boost: true).Error);
        Assert.True(Play(sim, a, "heavy_swing").Ok); // the plain card costs no AP
    }

    [Fact]
    public void A_hit_card_helps_the_next_attack_only()
    {
        var (sim, a, b, _) = TableOn(Real, "steady_aim");
        a.Pos = new(4, 1);
        var swing = sim.Profile(a).Action("swing")!;
        var before = Combat.ChanceToHit(sim, a, swing with { Accuracy = 0.3 }, b);
        Assert.True(Play(sim, a, "steady_aim").Ok);
        Assert.Equal(before + 0.1, Combat.ChanceToHit(sim, a, swing with { Accuracy = 0.3 }, b), 9);
        Assert.True(StrikeNow(sim, a, b, "swing").Ok);
        Assert.Equal(0, a.CardHit);
    }

    [Fact]
    public void Guard_takes_a_tier_off_the_next_hit_taken()
    {
        var (sim, a, b, _) = Table("roll_with_it");
        Assert.True(Play(sim, a, "roll_with_it").Ok);
        Assert.Equal(1, a.Guard);
        b.Pos = new(1, 1);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(StrikeNow(sim, b, a, "swing").Ok);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Attacked && e.TargetId == a.Id && e.Tier == HitTier.Graze);
        Assert.All(a.Body.Parts.Values, p => Assert.Equal(PartCondition.Healthy, p.Condition));
        Assert.Equal(0, a.Guard);
    }

    [Fact]
    public void Cover_lasts_until_the_next_own_turn()
    {
        var (sim, a, b, _) = Table("take_cover");
        var cover = sim.Profile(a).CoverBonus;
        Assert.True(Play(sim, a, "take_cover").Ok);
        Assert.Equal(cover + 0.15, sim.Profile(a).CoverBonus, 9);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(cover + 0.15, sim.Profile(a).CoverBonus, 9);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(cover, sim.Profile(a).CoverBonus, 9);
    }

    [Fact]
    public void A_consumable_card_is_gone_once_played_and_a_mend_needs_an_injury()
    {
        var (sim, a, _, deck) = Table("field_dressing");
        Assert.Equal(ScavengeError.NotUsable, Play(sim, a, "field_dressing").Error);
        a.Body.Injure(BodyPart.LeftArm, 0.5);
        Assert.True(Play(sim, a, "field_dressing").Ok);
        Assert.Empty(deck.Used);
        Assert.Equal(["field_dressing"], deck.Consumed);
        Assert.True(a.Body[BodyPart.LeftArm].Severity < 0.5);
    }

    [Fact]
    public void A_draw_card_draws()
    {
        var (sim, a, _, deck) = Table("scout_ahead");
        Assert.True(Play(sim, a, "scout_ahead").Ok);
        Assert.Equal(2, deck.Hand.Count);
        Assert.Equal(2, deck.DrawPile.Count);
    }

    [Fact]
    public void Arm_locked_cards_wait_for_working_arms()
    {
        var (sim, a, _, _) = Table("heavy_swing");
        a.Body.Injure(BodyPart.LeftArm, 0.8);
        a.Body.Injure(BodyPart.RightArm, 0.8);
        Assert.True(ProfileDeriver.ArmsLocked(Sure, a.Body, twoHanded: false));
        Assert.Equal(ScavengeError.ArmsLocked, Play(sim, a, "heavy_swing").Error);
    }

    [Fact]
    public void Only_reactions_may_be_played_on_a_unit_whose_turn_it_is_not()
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var a2 = SurvivorUnit(1, new(0, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 1), weapon: "pipe");
        var sim = Arena(10, 3, [a, a2, b], data: Sure);
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Hand = ["dash", "sidestep"], Points = 3, Budget = 3, HandSize = 5 });
        Ready(sim, b, a2.Pos, "swing");

        Assert.Equal(ScavengeError.NotYourTurn, Play(sim, a2, "dash").Error);
        Assert.True(Play(sim, a2, "sidestep", target: b.Id).Ok);
        Assert.Equal(1, a2.Guard);
    }

    [Fact]
    public void A_reaction_needs_a_seen_enemy_with_a_readied_strike()
    {
        var (sim, a, b, _) = Table("sidestep");
        Assert.Equal(ScavengeError.NoIntent, Play(sim, a, "sidestep", target: b.Id).Error);
        Assert.Equal(ScavengeError.NoIntent, Play(sim, a, "sidestep").Error);
        Ready(sim, b, new(3, 1), "swing");
        Assert.True(Play(sim, a, "sidestep", target: b.Id).Ok);
    }

    [Fact]
    public void A_counterstrike_hits_the_striker_first_when_its_strike_comes_down()
    {
        var (sim, a, b, _) = Table("counterstrike");
        a.Skills[Skill.Melee] = 30;
        b.Pos = new(1, 1);
        Ready(sim, b, a.Pos, "swing");
        Assert.True(Play(sim, a, "counterstrike", target: b.Id).Ok);
        Assert.Equal(b.Id, a.CounterId);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);

        var counter = sim.State.Events.FindIndex(e => e.Type == ScavengeEventType.Countered && e.UnitId == a.Id && e.TargetId == b.Id);
        var blow = sim.State.Events.FindIndex(e => e.Type == ScavengeEventType.Attacked && e.UnitId == b.Id);
        Assert.True(counter >= 0);
        Assert.True(blow < 0 || counter < blow);
        Assert.True(b.Hp < b.MaxHp);
        Assert.Null(a.CounterId);
    }

    [Fact]
    public void A_move_card_carries_into_a_move_made_after_it()
    {
        var (sim, a, _, _) = Table("dash");
        Assert.Equal(ScavengeError.Unreachable, sim.Execute(new Move(1, a.Id, new(3, 1))).Error);
        Assert.True(Play(sim, a, "dash").Ok);
        Assert.True(sim.Execute(new Move(1, a.Id, new(3, 1))).Ok);
        Assert.Equal(new Pos(3, 1), a.Pos);
    }

    [Fact]
    public void A_dead_hard_card_can_be_discarded_for_free_once_a_round()
    {
        var (sim, a, _, deck) = Table("hold_the_line", "sprint", "dash");
        Assert.Equal(ScavengeError.CardPlayable, sim.Execute(new DiscardDeadCard(1, a.Id, "dash")).Error);
        Assert.True(sim.Execute(new DiscardDeadCard(1, a.Id, "hold_the_line")).Ok);
        Assert.Equal(["hold_the_line"], deck.Used);
        Assert.Equal(3, deck.Points);
        Assert.Equal(ScavengeError.DeadCardSpent, sim.Execute(new DiscardDeadCard(1, a.Id, "sprint")).Error);
    }

    [Theory]
    [InlineData("a card not in hand", ScavengeError.NoCard)]
    [InlineData("an unknown card", ScavengeError.NoCard)]
    [InlineData("another player's id", ScavengeError.NotYourUnit)]
    [InlineData("another player's unit", ScavengeError.NotYourUnit)]
    [InlineData("a reaction at an unseen enemy", ScavengeError.NoIntent)]
    [InlineData("a reaction at a friend", ScavengeError.NoIntent)]
    public void Forged_card_plays_are_refused_and_change_nothing(string forged, ScavengeError expected)
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var a2 = SurvivorUnit(1, new(0, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 1), weapon: "pipe");
        var sim = Arena(14, 3, [a, a2, b], data: Sure);
        var deck = new SquadDeck { PlayerId = 1, Hand = ["sidestep", "dash"], Points = 3, Budget = 3, HandSize = 5 };
        sim.State.Decks.Add(deck);
        Ready(sim, b, a.Pos, "swing");
        Ready(sim, a2, new(1, 2), "swing");
        if (forged == "a reaction at an unseen enemy")
        {
            b.Pos = new(13, 1);
            Assert.False(Detection.SideSees(Sure, sim.State, a.Side, b));
        }
        var command = forged switch
        {
            "a card not in hand" => new PlayCard(1, a.Id, "sprint"),
            "an unknown card" => new PlayCard(1, a.Id, "no_such_card"),
            "another player's id" => new PlayCard(2, a.Id, "dash"),
            "another player's unit" => new PlayCard(1, b.Id, "dash"),
            "a reaction at an unseen enemy" => new PlayCard(1, a.Id, "sidestep", b.Id),
            _ => new PlayCard(1, a.Id, "sidestep", a2.Id),
        };
        Assert.Equal(expected, sim.Execute(command).Error);
        Assert.Equal(["sidestep", "dash"], deck.Hand);
        Assert.Equal(3, deck.Points);
        Assert.Equal(0, a.Guard + a.CardMove);
    }

    [Fact]
    public void A_draw_card_never_reshuffles_itself_back_into_the_hand()
    {
        var (sim, a, _, deck) = Table("scout_ahead");
        deck.DrawPile.Clear();
        deck.Used.Add("take_cover");
        Assert.True(Play(sim, a, "scout_ahead").Ok);
        Assert.Equal(["take_cover"], deck.Hand);
        Assert.Equal(["scout_ahead"], deck.Used);
    }

    [Fact]
    public void A_hit_card_waits_for_the_units_own_attack_not_an_overwatch_shot()
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var b = SurvivorUnit(2, new(7, 1), weapon: "pistol");
        b.Attributes[Survivors.AttributeKind.Agility] = 35;
        var sim = Arena(10, 3, [a, b], data: Sure);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new CommitOverwatch(2, b.Id, Arc: new Pos(-1, 0))).Ok);
        b.CardHit = 0.1;
        Assert.True(sim.Execute(new Move(1, a.Id, new(2, 1))).Ok);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Overwatch && e.UnitId == b.Id);
        Assert.Equal(0.1, b.CardHit, 9);
    }
}
