using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>A round is N sub-rounds: every unit acts in each, on one turn's AP, and tile attacks come down at each one's end.</summary>
public class SubRoundTests
{
    private static ScavengeData Rounds(int n) => With(r => r with
    {
        Turn = r.Turn with { SubRounds = n },
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    private static (ScavengeSim Sim, Unit A, Unit B) Duel(int subRounds = 2, string weapon = "sword")
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: weapon);
        var b = SurvivorUnit(2, new(6, 2), weapon: "pipe");
        return (Arena(10, 5, [a, b], data: Rounds(subRounds)), a, b);
    }

    [Fact]
    public void The_sub_round_count_comes_from_the_data()
    {
        Assert.True(Real.Rules.Turn.SubRounds >= 1);
        Assert.Equal(3, Rounds(3).Rules.Turn.SubRounds);
    }

    [Fact]
    public void Ap_carries_across_the_sub_rounds_and_refills_with_the_round()
    {
        var (sim, a, b) = Duel();
        var max = sim.Profile(a).MaxAp;
        Assert.True(sim.Execute(new Lookout(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);

        Assert.Equal((1, 2), (sim.State.Round, sim.State.SubRound));
        Assert.Equal(a.Id, sim.State.CurrentUnitId);
        Assert.Equal(max - 1, a.Ap);

        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal((2, 1), (sim.State.Round, sim.State.SubRound));
        Assert.Equal(max, a.Ap);
    }

    [Fact]
    public void A_strike_comes_down_at_the_end_of_its_own_sub_round()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(3, 2), "blade", StrikeMode.Stab)).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(4, 2))).Ok, "b walks into the sword");
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);

        Assert.Equal((1, 2), (sim.State.Round, sim.State.SubRound));
        Assert.True(b.Hp < b.MaxHp);
        Assert.Empty(sim.State.Strikes);
    }

    [Fact]
    public void Strikes_come_down_in_initiative_order()
    {
        var a1 = SurvivorUnit(1, new(2, 2), weapon: "sword");
        var a2 = SurvivorUnit(1, new(4, 4), weapon: "sword");
        var b = SurvivorUnit(2, new(4, 2), weapon: "pipe");
        b.Hp = 1;
        var sim = Arena(10, 6, [a1, a2, b], data: Rounds(2));
        Assert.True(sim.Execute(new AttackTile(1, a1.Id, new(4, 2), "blade", StrikeMode.Stab)).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a2.Id, new(4, 2), "blade", StrikeMode.Stab)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);

        Assert.False(b.IsActive);
        var hits = sim.State.Events.Where(e => e.Type == ScavengeEventType.Attacked && e.TargetId == b.Id).ToList();
        Assert.Equal(a1.Id, Assert.Single(hits).UnitId);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == a2.Id);
    }

    [Fact]
    public void A_unit_out_of_ap_or_done_for_the_round_sits_out_the_later_sub_rounds()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "sword");
        var c = SurvivorUnit(1, new(2, 3), weapon: "sword");
        var b = SurvivorUnit(2, new(6, 2), weapon: "pipe");
        var sim = Arena(10, 5, [a, c, b], data: Rounds(3));
        (a.Ap, a.Mp) = (1, 0);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(3, 2), "blade", StrikeMode.Stab)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, c.Id, WholeTurn: true)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);

        Assert.Equal((1, 2), (sim.State.Round, sim.State.SubRound));
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void One_sub_round_plays_as_whole_turns()
    {
        var (sim, a, b) = Duel(subRounds: 1);
        Assert.True(sim.Execute(new Move(1, a.Id, new(3, 2))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal((2, 1), (sim.State.Round, sim.State.SubRound));
        Assert.Equal(sim.Profile(a).MaxAp, a.Ap);
    }

    [Fact]
    public void A_pending_strike_and_the_sub_round_survive_save_and_load_and_older_runs_start_at_sub_round_one()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(3, 2), "blade", StrikeMode.Stab)).Ok);
        var loaded = ScavengeSave.Load(Rounds(2), ScavengeSave.Save(sim.State));
        var strike = Assert.Single(loaded.State.Strikes);
        Assert.Equal((a.Id, StrikeMode.Stab, new Pos(1, 0)), (strike.UnitId, strike.Mode, strike.Direction));

        var old = ScavengeSave.Save(Duel().Sim.State).Replace($"\"version\": {ScavengeSave.Version}", $"\"version\": {ScavengeSave.IntentsVersion}")
            .Replace("\"subRound\": 1,", "").Replace("\"strikes\": [],", "");
        var run = ScavengeSave.Load(Rounds(2), old);
        Assert.Equal(1, run.State.SubRound);
        Assert.Empty(run.State.Strikes);
    }
}
