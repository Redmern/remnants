namespace Remnants.Core.Scavenge;

/// <summary>How much screen feel the scavenge replay adds (the settings' master "Effects intensity").</summary>
public enum EffectsLevel { Off, Low, Normal, High }

/// <summary>
/// The feel settings the screen passes in (Core never reads the UI's settings): level, toggles, reduce motion, and the
/// CRT look and music reactivity. <see cref="Save"/> / <see cref="Load"/> are their saved values (settings.cfg
/// <c>[effects]</c>; reduce motion is saved with the UI's own settings).
/// </summary>
public readonly record struct JuiceSettings(EffectsLevel Level, bool Shake, bool HitStop, bool Flashes, bool ReduceMotion)
{
    public bool Crt { get; init; } = true;
    public bool Music { get; init; } = true;

    public static readonly JuiceSettings Default = new(EffectsLevel.Normal, true, true, true, false);

    public Dictionary<string, object> Save() => new()
    {
        ["level"] = (int)Level,
        ["shake"] = Shake,
        ["hit_stop"] = HitStop,
        ["flashes"] = Flashes,
        ["crt"] = Crt,
        ["music_reactive"] = Music,
    };

    /// <summary>Saved values over <paramref name="fallback"/>: a missing or bad value keeps the fallback's (a level out of range clamps).</summary>
    public static JuiceSettings Load(IReadOnlyDictionary<string, object> saved, JuiceSettings fallback)
    {
        bool Flag(string key, bool or) => saved.TryGetValue(key, out var v) && v is bool b ? b : or;
        var level = saved.TryGetValue("level", out var l) && l is int or long ? (EffectsLevel)Math.Clamp(Convert.ToInt32(l), 0, 3) : fallback.Level;
        return new JuiceSettings(level, Flag("shake", fallback.Shake), Flag("hit_stop", fallback.HitStop), Flag("flashes", fallback.Flashes), fallback.ReduceMotion)
        {
            Crt = Flag("crt", fallback.Crt),
            Music = Flag("music_reactive", fallback.Music),
        };
    }
}

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

    /// <summary>The widest a hand's outer card tilts (degrees).</summary>
    public const double MaxFanDegrees = 12;

    /// <summary>Card <paramref name="index"/> of <paramref name="count"/>'s tilt in the hand's fan: 0 in the middle, outer cards out.</summary>
    public static double FanAngle(int index, int count) => count <= 1 ? 0 : FanSpread(index, count) * Math.Min(MaxFanDegrees, 3 * (count - 1));

    /// <summary>How far card <paramref name="index"/> sits below the middle of the fan (pixels), so the hand arcs.</summary>
    public static double FanDrop(int index, int count)
    {
        if (count <= 1) return 0;
        var t = FanSpread(index, count);
        return 10 * t * t * Math.Min(1, (count - 1) / 4.0);
    }

    /// <summary>-1 for the first card, 1 for the last, 0 in the middle.</summary>
    private static double FanSpread(int index, int count) => 2.0 * Math.Clamp(index, 0, count - 1) / (count - 1) - 1;

    /// <summary>The most a hovered card tilts toward the mouse (degrees).</summary>
    public const double HoverTilt = 4;

    /// <summary>A card's pose in the hand: tilt (degrees), offset below its resting line (pixels, negative = lifted), scale, drawn on top.</summary>
    public readonly record struct Pose(double Angle, double Drop, double Scale, bool Raised);

    /// <summary>
    /// Card <paramref name="index"/> of <paramref name="count"/>'s pose: in the fan at rest; hovered it lifts, straightens,
    /// grows a little and tilts toward <paramref name="mouse"/> (-0.5 left edge .. 0.5 right edge). Reduce motion keeps it in
    /// the fan and only draws it on top; effects Off lays every card flat.
    /// </summary>
    public static Pose CardPose(int index, int count, bool hover, double mouse, JuiceSettings settings)
    {
        if (settings.Level == EffectsLevel.Off) return new Pose(0, 0, 1, false);
        var rest = new Pose(FanAngle(index, count), FanDrop(index, count), 1, false);
        if (!hover) return rest;
        if (settings.ReduceMotion) return rest with { Raised = true };
        return new Pose(Math.Clamp(mouse, -0.5, 0.5) * 2 * HoverTilt, -12, 1.06, true);
    }

    /// <summary>The highest a chain sound's pitch climbs (times its own).</summary>
    public const double MaxChainPitch = 1.6;

    /// <summary>A chain sound's pitch at the chain's length: a semitone a link from the second, capped.</summary>
    public static double ChainPitch(int chain) => Math.Min(MaxChainPitch, Math.Pow(2, Math.Max(0, chain - 1) / 12.0));

    /// <summary>How hard the music's combat layer plays for the chain's length (0 calm, 1 full).</summary>
    public static double MusicHeat(int chain) => Math.Clamp(chain / 6.0, 0, 1);

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
        // Reduce motion: no shake, no flash, no freeze.
        if (settings.ReduceMotion) return default;
        return new Feel(
            settings.Shake ? Math.Clamp(shake * k, 0, 1) : 0,
            settings.HitStop ? Math.Clamp(stop * k, 0, MaxHitStop) : 0,
            settings.Flashes ? Math.Clamp(flash * k, 0, 1) : 0);
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

    /// <summary>The seconds a blow may stop the screen: none skipping, for a blow the side did not see, or past the round's budget.</summary>
    public double Take(double seconds, bool skip, bool seen = true)
    {
        if (skip || !seen || seconds <= 0) return 0;
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

/// <summary>
/// How a hand changed between two layouts, for its motion (UI only): which new positions hold a drawn card, and which
/// cards left without being played (swept into the used pile). Cards are keys (card id and slot owner); copies count.
/// A played key excuses one copy that left; a play that left nothing (refused) excuses nothing. Copies are alike: a
/// key still in hand as often as before counts as kept, not as one played and one drawn.
/// </summary>
public static class HandMotion
{
    public sealed record Change(List<int> Drawn, List<string> Swept);

    public static Change Diff(IReadOnlyList<string> before, IReadOnlyList<string> after, IReadOnlyList<string> played)
    {
        var left = before.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        var drawn = new List<int>();
        for (var i = 0; i < after.Count; i++)
        {
            if (left.TryGetValue(after[i], out var n) && n > 0) left[after[i]] = n - 1;
            else drawn.Add(i);
        }
        foreach (var key in played)
            if (left.TryGetValue(key, out var n) && n > 0) left[key] = n - 1;
        var swept = new List<string>();
        foreach (var key in before)
        {
            if (left[key] <= 0) continue;
            left[key]--;
            swept.Add(key);
        }
        return new Change(drawn, swept);
    }
}
