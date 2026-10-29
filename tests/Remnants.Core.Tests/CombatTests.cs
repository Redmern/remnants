using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class CombatTests
{
    private static ActionProfile Action(Unit u, string id) => ProfileDeriver.Derive(Real, u).Action(id)!;

    [Fact]
    public void Reach_follows_the_attack_pattern()
    {
        var map = new TileMap(12, 12);
        var spear = Action(SurvivorUnit(weapon: "spear"), "thrust");    // line, reach 2
        var pipe = Action(SurvivorUnit(weapon: "pipe"), "swing");       // melee, reach 1
        var pistol = Action(SurvivorUnit(weapon: "pistol"), "shoot");   // ranged, reach 6
        var o = new Pos(5, 5);

        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, o, spear, new(7, 7)));
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, o, spear, new(7, 6)));
        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, o, pipe, new(6, 6)));
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, o, pipe, new(7, 5)));
        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, o, pistol, new(9, 8)));   // 5 tiles
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, o, pistol, new(10, 9)));
    }

    [Fact]
    public void Ranged_attacks_need_line_of_sight()
    {
        var map = new TileMap(8, 3);
        map[new Pos(3, 1)] = Terrain.Wall;
        var pistol = Action(SurvivorUnit(weapon: "pistol"), "shoot");
        Assert.Equal(ScavengeError.NoLineOfSight, Combat.CheckReach(Real, map, new(1, 1), pistol, new(5, 1)));
    }

    [Fact]
    public void Cover_between_attacker_and_target_lowers_hit_chance()
    {
        var map = new TileMap(10, 3);
        var shooter = SurvivorUnit(weapon: "pistol");
        var shot = Action(shooter, "shoot");
        var target = ProfileDeriver.Derive(Real, SurvivorUnit());
        var open = Combat.HitChance(Real, map, new(1, 1), shot, new(5, 1), target);

        map[new Pos(4, 1)] = Terrain.Cover;
        Assert.Equal(open - 0.3, Combat.HitChance(Real, map, new(1, 1), shot, new(5, 1), target), 9);
        // From the other side the cover does nothing.
        Assert.Equal(open, Combat.HitChance(Real, map, new(9, 1), shot, new(5, 1), target), 9);
    }

    [Fact]
    public void Hit_chance_is_clamped()
    {
        var map = new TileMap(10, 3);
        var shot = Action(SurvivorUnit(weapon: "pistol"), "shoot");
        var dodgy = SurvivorUnit(body: "scout_jacket");
        dodgy.Attributes[AttributeKind.Agility] = 100;
        map[new Pos(5, 1)] = Terrain.Wall;
        var chance = Combat.HitChance(Real, map, new(0, 0), shot with { Accuracy = 0.1 }, new(6, 1), ProfileDeriver.Derive(Real, dodgy));
        Assert.Equal(Real.Rules.Combat.MinHitChance, chance);
        Assert.Equal(Real.Rules.Combat.MaxHitChance,
            Combat.HitChance(Real, map, new(0, 0), shot with { Accuracy = 5 }, new(1, 1), ProfileDeriver.Derive(Real, SurvivorUnit())));
    }

    [Fact]
    public void Damage_injures_the_hit_part_and_lowers_hp()
    {
        var u = SurvivorUnit();
        var sim = Arena(3, 3, [u]);
        Combat.ApplyDamage(sim, u, BodyPart.LeftLeg, 20);
        Assert.Equal(80, u.Hp);
        Assert.Equal(PartCondition.Injured, u.Body[BodyPart.LeftLeg].Condition);
        Assert.Equal(0.5, u.Body[BodyPart.LeftLeg].Severity, 9); // leg toughness 40
        Assert.Equal(1, ProfileDeriver.Derive(Real, u).Movement.Primary.Range); // 2 * 0.75 legs
    }

    [Fact]
    public void Survivor_at_zero_hp_is_downed_then_bleeds_out_and_dies()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var other = SurvivorUnit(pos: new(2, 2));
        var sim = Arena(3, 3, [other, u]);
        Combat.ApplyDamage(sim, u, BodyPart.Torso, 100);
        Assert.Equal(UnitStatus.Downed, u.Status);
        Assert.Equal(Real.Rules.Combat.BleedOutRounds, u.BleedOut);

        for (var r = 0; r < Real.Rules.Combat.BleedOutRounds; r++)
        {
            Assert.Equal(UnitStatus.Downed, u.Status);
            Assert.True(sim.Execute(new EndTurn(1, other.Id)).Ok);
        }
        Assert.Equal(UnitStatus.Dead, u.Status);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.BledOut && e.UnitId == u.Id);
    }

    [Fact]
    public void Massive_damage_kills_outright_and_hitting_the_downed_kills()
    {
        var a = SurvivorUnit();
        var b = SurvivorUnit();
        var sim = Arena(3, 3, [a, b]);
        Combat.ApplyDamage(sim, a, BodyPart.Torso, 150);
        Assert.Equal(UnitStatus.Dead, a.Status);

        Combat.ApplyDamage(sim, b, BodyPart.Torso, 100);
        Assert.Equal(UnitStatus.Downed, b.Status);
        Combat.ApplyDamage(sim, b, BodyPart.Torso, 1);
        Assert.Equal(UnitStatus.Dead, b.Status);
    }

    [Fact]
    public void Enemies_die_at_zero_and_drop_only_lootable_gear()
    {
        var linked = EnemyUnit("linked", new(1, 1));
        var sim = Arena(3, 3, [SurvivorUnit(pos: new(0, 0)), linked]);
        Combat.ApplyDamage(sim, linked, BodyPart.Torso, 999);
        Assert.Equal(UnitStatus.Dead, linked.Status);
        var cache = Assert.Single(sim.State.Caches);
        Assert.Equal(["linked_carbine"], cache.Items); // plating has no loot weight
    }

    [Fact]
    public void Shattered_limbs_can_be_lost()
    {
        var lost = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var u = SurvivorUnit();
            u.Hp = 1000;
            var sim = Arena(3, 3, [u], seed: seed);
            Combat.ApplyDamage(sim, u, BodyPart.RightArm, 60);
            if (u.Body[BodyPart.RightArm].Condition == PartCondition.Missing) lost++;
            else Assert.Equal(1.0, u.Body[BodyPart.RightArm].Severity);
        }
        Assert.InRange(lost, 5, 35); // limbLossChance 0.5
    }

    [Fact]
    public void Armor_soaks_damage()
    {
        var target = SurvivorUnit(pos: new(1, 0), body: "riot_armor");
        var attacker = SurvivorUnit(2, new(0, 0), weapon: "pipe");
        var sim = Arena(3, 1, [attacker, target]);
        var hits = 0;
        while (hits == 0)
        {
            attacker.Ap = 3;
            Assert.True(sim.Execute(new Attack(2, attacker.Id, target.Id, "swing")).Ok);
            hits = sim.State.Events.Count(e => e.Type == ScavengeEventType.Attacked);
        }
        Assert.InRange(100 - target.Hp, 1, 12 - 6);
    }
}
