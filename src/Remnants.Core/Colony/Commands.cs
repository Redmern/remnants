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

/// <summary>
/// Send adults into a zone with gear from the colony inventory. They are away until the result is applied. With
/// <paramref name="WaitFor"/> the party waits outside the zone until that phase starts (if they would arrive in another),
/// taking extra rations for the wait (see <see cref="ColonySim.PlanArrival"/>).
/// </summary>
public sealed record LaunchExpedition(string ZoneId, List<ExpeditionMember> Members, DayPhase? WaitFor = null, RationChoice? Rations = null) : IColonyCommand;

/// <summary>Pick what a Farm, Greenhouse or Hunting Lodge works on (data/food.json harvests). The standing crop is lost.</summary>
public sealed record SetHarvest(int BuildingId, string HarvestId) : IColonyCommand;

/// <summary>
/// Fly a scout drone from the inventory over a rumored zone: it reveals the zone's intel without anyone going (see
/// <see cref="WorldMap.SendDrone"/>). <paramref name="PilotId"/>: a survivor at home who flies it (less risk, trains Drone Piloting).
/// </summary>
public sealed record SendScoutDrone(string ZoneId, int? PilotId = null) : IColonyCommand;

/// <summary>Repair a building struck by lightning (<c>weather.repair.cost</c>).</summary>
public sealed record RepairBuilding(int BuildingId) : IColonyCommand;

/// <summary>Bring the active expedition home: survivors, injuries, deaths, recruits and loot.</summary>
public sealed record ApplyExtractionResult(ExtractionResult Result) : IColonyCommand;

/// <summary>Admit an injured survivor to a Med Bay (the first with room when <paramref name="BuildingId"/> is null).</summary>
public sealed record AdmitPatient(int SurvivorId, int? BuildingId = null) : IColonyCommand;

public sealed record Discharge(int SurvivorId) : IColonyCommand;

/// <summary>
/// Fit a crude or mechanical prosthetic item (below <c>surgery.fromTier</c>) from the inventory onto a missing limb, at a
/// Med Bay of the item's level. It straps on at once; powered parts and up need <see cref="ScheduleSurgery"/>.
/// </summary>
public sealed record FitProsthetic(int SurvivorId, BodyPart Part, string ItemId) : IColonyCommand;

/// <summary>
/// Book surgery at a Med Bay (the first with a free bed when <paramref name="BuildingId"/> is null): fit this prosthetic
/// onto a missing limb, in place of an older prosthetic (which goes back to the inventory), or, with
/// <paramref name="Elective"/> confirmed, onto a healthy part (augmentation, tier 3+, adults only, a big humanity hit).
/// The item is set aside at once; the patient takes a bed until the surgery is done. See <c>Cybernetics</c>.
/// </summary>
public sealed record ScheduleSurgery(int SurvivorId, BodyPart Part, string ItemId, bool Elective = false, int? BuildingId = null) : IColonyCommand;

/// <summary>Call off a booked surgery: the item goes back to the inventory and the patient is discharged.</summary>
public sealed record CancelSurgery(int SurgeryId) : IColonyCommand;

/// <summary>
/// Shortcut: replace a production building's queue with one repeating order of this recipe; null clears the queue.
/// A dropped batch in progress refunds its inputs.
/// </summary>
public sealed record SetRecipe(int BuildingId, string? RecipeId) : IColonyCommand;

/// <summary>Add an order of <paramref name="Count"/> batches (or a repeating one) to the back of a building's queue.</summary>
public sealed record QueueRecipe(int BuildingId, string RecipeId, int Count = 1, bool Repeat = false) : IColonyCommand;

/// <summary>Remove an order from a building's queue; a batch in progress refunds its inputs.</summary>
public sealed record CancelOrder(int BuildingId, int OrderId) : IColonyCommand;

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
    NotASchematic,
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
    /// <summary>Cybernetics: this tier is fitted by surgery (<see cref="ScheduleSurgery"/>), not strapped on.</summary>
    NeedsSurgery,
    /// <summary>Surgery on a healthy part without the elective confirmation, below the elective tier, or on a child.</summary>
    ElectiveNotAllowed,
    PartInjured,
    AlreadyBooked,
    UnknownSurgery,
    NoCybernetics,
    /// <summary>Building or level gated by a schematic the colony does not know.</summary>
    SchematicNeeded,
    /// <summary>Food: the building does not harvest, or not this.</summary>
    UnknownHarvest,
    NotResearchable,
    NotHeardOf,
    ResearchBusy,
    NoResearch,
    IsResearching,
    NoTrader,
    NotInStock,
    UnknownCall,
    NoContract,
    /// <summary>Story commands in a Sandbox colony (or without story data).</summary>
    NoCampaign,
    UnknownMission,
    MissionNotAvailable,
    NoDialogue,
    InvalidChoice,
    NotDamaged,
    RouteBlocked,
    DronesGrounded,
    /// <summary>Power: the building neither makes, stores nor draws power.</summary>
    NotOnGrid,
}

/// <summary>
/// Switch a building's power draw on or off (see <c>Power</c>): a switched-off consumer never draws (and gets no bonus);
/// a switched-off generator stays cold (no fuel burnt).
/// </summary>
public sealed record SetPower(int BuildingId, bool On) : IColonyCommand;

/// <summary>Outcome of a command. <see cref="EntityId"/> carries e.g. a new building's id.</summary>
public sealed record CommandResult(CommandError Error, string Message, int? EntityId = null)
{
    public bool Ok => Error == CommandError.None;

    public static CommandResult Success(string message, int? entityId = null) => new(CommandError.None, message, entityId);
    public static CommandResult Fail(CommandError error, string message) => new(error, message);
}
