using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Enemies move live on their sub-turn, then declare a telegraphed tile strike that pays its AP at once and comes down at
/// the sub-round's end. It can be stepped out of for a free counter, and an interrupt can take it away.
/// </summary>
public class TelegraphedStrikeTests
{
    private static ScavengeData SureHits(int subRounds = 1) => With(r => r with
    {
        Turn = r.Turn with { SubRounds = subRounds },
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>P1's far survivor first, then a Tally blade (machete: slash only) next to P1's near survivor, who goes last.</summary>
    private static (ScavengeSim Sim, Unit Far, Unit Blade, Unit Near) Ambush(int subRounds = 1)
    {
        var far = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var blade = EnemyUnit("tally_blade", new(5, 2));
        var near = SurvivorUnit(1, new(6, 2), weapon: "pipe");
        var sim = Arena(12, 7, [far, blade, near], data: SureHits(subRounds));
        blade.Ai.Mode = AiMode.Hunting;
        return (sim, far, blade, near);
    }

    [Fact]
    public void AC1_an_enemy_declares_a_patterned_tile_strike_that_pays_ap_at_once_and_resolves_at_sub_round_end()
    {
        var (sim, far, blade, near) = Ambush();
        var ap = blade.Ap;

        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);

        var strike = Assert.Single(sim.State.Strikes);
        var cost = Combat.ApCost(sim.Profile(blade).Action(strike.ActionId)!, blade.Pos, strike.Tile);
        Assert.Equal(blade.Id, strike.UnitId);
        Assert.Equal(StrikeMode.Slash, strike.Mode);
        Assert.True(strike.Tiles.Count > 1);
        Assert.Contains(near.Pos, strike.Tiles);
        Assert.Equal(ap - cost, blade.Ap);
        Assert.Equal(near.MaxHp, near.Hp);

        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);

        Assert.DoesNotContain(sim.State.Strikes, s => s.UnitId == blade.Id && s.Tiles.Contains(near.Pos) && near.Hp == near.MaxHp);
        Assert.True(near.Hp < near.MaxHp);
    }

    [Fact]
    public void AC1_an_enemy_moves_live_on_its_sub_turn_then_declares_from_where_it_stopped()
    {
        var far = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var blade = EnemyUnit("tally_blade", new(4, 3));
        var near = SurvivorUnit(1, new(6, 3), weapon: "pipe");
        var sim = Arena(12, 7, [far, blade, near], data: SureHits());
        blade.Ai.Mode = AiMode.Hunting;
        var from = blade.Pos;

        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);

        Assert.NotEqual(from, blade.Pos);
        var strike = Assert.Single(sim.State.Strikes);
        Assert.Equal(blade.Pos, strike.From);
        Assert.Contains(near.Pos, strike.Tiles);
    }

    [Fact]
    public void AC2_a_target_that_leaves_the_covered_tiles_is_missed_and_earns_a_free_counter()
    {
        var (sim, far, blade, near) = Ambush();
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);
        var covered = Assert.Single(sim.State.Strikes).Tiles;
        var clear = sim.Reachable(near).Keys.First(p => !covered.Contains(p) && p.Chebyshev(blade.Pos) <= 1);

        Assert.True(sim.Execute(new Move(1, near.Id, clear)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);

        Assert.Equal(near.MaxHp, near.Hp);
        Assert.Equal(blade.Id, near.FreeCounterId);
    }

    [Fact]
    public void AC3_an_enemy_downed_before_sub_round_end_loses_its_strike()
    {
        var (sim, far, blade, near) = Ambush();
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);
        Assert.Single(sim.State.Strikes);

        Combat.ApplyDamage(sim, blade, Survivors.BodyPart.Torso, blade.Hp + 1, attacker: near);

        Assert.Empty(sim.State.Strikes);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentCanceled && e.UnitId == blade.Id);
    }

    [Fact]
    public void AC3_an_enemy_pinned_before_sub_round_end_loses_its_strike()
    {
        var (sim, far, blade, near) = Ambush();
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);

        Combat.Suppress(sim, near, sim.Profile(near).Actions[0], blade);

        Assert.Empty(sim.State.Strikes);
    }

    [Fact]
    public void AC3_an_enemy_stunned_by_a_hit_before_sub_round_end_loses_its_strike()
    {
        var far = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var linked = EnemyUnit("linked_drone", new(5, 2));
        var near = SurvivorUnit(1, new(6, 2), attributes: 90, weapon: "pipe+mod_shock_coil");
        near.Skills[Survivors.Skill.Melee] = 100;
        var sim = Arena(12, 7, [far, linked, near]);
        linked.Ai.Mode = AiMode.Hunting;
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);
        Assert.Contains(sim.State.Strikes, s => s.UnitId == linked.Id);
        linked.Hp = linked.MaxHp = 5000;

        var swing = sim.Profile(near).Action("swing")!;
        for (var i = 0; i < 20 && !linked.Suppressed; i++) Combat.Hit(sim, near, swing, [linked]);

        Assert.True(linked.Suppressed);
        Assert.DoesNotContain(sim.State.Strikes, s => s.UnitId == linked.Id);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentCanceled && e.UnitId == linked.Id);
    }

    [Fact]
    public void AC3_an_enemy_stunned_by_an_emp_before_sub_round_end_loses_its_strike()
    {
        var far = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var linked = EnemyUnit("linked", new(5, 2));
        var near = SurvivorUnit(1, new(6, 2), weapon: "pipe", pack: "emp_grenade");
        var sim = Arena(12, 7, [far, linked, near], data: SureHits());
        linked.Ai.Mode = AiMode.Hunting;
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);
        Assert.Contains(sim.State.Strikes, s => s.UnitId == linked.Id);

        Assert.True(sim.Execute(new UseItem(1, near.Id, "emp_grenade", At: linked.Pos)).Ok);

        Assert.DoesNotContain(sim.State.Strikes, s => s.UnitId == linked.Id);
    }

    [Fact]
    public void AC3_an_enemy_displaced_before_sub_round_end_loses_its_strike_and_is_exposed()
    {
        var (sim, far, blade, near) = Ambush();
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);

        CardAbilities.Displace(sim, blade, blade.Pos + new Pos(-1, 0), near);

        Assert.Empty(sim.State.Strikes);
        Assert.Equal(near.Side, blade.ExposedToSide);
    }

    [Fact]
    public void A_plain_hit_does_not_cancel_a_declared_strike()
    {
        var (sim, far, blade, near) = Ambush();
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);
        blade.Hp = blade.MaxHp = 500;

        Combat.Hit(sim, near, sim.Profile(near).Actions[0], [blade]);

        Assert.True(blade.Hp < blade.MaxHp);
        Assert.Contains(sim.State.Strikes, s => s.UnitId == blade.Id);
    }

    [Fact]
    public void An_enemy_strike_never_hits_a_unit_of_its_own_side_shoved_into_it()
    {
        var far = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var blade = EnemyUnit("tally_blade", new(5, 2));
        var mate = EnemyUnit("tally_blade", new(9, 5));
        var near = SurvivorUnit(1, new(6, 2), weapon: "pipe");
        var sim = Arena(12, 7, [far, blade, mate, near], data: SureHits());
        blade.Ai.Mode = AiMode.Hunting;
        mate.Ai.Mode = AiMode.Idle;
        Assert.True(sim.Execute(new EndTurn(1, far.Id)).Ok);
        var covered = sim.State.Strikes.First(s => s.UnitId == blade.Id).Tiles;
        var hp = mate.Hp;
        mate.Pos = covered.First(p => sim.State.UnitAt(p) is null);
        mate.Displaced = true;

        TileStrikes.ResolveAll(sim);

        Assert.Equal(hp, mate.Hp);
    }

    [Fact]
    public void A_players_strike_still_hits_an_ally_shoved_into_it()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var b = SurvivorUnit(1, new(3, 2), weapon: "pipe");
        var husk = EnemyUnit("husk", new(10, 6));
        var sim = Arena(12, 7, [a, b, husk], data: SureHits());
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, sim.Profile(a).Actions[0].Id)).Ok);
        b.Displaced = true; // shoved after b's sub-turn began

        TileStrikes.ResolveAll(sim);

        Assert.True(b.Hp < b.MaxHp);
    }
}
