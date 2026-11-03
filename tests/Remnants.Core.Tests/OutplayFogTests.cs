using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>What a side hears of facing, exposure and dodges by an attacker it cannot see: never who or where.</summary>
public class OutplayFogTests
{
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>P1's near-blind survivor at (1,2) and P2's pistol at (6,2), which P1's side cannot see.</summary>
    private static (ScavengeSim Sim, Unit U, Unit Shooter) Fogged()
    {
        var u = SurvivorUnit(1, new(1, 2));
        u.Attributes[AttributeKind.Perception] = 5;
        var shooter = SurvivorUnit(2, new(6, 2), weapon: "pistol");
        var sim = Arena(10, 5, [u, shooter], data: Sure);
        Assert.False(Detection.SideSees(sim.Data, sim.State, u.Side, shooter));
        Assert.True(Detection.SideSees(sim.Data, sim.State, shooter.Side, u));
        return (sim, u, shooter);
    }

    private static void AssertNothingOf(ScavengeSim sim, string side, Unit hidden, int from)
    {
        var heard = FogLog.For(sim, side, sim.State.Events.Skip(from));
        Assert.DoesNotContain(heard, e => e.UnitId == hidden.Id || e.Pos == hidden.Pos);
        Assert.DoesNotContain(heard, e => e.Message.Contains(hidden.Name) || e.Message.Contains("behind") || e.Message.Contains("side")
                                          || e.Message.Contains("exposed") || e.Message.Contains(hidden.Pos.ToString()));
    }

    [Fact]
    public void A_hit_from_behind_on_an_exposed_unit_tells_the_victim_nothing_of_the_shooter()
    {
        var (sim, u, shooter) = Fogged();
        u.Facing = new Pos(-1, 0);
        (u.ExposedUntilRound, u.ExposedToSide) = (sim.State.Round + 1, shooter.Side);
        var from = sim.State.Events.Count;
        Assert.True(Blow(sim, shooter, u, "shoot").Ok);
        Assert.Contains(sim.State.Events.Skip(from), e => e.Type == ScavengeEventType.Attacked && e.Message.Contains("from behind") && e.Message.Contains("exposed"));
        Assert.Contains(FogLog.For(sim, u.Side, sim.State.Events.Skip(from)), e => e.Type == ScavengeEventType.Attacked);
        AssertNothingOf(sim, u.Side, shooter, from);
    }

    [Fact]
    public void Dodging_a_strike_from_the_fog_gives_the_attacker_away_to_nobody()
    {
        var (sim, u, shooter) = Fogged();
        var from = sim.State.Events.Count;
        Assert.True(TileStrikes.Declare(sim, shooter, new AttackTile(2, shooter.Id, u.Pos, "shoot")).Ok);
        u.Pos = new Pos(1, 3);
        TileStrikes.ResolveAll(sim);
        Assert.Equal(shooter.Id, u.OutplayedId);
        Assert.Contains(sim.State.Events.Skip(from), e => e.Message.Contains("exposed"));
        AssertNothingOf(sim, u.Side, shooter, from);
    }
}
