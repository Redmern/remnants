using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>The AI strikes tiles with patterned weapons, and runs on sub-rounds stay deterministic.</summary>
public class TileAttackAiTests
{
    private static ScavengeData Rounds(int n) => With(r => r with
    {
        Turn = r.Turn with { SubRounds = n },
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>P1's far survivor first, then a Tally blade (machete: slash only) next to P1's near survivor, who goes last.</summary>
    private static (ScavengeSim Sim, Unit Far, Unit Blade, Unit Near) Ambush(int subRounds = 1)
    {
        var far = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var blade = EnemyUnit("tally_blade", new(5, 2));
        var near = SurvivorUnit(1, new(6, 2), weapon: "pipe");
        var sim = Arena(10, 5, [far, blade, near], data: Rounds(subRounds));
        blade.Ai.Mode = AiMode.Hunting;
        return (sim, far, blade, near);
    }

    [Fact]
    public void An_enemy_with_a_patterned_weapon_readies_a_tile_strike_instead_of_hitting_at_once()
    {
        var (sim, far, blade, near) = Ambush();
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);

        Assert.Equal(near.Id, sim.State.CurrentUnitId);
        Assert.Equal(near.MaxHp, near.Hp);
        var strike = Assert.Single(sim.State.Strikes);
        Assert.Equal(blade.Id, strike.UnitId);
        Assert.Equal(Items.StrikeMode.Slash, strike.Mode);
        Assert.Contains(near.Pos, strike.Tiles);
    }

    [Fact]
    public void The_enemy_strike_lands_on_whoever_stays_and_misses_who_steps_away()
    {
        var (sim, far, _, near) = Ambush();
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);
        Assert.True(near.Hp < near.MaxHp);

        var (sim2, far2, blade2, near2) = Ambush();
        Assert.True(sim2.Execute(new EndTurn(1, far2.Id)).Ok);
        var covered = sim2.State.Strikes[0].Tiles;
        var away = sim2.Reachable(near2).Keys.Where(p => !covered.Contains(p) && p.Chebyshev(blade2.Pos) > 1).OrderBy(p => p.X).ThenBy(p => p.Y).First();
        Assert.True(sim2.Execute(new Move(1, near2.Id, away)).Ok);
        Assert.True(sim2.Execute(new EndTurn(1, near2.Id)).Ok);
        Assert.Equal(near2.MaxHp, near2.Hp);
        Assert.Contains(sim2.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == blade2.Id);
    }

    [Fact]
    public void An_enemy_readies_one_strike_a_sub_round_and_another_in_the_next_with_ap_left()
    {
        var (sim, far, blade, near) = Ambush(subRounds: 2);
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);
        Assert.Single(sim.State.Strikes);
        var left = blade.Ap;
        Assert.True(left > 0);
        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);
        Assert.Equal(2, sim.State.SubRound);
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);
        Assert.Equal(blade.Id, Assert.Single(sim.State.Strikes).UnitId);
        Assert.True(blade.Ap < left);
    }

    [Fact]
    public void Same_seed_and_commands_give_the_same_game_with_sub_rounds_and_tile_strikes()
    {
        string Play()
        {
            var a = SurvivorUnit(1, new(1, 2), weapon: "spear");
            var units = new List<Unit> { a, EnemyUnit("tally_blade", new(7, 2)), EnemyUnit("husk", new(8, 4)), EnemyUnit("tally_blade", new(8, 0)) };
            var sim = Arena(10, 5, units, data: With(r => r with { Turn = r.Turn with { SubRounds = 2 } }), seed: 7);
            foreach (var u in units.Skip(1)) u.Ai.Mode = AiMode.Hunting;
            for (var i = 0; i < 20 && !sim.State.Finished && a.IsActive; i++)
            {
                var r = sim.Execute(new AttackTile(1, a.Id, new(2, 2), "thrust"));
                if (!r.Ok) Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
            }
            return string.Join("\n", sim.State.Events.Select(e => e.Message));
        }
        var first = Play();
        Assert.Contains("readies", first);
        Assert.Equal(first, Play());
    }
}
