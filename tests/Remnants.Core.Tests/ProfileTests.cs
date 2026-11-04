using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>MovementProfile and ActionProfiles derive from attributes + carried gear + body state.</summary>
public class MovementProfileTests
{
    private static UnitProfile Derive(Unit u) => ProfileDeriver.Derive(Real, u);

    private static Unit WithAgility(double agility, string? body = null, string? tool = null)
    {
        var u = SurvivorUnit(body: body, tool: tool);
        u.Attributes[AttributeKind.Agility] = agility;
        return u;
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(50, 2)]
    [InlineData(65, 3)]
    [InlineData(95, 4)]
    public void Base_walk_is_king_pattern_with_range_from_agility(double agility, int range)
    {
        var move = Derive(WithAgility(agility)).Movement;
        Assert.Single(move.Modes);
        Assert.Equal(new MoveMode(MovePattern.King, range, 1, false), move.Primary);
    }

    [Fact]
    public void Heavy_armor_moves_one_tile_any_direction_like_a_king()
    {
        Assert.Equal((MovePattern.King, 1), (Derive(WithAgility(95, body: "riot_armor")).Movement.Primary.Pattern,
                                             Derive(WithAgility(95, body: "riot_armor")).Movement.Primary.Range));
    }

    [Fact]
    public void Scout_jacket_moves_further_but_only_orthogonally_like_a_rook()
    {
        var primary = Derive(WithAgility(65, body: "scout_jacket")).Movement.Primary;
        Assert.Equal(MovePattern.Rook, primary.Pattern);
        Assert.Equal(4, primary.Range);
    }

    [Fact]
    public void Grappling_hook_adds_a_knight_jump_that_needs_working_arms()
    {
        var u = WithAgility(50, tool: "grappling_hook");
        var modes = Derive(u).Movement.Modes;
        Assert.Contains(modes, m => m is { Pattern: MovePattern.Knight, IgnoresTerrain: true, ApCost: 4 });

        u.Body.Injure(BodyPart.LeftArm, 0.8);
        u.Body.Injure(BodyPart.RightArm, 0.8);
        Assert.DoesNotContain(Derive(u).Movement.Modes, m => m.Pattern == MovePattern.Knight);
    }

    [Fact]
    public void Missing_leg_forces_a_one_tile_king_walk_even_in_scout_gear()
    {
        var u = WithAgility(95, body: "scout_jacket");
        u.Body.Lose(BodyPart.LeftLeg);
        Assert.Equal(new MoveMode(MovePattern.King, 1, 1, false, "missing legs"), Derive(u).Movement.Primary);
    }

    [Fact]
    public void Modes_name_what_gave_them_so_the_ui_can_explain_movement()
    {
        Assert.Null(Derive(WithAgility(50)).Movement.Primary.Source);
        Assert.Equal("Scout Jacket", Derive(WithAgility(50, body: "scout_jacket")).Movement.Primary.Source);
        var knight = Derive(WithAgility(50, tool: "grappling_hook")).Movement.Modes.Single(m => m.Pattern == MovePattern.Knight);
        Assert.Equal("Grappling Hook", knight.Source);
    }

    [Fact]
    public void Injured_legs_shorten_the_walk()
    {
        var u = WithAgility(95);
        Assert.Equal(4, Derive(u).Movement.Primary.Range);
        u.Body.Injure(BodyPart.LeftLeg, 0.5);
        Assert.Equal(3, Derive(u).Movement.Primary.Range);
        u.Body.Injure(BodyPart.RightLeg, 0.9);
        Assert.Equal(1, Derive(u).Movement.Primary.Range);
    }

    [Fact]
    public void A_prosthetic_leg_restores_movement_by_its_quality()
    {
        var u = WithAgility(95);
        u.Body.Lose(BodyPart.LeftLeg);
        Assert.Equal(1, Derive(u).Movement.Primary.Range);

        u.Body.FitProsthetic(BodyPart.LeftLeg, 0.5);
        Assert.Equal(3, Derive(u).Movement.Primary.Range); // 4 * 0.75

        u.Body.Lose(BodyPart.LeftLeg);
        u.Body.FitProsthetic(BodyPart.LeftLeg, 1.0);
        Assert.Equal(4, Derive(u).Movement.Primary.Range);
    }

    [Fact]
    public void Two_cybernetic_legs_beat_the_original()
    {
        var u = WithAgility(50);
        foreach (var leg in new[] { BodyPart.LeftLeg, BodyPart.RightLeg })
        {
            u.Body.Lose(leg);
            u.Body.FitProsthetic(leg, 1.3);
        }
        Assert.Equal(3, Derive(u).Movement.Primary.Range); // floor(2 * 1.3) + 1 from the body rule
    }

    [Fact]
    public void Heavy_load_costs_range()
    {
        var u = WithAgility(95);
        var capacity = Derive(u).CarryCapacity;
        u.Pack.AddRange(Enumerable.Repeat("scrap", (int)Math.Ceiling(capacity * 0.6)));
        Assert.Equal(3, Derive(u).Movement.Primary.Range);
        u.Pack.AddRange(Enumerable.Repeat("scrap", (int)Math.Ceiling(capacity * 0.3)));
        Assert.Equal(2, Derive(u).Movement.Primary.Range);
    }

    [Fact]
    public void Climbing_gear_makes_rubble_and_cover_cheap()
    {
        Assert.Equal(2, Derive(SurvivorUnit()).Movement.Cost(Terrain.Rubble));
        var gear = Derive(SurvivorUnit(tool: "climbing_gear")).Movement;
        Assert.Equal(1, gear.Cost(Terrain.Rubble));
        Assert.Equal(1, gear.Cost(Terrain.Cover));
        Assert.Null(gear.Cost(Terrain.Wall));
    }

    [Fact]
    public void Data_drives_the_derivation()
    {
        // Balancing is a data edit: a jacket that sets a bishop pattern changes movement with no code change.
        var bishopCoat = new ItemDef
        {
            Id = "bishop_coat", Name = "Bishop Coat", Kind = ItemKind.Clothing,
            Movement = new MovementModifier { SetPattern = MovePattern.Bishop, RangeBonus = 2 },
        };
        var data = new ScavengeData(Real.Items.Values.Append(bishopCoat), Real.Enemies.Values, Real.Zones.Values, Real.Rules);
        var u = WithAgility(50, body: "bishop_coat");
        Assert.Equal(new MoveMode(MovePattern.Bishop, 4, 1, false, "Bishop Coat"), ProfileDeriver.Derive(data, u).Movement.Primary);
    }
}

public class ActionProfileTests
{
    private static UnitProfile Derive(Unit u) => ProfileDeriver.Derive(Real, u);

    [Fact]
    public void Bare_hands_are_always_an_option()
    {
        var actions = Derive(SurvivorUnit()).Actions;
        Assert.Equal(["punch"], actions.Select(a => a.Id));
        Assert.Contains(Derive(SurvivorUnit(weapon: "pipe")).Actions, a => a.Id == "punch");
    }

    [Fact]
    public void Weapon_actions_take_reach_pattern_and_ap_from_data()
    {
        var actions = Derive(SurvivorUnit(weapon: "rifle")).Actions;
        var shot = actions.Single(a => a.Id == "aimed_shot");
        Assert.Equal((AttackPattern.Ranged, 10, 2, "rifle"), (shot.Pattern, shot.Reach, shot.ApCost, shot.ItemId));
        Assert.Contains(actions, a => a.Id == "butt_strike");
    }

    [Fact]
    public void Skill_and_perception_raise_accuracy()
    {
        var plain = SurvivorUnit(weapon: "pistol");
        var trained = SurvivorUnit(weapon: "pistol");
        trained.Skills[Skill.Firearms] = 80;
        var sharp = SurvivorUnit(weapon: "pistol");
        sharp.Attributes[AttributeKind.Perception] = 90;

        var baseline = Derive(plain).Action("shoot")!.Accuracy;
        Assert.Equal(0.65, baseline, 9);
        Assert.Equal(0.65 + 80 * 0.003, Derive(trained).Action("shoot")!.Accuracy, 9);
        Assert.Equal(0.65 + 40 * 0.002, Derive(sharp).Action("shoot")!.Accuracy, 9);
    }

    [Fact]
    public void Strength_scales_melee_damage()
    {
        var strong = SurvivorUnit(weapon: "pipe");
        strong.Attributes[AttributeKind.Strength] = 100;
        var swing = Derive(strong).Action("swing")!;
        Assert.Equal(6 * 1.3, swing.MinDamage, 9);
        Assert.Equal(12 * 1.3, swing.MaxDamage, 9);
    }

    [Fact]
    public void Two_handed_weapons_need_both_arms()
    {
        var u = SurvivorUnit(weapon: "rifle");
        u.Body.Lose(BodyPart.LeftArm);
        var actions = Derive(u).Actions.Select(a => a.Id).ToList();
        Assert.DoesNotContain("aimed_shot", actions);
        Assert.Contains("butt_strike", actions);
    }

    [Fact]
    public void Injured_arm_hurts_two_handed_accuracy_but_one_handed_uses_the_good_arm()
    {
        var u = SurvivorUnit(weapon: "rifle");
        var before = Derive(u).Action("aimed_shot")!.Accuracy;
        var meleeBefore = Derive(u).Action("butt_strike")!.Accuracy;
        u.Body.Injure(BodyPart.LeftArm, 0.5);
        Assert.Equal(before * 0.75, Derive(u).Action("aimed_shot")!.Accuracy, 9);
        Assert.Equal(meleeBefore, Derive(u).Action("butt_strike")!.Accuracy, 9);
    }

    [Fact]
    public void Head_injury_hurts_ranged_aim_and_sight()
    {
        var u = SurvivorUnit(weapon: "pistol");
        var aim = Derive(u).Action("shoot")!.Accuracy;
        var sight = Derive(u).Sight;
        u.Body.Injure(BodyPart.Head, 0.5);
        Assert.Equal(aim * 0.5, Derive(u).Action("shoot")!.Accuracy, 9);
        Assert.Equal(sight * 0.5, Derive(u).Sight, 9);
    }

    [Fact]
    public void Gear_sets_armor_dodge_initiative_ap_sight_and_carry()
    {
        var bare = Derive(SurvivorUnit());
        Assert.Equal(2, bare.MaxAp);
        Assert.Equal(4 + 50 * 0.06, bare.Sight, 9);
        Assert.Equal(10 + 50 * 0.3, bare.CarryCapacity, 9);

        Assert.Equal(6, Derive(SurvivorUnit(body: "riot_armor")).Armor);
        Assert.True(Derive(SurvivorUnit(body: "riot_armor")).Initiative < bare.Initiative);
        Assert.True(Derive(SurvivorUnit(body: "scout_jacket")).Initiative > bare.Initiative);
        Assert.Equal(0.05, Derive(SurvivorUnit(body: "scout_jacket")).Dodge, 9);
        Assert.Equal(bare.Sight + 3, Derive(SurvivorUnit(tool: "binoculars")).Sight, 9);
        Assert.Equal(bare.CarryCapacity + 15, Derive(SurvivorUnit(tool: "backpack")).CarryCapacity, 9);
    }

    [Fact]
    public void Enemies_derive_from_their_definition_the_same_way()
    {
        var husk = Derive(EnemyUnit("husk", default));
        Assert.Contains(husk.Actions, a => a.Id == "claw");
        Assert.Equal(MovePattern.King, husk.Movement.Primary.Pattern);

        var linked = Derive(EnemyUnit("linked", default));
        Assert.Contains(linked.Actions, a => a is { Id: "burst", Pattern: AttackPattern.Ranged });
        Assert.Equal(4, linked.Armor);
    }
}
