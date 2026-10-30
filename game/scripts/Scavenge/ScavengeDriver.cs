using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Remnants.Core.Scavenge;
using Remnants.Net;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Where the scavenge screen's game runs: a <see cref="LocalDriver"/> owns a <see cref="ScavengeSim"/> in-process;
/// a <see cref="RemoteDriver"/> sends intents to the multiplayer server and shows its fog-filtered view. The
/// presenter and views talk only to this, so one screen plays both.
/// </summary>
public interface IScavengeDriver
{
    /// <summary>The sim to ask questions of: the real one locally, a read-only model of the server's view remotely.</summary>
    ScavengeSim Sim { get; }

    int PlayerId { get; }

    /// <summary>Tiles the local side sees right now.</summary>
    HashSet<int> Visible { get; }

    /// <summary>Units the local side sees right now (its own included): in sight and detected, lit, revealed or scanned.</summary>
    HashSet<int> SeenUnits { get; }

    Task<ScavengeResult> Execute(IScavengeCommand command);

    /// <summary>Events since the last call the local side may hear of, oldest first, unseen actors made anonymous (<see cref="FogLog"/>).</summary>
    List<ScavengeEvent> TakeNewEvents();

    /// <summary>The local player's result: final once their run is over, null while a remote result is still on its way.</summary>
    ExtractionResult? Result();

    /// <summary>The state changed without a local command (another player's turn, a timeout). Never raised locally.</summary>
    event Action? Pushed;

    /// <summary>A line for the log that is not a Core event (multiplayer notices).</summary>
    event Action<string>? Noticed;

    /// <summary>Remote only: who is up and how long they have, or null when there is nothing to say.</summary>
    string? TurnStatus();

    /// <summary>The player walks away mid-run (the screen's Leave).</summary>
    void Leave();

    /// <summary>A player's name for labels ("Player 2" locally).</summary>
    string PlayerName(int playerId);

    /// <summary>Remote only: a line of alliance news for this player (offer, accepted, declined, break warning, broken), for banners.</summary>
    event Action<string>? AllianceNews;
}

/// <summary>Single player: the sim lives here and every command runs synchronously.</summary>
public sealed class LocalDriver : IScavengeDriver
{
    private ScavengeEvent? _lastSeen;
    private HashSet<int> _unitsBefore, _tilesBefore;

    public LocalDriver(ScavengeSim sim, int playerId)
    {
        Sim = sim;
        PlayerId = playerId;
        var events = sim.State.Events;
        _lastSeen = events.Count > 0 ? events[^1] : null;
        _unitsBefore = SeenUnits;
        _tilesBefore = Visible;
    }

    public ScavengeSim Sim { get; }
    public int PlayerId { get; }
    private string Side => Sim.State.FindParty(PlayerId)?.Side ?? Unit.PlayerSide(PlayerId);
    public HashSet<int> Visible => Sim.VisibleTo(PlayerId);
    public HashSet<int> SeenUnits => Detection.SeenBy(Sim.Data, Sim.State, Side);

    public event Action? Pushed { add { } remove { } }
    public event Action<string>? Noticed { add { } remove { } }
    public event Action<string>? AllianceNews { add { } remove { } }

    public string PlayerName(int playerId) => $"Player {playerId}";

    public Task<ScavengeResult> Execute(IScavengeCommand command) => Task.FromResult(Sim.Execute(command));

    /// <summary>
    /// Core events logged since the last call (the list is capped, so the last one seen is found by identity), as the
    /// local side hears them: judged against what it saw at the last call and sees now, as the server would.
    /// </summary>
    public List<ScavengeEvent> TakeNewEvents()
    {
        var events = Sim.State.Events;
        var start = 0;
        if (_lastSeen is not null)
            for (var i = events.Count - 1; i >= 0; i--)
                if (ReferenceEquals(events[i], _lastSeen)) { start = i + 1; break; }
        var fresh = events.Skip(start).ToList();
        if (events.Count > 0) _lastSeen = events[^1];
        var heard = FogLog.For(Sim, Side, fresh, _unitsBefore, _tilesBefore);
        _unitsBefore = SeenUnits;
        _tilesBefore = Visible;
        return heard;
    }

    public ExtractionResult Result() => Sim.ResultFor(PlayerId);

    public string? TurnStatus() => null;

    public void Leave() { }
}

/// <summary>
/// Multiplayer: a <see cref="NetClient"/> whose messages are already marshalled to the main thread. The view is
/// replaced by every snapshot, so <see cref="Sim"/> is read fresh each time.
/// </summary>
public sealed class RemoteDriver : IScavengeDriver
{
    private readonly NetClient _client;

    public RemoteDriver(NetClient client)
    {
        _client = client;
        client.Changed += () => Pushed?.Invoke();
        client.Noticed += text => Noticed?.Invoke(text);
        client.AllianceNews += m => AllianceNews?.Invoke(client.Describe(m));
    }

    public string PlayerName(int playerId) => _client.NameOf(playerId);
    public event Action<string>? AllianceNews;

    public NetClient Client => _client;
    public ScavengeSim Sim => _client.View ?? throw new InvalidOperationException("No view from the server yet.");
    public int PlayerId => _client.PlayerId;
    public HashSet<int> Visible => _client.Visible;

    /// <summary>The server only sends units the side sees.</summary>
    public HashSet<int> SeenUnits => _client.View is { } view ? view.State.Units.Where(u => u.OnMap).Select(u => u.Id).ToHashSet() : [];

    public event Action? Pushed;
    public event Action<string>? Noticed;

    public Task<ScavengeResult> Execute(IScavengeCommand command) => _client.Execute(command);

    public List<ScavengeEvent> TakeNewEvents() => _client.TakeEvents();

    public ExtractionResult? Result() => _client.Result
        ?? (_client.Phase == ClientPhase.Disconnected ? new ExtractionResult(PlayerId, false, [], [], [], new()) : null);

    public string? TurnStatus()
    {
        if (_client.Phase == ClientPhase.Disconnected)
            return _client.CanReconnect && _client.RefusedReason is null ? "Connection lost · reconnecting…" : "Disconnected from the server";
        if (_client.Turn is not { } turn) return null;
        var left = _client.TurnSecondsLeft(DateTimeOffset.UtcNow);
        var clock = turn.SecondsLeft > 0 ? $" · {Math.Ceiling(left):0}s" : "";
        return turn.PlayerId is not { } pid ? "AI turn…"
            : pid == PlayerId ? $"Your turn{clock}"
            : $"{_client.NameOf(pid)}'s turn{clock}";
    }

    public void Leave() => _client.Leave();
}
