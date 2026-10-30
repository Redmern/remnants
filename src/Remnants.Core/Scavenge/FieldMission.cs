namespace Remnants.Core.Scavenge;

/// <summary>
/// What an objective asks for (see <see cref="MissionRules"/> for when each passes or fails):
/// <list type="bullet">
/// <item><see cref="Reach"/>: a party unit stands within <see cref="ObjectiveDef.Radius"/> of a <see cref="ObjectiveDef.Marker"/> tile.</item>
/// <item><see cref="Hack"/>: the device tagged <see cref="ObjectiveDef.Tag"/> is hacked by the party (fails if it is destroyed).</item>
/// <item><see cref="Retrieve"/>: the item <see cref="ObjectiveDef.Item"/> is carried out (done at extraction).</item>
/// <item><see cref="Escort"/>: the unit tagged <see cref="ObjectiveDef.Tag"/> leaves alive with the party (fails if it dies).</item>
/// <item><see cref="Aid"/>: a party unit treats the unit tagged <see cref="ObjectiveDef.Tag"/> with a healing item.</item>
/// <item><see cref="Survive"/>: <see cref="ObjectiveDef.Count"/> rounds pass after it becomes active.</item>
/// <item><see cref="Defend"/>: the unit tagged <see cref="ObjectiveDef.Tag"/> is still standing at the end (fails when it dies).</item>
/// <item><see cref="KillTag"/>: <see cref="ObjectiveDef.Count"/> units tagged <see cref="ObjectiveDef.Tag"/> are dead.</item>
/// <item><see cref="Undetected"/>: stealth-only; fails as soon as a hostile picks out a party unit.</item>
/// </list>
/// </summary>
public enum ObjectiveKind { Reach, Hack, Retrieve, Escort, Aid, Survive, Defend, KillTag, Undetected }

public enum ObjectiveStatus { Inactive, Active, Done, Failed }

/// <summary>One objective of a scripted mission. <see cref="FailsMission"/>: failing it fails the whole mission.</summary>
public sealed record ObjectiveDef
{
    public string Id { get; init; } = "";
    public ObjectiveKind Kind { get; init; }
    public string Text { get; init; } = "";
    public string? Marker { get; init; }
    public int Radius { get; init; } = 1;
    public string? Tag { get; init; }
    public string? Item { get; init; }
    public int Count { get; init; } = 1;
    public bool Optional { get; init; }
    public bool FailsMission { get; init; }

    /// <summary>Starts inactive (hidden from the panel) until a trigger activates it.</summary>
    public bool Hidden { get; init; }
}

/// <summary>When a trigger fires. Each fires once.</summary>
public enum TriggerKind
{
    /// <summary>When the run starts.</summary>
    Start,
    /// <summary>At the start of round <see cref="TriggerWhen.Round"/>.</summary>
    Round,
    /// <summary>A party unit enters within <see cref="TriggerWhen.Radius"/> of the <see cref="TriggerWhen.Marker"/>.</summary>
    Enter,
    ObjectiveDone,
    ObjectiveFailed,
    /// <summary>A party unit picks up <see cref="TriggerWhen.Item"/>.</summary>
    PickedUp,
    /// <summary>The party's side sees a unit tagged <see cref="TriggerWhen.Tag"/>.</summary>
    Sees,
    /// <summary>A hack fails anywhere (the Network or the noise is up).</summary>
    Alarm,
    /// <summary>A prompt's option was chosen (<see cref="TriggerWhen.Choice"/> = "promptId:option").</summary>
    Chosen,
}

public sealed record TriggerWhen
{
    public TriggerKind Kind { get; init; }
    public int Round { get; init; }
    public string? Marker { get; init; }
    public int Radius { get; init; } = 1;
    public string? Objective { get; init; }
    public string? Item { get; init; }
    public string? Tag { get; init; }
    public string? Choice { get; init; }
}

/// <summary>What a trigger does.</summary>
public enum TriggerActionKind
{
    /// <summary><see cref="TriggerAction.Count"/> × <see cref="TriggerAction.Enemy"/> at the <see cref="TriggerAction.Marker"/> tiles, tagged; hunting toward <see cref="TriggerAction.Goal"/> if set.</summary>
    Spawn,
    /// <summary>A chatter line (<see cref="TriggerAction.Speaker"/>, <see cref="TriggerAction.Text"/>) above the action bar; does not pause.</summary>
    Say,
    Activate,
    Complete,
    Fail,
    /// <summary>Sets (or with <see cref="TriggerAction.Add"/> adds to) a field flag that goes home in the result.</summary>
    Flag,
    /// <summary>Puts the units tagged <see cref="TriggerAction.Tag"/> on <see cref="TriggerAction.Side"/> ("party" = the party's side, "hostile" = their own band, "neutral").</summary>
    SetSide,
    /// <summary>The units tagged <see cref="TriggerAction.Tag"/> join the nearest party unit as followers.</summary>
    Follow,
    /// <summary>A pausing choice (<see cref="TriggerAction.Prompt"/>) the player answers with <see cref="ChooseOption"/>.</summary>
    Prompt,
    /// <summary>Removes half of the items left in the device tagged <see cref="TriggerAction.Tag"/> (a split of the spoils).</summary>
    Split,
    /// <summary>Idle units tagged <see cref="TriggerAction.Tag"/> start hunting toward <see cref="TriggerAction.Goal"/>.</summary>
    Wake,
}

public sealed record TriggerAction
{
    public TriggerActionKind Kind { get; init; }
    public string? Enemy { get; init; }
    public int Count { get; init; } = 1;
    public string? Marker { get; init; }
    public string? Goal { get; init; }
    public string? Tag { get; init; }
    public string? Speaker { get; init; }
    public string? Text { get; init; }
    public string? Objective { get; init; }
    public string? Flag { get; init; }
    public int Value { get; init; }
    public bool Add { get; init; }
    public string? Side { get; init; }
    public PromptDef? Prompt { get; init; }
}

public sealed record TriggerDef
{
    public string Id { get; init; } = "";
    public TriggerWhen When { get; init; } = new();
    public List<TriggerAction> Actions { get; init; } = new();
}

/// <summary>A choice in the field: the game pauses until the player picks an option (each fires a <see cref="TriggerKind.Chosen"/> trigger).</summary>
public sealed record PromptDef
{
    public string Id { get; init; } = "";
    public string Speaker { get; init; } = "";
    public string Text { get; init; } = "";
    public List<PromptOption> Options { get; init; } = new();
}

/// <summary><see cref="Hint"/>: the known consequence, shown when it is obvious ("Cole will remember this"), never numbers.</summary>
public sealed record PromptOption(string Id, string Text, string? Hint = null);

/// <summary>
/// The script of a story mission in the field: objectives, triggers and how it ends. <see cref="AtHome"/>: fought at
/// the colony (a defense): the run ends as soon as the mission is done or failed, and everyone still standing is home.
/// <see cref="FailOnWipe"/>: a party that leaves nobody alive and out fails it.
/// </summary>
public sealed record FieldMission
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<ObjectiveDef> Objectives { get; init; } = new();
    public List<TriggerDef> Triggers { get; init; } = new();
    public bool AtHome { get; init; }
    public bool FailOnWipe { get; init; } = true;
}

/// <summary>A chatter line said in the field (radio, or someone on the map).</summary>
public sealed record ChatterLine(int Round, string Speaker, string Text);

public sealed class ObjectiveState
{
    public string Id { get; set; } = "";
    public ObjectiveStatus Status { get; set; }
    public int Progress { get; set; }
    public int StartRound { get; set; }
}

public enum MissionOutcome { Incomplete, Done, Failed }

/// <summary>
/// A story mission's live state in a scavenge run: its script (copied in, so a save carries it whole), objective
/// progress, fired triggers, chatter, the pending prompt, field flags and the named markers of a handmade map.
/// </summary>
public sealed class MissionRun
{
    public FieldMission Def { get; set; } = new();

    /// <summary>The handmade map (or room) the run was built from and a hash of its data (see <see cref="HandmadeMaps.Hash"/>).</summary>
    public string? MapId { get; set; }
    public string? MapHash { get; set; }

    public List<ObjectiveState> Objectives { get; set; } = new();
    public SortedSet<string> Fired { get; set; } = new(StringComparer.Ordinal);
    public List<ChatterLine> Chatter { get; set; } = new();
    public SortedDictionary<string, int> Flags { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Prompt ids answered, with the option picked.</summary>
    public SortedDictionary<string, string> Choices { get; set; } = new(StringComparer.Ordinal);
    public PromptDef? Pending { get; set; }

    /// <summary>Named tiles from the map's legend.</summary>
    public SortedDictionary<string, List<Pos>> Markers { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Raised tiles (watchtowers): more sight and detection for whoever stands there.</summary>
    public List<Pos> Raised { get; set; } = new();

    /// <summary>Unit ids a party unit has treated (for <see cref="ObjectiveKind.Aid"/>).</summary>
    public SortedSet<int> Aided { get; set; } = new();

    public MissionOutcome Outcome { get; set; }

    public ObjectiveState? Find(string id) => Objectives.Find(o => o.Id == id);
    public ObjectiveDef? DefOf(string id) => Def.Objectives.Find(o => o.Id == id);
    public List<Pos> Marker(string? name) => name is not null && Markers.TryGetValue(name, out var tiles) ? tiles : [];
}

/// <summary>What a story mission came to, carried home in the <see cref="ExtractionResult"/>.</summary>
public sealed record ObjectiveResult(
    string MissionId,
    MissionOutcome Outcome,
    SortedDictionary<string, ObjectiveStatus> Objectives,
    SortedDictionary<string, int> Flags,
    SortedDictionary<string, string> Choices,
    List<string> Escorted);
