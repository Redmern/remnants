using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Schematics;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The Radio Mast (shown while one is selected): tabs built from <see cref="Tabs"/>, a list of (title, builder) entries
/// so another tab (a Trading Post) is one more line. <b>Calls</b>: the queue, oldest first, with Answer (a trader's
/// call opens the Trader tab) and, for a contract offer, Accept. <b>Trader</b>: who, their greeting, the stock with a Buy
/// button per price option (cost chips mark what is missing), a Sell section (schematic copies and chips in the
/// stores, Research Data ×1 / ×5) and when they leave or next come. <b>Contracts</b>: the offer or the job taken, its due
/// day and reward. Core decides every trade.
/// </summary>
public partial class RadioPanel : PanelContainer
{
    /// <summary>The tabs, in order. Add an entry to add a tab.</summary>
    public static readonly (string Title, Func<ColonySession, RadioPanel, RadioTab> Build)[] Tabs =
    [
        ("Calls", (s, p) => new CallsTab(s, p)),
        ("Trader", (s, p) => new TraderTab(s, p)),
        ("Contracts", (s, p) => new ContractsTab(s, p)),
        ("Trading Post", (s, p) => new Trade.TradingPostTab(s, p)),
    ];

    private readonly ColonySession _session;
    private readonly TabContainer _tabs = new() { CustomMinimumSize = new Vector2(0, 160), FocusMode = FocusModeEnum.All };
    private readonly List<RadioTab> _built = new();
    private readonly Label _level = Widgets.Label("", "Small");

    public int? BuildingId { get; private set; }

    public RadioPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(300, 0);
        Visible = false;
        var box = Ui.Panel(this);
        var head = Widgets.Row(4);
        head.AddChild(_level);
        head.AddChild(new Hud.Help.InfoTip("trading"));
        box.AddChild(Widgets.SectionHeader("Radio", "ui_radio", head));
        foreach (var (title, build) in Tabs)
        {
            var tab = build(session, this);
            // No scroll of its own: the tab grows and the HUD's right column scrolls.
            tab.Name = title;
            tab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _tabs.AddChild(tab);
            _tabs.SetTabTitle(_tabs.GetTabCount() - 1, title);
            _built.Add(tab);
        }
        _tabs.TabChanged += _ => Refresh();
        box.AddChild(_tabs);
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        foreach (var tab in _built) tab.Invalidate();
        Refresh();
    }

    /// <summary>Switches to the tab with this title (case-insensitive), e.g. after answering the trader's call.</summary>
    public void ShowTab(string title)
    {
        var index = Array.FindIndex(Tabs, t => string.Equals(t.Title, title, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _tabs.CurrentTab = index;
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        Visible = building is not null && sim.Data.Schematics.All.Count > 0 && building.DefId == sim.Data.Schematics.Rules.Radio.Building;
        if (!Visible) return;
        var level = $"trader visits every {RadioMast.VisitEveryDays(sim):0} days";
        if (_level.Text != level) _level.Text = level;
        for (var i = 0; i < _built.Count; i++)
        {
            var title = Tabs[i].Title + (_built[i].Badge(sim) is { } badge ? $" ({badge})" : "");
            if (_tabs.GetTabTitle(i) != title) _tabs.SetTabTitle(i, title);
        }
        if (_tabs.CurrentTab >= 0 && _tabs.CurrentTab < _built.Count) _built[_tabs.CurrentTab].Refresh(sim);
    }

    internal static string When(ColonySim sim, long tick)
    {
        var tpd = sim.Data.Rules.TicksPerDay;
        var left = (int)Math.Max(0, tick - sim.State.Tick);
        return $"in {ColonyText.Duration(left, sim.Data.Rules)} (day {tick / tpd + 1}, {tick % tpd:00}:00)";
    }

    /// <summary>"+10 Scrap" chips for what the colony gets.</summary>
    internal static HFlowContainer GainChips(ColonySim sim, IReadOnlyDictionary<string, double> amounts)
    {
        var flow = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        foreach (var (id, amount) in amounts)
            flow.AddChild(Widgets.Chip(Icons.Resource(id, 16), $"+{Fmt.Amount(amount)}", Palette.Good,
                $"{sim.Data.Resources.GetValueOrDefault(id)?.Name ?? id}: +{Fmt.Amount(amount)}", Palette.Good));
        if (amounts.Count == 0) flow.AddChild(Widgets.Label("nothing", "Muted"));
        return flow;
    }

    internal static HFlowContainer CostChips(ColonySim sim, IReadOnlyDictionary<string, double> cost) =>
        Widgets.CostChips(cost.Select(kv => (kv.Key, sim.Data.Resources.GetValueOrDefault(kv.Key)?.Name ?? kv.Key, kv.Value, sim.State.Stockpile.Get(kv.Key))));
}

/// <summary>One tab of the <see cref="RadioPanel"/>: a status line refreshed every time, a body rebuilt when its signature changes.</summary>
public abstract partial class RadioTab : VBoxContainer
{
    protected readonly ColonySession Session;
    protected readonly RadioPanel Panel;
    protected readonly Label Status = Widgets.Wrapped("", "Small");
    protected readonly VBoxContainer Body = Widgets.Column(6);
    private string _signature = "";

    protected RadioTab(ColonySession session, RadioPanel panel)
    {
        Session = session;
        Panel = panel;
        AddThemeConstantOverride("separation", 6);
        AddChild(Status);
        AddChild(Body);
    }

    protected RadioTab() : this(null!, null!) { }

    public void Invalidate() => _signature = "";

    /// <summary>A count for the tab title ("Calls (2)"), or null.</summary>
    public virtual string? Badge(ColonySim sim) => null;

    public void Refresh(ColonySim sim)
    {
        var status = StatusText(sim);
        if (Status.Text != status) Status.Text = status;
        if (Status.Visible != status.Length > 0) Status.Visible = status.Length > 0;
        var signature = Signature(sim);
        if (signature != _signature)
        {
            _signature = signature;
            Widgets.Clear(Body);
            Build(sim);
            var index = 0;
            foreach (var child in Body.GetChildren())
                if (child is PanelContainer card) Motion.Enter(card, index++);
        }
        Update(sim);
    }

    /// <summary>Every refresh, after any rebuild: values that change with time (a fade countdown) update in place here.</summary>
    protected virtual void Update(ColonySim sim) { }

    /// <summary>An action button the keyboard can reach: Primary for the main action, "" (secondary) for the alternative.</summary>
    protected static Button Action(string icon, string text, string tooltip, System.Action pressed, string variation = "")
    {
        var button = Widgets.IconButton(icon, text, tooltip, pressed, variation: variation);
        button.FocusMode = FocusModeEnum.All;
        button.CustomMinimumSize = new Vector2(0, 30);
        return button;
    }

    protected abstract string StatusText(ColonySim sim);
    protected abstract string Signature(ColonySim sim);
    protected abstract void Build(ColonySim sim);

    protected static PanelContainer Card(string variation = "Card") => new() { ThemeTypeVariation = variation };

    protected static Label Bold(string text, Color? color = null, int size = 14)
    {
        var label = Widgets.Label(text, "", color ?? Palette.Text, size);
        label.AddThemeFontOverride("font", Fonts.BodyBold);
        label.ClipText = true;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }
}

/// <summary>The call queue.</summary>
public partial class CallsTab : RadioTab
{
    private static readonly StringName Urgent = "urgent";
    private readonly List<(PanelContainer Chip, Label Text, long Expires)> _fades = new();

    public CallsTab(ColonySession session, RadioPanel panel) : base(session, panel) { }

    public CallsTab() { }

    protected override void Update(ColonySim sim)
    {
        foreach (var (chip, text, expires) in _fades)
        {
            var left = expires - sim.State.Tick;
            var label = $"Fades out {RadioPanel.When(sim, expires)}";
            if (text.Text != label) text.Text = label;
            // Under a day left: the key urgency reads as an amber chip, not grey small print.
            var urgent = left < sim.Data.Rules.TicksPerDay;
            if (chip.HasMeta(Urgent) && chip.GetMeta(Urgent).AsBool() == urgent) continue;
            chip.SetMeta(Urgent, urgent);
            if (urgent) chip.RemoveThemeStyleboxOverride("panel");
            else chip.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            text.AddThemeColorOverride("font_color", urgent ? Palette.Amber : Palette.TextMuted);
        }
    }

    public override string? Badge(ColonySim sim) => sim.State.Radio.Calls.Count > 0 ? sim.State.Radio.Calls.Count.ToString() : null;

    protected override string StatusText(ColonySim sim) =>
        sim.State.Radio.Calls.Count == 0 ? "" : "Calls wait until answered or until they fade out.";

    protected override string Signature(ColonySim sim) => string.Join(",", sim.State.Radio.Calls.Select(c => c.Id));

    protected override void Build(ColonySim sim)
    {
        _fades.Clear();
        var calls = RadioMast.Calls(sim);
        if (calls.Count == 0)
        {
            Body.AddChild(Widgets.Wrapped("The radio is quiet. Traders, contract offers and other voices call in here.", "Muted"));
            return;
        }
        foreach (var call in calls)
        {
            var card = Card();
            var row = Widgets.Row(8);
            card.AddChild(row);
            var (icon, accent) = call.Kind switch
            {
                RadioCallKind.Trader => ("ui_trade", Palette.Amber),
                RadioCallKind.Contract => ("ui_contract", Palette.Amber),
                RadioCallKind.Story => ("stat_pop", Palette.Rust),
                _ => ("ui_radio", Palette.TextMuted),
            };
            var iconRect = Icons.Rect(Icons.Get(icon, 20), 20, accent);
            iconRect.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            row.AddChild(iconRect);
            var col = Widgets.Column(2);
            col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var head = Widgets.Row(6);
            head.AddChild(Bold(call.CallerName));
            var tpd = sim.Data.Rules.TicksPerDay;
            head.AddChild(Widgets.Label($"D{call.Tick / tpd + 1} {call.Tick % tpd:00}h", "Small", Palette.TextDim));
            col.AddChild(head);
            col.AddChild(Widgets.Wrapped(call.Text, "Small", Palette.Text));
            if (call.ExpiresTick is { } expires)
            {
                var chip = new PanelContainer { ThemeTypeVariation = "Chip", SizeFlagsHorizontal = SizeFlags.ShrinkBegin, TooltipText = "The call leaves the queue then, unanswered." };
                var fade = Widgets.Label("", "Small", Palette.TextMuted);
                chip.AddChild(fade);
                col.AddChild(chip);
                _fades.Add((chip, fade, expires));
            }
            var buttons = Widgets.Row(4);
            var id = call.Id;
            switch (call.Kind)
            {
                case RadioCallKind.Trader:
                    buttons.AddChild(Action("ui_trade", "Answer", "Talk to the trader: opens the Trader tab.", () =>
                    {
                        Session.Execute(new AnswerCall(id));
                        Panel.ShowTab("Trader");
                    }, variation: "Primary"));
                    break;
                case RadioCallKind.Contract when int.TryParse(call.Tag, out var contract):
                    buttons.AddChild(Action("stat_check", "Accept", "Take the contract (see the Contracts tab).", () =>
                    {
                        Session.Execute(new AcceptContract(contract));
                        Panel.ShowTab("Contracts");
                    }, variation: "Primary"));
                    buttons.AddChild(Action("ui_close", "Decline", "Let it go: the call leaves the queue.", () => Session.Execute(new AnswerCall(id))));
                    break;
                default:
                    buttons.AddChild(Action("ui_radio", "Answer", "Answer the call.", () => Session.Execute(new AnswerCall(id)), "Primary"));
                    break;
            }
            col.AddChild(buttons);
            row.AddChild(col);
            Body.AddChild(card);
        }
    }
}

/// <summary>The trader screen.</summary>
public partial class TraderTab : RadioTab
{
    public TraderTab(ColonySession session, RadioPanel panel) : base(session, panel) { }

    public TraderTab() { }

    public override string? Badge(ColonySim sim) => sim.State.Radio.TraderHere ? "here" : null;

    protected override string StatusText(ColonySim sim)
    {
        var radio = sim.State.Radio;
        var name = RadioMast.Trader(sim)?.Name ?? "The trader";
        if (radio.TraderLeavesTick is { } leaves) return $"{name} leaves {RadioPanel.When(sim, leaves)}.";
        if (!RadioMast.TraderAvailable(sim))
            return RadioMast.Trader(sim)?.RequiresScouted is { } zone
                ? $"No trader comes yet: they visit once {sim.Scavenge?.Zones.GetValueOrDefault(zone)?.Name ?? zone} is scouted."
                : "No trader comes yet.";
        return radio.NextVisitTick > 0 ? $"{name} comes back {RadioPanel.When(sim, radio.NextVisitTick)}." : "The trader's next visit is not set yet.";
    }

    protected override string Signature(ColonySim sim)
    {
        var radio = sim.State.Radio;
        var stock = string.Join(",", radio.Stock);
        var items = string.Join(",", SellableItems(sim).Select(id => $"{id}:{sim.State.Inventory.Count(id)}"));
        var money = string.Join(",", sim.Data.Resources.Keys.Select(r => Math.Floor(sim.State.Stockpile.Get(r))));
        return $"{radio.TraderHere}|{radio.TraderId}|{stock}|{items}|{money}|{string.Join(",", sim.State.Schematics.Known.Count)}";
    }

    private static List<string> SellableItems(ColonySim sim) =>
        sim.State.Inventory.Counts.Keys.Where(id => sim.Data.Schematics.Find(id) is not null).OrderBy(id => id, StringComparer.Ordinal).ToList();

    protected override void Build(ColonySim sim)
    {
        var trader = RadioMast.Trader(sim);
        var radio = sim.State.Radio;
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get("ui_trade", 22), 22, Palette.Amber));
        head.AddChild(Bold(trader?.Name ?? "No trader", size: 16));
        head.AddChild(Widgets.Chip(null, radio.TraderHere ? "PARKED OUTSIDE" : "AWAY", radio.TraderHere ? Palette.Good : Palette.TextDim));
        Body.AddChild(head);
        if (!radio.TraderHere)
        {
            Body.AddChild(Widgets.Wrapped("When the trader parks outside, they call on the radio. Buy schematics you do not know, "
                + "sell copies, chips and Research Data.", "Muted"));
            return;
        }
        if (trader?.Greeting is { Length: > 0 } greeting) Body.AddChild(Widgets.Wrapped($"“{greeting}”", "Muted"));

        Body.AddChild(PanelKit.Header("For sale", "item_schematic", "trading"));
        if (radio.Stock.Count == 0) Body.AddChild(Widgets.Label("Nothing you do not know already.", "Muted"));
        foreach (var id in radio.Stock)
            if (sim.Data.Schematics.Find(id) is { } def) Body.AddChild(Offer(sim, def));

        Body.AddChild(PanelKit.Header("Sell", "ui_trade", "trading"));
        var items = SellableItems(sim);
        foreach (var id in items)
        {
            var def = sim.Data.Schematics.All[id];
            var card = Card();
            var col = Widgets.Column(4);
            card.AddChild(col);
            var top = Widgets.Row(6);
            top.AddChild(Icons.Rect(Icons.Get(SchematicText.Icon(def), 18), 18, SchematicText.Accent(def)));
            var known = Schematics.Has(sim, id) ? "copy" : def.Encrypted ? "chip" : "plan";
            top.AddChild(Bold($"{def.Name} ({known}) ×{sim.State.Inventory.Count(id)}"));
            col.AddChild(top);
            var bottom = Widgets.Row(6);
            var gains = RadioPanel.GainChips(sim, RadioMast.SellPrice(sim, def));
            gains.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            bottom.AddChild(gains);
            bottom.AddChild(Action("ui_trade", "Sell", Schematics.Has(sim, id) ? "Sell this copy." : "Sell it: the colony does not learn it.",
                () => Session.Execute(new SellToTrader(ItemId: id))));
            col.AddChild(bottom);
            card.TooltipText = SchematicText.Tooltip(sim, def);
            Body.AddChild(card);
        }

        var data = SchematicText.ResearchData(sim);
        var dataCard = Card();
        var dataCol = Widgets.Column(4);
        dataCard.AddChild(dataCol);
        var dataTop = Widgets.Row(6);
        dataTop.AddChild(Icons.Rect(Icons.Resource(SchematicText.ResearchDataId(sim), 18), 18, Palette.Amber));
        dataTop.AddChild(Bold($"Research Data (have {Fmt.Amount(data)})"));
        dataCol.AddChild(dataTop);
        var dataRow = Widgets.Row(6);
        var per = RadioPanel.GainChips(sim, sim.Data.Schematics.Rules.Radio.ResearchDataPrice);
        per.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        per.TooltipText = "Per unit";
        dataRow.AddChild(per);
        foreach (var n in new[] { 1, 5 })
        {
            var sell = Widgets.TextButton($"Sell ×{n}", () => Session.Execute(new SellToTrader(ResearchData: n)),
                $"Sell {n} Research Data.", "Flat");
            sell.Disabled = data + 1e-9 < n;
            sell.FocusMode = FocusModeEnum.All;
            dataRow.AddChild(sell);
        }
        dataCol.AddChild(dataRow);
        Body.AddChild(dataCard);
        if (items.Count == 0) Body.AddChild(Widgets.Label("No schematic copies or chips in the stores to sell.", "Small"));
    }

    /// <summary>The price option picked per schematic (survives the tab's rebuilds).</summary>
    private readonly Dictionary<string, int> _choice = new();

    /// <summary>
    /// One card per schematic for sale: what it is, "Pay with" as a segmented choice of the trader's price options (each a
    /// row of cost chips whose tooltips give the colony's stock), and one Buy for the picked option.
    /// </summary>
    private Control Offer(ColonySim sim, SchematicDef def)
    {
        var card = Card();
        card.TooltipText = SchematicText.Tooltip(sim, def);
        var col = Widgets.Column(4);
        card.AddChild(col);
        var top = Widgets.Row(6);
        top.AddChild(Icons.Rect(Icons.Get(SchematicText.Icon(def), 20), 20, SchematicText.Accent(def)));
        top.AddChild(Bold(def.Name, size: 15));
        top.AddChild(Widgets.Pips(def.Tier, 4, SchematicText.Accent(def)));
        col.AddChild(top);
        col.AddChild(Widgets.Wrapped($"Tier {def.Tier}{SchematicText.Arrow(sim, def)}" + (def.Encrypted ? " · encrypted: decrypt at the Electronics Lab" : ""), "Small",
            def.Encrypted ? Palette.Network.Lerp(Palette.TextMuted, 0.3f) : Palette.TextMuted));
        var prices = RadioMast.Prices(sim, def);
        if (prices.Count == 0) return card;
        if (!_choice.TryGetValue(def.Id, out var picked) || picked >= prices.Count)
        {
            picked = Enumerable.Range(0, prices.Count).FirstOrDefault(i => sim.State.Stockpile.CanAfford(prices[i].Cost), 0);
            _choice[def.Id] = picked;
        }
        col.AddChild(Widgets.Label(prices.Count > 1 ? "PAY WITH (pick one)" : "PRICE", "Heading", Palette.TextMuted));
        var options = new HFlowContainer();
        options.AddThemeConstantOverride("h_separation", 4);
        options.AddThemeConstantOverride("v_separation", 4);
        col.AddChild(options);
        var segments = new List<PanelContainer>();
        var buyRow = Widgets.Row(6);
        var worth = Widgets.Label("", "Small", Palette.TextMuted);
        worth.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buyRow.AddChild(worth);
        var buy = Action("ui_trade", "Buy", "", () => Session.Execute(new BuyFromTrader(def.Id, _choice[def.Id])), "Primary");
        buy.CustomMinimumSize = new Vector2(84, 30);
        buyRow.AddChild(buy);
        void Pick(int option)
        {
            _choice[def.Id] = option;
            for (var i = 0; i < segments.Count; i++) segments[i].ThemeTypeVariation = i == option ? "CardSelected" : "Inset";
            var cost = prices[option].Cost;
            var afford = sim.State.Stockpile.CanAfford(cost);
            buy.Disabled = !afford;
            buy.TooltipText = afford ? "Buy it with the picked option." : "Not enough of what is marked with ! (hover a chip for the stock).";
            worth.Text = $"worth about {TradeValues.Of(sim.Data, cost):0} scrap" + (afford ? "" : " · short");
            worth.AddThemeColorOverride("font_color", afford ? Palette.TextMuted : Palette.Danger);
        }
        for (var i = 0; i < prices.Count; i++)
        {
            var option = i;
            if (i > 0) options.AddChild(Widgets.Label("or", "Small", Palette.TextDim));
            var segment = new PanelContainer
            {
                ThemeTypeVariation = "Inset",
                FocusMode = FocusModeEnum.All,
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                TooltipText = prices.Count > 1 ? "Pay with this option" : "",
            };
            // One row per option (a flow inside a segment would shrink to a chip per line).
            var flow = RadioPanel.CostChips(sim, prices[i].Cost);
            var chips = Widgets.Row(4);
            chips.MouseFilter = MouseFilterEnum.Pass;
            foreach (var chip in flow.GetChildren())
            {
                flow.RemoveChild(chip);
                chips.AddChild(chip);
            }
            flow.Free();
            segment.AddChild(chips);
            segment.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                    or InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Space })
                {
                    Pick(option);
                    segment.AcceptEvent();
                }
            };
            segment.FocusEntered += () => segment.SelfModulate = new Color(1.25f, 1.15f, 0.95f);
            segment.FocusExited += () => segment.SelfModulate = Colors.White;
            segments.Add(segment);
            options.AddChild(segment);
        }
        col.AddChild(buyRow);
        Pick(picked);
        return card;
    }
}

/// <summary>The radio contract on offer or taken.</summary>
public partial class ContractsTab : RadioTab
{
    public ContractsTab(ColonySession session, RadioPanel panel) : base(session, panel) { }

    public ContractsTab() { }

    public override string? Badge(ColonySim sim) => sim.State.Radio.Contract is { Accepted: false } ? "1" : null;

    protected override string StatusText(ColonySim sim)
    {
        var radio = sim.State.Radio;
        if (radio.Contract is { } c) return $"Due {RadioPanel.When(sim, c.DueTick)}.";
        return radio.NextContractTick > sim.State.Tick ? $"Next offer {RadioPanel.When(sim, radio.NextContractTick)}." : "";
    }

    protected override string Signature(ColonySim sim) =>
        sim.State.Radio.Contract is { } c ? $"{c.Id}:{c.Accepted}:{sim.State.Radio.ContractsDone}" : $"none:{sim.State.Radio.ContractsDone}";

    private static HBoxContainer InfoTip_(Label text) => Hud.Help.InfoTip.Beside(text, "expeditions");

    protected override void Build(ColonySim sim)
    {
        var radio = sim.State.Radio;
        if (radio.Contract is not { } contract)
        {
            Body.AddChild(Widgets.Wrapped("No contract on offer. Every few days a voice on the radio offers one: bring a party home "
                + "from a named zone before the due day, and it pays.", "Muted"));
        }
        else
        {
            var card = Card(contract.Accepted ? "CardSelected" : "Card");
            var col = Widgets.Column(4);
            card.AddChild(col);
            var top = Widgets.Row(6);
            top.AddChild(Icons.Rect(Icons.Get("ui_contract", 20), 20, Palette.Amber));
            var zone = sim.Scavenge?.Zones.GetValueOrDefault(contract.ZoneId);
            top.AddChild(Bold($"Run {zone?.Name ?? contract.ZoneId}", size: 15));
            top.AddChild(Widgets.Chip(null, contract.Accepted ? "TAKEN" : "OFFERED", contract.Accepted ? Palette.Good : Palette.Amber));
            col.AddChild(top);
            var tpd = sim.Data.Rules.TicksPerDay;
            col.AddChild(InfoTip_(Widgets.Wrapped($"Bring a party home from {zone?.Name ?? contract.ZoneId}" + (zone is null ? "" : $" (threat {zone.Threat})")
                + $" by day {contract.DueTick / tpd + 1}.", "Small", Palette.Text)));
            col.AddChild(Widgets.Label("REWARD", "Heading"));
            col.AddChild(RadioPanel.GainChips(sim, contract.Reward));
            if (!contract.Accepted)
            {
                var id = contract.Id;
                col.AddChild(Action("stat_check", "Accept", "Take the contract.", () => Session.Execute(new AcceptContract(id)), "Primary"));
            }
            Body.AddChild(card);
        }
        Body.AddChild(Widgets.Label($"Contracts done: {radio.ContractsDone}", "Small"));
    }
}
