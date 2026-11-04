using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Overwatch is committed to a tile or an arc and holds the unit's AP for the round. It fires when a hostile enters a watched
/// tile or commits a strike into one, each shot paying strike AP, and drops on stun, down or the holder moving.
/// </summary>
public class OverwatchTests
{
    private static ScavengeData Sure(int subRounds = 1, Func<ActionDef, ActionDef>? pistol = null) =>
        new(Real.Items.Values.Where(i => i.BaseId is null).Select(i => i.Id == "pistol" && pistol is not null ? i with { Actions = i.Actions.Select(pistol).ToList() } : i),
            Real.Enemies.Values, Real.Zones.Values,
            Real.Rules with
            {
                Turn = Real.Rules.Turn with { SubRounds = subRounds },
                Combat = Real.Rules.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = Real.Rules.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
            },
            Real.PerkHooks, Real.Cybernetics?.Def, Real.Schematics, Real.Electronics, medicine: Real.Medicine?.Def, loot: Real.LootFiles, salvage: Real.Salvage, cards: Real.Cards.Def);

    /// <summary>P1's pistol watcher first, an ally next, the enemy last: the ally's turn holds the enemy's back for the test to move it by hand.</summary>
    private static (ScavengeSim Sim, Unit Watcher, Unit Ally, Unit Enemy) Field(ScavengeData? data = null, string enemy = "tally_blade", Pos? enemyAt = null)
    {
        var watcher = SurvivorUnit(1, new(2, 3), weapon: "pistol");
        var ally = SurvivorUnit(1, new(4, 5), weapon: "pipe");
        var foe = EnemyUnit(enemy, enemyAt ?? new(9, 0));
        foe.Hp = foe.MaxHp = 500;
        var sim = Arena(12, 7, [watcher, ally, foe], data: data ?? Sure());
        return (sim, watcher, ally, foe);
    }

    private static int ShotCost(ScavengeSim sim, Unit watcher, Unit at) =>
        Combat.ApCost(sim.Profile(watcher).Actions[0], watcher.Pos, at.Pos);

    private static int Shots(ScavengeSim sim, Unit watcher, Unit target) =>
        sim.State.Events.Count(e => e.Type == ScavengeEventType.Overwatch && e.UnitId == watcher.Id && e.TargetId == target.Id);

    private static void WalkTo(ScavengeSim sim, Unit unit, Pos to)
    {
        var from = unit.Pos;
        unit.Pos = to;
        sim.Moved(unit, from);
    }

    [Fact]
    public void AC5_a_player_commits_overwatch_on_a_tile_holding_its_ap_and_ending_the_sub_turn()
    {
        var (sim, watcher, ally, _) = Field();
        var ap = watcher.Ap;
        var tile = new Pos(5, 3);

        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: tile)).Ok);

        Assert.Equal(ap, watcher.Ap);
        Assert.Equal(Stance.Overwatch, watcher.Stance);
        Assert.Equal(ally.Id, sim.State.CurrentUnitId);
        Assert.Equal([tile], Overwatches.Watched(sim, watcher));
    }

    [Fact]
    public void AC5_a_player_commits_overwatch_on_an_arc_watching_the_front_tiles_in_reach()
    {
        var (sim, watcher, _, _) = Field();

        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Arc: new Pos(1, 0))).Ok);

        var watched = Overwatches.Watched(sim, watcher);
        Assert.Contains(new Pos(3, 3), watched);
        Assert.Contains(new Pos(5, 3), watched);
        Assert.Contains(new Pos(4, 4), watched);
        Assert.DoesNotContain(new Pos(1, 3), watched);
        Assert.DoesNotContain(new Pos(2, 5), watched);
        Assert.Equal(new Pos(1, 0), watcher.Facing);
    }

    [Fact]
    public void AC5_tactics_view_lists_the_sides_own_watched_tiles_only()
    {
        var (sim, watcher, _, foe) = Field();
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);

        Assert.Equal([new Pos(5, 3)], Assert.Single(TacticsView.Watches(sim, watcher.Side)).Tiles);
        Assert.Empty(TacticsView.Watches(sim, foe.Side));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("both")]
    [InlineData("still")]
    [InlineData("far")]
    [InlineData("own")]
    public void AC5_a_commit_needs_one_tile_in_reach_or_one_real_arc(string how)
    {
        var (sim, watcher, _, _) = Field();
        var command = how switch
        {
            "none" => new CommitOverwatch(1, watcher.Id),
            "both" => new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3), Arc: new Pos(1, 0)),
            "still" => new CommitOverwatch(1, watcher.Id, Arc: new Pos(0, 0)),
            "far" => new CommitOverwatch(1, watcher.Id, Tile: new Pos(11, 6)),
            _ => new CommitOverwatch(1, watcher.Id, Tile: watcher.Pos),
        };

        var result = sim.Execute(command);

        Assert.Equal(how is "far" or "own" ? ScavengeError.OutOfRange : ScavengeError.InvalidTarget, result.Error);
        Assert.Equal(Stance.None, watcher.Stance);
    }

    [Fact]
    public void AC5_a_wall_cannot_be_watched()
    {
        var watcher = SurvivorUnit(1, new(1, 1), weapon: "pipe");
        var sim = Arena(5, 3, [watcher, EnemyUnit("husk", new(4, 1))], ["###..", "#.#..", "###.."], data: Sure());

        Assert.Equal(ScavengeError.OutOfRange, sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(2, 1))).Error);
        Assert.Equal(ScavengeError.OutOfRange, sim.Execute(new CommitOverwatch(1, watcher.Id, Arc: new Pos(1, 0))).Error);
    }

    [Fact]
    public void AC5_a_commit_needs_the_ap_for_a_shot()
    {
        var (sim, watcher, _, _) = Field();
        watcher.Ap = 0;

        var result = sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3)));

        Assert.Equal(ScavengeError.NotEnoughAp, result.Error);
    }

    [Fact]
    public void AC5_overwatch_is_no_longer_a_plain_stance()
    {
        var (sim, watcher, _, _) = Field();

        Assert.Equal(ScavengeError.InvalidStance, sim.Execute(new SetStance(1, watcher.Id, Stance.Overwatch)).Error);
        Assert.Equal(ScavengeError.InvalidStance, sim.Execute(new SetStance(1, watcher.Id, Stance.None)).Error);
    }

    [Fact]
    public void AC5_overwatch_holds_through_the_later_sub_round_and_lapses_with_the_round()
    {
        var (sim, watcher, ally, _) = Field(Sure(subRounds: 2), enemyAt: new(11, 0));
        var ap = watcher.Ap;
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, ally.Id)).Ok);

        Assert.Equal((1, 2), (sim.State.Round, sim.State.SubRound));
        Assert.Equal(ally.Id, sim.State.CurrentUnitId); // the watcher sits this sub-round out
        Assert.Equal(Stance.Overwatch, watcher.Stance);
        Assert.Equal(ap, watcher.Ap);

        Assert.True(sim.Execute(new EndTurn(1, ally.Id)).Ok);

        Assert.Equal(2, sim.State.Round);
        Assert.Equal(Stance.None, watcher.Stance);
        Assert.Null(watcher.Watch);
    }

    [Fact]
    public void AC6_overwatch_fires_when_an_enemy_enters_the_tile_and_the_shot_spends_strike_ap()
    {
        var (sim, watcher, _, foe) = Field();
        var ap = watcher.Ap;
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);

        WalkTo(sim, foe, new Pos(5, 3));

        Assert.Equal(1, Shots(sim, watcher, foe));
        Assert.Equal(ap - ShotCost(sim, watcher, foe), watcher.Ap);
        Assert.True(foe.Hp < foe.MaxHp);
    }

    [Fact]
    public void AC6_overwatch_holds_fire_on_an_enemy_entering_any_other_tile()
    {
        var (sim, watcher, _, foe) = Field();
        var ap = watcher.Ap;
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);

        WalkTo(sim, foe, new Pos(5, 2));

        Assert.Equal(0, Shots(sim, watcher, foe));
        Assert.Equal(ap, watcher.Ap);
    }

    [Fact]
    public void AC6_an_arc_fires_on_each_watched_tile_entered_while_ap_lasts_then_stops()
    {
        var (sim, watcher, _, foe) = Field();
        watcher.Ap = ShotCost(sim, watcher, foe) * 2;
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Arc: new Pos(1, 0))).Ok);

        WalkTo(sim, foe, new Pos(5, 3));
        WalkTo(sim, foe, new Pos(5, 4));
        WalkTo(sim, foe, new Pos(4, 3));

        Assert.Equal(2, Shots(sim, watcher, foe));
        Assert.Equal(0, watcher.Ap);
    }

    [Fact]
    public void A_perks_extra_shot_fires_once_the_held_ap_has_run_out()
    {
        var (sim, watcher, _, foe) = Field();
        watcher.Hooks = Real.HooksFor(["sentinel"]);
        watcher.Ap = ShotCost(sim, watcher, foe);
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Arc: new Pos(1, 0))).Ok);

        WalkTo(sim, foe, new Pos(5, 3));
        WalkTo(sim, foe, new Pos(5, 4));
        WalkTo(sim, foe, new Pos(4, 3));

        Assert.Equal(2, Shots(sim, watcher, foe));
        Assert.Equal(Stance.None, watcher.Stance);
    }

    [Fact]
    public void AC6_overwatch_fires_when_an_enemy_commits_a_strike_into_the_tile()
    {
        var (sim, watcher, ally, foe) = Field();
        foe.Pos = ally.Pos + new Pos(1, 0);
        var ap = watcher.Ap;
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: ally.Pos)).Ok);

        Assert.True(TileStrikes.Declare(sim, foe, StrikeOn(sim, foe, ally, sim.Profile(foe).Actions[0].Id)).Ok);

        Assert.Equal(1, Shots(sim, watcher, foe));
        Assert.Equal(ap - ShotCost(sim, watcher, foe), watcher.Ap);
        Assert.Contains(sim.State.Strikes, s => s.UnitId == foe.Id); // a plain hit does not stop it
    }

    [Fact]
    public void AC6_a_live_enemy_walking_into_a_watched_arc_on_its_own_sub_turn_draws_fire()
    {
        var watcher = SurvivorUnit(1, new(2, 3), weapon: "pistol");
        var foe = EnemyUnit("husk", new(8, 3));
        foe.Ai.Mode = AiMode.Hunting;
        foe.Hp = foe.MaxHp = 500;
        var sim = Arena(12, 7, [watcher, foe], data: Sure());

        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Arc: new Pos(1, 0))).Ok);

        Assert.True(Shots(sim, watcher, foe) >= 1);
    }

    [Fact]
    public void AC6_an_enemy_shoved_into_the_tile_draws_no_fire()
    {
        var (sim, watcher, ally, foe) = Field();
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);

        CardAbilities.Displace(sim, foe, new Pos(5, 3), ally);

        Assert.Equal(0, Shots(sim, watcher, foe));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Overwatch_friendly_fire_hits_only_displaced_units(bool displaced)
    {
        var watcher = SurvivorUnit(1, new(2, 3), weapon: "machete");
        var upper = SurvivorUnit(1, new(3, 2), weapon: "pipe");
        var lower = SurvivorUnit(1, new(3, 4), weapon: "pipe");
        var foe = EnemyUnit("husk", new(9, 0));
        foe.Hp = foe.MaxHp = 500;
        var sim = Arena(12, 7, [watcher, upper, lower, foe], data: Sure());
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(3, 3))).Ok);
        upper.Displaced = lower.Displaced = displaced;

        WalkTo(sim, foe, new Pos(3, 3));

        Assert.True(foe.Hp < foe.MaxHp);
        Assert.Equal(displaced, upper.Hp < upper.MaxHp || lower.Hp < lower.MaxHp);
    }

    private static (ScavengeSim Sim, Unit Watcher, Unit Ally, Unit Foe) StrikeWatched(Func<ActionDef, ActionDef> pistol, Action<Unit>? foeSetup = null)
    {
        var (sim, watcher, ally, foe) = Field(Sure(pistol: pistol), enemy: "linked");
        foe.Pos = ally.Pos + new Pos(1, 0);
        foeSetup?.Invoke(foe);
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: ally.Pos)).Ok);
        Assert.True(TileStrikes.Declare(sim, foe, StrikeOn(sim, foe, ally, sim.Profile(foe).Actions[0].Id)).Ok);
        Assert.Equal(1, Shots(sim, watcher, foe));
        return (sim, watcher, ally, foe);
    }

    [Fact]
    public void AC7_a_stunning_overwatch_hit_which_sets_the_pin_flag_cancels_the_enemys_strike()
    {
        var (sim, _, _, foe) = StrikeWatched(a => a with { Bane = new BaneDef { Tag = "network", Stun = true } });

        Assert.True(foe.Suppressed);
        Assert.DoesNotContain(sim.State.Strikes, s => s.UnitId == foe.Id);
    }

    [Fact]
    public void AC7_a_downing_overwatch_hit_cancels_the_enemys_strike()
    {
        var (sim, _, _, foe) = StrikeWatched(a => a, f => f.Hp = 1);

        Assert.False(foe.IsActive);
        Assert.DoesNotContain(sim.State.Strikes, s => s.UnitId == foe.Id);
    }

    [Fact]
    public void AC7_a_displacing_overwatch_hit_cancels_the_enemys_strike()
    {
        var (sim, _, ally, foe) = StrikeWatched(a => a with { Knockback = 1 });

        Assert.NotEqual(ally.Pos + new Pos(1, 0), foe.Pos);
        Assert.DoesNotContain(sim.State.Strikes, s => s.UnitId == foe.Id);
    }

    [Fact]
    public void AC7_a_plain_overwatch_hit_leaves_the_enemys_strike_standing()
    {
        var (sim, _, _, foe) = StrikeWatched(a => a);

        Assert.True(foe.IsActive);
        Assert.Contains(sim.State.Strikes, s => s.UnitId == foe.Id);
    }

    [Fact]
    public void AC8_overwatch_drops_when_the_holder_is_stunned_by_a_hit()
    {
        var holder = EnemyUnit("linked", new(5, 3));
        var shocker = SurvivorUnit(1, new(2, 3), weapon: "pistol");
        var sim = Arena(12, 7, [shocker, holder], data: Sure(pistol: a => a with { Bane = new BaneDef { Tag = "network", Stun = true } }));
        Assert.True(Overwatches.Commit(sim, holder, new CommitOverwatch(0, holder.Id, Arc: new Pos(-1, 0))).Ok);

        Combat.Hit(sim, shocker, sim.Profile(shocker).Actions[0], [holder]);

        Assert.Equal(Stance.None, holder.Stance);
        Assert.Null(holder.Watch);
    }

    [Fact]
    public void AC8_overwatch_drops_when_the_holder_is_stunned_by_an_emp()
    {
        var holder = EnemyUnit("linked", new(5, 3));
        var thrower = SurvivorUnit(1, new(2, 3), weapon: "pipe", pack: "emp_grenade");
        var sim = Arena(12, 7, [thrower, holder], data: Sure());
        Assert.True(Overwatches.Commit(sim, holder, new CommitOverwatch(0, holder.Id, Arc: new Pos(-1, 0))).Ok);

        Assert.True(sim.Execute(new UseItem(1, thrower.Id, "emp_grenade", At: holder.Pos)).Ok);

        Assert.Equal(Stance.None, holder.Stance);
        Assert.Null(holder.Watch);
    }

    [Fact]
    public void AC8_overwatch_drops_when_the_holder_goes_down()
    {
        var (sim, watcher, _, foe) = Field();
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);

        Combat.ApplyDamage(sim, watcher, BodyPart.Torso, watcher.Hp + 1, attacker: foe);

        Assert.Equal(Stance.None, watcher.Stance);
        Assert.Null(watcher.Watch);
        WalkTo(sim, foe, new Pos(5, 3));
        Assert.Equal(0, Shots(sim, watcher, foe));
    }

    [Fact]
    public void AC8_overwatch_drops_when_the_holder_moves_even_when_shoved()
    {
        var (sim, watcher, _, foe) = Field();
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);

        CardAbilities.Displace(sim, watcher, watcher.Pos + new Pos(0, 1), foe);

        Assert.Equal(Stance.None, watcher.Stance);
        Assert.Null(watcher.Watch);
        WalkTo(sim, foe, new Pos(5, 3));
        Assert.Equal(0, Shots(sim, watcher, foe));
    }

    [Fact]
    public void A_committed_watch_survives_save_and_load()
    {
        var (sim, watcher, _, _) = Field();
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);

        var loaded = ScavengeSave.Load(sim.Data, ScavengeSave.Save(sim.State));

        Assert.Equal([new Pos(5, 3)], Overwatches.Watched(loaded, loaded.State.FindUnit(watcher.Id)!));
    }

    [Fact]
    public void A_dropped_watch_gives_the_holder_no_turn_back_this_round()
    {
        var (sim, watcher, ally, foe) = Field(Sure(subRounds: 2), enemyAt: new(11, 0));
        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);
        CardAbilities.Displace(sim, watcher, watcher.Pos + new Pos(0, 1), foe);
        Assert.Equal(Stance.None, watcher.Stance);

        Assert.True(sim.Execute(new EndTurn(1, ally.Id)).Ok);

        Assert.Equal((1, 2), (sim.State.Round, sim.State.SubRound));
        Assert.Equal(ally.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void A_perks_extra_shot_never_buys_a_dearer_attack_while_ap_is_held()
    {
        // The pistol's free melee fallback aside, the survivor's fists cost what it holds; the gun would cost more.
        var (sim, watcher, _, foe) = Field(Sure(pistol: a => a with { ApCost = 9 }));
        watcher.Hooks = Real.HooksFor(["sentinel"]);
        foe.Pos = watcher.Pos + new Pos(3, 0);
        watcher.Reactions = 1;
        watcher.Stance = Stance.Overwatch;
        watcher.Watch = new OverwatchWatch { Tile = new Pos(3, 3) };
        var melee = sim.Profile(watcher).Actions.Where(a => a.ApCost <= watcher.Ap).ToList();
        var ap = watcher.Ap;

        WalkTo(sim, foe, new Pos(3, 3));

        Assert.NotEmpty(melee);
        Assert.Equal(1, watcher.Reactions);
        Assert.True(watcher.Ap < ap);
    }

    [Fact]
    public void The_commit_log_never_names_the_watched_tile()
    {
        var (sim, watcher, _, _) = Field();

        Assert.True(sim.Execute(new CommitOverwatch(1, watcher.Id, Tile: new Pos(5, 3))).Ok);

        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == watcher.Id && e.Type == ScavengeEventType.Overwatch && e.Message.Contains(new Pos(5, 3).ToString()));
    }
}
