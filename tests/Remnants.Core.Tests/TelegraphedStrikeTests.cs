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

    /// <summary>A seen blade next to P1's survivor, with a walled-off room the side cannot see into.</summary>
    private static (ScavengeSim Sim, Unit Blade, Unit Near, Pos Hidden) WalledOff()
    {
        var near = SurvivorUnit(1, new(2, 1), weapon: "pipe");
        var blade = EnemyUnit("tally_blade", new(3, 1));
        var sim = Arena(9, 3, [near, blade], rows: ["....#....", "....#....", "....#...."], data: SureHits());
        var hidden = new Pos(7, 1);
        Assert.DoesNotContain(sim.State.Map.Index(hidden), Visibility.SideVisible(sim.Data, sim.State, near.Side));
        return (sim, blade, near, hidden);
    }

    [Fact]
    public void AC4_enemy_strike_markers_appear_only_on_tiles_the_side_can_see()
    {
        var (sim, blade, near, hidden) = WalledOff();
        var seenTile = near.Pos + new Pos(0, 1);
        sim.State.Strikes.Add(new TileStrike
        {
            UnitId = blade.Id,
            ActionId = sim.Profile(blade).Actions[0].Id,
            From = blade.Pos,
            Tile = near.Pos,
            Direction = new(-1, 0),
            Tiles = [near.Pos, seenTile, hidden],
        });

        var telegraph = Assert.Single(TacticsView.Telegraphs(sim, near.Side));

        Assert.Equal(near.Pos, telegraph.AttackTile);
        Assert.Contains(near.Pos, telegraph.Threatened);
        Assert.Contains(seenTile, telegraph.Threatened);
        Assert.DoesNotContain(hidden, telegraph.Threatened);
        Assert.False(TacticsView.Threats(sim, near.Side).ContainsKey(hidden));
    }

    [Fact]
    public void AC4_a_strike_entirely_out_of_vision_shows_no_marker()
    {
        var (sim, blade, near, hidden) = WalledOff();
        sim.State.Strikes.Add(new TileStrike
        {
            UnitId = blade.Id,
            ActionId = sim.Profile(blade).Actions[0].Id,
            From = blade.Pos,
            Tile = hidden,
            Direction = new(1, 0),
            Tiles = [hidden],
        });

        Assert.Empty(TacticsView.Telegraphs(sim, near.Side));
    }

    [Fact]
    public void AC4_a_strike_by_an_enemy_the_side_does_not_see_shows_no_marker()
    {
        var (sim, blade, near, _) = WalledOff();
        blade.Pos = new(6, 1);
        sim.State.Strikes.Add(new TileStrike
        {
            UnitId = blade.Id,
            ActionId = sim.Profile(blade).Actions[0].Id,
            From = blade.Pos,
            Tile = near.Pos,
            Direction = new(-1, 0),
            Tiles = [near.Pos],
        });

        Assert.Empty(TacticsView.Telegraphs(sim, near.Side));
    }

    /// <summary>P1's only survivor goes first, so the blade next to it declares after the sub-round's last player turn.</summary>
    private static (ScavengeSim Sim, Unit Near, Unit Blade) LastToAct(int subRounds)
    {
        var near = SurvivorUnit(1, new(6, 2), weapon: "pipe");
        var blade = EnemyUnit("tally_blade", new(5, 2));
        var sim = Arena(12, 7, [near, blade], data: SureHits(subRounds));
        blade.Ai.Mode = AiMode.Hunting;
        return (sim, near, blade);
    }

    [Fact]
    public void AC1b_a_strike_declared_after_the_last_player_turn_waits_for_the_end_of_the_next_sub_round()
    {
        var (sim, near, blade) = LastToAct(subRounds: 2);

        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);

        Assert.Equal((1, 2), (sim.State.Round, sim.State.SubRound));
        Assert.Equal(near.Id, sim.State.CurrentUnitId);
        Assert.Contains(sim.State.Strikes, s => s.UnitId == blade.Id);
        Assert.Equal(near.MaxHp, near.Hp);

        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);

        Assert.True(near.Hp < near.MaxHp);
    }

    [Fact]
    public void AC1b_with_no_sub_round_left_it_waits_for_the_end_of_the_next_rounds_first_sub_round()
    {
        var (sim, near, blade) = LastToAct(subRounds: 1);

        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);

        Assert.Equal((2, 1), (sim.State.Round, sim.State.SubRound));
        Assert.Equal(near.Id, sim.State.CurrentUnitId);
        Assert.Contains(sim.State.Strikes, s => s.UnitId == blade.Id);
        Assert.Equal(near.MaxHp, near.Hp);

        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);

        Assert.True(near.Hp < near.MaxHp);
    }

    [Fact]
    public void AC1b_the_player_can_step_out_of_a_deferred_strike_and_earns_the_free_counter()
    {
        var (sim, near, blade) = LastToAct(subRounds: 2);
        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);
        var covered = Assert.Single(sim.State.Strikes).Tiles;
        var clear = sim.Reachable(near).Keys.First(p => !covered.Contains(p) && p.Chebyshev(blade.Pos) <= 1);

        Assert.True(sim.Execute(new Move(1, near.Id, clear)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, near.Id)).Ok);

        Assert.Equal(near.MaxHp, near.Hp);
        Assert.Equal(blade.Id, near.FreeCounterId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AC1b_a_player_unit_sitting_out_the_sub_round_is_no_turn_to_wait_for(bool nearDoneForTheRound, bool deferred)
    {
        var first = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var blade = EnemyUnit("tally_blade", new(5, 2));
        var near = SurvivorUnit(1, new(6, 2), weapon: "pipe");
        var sim = Arena(12, 7, [first, blade, near], data: SureHits(2));
        sim.State.SubRound = 2;
        sim.State.TurnIndex = 1;
        if (nearDoneForTheRound) near.TurnOverRound = sim.State.Round;

        Assert.True(TileStrikes.Declare(sim, blade, StrikeOn(sim, blade, near, sim.Profile(blade).Actions[0].Id)).Ok);

        Assert.Equal(deferred, Assert.Single(sim.State.Strikes).Deferred);
    }

    [Fact]
    public void A_players_strike_is_never_deferred()
    {
        var near = SurvivorUnit(1, new(6, 2), weapon: "pipe");
        var husk = EnemyUnit("husk", new(5, 2));
        var sim = Arena(12, 7, [near, husk], data: SureHits());

        Assert.True(TileStrikes.Declare(sim, near, StrikeOn(sim, near, husk, sim.Profile(near).Actions[0].Id)).Ok);

        Assert.False(Assert.Single(sim.State.Strikes).Deferred);
    }
}
