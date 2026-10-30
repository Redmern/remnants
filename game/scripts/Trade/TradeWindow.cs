using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Trade;
using Remnants.Game.Hud;
using Remnants.Net;

namespace Remnants.Game.Trade;

/// <summary>
/// The live trade window: two columns (what you give, what you get) of offer chips, each side's state (editing, locked,
/// confirmed), a "you give / you get" summary with reference values and, in the lobby, the courier's travel time; then
/// Lock, Confirm and Cancel. Any change to either offer unlocks both (the server enforces it; the window shows it). The
/// server's <see cref="TradeView"/> is the truth: the window only sends intents and redraws from what comes back.
/// </summary>
public partial class TradeWindow : Overlay
{
    private readonly NetClient _client;
    private readonly LiveTradeSync? _sync;
    private readonly Func<TradeVenue, List<(TradeGood Good, int Have)>> _available;
    private readonly Label _title = Title("TRADE");
    private readonly Label _subtitle = Widgets.Wrapped("", "Muted");
    private readonly VBoxContainer _mine = Widgets.Column(6);
    private readonly VBoxContainer _theirs = Widgets.Column(6);
    private readonly GoodPicker _picker = new();
    private readonly RichTextLabel _summary = Colony.Ui.Ui.Rich();
    private readonly Label _status = Widgets.Wrapped("", "Muted");
    private readonly Button _lock, _confirm;
    private TradeView? _view;

    public TradeWindow() : this(null!, null, _ => new()) { }

    public TradeWindow(NetClient client, LiveTradeSync? sync, Func<TradeVenue, List<(TradeGood Good, int Have)>> available) : base(760)
    {
        _client = client;
        _sync = sync;
        _available = available;
        Visible = false;
        Body.AddChild(_title);
        Body.AddChild(_subtitle);

        var columns = Widgets.Row(12);
        Body.AddChild(columns);
        columns.AddChild(Column("You give", "ui_trade", _mine, out var mineBox));
        mineBox.AddChild(_picker);
        columns.AddChild(Column("You get", "stat_loot", _theirs, out _));
        _picker.Picked += Add;

        Body.AddChild(Widgets.SectionHeader("Summary", "ui_info"));
        _summary.CustomMinimumSize = new Vector2(0, 70);
        Body.AddChild(_summary);
        Body.AddChild(_status);

        var buttons = Widgets.Row(8);
        Body.AddChild(buttons);
        _lock = Widgets.IconButton("stat_lock", "Lock offer", "Freeze your offer; confirming needs both sides locked", ToggleLock);
        _confirm = Widgets.IconButton("stat_check", "Confirm", "Agree to the trade as it stands. It goes through when both confirm.", Confirm, variation: "Primary");
        _lock.CustomMinimumSize = _confirm.CustomMinimumSize = new Vector2(150, 38);
        buttons.AddChild(_lock);
        buttons.AddChild(_confirm);
        buttons.AddChild(Widgets.Spacer());
        buttons.AddChild(Widgets.IconButton("ui_close", "Cancel trade", "Walk away: both sides keep their goods", CancelTrade));
    }

    public int? TradeId => _view?.TradeId;

    private static PanelContainer Column(string title, string icon, VBoxContainer content, out VBoxContainer box)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(340, 220) };
        panel.AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Inset, Palette.EdgeDark, 0.03f), 12, 10));
        box = Widgets.Column(8);
        panel.AddChild(box);
        box.AddChild(Widgets.SectionHeader(title, icon));
        content.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.AddChild(content);
        return panel;
    }

    public void Show(TradeView view)
    {
        _view = view;
        if (!Visible) Open();
        Redraw();
    }

    /// <summary>Closing the window (Esc, Cancel) walks away from the trade.</summary>
    public override void Close()
    {
        if (_view is { } v) _client.CancelTrade(v.TradeId);
        Dismiss();
    }

    /// <summary>The trade is over (committed or cancelled): hide without sending anything.</summary>
    public void Dismiss()
    {
        _view = null;
        base.Close();
    }

    /// <summary>The colony or the packs changed: refresh what can be picked.</summary>
    public void RefreshChoices()
    {
        if (_view is not null) Redraw();
    }

    private void Redraw()
    {
        if (_view is not { } view) return;
        var (me, them) = _client.SidesOf(view);
        var lobby = view.Venue == TradeVenue.Lobby;
        _title.Text = $"TRADE WITH {them.Name.ToUpperInvariant()}";
        _subtitle.Text = lobby
            ? "Lobby trade: your offer leaves the colony when you confirm; what you get travels home by courier over the Radio Mast."
            : "Field trade: items move between the two parties' packs and go home with whoever extracts.";

        Widgets.Clear(_mine);
        Widgets.Clear(_theirs);
        _mine.AddChild(State(me));
        _mine.AddChild(TradeUi.Chips(me.Goods, me.Locked ? null : g => () => Remove(g), "Nothing offered yet. Add goods below."));
        _theirs.AddChild(State(them));
        _theirs.AddChild(TradeUi.Chips(them.Goods, empty: view.Accepted ? $"{them.Name} has not offered anything yet." : $"Waiting for {them.Name} to accept…"));

        var choices = _available(view.Venue)
            .Select(c => (c.Good, c.Have - me.Goods.Where(g => g.Key == c.Good.Key).Sum(g => g.Count)))
            .ToList();
        _picker.SetChoices(choices);
        _picker.Visible = view.Accepted && !me.Locked;

        var give = TradeUi.Value(me.Goods);
        var get = TradeUi.Value(them.Goods);
        var lines = new List<string>
        {
            $"[b]You give:[/b] {Describe(me.Goods)}  {Palette.Paint($"≈ {give:0} scrap", Palette.TextMuted)}",
            $"[b]You get:[/b] {Describe(them.Goods)}  {Palette.Paint($"≈ {get:0} scrap", Palette.TextMuted)}",
            TradeUi.Fairness(give, get),
        };
        if (lobby && Colony.ColonySession.Current is { } session)
        {
            var legs = Courier.Legs(_client.NameOf(me.PlayerId), them.Name, TradeUi.Rules.Courier.MaxLegs);
            var plan = Courier.Plan(session.Sim, TradeUi.Rules, legs);
            var why = plan.Factors.Count == 0 ? "" : $" ({string.Join(", ", plan.Factors.Select(f => $"{f.Label} ×{f.Factor:0.##}"))})";
            lines.Add(Palette.Paint($"Courier: about {plan.Days:0.#} day{(plan.Days == 1 ? "" : "s")} over {plan.Legs} leg{(plan.Legs == 1 ? "" : "s")}{why}.", Palette.TextMuted));
        }
        _summary.Text = string.Join("\n", lines);

        _lock.Text = me.Locked ? "Unlock" : "Lock offer";
        _lock.Disabled = !view.Accepted || me.Confirmed;
        _confirm.Disabled = !view.Accepted || !me.Locked || !them.Locked || me.Confirmed;
        _confirm.Text = me.Confirmed ? "Confirmed" : "Confirm";
        _status.Text = !view.Accepted ? $"{them.Name} has been invited."
            : me.Confirmed && !them.Confirmed ? $"Waiting for {them.Name} to confirm."
            : them.Confirmed && !me.Confirmed ? $"{them.Name} has confirmed. Your turn."
            : me.Locked && them.Locked ? "Both offers are locked: confirm to trade."
            : me.Locked ? $"Locked. Waiting for {them.Name} to lock."
            : $"Changing either offer unlocks both. Closes after {view.SecondsLeft:0} s without changes.";
    }

    private static Control State(TradeSideView side)
    {
        var (text, color, icon) = side.Confirmed ? ("CONFIRMED", Palette.Good, "stat_check")
            : side.Locked ? ("LOCKED", Palette.Amber, "stat_lock")
            : ("EDITING", Palette.TextMuted, "ui_trade");
        var row = Widgets.Row(6);
        row.AddChild(Icons.Rect(Icons.Get(icon, 16), 16, color));
        row.AddChild(Widgets.Label(side.Name, color: Palette.Text, size: 15));
        row.AddChild(Widgets.Spacer());
        row.AddChild(TradeUi.Tag(text, color));
        return row;
    }

    private static string Describe(List<TradeGood> goods) =>
        goods.Count == 0 ? "nothing" : string.Join(", ", goods.Select(g => g.Count == 1 && g.Kind == GoodKind.Item ? TradeUi.Name(g) : $"{g.Count} {TradeUi.Name(g)}"));

    private List<TradeGood> MyGoods() => _view is { } v ? _client.SidesOf(v).Me.Goods.ToList() : new();

    private void Add(TradeGood good)
    {
        if (_view is not { } v) return;
        _client.OfferTrade(v.TradeId, TradeGoods.Normalize(MyGoods().Append(good)));
    }

    private void Remove(TradeGood good)
    {
        if (_view is not { } v) return;
        _client.OfferTrade(v.TradeId, MyGoods().Where(g => g.Key != good.Key).ToList());
    }

    private void ToggleLock()
    {
        if (_view is not { } v) return;
        _client.LockTrade(v.TradeId, !_client.SidesOf(v).Me.Locked);
    }

    /// <summary>Debug flags press Confirm like the player would.</summary>
    public void ConfirmFromDebug() => Confirm();

    private void Confirm()
    {
        if (_view is not { } v) return;
        if (_sync is not null)
        {
            var result = _sync.Confirm(v.TradeId);
            if (!result.Ok) _status.Text = result.Message;
        }
        else _client.ConfirmTrade(v.TradeId);
    }

    private void CancelTrade() => Close();
}
