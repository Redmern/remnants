using System.Security.Cryptography;
using System.Text.Json;
using Remnants.Core.Colony;
using Remnants.Core.Serialization;
using Remnants.Core.Trade;

namespace Remnants.Net;

/// <summary>
/// The player's Trading Post identity: a display name and a secret key generated on this machine (kept in a local file;
/// the game uses <c>user://trade_identity.json</c>). The server only ever stores the key's hash. Losing the file means a
/// new market id: open listings and unclaimed goods of the old one can no longer be reached.
/// </summary>
public sealed record TradeIdentity(string Name, string Key)
{
    public static TradeIdentity Create(string name) => new(name, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());

    /// <summary>Reads the identity file, or creates (and writes) a new one.</summary>
    public static TradeIdentity LoadOrCreate(string path, string name)
    {
        if (File.Exists(path))
        {
            try
            {
                if (JsonSerializer.Deserialize<TradeIdentity>(File.ReadAllText(path), Json.Options) is { Key.Length: >= 32 } existing)
                    return existing with { Name = name.Length > 0 ? name : existing.Name };
            }
            catch (JsonException) { }
        }
        var created = Create(name);
        File.WriteAllText(path, JsonSerializer.Serialize(created, Json.Options));
        return created;
    }
}

/// <summary>
/// The client side of the Trading Post, without any engine: hello, browse, post, accept, cancel and claims, with the
/// colony kept in step. A post or accept takes its goods into the colony's escrow first (under a fresh transaction id)
/// and is settled or refunded by the server's verdict; requests still waiting when the link drops are sent again with
/// the same id after the next hello, so nothing is lost or done twice. Claims are applied to the colony by claim id
/// (idempotent) and acked only after <see cref="ColonyChanged"/> (the game saves there).
/// </summary>
public sealed class MarketClient
{
    public const string PostPrefix = "mkt-post-";
    public const string AcceptPrefix = "mkt-take-";

    private IClientTransport _transport;
    private readonly Action<Action> _post;
    private readonly Func<ColonySim?> _colony;
    private readonly Dictionary<int, (string? TxId, TaskCompletionSource<MarketResult>? Result, TaskCompletionSource<MarketListings>? List)> _pending = new();
    private TradeIdentity? _identity;
    private int _nextRequest = 1;

    public MarketClient(IClientTransport transport, Func<ColonySim?> colony, Action<Action>? post = null)
    {
        _transport = transport;
        _colony = colony;
        _post = post ?? (a => a());
        Attach(transport);
    }

    public MarketWelcome? Welcome { get; private set; }
    public bool Connected { get; private set; } = true;
    public string? Error { get; private set; }
    public MarketListings? Listings { get; private set; }

    /// <summary>Claims applied to the colony since connecting (newest last), for toasts and the Claims tab.</summary>
    public List<MarketClaimView> Received { get; } = new();

    /// <summary>Anything changed (welcome, listings, claims, connection).</summary>
    public event Action? Changed;

    /// <summary>The colony changed (escrow, settle, refund, claim applied): save it. Claims are acked right after.</summary>
    public event Action? ColonyChanged;

    /// <summary>Claims just applied to the colony (for a toast).</summary>
    public event Action<List<MarketClaimView>>? Claimed;

    public void Hello(TradeIdentity identity)
    {
        _identity = identity;
        _transport.Send(new MarketHello(Protocol.Version, identity.Name, identity.Key));
    }

    /// <summary>A new link after a drop: hello again; pending requests go out again after the welcome.</summary>
    public void Reconnect(IClientTransport transport)
    {
        _transport = transport;
        Connected = true;
        Error = null;
        Attach(transport);
        if (_identity is not null) Hello(_identity);
    }

    public Task<MarketListings> Browse(MarketFilter? filter = null)
    {
        var id = _nextRequest++;
        var tcs = new TaskCompletionSource<MarketListings>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = (null, null, tcs);
        _transport.Send(new MarketList(id, filter));
        return tcs.Task;
    }

    /// <summary>Post "give for want": <paramref name="give"/> leaves the colony into escrow now.</summary>
    public Task<MarketResult> Post(List<TradeGood> give, List<TradeGood> want, double hours = 0)
    {
        if (_colony() is not { } colony) return Task.FromResult(new MarketResult(0, false, "No colony loaded.", Code: "invalid"));
        var tx = PostPrefix + Guid.NewGuid().ToString("N");
        var escrow = ColonyTrade.Escrow(colony, tx, give, "Trading Post listing", new PendingMarketOp(true, Want: TradeGoods.Normalize(want), Hours: hours));
        if (!escrow.Ok) return Task.FromResult(new MarketResult(0, false, escrow.Message, Code: "invalid"));
        ColonyChanged?.Invoke();
        return Send(tx, id => new MarketPost(id, tx, TradeGoods.Normalize(give), TradeGoods.Normalize(want), hours));
    }

    /// <summary>Take a listing: its Want leaves the colony into escrow now; its Give comes back as a claim.</summary>
    public Task<MarketResult> Accept(MarketListing listing)
    {
        if (_colony() is not { } colony) return Task.FromResult(new MarketResult(0, false, "No colony loaded.", Code: "invalid"));
        var tx = AcceptPrefix + Guid.NewGuid().ToString("N");
        var escrow = ColonyTrade.Escrow(colony, tx, listing.Want, $"listing #{listing.Id} of {listing.Seller}", new PendingMarketOp(false, listing.Id));
        if (!escrow.Ok) return Task.FromResult(new MarketResult(0, false, escrow.Message, Code: "invalid"));
        ColonyChanged?.Invoke();
        return Send(tx, id => new MarketAccept(id, tx, listing.Id));
    }

    public Task<MarketResult> Cancel(int listingId) => Send(null, id => new MarketCancel(id, listingId));

    public void RequestClaims() => _transport.Send(new MarketClaim(_nextRequest++));

    public void Close() => _transport.Close();

    private Task<MarketResult> Send(string? tx, Func<int, Message> make)
    {
        var id = _nextRequest++;
        var tcs = new TaskCompletionSource<MarketResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = (tx, tcs, null);
        _transport.Send(make(id));
        return tcs.Task;
    }

    private void Attach(IClientTransport transport)
    {
        transport.Received += m => _post(() =>
        {
            if (ReferenceEquals(transport, _transport)) Handle(m);
        });
        transport.Closed += () => _post(() =>
        {
            if (!ReferenceEquals(transport, _transport)) return;
            Connected = false;
            foreach (var (tx, result, list) in _pending.Values)
            {
                // Escrowed requests stay pending in the colony and go out again on the next connect.
                result?.TrySetResult(new MarketResult(0, false, "Connection lost; the request will be sent again when you reconnect.", Code: "offline"));
                list?.TrySetCanceled();
            }
            _pending.Clear();
            Changed?.Invoke();
        });
    }

    private void Handle(Message message)
    {
        switch (message)
        {
            case MarketWelcome w:
                Welcome = w;
                Resend();
                break;
            case MarketListings l:
                Listings = l;
                if (_pending.Remove(l.RequestId, out var p)) p.List?.TrySetResult(l);
                break;
            case MarketResult r:
                if (!r.Ok && r.Code is "version" or "invalid" && r.RequestId == 0) Error = r.Message;
                if (_pending.Remove(r.RequestId, out var pending))
                {
                    if (pending.TxId is { } tx && _colony() is { } colony && (r.Ok ? ColonyTrade.Settle(colony, tx) : ColonyTrade.Refund(colony, tx)))
                        ColonyChanged?.Invoke();
                    pending.Result?.TrySetResult(r);
                }
                break;
            case MarketClaims c:
                ApplyClaims(c.Claims);
                break;
        }
        Changed?.Invoke();
    }

    /// <summary>Posts and accepts the colony escrowed but never heard back about: send them again under the same id.</summary>
    private void Resend()
    {
        if (_colony() is not { } colony) return;
        foreach (var e in ColonyTrade.Of(colony).Escrows.Where(e => e.Market is not null).ToList())
        {
            var op = e.Market!;
            var tx = e.TxId;
            _ = op.IsPost
                ? Send(tx, id => new MarketPost(id, tx, e.Goods, op.Want ?? [], op.Hours))
                : Send(tx, id => new MarketAccept(id, tx, op.ListingId));
        }
    }

    private void ApplyClaims(List<MarketClaimView> claims)
    {
        if (claims.Count == 0 || _colony() is not { } colony) return;
        var fresh = claims.Where(c => ColonyTrade.Receive(colony, c.ClaimId, c.Goods, $"the Trading Post ({c.Reason})")).ToList();
        ColonyChanged?.Invoke();
        Received.AddRange(fresh);
        if (fresh.Count > 0) Claimed?.Invoke(fresh);
        // Acked after the colony was saved (ColonyChanged): a crash before this re-sends them, and Receive ignores repeats.
        _transport.Send(new MarketClaimAck(claims.Select(c => c.ClaimId).ToList()));
    }
}
