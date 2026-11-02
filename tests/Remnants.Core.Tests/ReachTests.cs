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
        var spear = Action(SurvivorUnit(weapon: "spear"), "thrust");  // line, reach 2

        Assert.Equal(Pos.Neighbours8.Select(d => o + d).OrderBy(p => p.Y).ThenBy(p => p.X),
            Combat.Tiles(map, pipe, o, null).OrderBy(p => p.Y).ThenBy(p => p.X));
        var line = Combat.Tiles(map, spear, o, null).ToHashSet();
        Assert.Equal(16, line.Count); // 8 directions, 2 tiles each
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
    public void Check_reach_is_membership_of_the_hittable_tiles()
    {
        var map = new TileMap(14, 14);
        map[new Pos(6, 4)] = Terrain.Wall;
        map[new Pos(4, 7)] = Terrain.Wall;
        map[new Pos(8, 8)] = Terrain.Wall;
        var o = new Pos(6, 6);
        foreach (var (weapon, id) in new[] { ("pipe", "swing"), ("spear", "thrust"), ("pistol", "shoot") })
        {
            var action = Action(SurvivorUnit(weapon: weapon), id);
            var hittable = Combat.Hittable(Real, map, action, o, null).ToHashSet();
            Assert.NotEmpty(hittable);
            foreach (var p in map.AllPositions)
                Assert.Equal(Combat.CheckReach(Real, map, o, action, p) == ScavengeError.None, hittable.Contains(p));
        }
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
