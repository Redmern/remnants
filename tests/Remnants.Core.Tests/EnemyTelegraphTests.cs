using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Every enemy blow is telegraphed, even at a target already in reach: readied as a tile strike on the enemy's sub-turn, it
/// comes down when the sub-round ends, so whoever it is aimed at and still to act can step out of it first.
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

    /// <summary>Ends the player units' turns until the Husk's readied strike has come down (it may ready the next one at once).</summary>
    private static void UntilResolved(ScavengeSim sim, Unit husk)
    {
        var strike = Assert.Single(sim.State.Strikes, s => s.UnitId == husk.Id);
        for (var i = 0; i < 12 && sim.State.Strikes.Contains(strike); i++)
        {
            var up = sim.State.CurrentUnit!;
            Assert.True(sim.Execute(new EndTurn(up.PlayerId!.Value, up.Id, WholeTurn: true)).Ok);
        }
        Assert.DoesNotContain(strike, sim.State.Strikes);
    }

    [Fact]
    public void An_enemy_with_a_target_in_reach_declares_the_blow_instead_of_striking()
    {
        var (sim, u, husk) = Adjacent(huskFirst: true);
        Assert.Equal(u.Id, sim.State.CurrentUnitId);
        Assert.Equal(u.Pos, Assert.Single(sim.State.Strikes, s => s.UnitId == husk.Id).Tile);
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
    public void An_enemy_in_reach_strikes_in_both_sub_rounds_each_blow_telegraphed()
    {
        // The survivor goes first: the Husk readies a blow in each sub-round, and each comes down as that sub-round ends.
        var u = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var husk = EnemyUnit("husk", new(3, 2));
        husk.Ai.Mode = AiMode.Hunting;
        var sim = Arena(10, 5, [u, husk], data: Rounds(2));
        int Blows(ScavengeEventType type) => sim.State.Events.Count(e => e.Round == 1 && e.Type == type && e.UnitId == husk.Id);
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.Equal((1, 2), (sim.State.Round, sim.State.SubRound));
        Assert.Equal((1, 1), (Blows(ScavengeEventType.IntentDeclared), Blows(ScavengeEventType.Attacked)));
        Assert.True(u.Hp < u.MaxHp);
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.Equal((2, 2), (Blows(ScavengeEventType.IntentDeclared), Blows(ScavengeEventType.Attacked)));
    }
}
