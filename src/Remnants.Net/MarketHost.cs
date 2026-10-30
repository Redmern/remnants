using System.Security.Cryptography;
using System.Text;
using Remnants.Core.Scavenge;
using Remnants.Core.Trade;

namespace Remnants.Net;

/// <summary>
/// The Trading Post: an asynchronous barter market on a persistent server (<c>Remnants.Server --market FILE</c>). Players
/// post "give X for Y"; the server takes X into its escrow (the client has already taken it out of its colony), lists it
/// for everyone, and when someone accepts (their Y escrowed the same way) both sides get a <b>claim</b>. Claims, cancelled
/// and expired listings' refunds included, wait on the server until the player's client has them in its colony and acks
/// them. Every request carries a client transaction id: a retry after a lost answer gets the same answer and does nothing
/// twice; a claim applied twice by the client is ignored there (its claim id).
/// <para>
/// Identity: a display name plus a locally generated secret key; the server keeps only the key's SHA-256 (the player's
/// market id). No accounts. Trust: like the rest of multiplayer, the server trusts the client's colony for what it
/// escrows (see docs/MULTIPLAYER.md "Trading"); it checks shapes and caps, rate-limits, and logs suspicious amounts.
/// </para>
/// Single-threaded: the transport serializes calls (see <see cref="TcpServer"/>); <see cref="Tick"/> expires listings.
/// </summary>
public sealed class MarketHost : IHostSink
{
    private readonly MarketStore _store;
    private readonly TradeRules _rules;
    private readonly Func<string, bool> _isResource;
    private readonly ScavengeData _items;
    private readonly TimeProvider _clock;
    private readonly Dictionary<int, Client> _clients = new();

    public MarketHost(MarketStore store, TradeRules rules, Func<string, bool> isResource, ScavengeData items, TimeProvider? clock = null)
    {
        _store = store;
        _rules = rules;
        _isResource = isResource;
        _items = items;
        _clock = clock ?? TimeProvider.System;
    }

    public Action<string>? Log { get; set; }

    public MarketData Data => _store.Data;

    private sealed class Client
    {
        public required IConnection Connection;
        public string? PlayerId;
        public string Name = "";
        public readonly Queue<DateTimeOffset> Requests = new();
        public readonly Queue<DateTimeOffset> Posts = new();
    }

    public static string PlayerIdOf(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();

    public void Connected(IConnection connection) => _clients[connection.Id] = new Client { Connection = connection };

    public void Disconnected(IConnection connection) => _clients.Remove(connection.Id);

    public void Received(IConnection connection, Message message)
    {
        if (!_clients.TryGetValue(connection.Id, out var client)) return;
        if (message is MarketHello hello)
        {
            Hello(client, hello);
            return;
        }
        if (client.PlayerId is null)
        {
            Log?.Invoke($"ignored {message.GetType().Name} before hello from connection {connection.Id}");
            return;
        }
        if (!Allow(client.Requests, _rules.Market.RequestsPerMinute))
        {
            if (RequestId(message) is { } rid) client.Connection.Send(new MarketResult(rid, false, "Too many requests; slow down.", Code: "rate"));
            return;
        }
        Expire();
        switch (message)
        {
            case MarketPost p: Post(client, p); break;
            case MarketList l: List(client, l); break;
            case MarketAccept a: Accept(client, a); break;
            case MarketCancel c: Cancel(client, c); break;
            case MarketClaim: SendClaims(client); break;
            case MarketClaimAck ack: Ack(client, ack); break;
            default: Log?.Invoke($"ignored {message.GetType().Name} from {client.Name}"); break;
        }
    }

    /// <summary>Expired listings go back to their sellers as claims.</summary>
    public void Tick() => Expire();

    private void Hello(Client client, MarketHello hello)
    {
        if (hello.Version != Protocol.Version)
        {
            client.Connection.Send(new MarketResult(0, false, $"Protocol version {hello.Version} does not match the Trading Post's {Protocol.Version}.", Code: "version"));
            client.Connection.Close();
            return;
        }
        var name = (hello.Name ?? "").Trim();
        if (name.Length == 0 || hello.Key is not { Length: >= 32 and <= 128 } key || !key.All(Uri.IsHexDigit))
        {
            client.Connection.Send(new MarketResult(0, false, "Pick a name; the identity key must be 32-128 hex digits.", Code: "invalid"));
            client.Connection.Close();
            return;
        }
        client.PlayerId = PlayerIdOf(key);
        client.Name = name.Length > 24 ? name[..24] : name;
        if (_store.Data.Names.GetValueOrDefault(client.PlayerId) != client.Name)
        {
            _store.Data.Names[client.PlayerId] = client.Name;
            _store.Save();
        }
        Log?.Invoke($"{client.Name} ({client.PlayerId}) at the Trading Post");
        client.Connection.Send(new MarketWelcome(Protocol.Version, client.PlayerId, client.Name, _rules.Market.ListingHours, _rules.Market.MaxOpenListings));
        Expire();
        SendClaims(client);
    }

    private void Post(Client client, MarketPost p)
    {
        var data = _store.Data;
        if (Repeat(client, p.RequestId, p.TxId, isPost: true)) return;
        var give = TradeGoods.Normalize(p.Give ?? []);
        var want = TradeGoods.Normalize(p.Want ?? []);
        string? problem = give.Count == 0 || want.Count == 0 ? "A listing gives something and wants something."
            : TradeGoods.Validate(give, _rules, _isResource, _items) ?? TradeGoods.Validate(want, _rules, _isResource, _items);
        if (problem is null && string.IsNullOrWhiteSpace(p.TxId)) problem = "Missing transaction id.";
        if (problem is not null)
        {
            Log?.Invoke($"rejected post from {client.Name}: {problem}");
            client.Connection.Send(new MarketResult(p.RequestId, false, problem, Code: "invalid"));
            return;
        }
        if (data.Listings.Count(l => l.SellerId == client.PlayerId) >= _rules.Market.MaxOpenListings)
        {
            client.Connection.Send(new MarketResult(p.RequestId, false, $"At most {_rules.Market.MaxOpenListings} open listings each.", Code: "cap"));
            return;
        }
        if (!Allow(client.Posts, _rules.Market.PostsPerMinute))
        {
            client.Connection.Send(new MarketResult(p.RequestId, false, "Posting too fast; wait a minute.", Code: "rate"));
            return;
        }
        Suspicious(client, "post", give.Concat(want));
        var now = _clock.GetUtcNow();
        var hours = p.Hours <= 0 ? _rules.Market.ListingHours : Math.Min(p.Hours, _rules.Market.MaxListingHours);
        var listing = new StoredListing
        {
            Id = data.NextListingId++, SellerId = client.PlayerId!, SellerName = client.Name, Give = give, Want = want,
            Posted = now, Expires = now.AddHours(hours), TxId = p.TxId,
        };
        data.Listings.Add(listing);
        data.Transactions[p.TxId] = new StoredTx { PlayerId = client.PlayerId!, IsPost = true, ListingId = listing.Id, At = now };
        _store.Save();
        Log?.Invoke($"{client.Name} posted #{listing.Id}: {Describe(give)} for {Describe(want)}");
        client.Connection.Send(new MarketResult(p.RequestId, true, $"Listed for {hours:0} hours.", View(listing, client)));
    }

    private void Accept(Client client, MarketAccept a)
    {
        var data = _store.Data;
        if (Repeat(client, a.RequestId, a.TxId, isPost: false)) return;
        if (string.IsNullOrWhiteSpace(a.TxId))
        {
            client.Connection.Send(new MarketResult(a.RequestId, false, "Missing transaction id.", Code: "invalid"));
            return;
        }
        if (data.Listings.Find(l => l.Id == a.ListingId) is not { } listing)
        {
            client.Connection.Send(new MarketResult(a.RequestId, false, "That listing is gone.", Code: "gone"));
            return;
        }
        if (listing.SellerId == client.PlayerId)
        {
            client.Connection.Send(new MarketResult(a.RequestId, false, "That is your own listing; cancel it instead.", Code: "own"));
            return;
        }
        var now = _clock.GetUtcNow();
        data.Listings.Remove(listing);
        AddClaim(listing.SellerId, listing.Want, $"{client.Name} took your listing #{listing.Id}", now);
        AddClaim(client.PlayerId!, listing.Give, $"Bought from {listing.SellerName} (#{listing.Id})", now);
        data.Transactions[a.TxId] = new StoredTx { PlayerId = client.PlayerId!, IsPost = false, ListingId = listing.Id, At = now };
        _store.Save();
        Suspicious(client, "accept", listing.Want);
        Log?.Invoke($"{client.Name} took #{listing.Id} from {listing.SellerName}");
        client.Connection.Send(new MarketResult(a.RequestId, true, $"Traded with {listing.SellerName}.", View(listing, client)));
        PushClaims(listing.SellerId);
        PushClaims(client.PlayerId!);
    }

    private void Cancel(Client client, MarketCancel c)
    {
        var data = _store.Data;
        if (data.Listings.Find(l => l.Id == c.ListingId && l.SellerId == client.PlayerId) is not { } listing)
        {
            client.Connection.Send(new MarketResult(c.RequestId, false, "No such listing of yours.", Code: "gone"));
            return;
        }
        data.Listings.Remove(listing);
        AddClaim(listing.SellerId, listing.Give, $"Listing #{listing.Id} cancelled", _clock.GetUtcNow());
        _store.Save();
        client.Connection.Send(new MarketResult(c.RequestId, true, "Listing withdrawn; the goods wait as a claim.", View(listing, client)));
        PushClaims(listing.SellerId);
    }

    private void List(Client client, MarketList l)
    {
        var f = l.Filter ?? new MarketFilter();
        var text = f.Text?.Trim();
        bool Named(List<TradeGood> goods) => goods.Any(g =>
            g.Id.Contains(text!, StringComparison.OrdinalIgnoreCase)
            || (_items.Items.TryGetValue(g.Id, out var item) && item.Name.Contains(text!, StringComparison.OrdinalIgnoreCase)));
        var matches = _store.Data.Listings
            .Where(x => !f.MineOnly || x.SellerId == client.PlayerId)
            .Where(x => f.GiveId is not { Length: > 0 } || x.Give.Any(g => g.Id == f.GiveId))
            .Where(x => f.WantId is not { Length: > 0 } || x.Want.Any(g => g.Id == f.WantId))
            .Where(x => text is not { Length: > 0 } || Named(x.Give) || Named(x.Want) || x.SellerName.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Posted).ThenByDescending(x => x.Id)
            .ToList();
        client.Connection.Send(new MarketListings(l.RequestId, matches.Take(_rules.Market.MaxListingsSent).Select(x => View(x, client)).ToList(), matches.Count));
    }

    private void Ack(Client client, MarketClaimAck ack)
    {
        var ids = (ack.ClaimIds ?? []).ToHashSet(StringComparer.Ordinal);
        if (_store.Data.Claims.RemoveAll(c => c.PlayerId == client.PlayerId && ids.Contains(c.ClaimId)) > 0) _store.Save();
    }

    /// <summary>A transaction id seen before: answer as the first time (or refuse someone else's id) and do nothing more.</summary>
    private bool Repeat(Client client, int requestId, string txId, bool isPost)
    {
        if (string.IsNullOrEmpty(txId) || !_store.Data.Transactions.TryGetValue(txId, out var tx)) return false;
        if (tx.PlayerId != client.PlayerId || tx.IsPost != isPost)
        {
            Log?.Invoke($"suspicious: {client.Name} reused transaction id {txId}");
            client.Connection.Send(new MarketResult(requestId, false, "That transaction id is taken.", Code: "invalid"));
            return true;
        }
        var listing = _store.Data.Listings.Find(l => l.Id == tx.ListingId);
        client.Connection.Send(new MarketResult(requestId, true, isPost ? "Already listed." : "Already traded.", listing is null ? null : View(listing, client)));
        return true;
    }

    private void Expire()
    {
        var now = _clock.GetUtcNow();
        var expired = _store.Data.Listings.Where(l => l.Expires <= now).ToList();
        if (expired.Count == 0) return;
        foreach (var l in expired)
        {
            _store.Data.Listings.Remove(l);
            AddClaim(l.SellerId, l.Give, $"Listing #{l.Id} expired", now);
            Log?.Invoke($"listing #{l.Id} of {l.SellerName} expired; refunded");
        }
        _store.Save();
        foreach (var seller in expired.Select(l => l.SellerId).Distinct()) PushClaims(seller);
    }

    private void AddClaim(string playerId, List<TradeGood> goods, string reason, DateTimeOffset now) =>
        _store.Data.Claims.Add(new StoredClaim { ClaimId = $"claim-{Guid.NewGuid():N}", PlayerId = playerId, Goods = goods, Reason = reason, Created = now });

    private void SendClaims(Client client) =>
        client.Connection.Send(new MarketClaims(_store.Data.Claims.Where(c => c.PlayerId == client.PlayerId)
            .Select(c => new MarketClaimView(c.ClaimId, c.Goods, c.Reason)).ToList()));

    private void PushClaims(string playerId)
    {
        foreach (var client in _clients.Values.Where(c => c.PlayerId == playerId)) SendClaims(client);
    }

    private static MarketListing View(StoredListing l, Client viewer) =>
        new(l.Id, l.SellerName, l.Give, l.Want, l.Posted, l.Expires, l.SellerId == viewer.PlayerId);

    private bool Allow(Queue<DateTimeOffset> window, int perMinute)
    {
        var now = _clock.GetUtcNow();
        while (window.Count > 0 && (now - window.Peek()).TotalSeconds >= 60) window.Dequeue();
        if (window.Count >= perMinute) return false;
        window.Enqueue(now);
        return true;
    }

    private void Suspicious(Client client, string what, IEnumerable<TradeGood> goods)
    {
        if (TradeGoods.Suspicious(goods, _rules) is { } big) Log?.Invoke($"suspicious {what} by {client.Name} ({client.PlayerId}): {big}");
    }

    private static int? RequestId(Message m) => m switch
    {
        MarketPost p => p.RequestId,
        MarketList l => l.RequestId,
        MarketAccept a => a.RequestId,
        MarketCancel c => c.RequestId,
        MarketClaim c => c.RequestId,
        _ => null,
    };

    private static string Describe(List<TradeGood> goods) => string.Join(", ", goods.Select(g => $"{g.Count}×{g.Id}"));
}
