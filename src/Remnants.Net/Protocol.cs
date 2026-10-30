using System.Text.Json.Serialization;
using Remnants.Core.Scavenge;

namespace Remnants.Net;

/// <summary>
/// The wire protocol between game clients and the authoritative server. Every frame is one <see cref="Message"/>
/// as JSON with a <c>type</c> discriminator (see <see cref="ProtocolJson"/>). <see cref="Version"/> is checked on
/// <see cref="Join"/>; bump it on any incompatible change to these records.
/// </summary>
public static class Protocol
{
    /// <summary>
    /// 3: co-op (lobby mode and teams, alliance messages, ping / give / carry commands, pings and alliances in views).
    /// 4: hacking and drones (hack / deployDrone / flyDrone / recallDrone / markTarget / dropPayload commands, devices and
    /// drone marks in views).
    /// 5: day and night: the run's environment (hour, phase, weather) in views; the server decides it.
    /// 6: cybernetics (eyes in bodies, units' cybernetic hooks, parts locked by interference, the limb-locked event).
    /// 7: schematics (schematic items; the result's co-op schematic copies and heard-of schematics).
    /// 8: weather: the environment's intensity, coming changes and the rounds of the last thunder and lightning.
    /// </summary>
    public const int Version = 7;
    public const int DefaultPort = 7777;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Join), "join")]
[JsonDerivedType(typeof(Ready), "ready")]
[JsonDerivedType(typeof(Command), "command")]
[JsonDerivedType(typeof(Leave), "leave")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(JoinRefused), "joinRefused")]
[JsonDerivedType(typeof(LobbyState), "lobby")]
[JsonDerivedType(typeof(MissionStarted), "missionStarted")]
[JsonDerivedType(typeof(Snapshot), "snapshot")]
[JsonDerivedType(typeof(CommandAccepted), "commandAccepted")]
[JsonDerivedType(typeof(CommandRejected), "commandRejected")]
[JsonDerivedType(typeof(TurnChanged), "turnChanged")]
[JsonDerivedType(typeof(PlayerLeft), "playerLeft")]
[JsonDerivedType(typeof(Notice), "notice")]
[JsonDerivedType(typeof(MissionEnded), "missionEnded")]
[JsonDerivedType(typeof(SetTeam), "setTeam")]
[JsonDerivedType(typeof(AllianceProposed), "allianceProposed")]
[JsonDerivedType(typeof(AllianceAccepted), "allianceAccepted")]
[JsonDerivedType(typeof(AllianceDeclined), "allianceDeclined")]
[JsonDerivedType(typeof(AllianceBreakWarning), "allianceBreakWarning")]
[JsonDerivedType(typeof(AllianceBroken), "allianceBroken")]
public abstract record Message;

// ---- client → server ----

/// <summary>
/// Take a seat with a party (lobby only), or with <paramref name="ReconnectToken"/> retake the seat a dropped
/// connection left during a mission (the party is then ignored).
/// </summary>
public sealed record Join(int Version, string Name, List<PartyMember> Party, string? ReconnectToken = null) : Message;

/// <summary>Lobby: ready (or not) to start. The mission starts once every seated player is ready.</summary>
public sealed record Ready(bool IsReady) : Message;

/// <summary>An intent: one of Core's scavenge commands. The server validates it with <see cref="ScavengeSim"/>.</summary>
public sealed record Command(int RequestId, IScavengeCommand Intent) : Message;

/// <summary>Leave for good: no reconnect, the party holds position for the rest of the mission.</summary>
public sealed record Leave : Message;

/// <summary>
/// Lobby, versus missions only: start on team <paramref name="Team"/> with every player who picks the same number (they
/// spawn together, allied), or on your own (null). In a co-op mission everyone is on team 1.
/// </summary>
public sealed record SetTeam(int? Team) : Message;

// ---- server → client ----

/// <summary>Seated as <paramref name="PlayerId"/>; keep the token to reconnect after a dropped connection.</summary>
public sealed record Welcome(int Version, int PlayerId, string ReconnectToken) : Message;

public sealed record JoinRefused(string Reason) : Message;

/// <summary>Versus: everyone for themselves unless they team up in the lobby or in the field. Co-op: everyone on one team.</summary>
public enum MissionMode { Versus, Coop }

/// <summary><paramref name="Team"/>: the starting team (null = on their own).</summary>
public sealed record LobbyPlayer(int PlayerId, string Name, int PartySize, bool Ready, bool Connected, int? Team = null);

public sealed record LobbyState(string ZoneId, int MinPlayers, int MaxPlayers, List<LobbyPlayer> Players, MissionMode Mode = MissionMode.Versus) : Message;

/// <summary><paramref name="Team"/>: the team the player started on (null = on their own); alliances change in the field.</summary>
public sealed record PlayerInfo(int PlayerId, string Name, int? Team = null);

/// <summary>
/// The mission is on. The terrain is sent once here (it never changes during a run); every
/// <see cref="Snapshot"/> after it leaves the map out.
/// </summary>
public sealed record MissionStarted(
    string ZoneId, TileMap Map, List<PlayerInfo> Players, double TurnSeconds, double ReconnectSeconds, MissionMode Mode = MissionMode.Versus) : Message;

/// <summary>
/// What the receiving player's side may know right now (see <see cref="SideFog"/>): its own units, others only on
/// tiles it sees, caches as last seen, no RNG state. <paramref name="Events"/> are the new events it may hear about
/// since the previous snapshot, oldest first. Sent after every accepted command and server-side turn change.
/// </summary>
public sealed record Snapshot(ScavengeState View, List<int> Visible, List<ScavengeEvent> Events) : Message;

/// <summary>Sent after the <see cref="Snapshot"/> that carries the command's effects.</summary>
public sealed record CommandAccepted(int RequestId, string Message) : Message;

public sealed record CommandRejected(int RequestId, ScavengeError Error, string Message) : Message;

/// <summary>
/// Whose turn it is. <paramref name="UnitId"/> only when the receiver may see that unit; <paramref name="PlayerId"/>
/// when a player (not the AI) is up. <paramref name="SecondsLeft"/> is that player's time to act (0 = no timer).
/// </summary>
public sealed record TurnChanged(int Round, int? UnitId, int? PlayerId, double SecondsLeft) : Message;

/// <summary>A player dropped. <paramref name="ReconnectSeconds"/> &gt; 0 while they may still come back; 0 = gone for good.</summary>
public sealed record PlayerLeft(int PlayerId, string Name, double ReconnectSeconds) : Message;

/// <summary>Something for the log that is not a Core event (a turn timed out, a player rejoined).</summary>
public sealed record Notice(string Text) : Message;

/// <summary>The receiver's run is over: their <see cref="ExtractionResult"/>, to apply to their colony.</summary>
public sealed record MissionEnded(ExtractionResult Result, string Reason) : Message;

// ---- alliances (server → the players involved only; see Core's Alliances) ----

/// <summary><paramref name="FromPlayerId"/> offers <paramref name="ToPlayerId"/> an alliance; the offer lapses after <paramref name="UntilRound"/>.</summary>
public sealed record AllianceProposed(int FromPlayerId, int ToPlayerId, int UntilRound) : Message;

/// <summary>The players are allies now (shared sight, no friendly fire). Sent to everyone on the merged team.</summary>
public sealed record AllianceAccepted(List<int> Team) : Message;

/// <summary>An offer was turned down (or lapsed, <paramref name="Lapsed"/>).</summary>
public sealed record AllianceDeclined(int FromPlayerId, int ToPlayerId, bool Lapsed) : Message;

/// <summary><paramref name="PlayerId"/> leaves the team when round <paramref name="EffectiveRound"/> starts. Sent to the whole team.</summary>
public sealed record AllianceBreakWarning(int PlayerId, int EffectiveRound) : Message;

/// <summary><paramref name="PlayerId"/> has left the team: no more shared sight. Sent to the leaver and the team they left.</summary>
public sealed record AllianceBroken(int PlayerId, List<int> FormerAllies) : Message;
