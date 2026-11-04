using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>The AI telegraphs blows with patterned weapons, and runs on sub-rounds with tile strikes stay deterministic.</summary>
public class TileAttackAiTests
{
    private static readonly ScavengeData SureHits = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>P1's far survivor first, then a Tally blade (machete: slash only) next to P1's near survivor, who goes last.</summary>
    private static (ScavengeSim Sim, Unit Far, Unit Blade, Unit Near) Ambush()
    {
        var far = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var blade = EnemyUnit("tally_blade", new(5, 2));
        var near = SurvivorUnit(1, new(6, 2), weapon: "pipe");
        var sim = Arena(10, 5, [far, blade, near], data: SureHits);
        blade.Ai.Mode = AiMode.Hunting;
        return (sim, far, blade, near);
    }

    /// <summary>What the AI has planned for the enemy's blow to cover.</summary>
    private static List<Pos> Threatened(ScavengeSim sim, Unit enemy) => Strikes(sim).First(s => s.UnitId == enemy.Id).Tiles;

    /// <summary>The planned strike comes down.</summary>
    private static void UntilResolved(ScavengeSim sim, Unit enemy)
    {
        Strikes(sim).First(s => s.UnitId == enemy.Id);
        Settle(sim);
    }

    [Fact]
    public void An_enemy_with_a_patterned_weapon_telegraphs_a_slash_in_reach()
    {
        var (sim, far, blade, near) = Ambush();
        sim.Start();
        Assert.Equal(near.MaxHp, near.Hp);
        Assert.Equal(Items.StrikeMode.Slash, Strikes(sim).First(s => s.UnitId == blade.Id).Mode);
        Assert.Contains(near.Pos, Threatened(sim, blade));
    }

    [Fact]
    public void An_enemy_with_a_plain_attack_telegraphs_the_one_tile()
    {
        var far = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var husk = EnemyUnit("turret", new(5, 2));
        var near = SurvivorUnit(1, new(6, 2), weapon: "pipe");
        var sim = Arena(10, 5, [far, husk, near], data: SureHits);
        husk.Ai.Mode = AiMode.Hunting;
        sim.Start();

        Assert.Equal(near.MaxHp, near.Hp);
        Assert.Null(Strikes(sim).First(s => s.UnitId == husk.Id).Mode);
        Assert.Equal([near.Pos], Threatened(sim, husk));
        UntilResolved(sim, husk);
        Assert.True(near.Hp < near.MaxHp);
    }

    [Fact]
    public void The_enemy_slash_lands_on_whoever_stays_and_misses_who_steps_out_of_its_arc()
    {
        var (sim, far, blade, near) = Ambush();
        sim.Start();
        UntilResolved(sim, blade);
        Assert.True(near.Hp < near.MaxHp);

        var (sim2, far2, blade2, near2) = Ambush();
        sim2.Start();
        var covered = Threatened(sim2, blade2);
        var away = sim2.Reachable(near2).Keys.Where(p => !covered.Contains(p) && p.Chebyshev(blade2.Pos) > 1).OrderBy(p => p.X).ThenBy(p => p.Y).First();
        Assert.True(sim2.Execute(new Move(1, near2.Id, away)).Ok);
        UntilResolved(sim2, blade2);
        Assert.Equal(near2.MaxHp, near2.Hp);
        Assert.Contains(sim2.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == blade2.Id);
    }

    [Fact]
    public void Same_seed_and_commands_give_the_same_game_with_sub_rounds_and_tile_strikes()
    {
        string Play()
        {
            var a = SurvivorUnit(1, new(1, 2), weapon: "spear");
            var units = new List<Unit> { a, EnemyUnit("tally_blade", new(7, 2)), EnemyUnit("husk", new(8, 4)), EnemyUnit("tally_blade", new(8, 0)) };
            var sim = Arena(10, 5, units, seed: 7);
            foreach (var u in units.Skip(1)) u.Ai.Mode = AiMode.Hunting;
            for (var i = 0; i < 20 && !sim.State.Finished && a.IsActive; i++)
            {
                var r = sim.Execute(new AttackTile(1, a.Id, new(2, 2), "thrust"));
                if (!r.Ok) Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
            }
            return string.Join("\n", sim.State.Events.Select(e => e.Message));
        }
        var first = Play();
        Assert.Contains("comes down", first);
        Assert.Equal(first, Play());
    }
}
