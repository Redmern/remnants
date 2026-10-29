using Remnants.Core.Random;

namespace Remnants.Core.Tests;

public class RngTests
{
    [Fact]
    public void Same_seed_gives_same_sequence()
    {
        var a = new DeterministicRng(42);
        var b = new DeterministicRng(42);
        for (var i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void Different_seeds_diverge()
    {
        var a = new DeterministicRng(1);
        var b = new DeterministicRng(2);
        Assert.NotEqual(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void Restoring_state_resumes_the_sequence()
    {
        var a = new DeterministicRng(7);
        a.NextULong();
        var resumed = new DeterministicRng { State = a.State };
        Assert.Equal(a.NextULong(), resumed.NextULong());
    }

    [Fact]
    public void Ranges_stay_in_bounds()
    {
        var rng = new DeterministicRng(3);
        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(rng.Next(5, 10), 5, 9);
            Assert.InRange(rng.NextInclusive(5, 10), 5, 10);
            Assert.InRange(rng.NextDouble(), 0.0, 0.9999999999);
        }
    }
}
