using Remnants.Core.Survivors;

namespace Remnants.Core.Data;

/// <summary>
/// Experience, perks and study (data/perks.json). Survivors earn XP from work, crafting, study and missions; each level
/// gives perk points to spend on <see cref="PerkDef"/>s. Skills can also be studied at a study building, faster with a
/// mentor. See <c>Colony/Progression.cs</c> and <c>Colony/Study.cs</c> for the rules.
/// </summary>
public sealed record ProgressionDef
{
    /// <summary>Per-skill name and the attributes that make someone quick at learning it.</summary>
    public List<SkillDef> Skills { get; init; } = new();
    public XpRules Xp { get; init; } = new();
    public StudyRules Study { get; init; } = new();
    public List<DisciplineDef> Disciplines { get; init; } = new();
    public List<PerkDef> Perks { get; init; } = new();
}

public sealed record SkillDef
{
    public Skill Skill { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Attribute weights for learning speed while studying (and for showing what the skill leans on).</summary>
    public Dictionary<AttributeKind, double> Attributes { get; init; } = new();
}

/// <summary>
/// XP sources and the level curve. XP needed to reach level L (from level 1):
/// <code>
/// xpFor(L) = perLevel · (L − 1) + perLevelStep · (L − 1)(L − 2) / 2
/// </code>
/// so each level costs <see cref="PerLevelStep"/> more than the one before. Every level up gives
/// <see cref="PointsPerLevel"/> perk points. Children earn XP at <see cref="ChildMultiplier"/>.
/// </summary>
public sealed record XpRules
{
    public double PerLevel { get; init; } = 72;
    public double PerLevelStep { get; init; } = 24;
    public int MaxLevel { get; init; } = 20;
    public int PointsPerLevel { get; init; } = 1;

    /// <summary>Each tick of work at a job (gathering, crafting, treating, practising).</summary>
    public double PerWorkTick { get; init; } = 1;

    /// <summary>Each finished crafting batch, to each worker at the building.</summary>
    public double PerCraft { get; init; } = 4;

    /// <summary>Each tick in class, for the student and (<see cref="PerTeachTick"/>) the mentor.</summary>
    public double PerStudyTick { get; init; } = 1;
    public double PerTeachTick { get; init; } = 0.5;

    /// <summary>A mission: per survivor who came home, plus per zone threat level.</summary>
    public double PerMission { get; init; } = 30;
    public double PerMissionThreat { get; init; } = 15;

    /// <summary>Per skill point gained in the field (practice on a scavenge run).</summary>
    public double PerFieldSkillPoint { get; init; } = 10;
    public double ChildMultiplier { get; init; } = 1.5;

    /// <summary>Old saves: XP granted per skill point a survivor already has, so veterans arrive with levels.</summary>
    public double MigrationPerSkillPoint { get; init; } = 3;
}

/// <summary>
/// Studying at a study building. A student learns a skill at
/// <code>
/// gain/tick = skillPerTick · aptitude · (mentor ? mentorMultiplier : 1) · (child ? training.childMultiplier : 1)
///             · (1 + perk studySpeed + manual studyBonus) · (1 − skill/100)
/// aptitude  = aptitudeFloor + (1 − aptitudeFloor) · (skill's attribute-weighted average / 100)
/// </code>
/// up to a cap: the mentor's skill, or <see cref="SelfStudyCap"/> alone (a manual's cap when it is higher). A perk is
/// studied with a mentor who has it: <see cref="PerkWorkPerTier"/> · tier effort at the same rate (no diminishing),
/// then the perk is learned without spending a point.
/// </summary>
public sealed record StudyRules
{
    /// <summary>Study building type → what can be studied there and how many students per level.</summary>
    public Dictionary<string, StudyBuildingRules> Buildings { get; init; } = new();
    public double SkillPerTick { get; init; } = 0.08;
    public double AptitudeFloor { get; init; } = 0.5;
    public double SelfStudyCap { get; init; } = 20;
    public double MentorMultiplier { get; init; } = 2;

    /// <summary>A mentor must be at least this far ahead of the student in the skill.</summary>
    public double MentorLead { get; init; } = 5;
    public int MaxStudentsPerMentor { get; init; } = 2;
    public double PerkWorkPerTier { get; init; } = 48;
}

public sealed record StudyBuildingRules
{
    public List<Skill> Skills { get; init; } = new();

    /// <summary>Students at once at level 1, 2, ...; levels past the end use the last entry.</summary>
    public List<int> Slots { get; init; } = [1];
}

/// <summary>A branch of the perk tree (Tech, Drones, Combat, Survival, Medic).</summary>
public sealed record DisciplineDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Icon id for the UI (<c>game/ui/icons</c>).</summary>
    public string Icon { get; init; } = "";

    /// <summary>Building type where its perks are studied with a mentor.</summary>
    public string StudyAt { get; init; } = "";
}

/// <summary>What a survivor (and the colony) needs before a perk can be learned.</summary>
public sealed record PerkRequirements
{
    public Dictionary<Skill, double> Skills { get; init; } = new();
    public Dictionary<AttributeKind, double> Attributes { get; init; } = new();
    public List<string> Perks { get; init; } = new();

    /// <summary>Survivor level.</summary>
    public int Level { get; init; } = 1;

    /// <summary>The colony has this building type at <see cref="BuildingLevel"/> or higher.</summary>
    public string? Building { get; init; }
    public int BuildingLevel { get; init; } = 1;

    /// <summary>A blueprint or datashard item the colony has studied (<c>KnownBlueprints</c>).</summary>
    public string? Blueprint { get; init; }

    public bool AdultsOnly { get; init; }
}

/// <summary>
/// What owning a perk does. Colony effects apply in Core now; <see cref="Hooks"/> are named numbers the scavenge mode
/// reads through <c>PerkEffects</c> (ids in <c>PerkHooks</c>, documented in ARCHITECTURE.md).
/// </summary>
public sealed record PerkEffectsDef
{
    /// <summary>Job id → output bonus (0.1 = +10%).</summary>
    public Dictionary<string, double> JobOutput { get; init; } = new();

    /// <summary>Job id → added to the crafting quality score when this survivor is the building's best worker.</summary>
    public Dictionary<string, double> CraftQuality { get; init; } = new();

    /// <summary>Added to study speed (0.25 = +25%).</summary>
    public double StudySpeed { get; init; }

    /// <summary>Added to XP earned (0.1 = +10%).</summary>
    public double XpBonus { get; init; }

    /// <summary>Scavenge hook id → value; values of several perks add up.</summary>
    public Dictionary<string, double> Hooks { get; init; } = new();
}

public sealed record PerkDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Discipline { get; init; } = "";

    /// <summary>Row in its discipline's tree (1 = entry level).</summary>
    public int Tier { get; init; } = 1;
    public string Description { get; init; } = "";

    /// <summary>Perk points it costs when chosen.</summary>
    public int Cost { get; init; } = 1;
    public PerkRequirements Requires { get; init; } = new();
    public PerkEffectsDef Effects { get; init; } = new();
}
