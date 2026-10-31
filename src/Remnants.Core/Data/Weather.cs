namespace Remnants.Core.Data;

/// <summary>
/// Weather over the colony and the world map (data/weather.json): the kinds, how long a spell lasts, what follows what
/// (a Markov chain, weighted per region), the forecast, and what each kind does at home, on the road and in the field.
/// Field effects live in <c>scavenge.json environment.weather</c>, keyed by the same ids. Disabled (no file, or
/// <c>enabled: false</c>) means it is always clear.
/// </summary>
public sealed record WeatherRules
{
    public bool Enabled { get; init; }

    /// <summary>The region the colony stands in (its weather is the one at home).</summary>
    public string ColonyRegion { get; init; } = "";

    /// <summary>The kind every chain starts in.</summary>
    public string Start { get; init; } = Clear;

    /// <summary>Kind id → definition. Ids match <c>scavenge.json environment.weather</c>.</summary>
    public Dictionary<string, WeatherKindDef> Kinds { get; init; } = new();

    /// <summary>Kind id → (next kind id → weight): what a spell turns into. A kind never follows itself.</summary>
    public Dictionary<string, Dictionary<string, double>> Next { get; init; } = new();

    /// <summary>Region → (kind id → weight multiplier): the wastes get more dust, the valley more rain.</summary>
    public Dictionary<string, Dictionary<string, double>> Regions { get; init; } = new();

    public ForecastRules Forecast { get; init; } = new();

    /// <summary>Weather changing during a mission: field rounds per colony hour, and how far ahead a run looks.</summary>
    public FieldWeatherRules Field { get; init; } = new();

    /// <summary>Lightning damage to buildings and its repair.</summary>
    public RepairRules Repair { get; init; } = new();


    /// <summary>Items in the stores that count as dust masks for outdoor workers (one each).</summary>
    public List<string> MaskItems { get; init; } = new();

    public const string Clear = "clear";

    public WeatherKindDef Kind(string id) => Kinds.GetValueOrDefault(id) ?? WeatherKindDef.None;
}

public sealed record WeatherKindDef
{
    public static readonly WeatherKindDef None = new();

    public string Name { get; init; } = "Clear";

    /// <summary>[min, max] hours a spell lasts (drawn uniformly).</summary>
    public int[] Hours { get; init; } = [12, 24];

    /// <summary>[min, max] intensity 0..1 (drawn uniformly), for the look and sound and "heavy" rain.</summary>
    public double[] Intensity { get; init; } = [0.5, 0.5];

    /// <summary>A forecast that misses names this kind instead (rain for a storm, overcast for fog).</summary>
    public string? ConfusedWith { get; init; }

    /// <summary>Scout drones stay grounded (always, or from <see cref="GroundsDronesFrom"/> intensity up).</summary>
    public bool GroundsDrones { get; init; }

    /// <summary>Drones are grounded at this intensity and up (heavy rain); null: only with <see cref="GroundsDrones"/>.</summary>
    public double? GroundsDronesFrom { get; init; }

    /// <summary>Starts and ends of this kind are worth a toast (storms).</summary>
    public bool Severe { get; init; }

    public ColonyWeatherEffects Colony { get; init; } = new();

    public TravelWeatherEffects Travel { get; init; } = new();
}

/// <summary>What a kind does at home, per tick. Multipliers default to 1, everything else to nothing.</summary>
public sealed record ColonyWeatherEffects
{
    /// <summary>Output of workers at outdoor buildings (rain slows them, a storm more).</summary>
    public double OutdoorOutput { get; init; } = 1;

    /// <summary>Builders' output at construction sites (null: <see cref="OutdoorOutput"/>; a dust storm halts building).</summary>
    public double? BuildOutput { get; init; }

    /// <summary>Crop yield multiplier (rain waters the fields, dust buries them); applied through <c>Crops.Modifiers</c>.</summary>
    public double CropOutput { get; init; } = 1;

    /// <summary>
    /// Outdoor workers with a dust mask work at this instead of <see cref="OutdoorOutput"/> (null: masks make no
    /// difference). One mask in the stores covers one worker.
    /// </summary>
    public double? MaskedOutput { get; init; }

    /// <summary>Health lost per tick by an outdoor worker on shift without a mask (only with <see cref="MaskedOutput"/>).</summary>
    public double UnmaskedHealthPerTick { get; init; }

    /// <summary>Children do no outdoor work (they stay in).</summary>
    public bool ChildrenStayIn { get; init; }

    /// <summary>Mood lost per tick by everyone at home.</summary>
    public double MoodPerTick { get; init; }

    /// <summary>The evening campfire is out (no campfire mood).</summary>
    public bool NoCampfire { get; init; }

    /// <summary>Chance per tick that lightning damages a building (at full intensity; scaled by intensity).</summary>
    public double LightningChancePerTick { get; init; }
}

/// <summary>What a kind does to a party on the road (see <c>TravelModifier</c>).</summary>
public sealed record TravelWeatherEffects
{
    /// <summary>Days added to a trip that sets out in this weather (at home or at the far end).</summary>
    public double ExtraDays { get; init; }

    /// <summary>Raging over a zone this many travel days out or more, the route is closed (null: never).</summary>
    public double? BlocksFromDays { get; init; }
}

public sealed record ForecastRules
{
    /// <summary>Days ahead the colony can see, by Town Hall level (1-based; the last entry for higher levels).</summary>
    public List<int> DaysByTownHallLevel { get; init; } = [1, 2, 3];

    /// <summary>Extra days with an Electronics Lab at <see cref="LabMinLevel"/> or above.</summary>
    public int LabDays { get; init; } = 1;

    public string LabBuildingId { get; init; } = "electronics_lab";
    public int LabMinLevel { get; init; } = 2;

    /// <summary>Chance the next spell is called right; loses <see cref="AccuracyLossPerDay"/> per day it lies ahead.</summary>
    public double Accuracy { get; init; } = 0.75;
    public double AccuracyLossPerDay { get; init; } = 0.1;

    /// <summary>Items in the stores that read the sky (a scanner, a radio): one adds <see cref="InstrumentAccuracy"/>.</summary>
    public List<string> InstrumentItems { get; init; } = new();
    public double InstrumentAccuracy { get; init; } = 0.15;

    public double LabAccuracyPerLevel { get; init; } = 0.03;
    public double MaxAccuracy { get; init; } = 0.97;
}

public sealed record FieldWeatherRules
{
    /// <summary>Field rounds per colony hour: a spell that starts 2 hours after arrival starts at round 1 + 2 × this.</summary>
    public int RoundsPerHour { get; init; } = 4;

    /// <summary>Hours after arrival the mission looks ahead for changes.</summary>
    public int LookaheadHours { get; init; } = 8;
}

public sealed record RepairRules
{
    /// <summary>A building struck by lightning works at this output until repaired.</summary>
    public double DamagedOutput { get; init; } = 0.5;

    public Dictionary<string, double> Cost { get; init; } = new();
}
