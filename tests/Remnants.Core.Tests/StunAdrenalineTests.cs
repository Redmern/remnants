using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>A block can stun the attacker, players too; adrenaline builds on what goes right and amplifies a unit in tiers.</summary>
public class StunAdrenalineTests
{
    private static ScavengeData Data(double stun = -1) => With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
        Stun = stun < 0 ? r.Stun : r.Stun with { Base = stun, PerBlock = 0, PowerPerDamage = 0, PerfectBonus = 0, Min = stun, Max = stun },
    });

    private static readonly ScavengeData Always = Data(1);
    private static readonly ScavengeData Never = Data(0);

    private static ScavengeSim Table(ScavengeData data, IEnumerable<Unit> units, params string[] hand)
    {
        var sim = Arena(12, 7, units, data: data);
        foreach (var pid in units.Where(u => u.PlayerId is not null).Select(u => u.PlayerId!.Value).Distinct())
            sim.State.Decks.Add(new SquadDeck { PlayerId = pid, Hand = hand.ToList(), Points = 9, Budget = 9, HandSize = 9 });
        return sim;
    }

    /// <summary>a (player 1) swings at a block by b (player 2) and it settles.</summary>
    private static (ScavengeSim Sim, Unit A, Unit B) Blocked(ScavengeData data)
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table(data, [a, b], "parry");
        Assert.True(sim.Execute(new PlayCard(2, b.Id, "parry", Tile: b.Pos)).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Settle(sim);
        return (sim, a, b);
    }

    private static void NextRound(ScavengeSim sim)
    {
        foreach (var u in sim.State.Units.Where(u => u.PlayerControlled && u.IsActive && u.OnMap).ToList()) sim.Execute(new EndTurn(u.PlayerId!.Value, u.Id));
    }

    [Fact]
    public void The_stun_chance_is_base_plus_block_over_power_clamped_with_a_perfect_bonus()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table(Data(), [a, b]);
        var rules = sim.Data.Rules.Stun;
        var action = sim.Profile(a).Action("swing")!;
        var power = (action.MinDamage + action.MaxDamage) / 2 * rules.PowerPerDamage;
        Assert.Equal(rules.Base + (0.5 - power) * rules.PerBlock, Stuns.Chance(sim, b, a, action, 0.5, perfect: false), 6);
        Assert.Equal(rules.PerfectBonus, Stuns.Chance(sim, b, a, action, 0.5, perfect: true) - Stuns.Chance(sim, b, a, action, 0.5, perfect: false), 6);
        var heavy = action with { MinDamage = 500, MaxDamage = 500 };
        Assert.Equal(rules.Min, Stuns.Chance(sim, b, a, heavy, 0, perfect: false), 6);
        var feather = action with { MinDamage = 0, MaxDamage = 0 };
        Assert.Equal(rules.Max, Stuns.Chance(sim, b, a, feather, 5, perfect: true), 6);
    }

    [Fact]
    public void A_block_can_stun_a_player_and_leaves_what_it_queued_this_round_alone()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table(Always, [a, b], "parry");
        Assert.True(sim.Execute(new PlayCard(2, b.Id, "parry", Tile: b.Pos)).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new Recover(1, a.Id)).Ok);
        Settle(sim);
        Assert.True(Stuns.Active(sim.State, a));
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Stunned && e.TargetId == a.Id);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Recovered && e.UnitId == a.Id);
    }

    [Fact]
    public void An_open_hit_never_stuns()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table(Always, [a, b]);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Settle(sim);
        Assert.False(Stuns.Active(sim.State, a));
    }

    [Fact]
    public void A_stun_costs_ap_mp_and_priority_next_round_and_is_gone_the_round_after()
    {
        var (sim, a, _) = Blocked(Always);
        var rules = sim.Data.Rules.Stun;
        var profile = sim.Profile(a);
        sim.State.Round++;
        sim.BeginRound(a);
        Assert.Equal((profile.MaxAp - rules.ApLoss, profile.MaxMp - rules.MpLoss), (a.Ap, a.Mp));
        Assert.Equal(-rules.PriorityPenalty, a.PriorityBonus);
        var reeling = Priority.Movement(sim, a);
        a.PriorityBonus = 0;
        Assert.Equal(rules.PriorityPenalty, Priority.Movement(sim, a) - reeling, 6);
        Assert.True(Stuns.Active(sim.State, a));
        sim.State.Round++;
        sim.BeginRound(a);
        Assert.Equal((profile.MaxAp, profile.MaxMp, 0.0), (a.Ap, a.Mp, a.PriorityBonus));
        Assert.False(Stuns.Active(sim.State, a));
    }

    [Fact]
    public void Resistance_scales_the_chance_down_and_repeats_diminish_it()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table(Data(), [a, b]);
        var rules = sim.Data.Rules.Stun;
        var action = sim.Profile(a).Action("swing")!;
        var fresh = Stuns.Chance(sim, b, a, action, 0.5, false);
        a.StunCount = 2;
        Assert.Equal(fresh * rules.RepeatFactor * rules.RepeatFactor, Stuns.Chance(sim, b, a, action, 0.5, false), 6);
        a.StunCount = 0;

        var data = Data();
        data = data.WithEnemy(data.Enemies["husk"] with { StunResist = 0.5 });
        var husk = EnemyUnit("husk", new(3, 3));
        var sim2 = Table(data, [husk, b]);
        var huskAction = sim2.Profile(husk).Actions[0];
        var plain = Math.Clamp(rules.Base + (0.5 - (huskAction.MinDamage + huskAction.MaxDamage) / 2 * rules.PowerPerDamage) * rules.PerBlock, rules.Min, rules.Max);
        Assert.Equal(plain * 0.5, Stuns.Chance(sim2, b, husk, huskAction, 0.5, false), 6);
    }

    [Fact]
    public void Being_blocked_costs_extra_stamina_a_stun_more_and_the_block_a_little()
    {
        var never = Blocked(Never);
        var always = Blocked(Always);
        var rules = never.Sim.Data.Rules.Stun;
        var ap = never.Sim.Data.Rules.Turn.StaminaPerAp;
        var baseStamina = never.Sim.Profile(never.A).MaxStamina;
        Assert.Equal(baseStamina - ap - rules.BlockedStamina, Economy.Stamina(never.Sim, never.A));
        Assert.Equal(baseStamina - ap - rules.BlockedStamina - rules.StunnedStamina, Economy.Stamina(always.Sim, always.A));
        Assert.Equal(baseStamina - ap - rules.DefenderStamina, Economy.Stamina(never.Sim, never.B)); // the parry's AP, then the block
    }

    [Fact]
    public void Adrenaline_rises_on_a_landed_hit_and_falls_further_on_a_miss()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table(Never, [a, b]);
        var rules = sim.Data.Rules.Adrenaline;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Settle(sim);
        Assert.Equal(rules.GainHit, a.Adrenaline, 6);

        var missData = With(r => r with { Combat = r.Combat with { MinHitChance = 0, MaxHitChance = 0 } });
        var c = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var d = SurvivorUnit(2, new(4, 3));
        var sim2 = Table(missData, [c, d]);
        c.Adrenaline = 40;
        Assert.True(sim2.Execute(new AttackTile(1, c.Id, d.Pos, "swing")).Ok);
        Settle(sim2);
        Assert.Equal(40 - rules.DrainFailed, c.Adrenaline, 6);
    }

    [Fact]
    public void A_push_is_a_strong_gain_a_whiffed_or_held_push_a_failure_and_a_dash_a_small_gain()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table(Never, [a, b]);
        var rules = sim.Data.Rules.Adrenaline;
        Assert.True(rules.GainPush > rules.GainHit);
        Assert.True(sim.Execute(new Push(1, a.Id, new Pos(1, 0))).Ok);
        Settle(sim);
        Assert.Equal(rules.GainPush, a.Adrenaline, 6);

        var c = SurvivorUnit(1, new(3, 3));
        var sim2 = Table(Never, [c, SurvivorUnit(2, new(9, 3))]);
        c.Adrenaline = 40;
        Assert.True(sim2.Execute(new Push(1, c.Id, new Pos(1, 0))).Ok);
        Settle(sim2);
        Assert.Equal(40 - rules.DrainFailed, c.Adrenaline, 6);

        var e = SurvivorUnit(1, new(3, 3));
        var f = SurvivorUnit(2, new(4, 3));
        var sim3 = Table(Never, [e, f], "parry");
        e.Adrenaline = 40;
        Assert.True(sim3.Execute(new PlayCard(2, f.Id, "parry", Tile: f.Pos)).Ok);
        Assert.True(sim3.Execute(new Push(1, e.Id, new Pos(1, 0))).Ok);
        Settle(sim3);
        Assert.Equal(40 - rules.DrainFailed, e.Adrenaline, 6);

        var g = SurvivorUnit(1, new(3, 3));
        var sim4 = Table(Never, [g, SurvivorUnit(2, new(9, 6))]);
        Assert.True(sim4.Execute(new Dash(1, g.Id, new Pos(1, 0), 2)).Ok);
        Settle(sim4);
        Assert.Equal(rules.GainDash, g.Adrenaline, 6);
    }

    [Fact]
    public void A_block_and_a_stun_feed_the_defender_a_blocked_or_stunned_attacker_pays()
    {
        var (sim, a, b) = Blocked(Always);
        var rules = sim.Data.Rules.Adrenaline;
        Assert.Equal(rules.GainBlock + rules.GainStun, b.Adrenaline, 6);
        Assert.Equal(0, a.Adrenaline, 6); // already empty: it cannot go below nothing

        var c = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var d = SurvivorUnit(2, new(4, 3));
        var sim2 = Table(Always, [c, d], "parry");
        c.Adrenaline = 50;
        Assert.True(sim2.Execute(new PlayCard(2, d.Id, "parry", Tile: d.Pos)).Ok);
        Assert.True(sim2.Execute(new AttackTile(1, c.Id, d.Pos, "swing")).Ok);
        Settle(sim2);
        Assert.Equal(50 - rules.DrainFailed - rules.DrainStunned, c.Adrenaline, 6);
    }

    [Fact]
    public void A_heavy_hit_drains_the_victim()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var data = With(r => r with
        {
            Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1 },
            Adrenaline = r.Adrenaline with { HeavyHitShare = 0.001 },
        });
        var sim = Table(data, [a, b]);
        b.Adrenaline = 50;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Settle(sim);
        Assert.Equal(50 - sim.Data.Rules.Adrenaline.DrainHeavyHit, b.Adrenaline, 6);
    }

    [Fact]
    public void It_drains_each_round_and_stays_between_zero_and_a_hundred()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var sim = Table(Never, [a, SurvivorUnit(2, new(9, 3))]);
        var rules = sim.Data.Rules.Adrenaline;
        a.Adrenaline = 30;
        sim.State.Round = 2;
        sim.BeginRound(a);
        Assert.Equal(30 - rules.DrainPerRound, a.Adrenaline, 6);
        a.Adrenaline = 1;
        sim.State.Round = 3;
        sim.BeginRound(a);
        Assert.Equal(0, a.Adrenaline, 6);
        Adrenaline.Gain(sim, a, 500);
        Assert.Equal(rules.Max, a.Adrenaline, 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(24, 0)]
    [InlineData(25, 1)]
    [InlineData(50, 2)]
    [InlineData(75, 3)]
    [InlineData(100, 4)]
    public void The_tiers_are_quarter_steps_with_a_bigger_one_at_the_top(double meter, int tier)
    {
        var a = SurvivorUnit(1, new(3, 3));
        var sim = Table(Never, [a]);
        a.Adrenaline = meter;
        Assert.Equal(tier, Adrenaline.Tier(sim, a));
        var bonus = sim.Data.Rules.Adrenaline.TierBonus;
        Assert.Equal(tier == 0 ? 0 : bonus[tier - 1], Adrenaline.Bonus(sim, a), 6);
        Assert.True(bonus[3] - bonus[2] > bonus[2] - bonus[1]);
    }

    [Fact]
    public void A_tier_amplifies_damage_priority_block_stun_chance_and_regen()
    {
        double Cut(double meter)
        {
            var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
            var b = SurvivorUnit(2, new(4, 3));
            var sim = Table(Never, [a, b]);
            a.Adrenaline = meter;
            sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing"));
            Settle(sim);
            return b.MaxHp - b.Hp;
        }
        Assert.True(Cut(100) > Cut(0));

        var u = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var foe = SurvivorUnit(2, new(4, 3));
        var sim = Table(Data(), [u, foe]);
        var action = sim.Profile(foe).Actions[0];
        foe.Fatigue = u.Fatigue = 9;
        var (priority, block, stun, regen) = (Priority.Movement(sim, u), Defences.BlockOf(sim, u, 0.3), Stuns.Chance(sim, u, foe, action, 0.3, false), Economy.Regen(sim, u));
        u.Adrenaline = 100;
        Assert.True(Priority.Movement(sim, u) > priority);
        Assert.True(Defences.BlockOf(sim, u, 0.3) > block);
        Assert.True(Stuns.Chance(sim, u, foe, action, 0.3, false) > stun);
        Assert.True(Economy.Regen(sim, u) > regen);
    }

    [Fact]
    public void A_mission_starts_from_nothing_or_what_perks_and_enemy_data_say()
    {
        var a = SurvivorUnit(1, new(3, 3));
        a.Adrenaline = 80;
        var data = Never;
        data = data.WithEnemy(data.Enemies["husk"] with { AdrenalineStart = 30 });
        var husk = EnemyUnit("husk", new(5, 3));
        var sim = Arena(12, 7, [a, husk], data: data);
        Adrenaline.Started(sim);
        Assert.Equal(0, a.Adrenaline, 6);
        Assert.Equal(30, husk.Adrenaline, 6);
    }

    [Fact]
    public void Perks_change_the_start_gain_and_drain_and_a_card_gives_it()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var sim = Table(Never, [a, SurvivorUnit(2, new(9, 3))], "war_cry");
        a.Hooks[PerkHooks.AdrenalineStart] = 20;
        a.Hooks[PerkHooks.AdrenalineGain] = 0.5;
        a.Hooks[PerkHooks.AdrenalineDrain] = 0.5;
        Adrenaline.Started(sim);
        Assert.Equal(20, a.Adrenaline, 6);
        Adrenaline.Gain(sim, a, 10);
        Assert.Equal(35, a.Adrenaline, 6);
        Adrenaline.Drain(sim, a, 10);
        Assert.Equal(30, a.Adrenaline, 6);
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "war_cry")).Ok);
        Assert.True(a.Adrenaline > 30);
    }

    [Fact]
    public void The_same_seed_and_plans_give_the_same_stuns()
    {
        List<string> Run()
        {
            var (sim, _, _) = Blocked(Data());
            return sim.State.Events.Select(e => e.Message).ToList();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void A_unit_reeling_this_round_cannot_be_stunned_on_into_the_next()
    {
        var a = SurvivorUnit(1, new(2, 2));
        var b = SurvivorUnit(2, new(3, 2));
        var sim = Arena(10, 5, [a, b]);
        b.StunnedRound = sim.State.Round;
        var count = b.StunCount;
        Assert.False(Stuns.Try(sim, b, 1, a, "test"));
        Assert.Equal(sim.State.Round, b.StunnedRound);
        Assert.Equal(count, b.StunCount);
        b.StunnedRound = sim.State.Round - 1;
        Assert.True(Stuns.Try(sim, b, 1, a, "test"));
        Assert.Equal(sim.State.Round + 1, b.StunnedRound);
    }
}
