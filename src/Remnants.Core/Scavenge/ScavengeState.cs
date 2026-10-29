namespace Remnants.Core.Scavenge;

/// <summary>A player's party in the instance. Several can share one map (multiplayer later).</summary>
public sealed class Party
{
    public int PlayerId { get; set; }
    public string Side { get; set; } = "";

    /// <summary>No controllable unit left on the map: the party's run is over and its result is final.</summary>
    public bool Done { get; set; }
}

public enum ScavengeEventType
{
    RoundStarted, Moved, Attacked, Missed, Injured, LimbLost, Downed, Died, BledOut,
    Stabilized, Revived, Healed, Evacuated, Extracted, PickedUp, Recruited, Spotted, PartyDone, Finished,
    Suppressed, Flanked, Retreated, Reinforced,
}

/// <summary>Something notable for the UI log or animation. Purely informational.</summary>
public sealed record ScavengeEvent(
    int Round, ScavengeEventType Type, string Message, int? UnitId = null, int? TargetId = null, Pos? Pos = null);

/// <summary>
/// The complete mutable state of one scavenge instance. Plain data so it serializes;
/// all rules live in <see cref="ScavengeSim"/> and its helpers.
/// </summary>
public sealed class ScavengeState
{
    public const int MaxEvents = 500;

    public string ZoneId { get; set; } = "";
    public ulong RngState { get; set; }
    public int Round { get; set; }

    /// <summary>Unit ids in initiative order for the current round.</summary>
    public List<int> TurnOrder { get; set; } = new();
    public int TurnIndex { get; set; }
    public bool Finished { get; set; }

    public int NextUnitId { get; set; } = 1;
    public int NextCacheId { get; set; } = 1;
    public TileMap Map { get; set; } = new();
    public List<Party> Parties { get; set; } = new();
    public List<Unit> Units { get; set; } = new();
    public List<LootCache> Caches { get; set; } = new();

    /// <summary>Tiles each side has ever seen (map index), for fog of war. Sorted so saves are stable.</summary>
    public Dictionary<string, SortedSet<int>> Explored { get; set; } = new();

    /// <summary>Consecutive rounds the Network has been alarmed (see <see cref="Network"/>); reset when it calls.</summary>
    public int AlarmRounds { get; set; }

    /// <summary>Reinforcement waves the zone's pylons have called so far.</summary>
    public int ReinforcementWaves { get; set; }

    public List<ScavengeEvent> Events { get; set; } = new();

    public Unit? FindUnit(int id) => Units.Find(u => u.Id == id);
    public LootCache? FindCache(int id) => Caches.Find(c => c.Id == id);
    public Party? FindParty(int playerId) => Parties.Find(p => p.PlayerId == playerId);

    /// <summary>The unit standing on a tile, if any (alive or downed; the dead leave a cache instead).</summary>
    public Unit? UnitAt(Pos p) => Units.Find(u => u.OnMap && u.Pos == p);

    public int? CurrentUnitId => !Finished && TurnIndex < TurnOrder.Count ? TurnOrder[TurnIndex] : null;

    public Unit? CurrentUnit => CurrentUnitId is { } id ? FindUnit(id) : null;

    public void Log(ScavengeEvent e)
    {
        Events.Add(e);
        if (Events.Count > MaxEvents) Events.RemoveRange(0, Events.Count - MaxEvents);
    }
}
