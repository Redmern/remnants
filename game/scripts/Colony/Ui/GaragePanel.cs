using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The Garage (opens with the Garage selected), one task per tab. <b>Vehicles</b>: the colony's vehicles down the left
/// with their condition (Up / Down move between them), the picked one turning in 3D with its four key numbers (speed,
/// cargo, fuel a day, condition) and the rest behind "More". <b>Upgrades</b>: the slots (engine, tyres, armor, utility)
/// with each fitted upgrade as a chip (quality, condition bar, take off), one picker per free slot (optionally only what
/// the stores have parts for), the considered upgrade's before → after numbers, its cost and why it cannot be fitted yet.
/// <b>Repairs</b>: every damaged vehicle with the spare parts it needs. <b>Build</b>: the Garage's production queue
/// (vehicles and parts, <see cref="ProductionPanel"/>). Each part keeps its rows and refreshes on its own signature, so
/// a production tick never rebuilds the list or closes an open picker. Core decides.
/// </summary>
public partial class GaragePanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Unchanged _set = new();
    private readonly Label _title = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly PanelTabs _tabs = new();

    // Vehicles tab.
    private readonly VBoxContainer _list = Widgets.Column(4);
    private readonly KeyedRows<int> _listRows;
    private readonly Dictionary<int, (PanelContainer Card, TextureRect Icon, StatBar Bar, Label Note)> _cards = new();
    private readonly Label _noVehicles = Widgets.Wrapped("No vehicles yet. Build one under Build.", "Muted");
    private readonly VBoxContainer _detail = Widgets.Column(6);
    private readonly Label _detailHead = Widgets.Label("", "Heading");
    private readonly VehiclePreview _preview3d = new(new Vector2(420, 190));
    private readonly GridContainer _keyStats = new() { Columns = 4 };
    private readonly Expander _more = new("More stats");
    private readonly GridContainer _moreStats = new() { Columns = 4 };
    private string _detailShown = "";

    // Upgrades tab.
    private readonly VBoxContainer _upgrades = Widgets.Column(6);
    private readonly Label _upgradesHead = Widgets.Label("", "Heading");
    private readonly OptionButton _vehiclePicker = PanelKit.Focusable(new OptionButton { ClipText = true, FitToLongestItem = false, CustomMinimumSize = new Vector2(200, 0) });
    private readonly CheckBox _onlyStocked = PanelKit.Focusable(new CheckBox { Text = "Only with parts in the stores", TooltipText = "Show only upgrades whose parts are already in the stores" });
    private readonly VBoxContainer _slots = Widgets.Column(6);
    private readonly VBoxContainer _fit = Widgets.Column(6);
    private readonly HFlowContainer _parts = new();
    private string _upgradesShown = "";
    private string _pickerShown = "";

    // Repairs tab.
    private readonly VBoxContainer _repairs = Widgets.Column(6);
    private string _repairsShown = "";

    private readonly ProductionPanel _build;
    private int? _vehicleId;
    private string? _preview;

    public int? BuildingId { get; private set; }

    public GaragePanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(720, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        var box = Ui.Panel(this, 8);
        box.AddChild(Ui.TitleRow("Garage", "bld_garage", () => Show(null)));
        box.AddChild(_title);
        box.AddChild(_tabs);

        // Vehicles: list | preview + key stats + more.
        var vehicles = Widgets.Row(12);
        var left = Widgets.Column(6);
        left.CustomMinimumSize = new Vector2(230, 0);
        left.AddChild(Widgets.SectionHeader("Vehicles", "ui_vehicle"));
        _list.AddChild(_noVehicles);
        _listRows = new KeyedRows<int>(_list, offset: 1);
        left.AddChild(_list);
        vehicles.AddChild(left);
        _detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var head = Widgets.Row(6);
        head.AddChild(Icons.Rect(Icons.Get("ui_vehicle", 14), 14, Palette.Rust));
        _detailHead.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _detailHead.ClipText = true;
        head.AddChild(_detailHead);
        head.AddChild(new InfoTip("vehicles"));
        _detail.AddChild(head);
        _detail.AddChild(_preview3d);
        _keyStats.AddThemeConstantOverride("h_separation", 12);
        _keyStats.AddThemeConstantOverride("v_separation", 4);
        _detail.AddChild(_keyStats);
        _moreStats.AddThemeConstantOverride("h_separation", 12);
        _moreStats.AddThemeConstantOverride("v_separation", 4);
        _more.Body.AddChild(_moreStats);
        _detail.AddChild(_more);
        vehicles.AddChild(_detail);
        _tabs.Add("vehicles", "Vehicles", vehicles, "Your vehicles and their numbers");

        // Upgrades: vehicle, slots, the considered upgrade, parts in the stores.
        var top = Widgets.Row(8);
        _upgradesHead.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(_upgradesHead);
        top.AddChild(_vehiclePicker);
        _upgrades.AddChild(top);
        var filter = Widgets.Row(8);
        filter.AddChild(_onlyStocked);
        filter.AddChild(Widgets.Spacer());
        filter.AddChild(PanelKit.Tip("Best parts first: their quality scales the effect", "vehicles"));
        _upgrades.AddChild(filter);
        _upgrades.AddChild(_slots);
        _upgrades.AddChild(_fit);
        _upgrades.AddChild(Widgets.SectionHeader("Parts in the stores", "item_material"));
        _parts.AddThemeConstantOverride("h_separation", 4);
        _parts.AddThemeConstantOverride("v_separation", 4);
        _upgrades.AddChild(_parts);
        _tabs.Add("upgrades", "Upgrades", _upgrades, "Fit and take off engine, tyre, armor and utility upgrades");
        _vehiclePicker.ItemSelected += i =>
        {
            var ids = Vehicles(_session.Sim).Select(v => v.Id).ToList();
            if (i >= 0 && i < ids.Count) Pick(ids[(int)i]);
        };
        _onlyStocked.Toggled += _ =>
        {
            _upgradesShown = "";
            Refresh();
        };

        _tabs.Add("repairs", "Repairs", _repairs, "Damaged vehicles and the spare parts they need");
        _build = new ProductionPanel(session, embedded: true);
        // A page of its own: the tabs show and hide the page, the production panel shows and hides itself.
        var buildPage = Widgets.Column(0);
        buildPage.AddChild(_build);
        _tabs.Add("build", "Build", buildPage, "The Garage's production queue: vehicles and parts");
        _tabs.Changed += _ => Refresh();
    }

    /// <summary>Shows the panel for a Garage building (hides it for anything else).</summary>
    public void Show(int? buildingId)
    {
        var sim = _session.Sim;
        var isGarage = buildingId is { } id && sim.State.FindBuilding(id)?.DefId == sim.Data.Vehicles.GarageBuilding;
        BuildingId = isGarage ? buildingId : null;
        if (isGarage && !Visible)
        {
            Motion.Reveal(this);
            if (DebugCapture.Value("--garage-tab") is { Length: > 0 } tab) _tabs.Select(tab);
        }
        else if (!isGarage) Visible = false;
        _build.Show(BuildingId);
        Invalidate();
        Refresh();
    }

    /// <summary>Debug / screenshots: select a vehicle and preview an upgrade on it (opens the Upgrades tab).</summary>
    public void Focus(int vehicleId, string? previewUpgrade = null)
    {
        _vehicleId = vehicleId;
        _preview = previewUpgrade;
        if (previewUpgrade is not null) _tabs.Select(DebugCapture.Value("--garage-tab") is { Length: > 0 } tab ? tab : "upgrades");
        Invalidate();
        Refresh();
    }

    private void Invalidate() => _detailShown = _upgradesShown = _repairsShown = _pickerShown = "";

    private void Pick(int vehicleId)
    {
        if (_vehicleId == vehicleId) return;
        _vehicleId = vehicleId;
        _preview = null;
        Invalidate();
        Refresh();
    }

    private static List<Vehicle> Vehicles(ColonySim sim) =>
        sim.State.Vehicles.OrderByDescending(v => sim.Data.VehicleTiers[v.TierId].Tier).ThenBy(v => v.Id).ToList();

    public void Refresh()
    {
        if (!Visible || BuildingId is not { } id || _session.Sim.State.FindBuilding(id) is not { } garage) return;
        var sim = _session.Sim;
        var vehicles = Vehicles(sim);
        if (_vehicleId is null || vehicles.All(v => v.Id != _vehicleId)) _vehicleId = vehicles.FirstOrDefault()?.Id;
        _set.Text(_title, $"Level {garage.Level} · {vehicles.Count} vehicle{(vehicles.Count == 1 ? "" : "s")}");
        var damaged = vehicles.Count(v => !v.Away && VehicleRules.PartsToRepair(sim, v) > 0);
        _tabs.SetLabel("repairs", damaged > 0 ? $"Repairs {damaged}" : "Repairs");
        _tabs.SetLabel("build", garage.Queue.Count > 0 ? $"Build {garage.Queue.Count}" : "Build");
        switch (_tabs.Selected)
        {
            case "vehicles":
                RefreshList(sim, vehicles);
                RefreshDetail(sim);
                break;
            case "upgrades":
                RefreshPicker(sim, vehicles);
                RefreshUpgrades(sim, garage);
                break;
            case "repairs":
                RefreshRepairs(sim, vehicles);
                break;
            case "build":
                _build.Refresh();
                break;
        }
    }

    // ---- Vehicles ----------------------------------------------------------------------------------------------------

    private void RefreshList(ColonySim sim, List<Vehicle> vehicles)
    {
        _set.Visible(_noVehicles, vehicles.Count == 0);
        var keys = vehicles.Select(v => v.Id).ToList();
        foreach (var gone in _cards.Keys.Except(keys).ToList()) _cards.Remove(gone);
        _listRows.Sync<PanelContainer>(keys, MakeCard, (vid, _) =>
        {
            if (sim.FindVehicle(vid) is not { } v || !_cards.TryGetValue(vid, out var card)) return;
            var stats = sim.VehicleStats(v);
            var picked = vid == _vehicleId;
            if (card.Card.ThemeTypeVariation != (picked ? "CardSelected" : "Card")) card.Card.ThemeTypeVariation = picked ? "CardSelected" : "Card";
            _set.Modulate(card.Icon, v.Away ? Palette.TextDim : Palette.Amber);
            var f = v.Condition / stats.MaxCondition;
            card.Bar.Set(f, $"{v.Condition:0}/{stats.MaxCondition:0}", Palette.Gauge(f));
            var broken = VehicleRules.Broken(sim.Data, v);
            _set.Text(card.Note, v.Away ? "Out with the party" : broken ? "Broken down: repair to drive" : "");
            _set.FontColor(card.Note, broken && !v.Away ? Palette.Danger : Palette.TextMuted);
            _set.Visible(card.Note, v.Away || broken);
        });
    }

    private PanelContainer MakeCard(int vid)
    {
        var sim = _session.Sim;
        var v = sim.FindVehicle(vid)!;
        var stats = sim.VehicleStats(v);
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var col = Widgets.Column(2);
        col.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(col);
        var head = Widgets.Row(6);
        head.MouseFilter = MouseFilterEnum.Ignore;
        var icon = Icons.Rect(Icons.Get("ui_vehicle", 18), 18, Palette.Amber);
        head.AddChild(icon);
        var name = Widgets.Label(v.Name);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        head.AddChild(name);
        head.AddChild(Widgets.Label($"T{stats.Tier.Tier}", "Small", Palette.TextMuted));
        col.AddChild(head);
        var bar = new StatBar("stat_hp", 120);
        col.AddChild(bar);
        var note = Widgets.Label("", "Small", Palette.TextMuted);
        note.Visible = false;
        col.AddChild(note);
        PanelKit.Clickable(card, () => Pick(vid));
        // Arrow keys move the focus down the list; the focused vehicle is the one shown.
        card.FocusEntered += () => Pick(vid);
        _cards[vid] = (card, icon, bar, note);
        return card;
    }

    public override void _Notification(int what)
    {
        // The preview is kept out of the tree while no vehicle is shown; free it with the panel so it does not leak.
        if (what == NotificationPredelete && IsInstanceValid(_preview3d) && _preview3d.GetParent() is null) _preview3d.Free();
    }

    private void RefreshDetail(ColonySim sim)
    {
        var v = _vehicleId is { } id ? sim.FindVehicle(id) : null;
        var signature = v is null ? "none" : $"{v.Id}|{v.TierId}|{v.Condition:0}|{string.Join(",", v.Upgrades.Select(u => $"{u.UpgradeId}/{u.Condition:0.00}"))}"
                                             + $"|{sim.State.Stockpile.Get(sim.Data.Vehicles.FuelResource):0}";
        if (signature == _detailShown) return;
        var vehicleChanged = _detailShown.Split('|')[0] != signature.Split('|')[0] || _detailShown.Split('|').ElementAtOrDefault(3) != signature.Split('|').ElementAtOrDefault(3);
        _detailShown = signature;
        _set.Visible(_detail, v is not null);
        if (v is null) return;
        var tier = sim.Data.VehicleTiers[v.TierId];
        var now = sim.VehicleStats(v);
        _set.Text(_detailHead, $"{v.Name} · {tier.Name}".ToUpperInvariant());
        if (vehicleChanged) _preview3d.Show(sim.Data, v);

        Widgets.Clear(_keyStats);
        var fuel = sim.Data.Vehicles.FuelResource;
        Stat(_keyStats, "stat_travel", "Speed", "travel", $"×{now.Speed:0.00}", Palette.Text, "Travel time is divided by this");
        Stat(_keyStats, "stat_load", "Cargo", "vehicles", $"{now.CargoKg:0} kg", Palette.Text, "What it carries home on top of the party's packs");
        Stat(_keyStats, "res_fuel", "Fuel/day", "vehicles", $"{now.FuelPerDay:0.0}", Palette.Text,
            $"{sim.Data.Resources[fuel].Name} burned per travel day; {sim.State.Stockpile.Get(fuel):0} in the stores");
        var f = v.Condition / now.MaxCondition;
        Stat(_keyStats, "stat_hp", "Condition", "vehicles", $"{v.Condition:0}/{now.MaxCondition:0}", Palette.Gauge(f), "Wear from each trip; repair it under Repairs");

        Widgets.Clear(_moreStats);
        Stat(_moreStats, "stat_threat", "Noise", "stealth", $"{now.Noise * 100:0}%", Palette.Text, "Raises the chance of being noticed on arrival");
        Stat(_moreStats, "stat_armor", "Armor", "vehicles", $"{now.Armor:0.#}", Palette.Text, "Soaks damage to the vehicle");
        Stat(_moreStats, "stat_range", "Range", "travel", $"{now.RangeDays:0.#} d", Palette.Text, "Travel days on a full tank");
        Stat(_moreStats, "stat_pop", "Seats", "vehicles", now.Seats is { } s ? $"{s}" : "any", Palette.Text, "Party members it carries");
        _more.SetTitle($"More stats (noise {now.Noise * 100:0}%, armor {now.Armor:0.#}, range {now.RangeDays:0.#} d)");
    }

    /// <summary>"[icon] Name (i)   value" into a 4-column grid (two stats per row).</summary>
    private static void Stat(GridContainer grid, string icon, string name, string topic, string value, Color color, string tip)
    {
        var row = Widgets.Row(4);
        row.AddChild(Icons.Rect(Icons.Get(icon, 14, "stat_check"), 14, Palette.TextMuted));
        row.AddChild(PanelKit.Tip(name, topic));
        row.TooltipText = tip;
        grid.AddChild(row);
        var label = Widgets.Label(value, "Number", color);
        label.TooltipText = tip;
        label.MouseFilter = MouseFilterEnum.Stop;
        grid.AddChild(label);
    }

    // ---- Upgrades ----------------------------------------------------------------------------------------------------

    private void RefreshPicker(ColonySim sim, List<Vehicle> vehicles)
    {
        var signature = string.Join(",", vehicles.Select(v => $"{v.Id}{v.Name}")) + $"|{_vehicleId}";
        if (signature == _pickerShown) return;
        _pickerShown = signature;
        _vehiclePicker.Clear();
        foreach (var v in vehicles) _vehiclePicker.AddItem($"{v.Name} · T{sim.Data.VehicleTiers[v.TierId].Tier}");
        _vehiclePicker.Select(vehicles.FindIndex(v => v.Id == _vehicleId));
        _vehiclePicker.Visible = vehicles.Count > 1;
    }

    /// <summary>What decides the Upgrades tab: the vehicle's fit, the garage level, the parts in the stores, schematics.</summary>
    private string UpgradesSignature(ColonySim sim, Building garage, Vehicle? v)
    {
        var parts = sim.Scavenge is { } items
            ? string.Join(",", sim.State.Inventory.Counts.Where(kv => items.Items.GetValueOrDefault(kv.Key)?.VehiclePart == true).Select(kv => $"{kv.Key}:{kv.Value}"))
            : "";
        var afford = _preview is { } p && sim.Data.VehicleUpgrades.TryGetValue(p, out var def) ? sim.State.Stockpile.CanAfford(def.Cost).ToString() : "";
        return $"{garage.Level}|{v?.Id}|{v?.Away}|{_preview}|{_onlyStocked.ButtonPressed}|{afford}|"
               + (v is null ? "" : string.Join(",", v.Upgrades.Select(u => $"{u.UpgradeId}/{u.Condition:0.00}")))
               + $"|{parts}|{sim.State.Stockpile.Get(sim.Data.Vehicles.FuelResource):0}|{sim.State.Schematics.Known.Count}";
    }

    private void RefreshUpgrades(ColonySim sim, Building garage)
    {
        var v = _vehicleId is { } id ? sim.FindVehicle(id) : null;
        var signature = UpgradesSignature(sim, garage, v);
        if (signature == _upgradesShown) return;
        _upgradesShown = signature;
        Widgets.Clear(_slots);
        Widgets.Clear(_fit);
        RebuildParts(sim);
        if (v is null)
        {
            _set.Text(_upgradesHead, "NO VEHICLE");
            _slots.AddChild(Widgets.Label("Build a vehicle first (Build tab).", "Muted"));
            return;
        }
        _set.Text(_upgradesHead, $"{v.Name} · {sim.Data.VehicleTiers[v.TierId].Name}".ToUpperInvariant());
        if (v.Away) _slots.AddChild(Widgets.Label("Out with the party: upgrades wait until it is home.", "Small", Palette.Warn));
        foreach (var slot in Enum.GetValues<VehicleSlot>())
        {
            var count = VehicleRules.SlotCount(sim, v, slot);
            if (count == 0) continue;
            var fitted = VehicleRules.InSlot(sim.Data, v, slot).ToList();
            var row = Widgets.Row(6);
            row.AddChild(Icons.Rect(Icons.Get($"slot_{slot.ToString().ToLowerInvariant()}", 18), 18, Palette.TextMuted));
            var label = Widgets.Label($"{slot} {fitted.Count}/{count}", "Small", Palette.TextMuted);
            label.CustomMinimumSize = new Vector2(84, 0);
            row.AddChild(label);
            var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation", 4);
            flow.AddThemeConstantOverride("v_separation", 4);
            foreach (var f in fitted) flow.AddChild(UpgradeChip(sim, v, f));
            // One picker per slot type: the first free place takes what is picked.
            if (fitted.Count < count && !v.Away) flow.AddChild(SlotPicker(sim, v, slot, count - fitted.Count));
            row.AddChild(flow);
            _slots.AddChild(row);
        }
        if (_preview is { } previewId && sim.Data.VehicleUpgrades.TryGetValue(previewId, out var preview)) _fit.AddChild(FitBox(sim, v, preview));
        else _fit.AddChild(Widgets.Label("Pick an upgrade in a free slot to see what it changes.", "Small", Palette.TextDim));
    }

    private Control UpgradeChip(ColonySim sim, Vehicle v, FittedUpgrade f)
    {
        var def = sim.Data.VehicleUpgrades[f.UpgradeId];
        var broken = VehicleRules.IsBroken(sim.Data, f);
        var card = new PanelContainer { ThemeTypeVariation = "Chip", TooltipText = $"{def.Name}: {def.Description}\nCondition {f.Condition * 100:0}%{(broken ? " — broken, does nothing until repaired" : "")}" };
        var col = Widgets.Column(2);
        card.AddChild(col);
        var head = Widgets.Row(4);
        head.AddChild(Widgets.Label(def.Name, "Small", broken ? Palette.Danger : Palette.Text));
        head.AddChild(Widgets.Label(VehicleRules.QualityName(sim, f.Quality), "Small", QualityColor(f.Quality)));
        var vid = v.Id;
        var upgradeId = f.UpgradeId;
        if (!v.Away)
            head.AddChild(PanelKit.Focusable(Widgets.IconButton("ui_close", "", "Take it off (half its parts come back)",
                () => _session.Execute(new RemoveUpgrade(vid, upgradeId)), variation: "Flat")));
        col.AddChild(head);
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(110, 5), MaxValue = 1, Value = f.Condition };
        bar.AddThemeStyleboxOverride("fill", HudTheme.Box(Palette.Gauge(f.Condition), radius: 2, padX: 0, padY: 0));
        col.AddChild(bar);
        return card;
    }

    private bool Stocked(ColonySim sim, VehicleUpgradeDef u) => u.Parts.All(kv => PartsHave(sim, kv.Key) >= kv.Value);

    /// <summary>A free slot's picker: the upgrades that go there (locked ones say why), filtered to stocked parts when asked.</summary>
    private Control SlotPicker(ColonySim sim, Vehicle v, VehicleSlot slot, int free)
    {
        var picker = PanelKit.Focusable(new OptionButton { ClipText = true, FitToLongestItem = false, CustomMinimumSize = new Vector2(190, 0) });
        var ids = new List<string?> { null };
        picker.AddItem(free > 1 ? $"{free} free — fit…" : "Free — fit…");
        foreach (var u in sim.Data.Vehicles.Upgrades.Where(u => u.Slot == slot && sim.Data.VehicleTiers[v.TierId].Tier >= u.MinTier && !v.Has(u.Id)))
        {
            if (_onlyStocked.ButtonPressed && !Stocked(sim, u) && u.Id != _preview) continue;
            ids.Add(u.Id);
            var why = VehicleRules.CanFit(sim, v, u);
            var note = why?.Error switch
            {
                CommandError.RecipeLocked => "  [schematic]",
                CommandError.RecipeLevelTooLow => $"  [Garage L{u.GarageLevel}]",
                CommandError.SlotFull or null => Stocked(sim, u) ? "" : "  [no parts]",
                _ => "  [—]",
            };
            picker.AddItem(u.Name + note);
        }
        picker.Select(Math.Max(0, ids.IndexOf(_preview)));
        picker.ItemSelected += i =>
        {
            _preview = ids[(int)i];
            _upgradesShown = "";
            Refresh();
        };
        return picker;
    }

    /// <summary>The considered upgrade: what it changes (before → after), its cost with what is missing, why not, and Fit.</summary>
    private Control FitBox(ColonySim sim, Vehicle v, VehicleUpgradeDef u)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Inset" };
        var col = Widgets.Column(6);
        card.AddChild(col);
        var head = Widgets.Row(6);
        var title = Widgets.Label($"{u.Name} ({u.Slot})", "Heading");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        head.AddChild(new InfoTip("vehicles"));
        col.AddChild(head);
        col.AddChild(Widgets.Wrapped(u.Description, "Muted"));

        // Only what changes, in the "80 → 120" style.
        var tier = sim.Data.VehicleTiers[v.TierId];
        var now = sim.VehicleStats(v);
        var after = VehicleRules.Stats(sim.Data, tier, v.Upgrades.Where(x => !VehicleRules.IsBroken(sim.Data, x)).Append(FittedUpgrade.Standard(u.Id)));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 3);
        var any = false;
        void Delta(string name, string topic, double a, double b, string format, bool higherIsBetter = true)
        {
            if (Math.Abs(a - b) < 1e-6) return;
            any = true;
            grid.AddChild(PanelKit.Tip(name, topic));
            grid.AddChild(Widgets.Label($"{a.ToString(format)} → {b.ToString(format)}", "Number", (b > a) == higherIsBetter ? Palette.Good : Palette.Danger));
        }
        Delta("Speed", "travel", now.Speed, after.Speed, "×0.00");
        Delta("Cargo kg", "vehicles", now.CargoKg, after.CargoKg, "0");
        Delta("Fuel/day", "vehicles", now.FuelPerDay, after.FuelPerDay, "0.0", higherIsBetter: false);
        Delta("Noise", "stealth", now.Noise * 100, after.Noise * 100, "0'%'", higherIsBetter: false);
        Delta("Armor", "vehicles", now.Armor, after.Armor, "0.#");
        Delta("Range d", "travel", now.RangeDays, after.RangeDays, "0.#");
        Delta("Seats", "vehicles", now.Seats ?? 99, after.Seats ?? 99, "0");
        Delta("Max cond.", "vehicles", now.MaxCondition, after.MaxCondition, "0");
        if (any) col.AddChild(grid);

        var costs = u.Cost.Select(kv => (kv.Key, sim.Data.Resources[kv.Key].Name, kv.Value, sim.State.Stockpile.Get(kv.Key)))
            .Concat(u.Parts.Select(kv => (kv.Key, sim.Scavenge?.Items.GetValueOrDefault(kv.Key)?.Name ?? kv.Key, (double)kv.Value,
                (double)PartsHave(sim, kv.Key))));
        col.AddChild(Widgets.CostChips(costs));
        var why = VehicleRules.CanFit(sim, v, u);
        if (why is not null) col.AddChild(Widgets.Wrapped(why.Message, color: Palette.Danger));
        var row = Widgets.Row(6);
        var vid = v.Id;
        var fit = PanelKit.Focusable(Widgets.IconButton("ui_plus", "Fit", "Fit it now", () =>
        {
            if (_session.Execute(new FitUpgrade(vid, u.Id)).Ok) _preview = null;
            _upgradesShown = "";
            Refresh();
        }, variation: "Primary"));
        fit.Disabled = why is not null;
        row.AddChild(fit);
        row.AddChild(PanelKit.Focusable(Widgets.TextButton("Clear", () =>
        {
            _preview = null;
            _upgradesShown = "";
            Refresh();
        }, "Stop considering it", "Flat")));
        col.AddChild(row);
        return card;
    }

    private void RebuildParts(ColonySim sim)
    {
        Widgets.Clear(_parts);
        if (sim.Scavenge is not { } items) return;
        var parts = sim.State.Inventory.Counts.Where(kv => items.Items.GetValueOrDefault(kv.Key)?.VehiclePart == true).ToList();
        if (parts.Count == 0) _parts.AddChild(Widgets.Label("No parts. The Garage makes them (Build); wrecks in the field give some.", "Muted"));
        foreach (var (id, count) in parts)
        {
            var def = items.Items[id];
            var quality = def.Quality ?? items.Rules.Quality.Find(t => t.Base)?.Id ?? "standard";
            _parts.AddChild(Widgets.Chip(Icons.Item(def.DefinedId, def.Kind.ToString(), 14), $"{items.Items[def.DefinedId].Name} ×{count}",
                QualityColor(quality), $"{def.Name}: {VehicleRules.QualityName(sim, quality)} quality"));
        }
        var fuel = sim.Data.Vehicles.FuelResource;
        _parts.AddChild(Widgets.Chip(Icons.Resource(fuel, 14), $"{sim.Data.Resources[fuel].Name} {sim.State.Stockpile.Get(fuel):0}"));
    }

    // ---- Repairs -----------------------------------------------------------------------------------------------------

    private void RefreshRepairs(ColonySim sim, List<Vehicle> vehicles)
    {
        var damaged = vehicles.Where(v => !v.Away && VehicleRules.PartsToRepair(sim, v) > 0)
            .OrderByDescending(v => VehicleRules.Broken(sim.Data, v)).ThenBy(v => v.Condition / sim.VehicleStats(v).MaxCondition).ToList();
        var have = PartsHave(sim, sim.Data.Vehicles.RepairItem);
        var signature = $"{have}|" + string.Join(",", damaged.Select(v => $"{v.Id}:{VehicleRules.PartsToRepair(sim, v)}"));
        if (signature == _repairsShown) return;
        _repairsShown = signature;
        Widgets.Clear(_repairs);
        var spare = sim.Scavenge?.Items.GetValueOrDefault(sim.Data.Vehicles.RepairItem)?.Name ?? "spare parts";
        _repairs.AddChild(PanelKit.Header("Repairs", "skill_mechanics", "vehicles"));
        _repairs.AddChild(Widgets.Label($"{spare}: {have} in the stores. A repair is done on the spot by the Garage crew.", "Small", Palette.TextMuted));
        if (damaged.Count == 0) _repairs.AddChild(Widgets.Label("Nothing to repair.", "Muted"));
        foreach (var v in damaged)
        {
            var stats = sim.VehicleStats(v);
            var card = new PanelContainer { ThemeTypeVariation = "Card" };
            var row = Widgets.Row(8);
            card.AddChild(row);
            row.AddChild(Icons.Rect(Icons.Get("ui_vehicle", 18), 18, VehicleRules.Broken(sim.Data, v) ? Palette.Danger : Palette.Amber));
            var col = Widgets.Column(2);
            col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var need = VehicleRules.PartsToRepair(sim, v);
            col.AddChild(Widgets.Label(v.Name));
            var bar = new StatBar("stat_hp", 160);
            bar.Set(v.Condition / stats.MaxCondition, $"{v.Condition:0}/{stats.MaxCondition:0}", Palette.Gauge(v.Condition / stats.MaxCondition));
            col.AddChild(bar);
            var worn = v.Upgrades.Count(u => u.Condition < 0.999);
            col.AddChild(Widgets.Label($"Needs {need} {spare}{(worn > 0 ? $" · {worn} worn upgrade{(worn == 1 ? "" : "s")}" : "")}", "Small", need > have ? Palette.Warn : Palette.TextMuted));
            row.AddChild(col);
            var vid = v.Id;
            var fix = PanelKit.Focusable(Widgets.IconButton("skill_mechanics", need > have && have > 0 ? $"Repair ({have})" : "Repair",
                need > have ? $"Use the {have} there are: a part repair" : $"Use {need} {spare}", () => _session.Execute(new RepairVehicle(vid)), variation: "Primary"));
            fix.Disabled = have == 0;
            fix.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(fix);
            _repairs.AddChild(card);
        }
    }

    private static int PartsHave(ColonySim sim, string baseId) =>
        sim.State.Inventory.Counts.Where(kv => kv.Key == baseId || sim.Scavenge?.Items.GetValueOrDefault(kv.Key)?.DefinedId == baseId).Sum(kv => kv.Value);

    public static Color QualityColor(string tier) => tier switch
    {
        "fine" => Palette.Amber,
        "crude" => Palette.TextDim,
        _ => Palette.Text,
    };
}
