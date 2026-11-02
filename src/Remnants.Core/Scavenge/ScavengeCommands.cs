namespace Remnants.Core.Scavenge;

/// <summary>
/// An intent from a player (UI now, a network client later): which player, which of their units.
/// Core validates ownership, turn and rules before applying it.
/// </summary>
public interface IScavengeCommand
{
    int PlayerId { get; }
    int UnitId { get; }
}

public sealed record Move(int PlayerId, int UnitId, Pos To) : IScavengeCommand;

public sealed record Attack(int PlayerId, int UnitId, int TargetId, string ActionId) : IScavengeCommand;

/// <summary>
/// Use a carried consumable on self (no target), on a unit in the item's range, or for items used on a tile (flare,
/// motion sensor) on the tile <see cref="At"/> in range.
/// </summary>
public sealed record UseItem(int PlayerId, int UnitId, string ItemId, int? TargetId = null, Pos? At = null) : IScavengeCommand;

/// <summary>
/// Telegraph a plan (wire name <c>plannedAction</c>): a move along <see cref="Path"/> (each waypoint one move from the
/// last) and optionally an attack with <see cref="ActionId"/> on the tile <see cref="AttackTile"/>, in reach from the
/// end of the path. Free now and ends the turn; it resolves, and costs its AP, at the start of the unit's next turn.
/// See <see cref="PlannedActions"/>.
/// </summary>
public sealed record DeclareIntent(int PlayerId, int UnitId, IReadOnlyList<Pos> Path, Pos? AttackTile = null, string? ActionId = null)
    : IScavengeCommand;

/// <summary>
/// Play a card on <see cref="UnitId"/> (the character it applies to): from the squad hand, or from that character's
/// personal slots (<see cref="FromSlot"/>). <see cref="TargetId"/>: the enemy whose plan a reaction answers.
/// <see cref="Boost"/>: pay the card's extra AP for its boost. See <see cref="CardPlay"/>.
/// </summary>
public sealed record PlayCard(int PlayerId, int UnitId, string CardId, int? TargetId = null, Pos? Tile = null, bool Boost = false, bool FromSlot = false)
    : IScavengeCommand;

/// <summary>Discard a hard card nobody in the party qualifies for, at no cost, once a round.</summary>
public sealed record DiscardDeadCard(int PlayerId, int UnitId, string CardId) : IScavengeCommand;

/// <summary>Go on overwatch or brace. Either ends the unit's turn and holds until its next one.</summary>
public sealed record SetStance(int PlayerId, int UnitId, Stance Stance) : IScavengeCommand;

/// <summary>Scan the surroundings: more sight and detection until the unit's next turn. Does not end the turn.</summary>
public sealed record Lookout(int PlayerId, int UnitId) : IScavengeCommand;

/// <summary>
/// Jury-rig gear in the field (needs <c>gear_mods.json rules.fieldHook</c>, the Jury-rig perk): fit a mod from the pack to an
/// equipped item (<paramref name="ModItemId"/>), or take the mod in <paramref name="RemoveSlot"/> off it into the pack.
/// <paramref name="Slot"/> picks the equipped item (default: the weapon). Costs <c>rules.fieldApCost</c> AP.
/// </summary>
public sealed record FieldMod(int PlayerId, int UnitId, string? ModItemId = null, string? RemoveSlot = null, Items.EquipSlot Slot = Items.EquipSlot.Weapon)
    : IScavengeCommand;

/// <summary>Take one item from a cache on or next to the unit's tile.</summary>
public sealed record PickUp(int PlayerId, int UnitId, int CacheId, string ItemId) : IScavengeCommand;

/// <summary>Take everything that fits from a pile on or next to the unit's tile, for one pick-up's AP.</summary>
public sealed record TakeAll(int PlayerId, int UnitId, int CacheId) : IScavengeCommand;

/// <summary>Drop a carried item on the unit's tile.</summary>
public sealed record DropItem(int PlayerId, int UnitId, string ItemId) : IScavengeCommand;

/// <summary>
/// Stash a carried item: into the party's parked vehicle next to the unit (it goes home with the vehicle), else on the exit
/// tile the unit stands on (it waits there for the next run).
/// </summary>
public sealed record StashItem(int PlayerId, int UnitId, string ItemId) : IScavengeCommand;

/// <summary>Work an adjacent salvage node (a wrecked car gives vehicle parts too): AP and noise; every few actions a lot comes off into the pack.</summary>
public sealed record Salvage(int PlayerId, int UnitId, int NodeId) : IScavengeCommand;

/// <summary>Talk an adjacent NPC survivor into following the unit to the exit.</summary>
public sealed record Recruit(int PlayerId, int UnitId, int NpcId) : IScavengeCommand;

/// <summary>Wait: end this unit's turn, forfeiting remaining AP.</summary>
public sealed record EndTurn(int PlayerId, int UnitId) : IScavengeCommand;

/// <summary>Leave the map from an exit tile or next to the side's parked vehicle, taking nearby followers along.</summary>
public sealed record Extract(int PlayerId, int UnitId) : IScavengeCommand;

/// <summary>Take an item back out of the party's vehicle next to the unit (needs a mobile workbench on it).</summary>
public sealed record TakeFromStash(int PlayerId, int UnitId, string ItemId) : IScavengeCommand;

/// <summary>Co-op: offer an alliance to another player whose party and yours can see each other. Free; on your turn.</summary>
public sealed record ProposeAlliance(int PlayerId, int UnitId, int ToPlayerId) : IScavengeCommand;

/// <summary>Co-op: accept or decline an open offer from <see cref="FromPlayerId"/>, on your turn. Accepting merges the teams.</summary>
public sealed record RespondAlliance(int PlayerId, int UnitId, int FromPlayerId, bool Accept) : IScavengeCommand;

/// <summary>Co-op: leave your team. It takes effect after a warning round both sides see (<see cref="AllianceBreak"/>).</summary>
public sealed record BreakAlliance(int PlayerId, int UnitId) : IScavengeCommand;

/// <summary>Co-op: mark a tile for your team only (danger / go here / loot). Free, a few per turn. (<c>Mark</c>, not <c>Kind</c>: the wire format uses <c>kind</c> for the command type.)</summary>
public sealed record PingTile(int PlayerId, int UnitId, Pos At, PingKind Mark) : IScavengeCommand;

/// <summary>Hand a carried item to an adjacent unit on your side (an ally's goes into their party, and home with them).</summary>
public sealed record GiveItem(int PlayerId, int UnitId, int TargetId, string ItemId) : IScavengeCommand;

/// <summary>Pick up an adjacent downed unit on your side and carry it (it moves and extracts with you; you move slower).</summary>
public sealed record Carry(int PlayerId, int UnitId, int TargetId) : IScavengeCommand;

/// <summary>Put the carried unit down on a free tile next to you.</summary>
public sealed record PutDown(int PlayerId, int UnitId, Pos At) : IScavengeCommand;

/// <summary>
/// Hack a device (<see cref="DeviceId"/>) or, with the Override perk, a Linked unit (<see cref="TargetId"/>; a camera's or
/// turret's unit means its device). Needs a hacking tool; each action puts work in, the last one rolls the chance.
/// </summary>
public sealed record Hack(int PlayerId, int UnitId, int? DeviceId = null, int? TargetId = null) : IScavengeCommand;

/// <summary>Launch the carried scout drone onto a tile next to the operator (<see cref="UnitId"/>).</summary>
public sealed record DeployDrone(int PlayerId, int UnitId, Pos At) : IScavengeCommand;

/// <summary>Fly the operator's drone to a tile (over any terrain) within its flight and control range; the operator pays the AP.</summary>
public sealed record FlyDrone(int PlayerId, int UnitId, Pos To) : IScavengeCommand;

/// <summary>Call the drone back into the operator's pack (it recharges there). It must be within control range.</summary>
public sealed record RecallDrone(int PlayerId, int UnitId) : IScavengeCommand;

/// <summary>Spotter perk: mark a hostile the drone sees, for the operator's whole side.</summary>
public sealed record MarkTarget(int PlayerId, int UnitId, int TargetId) : IScavengeCommand;

/// <summary>Payload perk: the drone drops a carried flare or motion sensor on its tile.</summary>
public sealed record DropPayload(int PlayerId, int UnitId, string ItemId) : IScavengeCommand;

/// <summary>Medic drone: apply a healing item from the operator's pack to a friendly unit next to the drone.</summary>
public sealed record DroneHeal(int PlayerId, int UnitId, int TargetId) : IScavengeCommand;

/// <summary>Cargo drone: take one item from a cache on or next to the drone into its hold.</summary>
public sealed record DroneLoad(int PlayerId, int UnitId, int CacheId, string ItemId) : IScavengeCommand;

/// <summary>Cargo drone next to an exit: fly the hold (and the drone) out of the zone; it is home whatever happens.</summary>
public sealed record DroneFerry(int PlayerId, int UnitId) : IScavengeCommand;

/// <summary>Combat drone: fire its gun at a hostile it sees; the operator pays the gun's AP.</summary>
public sealed record DroneFire(int PlayerId, int UnitId, int TargetId) : IScavengeCommand;

public enum ScavengeError
{
    None,
    UnknownCommand,
    Finished,
    UnknownUnit,
    NotYourUnit,
    NotYourTurn,
    Incapacitated,
    NotEnoughAp,
    Unreachable,
    UnknownAction,
    InvalidTarget,
    OutOfRange,
    NoLineOfSight,
    ItemNotCarried,
    NotUsable,
    UnknownCache,
    TooFar,
    ItemNotInCache,
    CarryLimitExceeded,
    NotOnExit,
    NotRecruitable,
    /// <summary>The target is not seen by the unit's side (hidden in the fog).</summary>
    NotVisible,
    InvalidStance,
    AlreadyOnLookout,
    InvalidTile,
    /// <summary>The target is an ally's unit: allies never shoot each other.</summary>
    FriendlyFire,
    /// <summary>Alliance offers go to another player's party that is hostile now.</summary>
    NotAPlayer,
    AlreadyAllied,
    /// <summary>The two parties do not see each other.</summary>
    NoContact,
    NoProposal,
    ProposalPending,
    NotAllied,
    BreakPending,
    PingLimit,
    /// <summary>Carry: the target is not a downed unit on your side, or the carrier's hands are full / empty.</summary>
    CannotCarry,
    /// <summary>Hack: no hacking kit or cyberdeck carried.</summary>
    NoHackingTool,
    UnknownDevice,
    /// <summary>The device works for your side already (or is destroyed / used up).</summary>
    AlreadyHacked,
    /// <summary>A failed hack locked your side out of this device for a few rounds.</summary>
    LockedOut,
    /// <summary>The target cannot be hacked (a Husk, or a Linked without the Override perk).</summary>
    NotHackable,
    /// <summary>The unit carries no scout drone, or has none deployed.</summary>
    NoDrone,
    DroneAlreadyOut,
    /// <summary>The drone's battery is too low to take off or fly.</summary>
    LowBattery,
    /// <summary>The action needs a perk the unit does not have (Spotter, Payload).</summary>
    MissingPerk,
    /// <summary>A story mission's prompt is waiting for an answer (<see cref="ChooseOption"/>).</summary>
    ChoicePending,
    /// <summary>No such prompt is waiting.</summary>
    NoPrompt,
    /// <summary>The parked vehicle's stash is at its cargo limit.</summary>
    CargoFull,
    /// <summary>Salvage: no wreck there, or it was stripped already.</summary>
    NothingToSalvage,
    DroneGrounded,
    /// <summary>Salvage: no such node, or nothing left on it.</summary>
    UnknownNode,
    NodeDepleted,
    /// <summary>Gear mods: the mod does not fit, the slot is full or empty, or there is no gear there.</summary>
    CannotMod,
    /// <summary>The unit already has a plan waiting: plans cannot be revised or cancelled.</summary>
    PlanLocked,
    /// <summary>The card is not in the hand (or the character's ready slots), or the party has no deck.</summary>
    NoCard,
    /// <summary>The character does not meet a hard card's requirement.</summary>
    NotQualified,
    NotEnoughPoints,
    /// <summary>An arm-locked card while the character's arms are too hurt.</summary>
    ArmsLocked,
    /// <summary>A reaction card needs a seen enemy with a telegraphed plan.</summary>
    NoIntent,
    /// <summary>A dead card was discarded this round already.</summary>
    DeadCardSpent,
    /// <summary>Only a hard card nobody in the party can play may be discarded.</summary>
    CardPlayable,
}

/// <summary>Outcome of a scavenge command. <see cref="EntityId"/> carries e.g. the attacked unit.</summary>
public sealed record ScavengeResult(ScavengeError Error, string Message, int? EntityId = null)
{
    public bool Ok => Error == ScavengeError.None;

    public static ScavengeResult Success(string message, int? entityId = null) => new(ScavengeError.None, message, entityId);
    public static ScavengeResult Fail(ScavengeError error, string message) => new(error, message);
}
