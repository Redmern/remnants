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
    NotArrived,
    PartyReturning,
}

/// <summary>Outcome of a command. <see cref="EntityId"/> carries e.g. a new building's id.</summary>
public sealed record CommandResult(CommandError Error, string Message, int? EntityId = null)
{
    public bool Ok => Error == CommandError.None;

    public static CommandResult Success(string message, int? entityId = null) => new(CommandError.None, message, entityId);
    public static CommandResult Fail(CommandError error, string message) => new(error, message);
}
