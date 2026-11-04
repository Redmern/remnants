using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Separate MP and AP, both draining one stamina pool that regenerates each round (<see cref="Economy"/>).</summary>
public class StaminaEconomyTests
{
    private static ScavengeSim Solo(out Unit unit, Pos? at = null, string? weapon = "pipe", string? body = null)
    {
        unit = SurvivorUnit(1, at ?? new Pos(2, 2), weapon: weapon, body: body);
        return Arena(12, 12, [unit]);
    }

    [Fact]
    public void A_baseline_survivor_has_the_default_pools()
    {
        var sim = Solo(out var u);
        var p = sim.Profile(u);
        Assert.Equal(4, p.MaxMp);
        Assert.Equal(1, p.MaxAp);
        Assert.Equal(10, p.MaxStamina);
        Assert.Equal(4, u.Mp);
        Assert.Equal(1, u.Ap);
        Assert.Equal(10, Economy.Stamina(sim, u));
    }

    [Fact]
    public void The_defaults_live_in_the_rules()
    {
        var data = With(r => r with { Turn = r.Turn with { BaseMp = 6, BaseAp = 3, BaseStamina = 14 } });
        var u = SurvivorUnit(1, new Pos(2, 2));
        var sim = Arena(12, 12, [u], data: data);
        var p = sim.Profile(u);
        Assert.Equal((6, 3, 14), (p.MaxMp, p.MaxAp, p.MaxStamina));
    }

    [Fact]
    public void An_enemy_type_can_override_its_pools()
    {
        var data = With(r => r);
        var def = data.Enemies["husk"] with { Mp = 7, Ap = 1, Stamina = 20 };
        data = data.WithEnemy(def);
        var husk = EnemyUnit("husk", new Pos(2, 2));
        var sim = Arena(12, 12, [SurvivorUnit(1, new Pos(9, 9)), husk], data: data);
        var p = sim.Profile(husk);
        Assert.Equal((7, 1, 20), (p.MaxMp, p.MaxAp, p.MaxStamina));
    }

    [Fact]
    public void Agility_and_load_shift_the_movement_pool()
    {
        var quick = SurvivorUnit(1, new Pos(2, 2), attributes: 60);
        var sim = Arena(12, 12, [quick]);
        Assert.True(sim.Profile(quick).MaxMp > 4);
    }

    [Fact]
    public void A_move_pays_mp_per_terrain_cost_and_one_stamina_per_mp()
    {
        var sim = Solo(out var u);
        Assert.True(Now(sim, new Move(1, u.Id, new Pos(5, 2))).Ok);
        Assert.Equal(1, u.Mp);
        Assert.Equal(1, u.Ap);
        Assert.Equal(7, Economy.Stamina(sim, u));
    }

    [Fact]
    public void A_move_past_the_remaining_mp_is_refused()
    {
        var sim = Solo(out var u);
        var result = Now(sim, new Move(1, u.Id, new Pos(7, 2)));
        Assert.False(result.Ok);
        Assert.Equal(new Pos(2, 2), u.Pos);
        Assert.Equal(4, u.Mp);
    }

    [Fact]
    public void An_ap_spent_costs_two_stamina()
    {
        var sim = Solo(out var u);
        Assert.True(sim.Execute(new Lookout(1, u.Id)).Ok);
        Assert.Equal(0, u.Ap);
        Assert.Equal(8, Economy.Stamina(sim, u));
    }

    [Fact]
    public void Stamina_regenerates_about_forty_percent_at_round_start()
    {
        var sim = Solo(out var u);
        u.Fatigue = 8;
        Economy.RoundStarted(sim, u);
        Assert.Equal(6, Economy.Stamina(sim, u));
        Assert.Equal(4, u.Mp);
        Assert.Equal(1, u.Ap);
    }

    [Fact]
    public void Regen_never_goes_past_the_maximum()
    {
        var sim = Solo(out var u);
        u.Fatigue = 1;
        Economy.RoundStarted(sim, u);
        Assert.Equal(10, Economy.Stamina(sim, u));
    }

    [Fact]
    public void A_heavy_load_cuts_regen_but_not_below_one()
    {
        var sim = Solo(out var light);
        var heavy = SurvivorUnit(1, new Pos(4, 4), pack: Enumerable.Repeat("scrap", 8).ToArray());
        var sim2 = Arena(12, 12, [heavy]);
        Assert.True(sim2.Profile(heavy).Load > 0);
        light.Fatigue = 9;
        heavy.Fatigue = 9;
        Economy.RoundStarted(sim, light);
        Economy.RoundStarted(sim2, heavy);
        Assert.True(Economy.Regen(sim2, heavy) < Economy.Regen(sim, light));
        Assert.True(Economy.Regen(sim2, heavy) >= 1);
    }

    [Fact]
    public void Recover_spends_ap_and_gives_stamina_back()
    {
        var sim = Solo(out var u);
        u.Fatigue = 6;
        var rules = sim.Data.Rules.Turn;
        Assert.True(sim.Execute(new Recover(1, u.Id)).Ok);
        Assert.Equal(1 - rules.RecoverApCost, u.Ap);
        Assert.Equal(4, Economy.Stamina(sim, u));
        Settle(sim);
        Assert.Equal(4 + rules.RecoverStamina, Economy.Stamina(sim, u));
    }

    [Fact]
    public void Running_out_of_stamina_ends_actions_and_halves_the_mp_left()
    {
        var sim = Solo(out var u);
        u.Fatigue = 8;
        Assert.True(sim.Execute(new Lookout(1, u.Id)).Ok);
        Assert.Equal(0, Economy.Stamina(sim, u));
        Assert.Equal(0, u.Ap);
        Assert.Equal(2, u.Mp);
        Assert.False(sim.Execute(new Recover(1, u.Id)).Ok);
    }

    [Fact]
    public void Starting_a_round_at_zero_stamina_means_no_actions_and_half_mp()
    {
        var data = With(r => r with { Turn = r.Turn with { StaminaRegen = 0, MinStaminaRegen = 0 } });
        var u = SurvivorUnit(1, new Pos(2, 2));
        var sim = Arena(12, 12, [u], data: data);
        u.Fatigue = 10;
        Economy.RoundStarted(sim, u);
        Assert.Equal(0, u.Ap);
        Assert.Equal(2, u.Mp);
        Assert.True(Now(sim, new Move(1, u.Id, new Pos(4, 2))).Ok);
        Assert.Equal(new Pos(4, 2), u.Pos);
    }
}
