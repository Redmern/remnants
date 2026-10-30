namespace Remnants.Core.Scavenge;

/// <summary>A player's party in the instance. Several can share one map (multiplayer later).</summary>
public sealed class Party
{
    public int PlayerId { get; set; }

    /// <summary>The party's side now: <see cref="Unit.PlayerSide"/> alone, or its team's <see cref="Unit.TeamSide"/> while allied.</summary>
    public string Side { get; set; } = "";

    /// <summary>No controllable unit left on the map: the party's run is over and its result is final.</summary>
    public bool Done { get; set; }

    /// <summary>The player is gone for good (multiplayer): their units hold position, and allies stop counting on them.</summary>
    public bool Left { get; set; }
}

public enum ScavengeEventType
{
    RoundStarted, Moved, Attacked, Missed, Injured, LimbLost, Downed, Died, BledOut,
    Stabilized, Revived, Healed, Evacuated, Extracted, PickedUp, Recruited, Spotted, PartyDone, Finished,
    Suppressed, Flanked, Retreated, Reinforced,
    Ambushed, Overwatch, Braced, Lookout, Revealed, FlareLit, SensorPlaced, SensorTripped,
    AllianceProposed, AllianceAccepted, AllianceDeclined, AllianceBreakWarning, AllianceBroken,
    Pinged, ItemGiven, PickedUpBody, PutDown,
}

/// <summary>
/// Something notable for the UI log or animation. Purely informational.
/// <see cref="Anonymous"/> is the text for a side that cannot see the actor (<see cref="UnitId"/>): "Ana was hit from
/// the fog" instead of naming the Linked; without it such a side does not hear of the event at all (see
/// <see cref="FogLog"/>). <see cref="Side"/> limits the event to one side (a motion sensor's alert).
/// </summary>
/// <see cref="SeenBy"/>: the party sides that saw the actor when the event was logged (filled in by the sim), so a side
/// hears a hidden unit's moves only while it actually saw them. <see cref="Sides"/> sends the event to exactly these
/// sides whatever they see (alliance news goes to the two parties involved and nobody else).
public sealed record ScavengeEvent(
    int Round, ScavengeEventType Type, string Message, int? UnitId = null, int? TargetId = null, Pos? Pos = null,
    string? Anonymous = null, string? Side = null, List<string>? SeenBy = null, List<string>? Sides = null);

/// <summary>A side knows something is near a tile without knowing what: a "?" on its map.</summary>
public enum ContactKind
{
    /// <summary>Heard gunfire or a fight.</summary>
    Noise,
    /// <summary>Heard something moving close by.</summary>
    Movement,
    /// <summary>Where an attack on one of its units came from (with <see cref="Contact.Toward"/>).</summary>
    Attack,
    /// <summary>A motion sensor went off.</summary>
    Sensor,
}

/// <summary>
/// A per-side "something is there". <see cref="Pos"/> is approximate (up to <see cref="FogRules.ContactFuzz"/> off), it
/// carries no identity, and it fades after <see cref="FogRules.ContactRounds"/>.
/// </summary>
public sealed record Contact(string Side, Pos Pos, ContactKind Kind, int Round, Pos? Toward = null);

/// <summary>A unit revealed to a side (muzzle flash, motion sensor) through <see cref="UntilRound"/>: stealth does not hide it there.</summary>
public sealed record Reveal(int UnitId, string Side, int UntilRound);

/// <summary>A burning flare: every side sees every tile within the radius, and everyone on them.</summary>
public sealed record Flare(Pos Pos, double Radius, int UntilRound);

/// <summary>
/// A motion sensor placed by a side: reveals and alerts on hostiles that move within its radius. <see cref="PlayerId"/>
/// (who placed it) decides which side keeps it when an alliance breaks.
/// </summary>
public sealed record Sensor(int Id, string Side, Pos Pos, int Radius, string ItemId, int? PlayerId = null);

/// <summary>What a team ping says.</summary>
public enum PingKind { Danger, GoHere, Loot }

/// <summary>A tile marked for the pinging player's team only; fades after <see cref="CoopRules.PingRounds"/>.</summary>
public sealed record Ping(string Side, int PlayerId, Pos Pos, PingKind Kind, int Round);

/// <summary>An open offer of alliance from one player to another; lapses after <see cref="CoopRules.ProposalRounds"/>.</summary>
public sealed record AllianceProposal(int FromPlayerId, int ToPlayerId, int Round);

/// <summary>
/// A player leaving their team: announced in <see cref="Round"/>, takes effect when round <see cref="EffectiveRound"/>
/// starts, so the team gets a full round's warning (no instant backstab).
/// </summary>
public sealed record AllianceBreak(int PlayerId, string Side, int Round, int EffectiveRound);

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

    /// <summary>Per-side contacts ("?" markers). Expired ones are dropped at round start.</summary>
    public List<Contact> Contacts { get; set; } = new();

    /// <summary>Units revealed to a side despite their stealth.</summary>
    public List<Reveal> Reveals { get; set; } = new();
    public List<Flare> Flares { get; set; } = new();
    public List<Sensor> Sensors { get; set; } = new();
    public int NextSensorId { get; set; } = 1;

    /// <summary>Co-op: open alliance offers, announced breaks, team pings, and the next team id (<see cref="Alliances"/>).</summary>
    public List<AllianceProposal> Proposals { get; set; } = new();
    public List<AllianceBreak> Breaks { get; set; } = new();
    public List<Ping> Pings { get; set; } = new();
    public int NextTeamId { get; set; } = 1;

    public List<ScavengeEvent> Events { get; set; } = new();

    public Unit? FindUnit(int id) => Units.Find(u => u.Id == id);
    public LootCache? FindCache(int id) => Caches.Find(c => c.Id == id);
    public Party? FindParty(int playerId) => Parties.Find(p => p.PlayerId == playerId);

    /// <summary>The unit standing on a tile, if any (alive or downed; the dead leave a cache instead).</summary>
    public Unit? UnitAt(Pos p) => Units.Find(u => u.OnMap && u.Pos == p && u.CarriedById is null);

    /// <summary>The player ids whose parties are on <paramref name="side"/> now (one alone, several while allied).</summary>
    public IEnumerable<int> PlayersOn(string side) => Parties.Where(p => p.Side == side).Select(p => p.PlayerId);

    /// <summary>Whether two players are on one team right now.</summary>
    public bool Allied(int a, int b) => a != b && FindParty(a) is { } pa && FindParty(b) is { } pb && pa.Side == pb.Side;

    public int? CurrentUnitId => !Finished && TurnIndex < TurnOrder.Count ? TurnOrder[TurnIndex] : null;

    public Unit? CurrentUnit => CurrentUnitId is { } id ? FindUnit(id) : null;

    /// <summary>Set by the sim: fills <see cref="ScavengeEvent.SeenBy"/> as an event is logged. Not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<ScavengeEvent, ScavengeEvent>? Stamp { get; set; }

    public void Log(ScavengeEvent e)
    {
        if (Stamp is { } stamp) e = stamp(e);
        Events.Add(e);
        if (Events.Count > MaxEvents) Events.RemoveRange(0, Events.Count - MaxEvents);
    }
}
