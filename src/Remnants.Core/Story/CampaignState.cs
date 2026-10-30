namespace Remnants.Core.Story;

public enum MissionStatus { Locked, Available, Active, Done, Failed }

/// <summary>One mission's place in the campaign. <see cref="Outcome"/>: "done" or "failed" once closed; a choice-keyed outcome later acts can read.</summary>
public sealed class MissionProgress
{
    public MissionStatus Status { get; set; }
    public string? Outcome { get; set; }
    public long? AvailableTick { get; set; }
    public long? DoneTick { get; set; }

    /// <summary>A failed mission comes back (in its retry form) at this tick.</summary>
    public long? RetryTick { get; set; }
    public int Attempts { get; set; }

    /// <summary>Colony objectives done so far (colony missions).</summary>
    public SortedSet<string> Objectives { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A scene's call was queued (colony missions).</summary>
    public bool SceneQueued { get; set; }

    /// <summary>The run was opened (the field result decides from here).</summary>
    public bool New { get; set; }
}

/// <summary>A call waiting on the Radio Mast (or taken, kept for replay).</summary>
public sealed class RadioCall
{
    public int Id { get; set; }
    public string DialogueId { get; set; } = "";
    public long Tick { get; set; }
    public bool Taken { get; set; }

    /// <summary>Put in the Radio Mast's queue (tag <c>story:&lt;Id&gt;</c>) once its time came.</summary>
    public bool Rung { get; set; }
}

/// <summary>The dialogue on screen: which graph, which line, and the call it came from.</summary>
public sealed class DialogueSession
{
    public string DialogueId { get; set; } = "";
    public string NodeId { get; set; } = "";
    public int? CallId { get; set; }
}

/// <summary>A line of the dialogue log (history): what was said, resolved, and which choice was picked.</summary>
public sealed record StoryLine(long Tick, string DialogueId, string Speaker, string Text, bool Choice = false);

/// <summary>A named character's state in this campaign.</summary>
public sealed class CharacterState
{
    public bool Met { get; set; }
    public bool Joined { get; set; }
    public bool Dead { get; set; }
}

/// <summary>
/// The campaign's whole state, in <c>ColonyState.Campaign</c> (null = Sandbox; every old save loads as Sandbox).
/// Plain data; the rules are in <see cref="Campaign"/> and <see cref="Dialogue"/>.
/// </summary>
public sealed class CampaignState
{
    public const int MaxLog = 400;

    /// <summary>Remnant (default) or Survivor (gentler).</summary>
    public string Difficulty { get; set; } = "remnant";
    public SortedDictionary<string, MissionProgress> Missions { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Flags { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, CharacterState> Characters { get; set; } = new(StringComparer.Ordinal);
    public List<RadioCall> Inbox { get; set; } = new();
    public int NextCallId { get; set; } = 1;
    public DialogueSession? Active { get; set; }

    /// <summary>Dialogues played to the end.</summary>
    public SortedSet<string> Heard { get; set; } = new(StringComparer.Ordinal);
    public List<StoryLine> Log { get; set; } = new();

    /// <summary>The last mission result, for the debrief panel.</summary>
    public MissionDebrief? LastDebrief { get; set; }
    public string? Ending { get; set; }

    public MissionProgress Progress(string id) => Missions.TryGetValue(id, out var p) ? p : Missions[id] = new MissionProgress();
    public int Flag(string id) => Flags.GetValueOrDefault(id);

    public CharacterState Character(string id) => Characters.TryGetValue(id, out var c) ? c : Characters[id] = new CharacterState();

    public void AddLog(StoryLine line)
    {
        Log.Add(line);
        if (Log.Count > MaxLog) Log.RemoveRange(0, Log.Count - MaxLog);
    }
}

/// <summary>What a mission came to and paid, for the debrief panel.</summary>
public sealed record MissionDebrief(string MissionId, string Outcome, long Tick, List<string> Rewards, List<string> Objectives);
