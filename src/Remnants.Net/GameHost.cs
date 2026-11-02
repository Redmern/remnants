using System.Security.Cryptography;
using Remnants.Core.Scavenge;
using Remnants.Core.Trade;

namespace Remnants.Net;

/// <summary>Server settings. <see cref="TurnSeconds"/> 0 turns the AFK timer off.</summary>
public sealed record HostOptions
{
    public string ZoneId { get; init; } = "suburb_ruins";
    public ulong Seed { get; init; } = 7;
    public int MinPlayers { get; init; } = 2;
    public int MaxPlayers { get; init; } = 4;
    public int MaxPartySize { get; init; } = 6;
    public double TurnSeconds { get; init; } = 60;
    public double ReconnectSeconds { get; init; } = 120;

    /// <summary>Co-op: every seat starts on one team (spawned together, shared sight). Versus: teams only by choice.</summary>
    public MissionMode Mode { get; init; } = MissionMode.Versus;

    /// <summary>Whether a late zone takes its retune (<see cref="LateZoneTuning"/>); the game's default, re-tuned expeditions.</summary>
    public LateZoneTuning LateZones { get; init; } = LateZoneTuning.Expeditions;

    /// <summary>
    /// Hour of day the mission is played at. The server decides: this when set, else an hour from the seed. Every player
    /// gets the same light (it is in each view) and the fog follows it on the server.
    /// </summary>
    public int? Hour { get; init; }

    public int MissionHour => Hour ?? (int)(Seed % 24);

    /// <summary>
    /// Weather the mission is played in (a <c>scavenge.json environment.weather</c> id, or <c>clear</c>). The server
    /// decides: this when set (an in-game host passes its colony's weather over the zone), else one rolled from the seed
    /// (<see cref="FieldEnvironment.RollWeather"/>). Like the hour it is in every view, so every player plays the same sky.
    /// </summary>
    public string? Weather { get; init; }

    /// <summary>Intensity 0..1 of <see cref="Weather"/> (for the look and sound only).</summary>
    public double WeatherIntensity { get; init; } = 0.7;

    /// <summary>Changes during the run (from the host's colony), by round.</summary>
    public List<WeatherShift> WeatherShifts { get; init; } = new();

    /// <summary>The mission's environment: the hour, and the weather set or rolled from the seed.</summary>
    public ScavengeEnvironment MissionEnvironment(ScavengeData data)
    {
        var env = ScavengeEnvironment.AtHour(MissionHour);
        var (weather, intensity) = Weather is { } w ? (w, WeatherIntensity) : FieldEnvironment.RollWeather(data, Seed);
        env.Weather = weather;
        env.Intensity = intensity;
        env.Shifts = WeatherShifts.ToList();
        return env;
    }

    /// <summary>Player trading rules (data/trade.json).</summary>
    public TradeRules Trade { get; init; } = new();

    /// <summary>
    /// Resource ids players may trade in the lobby (the colony's resources.json). Null: the ids item salvage names, plus
    /// Research Data.
    /// </summary>
    public IReadOnlyCollection<string>? Resources { get; init; }

    /// <summary>
    /// Co-op story (STORY.md "Co-op", v1): the host's story mission — its script, handmade map or room, and the
    /// schematics a guest copies. The first seat is the host (its campaign is the one that advances); a guest's party
    /// spawns at the map's second spawn point and sees the objectives and chatter as an ally.
    /// </summary>
    public MissionSetup? Mission { get; init; }
}

public enum HostPhase { Lobby, Playing, Ended }

/// <summary>
/// The authoritative multiplayer host for one shared scavenge instance. Transport-agnostic (<see cref="IHostSink"/>)
/// and single-threaded: the transport serializes calls, and <see cref="Tick"/> is called periodically for timers.
/// Wall-clock time comes only from <see cref="TimeProvider"/> and only decides when a turn times out or a seat is
/// given up; the <see cref="ScavengeSim"/> itself stays deterministic (a timeout is just an <see cref="EndTurn"/>).
/// <para>
/// Flow: clients <see cref="Join"/> with a party and get a seat; once at least <see cref="HostOptions.MinPlayers"/>
/// are seated and all are <see cref="Ready"/>, the zone is generated with one party per seat. Each accepted
/// <see cref="Command"/> is followed by a fog-filtered <see cref="Snapshot"/> to every seat and a
/// <see cref="CommandAccepted"/> to the sender. A dropped seat's units wait out their turns until it rejoins with
/// its token; after <see cref="HostOptions.ReconnectSeconds"/> the seat is gone for good (its party keeps waiting).
/// A seat whose party is done gets its <see cref="MissionEnded"/>; the host ends when every seat is done or gone.
/// </para>
/// </summary>
public sealed class GameHost : IHostSink, TradeDesk.IVenue
{
    private readonly ScavengeData _data;
    private readonly TimeProvider _clock;
    private readonly List<Seat> _seats = new();
    private readonly Dictionary<int, Seat> _byConnection = new();
    private int _nextPlayerId = 1;
    private ScavengeEvent? _lastPublished;
    private (int Round, int Index, int? Unit) _turnKey;
    private DateTimeOffset? _deadline;

    public GameHost(ScavengeData data, HostOptions? options = null, TimeProvider? clock = null)
    {
        _data = data;
        Options = options ?? new HostOptions();
        _clock = clock ?? TimeProvider.System;
        if (!data.Zones.ContainsKey(Options.ZoneId)) throw new ArgumentException($"Unknown zone '{Options.ZoneId}'.");
        var resources = (Options.Resources ?? data.Items.Values.Select(i => i.Salvage?.Resource).OfType<string>().Append("research_data").ToList())
            .ToHashSet(StringComparer.Ordinal);
        Trades = new TradeDesk(this, Options.Trade, resources.Contains, data, _clock);
    }

    /// <summary>Live trades between the seated players (lobby: colony goods; mission: allies' packs).</summary>
    public TradeDesk Trades { get; }

    public HostOptions Options { get; }
    public HostPhase Phase { get; private set; } = HostPhase.Lobby;

    /// <summary>The instance, once the mission started. Tests and the console may read it; only the host changes it.</summary>
    public ScavengeSim? Sim { get; private set; }

    /// <summary>Diagnostics for the console.</summary>
    public Action<string>? Log
    {
        get => _log;
        set
        {
            _log = value;
            Trades.Log = value;
        }
    }

    private Action<string>? _log;

    /// <summary>Raised once when the mission is over for everyone.</summary>
    public event Action? Ended;

    public IReadOnlyList<int> PlayerIds => _seats.Select(s => s.PlayerId).ToList();

    private sealed class Seat
    {
        public int PlayerId;
        public string Name = "";
        public string Token = "";
        public List<PartyMember> Party = new();
        public FieldVehicle? Vehicle;
        public List<string>? Deck;
        public bool Ready;
        public IConnection? Connection;
        public DateTimeOffset? LeftAt;
        public bool Gone;
        public bool ResultSent;
        public SideFog Fog = null!;
        public int? Team;

        /// <summary>The team the seat starts on: everyone on 1 in co-op, the lobby pick in versus.</summary>
        public int? StartTeam(MissionMode mode) => mode == MissionMode.Coop ? 1 : Team;
    }

    // ---- IHostSink ----

    /// <summary>A connection gets a seat only once it sends <see cref="Join"/>.</summary>
    public void Connected(IConnection connection) { }

    public void Received(IConnection connection, Message message)
    {
        _byConnection.TryGetValue(connection.Id, out var seat);
        if (seat is not null && Trades.Handle(seat.PlayerId, message)) return;
        switch (message)
        {
            case Join join when seat is null:
                HandleJoin(connection, join);
                break;
            case Ready ready when seat is not null && Phase == HostPhase.Lobby:
                seat.Ready = ready.IsReady;
                BroadcastLobby();
                TryStart();
                break;
            case SetTeam team when seat is not null && Phase == HostPhase.Lobby:
                if (Options.Mode == MissionMode.Versus) seat.Team = team.Team is > 0 and <= 8 ? team.Team : null;
                seat.Ready = false;
                BroadcastLobby();
                break;
            case Command command when seat is not null:
                HandleCommand(seat, command);
                break;
            case Leave when seat is not null:
                Drop(connection, seat, forGood: true);
                connection.Close();
                break;
            default:
                Log?.Invoke($"ignored {message.GetType().Name} from connection {connection.Id}");
                break;
        }
    }

    public void Disconnected(IConnection connection)
    {
        if (_byConnection.TryGetValue(connection.Id, out var seat)) Drop(connection, seat, forGood: false);
    }

    /// <summary>Timers: a player who sits on their turn too long waits it out; a seat dropped too long is given up.</summary>
    public void Tick()
    {
        Trades.Tick();
        if (Phase != HostPhase.Playing || Sim is null) return;
        var now = _clock.GetUtcNow();
        foreach (var seat in _seats.Where(s => s.Connection is null && !s.Gone && s.LeftAt is { } t && (now - t).TotalSeconds >= Options.ReconnectSeconds))
        {
            seat.Gone = true;
            Log?.Invoke($"{seat.Name} did not come back; seat given up");
            Broadcast(new PlayerLeft(seat.PlayerId, seat.Name, 0));
            Abandon(seat);
        }
        if (_deadline is { } deadline && now >= deadline && Sim.State.CurrentUnit is { PlayerControlled: true } unit)
        {
            _deadline = null;
            Broadcast(new Notice($"{unit.Name}'s turn timed out."));
            Act(new EndTurn(unit.PlayerId!.Value, unit.Id));
        }
        AfterChange();
    }

    // ---- lobby ----

    private void HandleJoin(IConnection connection, Join join)
    {
        if (join.Version != Protocol.Version)
        {
            Refuse(connection, $"Protocol version {join.Version} does not match the server's {Protocol.Version}.");
            return;
        }
        if (join.ReconnectToken is { Length: > 0 } token)
        {
            var back = _seats.Find(s => s.Token == token);
            if (back is null || back.Gone || back.Connection is not null)
            {
                Refuse(connection, "That seat cannot be retaken.");
                return;
            }
            Reattach(connection, back);
            return;
        }
        if (Phase != HostPhase.Lobby)
        {
            Refuse(connection, "A mission is already under way.");
            return;
        }
        if (_seats.Count >= Options.MaxPlayers)
        {
            Refuse(connection, $"The instance is full ({Options.MaxPlayers} players).");
            return;
        }
        if (ValidateParty(join) is { } problem)
        {
            Refuse(connection, problem);
            return;
        }

        var name = join.Name.Trim();
        var seat = new Seat
        {
            PlayerId = _nextPlayerId++,
            Name = name.Length > 24 ? name[..24] : name,
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            Party = join.Party,
            Vehicle = join.Vehicle,
            Deck = join.Deck is { Count: > 0 } deck ? new(deck) : null,
            Connection = connection,
        };
        seat.Fog = new SideFog(seat.PlayerId, Unit.PlayerSide(seat.PlayerId));
        _seats.Add(seat);
        _byConnection[connection.Id] = seat;
        Log?.Invoke($"{seat.Name} joined as player {seat.PlayerId} with {seat.Party.Count} survivors");
        connection.Send(new Welcome(Protocol.Version, seat.PlayerId, seat.Token));
        BroadcastLobby();
    }

    /// <summary>
    /// The server trusts the client's colony for who is in the party (see docs/MULTIPLAYER.md, "Trust gap"); it only
    /// checks that the party fits the instance, that nobody in it is a child, and that every item exists and sits in its slot.
    /// </summary>
    private string? ValidateParty(Join join)
    {
        if (string.IsNullOrWhiteSpace(join.Name)) return "Pick a player name.";
        if (join.Party is not { Count: > 0 }) return "Bring at least one survivor.";
        if (join.Party.Count > Options.MaxPartySize) return $"Parties are limited to {Options.MaxPartySize} survivors here.";
        foreach (var m in join.Party)
        {
            if (m.Survivor is null) return "A party member has no survivor record.";
            if (m.Survivor.IsChild) return $"{m.Survivor.Name} is a child; children stay home.";
            foreach (var (slot, id) in m.Equipment ?? new())
                if (!_data.Items.TryGetValue(id, out var item) || item.Slot != slot) return $"{m.Survivor.Name}: '{id}' does not go in the {slot} slot.";
            foreach (var id in m.Pack ?? [])
                if (!_data.Items.ContainsKey(id)) return $"{m.Survivor.Name}: unknown item '{id}'.";
            if (m.SlotCards is { } slots && slots.Count > _data.Cards.Rules.MaxSlotCards) return $"{m.Survivor.Name}: too many slot cards.";
            foreach (var id in m.SlotCards ?? [])
                if (_data.Cards.Find(id) is null) return $"{m.Survivor.Name}: unknown card '{id}'.";
        }
        // The colony's cards are trusted like its party (the trust gap), but a deck must keep the deck rules.
        if (join.Deck is { } deck && _data.Cards.CheckDeck(deck) is { } bad) return bad;
        if (join.Vehicle is { } v)
        {
            var limits = _data.Rules.Vehicle;
            if (v.Seats is { } seats && join.Party.Count > seats) return $"The {v.Name} seats {seats}, not {join.Party.Count}.";
            if (v.MaxHp is <= 0 || v.MaxHp > limits.MaxHp || v.Hp > v.MaxHp || v.CargoKg is < 0 || v.CargoKg > limits.MaxCargoKg || v.Armor < 0
                || v.DamageMultiplier is < 0 or > 1 || v.TurretDamage < 0 || v.TurretDamage > limits.MaxTurretDamage || v.TurretRange is < 0 or > 12)
                return $"The {v.Name} does not add up (HP, cargo or armor out of range).";
        }
        return null;
    }

    private static void Refuse(IConnection connection, string reason)
    {
        connection.Send(new JoinRefused(reason));
        connection.Close();
    }

    private void BroadcastLobby() =>
        Broadcast(new LobbyState(Options.ZoneId, Options.MinPlayers, Options.MaxPlayers,
            _seats.Select(s => new LobbyPlayer(s.PlayerId, s.Name, s.Party.Count, s.Ready, s.Connection is not null, s.StartTeam(Options.Mode))).ToList(),
            Options.Mode, Options.LateZones));

    private void TryStart()
    {
        if (Phase != HostPhase.Lobby || _seats.Count < Options.MinPlayers || !_seats.All(s => s.Ready && s.Connection is not null)) return;
        try
        {
            Sim = NewScavenge.Create(_data, Options.ZoneId, Options.Seed,
                _seats.Select(s => new PartySetup(s.PlayerId, s.Party, s.StartTeam(Options.Mode), s.Name)
                {
                    Vehicle = s.Vehicle,
                    Deck = s.Deck is { } cards ? new SquadLoadout(cards, _data.Cards.Rules.HandSize, _data.Cards.Rules.Draw, _data.Cards.Rules.Budget) : null,
                }).ToList(),
                new ZoneConditions(Environment: Options.MissionEnvironment(_data)) { LateZones = Options.LateZones }, Options.Mission);
        }
        catch (ArgumentException e)
        {
            Log?.Invoke($"could not start: {e.Message}");
            foreach (var s in _seats) s.Ready = false;
            Broadcast(new Notice($"Could not start the mission: {e.Message}"));
            BroadcastLobby();
            return;
        }
        Phase = HostPhase.Playing;
        Trades.VenueChanged();
        Log?.Invoke($"mission started in {Options.ZoneId} (seed {Options.Seed}, {Sim.State.Environment.Hour:00}:00 {Sim.State.Environment.Phase}, {FieldEnvironment.Name(Sim.State.Environment.Weather)}) with {_seats.Count} players");
        foreach (var seat in _seats) seat.Connection?.Send(Started());
        _lastPublished = null;
        Publish();
        AfterChange(forceTurn: true);
    }

    // ---- mission ----

    private void HandleCommand(Seat seat, Command command)
    {
        if (Phase != HostPhase.Playing || Sim is null)
        {
            seat.Connection?.Send(new CommandRejected(command.RequestId, ScavengeError.Finished, "No mission is running."));
            return;
        }
        if (command.Intent is null || command.Intent.PlayerId != seat.PlayerId)
        {
            seat.Connection?.Send(new CommandRejected(command.RequestId, ScavengeError.NotYourUnit, $"You are player {seat.PlayerId}."));
            return;
        }
        var result = Act(command.Intent);
        if (!result.Ok)
        {
            seat.Connection?.Send(new CommandRejected(command.RequestId, result.Error, result.Message));
            return;
        }
        seat.Connection?.Send(new CommandAccepted(command.RequestId, result.Message));
        AfterChange();
    }

    private MissionStarted Started() =>
        new(Sim!.State.ZoneId, Sim.State.Map, _seats.Select(s => new PlayerInfo(s.PlayerId, s.Name, s.StartTeam(Options.Mode))).ToList(),
            Options.TurnSeconds, Options.ReconnectSeconds, Options.Mode);

    /// <summary>
    /// Runs one command on the sim and, if it changed anything, sends every seat its new view, then tells the players
    /// involved about alliance changes it caused (an offer, a merge, a decline or lapse, a break announced or done).
    /// </summary>
    private ScavengeResult Act(IScavengeCommand command)
    {
        var state = Sim!.State;
        var offers = state.Proposals.ToList();
        var breaks = state.Breaks.ToList();
        var sides = state.Parties.ToDictionary(p => p.PlayerId, p => p.Side);
        var result = Sim.Execute(command);
        if (!result.Ok) return result;
        Publish();

        List<int> Team(int playerId) => state.PlayersOn(state.FindParty(playerId)!.Side).ToList();
        foreach (var offer in state.Proposals.Except(offers))
            SendTo([offer.FromPlayerId, offer.ToPlayerId],
                new AllianceProposed(offer.FromPlayerId, offer.ToPlayerId, offer.Round + _data.Rules.Coop.ProposalRounds - 1));
        foreach (var offer in offers.Except(state.Proposals))
        {
            if (state.Allied(offer.FromPlayerId, offer.ToPlayerId)) SendTo(Team(offer.ToPlayerId), new AllianceAccepted(Team(offer.ToPlayerId)));
            else SendTo([offer.FromPlayerId, offer.ToPlayerId],
                new AllianceDeclined(offer.FromPlayerId, offer.ToPlayerId, Lapsed: command is not RespondAlliance { Accept: false }));
        }
        foreach (var b in state.Breaks.Except(breaks)) SendTo(Team(b.PlayerId), new AllianceBreakWarning(b.PlayerId, b.EffectiveRound));
        foreach (var b in breaks.Except(state.Breaks).Where(b => b.EffectiveRound <= state.Round))
        {
            var former = sides.Where(kv => kv.Value == sides[b.PlayerId] && kv.Key != b.PlayerId).Select(kv => kv.Key).ToList();
            SendTo([b.PlayerId, .. former], new AllianceBroken(b.PlayerId, former));
        }
        return result;
    }

    /// <summary>A seat gone for good: allies stop waiting for it (Core's <see cref="ScavengeSim.Abandon"/>), everyone sees the effect.</summary>
    private void Abandon(Seat seat)
    {
        if (Sim is null || Phase != HostPhase.Playing) return;
        Sim.Abandon(seat.PlayerId);
        Publish();
    }

    private void SendTo(IEnumerable<int> playerIds, Message message)
    {
        foreach (var id in playerIds.Distinct()) SeatOf(id)?.Connection?.Send(message);
    }

    /// <summary>Every seat (connected or not, so its cache memory stays right) gets its fog updated; connected ones get a snapshot.</summary>
    private void Publish()
    {
        var state = Sim!.State;
        var fresh = TakeNewEvents(state);
        foreach (var seat in _seats)
        {
            var before = seat.Fog.Update(Sim);
            var events = seat.Fog.Filter(state, fresh, before);
            seat.Connection?.Send(new Snapshot(seat.Fog.View(Sim), seat.Fog.Visible.Order().ToList(), events));
        }
    }

    /// <summary>Events logged since the last publish (the log is capped, so the last one is found by identity).</summary>
    private List<ScavengeEvent> TakeNewEvents(ScavengeState state)
    {
        var events = state.Events;
        var start = 0;
        if (_lastPublished is not null)
            for (var i = events.Count - 1; i >= 0; i--)
                if (ReferenceEquals(events[i], _lastPublished)) { start = i + 1; break; }
        var fresh = events.Skip(start).ToList();
        if (events.Count > 0) _lastPublished = events[^1];
        return fresh;
    }

    /// <summary>
    /// After anything changed: seats without a connection wait out their units' turns, the turn timer restarts on a
    /// new turn, finished parties get their results, and the host ends once nobody is left playing.
    /// </summary>
    private void AfterChange(bool forceTurn = false)
    {
        if (Phase != HostPhase.Playing || Sim is null) return;
        var state = Sim.State;
        // Dropped seats wait out their turns, but only while someone connected is still playing: with nobody
        // there to play against, the instance holds still instead of running the AI against the absent.
        // (A connected player whose people are all downed, waiting on an ally, is not playing.)
        bool SomeonePlaying() => _seats.Any(s => s.Connection is not null && state.FindParty(s.PlayerId) is { Done: false }
                                                 && state.Units.Any(u => u.PlayerId == s.PlayerId && u.PlayerControlled && u.IsActive));
        for (var guard = 0; guard < 10000 && !state.Finished && state.CurrentUnit is { PlayerControlled: true } unit
                            && SeatOf(unit.PlayerId) is { Connection: null } && SomeonePlaying(); guard++)
            Act(new EndTurn(unit.PlayerId!.Value, unit.Id));

        var key = (state.Round, state.TurnIndex, state.CurrentUnitId);
        if (forceTurn || key != _turnKey)
        {
            _turnKey = key;
            var current = state.CurrentUnit;
            var player = current is { PlayerControlled: true } ? current.PlayerId : null;
            _deadline = player is not null && Options.TurnSeconds > 0 ? _clock.GetUtcNow().AddSeconds(Options.TurnSeconds) : null;
            foreach (var seat in _seats) SendTurn(seat);
        }

        foreach (var seat in _seats.Where(s => !s.ResultSent && state.FindParty(s.PlayerId) is { Done: true }))
            SendResult(seat, "Your party is out of the zone.");

        if (state.Finished || _seats.All(s => s.Gone || state.FindParty(s.PlayerId) is { Done: true }))
        {
            Phase = HostPhase.Ended;
            _deadline = null;
            Trades.VenueChanged();
            foreach (var seat in _seats.Where(s => !s.ResultSent)) SendResult(seat, "The mission is over.");
            Log?.Invoke("mission over");
            Ended?.Invoke();
        }
    }

    private void SendTurn(Seat seat)
    {
        if (seat.Connection is null || Sim is null) return;
        var state = Sim.State;
        var current = state.CurrentUnit;
        var shown = current is not null && seat.Fog.Knows(state, current) ? current.Id : (int?)null;
        var player = current is { PlayerControlled: true } ? current.PlayerId : null;
        var left = _deadline is { } d ? Math.Max(0, (d - _clock.GetUtcNow()).TotalSeconds) : 0;
        seat.Connection.Send(new TurnChanged(state.Round, shown, player, left));
    }

    private void SendResult(Seat seat, string reason)
    {
        if (seat.Connection is null) return;
        seat.ResultSent = true;
        seat.Connection.Send(new MissionEnded(Sim!.ResultFor(seat.PlayerId), reason));
    }

    private Seat? SeatOf(int? playerId) => _seats.Find(s => s.PlayerId == playerId);

    private void Drop(IConnection connection, Seat seat, bool forGood)
    {
        _byConnection.Remove(connection.Id);
        if (seat.Connection?.Id != connection.Id) return;
        seat.Connection = null;
        Trades.Dropped(seat.PlayerId);
        if (Phase == HostPhase.Lobby)
        {
            _seats.Remove(seat);
            Log?.Invoke($"{seat.Name} left the lobby");
            BroadcastLobby();
            TryStart();
            return;
        }
        if (Phase == HostPhase.Ended) return;
        seat.LeftAt = _clock.GetUtcNow();
        seat.Gone = forGood || seat.ResultSent;
        Log?.Invoke($"{seat.Name} {(seat.Gone ? "left" : "dropped")}");
        Broadcast(new PlayerLeft(seat.PlayerId, seat.Name, seat.Gone ? 0 : Options.ReconnectSeconds));
        if (seat.Gone) Abandon(seat);
        AfterChange();
    }

    private void Reattach(IConnection connection, Seat seat)
    {
        seat.Connection = connection;
        seat.LeftAt = null;
        _byConnection[connection.Id] = seat;
        Log?.Invoke($"{seat.Name} is back");
        connection.Send(new Welcome(Protocol.Version, seat.PlayerId, seat.Token));
        if (Phase == HostPhase.Lobby || Sim is null)
        {
            BroadcastLobby();
            return;
        }
        connection.Send(Started());
        connection.Send(new Snapshot(seat.Fog.View(Sim), seat.Fog.Visible.Order().ToList(), []));
        Broadcast(new Notice($"{seat.Name} is back."));
        SendTurn(seat);
        if (Phase == HostPhase.Ended || Sim.State.FindParty(seat.PlayerId) is { Done: true }) SendResult(seat, "Your party is out of the zone.");
    }

    // ---- TradeDesk.IVenue ----

    TradeVenue? TradeDesk.IVenue.Venue => Phase switch { HostPhase.Lobby => TradeVenue.Lobby, HostPhase.Playing => TradeVenue.Field, _ => null };

    bool TradeDesk.IVenue.IsConnected(int playerId) => SeatOf(playerId) is { Connection: not null, Gone: false };

    string TradeDesk.IVenue.NameOf(int playerId) => SeatOf(playerId)?.Name ?? $"Player {playerId}";

    void TradeDesk.IVenue.Send(int playerId, Message message) => SeatOf(playerId)?.Connection?.Send(message);

    string? TradeDesk.IVenue.CheckPair(int a, int b)
    {
        if (SeatOf(a) is not { Connection: not null } || SeatOf(b) is not { Connection: not null }) return "The other trader is not connected.";
        return Phase switch
        {
            HostPhase.Lobby => null,
            HostPhase.Playing when Sim is not null => FieldTrade.CheckPair(Sim, a, b),
            _ => "The mission is over.",
        };
    }

    /// <summary>Lobby offers are the client's colony goods (trusted, shape-checked by the desk); field offers must be in the packs.</summary>
    string? TradeDesk.IVenue.CheckOffer(int playerId, IReadOnlyList<TradeGood> goods) =>
        Phase == HostPhase.Playing && Sim is not null ? FieldTrade.CheckOffer(Sim, playerId, goods) : null;

    string? TradeDesk.IVenue.CommitField(int a, IReadOnlyList<TradeGood> aGives, int b, IReadOnlyList<TradeGood> bGives)
    {
        if (Sim is null) return "No mission is running.";
        var result = FieldTrade.Swap(Sim, a, aGives, b, bGives);
        if (!result.Ok) return result.Message;
        Publish();
        return null;
    }

    private void Broadcast(Message message)
    {
        foreach (var seat in _seats) seat.Connection?.Send(message);
    }
}
