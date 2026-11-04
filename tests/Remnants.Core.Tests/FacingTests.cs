using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Facing in combat: a blow from the side or from behind lands harder than one the target sees coming.</summary>
public class FacingTests
{
    /// <summary>Every attack lands as a plain hit, so only the damage roll (the same seed each time) is left.</summary>
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>The HP a pipe swing from <paramref name="from"/> takes off a target at (3,2) facing <paramref name="facing"/>.</summary>
    private static double Damage(Pos from, Pos? facing)
    {
        var a = SurvivorUnit(1, from, weapon: "pipe");
        var b = SurvivorUnit(2, new(3, 2));
        var sim = Arena(8, 5, [a, b], data: Sure, seed: 7);
        b.Facing = facing;
        Assert.True(Blow(sim, a, b, "swing").Ok);
        return Assert.Single(sim.State.Events, e => e.Type == ScavengeEventType.Attacked).Amount!.Value;
    }

    [Theory]
    [InlineData(2, 2, FacingArc.Front)]  // straight ahead
    [InlineData(2, 1, FacingArc.Front)]  // ahead, a step off
    [InlineData(3, 1, FacingArc.Side)]
    [InlineData(3, 3, FacingArc.Side)]
    [InlineData(4, 1, FacingArc.Rear)]
    [InlineData(4, 2, FacingArc.Rear)]
    public void The_arc_is_where_the_attacker_stands_against_where_the_target_faces(int x, int y, FacingArc arc) =>
        Assert.Equal(arc, Combat.ArcOf(new Pos(3, 2), new Pos(-1, 0), new Pos(x, y)));

    [Theory]
    [InlineData(1, 6, FacingArc.Side)]   // far off the flank, a step ahead: still the side
    [InlineData(-1, 6, FacingArc.Side)]  // far off the flank, a step behind
    [InlineData(6, 1, FacingArc.Front)]
    [InlineData(-6, -1, FacingArc.Rear)]
    [InlineData(4, 4, FacingArc.Front)]  // the diagonal ahead
    [InlineData(-4, 4, FacingArc.Rear)]  // the diagonal behind
    public void The_arc_of_a_distant_attacker_follows_its_true_bearing(int x, int y, FacingArc arc) =>
        Assert.Equal(arc, Combat.ArcOf(new Pos(0, 0), new Pos(1, 0), new Pos(x, y)));

    [Fact]
    public void A_target_facing_nowhere_is_struck_from_the_front() =>
        Assert.Equal(FacingArc.Front, Combat.ArcOf(new Pos(3, 2), null, new Pos(4, 2)));

    [Fact]
    public void The_bonuses_come_from_the_data_and_the_rear_is_worse_than_the_side()
    {
        var facing = Real.Rules.Combat.Facing;
        Assert.True(facing.SideDamage > 1);
        Assert.True(facing.RearDamage > facing.SideDamage);
    }

    [Fact]
    public void A_blow_from_the_side_or_behind_deals_the_datas_bonus()
    {
        var facing = Sure.Rules.Combat.Facing;
        var front = Damage(new(2, 2), new Pos(-1, 0));
        Assert.Equal(front * facing.SideDamage, Damage(new(2, 2), new Pos(0, 1)), 6);
        Assert.Equal(front * facing.RearDamage, Damage(new(2, 2), new Pos(1, 0)), 6);
        Assert.Equal(front, Damage(new(2, 2), null), 6);
    }

    [Fact]
    public void The_log_says_where_the_blow_came_from()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(3, 2));
        var sim = Arena(8, 5, [a, b], data: Sure);
        b.Facing = new Pos(1, 0);
        Assert.True(Blow(sim, a, b, "swing").Ok);
        Assert.Contains("from behind", Assert.Single(sim.State.Events, e => e.Type == ScavengeEventType.Attacked).Message);
    }

    [Fact]
    public void A_unit_faces_the_way_it_moves()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(7, 2));
        var sim = Arena(8, 5, [a, b], data: Sure);
        Assert.True(Now(sim, new Move(1, a.Id, new(2, 3))).Ok);
        Assert.Equal(new Pos(0, 1), a.Facing);
    }

    [Fact]
    public void A_unit_faces_the_tile_it_strikes_at()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(3, 1));
        var sim = Arena(8, 5, [a, b], data: Sure);
        Assert.True(StrikeNow(sim, a, b, "swing").Ok);
        Assert.Equal(new Pos(1, -1), a.Facing);
    }

    [Fact]
    public void A_strike_turns_the_unit_toward_its_attack_even_at_an_empty_tile()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 2));
        var sim = Arena(8, 5, [a, b], data: Sure);
        a.Facing = new Pos(-1, 0);
        Assert.True(Now(sim, new AttackTile(1, a.Id, new(3, 3), "swing")).Ok);
        Assert.Equal(new Pos(1, 1), a.Facing);

        b.Facing = new Pos(1, 0);
        Assert.True(Now(sim, new AttackTile(2, b.Id, new(4, 2), "punch")).Ok, "nobody there: still a strike");
        Assert.Equal(new Pos(-1, 0), b.Facing);
    }
}
