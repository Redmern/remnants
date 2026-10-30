namespace Remnants.Core.Random;

/// <summary>
/// Seeded xorshift64* generator. Its whole state is one ulong so it round-trips through saves,
/// and identical seeds give identical sequences on every platform.
/// </summary>
public sealed class DeterministicRng
{
    public ulong State { get; set; }

    public DeterministicRng() : this(1) { }

    public DeterministicRng(ulong seed)
    {
        // SplitMix64 scramble so small seeds (0, 1, 2...) still start well distributed; never zero.
        var z = seed + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        State = z == 0 ? 0x2545F4914F6CDD1DUL : z;
    }

    public ulong NextULong()
    {
        var x = State;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        State = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range is empty.");
        var range = (ulong)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextULong() % range);
    }

    /// <summary>Uniform int in [minInclusive, maxInclusive].</summary>
    public int NextInclusive(int minInclusive, int maxInclusive) => Next(minInclusive, maxInclusive + 1);

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(0, items.Count)];
}

/// <summary>
/// A one-off roll derived from a stream's current state and some keys, without advancing the stream: systems added later
/// (medicine) roll this way so they do not shift every other system's dice.
/// </summary>
public static class SideRoll
{
    public static double Next(DeterministicRng stream, params long[] keys)
    {
        var seed = stream.State;
        foreach (var k in keys) seed = (seed ^ (ulong)k) * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
        return new DeterministicRng(seed).NextDouble();
    }
}
