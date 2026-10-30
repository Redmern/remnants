namespace Remnants.Core.Scavenge;

/// <summary>Fog of war: what a unit, or a whole side, can see. Sight radius comes from the unit's profile (Perception, eyes, gear).</summary>
public static class Visibility
{
    public static bool CanSee(ScavengeData data, TileMap map, Pos from, double sight, Pos to) =>
        from.DistanceSquared(to) <= sight * sight + 1e-9 && map.HasLineOfSight(data, from, to);

    /// <summary>Tile indices visible to one unit.</summary>
    public static HashSet<int> VisibleTiles(ScavengeData data, TileMap map, Unit unit, double sight)
    {
        var result = new HashSet<int>();
        var r = (int)Math.Ceiling(sight);
        for (var y = unit.Pos.Y - r; y <= unit.Pos.Y + r; y++)
            for (var x = unit.Pos.X - r; x <= unit.Pos.X + r; x++)
            {
                var p = new Pos(x, y);
                if (map.InBounds(p) && CanSee(data, map, unit.Pos, sight, p)) result.Add(map.Index(p));
            }
        return result;
    }

    /// <summary>
    /// Tile indices currently visible to a side: the union over its conscious units, plus tiles lit by flares. Seeing a
    /// tile shows the terrain and caches on it; whether a hostile standing there is seen is <see cref="Detection"/>'s call.
    /// </summary>
    public static HashSet<int> SideVisible(ScavengeData data, ScavengeState state, string side)
    {
        var result = new HashSet<int>();
        foreach (var u in state.Units.Where(u => u.Side == side && u.IsActive))
        {
            var baseSight = ProfileDeriver.Derive(data, u).Sight;
            result.UnionWith(VisibleTiles(data, state.Map, u, FieldEnvironment.Sight(data, state, u, baseSight)));
            // A weapon light's cone shows its tiles as by day.
            if (GearEffects.ActiveLight(data, state, u) is { } light)
                result.UnionWith(VisibleTiles(data, state.Map, u, Math.Min(baseSight, light.Range))
                    .Where(i => GearEffects.InCone(u.Facing, u.Pos, new Pos(i % state.Map.Width, i / state.Map.Width), light.ConeDegrees)));
        }
        if (result.Count > 0) result.UnionWith(Detection.LitTiles(state));
        return result;
    }

    /// <summary>Adds what every party side sees right now to its explored set.</summary>
    public static void UpdateExplored(ScavengeData data, ScavengeState state)
    {
        foreach (var party in state.Parties.Where(p => !p.Done))
        {
            if (!state.Explored.TryGetValue(party.Side, out var explored))
                state.Explored[party.Side] = explored = new SortedSet<int>();
            explored.UnionWith(SideVisible(data, state, party.Side));
        }
    }
}
