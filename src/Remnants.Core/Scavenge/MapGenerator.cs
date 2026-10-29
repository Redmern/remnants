using Remnants.Core.Random;

namespace Remnants.Core.Scavenge;

/// <summary>A generated map plus the spawn anchor and every tile walkable from it.</summary>
public sealed record GeneratedMap(TileMap Map, Pos SpawnAnchor, HashSet<Pos> Reachable);

/// <summary>
/// Seeded procedural maps from a zone template: walled rooms with doorways, scattered rubble, cover and water,
/// exits on one edge and a carved route from the spawn edge to every exit so the zone is always escapable.
/// </summary>
public static class MapGenerator
{
    private const int MaxRoomTries = 40;

    public static GeneratedMap Generate(ScavengeData data, ZoneDef zone, DeterministicRng rng)
    {
        var map = new TileMap(zone.Width, zone.Height);
        var t = zone.Terrain;

        // Keep a clear band along the spawn edge (3 deep) and the exit edge (2 deep).
        bool InBand(Pos p) => Depth(map, zone.SpawnEdge, p) < 3 || Depth(map, zone.ExitEdge, p) < 2;

        var rooms = new List<(int X, int Y, int W, int H)>();
        var roomCount = rng.NextInclusive(t.Rooms[0], t.Rooms[1]);
        for (var tries = 0; rooms.Count < roomCount && tries < MaxRoomTries * Math.Max(1, roomCount); tries++)
        {
            var w = rng.NextInclusive(t.RoomSize[0], t.RoomSize[1]);
            var h = rng.NextInclusive(t.RoomSize[0], t.RoomSize[1]);
            if (w > map.Width - 2 || h > map.Height - 2) continue;
            var x = rng.NextInclusive(1, map.Width - w - 1);
            var y = rng.NextInclusive(1, map.Height - h - 1);
            if (Corners(x, y, w, h).Any(InBand)) continue;
            if (rooms.Any(r => x <= r.X + r.W && r.X <= x + w && y <= r.Y + r.H && r.Y <= y + h)) continue;
            rooms.Add((x, y, w, h));
            BuildRoom(map, rng, x, y, w, h);
        }

        foreach (var p in map.AllPositions.ToList())
        {
            if (map[p] != Terrain.Floor || InBand(p)) continue;
            var roll = rng.NextDouble();
            if (roll < t.Rubble) map[p] = Terrain.Rubble;
            else if (roll < t.Rubble + t.Cover) map[p] = Terrain.Cover;
            else if (roll < t.Rubble + t.Cover + t.Water) map[p] = Terrain.Water;
        }

        var exitEdge = EdgeTiles(map, zone.ExitEdge);
        var segment = exitEdge.Count / zone.Exits;
        for (var i = 0; i < zone.Exits; i++)
        {
            var exit = exitEdge[rng.Next(i * segment, Math.Max(i * segment + 1, (i + 1) * segment))];
            if (map.Exits.Contains(exit)) continue;
            map[exit] = Terrain.Floor;
            map.Exits.Add(exit);
        }

        var spawnEdge = EdgeTiles(map, zone.SpawnEdge);
        var anchor = spawnEdge[spawnEdge.Count / 2];
        foreach (var exit in map.Exits) Carve(data, map, anchor, exit);
        var reachable = Flood(data, map, anchor);

        // Pylons stand on open floor deep in the zone, away from the spawn edge and from each other, and never cut the
        // route: a candidate is skipped if placing it would disconnect a reachable tile.
        var pylons = rng.NextInclusive(zone.Pylons[0], zone.Pylons[1]);
        var candidates = reachable
            .Where(p => map[p] == Terrain.Floor && !map.IsExit(p) && Depth(map, zone.SpawnEdge, p) >= map.Height / 3
                        && Depth(map, zone.ExitEdge, p) >= 2)
            .OrderBy(map.Index).ToList();
        for (var placed = 0; placed < pylons && candidates.Count > 0;)
        {
            var p = candidates[rng.Next(0, candidates.Count)];
            candidates.Remove(p);
            if (map.AllPositions.Any(q => map[q] == Terrain.Pylon && q.Chebyshev(p) < 6)) continue;
            map[p] = Terrain.Pylon;
            var after = Flood(data, map, anchor);
            if (after.Count != reachable.Count - 1)
            {
                map[p] = Terrain.Floor;
                continue;
            }
            reachable = after;
            placed++;
        }

        return new GeneratedMap(map, anchor, reachable);
    }

    /// <summary>How far a tile is from an edge (0 = on it).</summary>
    public static int Depth(TileMap map, MapEdge edge, Pos p) => edge switch
    {
        MapEdge.North => p.Y,
        MapEdge.South => map.Height - 1 - p.Y,
        MapEdge.West => p.X,
        _ => map.Width - 1 - p.X,
    };

    /// <summary>Tiles along an edge, corners excluded, in a fixed order.</summary>
    public static List<Pos> EdgeTiles(TileMap map, MapEdge edge) => edge switch
    {
        MapEdge.North => Enumerable.Range(1, map.Width - 2).Select(x => new Pos(x, 0)).ToList(),
        MapEdge.South => Enumerable.Range(1, map.Width - 2).Select(x => new Pos(x, map.Height - 1)).ToList(),
        MapEdge.West => Enumerable.Range(1, map.Height - 2).Select(y => new Pos(0, y)).ToList(),
        _ => Enumerable.Range(1, map.Height - 2).Select(y => new Pos(map.Width - 1, y)).ToList(),
    };

    private static IEnumerable<Pos> Corners(int x, int y, int w, int h) =>
        [new(x, y), new(x + w - 1, y), new(x, y + h - 1), new(x + w - 1, y + h - 1)];

    /// <summary>Wall outline with two doorways (never on a corner).</summary>
    private static void BuildRoom(TileMap map, DeterministicRng rng, int x, int y, int w, int h)
    {
        var outline = new List<Pos>();
        for (var i = x; i < x + w; i++) { outline.Add(new(i, y)); outline.Add(new(i, y + h - 1)); }
        for (var j = y + 1; j < y + h - 1; j++) { outline.Add(new(x, j)); outline.Add(new(x + w - 1, j)); }
        foreach (var p in outline) map[p] = Terrain.Wall;

        var corners = Corners(x, y, w, h).ToHashSet();
        var doorCandidates = outline.Where(p => !corners.Contains(p)).ToList();
        for (var d = 0; d < 2; d++) map[rng.Pick(doorCandidates)] = Terrain.Floor;
    }

    /// <summary>Greedy walk from start to goal, turning any impassable tile on the way into floor.</summary>
    private static void Carve(ScavengeData data, TileMap map, Pos from, Pos to)
    {
        var p = from;
        while (true)
        {
            if (!data.Terrain(map[p]).Passable) map[p] = Terrain.Floor;
            if (p == to) return;
            var dx = to.X - p.X;
            var dy = to.Y - p.Y;
            p = Math.Abs(dx) >= Math.Abs(dy) ? p + new Pos(Math.Sign(dx), 0) : p + new Pos(0, Math.Sign(dy));
        }
    }

    /// <summary>Every passable tile connected to start by orthogonal steps (so any move pattern can get there).</summary>
    public static HashSet<Pos> Flood(ScavengeData data, TileMap map, Pos start)
    {
        var seen = new HashSet<Pos> { start };
        var queue = new Queue<Pos>([start]);
        while (queue.TryDequeue(out var p))
            foreach (var d in Pos.Orthogonal)
            {
                var n = p + d;
                if (map.InBounds(n) && data.Terrain(map[n]).Passable && seen.Add(n)) queue.Enqueue(n);
            }
        return seen;
    }
}
