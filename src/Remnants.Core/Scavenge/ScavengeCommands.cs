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

/// <summary>Go on overwatch or brace. Either ends the unit's turn and holds until its next one.</summary>
public sealed record SetStance(int PlayerId, int UnitId, Stance Stance) : IScavengeCommand;

/// <summary>Scan the surroundings: more sight and detection until the unit's next turn. Does not end the turn.</summary>
public sealed record Lookout(int PlayerId, int UnitId) : IScavengeCommand;

/// <summary>Take one item from a cache on or next to the unit's tile.</summary>
public sealed record PickUp(int PlayerId, int UnitId, int CacheId, string ItemId) : IScavengeCommand;

/// <summary>Talk an adjacent NPC survivor into following the unit to the exit.</summary>
public sealed record Recruit(int PlayerId, int UnitId, int NpcId) : IScavengeCommand;

/// <summary>Wait: end this unit's turn, forfeiting remaining AP.</summary>
public sealed record EndTurn(int PlayerId, int UnitId) : IScavengeCommand;

/// <summary>Leave the map from an exit tile, taking nearby followers along.</summary>
public sealed record Extract(int PlayerId, int UnitId) : IScavengeCommand;

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
}

/// <summary>Outcome of a scavenge command. <see cref="EntityId"/> carries e.g. the attacked unit.</summary>
public sealed record ScavengeResult(ScavengeError Error, string Message, int? EntityId = null)
{
    public bool Ok => Error == ScavengeError.None;

    public static ScavengeResult Success(string message, int? entityId = null) => new(ScavengeError.None, message, entityId);
    public static ScavengeResult Fail(ScavengeError error, string message) => new(error, message);
}
