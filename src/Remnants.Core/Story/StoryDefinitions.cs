using Remnants.Core.Colony;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Story;

/// <summary>A named story character (data/story/characters.json). Portraits resolve by <see cref="Id"/>.</summary>
public sealed record CharacterDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Pronouns { get; init; } = "";
    public string Role { get; init; } = "";

    /// <summary>The name a radio call shows ("Lantern").</summary>
    public string? CallSign { get; init; }
}

/// <summary>
/// One condition; every field that is set must hold. Used by mission <c>requires</c>, dialogue choices and conditional
/// effects: a mission done (with an optional outcome), a building at a level, the Town Hall level, a colony day, a
/// campaign flag in a range, items in the inventory, a character alive.
/// </summary>
public sealed record ConditionDef
{
    public string? Mission { get; init; }
    public string? Outcome { get; init; }
    public string? Building { get; init; }
    public int Level { get; init; } = 1;
    public int? TownHall { get; init; }
    public int? Day { get; init; }
    public string? Flag { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
    public string? Item { get; init; }
    public int Count { get; init; } = 1;
    public string? Alive { get; init; }
}

public enum EffectKind
{
    /// <summary>Flag <see cref="EffectDef.Id"/> = <see cref="EffectDef.Value"/>.</summary>
    SetFlag,
    /// <summary>Flag <see cref="EffectDef.Id"/> += <see cref="EffectDef.Value"/>.</summary>
    AddFlag,
    /// <summary>Schematic <see cref="EffectDef.Id"/> through <see cref="ISchematicGrants"/>.</summary>
    Schematic,
    /// <summary><see cref="EffectDef.Value"/> × item <see cref="EffectDef.Id"/> into the inventory (negative takes).</summary>
    Item,
    /// <summary><see cref="EffectDef.Value"/> of resource <see cref="EffectDef.Id"/> into the stockpile.</summary>
    Resource,
    ResearchData,
    /// <summary>Zone <see cref="EffectDef.Id"/> becomes Rumored (or Scouted with Value 2).</summary>
    RevealZone,
    /// <summary>Queues dialogue <see cref="EffectDef.Id"/> as a radio call, <see cref="EffectDef.Value"/> hours from now.</summary>
    Call,
    /// <summary>Character <see cref="EffectDef.Id"/> is met (Value 1) or joins (Value 2).</summary>
    Character,
    /// <summary>
    /// A reward for a feature not built yet (vehicles, prosthetic tiers): the schematic is granted anyway and this
    /// stands in, told in <see cref="EffectDef.Text"/>.
    /// </summary>
    Fallback,
    /// <summary>A line in the colony log and a toast.</summary>
    Note,
}

/// <summary>
/// The one small effect vocabulary of the story: mission rewards, dialogue node and choice effects, fail effects.
/// <see cref="When"/> makes it conditional (a reward for one outcome only).
/// </summary>
public sealed record EffectDef
{
    public EffectKind Kind { get; init; }
    public string Id { get; init; } = "";
    public int Value { get; init; } = 1;
    public string? Text { get; init; }
    public ConditionDef? When { get; init; }
}

public sealed record ChoiceDef
{
    public string Text { get; init; } = "";
    public string? Next { get; init; }
    public List<EffectDef> Effects { get; init; } = new();
    public List<ConditionDef> Requires { get; init; } = new();

    /// <summary>The known consequence when it is obvious ("Cole will remember this"). Never numbers.</summary>
    public string? Hint { get; init; }
}

/// <summary>
/// One line of a dialogue. <see cref="Speaker"/> is a character id, <c>player</c> (the colony's lead survivor),
/// <c>pell</c> (the colony's own Pell) or <c>narration</c>. Without <see cref="Choices"/> it goes on to
/// <see cref="Next"/>, or ends the dialogue. <see cref="Effects"/> apply when the line is shown.
/// </summary>
public sealed record DialogueNode
{
    public string Id { get; init; } = "";
    public string Speaker { get; init; } = "narration";
    public string Text { get; init; } = "";
    public string? Next { get; init; }
    public List<EffectDef> Effects { get; init; } = new();
    public List<ChoiceDef> Choices { get; init; } = new();

    /// <summary>The speaker's portrait mood ("neutral", "worried", "angry"); null = neutral.</summary>
    public string? Mood { get; init; }

    /// <summary>A conditional line: skipped (straight on to <see cref="Next"/>) unless this holds.</summary>
    public ConditionDef? When { get; init; }
}

public enum DialogueMode { Radio, InPerson }

/// <summary>A dialogue graph (data/story/dialogue/*.json); it starts at its first node.</summary>
public sealed record DialogueDef
{
    public string Id { get; init; } = "";
    public DialogueMode Mode { get; init; }
    public string Caller { get; init; } = "";
    public string Title { get; init; } = "";
    public List<DialogueNode> Nodes { get; init; } = new();

    public DialogueNode? Node(string id) => Nodes.Find(n => n.Id == id);
}

public enum ColonyObjectiveKind
{
    /// <summary>A building <see cref="ColonyObjectiveDef.Building"/> at <see cref="ColonyObjectiveDef.Level"/> or more.</summary>
    Build,
    HaveItem,
    HaveResource,
    /// <summary>Someone is a patient in a Med Bay.</summary>
    Patient,
    /// <summary>A child works at a building with job <see cref="ColonyObjectiveDef.Job"/>.</summary>
    ChildJob,
    /// <summary>
    /// A scene: once every other objective is done (and, with <see cref="ColonyObjectiveDef.Phase"/>, at that time of
    /// day) dialogue <see cref="ColonyObjectiveDef.Dialogue"/> calls; done when it has been played to the end.
    /// </summary>
    Scene,
}

public sealed record ColonyObjectiveDef
{
    public string Id { get; init; } = "";
    public ColonyObjectiveKind Kind { get; init; }
    public string Text { get; init; } = "";
    public string? Building { get; init; }
    public int Level { get; init; } = 1;
    public string? Item { get; init; }
    public string? Resource { get; init; }
    public int Count { get; init; } = 1;
    public string? Job { get; init; }
    public string? Dialogue { get; init; }
    public DayPhase? Phase { get; init; }
}

public enum MissionType
{
    /// <summary>Played in the colony (M1, the colony tasks): <see cref="MissionDef.ColonyObjectives"/>.</summary>
    Colony,
    /// <summary>Played in a zone or on the perimeter: <see cref="MissionDef.Field"/>.</summary>
    Field,
}

/// <summary>What happens when a mission fails: effects, and either a retry after some days (harder) or a final close.</summary>
public sealed record FailDef
{
    public int? RetryDays { get; init; }
    public List<EffectDef> Effects { get; init; } = new();
    public string? Call { get; init; }
    public List<MapEnemyDef> ExtraEnemies { get; init; } = new();
}

/// <summary>
/// A story mission (data/story/missions/*.json). <see cref="Map"/>: <c>handmade:&lt;id&gt;</c>, <c>perimeter</c> (the
/// colony perimeter variant for the Town Hall level), or <c>generated</c> with an optional fixed <see cref="Room"/>.
/// <see cref="Main"/>: a gold diamond on the world map (else a hollow one; colony tasks are neither).
/// </summary>
public sealed record MissionDef
{
    public string Id { get; init; } = "";
    public int Act { get; init; } = 1;
    public string Name { get; init; } = "";
    public MissionType Type { get; init; }
    public bool Main { get; init; } = true;
    public bool Task { get; init; }
    public string Summary { get; init; } = "";
    public string Teaches { get; init; } = "";
    public string? Zone { get; init; }
    public int Threat { get; init; }
    public string Map { get; init; } = "generated";
    public string? Room { get; init; }
    public int? MaxParty { get; init; }
    public List<ConditionDef> Requires { get; init; } = new();
    public string NeedsText { get; init; } = "";
    public string? Briefing { get; init; }
    public string? Debrief { get; init; }
    public List<ColonyObjectiveDef> ColonyObjectives { get; init; } = new();
    public FieldMission? Field { get; init; }
    public List<EffectDef> Rewards { get; init; } = new();
    public FailDef Fail { get; init; } = new();

    public bool Perimeter => Map == "perimeter";
    public string? HandmadeId => Map.StartsWith("handmade:", StringComparison.Ordinal) ? Map["handmade:".Length..] : null;
}
