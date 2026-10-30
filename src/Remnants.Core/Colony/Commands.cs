using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>An intent from the UI (or, later, a client). Core validates and applies it.</summary>
public interface IColonyCommand;

public sealed record AssignJob(int SurvivorId, int BuildingId) : IColonyCommand;

public sealed record Unassign(int SurvivorId) : IColonyCommand;

/// <summary>Build on <paramref name="Plot"/>, or on the first free plot when null.</summary>
public sealed record Construct(string BuildingDefId, int? Plot = null) : IColonyCommand;

public sealed record Upgrade(int BuildingId) : IColonyCommand;

/// <summary>Send adults into a zone with gear from the colony inventory. They are away until the result is applied.</summary>
public sealed record LaunchExpedition(string ZoneId, List<ExpeditionMember> Members) : IColonyCommand;

/// <summary>
/// Fly a scout drone from the inventory over a rumored zone: it reveals the zone's intel without anyone going (see
/// <see cref="WorldMap.SendDrone"/>). <paramref name="PilotId"/>: a survivor at home who flies it (less risk, trains Drone Piloting).
/// </summary>
public sealed record SendScoutDrone(string ZoneId, int? PilotId = null) : IColonyCommand;

/// <summary>Bring the active expedition home: survivors, injuries, deaths, recruits and loot.</summary>
public sealed record ApplyExtractionResult(ExtractionResult Result) : IColonyCommand;

/// <summary>Admit an injured survivor to a Med Bay (the first with room when <paramref name="BuildingId"/> is null).</summary>
public sealed record AdmitPatient(int SurvivorId, int? BuildingId = null) : IColonyCommand;

public sealed record Discharge(int SurvivorId) : IColonyCommand;

/// <summary>Fit a prosthetic item from the inventory onto a missing limb, at a Med Bay of the item's level.</summary>
public sealed record FitProsthetic(int SurvivorId, BodyPart Part, string ItemId) : IColonyCommand;

/// <summary>
/// Shortcut: replace a production building's queue with one repeating order of this recipe; null clears the queue.
/// A dropped batch in progress refunds its inputs.
/// </summary>
public sealed record SetRecipe(int BuildingId, string? RecipeId) : IColonyCommand;

/// <summary>Add an order of <paramref name="Count"/> batches (or a repeating one) to the back of a building's queue.</summary>
public sealed record QueueRecipe(int BuildingId, string RecipeId, int Count = 1, bool Repeat = false) : IColonyCommand;

/// <summary>Remove an order from a building's queue; a batch in progress refunds its inputs.</summary>
public sealed record CancelOrder(int BuildingId, int OrderId) : IColonyCommand;

/// <summary>Study (use up) a blueprint item at its study building, unlocking the recipes that name it.</summary>
public sealed record StudyBlueprint(string ItemId) : IColonyCommand;

/// <summary>
/// Nudge two adults: encourage (they bond faster), discourage (they stop bonding; a couple splits up) or neutral.
/// </summary>
public sealed record Matchmake(int SurvivorId, int OtherId, PairStance Stance) : IColonyCommand;

/// <summary>Spend perk points on a perk whose requirements the survivor meets.</summary>
public sealed record ChoosePerk(int SurvivorId, string PerkId) : IColonyCommand;

/// <summary>
/// Put a survivor in class at a study building: a <paramref name="Skill"/> (up to the mentor's level, or the self-study
/// cap) or a <paramref name="PerkId"/> (needs a mentor who has it). An optional manual from the inventory is used up.
/// Student and mentor leave their jobs.
/// </summary>
public sealed record StartStudy(int StudentId, int BuildingId, Skill? Skill = null, string? PerkId = null, int? MentorId = null,
    string? ManualItemId = null) : IColonyCommand;

public sealed record CancelStudy(int StudyId) : IColonyCommand;

/// <summary>Turn a building's Night Shift on or off. Needs a job, and light for outdoor work.</summary>
public sealed record SetNightShift(int BuildingId, bool On) : IColonyCommand;

/// <summary>Fit a Floodlight (<c>dayNight.floodlightCost</c>) to an outdoor building so it can run a Night Shift at level 1.</summary>
public sealed record InstallFloodlight(int BuildingId) : IColonyCommand;

public enum CommandError
{
    None,
    UnknownSurvivor,
    UnknownBuilding,
    UnknownBuildingType,
    NoJobAtBuilding,
    NoFreeSlot,
    NotAssigned,
    AlreadyExists,
    MaxLevel,
    TownHallLevelTooLow,
    NoTownHall,
    CannotAfford,
    UnknownCommand,
    InvalidPlot,
    PlotOccupied,
    NoFreePlot,
    NoScavengeData,
    ExpeditionActive,
    NoExpedition,
    UnknownZone,
    EmptyParty,
    PartyTooLarge,
    DuplicateMember,
    NotAnAdult,
    SurvivorAway,
    IsPatient,
    UnknownItem,
    InvalidGear,
    MissingItems,
    ResultNotFinal,
    ResultMismatch,
    NoMedBay,
    MedBayFull,
    NotInjured,
    NotAPatient,
    PartNotMissing,
    NotAProsthetic,
    WrongLimb,
    MedBayLevelTooLow,
    UnknownRecipe,
    RecipeLevelTooLow,
    RecipeLocked,
    InvalidCount,
    QueueFull,
    UnknownOrder,
    NotABlueprint,
    AlreadyKnown,
    NoStudyBuilding,
    TooYoung,
    NotForChildren,
    Pregnant,
    SamePerson,
    AlreadyPartnered,
    ZoneUnknown,
    /// <summary>Scout drone: none in the inventory, the zone is beyond its battery, or already scouted.</summary>
    NoDrone,
    OutOfDroneRange,
    AlreadyScouted,
    NotArrived,
    PartyReturning,
    UnknownPerk,
    AlreadyHasPerk,
    PerkLocked,
    PerkRequirementsNotMet,
    NoPerkPoints,
    NotAStudyBuilding,
    InvalidStudy,
    StudyFull,
    CannotStudyHere,
    InClass,
    NeedsMentor,
    MentorNotAhead,
    MentorBusy,
    AlreadyAtCap,
    NotAManual,
    WrongManual,
    UnknownStudy,
    /// <summary>Night Shift at an unlit outdoor building; or a Floodlight where it is indoors or already lit.</summary>
    NeedsLight,
    AlreadyLit,
    NotOutdoor,
}

/// <summary>Outcome of a command. <see cref="EntityId"/> carries e.g. a new building's id.</summary>
public sealed record CommandResult(CommandError Error, string Message, int? EntityId = null)
{
    public bool Ok => Error == CommandError.None;

    public static CommandResult Success(string message, int? entityId = null) => new(CommandError.None, message, entityId);
    public static CommandResult Fail(CommandError error, string message) => new(error, message);
}
