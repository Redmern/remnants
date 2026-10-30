using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Trade;
using Remnants.Game.Colony;
using Remnants.Game.Hud;
using Remnants.Net;

namespace Remnants.Game.Trade;

/// <summary>
/// Live trading for one multiplayer connection, in whatever screen hosts it (the lobby menu, the scavenge HUD): asks
/// when someone wants to trade, opens the <see cref="TradeWindow"/> on the server's updates, keeps the colony in step
/// (<see cref="LiveTradeSync"/>: escrow, courier, refunds; saved on every change) and toasts the outcome.
/// </summary>
public partial class TradeDock : Control
{
    private readonly NetClient _client;
    private readonly Toasts _toasts;
    private readonly Func<ColonySim?> _colony;
    private readonly LiveTradeSync _sync;

    public TradeDock() : this(null!, null) { }

    /// <param name="toasts">The screen's toast stack; null = the dock's own, bottom right.</param>
    public TradeDock(NetClient client, Toasts? toasts)
    {
        _client = client;
        Name = "TradeDock";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = HudTheme.Shared;
        // Lobby trades need the player's colony: the expedition's, else the saved one.
        _colony = () => (ColonySession.Current ??= Remnants.Game.Multiplayer.MultiplayerSession.LoadSavedColony())?.Sim;
        if (toasts is null)
        {
            toasts = new Toasts();
            toasts.SetAnchorsPreset(LayoutPreset.BottomRight);
            toasts.GrowHorizontal = GrowDirection.Begin;
            toasts.GrowVertical = GrowDirection.Begin;
            toasts.Position -= new Vector2(16, 16);
            AddChild(toasts);
        }
        _toasts = toasts;
        _sync = new LiveTradeSync(client, _colony, TradeUi.Rules);
        _sync.ColonyChanged += () => ColonySession.Current?.Save();
        _sync.Committed += OnCommitted;
        Window = new TradeWindow(client, _sync, Available);
        AddChild(Window);
        client.TradeNews += OnNews;
    }

    public TradeWindow Window { get; }

    /// <summary>Why lobby trades are not possible from this game (no colony, no Radio Mast), or null.</summary>
    public string? LobbyBlocked => _sync.LobbyBlocked();

    public override void _ExitTree()
    {
        _client.TradeNews -= OnNews;
        _sync.Dispose();
    }

    public void Request(int playerId)
    {
        if (_client.Phase == ClientPhase.Lobby && LobbyBlocked is { } why)
        {
            _toasts.Show("Can't trade", why, "stat_warning", Palette.Danger);
            return;
        }
        _client.RequestTrade(playerId);
    }

    /// <summary>What this player can put on the table: colony goods in the lobby, the party's pack items in the field.</summary>
    private List<(TradeGood Good, int Have)> Available(TradeVenue venue)
    {
        if (venue == TradeVenue.Field)
            return _client.View?.State.Units.Where(u => u.PlayerId == _client.PlayerId && u.OnMap).SelectMany(u => u.Pack)
                .GroupBy(id => id).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (TradeGood.Item(g.Key), g.Count())).ToList() ?? new();
        if (_colony() is not { } colony) return new();
        var state = colony.State;
        var data = colony.Data;
        return data.Resources.Keys.Where(id => state.Stockpile.Get(id) >= 1).OrderBy(id => data.Resources[id].Name)
            .Select(id => (TradeGood.Resource(id, 1), (int)Math.Floor(state.Stockpile.Get(id) + 1e-9)))
            .Concat(state.Inventory.Counts.Select(kv => (TradeGood.Item(kv.Key), kv.Value)))
            .ToList();
    }

    private void OnNews(Message message)
    {
        switch (message)
        {
            case TradeInvite invite:
                var accept = new Action(() => _client.RespondTrade(invite.TradeId, true));
                if (invite.Venue == TradeVenue.Lobby && LobbyBlocked is { } why)
                {
                    _client.RespondTrade(invite.TradeId, false);
                    _toasts.Show("Trade declined", $"{invite.FromName} wanted to trade. {why}", "ui_trade", Palette.Warn);
                    break;
                }
                if (DebugCapture.HasFlag("--trade-accept")) accept();
                else ConfirmDialog.Ask(this, "Trade offer", $"{invite.FromName} wants to trade with you.", "Open trade", accept);
                break;
            case TradeUpdate update:
                var (me, _) = _client.SidesOf(update.Trade);
                // The inviter sees the window at once; the invited player once they accept.
                if (update.Trade.Accepted || update.Trade.Sides[0].PlayerId == me.PlayerId) Window.Show(update.Trade);
                break;
            case TradeCancelled cancelled:
                if (Window.TradeId == cancelled.TradeId || cancelled.TradeId == 0) Window.Dismiss();
                _toasts.Show("Trade off", cancelled.Reason, "ui_trade", Palette.Warn, "trade");
                break;
        }
    }

    private void OnCommitted(TradeCommit commit, CourierPlan? plan)
    {
        if (Window.TradeId == commit.TradeId) Window.Dismiss();
        var got = commit.Get.Count == 0 ? "nothing" : string.Join(", ", commit.Get.Select(g => $"{g.Count} {TradeUi.Name(g)}"));
        _toasts.Show($"Trade with {commit.PartnerName} done",
            plan is { } p ? $"{got} on the way by courier: about {p.Days:0.#} day{(p.Days == 1 ? "" : "s")}." : $"{got} is in your packs.",
            plan is null ? "ui_trade" : "ui_courier", Palette.Good);
    }
}
