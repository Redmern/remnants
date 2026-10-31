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

    /// <summary>Who takes over the character's role once they are dead ("dee" for Marrow): <c>{name:marrow}</c> then names them.</summary>
    public string? Successor { get; init; }

    /// <summary>A character who can join the colony: the survivor they become (see <see cref="Campaign.Join"/>).</summary>
    public UniqueSurvivorDef? Survivor { get; init; }
}

/// <summary>
/// The survivor a story character becomes when they join: fixed sex and age, attributes and skills (the rest rolled
/// as for a recruit), perks, story traits (<c>data/story/traits.json</c>) and a starting humanity below 100 for
/// someone who was never quite all flesh (Seven: 0).
/// </summary>
public sealed record UniqueSurvivorDef
{
    /// <summary>The name on the roster ("Doc Hale"); the character's name when unset.</summary>
    public string? Name { get; init; }
    public string Sex { get; init; } = "Female";
    public int AgeYears { get; init; } = 30;
    public Dictionary<string, double> Attributes { get; init; } = new();
    public Dictionary<string, double> Skills { get; init; } = new();
    public List<string> Perks { get; init; } = new();
    public List<string> Traits { get; init; } = new();
    public double Humanity { get; init; } = 100;
}

/// <summary>
/// A unique survivor's trait: a name, a line, and hooks (the perk hook ids of <c>PerkHooks</c>, colony and field
/// alike). The hooks are copied onto the survivor when they join (<c>Survivor.TraitHooks</c>), so the colony and a
/// scavenge server read them without the story data.
/// </summary>
public sealed record TraitDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
    public Dictionary<string, double> Hooks { get; init; } = new();
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

    /// <summary>The character is dead.</summary>
    public string? Dead { get; init; }

    /// <summary>The character has joined the colony and is in it now (not dead, not left).</summary>
    public string? Joined { get; init; }

    /// <summary>The colony's unique survivor (Pell) is an adult.</summary>
    public string? Adult { get; init; }

    /// <summary>
    /// Humanity in a range: the average of the colony's adults, or with <see cref="Of"/> one unique survivor's own
    /// (false if they are not in the colony).
    /// </summary>
    public int? HumanityMin { get; init; }
    public int? HumanityMax { get; init; }
    public string? Of { get; init; }

    /// <summary>An adult at home has <see cref="Skill"/> at <see cref="SkillMin"/> or more.</summary>
    public string? Skill { get; init; }
    public int SkillMin { get; init; }

    /// <summary>At least one of these holds.</summary>
    public List<ConditionDef>? Any { get; init; }

    /// <summary>This does not hold.</summary>
    public ConditionDef? Not { get; init; }
}

public enum EffectKind
{
    /// <summary>Flag <see cref="EffectDef.Id"/> = <see cref="EffectDef.Value"/>.</summary>
    SetFlag,
    /// <summary>Flag <see cref="EffectDef.Id"/> += <see cref="EffectDef.Value"/>.</summary>
    AddFlag,
    /// <summary>Schematic <see cref="EffectDef.Id"/>, learned through <c>Schematics.Grant</c> (source Story).</summary>
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
    /// <summary>Character <see cref="EffectDef.Id"/>'s mood (as a colony survivor) changes by <see cref="EffectDef.Value"/>.</summary>
    Mood,
    /// <summary>Character <see cref="EffectDef.Id"/>'s humanity changes by <see cref="EffectDef.Value"/>.</summary>
    Humanity,
    /// <summary>Character <see cref="EffectDef.Id"/> leaves the colony (alive; <see cref="EffectDef.Text"/> is the log line).</summary>
    Leaves,
    /// <summary>Character <see cref="EffectDef.Id"/> is dead (and leaves the roster, to the memorial, if they had joined).</summary>
    Dies,
    /// <summary>Mission <see cref="EffectDef.Id"/> closes as done with outcome <see cref="EffectDef.Text"/> (default "done"), no rewards.</summary>
    Close,
    /// <summary>World effect <see cref="EffectDef.Id"/> for <see cref="EffectDef.Value"/> days (0 = for good). See <see cref="WorldEffects"/>.</summary>
    WorldEffect,
    /// <summary>World effect <see cref="EffectDef.Id"/> ends now.</summary>
    EndWorldEffect,
    /// <summary>The campaign ends with ending <see cref="EffectDef.Id"/> (silence, terms, voice, calm): the epilogue plays.</summary>
    Ending,
    /// <summary>
    /// A new adult recruit named <see cref="EffectDef.Text"/> (rolled as any recruit) joins; <see cref="EffectDef.Value"/>
    /// is their humanity (100 = untouched). With <see cref="EffectDef.Id"/> "child", a child.
    /// </summary>
    Recruit,
    /// <summary>The survivors Halcyon took come home, each with <see cref="EffectDef.Value"/> humanity lost.</summary>
    ReturnTaken,
    /// <summary>Character <see cref="EffectDef.Id"/> (a unique survivor) is renamed <see cref="EffectDef.Text"/>.</summary>
    Rename,
    /// <summary>A vehicle of tier <see cref="EffectDef.Id"/> (vehicles.json) joins the colony's yard (Juniper's handcart).</summary>
    Vehicle,
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
    /// <see cref="ColonyObjectiveDef.Count"/> defenses of kind <see cref="ColonyObjectiveDef.Defense"/> (a
    /// <c>DefenseKind</c> name: Tower, Trap, Barricade...) stand at the colony; any kind when it is null.
    /// </summary>
    Defense,
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
    public string? Defense { get; init; }
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

    /// <summary>
    /// The weapon tier the fight is balanced for (a stand-up fight a party with starting gear loses): the briefing warns
    /// below it (<see cref="MissionReadiness"/>). Null: no gear warning.
    /// </summary>
    public int? GearTier { get; init; }
    public List<ConditionDef> Requires { get; init; } = new();
    public string NeedsText { get; init; } = "";
    public string? Briefing { get; init; }
    public string? Debrief { get; init; }
    public List<ColonyObjectiveDef> ColonyObjectives { get; init; } = new();
    public FieldMission? Field { get; init; }
    public List<EffectDef> Rewards { get; init; } = new();
    public FailDef Fail { get; init; } = new();

    /// <summary>A repeatable mission (Doc's Rounds) comes back this many days after it is done.</summary>
    public int? RepeatDays { get; init; }

    /// <summary>
    /// A floor of a multi-floor assault: the mission before it in the chain. It opens as soon as that one is done, and
    /// only the survivors who came back from it may go in (wounds and all; see <see cref="Campaign.ChainParty"/>).
    /// </summary>
    public string? ChainFrom { get; init; }

    /// <summary>Story characters from the colony who must be in the party (by character id, or "key" for the key-carrier).</summary>
    public List<string> MustBring { get; init; } = new();

    /// <summary>A story character who never goes in a guest's co-op party and makes this a host-only mission when in the party.</summary>
    public bool HostOnly { get; init; }

    public bool Perimeter => Map == "perimeter";
    public string? HandmadeId => Map.StartsWith("handmade:", StringComparison.Ordinal) ? Map["handmade:".Length..] : null;
}

/// <summary>
/// A Codex page (data/story/codex.json): unlocked once <see cref="When"/> holds (a mission done, a flag from a hacked
/// terminal), and it grows by its <see cref="Lines"/> whose conditions hold. <see cref="Tab"/>: people, logs or story.
/// </summary>
public sealed record CodexEntryDef
{
    public string Id { get; init; } = "";
    public string Tab { get; init; } = "people";
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    public ConditionDef? When { get; init; }
    public List<CodexLineDef> Lines { get; init; } = new();
}

public sealed record CodexLineDef(string Text, ConditionDef? When = null);

/// <summary>One still of an ending's epilogue: the first variant whose condition holds is shown (none: the slide is skipped).</summary>
public sealed record EpilogueSlideDef
{
    public string Still { get; init; } = "";
    public List<CodexLineDef> Variants { get; init; } = new();
}

/// <summary>An ending (data/story/endings.json): its title, epilogue slides and the world effects it leaves behind.</summary>
public sealed record EndingDef
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";

    /// <summary>The ending closes the game: no free play after the epilogue (the Calm).</summary>
    public bool Final { get; init; }

    /// <summary>When the ending is open at the core (all must hold).</summary>
    public List<ConditionDef> Requires { get; init; } = new();

    /// <summary>Why it is closed, shown greyed out (null: a hidden ending, never shown closed).</summary>
    public string? ClosedText { get; init; }
    public List<string> WorldEffects { get; init; } = new();
    public List<EpilogueSlideDef> Slides { get; init; } = new();
}
