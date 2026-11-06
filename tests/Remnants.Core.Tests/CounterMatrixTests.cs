using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// AC-13: the counter relations, pair by pair. Defend beats Attack, Push breaks a guard, Attack beats Push and Dash when it is
/// first or covers the destination, Dash slips a Push, and Recover loses to any landed blow. Nothing beats everything.
/// </summary>
public class CounterMatrixTests
{
    public enum Act { Attack, Defend, Push, Dash, Recover }

    /// <summary>Who goes first when the two come up: A, B, or the same score (a clash).</summary>
    public enum First { A, B, Tie }

    public enum Winner { A, B, Neither }

    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    private static readonly Pos East = new(1, 0);
    private static readonly Pos West = new(-1, 0);
    private static readonly Pos South = new(0, 1);

    private static (ScavengeSim Sim, Unit A, Unit B) Table(ScavengeData? data = null, string? weaponA = "pipe", string? weaponB = "pipe")
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: weaponA);
        var b = SurvivorUnit(2, new(5, 3), weapon: weaponB);
        return (Arena(14, 7, [a, b], data: data ?? Sure), a, b);
    }

    private static IScavengeCommand Order(Act act, int pid, Unit unit, Unit foe, Pos toward) => act switch
    {
        Act.Attack => new AttackTile(pid, unit.Id, foe.Pos, "swing", StrikeMode.Stab, toward),
        Act.Defend => new Defend(pid, unit.Id, toward),
        Act.Push => new Push(pid, unit.Id, toward),
        // Away along the board's other axis: a dash that gets clear of the other unit's tile.
        Act.Dash => new Dash(pid, unit.Id, South, 3),
        _ => new Recover(pid, unit.Id),
    };

    /// <summary>Plans both actions, sets who comes up first, and resolves. Returns who came off better.</summary>
    private static (Winner Winner, ScavengeSim Sim, Unit A, Unit B) Duel(Act actA, Act actB, First first)
    {
        var (sim, a, b) = Table();
        Assert.True(sim.Execute(Order(actA, 1, a, b, East)).Ok);
        Assert.True(sim.Execute(Order(actB, 2, b, a, West)).Ok);
        // Stamina paid differs by action; only the order asked for matters here.
        (a.Fatigue, b.Fatigue) = (0, 0);
        var (stepA, stepB) = (Plans.Of(sim.State, a)!.Steps[0], Plans.Of(sim.State, b)!.Steps[0]);
        var gap = Priority.Action(sim, a, stepA) - Priority.Action(sim, b, stepB);
        // The clash needs equal scores; a lead is big enough to outweigh all else.
        a.PriorityBonus = first switch { First.A => 50, First.B => -50, _ => -gap };
        Settle(sim);
        bool Lost(Unit me, Act act, Unit other)
        {
            var e = sim.State.Events;
            if (act == Act.Attack && e.Exists(x => x.Type == ScavengeEventType.Blocked && x.TargetId == other.Id)) return true;
            if (act == Act.Attack && e.Exists(x => x.Type == ScavengeEventType.IntentFizzled && x.UnitId == me.Id)) return true;
            if (act == Act.Push && e.Exists(x => x.Type == ScavengeEventType.IntentFizzled && x.UnitId == me.Id)) return true;
            if (act == Act.Defend && e.Exists(x => x.Type == ScavengeEventType.GuardBroken && x.TargetId == me.Id)) return true;
            if (act == Act.Recover && e.Exists(x => x.Type == ScavengeEventType.IntentCanceled && x.UnitId == me.Id)) return true;
            return act != Act.Defend && me.Hp < me.MaxHp && actA != actB;
        }
        var (lostA, lostB) = (Lost(a, actA, b), Lost(b, actB, a));
        return (lostA == lostB ? Winner.Neither : lostA ? Winner.B : Winner.A, sim, a, b);
    }

    [Theory]
    // Defend beats Attack, whoever comes up first: the guard is up when the action phase opens.
    [InlineData(Act.Defend, Act.Attack, First.A, Winner.A)]
    [InlineData(Act.Defend, Act.Attack, First.B, Winner.A)]
    [InlineData(Act.Defend, Act.Attack, First.Tie, Winner.A)]
    // Push breaks a guard, whoever comes up first.
    [InlineData(Act.Push, Act.Defend, First.A, Winner.A)]
    [InlineData(Act.Push, Act.Defend, First.B, Winner.A)]
    [InlineData(Act.Push, Act.Defend, First.Tie, Winner.A)]
    // Attack beats Push when it lands first; a push that comes first throws the attacker's blow off.
    [InlineData(Act.Attack, Act.Push, First.A, Winner.A)]
    [InlineData(Act.Attack, Act.Push, First.B, Winner.B)]
    // Attack beats Dash when it lands before the dash leaves; a dash gone first has dodged it.
    [InlineData(Act.Attack, Act.Dash, First.A, Winner.A)]
    [InlineData(Act.Attack, Act.Dash, First.B, Winner.B)]
    // Dash slips a Push, whoever comes up first.
    [InlineData(Act.Dash, Act.Push, First.A, Winner.A)]
    [InlineData(Act.Dash, Act.Push, First.B, Winner.A)]
    [InlineData(Act.Dash, Act.Push, First.Tie, Winner.A)]
    // A landed blow beats Recover, whoever comes up first.
    [InlineData(Act.Attack, Act.Recover, First.A, Winner.A)]
    [InlineData(Act.Attack, Act.Recover, First.B, Winner.A)]
    [InlineData(Act.Attack, Act.Recover, First.Tie, Winner.A)]
    // These do not touch each other.
    [InlineData(Act.Defend, Act.Dash, First.A, Winner.Neither)]
    [InlineData(Act.Defend, Act.Recover, First.A, Winner.Neither)]
    [InlineData(Act.Dash, Act.Recover, First.A, Winner.Neither)]
    [InlineData(Act.Push, Act.Recover, First.A, Winner.Neither)]
    [InlineData(Act.Defend, Act.Defend, First.A, Winner.Neither)]
    [InlineData(Act.Dash, Act.Dash, First.A, Winner.Neither)]
    [InlineData(Act.Recover, Act.Recover, First.A, Winner.Neither)]
    [InlineData(Act.Attack, Act.Attack, First.Tie, Winner.Neither)]
    public void Pair(Act a, Act b, First first, Winner expected) => Assert.Equal(expected, Duel(a, b, first).Winner);

    [Fact]
    public void Nothing_beats_everything()
    {
        var acts = Enum.GetValues<Act>();
        foreach (var act in acts)
        {
            var beaten = acts.Any(other => other != act && Enum.GetValues<First>().Any(first =>
            {
                try { return Duel(other, act, first).Winner == Winner.A; }
                catch (Xunit.Sdk.XunitException) { return false; }
            }));
            Assert.True(beaten, $"{act} has no counter");
        }
    }

    [Fact]
    public void A_broken_guard_leaves_the_defender_open_to_a_later_blow_and_the_push_goes_its_full_way()
    {
        var (sim, a, b) = Table();
        var third = SurvivorUnit(2, new(5, 2), weapon: "pipe");
        sim.State.Units.Add(third);
        third.Id = 77;
        (third.Ap, third.Mp) = (1, 4);
        b.PriorityBonus = 50;
        third.PriorityBonus = -50;
        Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new Push(2, b.Id, West)).Ok);
        // The blow falls on where the push leaves the defender, one tile on.
        Assert.True(sim.Execute(new AttackTile(2, third.Id, new Pos(4, 3), "swing", StrikeMode.Stab, new Pos(-1, 1))).Ok);
        Plans.Of(sim.State, third)!.Steps[0].Strike!.Tiles = [new Pos(3, 3)];
        Settle(sim);
        Assert.Equal(new Pos(3, 3), a.Pos);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.GuardBroken && e.TargetId == a.Id && e.UnitId == b.Id);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.Blocked);
        Assert.True(a.Hp < a.MaxHp);
    }

    [Fact]
    public void A_block_card_stands_up_to_a_push_where_defend_does_not()
    {
        var (sim, a, b) = Table();
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Hand = ["parry"], Points = 9, Budget = 9, HandSize = 9 });
        b.PriorityBonus = 50;
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "parry", Tile: a.Pos)).Ok);
        Assert.True(sim.Execute(new Push(2, b.Id, West)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(4, 3), a.Pos);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.GuardBroken);
    }

    [Fact]
    public void A_friend_on_a_guarded_tile_is_shoved_a_tile_less_and_the_guard_holds()
    {
        var (sim, a, b) = Table();
        var friend = SurvivorUnit(1, new(4, 4));
        sim.State.Units.Add(friend);
        friend.Id = 77;
        var pusher = SurvivorUnit(2, new(5, 4));
        sim.State.Units.Add(pusher);
        pusher.Id = 78;
        (pusher.Ap, pusher.Mp) = (1, 4);
        Assert.True(sim.Execute(new Defend(1, a.Id, South)).Ok);
        Assert.True(sim.Execute(new Push(2, pusher.Id, West)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(4, 4), friend.Pos);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.GuardBroken);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Blocked && e.UnitId == a.Id);
    }

    [Fact]
    public void An_ally_shove_does_not_break_a_guard()
    {
        var (sim, a, _) = Table();
        var friend = SurvivorUnit(1, new(3, 3));
        sim.State.Units.Add(friend);
        friend.Id = 77;
        (friend.Ap, friend.Mp) = (1, 4);
        Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new Push(1, friend.Id, East)).Ok);
        Settle(sim);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.GuardBroken);
    }

    [Fact]
    public void A_push_that_finds_nobody_leaves_a_guard_up()
    {
        var (sim, a, b) = Table();
        Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new Push(2, b.Id, South)).Ok);
        Settle(sim);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.GuardBroken);
    }

    [Fact]
    public void A_dash_slips_a_faster_push_and_the_push_says_so_in_the_log()
    {
        var (sim, a, b) = Table();
        a.PriorityBonus = 50;
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new Dash(2, b.Id, South, 3)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(5, 6), b.Pos);
        var fizzle = sim.State.Events.Single(e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == a.Id);
        Assert.Contains("dash", fizzle.Message);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.PatternResolved && e.UnitId == b.Id);
    }

    [Fact]
    public void A_dash_that_cannot_get_away_is_pushed_after_all()
    {
        var (sim, a, b) = Table();
        a.PriorityBonus = 50;
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new Dash(2, b.Id, South, 3)).Ok);
        sim.State.Map[new Pos(5, 4)] = Terrain.Wall;
        Settle(sim);
        Assert.Equal(new Pos(6, 3), b.Pos);
    }

    [Theory]
    [InlineData(First.A)]
    [InlineData(First.B)]
    [InlineData(First.Tie)]
    public void A_blow_on_the_dashes_landing_hits_it_there_whoever_comes_up_first(First first)
    {
        var (sim, a, b) = Table(weaponA: "spear");
        // b dashes south from (5,3): 3 tiles, or 1 at a clash's reduced effect. a's thrust along the row covers where it lands.
        var landing = first == First.Tie ? new Pos(5, 4) : new Pos(5, 6);
        a.Pos = landing + West;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, landing, "thrust", StrikeMode.Stab, East)).Ok);
        Assert.True(sim.Execute(new Dash(2, b.Id, South, 3)).Ok);
        (a.Fatigue, b.Fatigue) = (0, 0);
        var gap = Priority.Action(sim, a, Plans.Of(sim.State, a)!.Steps[0]) - Priority.Action(sim, b, Plans.Of(sim.State, b)!.Steps[0]);
        a.PriorityBonus = first switch { First.A => 50, First.B => -50, _ => -gap };
        Settle(sim);
        Assert.Equal(landing, b.Pos);
        Assert.True(b.Hp < b.MaxHp, $"{first}: not hit at {b.Pos}");
    }

    [Fact]
    public void A_blow_on_the_tile_a_push_lands_someone_on_hits_them_when_it_comes_later()
    {
        var (sim, a, b) = Table();
        var striker = SurvivorUnit(1, new(6, 2), weapon: "pipe");
        sim.State.Units.Add(striker);
        striker.Id = 77;
        (striker.Ap, striker.Mp) = (1, 4);
        a.PriorityBonus = 50;
        striker.PriorityBonus = -50;
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new AttackTile(1, striker.Id, new Pos(6, 3), "swing", StrikeMode.Stab, South)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(6, 3), b.Pos);
        Assert.True(b.Hp < b.MaxHp);
    }

    [Fact]
    public void A_blow_on_the_pusher_does_not_cancel_its_push()
    {
        var (sim, a, b) = Table();
        a.PriorityBonus = 50;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing", StrikeMode.Stab, East)).Ok);
        Assert.True(sim.Execute(new Push(2, b.Id, West)).Ok);
        Settle(sim);
        Assert.True(b.Hp < b.MaxHp);
        Assert.Equal(new Pos(3, 3), a.Pos);
    }

    [Fact]
    public void An_unattacked_recover_pays_when_the_round_ends()
    {
        var (sim, a, _) = Table();
        a.Fatigue = 6;
        Assert.True(sim.Execute(new Recover(1, a.Id)).Ok);
        Settle(sim);
        Assert.Equal(2, a.Fatigue);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Recovered && e.UnitId == a.Id);
    }

    [Fact]
    public void A_missed_blow_does_not_cancel_a_recover()
    {
        var never = With(r => r with { Combat = r.Combat with { MinHitChance = 0, MaxHitChance = 0 } });
        var (sim, a, b) = Table(never);
        a.Fatigue = 6;
        Assert.True(sim.Execute(new Recover(1, a.Id)).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing", StrikeMode.Stab, West)).Ok);
        Settle(sim);
        Assert.Equal(2, a.Fatigue);
    }

    [Fact]
    public void A_hit_cancels_a_recover_gives_no_stamina_and_does_the_datas_extra_damage()
    {
        double Taken(bool recovering)
        {
            var (sim, a, b) = Table();
            sim.State.RngState = 4242;
            a.Fatigue = 6;
            a.Facing = East;
            if (recovering) Assert.True(sim.Execute(new Recover(1, a.Id)).Ok);
            else Plans.For(sim.State, a);
            a.PriorityBonus = 50;
            Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing", StrikeMode.Stab, West)).Ok);
            Settle(sim);
            if (recovering)
            {
                Assert.Equal(6, a.Fatigue);
                Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentCanceled && e.UnitId == a.Id);
                Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.Recovered);
            }
            return a.MaxHp - a.Hp;
        }
        Assert.True(Taken(true) > Taken(false));
        Assert.True(Real.Rules.Turn.RecoverHitDamage > 0);
    }
}
