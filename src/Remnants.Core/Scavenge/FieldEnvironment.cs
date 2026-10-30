using Remnants.Core.Colony;

namespace Remnants.Core.Scavenge;

/// <summary>
/// The light and weather a scavenge instance is played in: set when the instance is made (the colony's arrival time and
/// the weather over the zone, or the server's choice in multiplayer) and saved with it. <see cref="Weather"/> names the
/// weather's effects (<c>scavenge.json environment.weather</c>); null or an id without an entry is clear. The weather may
/// change during the run (<see cref="Shifts"/>), and a storm's thunder and lightning are rolled each round.
/// </summary>
public sealed class ScavengeEnvironment
{
    /// <summary>Hour of day the mission is played at (0..23), for the light on screen.</summary>
    public int Hour { get; set; } = 12;

    public DayPhase Phase { get; set; } = DayPhase.Day;

    public string? Weather { get; set; }

    /// <summary>How hard it rains or blows (0..1), for the look and sound; rules read <see cref="Weather"/>.</summary>
    public double Intensity { get; set; }

    /// <summary>Weather changes still to come, by the round they start (applied at that round's start).</summary>
    public List<WeatherShift> Shifts { get; set; } = new();

    /// <summary>Round in which thunder last rolled: it drowns out movement that round.</summary>
    public int ThunderRound { get; set; }

    /// <summary>Round in which lightning last struck: it shows every unit on the map to every side that round.</summary>
    public int LightningRound { get; set; }

    public static ScavengeEnvironment At(int hour, DayPhase phase, string? weather = null) => new() { Hour = hour, Phase = phase, Weather = weather };

    /// <summary>
    /// The environment at an hour by the default colony phase hours (<see cref="Data.DayNightRules"/>), for a server that has
    /// no colony rules loaded.
    /// </summary>
    public static ScavengeEnvironment AtHour(int hour, string? weather = null) =>
        At(((hour % 24) + 24) % 24, DayNight.PhaseAtHour(((hour % 24) + 24) % 24, new Data.DayNightRules()), weather);

    public ScavengeEnvironment Copy() => new()
    {
        Hour = Hour, Phase = Phase, Weather = Weather, Intensity = Intensity, Shifts = Shifts.ToList(),
        ThunderRound = ThunderRound, LightningRound = LightningRound,
    };

    public bool IsDark => Phase == DayPhase.Night;
}

/// <summary>How a unit senses the world, for the environment's effects on sight and stealth.</summary>
public enum SenseKind { Human, Husk, Linked, Drone, Camera, Turret }

/// <summary>The weather turns to <paramref name="Weather"/> at the start of <paramref name="Round"/>.</summary>
public sealed record WeatherShift(int Round, string Weather, double Intensity);

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

    /// <summary>Lowest sight the effects cut anyone to (combined: the lower floor, so thick weather can blind further).</summary>
    public double MinSight { get; init; } = 2;

    /// <summary>How far everyone hears noise and movement: radius × this (rain drowns it out).</summary>
    public double HearingMultiplier { get; init; } = 1;

    /// <summary>A flare burns for its rounds × this (at least one round; rain puts it out sooner).</summary>
    public double FlareRoundsMultiplier { get; init; } = 1;

    /// <summary>Taken off the hit chance of every ranged attack (dust in the air).</summary>
    public double RangedAccuracyPenalty { get; init; }

    /// <summary>Scout drones cannot take off or fly (they can still be recalled).</summary>
    public bool DronesGrounded { get; init; }

    /// <summary>The Linked's relay and pylon ranges × this (rounded down): static breaks their links.</summary>
    public double RelayRangeMultiplier { get; init; } = 1;

    /// <summary>Chance per round that thunder drowns out every footstep (no movement contacts that round).</summary>
    public double ThunderChance { get; init; }

    /// <summary>Chance per round that lightning shows every unit on the map to every side for that round.</summary>
    public double LightningChance { get; init; }

    public double SightFor(SenseKind kind) => Sight.GetValueOrDefault(kind, 1);

    public EnvironmentEffects Combine(EnvironmentEffects other) => new()
    {
        Sight = Enum.GetValues<SenseKind>().ToDictionary(k => k, k => SightFor(k) * other.SightFor(k)),
        StealthBonus = StealthBonus + other.StealthBonus,
        StealthIgnoredBy = StealthIgnoredBy.Union(other.StealthIgnoredBy).ToList(),
        HuskCountMultiplier = HuskCountMultiplier * other.HuskCountMultiplier,
        HuskHearingMultiplier = HuskHearingMultiplier * other.HuskHearingMultiplier,
        FlareRadiusMultiplier = FlareRadiusMultiplier * other.FlareRadiusMultiplier,
        MinSight = Math.Min(MinSight, other.MinSight),
        HearingMultiplier = HearingMultiplier * other.HearingMultiplier,
        FlareRoundsMultiplier = FlareRoundsMultiplier * other.FlareRoundsMultiplier,
        RangedAccuracyPenalty = RangedAccuracyPenalty + other.RangedAccuracyPenalty,
        DronesGrounded = DronesGrounded || other.DronesGrounded,
        RelayRangeMultiplier = RelayRangeMultiplier * other.RelayRangeMultiplier,
        ThunderChance = 1 - (1 - ThunderChance) * (1 - other.ThunderChance),
        LightningChance = 1 - (1 - LightningChance) * (1 - other.LightningChance),
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

    /// <summary>The effects on one unit's senses: the weather's are left out when its gear protects it from that weather.</summary>
    public static EnvironmentEffects EffectsFor(ScavengeData data, ScavengeState state, Unit unit)
    {
        var env = state.Environment;
        if (env.Weather is { } w && GearEffects.Protected(data, unit, w))
            return data.Rules.Environment.Phases.GetValueOrDefault(env.Phase) ?? new EnvironmentEffects();
        return Effects(data, state);
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
        var effects = EffectsFor(data, state, unit);
        var mult = effects.SightFor(SenseOf(data, state, unit));
        return mult >= 1 ? baseSight * mult : Math.Max(Math.Min(baseSight, effects.MinSight), baseSight * mult);
    }

    /// <summary>Lightning lights up the map this round: everyone on it is seen by every side.</summary>
    public static bool LightningNow(ScavengeState state) => state.Round > 0 && state.Environment.LightningRound == state.Round;

    /// <summary>Thunder this round drowns out movement.</summary>
    public static bool ThunderNow(ScavengeState state) => state.Round > 0 && state.Environment.ThunderRound == state.Round;

    /// <summary>
    /// Round start: a weather change due now takes over, then a storm rolls for thunder and lightning (drawing from the
    /// run's RNG only when the weather has them). Every side hears all of it: the weather is the same for everyone.
    /// </summary>
    internal static void RoundStarted(ScavengeSim sim)
    {
        var state = sim.State;
        var env = state.Environment;
        var due = env.Shifts.Where(s => s.Round <= state.Round).OrderBy(s => s.Round).ToList();
        if (due.Count > 0)
        {
            var shift = due[^1];
            env.Shifts.RemoveAll(s => s.Round <= state.Round);
            var was = env.Weather;
            env.Weather = shift.Weather;
            env.Intensity = shift.Intensity;
            if (was != shift.Weather)
                state.Log(new(state.Round, ScavengeEventType.WeatherChanged, $"The weather turns: {Name(shift.Weather)}."));
        }
        var effects = Effects(sim.Data, state);
        if (effects.ThunderChance > 0 && sim.Rng.NextDouble() < effects.ThunderChance)
        {
            env.ThunderRound = state.Round;
            state.Log(new(state.Round, ScavengeEventType.Thunder, "Thunder rolls: nobody hears footsteps this round."));
        }
        if (effects.LightningChance > 0 && sim.Rng.NextDouble() < effects.LightningChance)
        {
            env.LightningRound = state.Round;
            state.Log(new(state.Round, ScavengeEventType.Lightning, "Lightning! For a moment everyone outside is in plain view."));
        }
    }

    /// <summary>
    /// Weather for a mission with no colony behind it (a dedicated server, a debug run): from the seed alone, clear about
    /// half the time, else one of the field weathers in <c>scavenge.json</c> (by id order), at an intensity from the seed.
    /// </summary>
    public static (string Weather, double Intensity) RollWeather(ScavengeData data, ulong seed)
    {
        var rng = new Random.DeterministicRng(seed * 0x9E3779B97F4A7C15UL + 0x5EA7);
        var kinds = data.Rules.Environment.Weather.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var pick = rng.Next(0, 2 * Math.Max(1, kinds.Count));
        var intensity = Math.Round(0.4 + 0.6 * rng.NextDouble(), 3);
        return pick < kinds.Count ? (kinds[pick], intensity) : (Data.WeatherRules.Clear, 0);
    }

    /// <summary>A weather id as words ("dust_storm" → "dust storm").</summary>
    public static string Name(string? weather) => string.IsNullOrEmpty(weather) ? "clear" : weather.Replace('_', ' ');

    /// <summary>Stealth every target gains against this observer (0 for the kinds that see through it).</summary>
    public static double StealthBonusAgainst(ScavengeData data, ScavengeState state, Unit observer)
    {
        var effects = Effects(data, state);
        return effects.StealthIgnoredBy.Contains(SenseOf(data, state, observer)) ? 0 : effects.StealthBonus;
    }
}
