using Remnants.Core.Colony;
using Remnants.Core.Trade;

namespace Remnants.Net;

/// <summary>
/// Keeps the local colony in step with this client's live lobby trades: the offer goes into escrow when the player
/// confirms, the escrow is settled and the other side's goods sent home by courier on <see cref="TradeCommit"/>, and
/// refunded when the trade is cancelled or a change on the table un-confirms it. Field trades need nothing here: the
/// server swaps the pack items and they come home with the extraction result.
/// </summary>
public sealed class LiveTradeSync : IDisposable
{
    public const string EscrowPrefix = "live:";

    private readonly NetClient _client;
    private readonly Func<ColonySim?> _colony;
    private readonly TradeRules _rules;

    public LiveTradeSync(NetClient client, Func<ColonySim?> colony, TradeRules rules)
    {
        _client = client;
        _colony = colony;
        _rules = rules;
        client.TradeNews += OnNews;
    }

    /// <summary>The colony changed (escrowed, settled, refunded, a courier sent): save it.</summary>
    public event Action? ColonyChanged;

    /// <summary>A lobby trade went through: its courier plan (null for field trades).</summary>
    public event Action<TradeCommit, CourierPlan?>? Committed;

    public void Dispose() => _client.TradeNews -= OnNews;

    public string EscrowId(int tradeId) => $"{EscrowPrefix}{_client.SessionTag}:{tradeId}";

    /// <summary>Why this client cannot trade colony goods in the lobby, or null.</summary>
    public string? LobbyBlocked() => _colony() is not { } colony ? "Bring a colony to trade: launch an online expedition from it." : Courier.Blocked(colony, _rules);

    /// <summary>
    /// Confirm my side. In the lobby my offer leaves the colony into escrow first (fails when the colony no longer has it).
    /// </summary>
    public CommandResult Confirm(int tradeId)
    {
        if (!_client.Trades.TryGetValue(tradeId, out var trade)) return CommandResult.Fail(CommandError.UnknownCommand, "That trade is no longer open.");
        var (me, them) = _client.SidesOf(trade);
        if (!me.Locked || !them.Locked) return CommandResult.Fail(CommandError.UnknownCommand, "Both sides must lock their offers first.");
        if (me.Confirmed) return CommandResult.Success("Already confirmed.");
        if (trade.Venue == TradeVenue.Lobby)
        {
            if (LobbyBlocked() is { } why) return CommandResult.Fail(CommandError.CannotAfford, why);
            var result = ColonyTrade.Escrow(_colony()!, EscrowId(tradeId), me.Goods, $"trade with {them.Name}");
            if (!result.Ok) return result;
            ColonyChanged?.Invoke();
        }
        _client.ConfirmTrade(tradeId);
        return CommandResult.Success("Confirmed.");
    }

    private void OnNews(Message message)
    {
        var colony = _colony();
        switch (message)
        {
            case TradeUpdate u when u.Trade.Venue == TradeVenue.Lobby && colony is not null:
                if (!_client.SidesOf(u.Trade).Me.Confirmed && ColonyTrade.Refund(colony, EscrowId(u.Trade.TradeId))) ColonyChanged?.Invoke();
                break;
            case TradeCancelled c when colony is not null:
                if (ColonyTrade.Refund(colony, EscrowId(c.TradeId))) ColonyChanged?.Invoke();
                break;
            case TradeCommit c when c.Venue == TradeVenue.Lobby && colony is not null:
                ColonyTrade.Settle(colony, EscrowId(c.TradeId));
                var plan = Courier.Plan(colony, _rules, c.Legs);
                ColonyTrade.Dispatch(colony, c.TxId, c.Get, c.PartnerName, plan.Days);
                ColonyChanged?.Invoke();
                Committed?.Invoke(c, plan);
                break;
            case TradeCommit c:
                Committed?.Invoke(c, null);
                break;
        }
    }
}
