using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Every enemy blow is telegraphed, even at a target already in reach: declared as a plan, it comes down at the start of
/// the enemy's next turn, so whoever it is aimed at always gets a turn to step out of it first.
/// </summary>
public class EnemyTelegraphTests
{
    private static ScavengeData Rounds(int n) => With(r => r with
    {
        Turn = r.Turn with { SubRounds = n },
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>
    /// A Husk next to P1's survivor. With <paramref name="huskFirst"/> a far P1 lookout opens the round and the Husk acts
    /// before the survivor (the survivor is up when the call returns); else the survivor is up first and the Husk goes last.
    /// </summary>
    private static (ScavengeSim Sim, Unit U, Unit Husk) Adjacent(bool huskFirst = false, int subRounds = 2)
    {
        var far = SurvivorUnit(1, new(9, 4), weapon: "pipe");
        var u = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var husk = EnemyUnit("husk", new(3, 2));
        husk.Ai.Mode = AiMode.Hunting;
        var sim = Arena(10, 5, huskFirst ? [far, husk, u] : [u, husk, far], data: Rounds(subRounds));
        if (huskFirst) Assert.True(sim.Execute(new EndTurn(1, far.Id, WholeTurn: true)).Ok);
        return (sim, u, husk);
    }

    /// <summary>Ends the player units' turns until the Husk's plan has come down (it may declare the next one at once).</summary>
    private static void UntilResolved(ScavengeSim sim, Unit husk)
    {
        var plan = Assert.IsType<PlannedAction>(husk.Plan);
        for (var i = 0; i < 12 && ReferenceEquals(husk.Plan, plan); i++)
        {
            var up = sim.State.CurrentUnit!;
            Assert.True(sim.Execute(new EndTurn(up.PlayerId!.Value, up.Id, WholeTurn: true)).Ok);
        }
        Assert.False(ReferenceEquals(husk.Plan, plan));
    }

    [Fact]
    public void An_enemy_with_a_target_in_reach_declares_the_blow_instead_of_striking()
    {
        var (sim, u, husk) = Adjacent(huskFirst: true);
        Assert.Equal(u.Id, sim.State.CurrentUnitId);
        var plan = Assert.IsType<PlannedAction>(husk.Plan);
        Assert.Empty(plan.Path);
        Assert.Equal(u.Pos, plan.AttackTile);
        Assert.Empty(sim.State.Strikes);
        Assert.Equal(u.MaxHp, u.Hp);
        Assert.Contains(TacticsView.Telegraphs(sim, u.Side), t => t.UnitId == husk.Id && t.Threatened.Contains(u.Pos));
    }

    [Fact]
    public void The_declared_blow_lands_on_whoever_stays()
    {
        var (sim, u, husk) = Adjacent(huskFirst: true, subRounds: 1);
        UntilResolved(sim, husk);
        Assert.True(u.Hp < u.MaxHp);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Attacked && e.UnitId == husk.Id && e.TargetId == u.Id);
    }

    [Fact]
    public void Stepping_out_of_the_declared_blow_dodges_it()
    {
        var (sim, u, husk) = Adjacent(huskFirst: true, subRounds: 1);
        Assert.True(sim.Execute(new Move(1, u.Id, new(1, 2))).Ok);
        UntilResolved(sim, husk);
        Assert.Equal(u.MaxHp, u.Hp);
        Assert.Equal(husk.Id, u.OutplayedId);
        Assert.True(Combat.Exposed(sim, husk));
    }

    [Fact]
    public void A_target_that_already_spent_its_turn_still_gets_a_turn_before_the_blow_lands()
    {
        // The survivor goes first and is done for the round; the Husk then declares. The blow waits for the Husk's next
        // turn, which comes after the survivor's.
        var (sim, u, husk) = Adjacent(huskFirst: false, subRounds: 2);
        Assert.True(sim.Execute(new EndTurn(1, u.Id, WholeTurn: true)).Ok);
        Assert.Equal(sim.State.TurnOrder[^1], sim.State.CurrentUnitId); // the far lookout, after the Husk declared
        Assert.True(sim.Execute(new EndTurn(1, sim.State.CurrentUnitId!.Value, WholeTurn: true)).Ok);
        Assert.Equal(2, sim.State.Round);
        for (var i = 0; i < 4 && sim.State.CurrentUnitId != u.Id; i++)
            Assert.True(sim.Execute(new EndTurn(1, sim.State.CurrentUnitId!.Value, WholeTurn: true)).Ok);
        Assert.Equal(u.Id, sim.State.CurrentUnitId);
        Assert.NotNull(husk.Plan);
        Assert.Equal(u.MaxHp, u.Hp);
        Assert.True(sim.Execute(new Move(1, u.Id, new(1, 2))).Ok);
        UntilResolved(sim, husk);
        Assert.Equal(u.MaxHp, u.Hp);
        Assert.Equal(husk.Id, u.OutplayedId);
    }
}
