namespace Remnants.Core.Scavenge;

/// <summary>A player's party in the instance. Several can share one map (multiplayer later).</summary>
public sealed class Party
{
    public int PlayerId { get; set; }

    /// <summary>The player's name for log lines ("" = unnamed: "Player N"; saves from before names load so).</summary>
    public string Name { get; set; } = "";

    /// <summary>The party's side now: <see cref="Unit.PlayerSide"/> alone, or its team's <see cref="Unit.TeamSide"/> while allied.</summary>
    public string Side { get; set; } = "";

    /// <summary>No controllable unit left on the map: the party's run is over and its result is final.</summary>
    public bool Done { get; set; }

    /// <summary>The player is gone for good (multiplayer): their units hold position, and allies stop counting on them.</summary>
    public bool Left { get; set; }

    /// <summary>Items a cargo drone ferried out of the zone (and the drone itself): home whatever happens to the party.</summary>
    public List<string> Secured { get; set; } = new();

    /// <summary>Cards the party found in this run (loot, salvage): they join the colony's collection only if someone gets out.</summary>
    public List<string> CardPouch { get; set; } = new();

    /// <summary>What the party learned of enemy types this run (see <see cref="Scavenge.Bestiary"/>), and the enemies already counted.</summary>
    public SortedDictionary<string, BestiaryEntry> Bestiary { get; set; } = new(StringComparer.Ordinal);
    public SortedSet<int> SeenIds { get; set; } = new();
    public SortedSet<int> FoughtIds { get; set; } = new();
    public SortedSet<int> KilledIds { get; set; } = new();
}

public enum ScavengeEventType
{
    RoundStarted, Moved, Attacked, Missed, Injured, LimbLost, Downed, Died, BledOut,
    Stabilized, Revived, Healed, Fractured, Treated, Crashed, Evacuated, Extracted, PickedUp, Recruited, Spotted, PartyDone, Finished,
    Suppressed, Flanked, Retreated, Reinforced,
    Ambushed, Overwatch, Braced, Lookout, Revealed, FlareLit, SensorPlaced, SensorTripped,
    AllianceProposed, AllianceAccepted, AllianceDeclined, AllianceBreakWarning, AllianceBroken,
    Pinged, ItemGiven, PickedUpBody, PutDown,
    HackProgress, Hacked, HackFailed, Alarm, Overridden, OverrideEnded, DeviceDisabled,
    DroneDeployed, DroneFlown, DroneRecalled, DroneLanded, Marked, PayloadDropped,
    RelayDisrupted,
    LimbLocked,
    ObjectiveUpdated, Chatter, MissionPrompt, MissionEnded,
    Stashed, VehicleHit, VehicleWrecked, VehicleTurret, Salvaged,
    WeatherChanged, Thunder, Lightning,
    EmpBurst, DecoyDeployed, DecoyGone, NoiseDecoy, DroneHealed, DroneLoaded, DroneFerried, DroneFired, Traced,
    GearModded,
    LootDropped, ItemDropped, SalvageProgress,
    IntentDeclared, IntentResolved, IntentFizzled, IntentCanceled,
    DeckShuffled, CardPlayed, CardDiscarded, CardFound, Countered,
}

/// <summary>
/// Something notable for the UI log or animation. Purely informational.
/// <see cref="Anonymous"/> is the text for a side that cannot see the actor (<see cref="UnitId"/>): "Ana was hit from
/// the fog" instead of naming the Linked; without it such a side does not hear of the event at all (see
/// <see cref="FogLog"/>). <see cref="Side"/> limits the event to one side (a motion sensor's alert).
/// </summary>
/// <see cref="SeenBy"/>: the party sides that saw the actor when the event was logged (filled in by the sim), so a side
/// hears a hidden unit's moves only while it actually saw them. <see cref="Sides"/> sends the event to exactly these
/// sides whatever they see (alliance news goes to the two parties involved and nobody else). <see cref="Tier"/>: how a
/// landed attack resolved (graze, hit, solid); <see cref="Amount"/>: the HP it took.
public sealed record ScavengeEvent(
    int Round, ScavengeEventType Type, string Message, int? UnitId = null, int? TargetId = null, Pos? Pos = null,
    string? Anonymous = null, string? Side = null, List<string>? SeenBy = null, List<string>? Sides = null, HitTier? Tier = null,
    double? Amount = null);

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

/// <summary>A target marked by a side's scout drone: the side sees it through walls and stealth and hits it more easily.</summary>
public sealed record Mark(int UnitId, string Side, int UntilRound);

/// <summary>One side's hack in progress on a device: progress points done of those needed, and who is working on it.</summary>
public sealed record HackProgress(string Side, int HackerId, int Done, int Needed);

/// <summary>A side locked out of a device after a failed hack, through <see cref="UntilRound"/>.</summary>
public sealed record Lockout(string Side, int UntilRound);

/// <summary>
/// A hackable device on the map (see <see cref="Hacking"/>). Devices that act (a camera, a turret) are also units
/// (<see cref="UnitId"/>, <see cref="UnitKind.Device"/>), so sight, fire, fog and being shot work as for anyone; their
/// side is the controller. The others (terminal, door, locker, relay node) block their tile (a door only for sides other
/// than its controller). Everything starts working for the Network (<see cref="Unit.EnemySide"/>); a hack hands the
/// device to the hacker's side (<see cref="Controller"/>; <see cref="ControllerPlayerId"/> decides who keeps it when an
/// alliance breaks). Per-side state (progress, lockouts) and contents are private: the multiplayer fog never sends them
/// to other sides.
/// </summary>
public sealed class ZoneDevice
{
    public int Id { get; set; }
    public DeviceKind Kind { get; set; }
    public Pos Pos { get; set; }
    public bool Networked { get; set; }

    /// <summary>The side it works for (for unit devices this mirrors the unit's side).</summary>
    public string Controller { get; set; } = Unit.EnemySide;
    public int? ControllerPlayerId { get; set; }
    public int? UnitId { get; set; }

    /// <summary>Destroyed (its unit was shot down): no longer works or can be hacked.</summary>
    public bool Disabled { get; set; }

    /// <summary>A terminal downloaded, a locker opened.</summary>
    public bool Used { get; set; }

    /// <summary>Failed hacks on it so far (a story device may fuse, see <see cref="FieldMission.FuseAfter"/>).</summary>
    public int Failures { get; set; }

    /// <summary>What a terminal or locker holds until it is hacked.</summary>
    public List<string> Items { get; set; } = new();
    public List<HackProgress> Progress { get; set; } = new();
    public List<Lockout> Lockouts { get; set; } = new();

    /// <summary>Story missions: a script tag objectives point at ("terminal", "locker").</summary>
    public string? Tag { get; set; }

    /// <summary>Whether a side other than the Network controls it.</summary>
    public bool Hacked => Controller != Unit.EnemySide;
}

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

    /// <summary>Time of day and weather the run is played in (see <see cref="FieldEnvironment"/>).</summary>
    public ScavengeEnvironment Environment { get; set; } = new();
    public ulong RngState { get; set; }
    public int Round { get; set; }

    /// <summary>Unit ids in initiative order for the current round.</summary>
    public List<int> TurnOrder { get; set; } = new();
    public int TurnIndex { get; set; }

    /// <summary>Which of the round's sub-rounds is being played, from 1 (<see cref="TurnRules.SubRounds"/>).</summary>
    public int SubRound { get; set; } = 1;

    /// <summary>Tile attacks declared this sub-round, in declaration order; they strike at its end (<see cref="TileStrikes"/>).</summary>
    public List<TileStrike> Strikes { get; set; } = new();
    public bool Finished { get; set; }

    public int NextUnitId { get; set; } = 1;
    public int NextCacheId { get; set; } = 1;
    public TileMap Map { get; set; } = new();
    public List<Party> Parties { get; set; } = new();
    public List<Unit> Units { get; set; } = new();
    public List<LootCache> Caches { get; set; } = new();

    /// <summary>Salvage nodes (<see cref="FieldSalvage"/>): shared by every side, worked down lot by lot.</summary>
    public List<SalvageNode> SalvageNodes { get; set; } = new();
    public int NextSalvageId { get; set; } = 1;

    /// <summary>Seeds the per-kill and per-lot loot rolls (<see cref="FieldLoot.Rng"/>). Never sent to clients.</summary>
    public ulong LootSeed { get; set; }

    /// <summary>The card stream: every deck shuffle draws from it, never from the game's RNG. Never sent to clients.</summary>
    public ulong CardRngState { get; set; }

    /// <summary>Each party's squad deck (<see cref="SquadDecks"/>); none for a party that brought no cards.</summary>
    public List<SquadDeck> Decks { get; set; } = new();

    /// <summary>The Story difficulty the run is played at (loot quantities, <see cref="LootRules.Difficulty"/>); null in Sandbox.</summary>
    public string? Difficulty { get; set; }

    /// <summary>Schematics a side learned of without getting them (a failed terminal hack): side → schematic ids.</summary>
    public SortedDictionary<string, SortedSet<string>> HeardSchematics { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Schematics the colony running this instance already knows (picked at half weight by enemy drops).</summary>
    public SortedSet<string> KnownSchematics { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Tiles each side has ever seen (map index), for fog of war. Sorted so saves are stable.</summary>
    public Dictionary<string, SortedSet<int>> Explored { get; set; } = new();

    /// <summary>Consecutive rounds the Network has been alarmed (see <see cref="Network"/>); reset when it calls.</summary>
    public int AlarmRounds { get; set; }

    /// <summary>Reinforcement waves the zone's pylons have called so far.</summary>
    public int ReinforcementWaves { get; set; }

    /// <summary>The run took its zone's retune (<see cref="ZoneDef.Retune"/>): reinforcements follow it too.</summary>
    public bool Retuned { get; set; }

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

    /// <summary>Hackable devices (<see cref="Hacking"/>) and drone marks (<see cref="Drones"/>).</summary>
    public List<ZoneDevice> Devices { get; set; } = new();
    public int NextDeviceId { get; set; } = 1;
    public List<Mark> Marks { get; set; } = new();

    /// <summary>A story mission's objectives, triggers and chatter (<see cref="MissionRules"/>); null on a plain run.</summary>
    public MissionRun? Mission { get; set; }
    /// <summary>Parties' parked vehicles: extraction points and loot stashes (<see cref="ParkedVehicles"/>).</summary>
    public List<ParkedVehicle> Vehicles { get; set; } = new();

    /// <summary>Wrecks already stripped for parts, by the map index of their first tile (<see cref="Salvaging"/>).</summary>
    public SortedSet<int> SalvagedWrecks { get; set; } = new();

    public List<ScavengeEvent> Events { get; set; } = new();

    public Unit? FindUnit(int id) => Units.Find(u => u.Id == id);
    public LootCache? FindCache(int id) => Caches.Find(c => c.Id == id);
    public Party? FindParty(int playerId) => Parties.Find(p => p.PlayerId == playerId);
    public ZoneDevice? FindDevice(int id) => Devices.Find(d => d.Id == id);

    /// <summary>The device on a tile (a camera may sit in a wall), if any.</summary>
    public ZoneDevice? DeviceAt(Pos p) => Devices.Find(d => d.Pos == p);

    /// <summary>A player's name for log lines: their party's <see cref="Party.Name"/>, else "Player N".</summary>
    public string PlayerName(int playerId) => FindParty(playerId)?.Name is { Length: > 0 } name ? name : $"Player {playerId}";

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
