using Remnants.Core.Trade;

namespace Remnants.Net;

/// <summary>
/// Live trades between two connected players (the trade window), held by the server. Single-threaded like
/// <see cref="GameHost"/>, which owns it and feeds it messages, drops and ticks.
/// <para>
/// Flow: <see cref="TradeRequest"/> → the other side gets a <see cref="TradeInvite"/> and accepts → both put goods on the
/// table (<see cref="TradeOffer"/>; any change unlocks and unconfirms both) → both <see cref="TradeLock"/> → both
/// <see cref="TradeConfirm"/> → the desk commits once, atomically: both get <see cref="TradeCommit"/>, or (the goods were
/// gone, a trader left, it idled out) both get <see cref="TradeCancelled"/> and refund. Every message goes to the two
/// traders only.
/// </para>
/// <para>
/// Lobby trades are client-trusted (the colonies live on the clients): the client escrows its offer before confirming,
/// the desk checks the offer's shape and caps, logs suspicious amounts, and tells both what to settle. Field trades are
/// server-authoritative: the goods are items in the parties' packs on the server's sim, swapped by the
/// <see cref="ICommitter"/> (Core's <c>FieldTrade</c>).
/// </para>
/// </summary>
public sealed class TradeDesk
{
    /// <summary>What the desk needs from its host.</summary>
    public interface IVenue
    {
        /// <summary>Where trades happen right now (null = nowhere: the mission is over).</summary>
        TradeVenue? Venue { get; }

        bool IsConnected(int playerId);
        string NameOf(int playerId);
        void Send(int playerId, Message message);

        /// <summary>Why these two cannot trade now (field: not allied, a party out), or null.</summary>
        string? CheckPair(int a, int b);

        /// <summary>Why <paramref name="playerId"/> cannot offer these goods (beyond shape and caps), or null.</summary>
        string? CheckOffer(int playerId, IReadOnlyList<TradeGood> goods);

        /// <summary>Field trades: do the swap now, all or nothing. Returns why it failed, or null.</summary>
        string? CommitField(int a, IReadOnlyList<TradeGood> aGives, int b, IReadOnlyList<TradeGood> bGives);
    }

    public const int MaxOpenPerPlayer = 3;

    private readonly IVenue _venue;
    private readonly TradeRules _rules;
    private readonly Func<string, bool> _isResource;
    private readonly Core.Scavenge.ScavengeData _items;
    private readonly TimeProvider _clock;
    private readonly Dictionary<int, Session> _sessions = new();
    private int _nextId = 1;

    public TradeDesk(IVenue venue, TradeRules rules, Func<string, bool> isResource, Core.Scavenge.ScavengeData items, TimeProvider clock)
    {
        _venue = venue;
        _rules = rules;
        _isResource = isResource;
        _items = items;
        _clock = clock;
    }

    public Action<string>? Log { get; set; }

    /// <summary>Trades committed so far (for the console and tests).</summary>
    public int Committed { get; private set; }

    public int Open => _sessions.Count;

    private sealed class Session
    {
        public int Id;
        public TradeVenue Venue;
        public int[] Players = new int[2];
        public List<TradeGood>[] Goods = [[], []];
        public bool[] Locked = new bool[2];
        public bool[] Confirmed = new bool[2];
        public bool Accepted;
        public DateTimeOffset Touched;

        public int Side(int playerId) => Players[0] == playerId ? 0 : Players[1] == playerId ? 1 : -1;

        public void Unlock()
        {
            Locked[0] = Locked[1] = false;
            Confirmed[0] = Confirmed[1] = false;
        }
    }

    /// <summary>A trade message from <paramref name="playerId"/>. Returns false when it is not a trade message.</summary>
    public bool Handle(int playerId, Message message)
    {
        switch (message)
        {
            case TradeRequest r: Request(playerId, r.ToPlayerId); return true;
            case TradeRespond r: With(playerId, r.TradeId, s => Respond(s, playerId, r.Accept)); return true;
            case TradeOffer o: With(playerId, o.TradeId, s => Offer(s, playerId, o.Goods)); return true;
            case TradeLock l: With(playerId, l.TradeId, s => Lock(s, playerId, l.Locked)); return true;
            case TradeConfirm c: With(playerId, c.TradeId, s => Confirm(s, playerId)); return true;
            case TradeCancel c: With(playerId, c.TradeId, s => Cancel(s, $"{_venue.NameOf(playerId)} cancelled the trade.")); return true;
            default: return false;
        }
    }

    /// <summary>A player dropped or left: their trades are off (both sides refund).</summary>
    public void Dropped(int playerId)
    {
        foreach (var s in _sessions.Values.Where(s => s.Side(playerId) >= 0).ToList())
            Cancel(s, $"{_venue.NameOf(playerId)} disconnected.");
    }

    /// <summary>The venue changed (the mission started or ended): every trade from the old venue is off.</summary>
    public void VenueChanged()
    {
        foreach (var s in _sessions.Values.Where(s => s.Venue != _venue.Venue).ToList())
            Cancel(s, s.Venue == TradeVenue.Lobby ? "The mission started." : "The mission is over.");
    }

    /// <summary>Timers and changed circumstances: idle trades and field trades whose pair broke up are cancelled.</summary>
    public void Tick()
    {
        var now = _clock.GetUtcNow();
        foreach (var s in _sessions.Values.ToList())
        {
            if ((now - s.Touched).TotalSeconds >= _rules.Live.IdleSeconds) Cancel(s, "The trade timed out.");
            else if (_venue.CheckPair(s.Players[0], s.Players[1]) is { } why) Cancel(s, why);
        }
    }

    private void With(int playerId, int tradeId, Action<Session> action)
    {
        if (_sessions.TryGetValue(tradeId, out var s) && s.Side(playerId) >= 0) action(s);
        else _venue.Send(playerId, new Notice("That trade is no longer open."));
    }

    private void Request(int from, int to)
    {
        if (_venue.Venue is not { } venue) { Refuse(from, "There is nothing to trade with right now."); return; }
        if (from == to || !_venue.IsConnected(to)) { Refuse(from, "That player is not here."); return; }
        if (_venue.CheckPair(from, to) is { } why) { Refuse(from, why); return; }
        if (_sessions.Values.Any(s => s.Side(from) >= 0 && s.Side(to) >= 0)) { Refuse(from, $"You already have a trade open with {_venue.NameOf(to)}."); return; }
        if (_sessions.Values.Count(s => s.Side(from) >= 0) >= MaxOpenPerPlayer || _sessions.Values.Count(s => s.Side(to) >= 0) >= MaxOpenPerPlayer)
        {
            Refuse(from, "Too many trades open.");
            return;
        }
        var session = new Session { Id = _nextId++, Venue = venue, Players = [from, to], Touched = _clock.GetUtcNow() };
        _sessions[session.Id] = session;
        _venue.Send(to, new TradeInvite(session.Id, from, _venue.NameOf(from), venue));
        Publish(session);
    }

    private void Respond(Session s, int playerId, bool accept)
    {
        if (s.Side(playerId) != 1 || s.Accepted) return;
        if (!accept)
        {
            Cancel(s, $"{_venue.NameOf(playerId)} declined the trade.");
            return;
        }
        s.Accepted = true;
        Touch(s);
    }

    private void Offer(Session s, int playerId, List<TradeGood>? goods)
    {
        if (!s.Accepted) return;
        var lines = TradeGoods.Normalize(goods ?? []);
        if (CheckOffer(s, playerId, lines) is { } problem)
        {
            _venue.Send(playerId, new Notice($"Offer refused: {problem}"));
            Publish(s);
            return;
        }
        var side = s.Side(playerId);
        if (s.Goods[side].SequenceEqual(lines)) return;
        s.Goods[side] = lines;
        s.Unlock();
        Touch(s);
    }

    private void Lock(Session s, int playerId, bool locked)
    {
        if (!s.Accepted) return;
        var side = s.Side(playerId);
        if (!locked)
        {
            if (!s.Locked[side]) return;
            s.Unlock();
        }
        else
        {
            if (s.Locked[side]) return;
            if (CheckOffer(s, playerId, s.Goods[side]) is { } problem)
            {
                _venue.Send(playerId, new Notice($"Cannot lock: {problem}"));
                return;
            }
            s.Locked[side] = true;
        }
        Touch(s);
    }

    private void Confirm(Session s, int playerId)
    {
        var side = s.Side(playerId);
        // Confirming twice is harmless: the trade commits exactly once, when the second side confirms.
        if (s.Confirmed[side]) return;
        if (!s.Locked[0] || !s.Locked[1])
        {
            _venue.Send(playerId, new Notice("Both sides must lock their offers first."));
            return;
        }
        s.Confirmed[side] = true;
        if (!s.Confirmed[1 - side])
        {
            Touch(s);
            return;
        }
        Commit(s);
    }

    private void Commit(Session s)
    {
        var (a, b) = (s.Players[0], s.Players[1]);
        if ((_venue.CheckPair(a, b) ?? CheckOffer(s, a, s.Goods[0]) ?? CheckOffer(s, b, s.Goods[1])) is { } problem)
        {
            Cancel(s, problem);
            return;
        }
        if (s.Venue == TradeVenue.Field && _venue.CommitField(a, s.Goods[0], b, s.Goods[1]) is { } failed)
        {
            Cancel(s, failed);
            return;
        }
        _sessions.Remove(s.Id);
        Committed++;
        var tx = $"live-{Guid.NewGuid():N}";
        var legs = Courier.Legs(_venue.NameOf(a), _venue.NameOf(b), _rules.Courier.MaxLegs);
        if ((TradeGoods.Suspicious(s.Goods[0], _rules) ?? TradeGoods.Suspicious(s.Goods[1], _rules)) is { } big)
            Log?.Invoke($"suspicious trade {tx} between {_venue.NameOf(a)} and {_venue.NameOf(b)}: {big}");
        Log?.Invoke($"trade {s.Id} ({s.Venue}) committed: {_venue.NameOf(a)} gives {Describe(s.Goods[0])}, {_venue.NameOf(b)} gives {Describe(s.Goods[1])}");
        _venue.Send(a, new TradeCommit(s.Id, tx, s.Venue, b, _venue.NameOf(b), s.Goods[0], s.Goods[1], legs));
        _venue.Send(b, new TradeCommit(s.Id, tx, s.Venue, a, _venue.NameOf(a), s.Goods[1], s.Goods[0], legs));
    }

    private void Cancel(Session s, string reason)
    {
        if (!_sessions.Remove(s.Id)) return;
        foreach (var p in s.Players) _venue.Send(p, new TradeCancelled(s.Id, reason));
    }

    private string? CheckOffer(Session s, int playerId, IReadOnlyList<TradeGood> goods)
    {
        if (TradeGoods.Validate(goods, _rules, _isResource, _items) is { } shape) return shape;
        if (s.Venue == TradeVenue.Field && goods.Any(g => g.Kind != GoodKind.Item)) return "In the field you trade items from your packs.";
        return _venue.CheckOffer(playerId, goods);
    }

    private void Touch(Session s)
    {
        s.Touched = _clock.GetUtcNow();
        Publish(s);
    }

    private void Publish(Session s)
    {
        var view = new TradeView(s.Id, s.Venue, s.Accepted,
            [.. Enumerable.Range(0, 2).Select(i => new TradeSideView(s.Players[i], _venue.NameOf(s.Players[i]), s.Goods[i], s.Locked[i], s.Confirmed[i]))],
            Math.Max(0, _rules.Live.IdleSeconds - (_clock.GetUtcNow() - s.Touched).TotalSeconds));
        foreach (var p in s.Players) _venue.Send(p, new TradeUpdate(view));
    }

    private void Refuse(int playerId, string reason) => _venue.Send(playerId, new TradeCancelled(0, reason));

    private static string Describe(List<TradeGood> goods) => goods.Count == 0 ? "nothing" : string.Join(", ", goods.Select(g => $"{g.Count}×{g.Id}"));
}
