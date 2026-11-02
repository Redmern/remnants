using Remnants.Core.Scavenge;
using Remnants.Core.Trade;

namespace Remnants.Net;

public enum ClientPhase { Connecting, Lobby, Playing, Ended, Disconnected }

/// <summary>
/// The client side of a multiplayer session, without any engine: joins, readies, sends intents and keeps the latest
/// fog-filtered view. <see cref="View"/> is a <see cref="ScavengeSim"/> over that view, used only to <b>ask</b> Core
/// questions (reachable tiles, hit chances, profiles) for the UI; it is never executed — the server's sim is the game.
/// <para>
/// Messages are handled on the thread that <paramref name="post"/> runs them on (the Godot main thread in the game,
/// inline in tests), so a TCP reader thread never touches the state.
/// </para>
/// </summary>
public sealed class NetClient
{
    private IClientTransport _transport;
    private readonly Action<Action> _post;
    private readonly Dictionary<int, TaskCompletionSource<ScavengeResult>> _pending = new();
    private readonly List<ScavengeEvent> _events = new();
    private int _nextRequest = 1;
    private DateTimeOffset _turnReceived;
    private string _name = "";

    public NetClient(IClientTransport transport, ScavengeData data, Action<Action>? post = null)
    {
        _transport = transport;
        Data = data;
        _post = post ?? (a => a());
        Attach(transport);
    }

    /// <summary>Listens to a link; messages from a link that was since replaced (<see cref="Reconnect"/>) are dropped.</summary>
    private void Attach(IClientTransport transport)
    {
        transport.Received += m => _post(() =>
        {
            if (ReferenceEquals(transport, _transport)) Handle(m);
        });
        transport.Closed += () => _post(() =>
        {
            if (ReferenceEquals(transport, _transport)) OnClosed();
        });
    }

    public ScavengeData Data { get; }
    public ClientPhase Phase { get; private set; } = ClientPhase.Connecting;
    public int PlayerId { get; private set; }
    public string? ReconnectToken { get; private set; }
    public string? RefusedReason { get; private set; }
    public LobbyState? Lobby { get; private set; }
    public MissionStarted? Mission { get; private set; }

    /// <summary>A read-only model of what this side knows, rebuilt from each <see cref="Snapshot"/>.</summary>
    public ScavengeSim? View { get; private set; }

    public HashSet<int> Visible { get; private set; } = [];
    public TurnChanged? Turn { get; private set; }

    /// <summary>This player's result, once their run is over.</summary>
    public ExtractionResult? Result { get; private set; }
    public string? EndReason { get; private set; }

    /// <summary>The player chose to <see cref="Leave"/>; a dropped link (false) may <see cref="Reconnect"/>.</summary>
    public bool LeftOnPurpose { get; private set; }

    /// <summary>A dropped mission seat that can still be retaken with the reconnect token.</summary>
    public bool CanReconnect => Phase == ClientPhase.Disconnected && !LeftOnPurpose && ReconnectToken is not null && Mission is not null;

    /// <summary>Anything changed (lobby, view, turn, result, connection).</summary>
    public event Action? Changed;

    /// <summary>A line for the log: notices, players dropping and coming back, alliance news.</summary>
    public event Action<string>? Noticed;

    /// <summary>Alliance news for this player (offer, accepted, declined, break warning, broken), for banners.</summary>
    public event Action<Message>? AllianceNews;

    /// <summary>The latest alliance message for this player (banners re-read it).</summary>
    public Message? LastAllianceNews { get; private set; }

    /// <summary>Open live trades this player is in, by trade id (the latest table from the server).</summary>
    public Dictionary<int, TradeView> Trades { get; } = new();

    /// <summary>Unique per client session: tells this session's escrows apart from another server's equal trade ids.</summary>
    public string SessionTag { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>A trade message arrived (invite, update, commit, cancelled), after <see cref="Trades"/> was updated.</summary>
    public event Action<Message>? TradeNews;

    public void RequestTrade(int playerId) => _transport.Send(new TradeRequest(playerId));
    public void RespondTrade(int tradeId, bool accept) => _transport.Send(new TradeRespond(tradeId, accept));
    public void OfferTrade(int tradeId, List<TradeGood> goods) => _transport.Send(new TradeOffer(tradeId, goods));
    public void LockTrade(int tradeId, bool locked) => _transport.Send(new TradeLock(tradeId, locked));
    public void ConfirmTrade(int tradeId) => _transport.Send(new TradeConfirm(tradeId));
    public void CancelTrade(int tradeId) => _transport.Send(new TradeCancel(tradeId));

    /// <summary>My side and the other side of a trade.</summary>
    public (TradeSideView Me, TradeSideView Them) SidesOf(TradeView trade) =>
        trade.Sides[0].PlayerId == PlayerId ? (trade.Sides[0], trade.Sides[1]) : (trade.Sides[1], trade.Sides[0]);

    /// <summary>Lobby, versus missions: start on a team with whoever picks the same number (null = on your own).</summary>
    public void SetTeam(int? team) => _transport.Send(new SetTeam(team));

    public string NameOf(int playerId) =>
        Mission?.Players.Find(p => p.PlayerId == playerId)?.Name ?? Lobby?.Players.Find(p => p.PlayerId == playerId)?.Name ?? $"Player {playerId}";

    /// <summary>Seconds the player who is up has left, counted down locally from the last <see cref="TurnChanged"/>.</summary>
    public double TurnSecondsLeft(DateTimeOffset now) =>
        Turn is { SecondsLeft: > 0 } t ? Math.Max(0, t.SecondsLeft - (now - _turnReceived).TotalSeconds) : 0;

    /// <summary>Take a seat with a party, driving <paramref name="vehicle"/> in if the colony sent one.</summary>
    public void Join(string name, List<PartyMember> party, string? reconnectToken = null, FieldVehicle? vehicle = null, List<string>? deck = null)
    {
        _name = name;
        _transport.Send(new Join(Protocol.Version, name, party, reconnectToken, vehicle, deck));
    }

    /// <summary>
    /// Retakes the seat over a new link after the old one dropped. The view, log and result carry on; the server
    /// answers with a fresh <see cref="MissionStarted"/> and <see cref="Snapshot"/>.
    /// </summary>
    public void Reconnect(IClientTransport transport)
    {
        if (!CanReconnect) throw new InvalidOperationException("Nothing to reconnect to.");
        _transport = transport;
        RefusedReason = null;
        Attach(transport);
        transport.Send(new Join(Protocol.Version, _name, [], ReconnectToken));
    }

    public void SetReady(bool ready) => _transport.Send(new Ready(ready));

    /// <summary>Sends an intent; completes with the server's verdict (after the snapshot that carries its effects).</summary>
    public Task<ScavengeResult> Execute(IScavengeCommand command)
    {
        if (Phase != ClientPhase.Playing)
            return Task.FromResult(ScavengeResult.Fail(ScavengeError.Finished, "Not connected to a running mission."));
        var id = _nextRequest++;
        var tcs = new TaskCompletionSource<ScavengeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        _transport.Send(new Command(id, command));
        return tcs.Task;
    }

    /// <summary>Leave for good (no reconnect).</summary>
    public void Leave()
    {
        LeftOnPurpose = true;
        if (Phase is ClientPhase.Disconnected) return;
        _transport.Send(new Leave());
        _transport.Close();
    }

    /// <summary>Events received since the last call, oldest first.</summary>
    public List<ScavengeEvent> TakeEvents()
    {
        var list = _events.ToList();
        _events.Clear();
        return list;
    }

    private void Handle(Message message)
    {
        switch (message)
        {
            case Welcome w:
                PlayerId = w.PlayerId;
                ReconnectToken = w.ReconnectToken;
                if (Phase is ClientPhase.Connecting or ClientPhase.Disconnected) Phase = ClientPhase.Lobby;
                break;
            case JoinRefused r:
                RefusedReason = r.Reason;
                Noticed?.Invoke(r.Reason);
                break;
            case LobbyState l:
                Lobby = l;
                break;
            case MissionStarted m:
                Mission = m;
                Phase = ClientPhase.Playing;
                break;
            case Snapshot s:
                if (Mission is null) return;
                s.View.Map = Mission.Map;
                View = new ScavengeSim(Data, s.View);
                Visible = s.Visible.ToHashSet();
                _events.AddRange(s.Events);
                break;
            case CommandAccepted a:
                Complete(a.RequestId, ScavengeResult.Success(a.Message));
                break;
            case CommandRejected r:
                Complete(r.RequestId, ScavengeResult.Fail(r.Error, r.Message));
                break;
            case TurnChanged t:
                Turn = t;
                _turnReceived = DateTimeOffset.UtcNow;
                break;
            case PlayerLeft p:
                Noticed?.Invoke(p.ReconnectSeconds > 0 ? $"{p.Name} lost connection; their party holds position." : $"{p.Name} has left.");
                break;
            case Notice n:
                Noticed?.Invoke(n.Text);
                break;
            case AllianceProposed or AllianceAccepted or AllianceDeclined or AllianceBreakWarning or AllianceBroken:
                LastAllianceNews = message;
                Noticed?.Invoke(Describe(message));
                AllianceNews?.Invoke(message);
                break;
            case TradeInvite i:
                Noticed?.Invoke($"{i.FromName} wants to trade.");
                TradeNews?.Invoke(message);
                break;
            case TradeUpdate u:
                Trades[u.Trade.TradeId] = u.Trade;
                TradeNews?.Invoke(message);
                break;
            case TradeCommit c:
                Trades.Remove(c.TradeId);
                Noticed?.Invoke($"Trade with {c.PartnerName} done.");
                TradeNews?.Invoke(message);
                break;
            case TradeCancelled c:
                Trades.Remove(c.TradeId);
                Noticed?.Invoke(c.Reason);
                TradeNews?.Invoke(message);
                break;
            case MissionEnded e:
                Result ??= e.Result;
                EndReason = e.Reason;
                Phase = ClientPhase.Ended;
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>A log line for an alliance message, from this player's point of view.</summary>
    public string Describe(Message message)
    {
        string Who(int id) => id == PlayerId ? "you" : NameOf(id);
        string Them(IEnumerable<int> ids) => string.Join(" and ", ids.Where(id => id != PlayerId).Select(NameOf));
        return message switch
        {
            AllianceProposed p when p.ToPlayerId == PlayerId => $"{NameOf(p.FromPlayerId)} offers an alliance (answer on your turn, until round {p.UntilRound}).",
            AllianceProposed p => $"You offered {NameOf(p.ToPlayerId)} an alliance.",
            AllianceAccepted a => $"Allied with {Them(a.Team)}: shared sight, no friendly fire.",
            AllianceDeclined d when d.Lapsed => $"The alliance offer between {Who(d.FromPlayerId)} and {Who(d.ToPlayerId)} lapsed.",
            AllianceDeclined d => d.ToPlayerId == PlayerId ? $"You declined {NameOf(d.FromPlayerId)}'s alliance." : $"{NameOf(d.ToPlayerId)} declined your alliance.",
            AllianceBreakWarning w when w.PlayerId == PlayerId => $"You leave the alliance when round {w.EffectiveRound} starts.",
            AllianceBreakWarning w => $"{NameOf(w.PlayerId)} leaves the alliance when round {w.EffectiveRound} starts!",
            AllianceBroken b when b.PlayerId == PlayerId => $"You have left the alliance with {Them(b.FormerAllies)}.",
            AllianceBroken b => $"{NameOf(b.PlayerId)} has left the alliance: no more shared sight.",
            _ => message.GetType().Name,
        };
    }

    private void Complete(int requestId, ScavengeResult result)
    {
        if (_pending.Remove(requestId, out var tcs)) tcs.TrySetResult(result);
    }

    private void OnClosed()
    {
        // The server cancels every trade of a dropped player; the client does the same on its side.
        foreach (var id in Trades.Keys.ToList())
        {
            Trades.Remove(id);
            TradeNews?.Invoke(new TradeCancelled(id, "Connection lost."));
        }
        if (Phase != ClientPhase.Ended) Phase = ClientPhase.Disconnected;
        foreach (var tcs in _pending.Values) tcs.TrySetResult(ScavengeResult.Fail(ScavengeError.Finished, "Connection lost."));
        _pending.Clear();
        Changed?.Invoke();
    }
}
