using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class InjuryEffectTests
{
    [Fact]
    public void Injured_legs_shrink_any_move_reach()
    {
        var body = new Body();
        Assert.Equal(4, ProfileDeriver.LegReach(4, body));
        body.Injure(BodyPart.LeftLeg, 0.5); // legs at 0.75
        Assert.Equal(3, ProfileDeriver.LegReach(4, body));
        Assert.Equal(1, ProfileDeriver.LegReach(2, body));
        body.Injure(BodyPart.RightLeg, 1);
        Assert.Equal(1, ProfileDeriver.LegReach(4, body));
    }

    [Fact]
    public void The_walk_uses_the_same_leg_reach()
    {
        var u = SurvivorUnit();
        var healthy = ProfileDeriver.Derive(Real, u).Movement.Primary.Range;
        u.Body.Injure(BodyPart.LeftLeg, 0.5);
        Assert.Equal(ProfileDeriver.LegReach(healthy, u.Body), ProfileDeriver.Derive(Real, u).Movement.Primary.Range);
    }

    [Fact]
    public void Badly_hurt_arms_lock_arm_heavy_moves()
    {
        var below = Real.Rules.Combat.ArmLockBelow;
        var body = new Body();
        Assert.False(ProfileDeriver.ArmsLocked(Real, body, twoHanded: false));

        body.Injure(BodyPart.RightArm, 1 - below + 0.1);
        Assert.False(ProfileDeriver.ArmsLocked(Real, body, twoHanded: false)); // the good arm does it
        Assert.False(ProfileDeriver.ArmsLocked(Real, body, twoHanded: true));               // both arms average out

        body.Injure(BodyPart.LeftArm, 1 - below + 0.1);
        Assert.True(ProfileDeriver.ArmsLocked(Real, body, twoHanded: false));
        Assert.True(ProfileDeriver.ArmsLocked(Real, body, twoHanded: true));
    }

    [Fact]
    public void An_arm_locked_attack_drops_out_while_the_arms_are_hurt()
    {
        var club = new ItemDef
        {
            Id = "test_maul", Name = "Test Maul", Kind = ItemKind.Weapon,
            Actions = [new ActionDef { Id = "smash", Name = "Smash", MinDamage = 5, MaxDamage = 9, ArmLock = true },
                       new ActionDef { Id = "jab", Name = "Jab", MinDamage = 1, MaxDamage = 3 }],
        };
        var data = new ScavengeData(Real.Items.Values.Where(i => i.BaseId is null).Append(club), Real.Enemies.Values, Real.Zones.Values, Real.Rules,
            Real.PerkHooks, Real.Cybernetics?.Def, Real.Schematics, Real.Electronics, medicine: Real.Medicine?.Def);
        var u = SurvivorUnit(weapon: "test_maul");
        Assert.NotNull(ProfileDeriver.Derive(data, u).Action("smash"));

        u.Body.Injure(BodyPart.LeftArm, 0.9);
        u.Body.Injure(BodyPart.RightArm, 0.9);
        var hurt = ProfileDeriver.Derive(data, u);
        Assert.Null(hurt.Action("smash"));
        Assert.NotNull(hurt.Action("jab"));
    }

    [Fact]
    public void Injuries_cost_no_ap()
    {
        var u = SurvivorUnit();
        var healthy = ProfileDeriver.Derive(Real, u).MaxAp;
        foreach (var part in new[] { BodyPart.LeftLeg, BodyPart.RightLeg, BodyPart.LeftArm, BodyPart.RightArm, BodyPart.Torso })
            u.Body.Injure(part, 0.8);
        Assert.Equal(healthy, ProfileDeriver.Derive(Real, u).MaxAp);
    }

    [Fact]
    public void Mending_lowers_the_worst_injury_and_heals_a_light_one()
    {
        var body = new Body();
        body.Injure(BodyPart.LeftLeg, 0.6);
        body.Injure(BodyPart.LeftArm, 0.1);
        Assert.NotNull(Treatment.Mend(body, 0.2));
        Assert.Equal(0.4, body[BodyPart.LeftLeg].Severity, 9);
        Assert.Equal(0.1, body[BodyPart.LeftArm].Severity, 9);

        var light = new Body();
        light.Injure(BodyPart.RightArm, 0.15);
        Treatment.Mend(light, 0.2);
        Assert.Equal(PartCondition.Healthy, light[BodyPart.RightArm].Condition);
        Assert.Null(Treatment.Mend(new Body(), 0.2));
    }

    [Fact]
    public void A_field_item_mends_an_injury_during_a_run()
    {
        var kit = Real.Items.Values.First(i => i.Use is { Mend: > 0 });
        var medic = SurvivorUnit(pos: new(0, 0), pack: kit.Id);
        var hurt = SurvivorUnit(pos: new(1, 0));
        var sim = Arena(3, 3, [medic, hurt]);
        hurt.Body.Injure(BodyPart.RightLeg, 0.7);
        Assert.True(sim.Execute(new UseItem(1, medic.Id, kit.Id, hurt.Id)).Ok);
        Assert.Equal(0.7 - kit.Use!.Mend, hurt.Body[BodyPart.RightLeg].Severity, 9);
    }
}
