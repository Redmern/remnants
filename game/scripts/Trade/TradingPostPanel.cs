using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Trade;
using Remnants.Game.Colony;
using Remnants.Game.Hud;
using Remnants.Game.Multiplayer;
using Remnants.Net;

namespace Remnants.Game.Trade;

/// <summary>
/// The Trading Post at the Radio Mast (separate from the NPC trader): an async barter market on a persistent server
/// (<c>Remnants.Server --market</c>). Tabs: <b>Browse</b> (filter, take a listing), <b>Post</b> (give X for Y),
/// <b>My offers</b> (cancel) and <b>Claims</b> (what came in, what is still in escrow or on the road). Goods leave the
/// colony when posted or taken and come back as claims, applied once each and saved before they are acked.
/// </summary>
public partial class TradingPostPanel : Overlay
{
    public const string IdentityPath = "user://trade_identity.json";
    public const string DefaultAddress = "127.0.0.1:7780";

    private readonly ColonySession _session;
    private readonly LineEdit _address = new() { Text = DefaultAddress, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly LineEdit _name = new() { MaxLength = 24, CustomMinimumSize = new Vector2(150, 0) };
    private readonly Button _connect;
    private readonly Label _status = Widgets.Wrapped("", "Muted");
    private readonly Dictionary<string, Button> _tabs = new();
    private readonly Dictionary<string, VBoxContainer> _pages = new();
    private readonly LineEdit _filter = new() { PlaceholderText = "Filter: wood, rifle, a seller…", SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly VBoxContainer _browse = Widgets.Column(6);
    private readonly VBoxContainer _mine = Widgets.Column(6);
    private readonly VBoxContainer _claims = Widgets.Column(6);
    private readonly GoodPicker _givePicker = new();
    private readonly GoodPicker _wantPicker = new();
    private readonly VBoxContainer _draft = Widgets.Column(6);
    private readonly OptionButton _hours = new();
    private readonly List<TradeGood> _give = new();
    private readonly List<TradeGood> _want = new();
    private MarketClient? _market;
    private string _tab = "Browse";

    public TradingPostPanel() : this(null!) { }

    public TradingPostPanel(ColonySession session) : base(860)
    {
        _session = session;
        Visible = false;
        Body.AddChild(Title("TRADING POST"));
        var subtitle = Widgets.Wrapped("Barter with other colonies over the Radio Mast. Posted goods are held by the Trading Post until someone takes " +
                                       "the offer, you cancel it, or it expires; everything owed to you waits here as a claim.", "Muted");
        Body.AddChild(subtitle);

        var connection = Widgets.Row(6);
        Body.AddChild(connection);
        connection.AddChild(Widgets.Label("Name"));
        connection.AddChild(_name);
        connection.AddChild(Widgets.Label("Server"));
        connection.AddChild(_address);
        _connect = Widgets.IconButton("ui_continue", "Connect", "Connect to the Trading Post server", () => _ = Connect(), variation: "Primary");
        connection.AddChild(_connect);
        Body.AddChild(_status);

        var tabs = Widgets.Row(4);
        Body.AddChild(tabs);
        foreach (var (tab, icon) in new[] { ("Browse", "ui_filter"), ("Post", "ui_post"), ("My offers", "ui_trade"), ("Claims", "stat_loot") })
        {
            var t = tab;
            var button = Widgets.IconButton(icon, tab, tab, () => ShowTab(t), variation: "Tab");
            button.ToggleMode = true;
            _tabs[tab] = button;
            tabs.AddChild(button);
        }

        var pageHost = new PanelContainer { CustomMinimumSize = new Vector2(0, 360) };
        pageHost.AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Inset, Palette.EdgeDark, 0.03f), 12, 10));
        Body.AddChild(pageHost);
        var stack = new VBoxContainer();
        pageHost.AddChild(stack);

        var browse = Page("Browse", stack);
        var filterRow = Widgets.Row(6);
        filterRow.AddChild(_filter);
        filterRow.AddChild(Widgets.IconButton("ui_filter", "Search", "Search the listings", () => _ = Browse()));
        _filter.TextSubmitted += text => { _ = Browse(); };
        browse.AddChild(filterRow);
        browse.AddChild(Scroll(_browse));

        var post = Page("Post", stack);
        post.AddChild(Widgets.SectionHeader("You give (leaves the colony now)", "ui_trade"));
        post.AddChild(_givePicker);
        post.AddChild(Widgets.SectionHeader("You want", "stat_loot"));
        post.AddChild(_wantPicker);
        post.AddChild(_draft);
        var postRow = Widgets.Row(8);
        foreach (var h in new[] { 24, 72, 168 }) _hours.AddItem($"{h} hours");
        _hours.Select(1);
        postRow.AddChild(Widgets.Label("Lasts"));
        postRow.AddChild(_hours);
        postRow.AddChild(Widgets.Spacer());
        postRow.AddChild(Widgets.IconButton("ui_close", "Clear", "Start over", () => { _give.Clear(); _want.Clear(); Redraw(); }));
        postRow.AddChild(Widgets.IconButton("ui_post", "Post offer", "List it on the Trading Post", () => _ = Post(), variation: "Primary"));
        post.AddChild(postRow);
        _givePicker.Picked += g => { _give.Add(g); Normalize(_give); Redraw(); };
        _wantPicker.Picked += g => { _want.Add(g); Normalize(_want); Redraw(); };

        Page("My offers", stack).AddChild(Scroll(_mine));
        Page("Claims", stack).AddChild(Scroll(_claims));
        ShowTab("Browse");
    }

    public bool Online => _market is { Connected: true, Welcome: not null };

    /// <summary>Opens the panel; <paramref name="address"/> connects at once.</summary>
    public void Open(string? address)
    {
        Open();
        if (_name.Text.Length == 0) _name.Text = Identity(DefaultName()).Name;
        if (address is { Length: > 0 })
        {
            _address.Text = address;
            if (!Online) _ = Connect();
        }
        Redraw();
    }

    public void ShowTab(string tab)
    {
        _tab = tab;
        foreach (var (name, button) in _tabs) button.SetPressedNoSignal(name == tab);
        foreach (var (name, page) in _pages) page.Visible = name == tab;
        if (tab is "Browse" or "My offers" && Online) _ = Browse();
        Redraw();
    }

    public override void _ExitTree() => _market?.Close();

    private VBoxContainer Page(string name, VBoxContainer stack)
    {
        var page = Widgets.Column(8);
        page.SizeFlagsVertical = SizeFlags.ExpandFill;
        _pages[name] = page;
        stack.AddChild(page);
        return page;
    }

    private static ScrollContainer Scroll(Control content)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 300), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
        return scroll;
    }

    private static string DefaultName() => System.Environment.UserName is { Length: > 0 } u ? u : "Survivor";

    private static TradeIdentity Identity(string name) => TradeIdentity.LoadOrCreate(ProjectSettings.GlobalizePath(IdentityPath), name);

    private async Task Connect()
    {
        _market?.Close();
        _market = null;
        var (host, port) = MultiplayerSession.ParseAddress(_address.Text);
        _status.Text = $"Connecting to {host}:{port}…";
        _connect.Disabled = true;
        NetPump.Ensure(GetTree());
        try
        {
            var transport = await TcpClientTransport.ConnectAsync(host, port);
            var market = new MarketClient(transport, () => _session.Sim, NetPump.Post);
            var listed = false;
            market.Changed += () =>
            {
                // First listing as soon as the server has said hello.
                if (!listed && market.Welcome is not null)
                {
                    listed = true;
                    _ = Browse();
                }
                Redraw();
            };
            market.ColonyChanged += () => _session.Save();
            _market = market;
            transport.Start();
            market.Hello(Identity(_name.Text.Trim().Length > 0 ? _name.Text.Trim() : DefaultName()));
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or ArgumentException or OperationCanceledException)
        {
            _status.Text = $"Could not reach the Trading Post at {host}:{port}: {e.Message}";
        }
        _connect.Disabled = false;
    }

    private async Task Browse()
    {
        if (_market is not { Welcome: not null } market) return;
        try
        {
            await market.Browse(new MarketFilter(Text: _filter.Text.Trim().Length > 0 ? _filter.Text.Trim() : null));
        }
        catch (TaskCanceledException) { }
        Redraw();
    }

    private async Task Post()
    {
        if (_market is not { Welcome: not null } market || _give.Count == 0 || _want.Count == 0) return;
        var hours = new[] { 24, 72, 168 }[Math.Max(0, _hours.Selected)];
        var result = await market.Post(_give.ToList(), _want.ToList(), hours);
        _status.Text = result.Ok ? $"Posted: {result.Message}" : $"Not posted: {result.Message}";
        if (result.Ok)
        {
            _give.Clear();
            _want.Clear();
            ShowTab("My offers");
        }
        Redraw();
    }

    private async Task Accept(MarketListing listing)
    {
        if (_market is not { } market) return;
        var result = await market.Accept(listing);
        _status.Text = result.Ok ? $"{result.Message} The goods are in your stores." : $"Could not take it: {result.Message}";
        await Browse();
    }

    private async Task CancelListing(MarketListing listing)
    {
        if (_market is not { } market) return;
        var result = await market.Cancel(listing.Id);
        _status.Text = result.Message;
        await Browse();
    }

    private static void Normalize(List<TradeGood> goods)
    {
        var merged = TradeGoods.Normalize(goods);
        goods.Clear();
        goods.AddRange(merged);
    }

    private void Redraw()
    {
        if (!IsInsideTree() || _session is null) return;
        var sim = _session.Sim;
        var market = _market;
        _status.Text = market switch
        {
            null when _status.Text.Length > 0 => _status.Text,
            null => "Not connected. Run a Trading Post (Remnants.Server --market market.json) and connect.",
            { Error: { } error } => error,
            { Connected: false } => "Disconnected. Connect again; requests still waiting are sent again.",
            { Welcome: null } => "Saying hello…",
            { Welcome: { } w } when !_status.Text.StartsWith("Posted") && !_status.Text.StartsWith("Could") && !_status.Text.StartsWith("Not posted") && !_status.Text.Contains("stores") =>
                $"Connected as {w.Name} · market id {w.PlayerId} · up to {w.MaxOpenListings} open offers, {w.ListingHours:0} h each.",
            _ => _status.Text,
        };

        Widgets.Clear(_browse);
        var listings = market?.Listings?.Listings ?? new();
        var others = listings.Where(l => !l.Mine).ToList();
        if (others.Count == 0) _browse.AddChild(Widgets.Label(market is null ? "Connect to see the offers." : "No offers match.", "Muted"));
        foreach (var l in others) _browse.AddChild(Row(l, TradeGoods.Has(sim.State, l.Want)
            ? Widgets.IconButton("stat_check", "Take", "Pay what they want; their goods come to you as a claim", () => _ = Accept(l), variation: "Primary")
            : Disabled("Take", "Your colony does not have what they want.")));

        Widgets.Clear(_mine);
        var mine = listings.Where(l => l.Mine).ToList();
        if (mine.Count == 0) _mine.AddChild(Widgets.Label("You have no open offers.", "Muted"));
        foreach (var l in mine) _mine.AddChild(Row(l, Widgets.IconButton("ui_close", "Cancel", "Withdraw it: the goods come back as a claim", () => _ = CancelListing(l))));

        Widgets.Clear(_draft);
        _draft.AddChild(Draft("Giving", _give));
        _draft.AddChild(Draft("Wanting", _want));
        var give = TradeUi.Value(_give);
        var want = TradeUi.Value(_want);
        var hint = Colony.Ui.Ui.Rich();
        hint.Text = $"Reference: you give ≈ {give:0} scrap, you ask ≈ {want:0} scrap. {TradeUi.Fairness(give, want)}";
        _draft.AddChild(hint);
        var state = sim.State;
        _givePicker.SetChoices(sim.Data.Resources.Keys.Where(id => state.Stockpile.Get(id) >= 1).OrderBy(id => sim.Data.Resources[id].Name)
            .Select(id => (TradeGood.Resource(id, 1), (int)Math.Floor(state.Stockpile.Get(id) + 1e-9) - _give.Where(g => g.Id == id).Sum(g => g.Count)))
            .Concat(state.Inventory.Counts.Select(kv => (TradeGood.Item(kv.Key), kv.Value - _give.Where(g => g.Id == kv.Key).Sum(g => g.Count)))));
        var items = GameDataPaths.LoadScavenge().Items.Values.Where(i => !i.Natural && i.LootWeight > 0 || i.Kind == Core.Items.ItemKind.Blueprint)
            .OrderBy(i => i.Name).Select(i => (TradeGood.Item(i.Id), TradeUi.Rules.Limits.MaxItemCount));
        _wantPicker.SetChoices(sim.Data.Resources.Keys.OrderBy(id => sim.Data.Resources[id].Name)
            .Select(id => (TradeGood.Resource(id, 1), TradeUi.Rules.Limits.MaxResourceAmount)).Concat(items));

        Widgets.Clear(_claims);
        var trade = state.Trade;
        _claims.AddChild(Widgets.SectionHeader("Collected this session", "stat_loot"));
        if (market is null || market.Received.Count == 0) _claims.AddChild(Widgets.Label("Nothing yet. Claims are collected as soon as you connect.", "Muted"));
        else foreach (var c in Enumerable.Reverse(market.Received)) _claims.AddChild(Line(c.Reason, c.Goods));
        _claims.AddChild(Widgets.SectionHeader("Waiting for the server", "stat_time"));
        var pending = trade.Escrows.Where(e => e.Market is not null).ToList();
        if (pending.Count == 0) _claims.AddChild(Widgets.Label("Nothing in escrow.", "Muted"));
        foreach (var e in pending) _claims.AddChild(Line(e.Purpose, e.Goods));
        _claims.AddChild(Widgets.SectionHeader("Couriers on the road", "ui_courier"));
        if (trade.Deliveries.Count == 0) _claims.AddChild(Widgets.Label("No couriers on the way.", "Muted"));
        foreach (var d in trade.Deliveries)
            _claims.AddChild(Line($"From {d.From}, arriving in {Math.Max(0, d.ArriveTick - state.Tick) / (double)sim.Data.Rules.TicksPerDay:0.#} days", d.Goods));
    }

    private static Button Disabled(string text, string why)
    {
        var button = Widgets.IconButton("stat_lock", text, why, () => { });
        button.Disabled = true;
        return button;
    }

    private Control Draft(string label, List<TradeGood> goods)
    {
        var row = Widgets.Row(8);
        var title = Widgets.Label(label.ToUpperInvariant(), "Heading");
        title.CustomMinimumSize = new Vector2(80, 0);
        row.AddChild(title);
        row.AddChild(TradeUi.Chips(goods, g => () =>
        {
            goods.RemoveAll(x => x.Key == g.Key);
            Callable.From(Redraw).CallDeferred();
        }, "add goods above"));
        return row;
    }

    private static Control Line(string text, List<TradeGood> goods)
    {
        var row = Widgets.Row(8);
        var label = Widgets.Label(text, color: Palette.TextMuted, size: 13);
        label.CustomMinimumSize = new Vector2(260, 0);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        row.AddChild(TradeUi.Chips(goods));
        return row;
    }

    private static Control Row(MarketListing l, Control action)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Raised, Palette.EdgeDark, 0.03f), 10, 8));
        var row = Widgets.Row(10);
        panel.AddChild(row);
        var who = Widgets.Column(2);
        who.CustomMinimumSize = new Vector2(120, 0);
        who.AddChild(Widgets.Label(l.Seller, color: l.Mine ? Palette.Amber : Palette.Text, size: 15));
        var left = l.Expires - DateTimeOffset.UtcNow;
        who.AddChild(Widgets.Label(left.TotalHours >= 1 ? $"#{l.Id} · {left.TotalHours:0} h left" : $"#{l.Id} · {Math.Max(0, left.TotalMinutes):0} min left", "Small", Palette.TextMuted));
        row.AddChild(who);
        var gives = Widgets.Column(2);
        gives.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        gives.AddChild(TradeUi.Tag($"gives · ≈ {TradeUi.Value(l.Give):0}", Palette.TextMuted));
        gives.AddChild(TradeUi.Chips(l.Give));
        row.AddChild(gives);
        row.AddChild(Icons.Rect(Icons.Get("ui_trade", 20), 20, Palette.Rust));
        var wants = Widgets.Column(2);
        wants.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        wants.AddChild(TradeUi.Tag($"wants · ≈ {TradeUi.Value(l.Want):0}", Palette.TextMuted));
        wants.AddChild(TradeUi.Chips(l.Want));
        row.AddChild(wants);
        row.AddChild(action);
        return panel;
    }
}
