using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class HitTierTests
{
    private static readonly HitTierRules Tiers = Real.Rules.Combat.Tiers;

    /// <summary>Data where every attack lands and resolves at the given margins.</summary>
    private static ScavengeData Landing(double grazeBelow, double solidFrom) =>
        With(r => r with
        {
            Combat = r.Combat with
            {
                MinHitChance = 1,
                MaxHitChance = 1,
                Tiers = r.Combat.Tiers with { GrazeBelow = grazeBelow, SolidFrom = solidFrom },
            },
        });

    [Fact]
    public void The_roll_margin_picks_the_tier()
    {
        // A 60% shot: the share of the chance the roll came in under.
        Assert.Equal(HitTier.Graze, Combat.TierOf(Tiers, 0.6, 0.6 * (1 - Tiers.GrazeBelow) + 1e-6, 0, 0));
        Assert.Equal(HitTier.Hit, Combat.TierOf(Tiers, 0.6, 0.6 * (1 - Tiers.GrazeBelow) - 1e-6, 0, 0));
        Assert.Equal(HitTier.Hit, Combat.TierOf(Tiers, 0.6, 0.6 * (1 - Tiers.SolidFrom) + 1e-6, 0, 0));
        Assert.Equal(HitTier.Solid, Combat.TierOf(Tiers, 0.6, 0.6 * (1 - Tiers.SolidFrom) - 1e-6, 0, 0));
        Assert.Equal(HitTier.Solid, Combat.TierOf(Tiers, 0.6, 0, 0, 0));
    }

    [Fact]
    public void Armor_and_cover_each_shift_a_hit_down_a_tier_but_never_below_a_graze()
    {
        Assert.Equal(HitTier.Hit, Combat.TierOf(Tiers, 1, 0, Tiers.ArmorShiftFrom, 0));
        Assert.Equal(HitTier.Hit, Combat.TierOf(Tiers, 1, 0, 0, Tiers.CoverShiftFrom));
        Assert.Equal(HitTier.Graze, Combat.TierOf(Tiers, 1, 0, Tiers.ArmorShiftFrom, Tiers.CoverShiftFrom));
        Assert.Equal(HitTier.Graze, Combat.TierOf(Tiers, 1, 0.99, Tiers.ArmorShiftFrom, Tiers.CoverShiftFrom));
        // Light armor and thin cover do not shift.
        Assert.Equal(HitTier.Solid, Combat.TierOf(Tiers, 1, 0, Tiers.ArmorShiftFrom - 0.5, Tiers.CoverShiftFrom - 0.05));
    }

    [Fact]
    public void Tier_numbers_come_from_the_data()
    {
        Assert.InRange(Tiers.GrazeBelow, 0.01, 0.99);
        Assert.InRange(Tiers.SolidFrom, Tiers.GrazeBelow, 0.99);
        Assert.InRange(Tiers.GrazeDamage, 0.01, 0.99);
        Assert.True(Tiers.SolidSeverity > 1);
    }

    [Fact]
    public void A_graze_takes_a_little_hp_and_injures_no_body_part()
    {
        var (grazed, grazeLoss) = Punch(Landing(grazeBelow: 1, solidFrom: 1));
        var (hit, hitLoss) = Punch(Landing(grazeBelow: 0, solidFrom: 1));
        Assert.All(grazed.Body.Parts.Values, p => Assert.Equal(PartCondition.Healthy, p.Condition));
        Assert.Contains(hit.Body.Parts.Values, p => p.Condition == PartCondition.Injured);
        Assert.Equal(Math.Max(1, hitLoss * Tiers.GrazeDamage), grazeLoss, 9);
    }

    [Fact]
    public void A_solid_hit_does_the_same_damage_with_worse_injury()
    {
        var (hit, hitLoss) = Punch(Landing(grazeBelow: 0, solidFrom: 1));
        var (solid, solidLoss) = Punch(Landing(grazeBelow: 0, solidFrom: 0));
        Assert.Equal(hitLoss, solidLoss, 9);
        var part = hit.Body.Parts.Single(kv => kv.Value.Condition == PartCondition.Injured).Key;
        Assert.Equal(hit.Body[part].Severity * Tiers.SolidSeverity, solid.Body[part].Severity, 9);
    }

    [Fact]
    public void Armor_turns_a_solid_hit_into_a_plain_one()
    {
        var solid = Landing(grazeBelow: 0, solidFrom: 0);
        var ignoresArmor = With(r => r with { Combat = solid.Rules.Combat with { Tiers = solid.Rules.Combat.Tiers with { ArmorShiftFrom = 99 } } });
        var (shifted, shiftedLoss) = Punch(solid, body: "linked_plating");
        var (unshifted, unshiftedLoss) = Punch(ignoresArmor, body: "linked_plating");
        Assert.Equal(unshiftedLoss, shiftedLoss, 9);
        var part = unshifted.Body.Parts.Single(kv => kv.Value.Condition == PartCondition.Injured).Key;
        Assert.Equal(unshifted.Body[part].Severity / Tiers.SolidSeverity, shifted.Body[part].Severity, 9);
    }

    [Fact]
    public void A_graze_through_heavy_armor_still_takes_1_hp()
    {
        var (_, loss) = Punch(Landing(grazeBelow: 1, solidFrom: 1), body: "riot_armor"); // punch 2-5 against armor 6
        Assert.Equal(1, loss, 9);
    }

    [Fact]
    public void Cover_beside_the_target_turns_a_solid_shot_into_a_plain_one()
    {
        var data = Landing(grazeBelow: 0, solidFrom: 0);
        var (open, openLoss) = Shoot(data, cover: false);
        var (covered, coveredLoss) = Shoot(data, cover: true);
        Assert.Equal(openLoss, coveredLoss, 9);
        var part = open.Body.Parts.Single(kv => kv.Value.Condition == PartCondition.Injured).Key;
        Assert.Equal(open.Body[part].Severity / Tiers.SolidSeverity, covered.Body[part].Severity, 9);
    }

    [Fact]
    public void The_log_names_the_tier()
    {
        var a = SurvivorUnit(pos: new(0, 0));
        var b = SurvivorUnit(playerId: 2, pos: new(1, 0));
        var sim = Arena(3, 3, [a, b], data: Landing(grazeBelow: 1, solidFrom: 1));
        Assert.True(sim.Execute(new Attack(1, a.Id, b.Id, "punch")).Ok);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Attacked && e.Message.Contains("grazed"));
    }

    /// <summary>One pistol shot from 3 tiles, same seed every time, with or without Cover beside the target.</summary>
    private static (Unit Target, double Loss) Shoot(ScavengeData data, bool cover)
    {
        var a = SurvivorUnit(pos: new(0, 1), weapon: "pistol");
        var b = SurvivorUnit(playerId: 2, pos: new(3, 1));
        var sim = Arena(5, 3, [a, b], rows: [".....", cover ? "..c.." : ".....", "....."], data: data, seed: 7);
        Assert.True(Combat.CoverAgainst(data, sim.State.Map, b.Pos, a.Pos) + 1e-9 >= Tiers.CoverShiftFrom == cover);
        Assert.True(sim.Execute(new Attack(1, a.Id, b.Id, "shoot")).Ok);
        return (b, b.MaxHp - b.Hp);
    }

    /// <summary>One punch, same seed every time; returns the target and the HP it lost.</summary>
    private static (Unit Target, double Loss) Punch(ScavengeData data, string? body = null)
    {
        var a = SurvivorUnit(pos: new(0, 0));
        var b = SurvivorUnit(playerId: 2, pos: new(1, 0), body: body);
        var sim = Arena(3, 3, [a, b], data: data, seed: 7);
        Assert.True(sim.Execute(new Attack(1, a.Id, b.Id, "punch")).Ok);
        return (b, b.MaxHp - b.Hp);
    }
}
