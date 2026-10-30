using Remnants.Core.Colony;

namespace Remnants.Core.Scavenge;

/// <summary>
/// The light and weather a scavenge instance is played in: fixed when the instance is made (the colony's arrival time,
/// or the server's choice in multiplayer) and saved with it. <see cref="Weather"/> is a slot for weather effects
/// (<c>scavenge.json environment.weather</c>); null is clear.
/// </summary>
public sealed class ScavengeEnvironment
{
    /// <summary>Hour of day the mission is played at (0..23), for the light on screen.</summary>
    public int Hour { get; set; } = 12;

    public DayPhase Phase { get; set; } = DayPhase.Day;

    public string? Weather { get; set; }

    public static ScavengeEnvironment At(int hour, DayPhase phase, string? weather = null) => new() { Hour = hour, Phase = phase, Weather = weather };

    /// <summary>
    /// The environment at an hour by the default colony phase hours (<see cref="Data.DayNightRules"/>), for a server that has
    /// no colony rules loaded.
    /// </summary>
    public static ScavengeEnvironment AtHour(int hour, string? weather = null) =>
        At(((hour % 24) + 24) % 24, DayNight.PhaseAtHour(((hour % 24) + 24) % 24, new Data.DayNightRules()), weather);

    public ScavengeEnvironment Copy() => At(Hour, Phase, Weather);

    public bool IsDark => Phase == DayPhase.Night;
}

/// <summary>How a unit senses the world, for the environment's effects on sight and stealth.</summary>
public enum SenseKind { Human, Husk, Linked, Drone, Camera, Turret }

/// <summary>
/// What a phase (or a weather) does in the field. Multipliers default to 1 and bonuses to 0, so an empty entry changes
/// nothing; a phase and a weather combine (multipliers multiply, bonuses add, ignore lists merge).
/// </summary>
public sealed record EnvironmentEffects
{
    /// <summary>Sight multiplier per sense kind (missing kinds: 1). Never cuts sight below <see cref="MinSight"/>.</summary>
    public Dictionary<SenseKind, double> Sight { get; init; } = new();

    /// <summary>Added to every unit's stealth against observers not in <see cref="StealthIgnoredBy"/>.</summary>
    public double StealthBonus { get; init; }

    /// <summary>Observers whose senses see through the dark (the Linked's sensors, cameras on night vision).</summary>
    public List<SenseKind> StealthIgnoredBy { get; init; } = new();

    /// <summary>Husks in the zone: count × this (extra Husks spawn on top of the usual mix).</summary>
    public double HuskCountMultiplier { get; init; } = 1;

    /// <summary>How far Husks hear noise: radius × this.</summary>
    public double HuskHearingMultiplier { get; init; } = 1;

    /// <summary>A flare's light radius × this (it carries further in the dark).</summary>
    public double FlareRadiusMultiplier { get; init; } = 1;

    /// <summary>Lowest sight the effects cut anyone to.</summary>
    public double MinSight { get; init; } = 2;

    public double SightFor(SenseKind kind) => Sight.GetValueOrDefault(kind, 1);

    public EnvironmentEffects Combine(EnvironmentEffects other) => new()
    {
        Sight = Enum.GetValues<SenseKind>().ToDictionary(k => k, k => SightFor(k) * other.SightFor(k)),
        StealthBonus = StealthBonus + other.StealthBonus,
        StealthIgnoredBy = StealthIgnoredBy.Union(other.StealthIgnoredBy).ToList(),
        HuskCountMultiplier = HuskCountMultiplier * other.HuskCountMultiplier,
        HuskHearingMultiplier = HuskHearingMultiplier * other.HuskHearingMultiplier,
        FlareRadiusMultiplier = FlareRadiusMultiplier * other.FlareRadiusMultiplier,
        MinSight = Math.Max(MinSight, other.MinSight),
    };
}

/// <summary><c>scavenge.json environment</c>: effects per day phase, and per weather id.</summary>
public sealed record EnvironmentRules
{
    public Dictionary<DayPhase, EnvironmentEffects> Phases { get; init; } = new();
    public Dictionary<string, EnvironmentEffects> Weather { get; init; } = new();
}

/// <summary>
/// The environment's rules in the field: the effects in force (<see cref="Effects"/>), a unit's sense kind, and its sight
/// and the stealth bonus it faces. Night: humans see much less, everyone is stealthier, except to the Linked's sensors and
/// cameras; Husks are more and hear further; flares carry further. Detection, visibility and AI read it through here.
/// </summary>
public static class FieldEnvironment
{
    public static EnvironmentEffects Effects(ScavengeData data, ScavengeState state)
    {
        var rules = data.Rules.Environment;
        var env = state.Environment;
        var effects = rules.Phases.GetValueOrDefault(env.Phase) ?? new EnvironmentEffects();
        return env.Weather is { } w && rules.Weather.TryGetValue(w, out var weather) ? effects.Combine(weather) : effects;
    }

    public static SenseKind SenseOf(ScavengeData data, ScavengeState state, Unit unit) => unit.Kind switch
    {
        UnitKind.Drone => SenseKind.Drone,
        UnitKind.Device => state.Devices.FirstOrDefault(d => d.UnitId == unit.Id)?.Kind == DeviceKind.Turret ? SenseKind.Turret : SenseKind.Camera,
        UnitKind.Enemy => Network.IsLinked(data, unit) ? SenseKind.Linked : SenseKind.Husk,
        _ => SenseKind.Human,
    };

    /// <summary>Sight after the environment: base × the kind's multiplier, never below <see cref="EnvironmentEffects.MinSight"/> (unless it was already).</summary>
    public static double Sight(ScavengeData data, ScavengeState state, Unit unit, double baseSight)
    {
        var effects = Effects(data, state);
        var mult = effects.SightFor(SenseOf(data, state, unit));
        return mult >= 1 ? baseSight * mult : Math.Max(Math.Min(baseSight, effects.MinSight), baseSight * mult);
    }

    /// <summary>Stealth every target gains against this observer (0 for the kinds that see through it).</summary>
    public static double StealthBonusAgainst(ScavengeData data, ScavengeState state, Unit observer)
    {
        var effects = Effects(data, state);
        return effects.StealthIgnoredBy.Contains(SenseOf(data, state, observer)) ? 0 : effects.StealthBonus;
    }
}
