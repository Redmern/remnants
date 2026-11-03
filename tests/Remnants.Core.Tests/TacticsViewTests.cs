using Remnants.Core.Colony;
using Remnants.Core.Help;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>The plain view models the scavenge and village UI draw: telegraphs, threats, hit previews, the hand, the picker.</summary>
public class TacticsViewTests
{
    private static (ScavengeSim Sim, Unit A, Unit B, Unit Husk) Field()
    {
        var a = SurvivorUnit(1, new(1, 1), weapon: "pipe");
        var b = SurvivorUnit(1, new(1, 3), weapon: "pistol");
        var husk = EnemyUnit("husk", new(6, 1));
        var sim = Arena(12, 6, [a, b, husk]);
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Hand = ["dash", "sprint", "sidestep", "hold_the_line"], DrawPile = ["take_cover", "dash"],
            Used = ["steady_aim"], Points = 2, Budget = 3, HandSize = 5 });
        return (sim, a, b, husk);
    }

    [Fact]
    public void Telegraphs_show_seen_enemy_plans_without_their_card()
    {
        var (sim, a, _, husk) = Field();
        husk.Plan = new PlannedAction { Path = [new(4, 1), new(2, 1)], AttackTile = a.Pos, ActionId = "claw", CardId = "secret", TargetId = a.Id, Round = 1 };
        var t = Assert.Single(TacticsView.Telegraphs(sim, a.Side));
        Assert.Equal(husk.Id, t.UnitId);
        Assert.Equal([new Pos(4, 1), new Pos(2, 1)], t.Path);
        Assert.Equal(a.Pos, t.AttackTile);
        Assert.Contains(a.Pos, t.Threatened);
        Assert.True(t.AimedAtUs);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(t));

        husk.Pos = new(11, 5); // out of sight: nothing
        Assert.Empty(TacticsView.Telegraphs(sim, a.Side));
    }

    [Fact]
    public void The_threat_overlay_marks_planned_blows_above_plain_reach()
    {
        var (sim, a, _, husk) = Field();
        husk.Plan = new PlannedAction { Path = [new(4, 1)], AttackTile = new(3, 1), ActionId = "claw", Round = 1 };
        var overlay = TacticsView.Threats(sim, a.Side);
        Assert.Equal(ThreatKind.Planned, overlay[new Pos(3, 1)]);
        Assert.Equal(ThreatKind.Reach, overlay[new Pos(5, 1)]); // next to the husk now
        Assert.False(overlay.ContainsKey(a.Pos));
    }

    [Fact]
    public void The_hit_preview_from_a_tile_matches_the_rules_and_names_its_terms()
    {
        var (sim, a, _, husk) = Field();
        var swing = sim.Profile(a).Action("swing")!;
        var preview = TacticsView.HitPreview(sim, a, swing, husk, new Pos(5, 1));
        Assert.Equal(Combat.ChanceToHit(sim, a, swing, husk, new Pos(5, 1)), preview.Chance, 9);
        Assert.False(TacticsView.HitPreview(sim, a, swing, husk).InReach); // from (1,1) a pipe does not reach

        a.OutplayedId = husk.Id;
        a.OutplayedUntilRound = sim.State.Round;
        a.CardHit = 0.1;
        var terms = TacticsView.HitPreview(sim, a, swing, husk, new Pos(5, 1)).Breakdown!.Rows.Select(r => r.Label).ToList();
        Assert.Contains("Dodged their attack", terms);
        Assert.Contains("Card", terms);

        husk.Pos = new(4, 1); // within one move and a swing
        var best = TacticsView.MovePreview(sim, a, husk);
        Assert.True(best[new Pos(3, 1)] > 0);
        Assert.False(best.ContainsKey(new Pos(1, 1))); // no blow from here
    }

    [Fact]
    public void The_hand_shows_cards_points_counts_and_who_can_play_each()
    {
        var (sim, a, b, _) = Field();
        b.SlotCards = ["take_cover"];
        var hand = TacticsView.Hand(sim, 1)!;
        Assert.Equal((2, 3, 4, 2, 1), (hand.Points, hand.Budget, hand.Cards.Count(c => c.SlotOf is null), hand.DrawCount, hand.UsedCount));
        var dash = hand.Cards.First(c => c.Id == "dash");
        Assert.Contains(a.Id, dash.PlayableBy);           // a is up
        Assert.DoesNotContain(b.Id, dash.PlayableBy);     // not b's turn
        var hold = hand.Cards.First(c => c.Id == "hold_the_line");
        Assert.Empty(hold.PlayableBy);
        Assert.True(hold.Dead);                           // nobody qualifies: discardable
        Assert.Contains(hand.Cards, c => c.Id == "take_cover" && c.SlotOf == b.Id);
    }

    [Fact]
    public void The_picker_offers_qualified_units_and_says_why_not_for_the_rest()
    {
        var (sim, a, b, _) = Field();
        a.Attributes[AttributeKind.Agility] = 60;
        var rows = TacticsView.Picker(sim, 1, "sprint");
        Assert.True(rows.Single(r => r.UnitId == a.Id).Ok);
        var no = rows.Single(r => r.UnitId == b.Id);
        Assert.False(no.Ok);
        Assert.Contains("turn", no.Reason);
        a.Attributes[AttributeKind.Agility] = 50;
        Assert.Contains("Agility 60", TacticsView.Picker(sim, 1, "sprint").Single(r => r.UnitId == a.Id).Reason);
        // An AP boost that the unit cannot pay is a reason too.
        a.Ap = 0;
        Assert.Contains("AP", TacticsView.Picker(sim, 1, "dash", boost: true).Single(r => r.UnitId == a.Id).Reason);
        Assert.True(TacticsView.Picker(sim, 1, "dash").Single(r => r.UnitId == a.Id).Ok);
    }

    [Fact]
    public void Reactions_are_offered_only_against_a_seen_plan()
    {
        var (sim, a, _, husk) = Field();
        Assert.Empty(TacticsView.ReactionTargets(sim, 1));
        husk.Plan = new PlannedAction { Path = [], AttackTile = a.Pos, ActionId = "claw", Round = 1 };
        Assert.Equal([husk.Id], TacticsView.ReactionTargets(sim, 1).Select(u => u.Id));
        Assert.True(TacticsView.Picker(sim, 1, "sidestep", target: husk.Id).Single(r => r.UnitId == a.Id).Ok);
    }

    [Fact]
    public void The_deck_builder_shows_what_is_owned_used_and_allowed()
    {
        var sim = NewGame.Create(TestData.Real, 7, Real);
        var view = DeckBuilder.View(sim);
        Assert.Equal(Real.Cards.Rules.MaxDeck, view.MaxDeck);
        Assert.Equal(sim.State.Cards.Deck.Count, view.DeckCount);
        var dash = view.Rows.Single(r => r.Id == "shove");
        Assert.Equal((2, 2, 0, 0), (dash.Owned, dash.InDeck, dash.InSlots, dash.Free));
        Assert.Equal(Real.Cards.Rules.MaxCopies, dash.MaxCopies);
        Assert.All(view.Slots, s => Assert.True(s.Slots >= 0));
        Assert.Contains(view.Upgrades, u => u.Id == "budget_1" && u.Available && !u.Bought);
        Assert.Contains(view.Upgrades, u => u.Id == "budget_2" && !u.Available);
    }

    [Fact]
    public void A_survivors_slot_count_in_the_village_comes_from_level_skills_and_perks()
    {
        var s = TestData.Survivor();
        Assert.Equal(0, CardSlots.Count(Real, s));
        s.Level = Real.Cards.Rules.Slots.Level;
        s.Set(Skill.Melee, 60);
        Assert.Equal(2, CardSlots.Count(Real, s));
    }

    [Fact]
    public void The_bestiary_list_hides_what_the_colony_has_not_learned()
    {
        var colony = new ColonyState();
        colony.Bestiary["husk"] = new BestiaryEntry { Seen = 1, Fought = 5, Killed = 2 };
        colony.Bestiary["linked"] = new BestiaryEntry { Seen = 1 };
        var list = Bestiary.List(Real, colony.Bestiary);
        Assert.Equal(Real.Enemies.Count, list.Count);
        Assert.Equal(BestiaryTier.Killed, list.Single(c => c.Id == "husk").Tier);
        Assert.Equal(BestiaryTier.Seen, list.Single(c => c.Id == "linked").Tier);
        var unknown = list.Single(c => c.Id == "linked_warden");
        Assert.Equal(BestiaryTier.Unknown, unknown.Tier);
        Assert.Equal("???", unknown.Name);
        Assert.True(list.IndexOf(list.Single(c => c.Id == "husk")) < list.IndexOf(unknown)); // known first
    }
}
