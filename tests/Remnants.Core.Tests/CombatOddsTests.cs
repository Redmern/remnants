using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>AC-14, AC-15, AC-27 to AC-29: bounded luck with odds shown first, and previews that name what changes the result.</summary>
public class CombatOddsTests
{
    private static ScavengeData Crit(double chance) => With(r => r with
    {
        Combat = r.Combat with { Crit = r.Combat.Crit with { Chance = chance, Side = 0, Rear = 0 }, MinHitChance = 1, MaxHitChance = 1 },
    });

    private static (ScavengeSim Sim, Unit A, Unit B) Pair(ScavengeData? data = null, ulong seed = 1, string[]? rows = null, Pos? b = null)
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var v = SurvivorUnit(2, b ?? new(5, 3), weapon: "pipe");
        return (Arena(14, 7, [a, v], rows: rows, seed: seed, data: data), a, v);
    }

    // Crits (AC-27)

    [Fact]
    public void Flank_and_rear_raise_the_crit_chance()
    {
        var rules = Real.Rules.Combat.Crit;
        Assert.True(rules.Chance > 0);
        Assert.True(Combat.CritChance(rules, FacingArc.Side) > Combat.CritChance(rules, FacingArc.Front));
        Assert.True(Combat.CritChance(rules, FacingArc.Rear) > Combat.CritChance(rules, FacingArc.Side));
    }

    [Fact]
    public void A_crit_does_more_damage_and_says_so()
    {
        double Dealt(double chance)
        {
            var (sim, a, b) = Pair(Crit(chance));
            Assert.True(StrikeNow(sim, a, b, "swing").Ok);
            return b.MaxHp - b.Hp;
        }
        Assert.True(Dealt(1) > Dealt(0));
        var (s, x, y) = Pair(Crit(1));
        Assert.True(StrikeNow(s, x, y, "swing").Ok);
        Assert.Contains(s.State.Events, e => e.Type == ScavengeEventType.Attacked && e.Message.Contains("critical"));
    }

    [Fact]
    public void The_same_seed_gives_the_same_blows()
    {
        double Run()
        {
            var (sim, a, b) = Pair(seed: 42);
            for (var i = 0; i < 6; i++)
            {
                (a.Ap, a.Fatigue) = (1, 0);
                StrikeNow(sim, a, b, "swing");
            }
            return b.Hp;
        }
        Assert.Equal(Run(), Run());
    }

    // Odds (AC-28)

    [Fact]
    public void The_odds_add_up_and_match_the_tier_rules()
    {
        var (sim, a, b) = Pair();
        var action = sim.Profile(a).Action("swing")!;
        var odds = TacticsView.Odds(sim, a, action, b);
        Assert.Equal(1, odds.Miss + odds.Graze + odds.Hit + odds.Solid, 6);
        var tiers = sim.Data.Rules.Combat.Tiers;
        var chance = Combat.ChanceToHit(sim, a, action, b);
        Assert.Equal(1 - chance, odds.Miss, 6);
        Assert.Equal(chance * tiers.GrazeBelow, odds.Graze, 6);
        Assert.Equal(chance * (1 - tiers.SolidFrom), odds.Solid, 6);
    }

    [Fact]
    public void Cover_moves_the_odds_down_a_tier()
    {
        string[] rows = ["..............", "..............", "..............", ".....c........", "..............", "..............", ".............."];
        var (sim, a, b) = Pair(rows: rows, b: new(6, 3));
        a.Pos = new(4, 3);
        b.Facing = new Pos(-1, 0);
        var rifle = sim.Profile(a).Action("swing")! with { Pattern = AttackPattern.Ranged, Reach = 5, Modes = null };
        var odds = TacticsView.Odds(sim, a, rifle, b);
        Assert.Equal(0, odds.Solid, 6);
        Assert.Contains(odds.Factors, f => f.Contains("cover"));
    }

    [Fact]
    public void The_odds_name_a_blow_from_behind_and_the_crit_it_brings()
    {
        var (sim, a, b) = Pair();
        var action = sim.Profile(a).Action("swing")!;
        b.Facing = new Pos(-1, 0);
        var front = TacticsView.Odds(sim, a, action, b);
        b.Facing = new Pos(1, 0);
        var rear = TacticsView.Odds(sim, a, action, b);
        Assert.True(rear.Crit > front.Crit);
        Assert.Contains(rear.Factors, f => f.Contains("rear"));
        Assert.DoesNotContain(front.Factors, f => f.Contains("rear"));
    }

    [Fact]
    public void The_odds_give_the_stun_a_guarded_target_may_land()
    {
        var (sim, a, b) = Pair();
        var action = sim.Profile(a).Action("swing")!;
        var share = Defences.BlockOf(sim, b, Defences.GuardOf(sim, b).Block);
        Assert.Equal(Stuns.Chance(sim, b, a, action, share, perfect: false), TacticsView.Odds(sim, a, action, b).StunIfGuarded, 6);
    }

    [Fact]
    public void A_defend_preview_gives_what_it_blocks_and_the_stun_chance()
    {
        var (sim, a, b) = Pair();
        var odds = TacticsView.DefendOdds(sim, a);
        var seen = Assert.Single(odds);
        Assert.Equal(b.Id, seen.UnitId);
        Assert.True(seen.Block > 0);
        Assert.InRange(seen.Stun, 0.0001, 1);
    }

    // Factors (AC-15)

    [Fact]
    public void Low_stamina_and_adrenaline_are_named()
    {
        var (sim, a, _) = Pair();
        Assert.DoesNotContain(TacticsView.Factors(sim, a), f => f.Contains("stamina"));
        a.Fatigue = sim.Profile(a).MaxStamina - 1;
        Assert.Contains(TacticsView.Factors(sim, a), f => f.Contains("low stamina: acts later"));
        a.Adrenaline = 60;
        Assert.Contains(TacticsView.Factors(sim, a), f => f.Contains("adrenaline"));
    }

    // Push preview (AC-14)

    [Fact]
    public void A_push_preview_warns_of_a_wall()
    {
        string[] rows = ["..............", "..............", "..............", "......#.......", "..............", "..............", ".............."];
        var (sim, a, b) = Pair(rows: rows);
        var outcome = Assert.Single(TacticsView.PushPreview(sim, a, new(1, 0)));
        Assert.Equal(b.Id, outcome.UnitId);
        Assert.Equal(b.Pos, outcome.Landing);
        Assert.True(outcome.Collides);
        Assert.Contains("wall", outcome.Text);
        Assert.Contains(sim.Data.Rules.Combat.Push.CollisionDamage.ToString("0"), outcome.Text);
    }

    [Fact]
    public void A_push_preview_warns_of_a_seen_unit_but_not_a_hidden_one()
    {
        var other = SurvivorUnit(2, new(6, 3), weapon: "pipe");
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [a, b, other]);
        var outcome = Assert.Single(TacticsView.PushPreview(sim, a, new(1, 0)));
        Assert.True(outcome.Collides);

        // Eyes that make out nothing past the next tile.
        var nearSighted = With(r => r with { Fog = r.Fog with { CloseRange = 1, PerTile = 1000 } });
        var (c, d, e) = (SurvivorUnit(1, new(4, 3), weapon: "pipe"), SurvivorUnit(2, new(5, 3), weapon: "pipe"), SurvivorUnit(2, new(6, 3), weapon: "pipe"));
        var fogged = Arena(14, 7, [c, d, e], data: nearSighted);
        Assert.False(Detection.SideSees(fogged.Data, fogged.State, c.Side, e));
        Assert.False(TacticsView.PushPreview(fogged, c, new(1, 0)).Single().Collides);
    }

    [Fact]
    public void A_push_preview_shows_the_landing_and_a_broken_guard_is_named()
    {
        var (sim, a, b) = Pair();
        var outcome = Assert.Single(TacticsView.PushPreview(sim, a, new(1, 0)));
        Assert.False(outcome.Collides);
        Assert.Equal(new Pos(6, 3), outcome.Landing);
        Assert.Contains("breaks a guard", outcome.Text);
    }

    [Fact]
    public void A_push_preview_does_not_count_the_pusher_who_has_walked_off()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = SurvivorUnit(2, new(5, 4), weapon: "pipe");
        var sim = Arena(14, 7, [a, foe]);
        Assert.True(sim.Execute(new Move(1, a.Id, new(6, 4))).Ok);
        Assert.False(TacticsView.PushPreview(sim, a, new(-1, 0)).Single().Collides);
        Assert.True(TacticsView.PushPreview(sim, a, new(-1, 0)).Single().Text.Length > 0);
    }

    [Fact]
    public void A_push_on_an_ally_breaks_no_guard_so_its_preview_says_none()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var friend = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [a, friend]);
        Assert.DoesNotContain("guard", TacticsView.PushPreview(sim, a, new(1, 0)).Single().Text);
    }

    [Fact]
    public void A_push_at_nobody_previews_nothing()
    {
        var (sim, a, _) = Pair(b: new(10, 3));
        Assert.Empty(TacticsView.PushPreview(sim, a, new(1, 0)));
    }

    // A correct read (AC-29)

    /// <summary>The share of 300 seeds in which a stab at <paramref name="aimedAt"/> hurts a quicker rival dashing from (5,2) to (5,3).</summary>
    private static double LandsAt(Pos aimedAt)
    {
        var (lands, runs) = (0, 300);
        for (var seed = 1UL; seed <= (ulong)runs; seed++)
        {
            var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
            var b = SurvivorUnit(2, new(5, 2), weapon: "pipe");
            var sim = Arena(14, 7, [a, b], seed: seed);
            b.PriorityBonus = 5;
            Assert.True(sim.Execute(new Dash(2, b.Id, new(0, 1), 1)).Ok);
            Assert.True(sim.Execute(new AttackTile(1, a.Id, aimedAt, "swing", StrikeMode.Stab, Combat.Face(a.Pos, aimedAt))).Ok);
            Settle(sim);
            if (b.Hp < b.MaxHp) lands++;
        }
        return (double)lands / runs;
    }

    [Fact]
    public void A_correct_read_wins_clearly_more_often_than_a_wrong_one_but_not_always()
    {
        var right = LandsAt(new(5, 3));
        var wrong = LandsAt(new(5, 2));
        Assert.InRange(right, 0.6, 0.95);
        Assert.Equal(0, wrong);
    }
}
