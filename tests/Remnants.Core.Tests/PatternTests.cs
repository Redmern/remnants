using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Attack and defence cards work on tile-offset patterns, rotated free of cost; defended tiles soften any hit on them at resolve time.</summary>
public class PatternTests
{
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    private static ScavengeSim Table(IEnumerable<Unit> units, params string[] hand)
    {
        var sim = Arena(12, 7, units, data: Sure);
        foreach (var pid in units.Where(u => u.PlayerId is not null).Select(u => u.PlayerId!.Value).Distinct())
            sim.State.Decks.Add(new SquadDeck { PlayerId = pid, Hand = hand.ToList(), Points = 9, Budget = 9, HandSize = 9 });
        return sim;
    }

    private static CardPattern Custom(params int[][] offsets) => new() { Offsets = offsets.ToList() };

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, 1)]
    [InlineData(-1, -1)]
    public void Named_shapes_cover_what_a_stab_and_a_slash_cover(int x, int y)
    {
        var map = new TileMap(12, 12);
        var action = new ActionProfile("t", "t", null, AttackPattern.Melee, 2, 1, 1, 1, 1, 0, Modes: [StrikeMode.Stab, StrikeMode.Slash]);
        var (from, dir) = (new Pos(5, 5), new Pos(x, y));
        Assert.Equal(Combat.Covered(map, action, StrikeMode.Slash, from, dir).ToHashSet(), Patterns.Tiles(map, new CardPattern { Shape = PatternShape.Slash }, from, dir).ToHashSet());
        Assert.Equal(Combat.Covered(map, action, StrikeMode.Stab, from, dir).ToHashSet(), Patterns.Tiles(map, new CardPattern { Shape = PatternShape.Stab, Length = 2 }, from, dir).ToHashSet());
    }

    [Fact]
    public void Custom_offsets_are_forward_and_side_and_rotate_with_the_direction()
    {
        var map = new TileMap(12, 12);
        var hook = Custom([1, 0], [2, 1]);
        var from = new Pos(5, 5);
        Assert.Equal([new Pos(6, 5), new Pos(7, 6)], Patterns.Tiles(map, hook, from, new Pos(1, 0)));
        Assert.Equal([new Pos(5, 6), new Pos(4, 7)], Patterns.Tiles(map, hook, from, new Pos(0, 1)));
        Assert.Equal([new Pos(4, 5), new Pos(3, 4)], Patterns.Tiles(map, hook, from, new Pos(-1, 0)));
        Assert.Equal([new Pos(5, 4), new Pos(6, 3)], Patterns.Tiles(map, hook, from, new Pos(0, -1)));
    }

    [Fact]
    public void An_area_keeps_its_shape_whatever_the_direction_and_tiles_off_the_map_drop()
    {
        var map = new TileMap(12, 12);
        var area = new CardPattern { Shape = PatternShape.Area, Length = 1 };
        Assert.Equal(9, Patterns.Tiles(map, area, new Pos(5, 5), new Pos(1, 1)).Count);
        Assert.Equal(Patterns.Tiles(map, area, new Pos(5, 5), new Pos(1, 0)).ToHashSet(), Patterns.Tiles(map, area, new Pos(5, 5), new Pos(-1, 1)).ToHashSet());
        Assert.Equal(4, Patterns.Tiles(map, area, new Pos(0, 0), new Pos(1, 0)).Count);
    }

    [Fact]
    public void The_card_data_has_attack_and_defence_patterns_with_custom_offsets()
    {
        Assert.NotNull(Real.Cards.Find("sweep")!.Pattern);
        Assert.True(Real.Cards.Find("sweep")!.Effect.Attack);
        Assert.True(Real.Cards.Find("parry")!.Effect.Block > 0);
        Assert.Contains(Real.Cards.All.Values, c => c.Pattern?.Offsets is { Count: > 0 });
    }

    [Fact]
    public void An_attack_card_strikes_every_hostile_on_its_rotated_pattern_and_rotating_costs_nothing()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var x = SurvivorUnit(2, new(3, 4));
        var y = SurvivorUnit(2, new(2, 4));
        var away = SurvivorUnit(2, new(4, 2));
        var sim = Table([a, x, y, away], "sweep", "sweep");
        var deck = SquadDecks.Of(sim.State, 1)!;
        var (points, ap) = (deck.Points, a.Ap);
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "sweep", Direction: new Pos(0, 1))).Ok);
        var spent = (points - deck.Points, ap - a.Ap);
        Assert.Equal(100, x.Hp);
        Settle(sim);
        Assert.True(x.Hp < 100);
        Assert.True(y.Hp < 100);
        Assert.Equal(100, away.Hp);

        var b = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var sim2 = Table([b, SurvivorUnit(2, new(9, 3))], "sweep");
        var deck2 = SquadDecks.Of(sim2.State, 1)!;
        var (points2, ap2) = (deck2.Points, b.Ap);
        Assert.True(sim2.Execute(new PlayCard(1, b.Id, "sweep", Direction: new Pos(1, 1))).Ok);
        Assert.Equal(spent, (points2 - deck2.Points, ap2 - b.Ap));
    }

    [Fact]
    public void An_attack_card_is_aimed_from_where_the_planned_moves_end()
    {
        var a = SurvivorUnit(1, new(1, 3), weapon: "pipe");
        var x = SurvivorUnit(2, new(4, 3));
        var sim = Table([a, x], "sweep");
        Assert.True(sim.Execute(new Move(1, a.Id, new(3, 3))).Ok);
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "sweep", Direction: new Pos(1, 0))).Ok);
        Settle(sim);
        Assert.True(x.Hp < 100);
    }

    private static double DamageOn(Pos tile, bool defended, string? guardCard = "parry", Action<Unit>? defender = null)
    {
        var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var b = SurvivorUnit(2, tile);
        var c = SurvivorUnit(2, new(tile.X, tile.Y + 1));
        defender?.Invoke(c);
        var sim = Table([a, b, c], "parry", "shield_wall");
        if (defended) Assert.True(sim.Execute(new PlayCard(2, c.Id, guardCard!, Tile: tile, Direction: new Pos(0, -1))).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, tile, "swing")).Ok);
        Settle(sim);
        return 100 - b.Hp;
    }

    [Fact]
    public void A_defended_tile_softens_a_hit_on_whoever_stands_there()
    {
        var at = new Pos(3, 3);
        Assert.True(DamageOn(at, defended: true) < DamageOn(at, defended: false));
    }

    [Fact]
    public void A_defence_holds_for_who_walks_onto_the_tile_not_who_stood_there_when_it_was_planned()
    {
        var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var guard = SurvivorUnit(2, new(4, 4));
        var walker = SurvivorUnit(2, new(5, 3));
        var sim = Table([a, guard, walker], "parry");
        var tile = new Pos(4, 3);
        a.Attributes[AttributeKind.Agility] = 80;
        Assert.True(sim.Execute(new PlayCard(2, guard.Id, "parry", Tile: tile)).Ok);
        Assert.True(sim.Execute(new Move(2, walker.Id, tile)).Ok);
        Assert.True(sim.Execute(new Move(1, a.Id, new(3, 3))).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, tile, "swing")).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Blocked && e.TargetId == walker.Id);
    }

    [Fact]
    public void A_defence_does_nothing_against_its_own_side()
    {
        var c = SurvivorUnit(2, new(3, 4));
        var ally = SurvivorUnit(2, new(2, 3), weapon: "pipe");
        var friend = SurvivorUnit(2, new(3, 3));
        var sim = Table([c, ally, friend], "parry");
        Assert.True(sim.Execute(new PlayCard(2, c.Id, "parry", Tile: friend.Pos)).Ok);
        Assert.True(sim.Execute(new AttackTile(2, ally.Id, friend.Pos, "swing")).Ok);
        Settle(sim);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.Blocked);
    }

    [Fact]
    public void Block_is_card_base_plus_block_skill_plus_armor_capped()
    {
        var rules = Sure.Rules.Combat.Block;
        var plain = SurvivorUnit(2, new(5, 5));
        plain.Skills[Skill.Melee] = 0;
        var skilled = SurvivorUnit(2, new(6, 5));
        skilled.Skills[Skill.Melee] = 50;
        var armored = SurvivorUnit(2, new(7, 5), body: "riot_armor");
        armored.Skills[Skill.Melee] = 0;
        var sim = Table([plain, skilled, armored]);
        var card = Sure.Cards.Find("parry")!.Effect.Block;
        Assert.Equal(card, Defences.BlockOf(sim, plain, card), 6);
        Assert.Equal(card + 50 * rules.PerSkill, Defences.BlockOf(sim, skilled, card), 6);
        Assert.True(Defences.BlockOf(sim, armored, card) > card);
        Assert.Equal(rules.Max, Defences.BlockOf(sim, skilled, 5), 6);
    }

    [Fact]
    public void A_block_that_comes_up_before_the_blow_is_perfect_and_a_late_one_still_counts()
    {
        bool Perfect(bool early)
        {
            var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
            var b = SurvivorUnit(2, new(3, 3));
            var sim = Table([a, b], "parry");
            (early ? b : a).Attributes[AttributeKind.Agility] = 95;
            Assert.True(sim.Execute(new PlayCard(2, b.Id, "parry", Tile: b.Pos)).Ok);
            if (!early) b.PriorityBonus = -20;
            Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
            Settle(sim);
            var block = Assert.Single(sim.State.Events, e => e.Type == ScavengeEventType.Blocked);
            Assert.Equal(early ? OutcomeKind.PerfectBlock : OutcomeKind.Block, ReplayFeedback.Of(block)!.Kind);
            return block.Message.Contains("perfect");
        }
        Assert.True(Perfect(early: true));
        Assert.False(Perfect(early: false));
    }

    [Fact]
    public void A_guard_struck_down_before_the_blow_leaves_the_tile_open()
    {
        var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var killer = SurvivorUnit(1, new(4, 5), weapon: "pipe");
        var guard = SurvivorUnit(2, new(4, 4));
        var b = SurvivorUnit(2, new(3, 3));
        guard.Hp = 0.5;
        guard.CanBeDowned = false;
        var sim = Table([a, killer, guard, b], "parry");
        killer.PriorityBonus = 50;
        guard.PriorityBonus = -30;
        Assert.True(sim.Execute(new PlayCard(2, guard.Id, "parry", Tile: b.Pos)).Ok);
        Assert.True(sim.Execute(new AttackTile(1, killer.Id, guard.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Settle(sim);
        Assert.Equal(UnitStatus.Dead, guard.Status);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.Blocked);
    }

    [Fact]
    public void A_block_whose_owner_was_moved_away_still_comes_up_and_holds_its_tile()
    {
        var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(3, 3));
        var c = SurvivorUnit(2, new Pos(3, 4));
        var sim = Table([a, b, c], "parry");
        Assert.True(sim.Execute(new PlayCard(2, c.Id, "parry", Tile: b.Pos)).Ok);
        c.PriorityBonus = -30;
        c.Pos = new Pos(9, 6);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Blocked && e.TargetId == b.Id);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == c.Id);
    }

    [Fact]
    public void A_planned_defence_is_hidden_and_taking_it_back_refunds_it()
    {
        var a = SurvivorUnit(1, new(2, 3));
        var b = SurvivorUnit(2, new(3, 3));
        var sim = Table([a, b], "parry");
        var deck = SquadDecks.Of(sim.State, 2)!;
        var (points, ap, tired, events) = (deck.Points, b.Ap, b.Fatigue, sim.State.Events.Count);
        Assert.True(sim.Execute(new PlayCard(2, b.Id, "parry", Tile: b.Pos)).Ok);
        Assert.Equal(events, sim.State.Events.Count);
        Assert.Empty(sim.State.Defences);
        Assert.True(sim.Execute(new UndoPlan(2, b.Id)).Ok);
        Assert.Equal((points, ap, tired), (deck.Points, b.Ap, b.Fatigue));
        Assert.Contains("parry", deck.Hand);
    }

    [Fact]
    public void Blocks_resolve_early()
    {
        var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var sim = Table([a]);
        var guard = new PlanStep { Kind = PlanStepKind.Card, CardId = "parry" };
        var blow = new PlanStep { Kind = PlanStepKind.Strike, Strike = new TileStrike { ActionId = "swing", UnitId = a.Id } };
        Assert.True(Priority.Action(sim, a, guard) > Priority.Action(sim, a, blow));
    }

    [Fact]
    public void A_pattern_hits_an_ally_on_it_and_the_aim_preview_warns_of_it()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var friend = SurvivorUnit(1, new(4, 3));
        var foe = SurvivorUnit(2, new(4, 4));
        var sim = Table([a, friend, foe], "sweep");
        var aim = TacticsView.PatternAim(sim, a, "sweep", null, new Pos(1, 0));
        Assert.Contains(new Pos(4, 3), aim.Tiles);
        Assert.Equal([new Pos(4, 3)], aim.AllyTiles);
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "sweep", Direction: new Pos(1, 0))).Ok);
        Settle(sim);
        Assert.True(friend.Hp < 100);
        Assert.True(foe.Hp < 100);
    }

    [Fact]
    public void An_attacker_is_never_struck_by_its_own_pattern()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var sim = Table([a, SurvivorUnit(2, new(9, 3))]);
        var hits = TacticsView.AllyTiles(sim, a, [a.Pos]);
        Assert.Empty(hits);
    }

    [Fact]
    public void The_ai_aims_a_patterned_blow_around_its_friends()
    {
        var u = SurvivorUnit(1, new(5, 2));
        var blade = EnemyUnit("tally_blade", new(4, 2));
        var mate = EnemyUnit("tally_blade", new(5, 1));
        var sim = Arena(10, 5, [u, blade, mate], data: Sure);
        blade.Ai.Mode = AiMode.Hunting;
        ScavengeAi.TakeTurn(sim, blade);
        var strike = Strikes(sim).First(s => s.UnitId == blade.Id);
        Assert.Contains(u.Pos, strike.Tiles);
        Assert.DoesNotContain(mate.Pos, strike.Tiles);
    }

    [Fact]
    public void The_same_seed_and_plans_give_the_same_block_outcome()
    {
        List<string> Run()
        {
            var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
            var b = SurvivorUnit(2, new(3, 3));
            var sim = Table([a, b], "parry");
            sim.Execute(new PlayCard(2, b.Id, "parry", Tile: b.Pos));
            sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing"));
            sim.Execute(new EndTurn(1, a.Id));
            sim.Execute(new EndTurn(2, b.Id));
            return sim.State.Events.Select(e => e.Message).ToList();
        }
        Assert.Equal(Run(), Run());
    }
}
