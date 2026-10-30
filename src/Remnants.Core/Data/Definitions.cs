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

    /// <summary>Other skills this job trains on the side, as a share of the main skill's gain (electronics → Hacking 0.3).</summary>
    public Dictionary<Skill, double> Trains { get; init; } = new();

    /// <summary>Light work children may do as apprentices (at <c>output.childOutputMultiplier</c>; they train faster).</summary>
    public bool ChildrenAllowed { get; init; }
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
/// Something a production building makes (data/recipes.json). A batch's inputs (resources and items) are paid when
/// it starts; the building's workers add their job output as effort each tick until it reaches <see cref="Work"/>,
/// then the batch yields <see cref="Amount"/> of an item (into the colony inventory, at a rolled quality for gear) or
/// of a resource (into the stockpile), or teaches a schematic (<see cref="Learns"/>: the synthesized Decrypt orders).
/// Recipes a schematic unlocks (data/schematics.json) stay locked until the colony knows it.
/// </summary>
public sealed record RecipeDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Building type that makes it; its job does the work and trains.</summary>
    public string Building { get; init; } = "";
    public int MinLevel { get; init; } = 1;
    public double Work { get; init; } = 10;
    public Dictionary<string, double> Inputs { get; init; } = new();

    /// <summary>Items used up per batch: item id → count.</summary>
    public Dictionary<string, int> ItemInputs { get; init; } = new();

    /// <summary>
    /// Food from the pantry used up per batch: a food item id or <c>tag:x</c> (any food tagged x, soonest to spoil
    /// first, never spoiled) → count. Food outputs go into the pantry as a lot with the cook's quality.
    /// </summary>
    public Dictionary<string, int> Ingredients { get; init; } = new();
    public string? Item { get; init; }
    public string? Resource { get; init; }

    /// <summary>A vehicle tier id (data/vehicles.json): a finished batch rolls a new vehicle out of the Garage.</summary>
    public string? Vehicle { get; init; }
    public double Amount { get; init; } = 1;

    /// <summary>At least one worker needs this much of the job's skill, or the building idles.</summary>
    public double MinSkill { get; init; }

    /// <summary>Decrypt orders: the schematic learned when the batch is done (its item is the one input).</summary>
    public string? Learns { get; init; }
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

    /// <summary>Work happens outside: a Night Shift here needs light (level ≥ <c>dayNight.litMinLevel</c> or a Floodlight).</summary>
    public bool Outdoor { get; init; }

    /// <summary>Town Hall level needed to construct it (on top of the Town Hall capping every building's level).</summary>
    public int MinTownHallLevel { get; init; }

    /// <summary>Index 0 is level 1: its cost is the construction cost.</summary>
    public List<BuildingLevelDef> Levels { get; init; } = new();

    public int MaxLevel => Levels.Count;

    public BuildingLevelDef Level(int level) => Levels[level - 1];
}
