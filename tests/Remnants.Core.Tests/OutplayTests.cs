using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Dodging a telegraphed blow (its tile vacated): the attacker is left exposed (the next hit on it lands harder) and the
/// dodger may strike back once for free.
/// </summary>
public class OutplayTests
{
    /// <summary>Every attack lands as a plain hit, so outcomes do not hang on the seed.</summary>
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>P1's knife at (2,2) stabs at P2 on (3,2); P2 steps off to (4,3) and the stab comes down on nobody.</summary>
    private static (ScavengeSim Sim, Unit A, Unit B) Dodged()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "knife");
        var b = SurvivorUnit(2, new(3, 2), weapon: "knife");
        var sim = Arena(10, 5, [a, b], data: Sure);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(3, 2), "cut", StrikeMode.Stab)).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(4, 3))).Ok, "step off");
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(a.Id, b.OutplayedId);
        return (sim, a, b);
    }

    [Fact]
    public void The_values_come_from_the_data()
    {
        var outplay = Real.Rules.Combat.Outplay;
        Assert.True(outplay.ExposedDamage > 1);
        Assert.True(outplay.FreeCounter);
    }

    [Fact]
    public void A_dodged_tile_strike_leaves_the_attacker_exposed_through_the_next_round()
    {
        var (sim, a, _) = Dodged();
        Assert.Equal(2, sim.State.Round);
        Assert.True(Combat.Exposed(sim, a));
        Assert.Equal(2, a.ExposedUntilRound); // dodged at the end of round 1: good through round 2
    }

    [Fact]
    public void A_dodged_plan_leaves_the_attacker_exposed()
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 1), weapon: "pipe");
        var sim = Arena(10, 3, [a, b], data: Sure);
        Assert.True(sim.Execute(new DeclareIntent(1, a.Id, [new(2, 1), new(4, 1)], new Pos(5, 1), "swing")).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(6, 1))).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(a.Id, b.OutplayedId);
        Assert.True(Combat.Exposed(sim, a));
    }

    [Fact]
    public void A_strike_that_lands_exposes_nobody()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "knife");
        var b = SurvivorUnit(2, new(3, 2), weapon: "knife");
        var sim = Arena(10, 5, [a, b], data: Sure);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(3, 2), "cut", StrikeMode.Stab)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.True(b.Hp < b.MaxHp);
        Assert.False(Combat.Exposed(sim, a));
    }

    /// <summary>The HP a knife cut from (4,2) takes off a target at (3,2), exposed or not, on the same seed.</summary>
    private static (double First, double Second) Cuts(bool exposed)
    {
        var a = SurvivorUnit(1, new(3, 2));
        var b = SurvivorUnit(2, new(4, 2), weapon: "knife");
        var sim = Arena(10, 5, [a, b], data: Sure, seed: 3);
        if (exposed) a.ExposedUntilRound = sim.State.Round + 1;
        Assert.True(Blow(sim, b, a, "cut").Ok);
        b.Ap = sim.Profile(b).MaxAp;
        Assert.True(Blow(sim, b, a, "cut").Ok);
        var hits = sim.State.Events.Where(e => e.Type == ScavengeEventType.Attacked).Select(e => e.Amount!.Value).ToList();
        return (hits[0], hits[1]);
    }

    [Fact]
    public void The_next_hit_on_an_exposed_unit_deals_the_bonus_once()
    {
        var plain = Cuts(exposed: false);
        var exposed = Cuts(exposed: true);
        Assert.Equal(plain.First * Sure.Rules.Combat.Outplay.ExposedDamage, exposed.First, 6);
        Assert.Equal(plain.Second, exposed.Second, 6);
    }

    [Fact]
    public void The_dodger_strikes_back_once_for_free()
    {
        var (sim, a, b) = Dodged();
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);   // round 2: a holds, b is up with full AP
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
        Assert.True(sim.Execute(new Move(2, b.Id, new(3, 3))).Ok, "back in reach");
        var ap = b.Ap;
        Assert.True(TileStrikes.FreeCounter(sim, b, [a.Pos]));
        Assert.True(sim.Execute(StrikeOn(sim, b, a, "cut")).Ok);
        Assert.Equal(ap, b.Ap);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Countered && e.UnitId == b.Id);

        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);   // round 3: the counter is spent
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
        Assert.False(TileStrikes.FreeCounter(sim, b, [a.Pos]));
        ap = b.Ap;
        Assert.True(sim.Execute(StrikeOn(sim, b, a, "cut")).Ok);
        Assert.Equal(ap - sim.Profile(b).Action("cut")!.ApCost, b.Ap);
    }

    [Fact]
    public void The_free_counter_is_only_against_the_attacker_it_dodged()
    {
        var (sim, a, b) = Dodged();
        Assert.False(TileStrikes.FreeCounter(sim, b, [new Pos(5, 3)]));
        Assert.True(TileStrikes.FreeCounter(sim, b, [new Pos(5, 3), a.Pos]));
    }
}
