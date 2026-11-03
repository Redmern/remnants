using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// The stab / slash mode the player picked for tile strikes, kept only for the round it was picked in: a new round
/// starts with clicks on enemies attacking them again (<see cref="Sync"/>).
/// </summary>
public sealed class StrikeSelection
{
    private int _round;

    public StrikeMode? Mode { get; private set; }

    /// <summary>Picks <paramref name="mode"/> in <paramref name="round"/>, or drops it when it was already picked.</summary>
    public void Toggle(StrikeMode mode, int round)
    {
        Mode = Mode == mode ? null : mode;
        _round = round;
    }

    /// <summary>Drops the mode once <paramref name="round"/> is past the one it was picked in.</summary>
    public void Sync(int round)
    {
        if (round != _round) Mode = null;
    }

    public void Clear() => Mode = null;
}
