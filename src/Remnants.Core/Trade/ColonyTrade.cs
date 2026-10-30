using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Trade;

/// <summary>
/// The colony's side of player trading (saved with the colony). Goods offered in a trade or posted on the Trading Post
/// leave the stockpile into <see cref="Escrows"/> until the server settles them (gone) or refunds them; goods coming in
/// by courier wait in <see cref="Deliveries"/>; <see cref="Applied"/> remembers every transaction id already paid out,
/// so a claim or commit delivered twice (a retry, a reconnect) is applied once.
/// </summary>
public sealed class TradeState
{
    public const int MaxApplied = 1000;

    public List<TradeEscrow> Escrows { get; set; } = new();
    public List<CourierDelivery> Deliveries { get; set; } = new();
    public List<string> Applied { get; set; } = new();

    public bool WasApplied(string txId) => Applied.Contains(txId);

    internal void MarkApplied(string txId)
    {
        Applied.Add(txId);
        if (Applied.Count > MaxApplied) Applied.RemoveRange(0, Applied.Count - MaxApplied);
    }
}

/// <summary>
/// Goods set aside for a trade (<paramref name="Purpose"/> for the UI, e.g. "trade with Bob" or "Trading Post").
/// <paramref name="Market"/>: the Trading Post request it pays for, kept so it can be sent again (same id) after a lost
/// connection until the server answers.
/// </summary>
public sealed record TradeEscrow(string TxId, List<TradeGood> Goods, string Purpose, long Tick, PendingMarketOp? Market = null);

/// <summary>A Trading Post request waiting for the server's answer: a post (<paramref name="Want"/>, <paramref name="Hours"/>) or an accept (<paramref name="ListingId"/>).</summary>
public sealed record PendingMarketOp(bool IsPost, int ListingId = 0, List<TradeGood>? Want = null, double Hours = 0);

/// <summary>A courier carrying a lobby trade's goods home: they arrive at <paramref name="ArriveTick"/>.</summary>
public sealed record CourierDelivery(string TxId, List<TradeGood> Goods, string From, long DepartTick, long ArriveTick);

/// <summary>
/// Moves goods in and out of a colony for trading. Every entry point takes a transaction id and is idempotent on it.
/// </summary>
public static class ColonyTrade
{
    public static TradeState Of(ColonySim colony) => colony.State.Trade ??= new TradeState();

    /// <summary>
    /// Set <paramref name="goods"/> aside for transaction <paramref name="txId"/>: they leave the stockpile and inventory
    /// now. Fails (taking nothing) when any is missing. Escrowing the same id twice is a no-op success.
    /// </summary>
    public static CommandResult Escrow(ColonySim colony, string txId, IEnumerable<TradeGood> goods, string purpose, PendingMarketOp? market = null)
    {
        var trade = Of(colony);
        if (trade.Escrows.Any(e => e.TxId == txId)) return CommandResult.Success("Already set aside.");
        if (trade.WasApplied(txId)) return CommandResult.Fail(CommandError.AlreadyKnown, "That trade is already settled.");
        var lines = TradeGoods.Normalize(goods);
        var state = colony.State;
        foreach (var g in lines)
        {
            var have = g.Kind == GoodKind.Resource ? state.Stockpile.Get(g.Id) : state.Inventory.Count(g.Id);
            if (have + 1e-9 < g.Count)
                return CommandResult.Fail(CommandError.MissingItems, $"Not enough {Name(colony, g)} (have {Math.Floor(have):0}, offered {g.Count}).");
        }
        foreach (var g in lines) Take(state, g);
        trade.Escrows.Add(new TradeEscrow(txId, lines, purpose, state.Tick, market));
        return CommandResult.Success($"Set aside for {purpose}.");
    }

    /// <summary>The trade fell through: the escrowed goods go back. Unknown ids are ignored.</summary>
    public static bool Refund(ColonySim colony, string txId)
    {
        var trade = Of(colony);
        if (trade.Escrows.Find(e => e.TxId == txId) is not { } escrow) return false;
        trade.Escrows.Remove(escrow);
        foreach (var g in escrow.Goods) Put(colony, g);
        return true;
    }

    /// <summary>
    /// Refund every escrow whose id starts with <paramref name="prefix"/> (on load: live trades whose connection is gone
    /// can no longer commit). Returns how many.
    /// </summary>
    public static int RefundAll(ColonySim colony, string prefix) =>
        Of(colony).Escrows.Where(e => e.TxId.StartsWith(prefix, StringComparison.Ordinal)).Select(e => e.TxId).ToList().Count(id => Refund(colony, id));

    /// <summary>The trade went through: the escrowed goods are gone for good.</summary>
    public static bool Settle(ColonySim colony, string txId)
    {
        var trade = Of(colony);
        if (trade.Escrows.Find(e => e.TxId == txId) is not { } escrow) return false;
        trade.Escrows.Remove(escrow);
        return true;
    }

    /// <summary>
    /// Goods arrive now (a Trading Post claim, a field trade carried home). Returns false when <paramref name="txId"/> was
    /// already applied (nothing is added twice).
    /// </summary>
    public static bool Receive(ColonySim colony, string txId, IEnumerable<TradeGood> goods, string from)
    {
        var trade = Of(colony);
        if (trade.WasApplied(txId)) return false;
        trade.MarkApplied(txId);
        var lines = TradeGoods.Normalize(goods);
        foreach (var g in lines) Put(colony, g);
        var state = colony.State;
        state.Log(new(state.Tick, ColonyEventType.TradeReceived,
            $"From {from}: {(colony.Scavenge is { } items ? TradeGoods.Describe(lines, colony.Data, items) : $"{lines.Count} kinds of goods")}."));
        return true;
    }

    /// <summary>
    /// Send goods home by courier: they arrive after <paramref name="days"/> colony days (see <see cref="Courier.Plan"/>).
    /// Returns false when <paramref name="txId"/> is already on the road or delivered.
    /// </summary>
    public static bool Dispatch(ColonySim colony, string txId, IEnumerable<TradeGood> goods, string from, double days)
    {
        var trade = Of(colony);
        if (trade.WasApplied(txId) || trade.Deliveries.Any(d => d.TxId == txId)) return false;
        var state = colony.State;
        var arrive = state.Tick + Math.Max(1, (long)Math.Round(days * colony.Data.Rules.TicksPerDay));
        trade.Deliveries.Add(new CourierDelivery(txId, TradeGoods.Normalize(goods), from, state.Tick, arrive));
        state.Log(new(state.Tick, ColonyEventType.CourierDispatched, $"A courier with goods from {from} is on the way ({days:0.#} days)."));
        return true;
    }

    /// <summary>Every tick: couriers that have arrived hand over their goods.</summary>
    public static void Run(ColonySim colony)
    {
        if (colony.State.Trade is not { Deliveries.Count: > 0 } trade) return;
        foreach (var d in trade.Deliveries.Where(d => d.ArriveTick <= colony.State.Tick).ToList())
        {
            trade.Deliveries.Remove(d);
            if (trade.WasApplied(d.TxId)) continue;
            trade.MarkApplied(d.TxId);
            foreach (var g in d.Goods) Put(colony, g);
            var state = colony.State;
            state.Log(new(state.Tick, ColonyEventType.CourierArrived,
                $"The courier from {d.From} arrived: {(colony.Scavenge is { } items ? TradeGoods.Describe(d.Goods, colony.Data, items) : "goods")}."));
            colony.RaiseCourierArrived(d);
        }
    }

    private static void Take(ColonyState state, TradeGood g)
    {
        if (g.Kind == GoodKind.Resource) state.Stockpile.TryTake(g.Id, g.Count);
        else state.Inventory.TryTake(g.Id, g.Count);
    }

    private static void Put(ColonySim colony, TradeGood g)
    {
        if (g.Kind == GoodKind.Resource) colony.State.Stockpile.Add(g.Id, g.Count);
        else for (var i = 0; i < g.Count; i++) SchematicCopies.Receive(colony, g.Id);
    }

    private static string Name(ColonySim colony, TradeGood g) =>
        colony.Scavenge is { } items ? TradeGoods.Name(g, colony.Data, items) : g.Id;
}
