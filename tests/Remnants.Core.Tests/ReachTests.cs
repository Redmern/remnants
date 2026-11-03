using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class ReachTests
{
    private static ActionProfile Action(Unit u, string id) => ProfileDeriver.Derive(Real, u).Action(id)!;

    [Fact]
    public void Tiles_are_the_shape_of_the_attack_pattern()
    {
        var map = new TileMap(12, 12);
        var o = new Pos(5, 5);
        var pipe = Action(SurvivorUnit(weapon: "pipe"), "swing");     // melee, reach 1
        var spear = Action(SurvivorUnit(weapon: "spear"), "thrust");  // line, reach 3 straight, 2 diagonal

        Assert.Equal(Pos.Neighbours8.Select(d => o + d).OrderBy(p => p.Y).ThenBy(p => p.X),
            Combat.Tiles(map, pipe, o, null).OrderBy(p => p.Y).ThenBy(p => p.X));
        var line = Combat.Tiles(map, spear, o, null).ToHashSet();
        Assert.Equal(20, line.Count); // 4 straight directions of 3 tiles, 4 diagonals of 2
        Assert.Contains(new Pos(7, 7), line);
        Assert.DoesNotContain(new Pos(7, 6), line);
        Assert.DoesNotContain(o, line);
    }

    [Fact]
    public void Tiles_stay_on_the_map()
    {
        var map = new TileMap(4, 4);
        var pistol = Action(SurvivorUnit(weapon: "pistol"), "shoot");
        Assert.All(Combat.Tiles(map, pistol, new(0, 0), null), p => Assert.True(map.InBounds(p)));
    }

    [Fact]
    public void Check_reach_keeps_the_old_rules()
    {
        var map = new TileMap(14, 14);
        var o = new Pos(5, 5);
        var pistol = Action(SurvivorUnit(weapon: "pistol"), "shoot");   // ranged, reach 6: a disc
        var spear = Action(SurvivorUnit(weapon: "spear"), "thrust");    // line, reach 3 straight, 2 diagonal

        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, o, pistol, new(11, 5)));       // 36 = 6²
        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, o, pistol, new(10, 8)));       // 25 + 9 = 34
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, o, pistol, new(10, 9))); // 25 + 16 = 41
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, o, pistol, new(11, 6))); // 36 + 1 = 37
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, o, pistol, o));
        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, o, spear, new(7, 3)));
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, o, spear, new(7, 6)));   // off the diagonal
        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, o, spear, new(5, 8)));         // 3 along the column
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, o, spear, new(5, 9)));   // 4 along the column
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, o, spear, new(8, 8)));   // 3 along the diagonal

        map[new Pos(6, 5)] = Terrain.Wall;
        Assert.Equal(ScavengeError.NoLineOfSight, Combat.CheckReach(Real, map, o, spear, new(7, 5)));
        Assert.Equal(ScavengeError.NoLineOfSight, Combat.CheckReach(Real, map, o, pistol, new(9, 5)));
        map[new Pos(6, 6)] = Terrain.Wall;
        map[new Pos(5, 6)] = Terrain.Wall;
        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, o, pistol, new(6, 4)));        // adjacent: no sight check
    }

    [Fact]
    public void Hittable_tiles_are_the_reachable_tiles_in_sight()
    {
        var map = new TileMap(14, 14);
        var o = new Pos(5, 5);
        map[new Pos(6, 5)] = Terrain.Wall;
        var pistol = Action(SurvivorUnit(weapon: "pistol"), "shoot");
        var hittable = Combat.Hittable(Real, map, pistol, o, null).ToHashSet();
        Assert.Contains(new Pos(6, 5), hittable);        // the wall tile itself is next to the shooter
        Assert.DoesNotContain(new Pos(8, 5), hittable);  // behind the wall
        Assert.Contains(new Pos(8, 7), hittable);
        Assert.Contains(new Pos(10, 8), hittable);
        Assert.DoesNotContain(new Pos(10, 9), hittable); // out of the disc
        Assert.DoesNotContain(o, hittable);
        foreach (var p in map.AllPositions)
            Assert.Equal(Combat.CheckReach(Real, map, o, pistol, p) == ScavengeError.None, hittable.Contains(p));
    }

    [Fact]
    public void A_wall_hides_tiles_behind_it_from_the_threat_zone()
    {
        var shooter = SurvivorUnit(pos: new(1, 1), weapon: "pistol");
        var sim = Arena(8, 3, [shooter], rows: ["........", "...#....", "........"]);
        var zone = Combat.ThreatZone(sim, shooter);
        Assert.Contains(new Pos(2, 1), zone);
        Assert.DoesNotContain(new Pos(5, 1), zone);
        Assert.Contains(new Pos(5, 0), zone);
        Assert.DoesNotContain(shooter.Pos, zone);
    }

    [Fact]
    public void The_threat_zone_unions_every_attack_the_unit_has()
    {
        var spearman = SurvivorUnit(pos: new(5, 5), weapon: "spear");
        var sim = Arena(11, 11, [spearman]);
        var zone = Combat.ThreatZone(sim, spearman);
        foreach (var action in sim.Profile(spearman).Actions)
            Assert.Subset(zone, Combat.Hittable(Real, sim.State.Map, action, spearman.Pos, spearman.Facing).ToHashSet());
        Assert.Contains(new Pos(7, 3), zone);
    }

    [Fact]
    public void A_downed_unit_threatens_nothing()
    {
        var u = SurvivorUnit(pos: new(2, 2), weapon: "pipe");
        var sim = Arena(5, 5, [u]);
        u.Status = UnitStatus.Downed;
        Assert.Empty(Combat.ThreatZone(sim, u));
    }
}
