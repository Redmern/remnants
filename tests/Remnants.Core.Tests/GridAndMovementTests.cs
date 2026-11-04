using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class MovementTests
{
    [Fact]
    public void King_walk_reaches_every_tile_within_range_in_any_direction()
    {
        var u = SurvivorUnit(pos: new(3, 3));
        var sim = Arena(7, 7, [u]);
        u.Mp = 2;
        var reach = sim.Reachable(u);
        Assert.Equal(24, reach.Count); // 5x5 minus own tile
        Assert.All(reach, kv => Assert.Equal(kv.Key.Chebyshev(u.Pos), kv.Value));
        Assert.DoesNotContain(new Pos(3, 0), reach.Keys);
    }

    [Fact]
    public void Rubble_costs_more_and_walls_block()
    {
        var u = SurvivorUnit(pos: new(0, 1));
        var sim = Arena(5, 3, [u], [
            ".#...",
            ".r...",
            ".#...",
        ]);
        u.Mp = 2;
        var reach = sim.Reachable(u).Keys.ToHashSet();
        Assert.Contains(new Pos(1, 1), reach); // rubble costs 2 = all the MP left
        Assert.DoesNotContain(new Pos(2, 1), reach);
        Assert.DoesNotContain(new Pos(1, 0), reach); // wall
    }

    [Fact]
    public void A_range_one_unit_can_always_take_one_step_onto_rough_terrain()
    {
        // Low agility (or hurt legs, heavy armor, a missing leg) leaves range 1; rubble and water cost more than that.
        var u = SurvivorUnit(pos: new(0, 1));
        u.Attributes[AttributeKind.Agility] = 10;
        var sim = Arena(4, 3, [u], [
            "r##.",
            "r~..",
            "r#..",
        ]);
        var reach = sim.Reachable(u).Keys.ToHashSet();
        Assert.Contains(new Pos(1, 1), reach); // water costs 3: one slog step uses the whole move
        Assert.Contains(new Pos(0, 0), reach);
        Assert.DoesNotContain(new Pos(2, 1), reach); // but no further
    }

    [Fact]
    public void A_range_one_rook_can_step_onto_rubble()
    {
        var u = SurvivorUnit(pos: new(0, 0), body: "scout_jacket");
        u.Attributes[AttributeKind.Agility] = 0;
        u.Body.Injure(BodyPart.LeftLeg, 0.9);
        var sim = Arena(3, 1, [u], [".r."]);
        Assert.Equal(1, sim.Profile(u).Movement.Modes[0].Range);
        var reach = sim.Reachable(u).Keys.ToHashSet();
        Assert.Contains(new Pos(1, 0), reach);
        Assert.DoesNotContain(new Pos(2, 0), reach);
    }

    [Fact]
    public void Rook_pattern_moves_only_in_straight_orthogonal_lines()
    {
        var u = SurvivorUnit(pos: new(3, 3), body: "scout_jacket"); // range 3, rook
        var sim = Arena(7, 7, [u]);
        var reach = sim.Reachable(u).Keys.ToHashSet();
        Assert.Contains(new Pos(3, 0), reach);
        Assert.Contains(new Pos(6, 3), reach);
        Assert.DoesNotContain(new Pos(4, 4), reach);
        Assert.Equal(12, reach.Count);
    }

    [Fact]
    public void Knight_jump_clears_walls()
    {
        var u = SurvivorUnit(pos: new(0, 0), tool: "grappling_hook");
        u.Attributes[AttributeKind.Agility] = 10; // walk range 1
        var sim = Arena(4, 4, [u], [
            ".#..",
            "##..",
            "....",
            "....",
        ]);
        var reach = sim.Reachable(u);
        Assert.Equal(4, reach[new Pos(1, 2)]);
        Assert.Equal(4, reach[new Pos(2, 1)]);
        Assert.Equal(2, reach.Count);
    }

    [Fact]
    public void Hostiles_block_the_way_allies_do_not_and_nobody_shares_a_tile()
    {
        var mover = SurvivorUnit(pos: new(0, 0), body: "scout_jacket"); // rook 3
        var ally = SurvivorUnit(pos: new(1, 0));
        var husk = EnemyUnit("husk", new(0, 1));
        var sim = Arena(4, 4, [mover, ally, husk]);
        var reach = sim.Reachable(mover).Keys.ToHashSet();
        Assert.Contains(new Pos(2, 0), reach);   // through the ally
        Assert.DoesNotContain(new Pos(1, 0), reach); // onto the ally
        Assert.DoesNotContain(new Pos(0, 2), reach); // past the husk
    }

    [Fact]
    public void No_squeezing_diagonally_between_two_walls()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var sim = Arena(3, 3, [u], [
            ".#.",
            "#..",
            "...",
        ]);
        Assert.Empty(sim.Reachable(u));
    }

    [Fact]
    public void Distance_field_counts_terrain_cost()
    {
        var map = new TileMap(4, 1);
        map[new Pos(1, 0)] = Terrain.Rubble;
        var profile = ProfileDeriver.Derive(Real, SurvivorUnit()).Movement;
        var field = Movement.DistanceField(map, profile, new Pos(0, 0));
        Assert.Equal([0, 1, 3, 4], Enumerable.Range(0, 4).Select(x => field[new Pos(x, 0)]));
    }
}

public class VisibilityTests
{
    [Fact]
    public void Walls_block_line_of_sight()
    {
        var map = new TileMap(5, 3);
        map[new Pos(2, 1)] = Terrain.Wall;
        Assert.False(map.HasLineOfSight(Real, new(0, 1), new(4, 1)));
        Assert.True(map.HasLineOfSight(Real, new(0, 0), new(4, 0)));
        Assert.True(map.HasLineOfSight(Real, new(0, 1), new(2, 1))); // you can see the wall itself
    }

    [Fact]
    public void Line_of_sight_is_symmetric()
    {
        var map = new TileMap(9, 9);
        foreach (var p in new Pos[] { new(3, 2), new(5, 6), new(4, 4), new(2, 5) }) map[p] = Terrain.Wall;
        foreach (var a in map.AllPositions)
            foreach (var b in new Pos[] { new(0, 0), new(8, 3), new(6, 8) })
                Assert.Equal(map.HasLineOfSight(Real, a, b), map.HasLineOfSight(Real, b, a));
    }

    [Fact]
    public void Perception_sets_sight_radius()
    {
        var keen = SurvivorUnit(pos: new(0, 0));
        keen.Attributes[AttributeKind.Perception] = 100; // 4 + 6 = 10
        var dull = SurvivorUnit(pos: new(0, 0));
        dull.Attributes[AttributeKind.Perception] = 0;   // 4
        var map = new TileMap(20, 1);
        Assert.Contains(map.Index(new Pos(10, 0)), Visibility.VisibleTiles(Real, map, keen, ProfileDeriver.Derive(Real, keen).Sight));
        var dullSees = Visibility.VisibleTiles(Real, map, dull, ProfileDeriver.Derive(Real, dull).Sight);
        Assert.Contains(map.Index(new Pos(4, 0)), dullSees);
        Assert.DoesNotContain(map.Index(new Pos(5, 0)), dullSees);
    }

    [Fact]
    public void Each_side_has_its_own_fog_and_remembers_what_it_explored()
    {
        var a = SurvivorUnit(1, new(0, 0));
        var b = SurvivorUnit(2, new(29, 0));
        var sim = Arena(30, 3, [a, b]);
        Assert.Contains(sim.State.Map.Index(new Pos(0, 0)), sim.VisibleTo(1));
        Assert.DoesNotContain(sim.State.Map.Index(new Pos(29, 0)), sim.VisibleTo(1));
        Assert.Contains(sim.State.Map.Index(new Pos(29, 0)), sim.VisibleTo(2));

        Assert.True(Now(sim, new Move(1, a.Id, new(2, 0))).Ok);
        var explored = sim.State.Explored[Unit.PlayerSide(1)];
        Assert.Contains(sim.State.Map.Index(new Pos(8, 0)), explored);
        Assert.DoesNotContain(sim.State.Map.Index(new Pos(20, 0)), explored);
    }
}
