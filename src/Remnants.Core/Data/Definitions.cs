using Remnants.Core.Survivors;

namespace Remnants.Core.Data;

public sealed record ResourceDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

/// <summary>A job: which attributes, skill and body regions drive its output.</summary>
public sealed record JobDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public Skill Skill { get; init; }
    public double SkillWeight { get; init; } = 1.0;
    public Dictionary<AttributeKind, double> Attributes { get; init; } = new();
    public Dictionary<BodyRegion, double> BodyParts { get; init; } = new();

    /// <summary>Output bonus at tool quality 1 (0.5 = +50%).</summary>
    public double ToolBonus { get; init; }
}

public enum BuildingCategory { Core, Housing, Gathering, Production, Care }

public sealed record BuildingLevelDef
{
    public Dictionary<string, double> Cost { get; init; } = new();
    public int WorkerSlots { get; init; }
    public double OutputMultiplier { get; init; } = 1.0;
    public int Beds { get; init; }

    /// <summary>Care buildings: how many patients can be admitted at once.</summary>
    public int Patients { get; init; }
}

/// <summary>
/// Something a production building makes. Inputs are paid when a batch starts; workers add effort each tick
/// (their job output) until it reaches <see cref="Work"/>, then the batch yields <see cref="Amount"/> of an item
/// (into the colony inventory) or of a resource (into the stockpile).
/// </summary>
public sealed record RecipeDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int MinLevel { get; init; } = 1;
    public double Work { get; init; } = 10;
    public Dictionary<string, double> Inputs { get; init; } = new();
    public string? Item { get; init; }
    public string? Resource { get; init; }
    public double Amount { get; init; } = 1;
}

public sealed record BuildingDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public BuildingCategory Category { get; init; }

    /// <summary>Only one may exist (e.g. Town Hall).</summary>
    public bool Unique { get; init; }

    /// <summary>Job id workers perform here, if the building takes workers.</summary>
    public string? Job { get; init; }

    /// <summary>Resource id produced by workers, if any.</summary>
    public string? Produces { get; init; }

    /// <summary>Units per worker per tick at performance 1, before level multiplier.</summary>
    public double BaseOutput { get; init; }

    public List<RecipeDef> Recipes { get; init; } = new();

    /// <summary>Index 0 is level 1: its cost is the construction cost.</summary>
    public List<BuildingLevelDef> Levels { get; init; } = new();

    public int MaxLevel => Levels.Count;

    public BuildingLevelDef Level(int level) => Levels[level - 1];
}
