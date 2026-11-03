using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>An intent from the UI (or, later, a client). Core validates and applies it.</summary>
public interface IColonyCommand;

/// <summary>
/// Adds a job at the building; the survivor keeps their other activities (see <c>Activities</c>). With
/// <paramref name="Move"/> it replaces every job they hold instead (class and site stay); refused, it changes nothing.
/// </summary>
public sealed record AssignJob(int SurvivorId, int BuildingId, bool Move = false) : IColonyCommand;

/// <summary>
/// Leave the job at <paramref name="BuildingId"/>, or the site there if they build it. Without a building: leave every
/// job (or the site, with none).
/// </summary>
public sealed record Unassign(int SurvivorId, int? BuildingId = null) : IColonyCommand;

/// <summary>Build on <paramref name="Plot"/>, or on the first free plot when null.</summary>
public sealed record Construct(string BuildingDefId, int? Plot = null) : IColonyCommand;

public sealed record Upgrade(int BuildingId) : IColonyCommand;

/// <summary>Put a survivor to work at a construction site (the building's id; they leave their job or post).</summary>
public sealed record AssignBuilder(int SurvivorId, int BuildingId) : IColonyCommand;

/// <summary>Call off a construction site: the materials come back (most of those already hauled).</summary>
public sealed record CancelConstruction(int BuildingId) : IColonyCommand;

/// <summary>Stop or resume work at a construction site.</summary>
public sealed record PauseConstruction(int BuildingId, bool Paused) : IColonyCommand;

/// <summary>Which site idle hands go to first (Low: assigned builders only).</summary>
public sealed record SetConstructionPriority(int BuildingId, SitePriority Priority) : IColonyCommand;

/// <summary>Builders at an upgrade of a lit building keep going after the day shift.</summary>
public sealed record SetSiteNightShift(int BuildingId, bool On) : IColonyCommand;

/// <summary>
/// Send adults into a zone with gear from the colony inventory. They are away until the result is applied. With
/// <paramref name="WaitFor"/> the party waits outside the zone until that phase starts (if they would arrive in another),
/// taking extra rations for the wait (see <see cref="ColonySim.PlanArrival"/>). With <paramref name="VehicleId"/> the party
/// drives a colony vehicle: faster on the road, its seats cap the party, it burns fuel and parks at the zone's edge.
/// </summary>
public sealed record LaunchExpedition(string ZoneId, List<ExpeditionMember> Members, DayPhase? WaitFor = null, RationChoice? Rations = null,
    int? VehicleId = null) : IColonyCommand;

/// <summary>Pick what a Farm, Greenhouse or Hunting Lodge works on (data/food.json harvests). The standing crop is lost.</summary>
public sealed record SetHarvest(int BuildingId, string HarvestId) : IColonyCommand;

/// <summary>Fit an upgrade (data/vehicles.json) to a vehicle at the Garage, paying its resources and parts.</summary>
public sealed record FitUpgrade(int VehicleId, string UpgradeId) : IColonyCommand;

/// <summary>Take an upgrade off a vehicle at the Garage; some of its parts come back.</summary>
public sealed record RemoveUpgrade(int VehicleId, string UpgradeId) : IColonyCommand;

/// <summary>Repair a vehicle at the Garage with spare parts from the stores (as many as it needs, or as there are).</summary>
public sealed record RepairVehicle(int VehicleId) : IColonyCommand;

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

/// <summary>Use a medical item from the inventory on a survivor at home: dress, cure, splint, numb or patch up (see <see cref="Medicine"/>).</summary>
public sealed record TreatSurvivor(int SurvivorId, string ItemId) : IColonyCommand;

/// <summary>The Med Bay's auto-treatment order; priorities left out are off. Null restores the default order.</summary>
public sealed record SetCarePriorities(List<Data.TreatPriority>? Priorities) : IColonyCommand;

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

/// <summary>
/// Gear Bench: fit a mod from the inventory to a piece of gear in the inventory, or to a survivor's fitted prosthetic
/// (<paramref name="SurvivorId"/> + <paramref name="Part"/>: a gun arm). A mod already in that slot comes off into the
/// inventory. Needs a bench building (<c>gear_mods.json rules.benches</c>).
/// </summary>
public sealed record InstallMod(string ItemId, string ModItemId, int? SurvivorId = null, BodyPart? Part = null) : IColonyCommand;

/// <summary>Gear Bench: take the mod in a slot off (it goes back to the inventory intact).</summary>
public sealed record RemoveMod(string ItemId, string Slot, int? SurvivorId = null, BodyPart? Part = null) : IColonyCommand;

/// <summary>Fit an upgrade kit from the inventory to gear in the inventory: it goes up one Mk (<c>rules.upgradeAt</c>).</summary>
public sealed record UpgradeGear(string ItemId, string KitItemId) : IColonyCommand;

public enum CommandError
{
    None,
    UnknownCard,
    /// <summary>The deck and slots would use more copies of a card than the colony owns.</summary>
    NotEnoughCards,
    UnknownCardUpgrade,
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
    /// <summary>Vehicles: unknown, out with a party, broken down, too few seats, blocked by weather or range, no Garage.</summary>
    UnknownVehicle,
    UnknownUpgrade,
    VehicleAway,
    VehicleBroken,
    NotEnoughSeats,
    NoGarage,
    AlreadyFitted,
    UpgradeDoesNotFit,
    /// <summary>Every slot of the upgrade's kind on the vehicle is taken.</summary>
    SlotFull,
    /// <summary>Nothing to repair (a building or a vehicle).</summary>
    /// <summary>Medicine: the item is not a treatment used at home, or would do nothing for them now.</summary>
    NotATreatment,
    NothingToTreat,
    /// <summary>Raids and defenses (<c>Raids/</c>): no raids.json loaded; unknown placed defense; its schematic is not known.</summary>
    NoRaidData,
    UnknownDefense,
    SchematicMissing,
    InvalidCell,
    CellOccupied,
    NotATrap,
    AlreadyArmed,
    NoRaidWarning,
    CannotRecall,
    /// <summary>The building was wrecked in a raid: repair it first.</summary>
    Wrecked,
    /// <summary>The raid cannot be fought on the map (no field units, no Town Hall, nobody able).</summary>
    CannotFight,
    NotDamaged,
    /// <summary>The road is closed: the weather over the zone, or the vehicle cannot make it (dust, range).</summary>
    RouteBlocked,
    DronesGrounded,
    /// <summary>Power: the building neither makes, stores nor draws power.</summary>
    NotOnGrid,
    /// <summary>Gear mods: no bench (or upgrade) building; the gear or mod or kit does not work that way.</summary>
    NoBench,
    NotModdable,
    ModDoesNotFit,
    NoFreeModSlot,
    EmptyModSlot,
    WrongKit,
    MaxMk,
    /// <summary>Construction: the building has no site; or it is already being built or upgraded.</summary>
    NotUnderConstruction,
    UnderConstruction,
    /// <summary>The survivor already holds <c>rules.maxActivities</c> activities (jobs, a class, a site).</summary>
    TooManyActivities,
}

/// <summary>
/// Switch a building's power draw on or off (see <c>Power</c>): a switched-off consumer never draws (and gets no bonus);
/// a switched-off generator stays cold (no fuel burnt).
/// </summary>
public sealed record SetPower(int BuildingId, bool On) : IColonyCommand;

/// <summary>The squad deck parties take out: card ids from the collection (copies repeated).</summary>
public sealed record SetDeck(List<string> Cards) : IColonyCommand;

/// <summary>The cards a survivor holds in their personal slots (an empty list clears them).</summary>
public sealed record SetCardSlots(int SurvivorId, List<string> Cards) : IColonyCommand;

/// <summary>Buy a squad deck upgrade (cards.json <c>rules.upgrades</c>).</summary>
public sealed record BuyCardUpgrade(string UpgradeId) : IColonyCommand;

/// <summary>Outcome of a command. <see cref="EntityId"/> carries e.g. a new building's id.</summary>
public sealed record CommandResult(CommandError Error, string Message, int? EntityId = null)
{
    public bool Ok => Error == CommandError.None;

    public static CommandResult Success(string message, int? entityId = null) => new(CommandError.None, message, entityId);
    public static CommandResult Fail(CommandError error, string message) => new(error, message);
}
