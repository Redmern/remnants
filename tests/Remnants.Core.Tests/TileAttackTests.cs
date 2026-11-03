using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Tile attacks: declared on a tile and a direction, struck at the end of the sub-round on whoever stands there then.</summary>
public class TileAttackTests
{
    /// <summary>Every attack lands as a plain hit, so outcomes do not hang on the seed.</summary>
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>P1's <paramref name="weapon"/> at (2,2) against P2 units at <paramref name="foes"/> on an open 10×5 floor; P1 is up.</summary>
    private static (ScavengeSim Sim, Unit A, List<Unit> Foes) Fight(string weapon, params Pos[] foes)
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: weapon);
        var others = foes.Select(p => SurvivorUnit(2, p, weapon: "pipe")).ToList();
        var sim = Arena(10, 5, [a, .. others], data: Sure);
        return (sim, a, others);
    }

    private static void PassAll(ScavengeSim sim, IEnumerable<Unit> foes)
    {
        foreach (var f in foes)
            if (sim.State.CurrentUnitId == f.Id) Assert.True(sim.Execute(new EndTurn(2, f.Id)).Ok);
    }

    /// <summary>Ends the foes' turns until <paramref name="a"/> is up again (its plan resolved), whatever the initiative order.</summary>
    private static void UntilUp(ScavengeSim sim, Unit a)
    {
        for (var i = 0; i < 10 && sim.State.CurrentUnitId != a.Id; i++)
            Assert.True(sim.Execute(new EndTurn(2, sim.State.CurrentUnitId!.Value)).Ok);
        Assert.Equal(a.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void A_tile_attack_pays_now_ends_the_sub_turn_and_strikes_at_the_end_of_the_sub_round()
    {
        var (sim, a, foes) = Fight("sword", new Pos(4, 2));
        var b = foes[0];
        var ap = a.Ap;
        var r = sim.Execute(new AttackTile(1, a.Id, new(3, 2), "blade", StrikeMode.Stab));
        Assert.True(r.Ok, r.Message);
        Assert.Equal(ap - 1, a.Ap);
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
        Assert.Equal(b.MaxHp, b.Hp);
        var strike = Assert.Single(sim.State.Strikes);
        Assert.Equal([new Pos(3, 2), new Pos(4, 2)], strike.Tiles);

        PassAll(sim, foes);
        Assert.True(b.Hp < b.MaxHp);
        Assert.Empty(sim.State.Strikes);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Attacked && e.UnitId == a.Id && e.TargetId == b.Id);
    }

    [Fact]
    public void An_enemy_that_steps_onto_a_covered_tile_is_hit()
    {
        var (sim, a, foes) = Fight("spear", new Pos(7, 2));
        var b = foes[0];
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(5, 2), "thrust")).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(5, 2))).Ok, "walk into the spear");
        PassAll(sim, foes);
        Assert.True(b.Hp < b.MaxHp);
    }

    [Fact]
    public void An_empty_tile_is_a_miss_and_the_one_who_stepped_off_dodged_it()
    {
        var (sim, a, foes) = Fight("knife", new Pos(3, 2));
        var b = foes[0];
        var ap = a.Ap;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(3, 2), "cut", StrikeMode.Stab)).Ok);
        Assert.Equal(ap - 1, a.Ap);
        Assert.True(sim.Execute(new Move(2, b.Id, new(4, 3))).Ok, "step away");
        PassAll(sim, foes);
        Assert.Equal(b.MaxHp, b.Hp);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == a.Id);
        Assert.Equal(a.Id, b.OutplayedId);
    }

    [Fact]
    public void A_stab_runs_through_everyone_on_the_line_a_slash_through_the_arc()
    {
        var (sim, a, foes) = Fight("spear", new Pos(3, 2), new Pos(5, 2), new Pos(6, 2));
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(3, 2), "thrust")).Ok);
        PassAll(sim, foes);
        Assert.True(foes[0].Hp < foes[0].MaxHp);
        Assert.True(foes[1].Hp < foes[1].MaxHp);
        Assert.Equal(foes[2].MaxHp, foes[2].Hp); // past the spear's 3 tiles

        var (sim2, a2, foes2) = Fight("sword", new Pos(3, 1), new Pos(3, 3), new Pos(4, 2));
        Assert.True(sim2.Execute(new AttackTile(1, a2.Id, new(3, 2), "blade", StrikeMode.Slash)).Ok);
        PassAll(sim2, foes2);
        Assert.True(foes2[0].Hp < foes2[0].MaxHp);
        Assert.True(foes2[1].Hp < foes2[1].MaxHp);
        Assert.Equal(foes2[2].MaxHp, foes2[2].Hp); // a slash reaches one tile
    }

    [Fact]
    public void A_friend_on_a_covered_tile_is_not_struck()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "spear");
        var friend = SurvivorUnit(1, new(3, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 2), weapon: "pipe");
        var sim = Arena(10, 5, [a, friend, b], data: Sure);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(4, 2), "thrust")).Ok);
        Assert.True(sim.Execute(new EndTurn(1, friend.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(friend.MaxHp, friend.Hp);
        Assert.True(b.Hp < b.MaxHp);
    }

    [Fact]
    public void A_strike_needs_a_covered_tile_a_mode_the_weapon_has_and_the_ap()
    {
        var (sim, a, _) = Fight("spear", new Pos(7, 2));
        Assert.Equal(ScavengeError.OutOfRange, sim.Execute(new AttackTile(1, a.Id, new(6, 2), "thrust")).Error);
        Assert.Equal(ScavengeError.OutOfRange, sim.Execute(new AttackTile(1, a.Id, new(4, 3), "thrust")).Error);
        Assert.Equal(ScavengeError.InvalidTarget, sim.Execute(new AttackTile(1, a.Id, new(3, 2), "thrust", StrikeMode.Slash)).Error);
        a.Ap = 0;
        Assert.Equal(ScavengeError.NotEnoughAp, sim.Execute(new AttackTile(1, a.Id, new(3, 2), "thrust")).Error);
        Assert.Empty(sim.State.Strikes);
    }

    [Fact]
    public void An_explicit_direction_aims_a_diagonal_slash_through_a_flank_tile()
    {
        var (sim, a, foes) = Fight("sword", new Pos(3, 2), new Pos(2, 3));
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(3, 2), "blade", StrikeMode.Slash, Direction: new Pos(1, 1))).Ok);
        Assert.Equal([new Pos(3, 3), new Pos(3, 2), new Pos(2, 3)], sim.State.Strikes[0].Tiles);
        PassAll(sim, foes);
        Assert.All(foes, f => Assert.True(f.Hp < f.MaxHp));
    }

    [Fact]
    public void A_weapon_without_modes_strikes_the_one_tile_aimed_at()
    {
        var (sim, a, foes) = Fight("pistol", new Pos(6, 2));
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new(6, 2), "shoot")).Ok);
        Assert.Equal([new Pos(6, 2)], sim.State.Strikes[0].Tiles);
        PassAll(sim, foes);
        Assert.True(foes[0].Hp < foes[0].MaxHp);
    }

    [Fact]
    public void A_planned_stab_comes_down_on_every_hostile_on_the_line_from_the_end_of_the_path()
    {
        var (sim, a, foes) = Fight("spear", new Pos(5, 2), new Pos(6, 2));
        var r = sim.Execute(new DeclareIntent(1, a.Id, [new(3, 2)], new Pos(4, 2), "thrust", Mode: StrikeMode.Stab));
        Assert.True(r.Ok, r.Message);
        Assert.Equal(StrikeMode.Stab, a.Plan!.Mode);
        UntilUp(sim, a);

        Assert.Equal(new Pos(3, 2), a.Pos);
        Assert.All(foes, f => Assert.True(f.Hp < f.MaxHp));
    }

    [Fact]
    public void A_planned_slash_keeps_its_mode_and_direction()
    {
        var (sim, a, foes) = Fight("sword", new Pos(4, 1), new Pos(4, 3));
        Assert.True(sim.Execute(new DeclareIntent(1, a.Id, [new(3, 2)], new Pos(4, 2), "blade", Mode: StrikeMode.Slash)).Ok);
        UntilUp(sim, a);
        Assert.All(foes, f => Assert.True(f.Hp < f.MaxHp));
    }

    [Fact]
    public void A_plan_needs_a_mode_the_weapon_has_and_a_covered_tile()
    {
        var (sim, a, _) = Fight("spear", new Pos(7, 2));
        Assert.Equal(ScavengeError.InvalidTarget, sim.Execute(new DeclareIntent(1, a.Id, [], new Pos(3, 2), "thrust", Mode: StrikeMode.Slash)).Error);
        Assert.Equal(ScavengeError.OutOfRange, sim.Execute(new DeclareIntent(1, a.Id, [], new Pos(4, 3), "thrust")).Error);
        Assert.Null(a.Plan);
    }
}
