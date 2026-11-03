using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// The scavenge screen's feel (a UI view model like <see cref="ReplayPacing"/>): the squad's hit chain, how big a pop
/// is, how hard a hit shakes and stops the screen. Nothing here is a rule.
/// </summary>
public class ReplayJuiceTests
{
    private static (ScavengeSim Sim, Unit Mine, Unit Ally, Unit Enemy) Setup()
    {
        var mine = SurvivorUnit(1, new(1, 1));
        var ally = SurvivorUnit(1, new(2, 1));
        var enemy = EnemyUnit("husk", new(8, 1));
        return (Arena(10, 3, [mine, ally, enemy]), mine, ally, enemy);
    }

    private static ScavengeEvent Hit(Unit by, Unit on, double amount, HitTier tier = HitTier.Hit, string where = "") =>
        new(1, ScavengeEventType.Attacked, $"{by.Name} hit {on.Name}'s torso{where} for {amount:0} (Punch).", by.Id, on.Id, on.Pos, Tier: tier, Amount: amount);

    private static ScavengeEvent Miss(Unit by, Unit on) => new(1, ScavengeEventType.Missed, $"{by.Name} missed {on.Name} (Punch, 50%).", by.Id, on.Id, on.Pos);

    [Fact]
    public void The_squads_hits_and_card_plays_build_a_chain_and_a_running_total()
    {
        var (sim, mine, ally, enemy) = Setup();
        var chain = new HitChain();
        Assert.Equal(1, chain.Step(sim.State, mine.Side, Hit(mine, enemy, 6)).Count);
        Assert.Equal(2, chain.Step(sim.State, mine.Side, new(1, ScavengeEventType.CardPlayed, "Bo plays Shove: push 2.", ally.Id, enemy.Id, ally.Pos)).Count);
        var third = chain.Step(sim.State, mine.Side, Hit(ally, enemy, 9));
        Assert.Equal(3, third.Count);
        Assert.Equal(15, third.Total, 9);
        Assert.True(third.Grew);
    }

    [Fact]
    public void A_squad_miss_breaks_the_chain_but_the_rounds_total_stays()
    {
        var (sim, mine, _, enemy) = Setup();
        var chain = new HitChain();
        chain.Step(sim.State, mine.Side, Hit(mine, enemy, 6));
        chain.Step(sim.State, mine.Side, Hit(mine, enemy, 4));
        var broke = chain.Step(sim.State, mine.Side, Miss(mine, enemy));
        Assert.Equal(0, broke.Count);
        Assert.True(broke.Broke);
        Assert.Equal(10, broke.Total, 9);
        Assert.Equal(1, chain.Step(sim.State, mine.Side, Hit(mine, enemy, 2)).Count);
    }

    [Fact]
    public void The_chain_and_total_reset_when_a_round_starts()
    {
        var (sim, mine, _, enemy) = Setup();
        var chain = new HitChain();
        chain.Step(sim.State, mine.Side, Hit(mine, enemy, 6));
        var reset = chain.Step(sim.State, mine.Side, new(2, ScavengeEventType.RoundStarted, "Round 2."));
        Assert.Equal(0, reset.Count);
        Assert.Equal(0, reset.Total);
        Assert.True(reset.Reset);
    }

    [Fact]
    public void What_the_enemy_does_and_hits_on_the_squad_neither_grow_nor_break_the_chain()
    {
        var (sim, mine, _, enemy) = Setup();
        var chain = new HitChain();
        chain.Step(sim.State, mine.Side, Hit(mine, enemy, 6));
        var taken = chain.Step(sim.State, mine.Side, Hit(enemy, mine, 5));
        Assert.Equal(1, taken.Count);
        Assert.False(taken.Grew || taken.Broke);
        Assert.Equal(1, chain.Step(sim.State, mine.Side, Miss(enemy, mine)).Count);
        // A shock the unit takes itself (a failed hack's feedback) is no hit of the squad.
        Assert.Equal(1, chain.Step(sim.State, mine.Side, Hit(mine, mine, 3)).Count);
        Assert.Equal(6, chain.Total, 9);
    }

    [Fact]
    public void Another_sides_chain_is_its_own()
    {
        var (sim, mine, _, enemy) = Setup();
        var chain = new HitChain();
        Assert.Equal(1, chain.Step(sim.State, enemy.Side, Hit(enemy, mine, 5)).Count);
        Assert.Equal(1, chain.Step(sim.State, enemy.Side, Hit(mine, enemy, 5)).Count);
    }

    [Fact]
    public void A_hit_reads_its_arc_exposure_and_tier_from_the_event()
    {
        var (_, mine, _, enemy) = Setup();
        var rear = ReplayJuice.ImpactOf(Hit(mine, enemy, 12, HitTier.Solid, " from behind (exposed)"));
        Assert.Equal(HitTier.Solid, rear.Tier);
        Assert.Equal(FacingArc.Rear, rear.Arc);
        Assert.True(rear.Exposed);
        Assert.Equal(12, rear.Amount, 9);
        var side = ReplayJuice.ImpactOf(Hit(mine, enemy, 3, HitTier.Graze, " from the side"));
        Assert.Equal(FacingArc.Side, side.Arc);
        Assert.False(side.Exposed);
        Assert.Equal(FacingArc.Front, ReplayJuice.ImpactOf(Hit(mine, enemy, 3)).Arc);
    }

    [Fact]
    public void A_real_blow_from_behind_on_an_exposed_target_reads_as_rear_and_exposed()
    {
        // Every swing lands solid, so the event is Combat's own line, not a hand-written one.
        var sure = With(r => r with { Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 0 } } });
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(3, 2));
        var sim = Arena(8, 5, [a, b], data: sure);
        b.Facing = new Pos(1, 0);
        (b.ExposedUntilRound, b.ExposedToSide) = (sim.State.Round, a.Side);
        Assert.True(Blow(sim, a, b, "swing").Ok);
        var hit = Assert.Single(sim.State.Events, e => e.Type == ScavengeEventType.Attacked);
        var impact = ReplayJuice.ImpactOf(hit);
        Assert.Equal(FacingArc.Rear, impact.Arc);
        Assert.True(impact.Exposed);
        Assert.Equal(hit.Tier, impact.Tier);
        Assert.Equal(hit.Amount!.Value, impact.Amount, 9);
    }

    [Fact]
    public void A_card_play_and_the_blow_it_sets_up_are_two_links()
    {
        var (sim, mine, _, enemy) = Setup();
        var chain = new HitChain();
        chain.Step(sim.State, mine.Side, new(1, ScavengeEventType.CardPlayed, "P1 plays Steady Aim.", mine.Id, mine.Id, mine.Pos));
        Assert.Equal(2, chain.Step(sim.State, mine.Side, Hit(mine, enemy, 5)).Count);
    }

    [Fact]
    public void Pops_grow_with_the_value_and_the_chain_and_stay_within_bounds()
    {
        Assert.True(ReplayJuice.PopScale(20, 0) > ReplayJuice.PopScale(4, 0));
        Assert.True(ReplayJuice.PopScale(4, 5) > ReplayJuice.PopScale(4, 1));
        Assert.True(ReplayJuice.PopHeat(30, 4) > ReplayJuice.PopHeat(4, 1));
        Assert.Equal(1, ReplayJuice.PopScale(0, 0), 9);
        Assert.InRange(ReplayJuice.PopScale(9999, 99), 1, ReplayJuice.MaxPopScale);
        Assert.InRange(ReplayJuice.PopHeat(9999, 99), 0, 1);
        Assert.Equal(0, ReplayJuice.PopHeat(0, 0), 9);
    }

    private static readonly JuiceSettings Normal = new(EffectsLevel.Normal, Shake: true, HitStop: true, Flashes: true, ReduceMotion: false);

    [Fact]
    public void Harder_hits_shake_and_stop_the_screen_more()
    {
        var graze = ReplayJuice.Feel(new Impact(HitTier.Graze, FacingArc.Front, false, 2), Normal);
        var hit = ReplayJuice.Feel(new Impact(HitTier.Hit, FacingArc.Front, false, 6), Normal);
        var solid = ReplayJuice.Feel(new Impact(HitTier.Solid, FacingArc.Front, false, 12), Normal);
        Assert.True(graze.Shake < hit.Shake && hit.Shake < solid.Shake);
        Assert.True(graze.HitStop <= hit.HitStop && hit.HitStop < solid.HitStop);
        Assert.Equal(0, graze.Flash);
        Assert.True(solid.Flash > 0);

        var rear = ReplayJuice.Feel(new Impact(HitTier.Hit, FacingArc.Rear, false, 6), Normal);
        var flank = ReplayJuice.Feel(new Impact(HitTier.Hit, FacingArc.Side, false, 6), Normal);
        var exposed = ReplayJuice.Feel(new Impact(HitTier.Hit, FacingArc.Front, true, 6), Normal);
        Assert.True(rear.Shake > flank.Shake && flank.Shake > hit.Shake);
        Assert.True(exposed.Shake > hit.Shake);
        Assert.True(rear.HitStop > hit.HitStop);
    }

    [Fact]
    public void Kills_and_pushes_land_with_weight_too()
    {
        var kill = ReplayJuice.Feel(Impact.Kill, Normal);
        var push = ReplayJuice.Feel(Impact.Push, Normal);
        Assert.True(kill.Shake > push.Shake && push.Shake > 0);
        Assert.True(kill.HitStop > 0 && kill.Flash > 0);
    }

    [Fact]
    public void Intensity_scales_everything_and_off_or_a_toggle_turns_it_off()
    {
        var solid = new Impact(HitTier.Solid, FacingArc.Rear, true, 14);
        var low = ReplayJuice.Feel(solid, Normal with { Level = EffectsLevel.Low });
        var normal = ReplayJuice.Feel(solid, Normal);
        var high = ReplayJuice.Feel(solid, Normal with { Level = EffectsLevel.High });
        Assert.True(low.Shake < normal.Shake && normal.Shake < high.Shake);
        Assert.True(low.HitStop < normal.HitStop && normal.HitStop <= high.HitStop);
        Assert.InRange(high.Shake, 0, 1);
        Assert.InRange(high.HitStop, 0, ReplayJuice.MaxHitStop);
        Assert.Equal(default, ReplayJuice.Feel(solid, Normal with { Level = EffectsLevel.Off }));
        Assert.Equal(0, ReplayJuice.Feel(solid, Normal with { Shake = false }).Shake);
        Assert.Equal(0, ReplayJuice.Feel(solid, Normal with { HitStop = false }).HitStop);
        Assert.Equal(0, ReplayJuice.Feel(solid, Normal with { Flashes = false }).Flash);
    }

    [Fact]
    public void Reduce_motion_takes_out_shake_and_flashes_but_keeps_the_beat()
    {
        var feel = ReplayJuice.Feel(new Impact(HitTier.Solid, FacingArc.Rear, true, 14), Normal with { ReduceMotion = true });
        Assert.Equal(0, feel.Shake);
        Assert.Equal(0, feel.Flash);
        Assert.True(feel.HitStop > 0);
    }

    [Fact]
    public void Skipping_animations_has_no_hit_stop_and_a_round_never_stops_longer_than_the_cap()
    {
        var budget = new HitStopBudget();
        Assert.Equal(0, budget.Take(0.1, skip: true));
        double total = 0;
        for (var i = 0; i < 40; i++) total += budget.Take(ReplayJuice.MaxHitStop, skip: false);
        Assert.Equal(HitStopBudget.PerRound, total, 9);
        budget.NewRound();
        Assert.Equal(ReplayJuice.MaxHitStop, budget.Take(ReplayJuice.MaxHitStop, skip: false), 9);
        Assert.Equal(ReplayJuice.MaxHitStop, budget.Spent, 9);
    }

    [Fact]
    public void Flashes_never_strobe_faster_than_the_cap()
    {
        var gate = new FlashGate();
        var shown = 0;
        // 50 big hits a tenth of a second apart (5 s): at most MaxPerSecond flashes in any second.
        for (var i = 0; i < 50; i++)
            if (gate.Allow(i * 0.1)) shown++;
        Assert.Equal(10, shown);
        Assert.True(gate.Allow(100));
        Assert.False(gate.Allow(100 + FlashGate.MinGap / 2));
    }

    [Fact]
    public void A_hand_fans_out_symmetrically_with_the_middle_card_highest()
    {
        Assert.Equal(0, ReplayJuice.FanAngle(0, 1), 9);
        Assert.Equal(0, ReplayJuice.FanDrop(0, 1), 9);
        Assert.Equal(-ReplayJuice.FanAngle(0, 5), ReplayJuice.FanAngle(4, 5), 9);
        Assert.Equal(0, ReplayJuice.FanAngle(2, 5), 9);
        Assert.True(ReplayJuice.FanAngle(0, 5) < 0 && ReplayJuice.FanAngle(1, 5) < 0);
        Assert.True(ReplayJuice.FanDrop(0, 5) > ReplayJuice.FanDrop(1, 5) && ReplayJuice.FanDrop(1, 5) > ReplayJuice.FanDrop(2, 5));
        // A big hand does not fan wider than a full one.
        Assert.InRange(Math.Abs(ReplayJuice.FanAngle(0, 12)), 0, ReplayJuice.MaxFanDegrees);
    }

    [Fact]
    public void Chain_sounds_climb_in_pitch_and_the_music_heats_up_with_the_chain_both_capped()
    {
        Assert.Equal(1, ReplayJuice.ChainPitch(0), 9);
        Assert.Equal(1, ReplayJuice.ChainPitch(1), 9);
        Assert.True(ReplayJuice.ChainPitch(3) > ReplayJuice.ChainPitch(2));
        Assert.InRange(ReplayJuice.ChainPitch(99), 1, ReplayJuice.MaxChainPitch);
        Assert.Equal(0, ReplayJuice.MusicHeat(0), 9);
        Assert.True(ReplayJuice.MusicHeat(4) > ReplayJuice.MusicHeat(2));
        Assert.InRange(ReplayJuice.MusicHeat(99), 0, 1);
    }
}
