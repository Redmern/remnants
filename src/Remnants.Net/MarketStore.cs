using System.Text.Json;
using Remnants.Core.Serialization;
using Remnants.Core.Trade;

namespace Remnants.Net;

/// <summary>An open listing on the Trading Post. Its <see cref="Give"/> is held by the server (escrow) until it is taken, cancelled or expires.</summary>
public sealed class StoredListing
{
    public int Id { get; set; }
    public string SellerId { get; set; } = "";
    public string SellerName { get; set; } = "";
    public List<TradeGood> Give { get; set; } = new();
    public List<TradeGood> Want { get; set; } = new();
    public DateTimeOffset Posted { get; set; }
    public DateTimeOffset Expires { get; set; }
    public string TxId { get; set; } = "";
}

/// <summary>Goods the server holds for a player until their client has them safely in its colony (acked).</summary>
public sealed class StoredClaim
{
    public string ClaimId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public List<TradeGood> Goods { get; set; } = new();
    public string Reason { get; set; } = "";
    public DateTimeOffset Created { get; set; }
}

/// <summary>A client transaction id the server has already carried out (so a retry is answered, not repeated).</summary>
public sealed class StoredTx
{
    public string PlayerId { get; set; } = "";
    public bool IsPost { get; set; }
    public int ListingId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Everything the Trading Post keeps across restarts.</summary>
public sealed class MarketData
{
    public int Version { get; set; } = 1;
    public int NextListingId { get; set; } = 1;
    public List<StoredListing> Listings { get; set; } = new();
    public List<StoredClaim> Claims { get; set; } = new();
    public Dictionary<string, StoredTx> Transactions { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Market player id → last display name.</summary>
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The Trading Post's storage: one JSON file, rewritten atomically (temp file + rename) after every change, so a crash
/// leaves either the old or the new state. No database dependency. Null path = in memory only (tests).
/// </summary>
public sealed class MarketStore
{
    public const int MaxTransactions = 20000;

    public MarketStore(string? path)
    {
        Path = path;
        Data = path is not null && File.Exists(path)
            ? JsonSerializer.Deserialize<MarketData>(File.ReadAllText(path), Json.Options) ?? new MarketData()
            : new MarketData();
    }

    public string? Path { get; }
    public MarketData Data { get; }

    public void Save()
    {
        if (Data.Transactions.Count > MaxTransactions)
            foreach (var old in Data.Transactions.OrderBy(kv => kv.Value.At).Take(Data.Transactions.Count - MaxTransactions).Select(kv => kv.Key).ToList())
                Data.Transactions.Remove(old);
        if (Path is null) return;
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (dir is not null) Directory.CreateDirectory(dir);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Data, Json.Options));
        File.Move(temp, Path, overwrite: true);
    }
}
