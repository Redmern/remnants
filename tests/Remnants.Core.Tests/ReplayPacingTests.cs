using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>How the scavenge replay paces events: what the side saw plays out, the rest (and everything, skipping) is instant.</summary>
public class ReplayPacingTests
{
    private static (ScavengeSim Sim, Unit Mine, Unit Enemy) Setup()
    {
        var mine = SurvivorUnit(1, new(1, 1));
        var enemy = EnemyUnit("husk", new(8, 1));
        return (Arena(10, 3, [mine, enemy]), mine, enemy);
    }

    private static HashSet<int> Seeing(ScavengeSim sim, params Pos[] tiles) => tiles.Select(sim.State.Map.Index).ToHashSet();

    [Fact]
    public void An_enemy_move_entirely_out_of_sight_is_not_seen()
    {
        var (sim, mine, enemy) = Setup();
        var moved = new ScavengeEvent(1, ScavengeEventType.Moved, "moved", enemy.Id, Pos: new Pos(7, 1));
        Assert.False(ReplayPacing.InSight(sim.State, mine.Side, moved, Seeing(sim, mine.Pos), actorAt: enemy.Pos, targetAt: null));
        Assert.True(ReplayPacing.InSight(sim.State, mine.Side, moved, Seeing(sim, mine.Pos, new Pos(7, 1)), enemy.Pos, null), "walks into sight");
        Assert.True(ReplayPacing.InSight(sim.State, mine.Side, moved, Seeing(sim, mine.Pos, enemy.Pos), enemy.Pos, null), "walks out of sight");
    }

    [Fact]
    public void What_is_done_by_or_to_the_sides_own_units_is_always_seen()
    {
        var (sim, mine, enemy) = Setup();
        var hit = new ScavengeEvent(1, ScavengeEventType.Attacked, "hit", enemy.Id, mine.Id, mine.Pos);
        Assert.True(ReplayPacing.InSight(sim.State, mine.Side, hit, Seeing(sim), enemy.Pos, mine.Pos));
        var own = new ScavengeEvent(1, ScavengeEventType.Moved, "moved", mine.Id, Pos: new Pos(2, 1));
        Assert.True(ReplayPacing.InSight(sim.State, mine.Side, own, Seeing(sim), mine.Pos, null));
        Assert.True(ReplayPacing.InSight(sim.State, mine.Side, new ScavengeEvent(1, ScavengeEventType.RoundStarted, "Round 2."), Seeing(sim), null, null));
    }

    [Fact]
    public void Unseen_steps_and_every_step_when_skipping_hold_input_for_nothing()
    {
        Assert.Equal(0, ReplayPacing.Hold(0.6, seen: false, skip: false));
        Assert.Equal(0, ReplayPacing.Hold(0.6, seen: true, skip: true));
        Assert.Equal(0.6, ReplayPacing.Hold(0.6, seen: true, skip: false), 9);
        Assert.Equal(0, ReplayPacing.WalkSeconds(0.2, tiles: 6, run: true, skip: true));
    }

    [Fact]
    public void A_walk_takes_its_tiles_at_the_set_pace_and_long_ones_are_capped()
    {
        Assert.Equal(0.4, ReplayPacing.WalkSeconds(0.2, tiles: 2, run: false, skip: false), 9);
        Assert.Equal(0.2 * ReplayPacing.RunPace * 3, ReplayPacing.WalkSeconds(0.2, tiles: 3, run: true, skip: false), 9);
        Assert.Equal(ReplayPacing.MaxWalkSeconds, ReplayPacing.WalkSeconds(0.25, tiles: 40, run: false, skip: false), 9);
        Assert.True(ReplayPacing.MaxWalkSeconds / 40 <= 0.25);
    }
}
