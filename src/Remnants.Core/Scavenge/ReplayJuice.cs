namespace Remnants.Core.Scavenge;

/// <summary>How much screen feel the scavenge replay adds (the settings' master "Effects intensity").</summary>
public enum EffectsLevel { Off, Low, Normal, High }

/// <summary>The feel settings the screen passes in (Core never reads the UI's settings): level, toggles, reduce motion.</summary>
public readonly record struct JuiceSettings(EffectsLevel Level, bool Shake, bool HitStop, bool Flashes, bool ReduceMotion);

/// <summary>
/// What landed, as the screen feels it: a hit's tier, arc, exposure and HP (<see cref="ReplayJuice.ImpactOf"/>), or a
/// kill (<see cref="Kill"/>) or a card's shove (<see cref="Push"/>).
/// </summary>
public readonly record struct Impact(HitTier? Tier, FacingArc Arc, bool Exposed, double Amount, bool Killed = false, bool Pushed = false)
{
    public static Impact Kill => new(null, FacingArc.Front, false, 0, Killed: true);
    public static Impact Push => new(null, FacingArc.Front, false, 0, Pushed: true);
}

/// <summary>
/// A hit's screen feel: <see cref="Shake"/> trauma (0..1, the camera's shake), <see cref="HitStop"/> seconds the replay
/// freezes on the blow, <see cref="Flash"/> the strength (0..1) of the colour flash / vignette.
/// </summary>
public readonly record struct Feel(double Shake, double HitStop, double Flash);

/// <summary>
/// The scavenge screen's "juice" (a UI view model, like <see cref="ReplayPacing"/>; nothing here is a rule, nothing draws
/// from the run's RNG, and the run plays out the same with every effect on, off or skipped): how big and hot a pop is for
/// its value and the squad's chain, how hard a hit shakes and stops the screen for its tier, arc and exposure.
/// </summary>
public static class ReplayJuice
{
    /// <summary>The largest a pop grows (times its normal size).</summary>
    public const double MaxPopScale = 2.4;

    /// <summary>The longest one blow freezes the replay (seconds).</summary>
    public const double MaxHitStop = 0.12;

    /// <summary>The share of a Normal effect each level plays.</summary>
    public static double Intensity(EffectsLevel level) => level switch
    {
        EffectsLevel.Off => 0,
        EffectsLevel.Low => 0.5,
        EffectsLevel.High => 1.4,
        _ => 1,
    };

    /// <summary>A pop's size for its value (HP) and the chain it lands in: 1 for nothing, growing slower as it gets big.</summary>
    public static double PopScale(double amount, int chain) =>
        Math.Clamp(1 + 0.3 * Math.Log2(1 + Math.Max(0, amount) / 4) + 0.07 * Math.Min(Math.Max(0, chain - 1), 10), 1, MaxPopScale);

    /// <summary>How hot a pop's colour runs (0 the base colour, 1 white-hot) for its value and chain.</summary>
    public static double PopHeat(double amount, int chain) =>
        Math.Clamp(Math.Max(0, amount) / 30 + 0.08 * Math.Max(0, chain - 1), 0, 1);

    /// <summary>
    /// A landed attack as the screen feels it: Core's tier and HP, and the arc and exposure its line names ("from behind",
    /// "from the side", "(exposed)", <see cref="Combat"/>'s wording).
    /// </summary>
    public static Impact ImpactOf(ScavengeEvent e)
    {
        var text = e.Message;
        var arc = text.Contains(" from behind") ? FacingArc.Rear : text.Contains(" from the side") ? FacingArc.Side : FacingArc.Front;
        return new Impact(e.Tier ?? HitTier.Hit, arc, text.Contains("(exposed)"), e.Amount ?? 0);
    }

    /// <summary>How hard an impact shakes, stops and flashes the screen at the given settings.</summary>
    public static Feel Feel(Impact impact, JuiceSettings settings)
    {
        var k = Intensity(settings.Level);
        if (k <= 0) return default;
        double shake, stop, flash;
        if (impact.Killed) (shake, stop, flash) = (0.55, 0.08, 0.5);
        else if (impact.Pushed) (shake, stop, flash) = (0.22, 0.03, 0);
        else
        {
            (shake, stop, flash) = impact.Tier switch
            {
                HitTier.Graze => (0.12, 0.0, 0.0),
                HitTier.Solid => (0.45, 0.07, 0.35),
                _ => (0.25, 0.035, 0.0),
            };
            var arcBonus = impact.Arc switch { FacingArc.Rear => 1.0, FacingArc.Side => 0.5, _ => 0 };
            shake += 0.15 * arcBonus + (impact.Exposed ? 0.12 : 0) + Math.Min(impact.Amount, 30) / 30 * 0.1;
            stop += 0.025 * arcBonus + (impact.Exposed ? 0.02 : 0);
            if (flash > 0 || arcBonus >= 1 && impact.Exposed) flash = Math.Max(flash, 0.3) + 0.1 * arcBonus;
        }
        var motion = !settings.ReduceMotion;
        return new Feel(
            settings.Shake && motion ? Math.Clamp(shake * k, 0, 1) : 0,
            settings.HitStop ? Math.Clamp(stop * k, 0, MaxHitStop) : 0,
            settings.Flashes && motion ? Math.Clamp(flash * k, 0, 1) : 0);
    }
}

/// <summary>
/// The squad's chain this round, from the events alone (UI only, no rule): each hit a unit of the side lands on someone
/// else and each card it plays grows it, a miss of the side breaks it; the running total is the HP the side's hits took
/// this round. Both reset when a round starts. What others do neither grows nor breaks it.
/// </summary>
public sealed class HitChain
{
    /// <summary>The chain after an event: its length, the round's total, and whether this event grew, broke or reset it.</summary>
    public readonly record struct Link(int Count, double Total, bool Grew, bool Broke, bool Reset);

    public int Count { get; private set; }
    public double Total { get; private set; }

    public Link Step(ScavengeState state, string side, ScavengeEvent e)
    {
        bool Mine(int? id) => id is { } i && state.FindUnit(i) is { } u && u.Side == side;
        switch (e.Type)
        {
            case ScavengeEventType.RoundStarted:
                (Count, Total) = (0, 0);
                return new(0, 0, false, false, true);
            case ScavengeEventType.Attacked when Mine(e.UnitId) && e.TargetId != e.UnitId && !Mine(e.TargetId):
                Count++;
                Total += e.Amount ?? 0;
                return new(Count, Total, true, false, false);
            case ScavengeEventType.CardPlayed when Mine(e.UnitId):
                Count++;
                return new(Count, Total, true, false, false);
            case ScavengeEventType.Missed when Mine(e.UnitId) && Count > 0:
                Count = 0;
                return new(0, Total, false, true, false);
            default:
                return new(Count, Total, false, false, false);
        }
    }
}

/// <summary>
/// Caps the time hit-stops add to a round (<see cref="PerRound"/>): each blow takes what is left of the round's budget,
/// nothing while skipping animations. <see cref="NewRound"/> refills it.
/// </summary>
public sealed class HitStopBudget
{
    /// <summary>The most hit-stop seconds one round may add.</summary>
    public const double PerRound = 0.6;

    public double Spent { get; private set; }

    public double Take(double seconds, bool skip)
    {
        if (skip || seconds <= 0) return 0;
        var take = Math.Min(seconds, PerRound - Spent);
        if (take <= 0) return 0;
        Spent += take;
        return take;
    }

    public void NewRound() => Spent = 0;
}

/// <summary>
/// The strobing cap for colour flashes (photosensitivity): never two closer than <see cref="MinGap"/> seconds, so never
/// more than <see cref="MaxPerSecond"/> a second (under the three-a-second guideline).
/// </summary>
public sealed class FlashGate
{
    public const double MinGap = 0.5;
    public const double MaxPerSecond = 1 / MinGap;
    private double? _last;

    /// <summary>Whether a flash may show at <paramref name="now"/> (seconds); if so it counts.</summary>
    public bool Allow(double now)
    {
        if (_last is { } last && now - last < MinGap) return false;
        _last = now;
        return true;
    }
}
