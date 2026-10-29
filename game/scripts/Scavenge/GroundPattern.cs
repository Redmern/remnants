using Remnants.Core.Scavenge;
using TileMap = Remnants.Core.Scavenge.TileMap;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Deterministic, purely visual dressing choices for the map: which ground variant a tile gets, how a prop is
/// turned. Seeded from the map itself, so a mission looks the same after a save/load. Ground comes in patches
/// (a calm base with ragged islands of the other variants) instead of per-tile noise.
/// </summary>
public static class GroundPattern
{
    /// <summary>Patch cell size in tiles.</summary>
    public const int PatchSize = 4;

    /// <summary>Percent of patches that use the base variant.</summary>
    public const int BaseShare = 75;

    public static ulong Seed(TileMap map, string zoneId)
    {
        var h = 14695981039346656037UL;
        void Mix(ulong v)
        {
            h ^= v;
            h *= 1099511628211UL;
        }
        foreach (var c in zoneId) Mix(c);
        Mix((ulong)map.Width);
        Mix((ulong)map.Height);
        foreach (var p in map.AllPositions) Mix((ulong)map[p]);
        return h;
    }

    /// <summary>splitmix64 of the seed, a tile and a salt (one salt per kind of choice).</summary>
    public static ulong Hash(ulong seed, int x, int y, int salt)
    {
        var z = seed ^ ((ulong)(uint)x * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)(uint)salt << 48);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>One of <paramref name="count"/> options for this tile.</summary>
    public static int Pick(ulong seed, Pos p, int count, int salt) => count <= 1 ? 0 : (int)(Hash(seed, p.X, p.Y, salt) % (ulong)count);

    /// <summary>Ground variant for a tile: 0 is the base; patch borders are jittered by a tile so they are not square.</summary>
    public static int Variant(ulong seed, Pos p, int count)
    {
        if (count <= 1) return 0;
        var cx = FloorDiv(p.X + Pick(seed, p, 3, 1) - 1, PatchSize);
        var cy = FloorDiv(p.Y + Pick(seed, p, 3, 2) - 1, PatchSize);
        var h = Hash(seed, cx, cy, 3);
        if ((int)(h % 100) < BaseShare) return 0;
        return 1 + (int)((h >> 16) % (ulong)(count - 1));
    }

    /// <summary>Half the tiles are turned 180°; that hides repeats without breaking directional detail like ruts.</summary>
    public static bool Flip(ulong seed, Pos p) => Pick(seed, p, 2, 4) == 1;

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
}
