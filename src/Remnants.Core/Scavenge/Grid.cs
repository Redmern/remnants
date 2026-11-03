using System.Text.Json.Serialization;

namespace Remnants.Core.Scavenge;

/// <summary>A tile coordinate. X grows east, Y grows south (row 0 is the north edge).</summary>
public readonly record struct Pos(int X, int Y)
{
    public static Pos operator +(Pos a, Pos b) => new(a.X + b.X, a.Y + b.Y);
    public static Pos operator -(Pos a, Pos b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>King-move distance: diagonal steps count as 1.</summary>
    public int Chebyshev(Pos other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public int DistanceSquared(Pos other) => (X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y);

    public override string ToString() => $"({X},{Y})";

    public static readonly Pos[] Neighbours8 =
        [new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1)];

    public static readonly Pos[] Orthogonal = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];

    public static readonly Pos[] Diagonal = [new(1, -1), new(1, 1), new(-1, 1), new(-1, -1)];

    public static readonly Pos[] KnightJumps =
        [new(1, -2), new(2, -1), new(2, 1), new(1, 2), new(-1, 2), new(-2, 1), new(-2, -1), new(-1, -2)];
}

/// <summary>The terrain grid plus exit tiles. Plain data; rules ask it questions through <see cref="ScavengeData"/>.</summary>
public sealed class TileMap
{
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Row-major, Width * Height entries.</summary>
    public List<Terrain> Tiles { get; set; } = new();

    public List<Pos> Exits { get; set; } = new();

    public TileMap() { }

    public TileMap(int width, int height, Terrain fill = Terrain.Floor)
    {
        Width = width;
        Height = height;
        Tiles = Enumerable.Repeat(fill, width * height).ToList();
    }

    public bool InBounds(Pos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public int Index(Pos p) => p.Y * Width + p.X;

    public Pos FromIndex(int index) => new(index % Width, index / Width);

    /// <summary>
    /// The terrain on a tile, with a card's barrier over it (<see cref="Overlay"/>). Setting it changes the map itself.
    /// </summary>
    public Terrain this[Pos p]
    {
        get => Overlay.Count > 0 && Overlay.TryGetValue(Index(p), out var over) ? over : Tiles[Index(p)];
        set => Tiles[Index(p)] = value;
    }

    /// <summary>Terrain laid over tiles for now (barriers), from <see cref="ScavengeState.FieldEffects"/>; not saved.</summary>
    [JsonIgnore]
    public Dictionary<int, Terrain> Overlay { get; } = new();

    /// <summary>Tile indices under smoke (<see cref="FieldEffects"/>): they block sight; not saved.</summary>
    [JsonIgnore]
    public HashSet<int> Smoke { get; } = new();

    [JsonIgnore]
    public IEnumerable<Pos> AllPositions
    {
        get
        {
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    yield return new Pos(x, y);
        }
    }

    public bool IsExit(Pos p) => Exits.Contains(p);

    /// <summary>
    /// Line of sight between two tiles: no sight-blocking tile strictly between them on a Bresenham line.
    /// Checked both ways and passes if either is clear, so it is symmetric. Smoke blocks it like a wall, and hides a tile
    /// in it from anything not right next to it (and the view out of it the same way).
    /// </summary>
    public bool HasLineOfSight(ScavengeData data, Pos from, Pos to)
    {
        if (Smoke.Count > 0 && from.Chebyshev(to) > 1 && (Smoke.Contains(Index(from)) || Smoke.Contains(Index(to)))) return false;
        return LineClear(data, from, to) || LineClear(data, to, from);
    }

    private bool LineClear(ScavengeData data, Pos from, Pos to)
    {
        foreach (var p in Line(from, to))
        {
            if (p == from || p == to) continue;
            if (data.Terrain(this[p]).BlocksSight || Smoke.Count > 0 && Smoke.Contains(Index(p))) return false;
        }
        return true;
    }

    /// <summary>Bresenham line including both endpoints.</summary>
    public static IEnumerable<Pos> Line(Pos from, Pos to)
    {
        int x = from.X, y = from.Y;
        int dx = Math.Abs(to.X - x), dy = -Math.Abs(to.Y - y);
        int sx = x < to.X ? 1 : -1, sy = y < to.Y ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            yield return new Pos(x, y);
            if (x == to.X && y == to.Y) yield break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }
}
