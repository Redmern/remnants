using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The Garage (opens with the Garage selected): the colony's vehicles down the left with their condition; the chosen
/// vehicle's numbers and its slots (engine, tyres, armor, utility) with each fitted upgrade as a chip (quality, condition
/// bar, take off); an upgrade picker for a free slot with the before → after numbers, its cost and why it cannot be fitted
/// yet; the parts in the stores by quality; and the repair list (every damaged vehicle with the spare parts it needs).
/// Building vehicles and parts is the Garage's production queue (<see cref="ProductionPanel"/>). Core decides.
/// </summary>
public partial class GaragePanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly VBoxContainer _list = new();
    private readonly VBoxContainer _detail = new();
    private readonly HFlowContainer _parts = new();
    private readonly VBoxContainer _repairs = new();
    private readonly Label _title = Widgets.Label("", "Heading");
    private int? _vehicleId;
    private string? _preview;
    private string _shown = "";

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
        var columns = Widgets.Row(10);
        columns.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.AddChild(columns);

        var left = Widgets.Column(6);
        left.CustomMinimumSize = new Vector2(230, 0);
        left.AddChild(Widgets.SectionHeader("Vehicles", "ui_vehicle"));
        _list.AddThemeConstantOverride("separation", 4);
        left.AddChild(_list);
        left.AddChild(Widgets.SectionHeader("Repairs", "skill_mechanics"));
        _repairs.AddThemeConstantOverride("separation", 4);
        left.AddChild(_repairs);
        columns.AddChild(left);

        _detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _detail.AddThemeConstantOverride("separation", 6);
        columns.AddChild(_detail);

        box.AddChild(Widgets.SectionHeader("Parts in the stores", "item_material"));
        _parts.AddThemeConstantOverride("h_separation", 4);
        _parts.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(_parts);
    }

    /// <summary>Shows the panel for a Garage building (hides it for anything else).</summary>
    public void Show(int? buildingId)
    {
        var sim = _session.Sim;
        var isGarage = buildingId is { } id && sim.State.FindBuilding(id)?.DefId == sim.Data.Vehicles.GarageBuilding;
        BuildingId = isGarage ? buildingId : null;
        Visible = isGarage;
        _shown = "";
        Refresh();
    }

    /// <summary>Debug / screenshots: select a vehicle and preview an upgrade on it.</summary>
    public void Focus(int vehicleId, string? previewUpgrade = null)
    {
        _vehicleId = vehicleId;
        _preview = previewUpgrade;
        _shown = "";
        Refresh();
    }

    public void Refresh()
    {
        if (!Visible || BuildingId is not { } id || _session.Sim.State.FindBuilding(id) is not { } garage) return;
        var sim = _session.Sim;
        var vehicles = sim.State.Vehicles.OrderByDescending(v => sim.Data.VehicleTiers[v.TierId].Tier).ThenBy(v => v.Id).ToList();
        if (_vehicleId is null || vehicles.All(v => v.Id != _vehicleId)) _vehicleId = vehicles.FirstOrDefault()?.Id;
        var signature = $"{garage.Level}|{_vehicleId}|{_preview}|" + string.Join(";", vehicles.Select(v =>
                            $"{v.Id}:{v.Condition:0.0}:{v.Away}:{string.Join(",", v.Upgrades.Select(u => $"{u.UpgradeId}/{u.Condition:0.00}"))}"))
                        + "|" + string.Join(",", sim.State.Inventory.Counts.Select(kv => $"{kv.Key}:{kv.Value}"))
                        + "|" + string.Join(",", sim.State.Stockpile.Amounts.Select(kv => $"{kv.Key}:{kv.Value:0}"))
                        + "|" + string.Join(",", sim.State.KnownBlueprints);
        if (signature == _shown) return;
        _shown = signature;
        _title.Text = $"Level {garage.Level} · {vehicles.Count} vehicle{(vehicles.Count == 1 ? "" : "s")}";
        RebuildList(sim, vehicles);
        RebuildDetail(sim);
        RebuildParts(sim);
        RebuildRepairs(sim, vehicles);
    }

    private void RebuildList(ColonySim sim, List<Vehicle> vehicles)
    {
        Widgets.Clear(_list);
        if (vehicles.Count == 0) _list.AddChild(Widgets.Label("No vehicles. Queue one in the Garage's production.", "Muted"));
        foreach (var v in vehicles)
        {
            var stats = sim.VehicleStats(v);
            var card = new PanelContainer { ThemeTypeVariation = v.Id == _vehicleId ? "CardSelected" : "Card", MouseFilter = MouseFilterEnum.Stop };
            var col = Widgets.Column(2);
            card.AddChild(col);
            var head = Widgets.Row(6);
            head.AddChild(Icons.Rect(Icons.Get("ui_vehicle", 18), 18, v.Away ? Palette.TextDim : Palette.Amber));
            var name = Widgets.Label(v.Name);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            head.AddChild(name);
            head.AddChild(Widgets.Label($"T{stats.Tier.Tier}", "Small", Palette.TextMuted));
            col.AddChild(head);
            var bar = new StatBar("stat_hp", 120);
            bar.Set(v.Condition / stats.MaxCondition, $"{v.Condition:0}/{stats.MaxCondition:0}", Palette.Gauge(v.Condition / stats.MaxCondition));
            col.AddChild(bar);
            if (v.Away) col.AddChild(Widgets.Label("Out with the party", "Small", Palette.TextMuted));
            else if (VehicleRules.Broken(sim.Data, v)) col.AddChild(Widgets.Label("Broken down: repair to drive", "Small", Palette.Danger));
            var vid = v.Id;
            card.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
                _vehicleId = vid;
                _preview = null;
                _shown = "";
                Refresh();
            };
            _list.AddChild(card);
        }
    }

    private void RebuildDetail(ColonySim sim)
    {
        Widgets.Clear(_detail);
        if (_vehicleId is not { } id || sim.FindVehicle(id) is not { } v) return;
        var tier = sim.Data.VehicleTiers[v.TierId];
        var now = sim.VehicleStats(v);
        _detail.AddChild(Widgets.SectionHeader($"{v.Name} · {tier.Name}", "ui_vehicle"));

        // Before → after when an upgrade is being considered.
        VehicleStats? after = null;
        if (_preview is { } up && sim.Data.VehicleUpgrades.TryGetValue(up, out var def) && !v.Has(up))
            after = VehicleRules.Stats(sim.Data, tier, v.Upgrades.Where(u => !VehicleRules.IsBroken(sim.Data, u)).Append(FittedUpgrade.Standard(def.Id)));
        _detail.AddChild(StatGrid(now, after));

        foreach (var slot in Enum.GetValues<VehicleSlot>())
        {
            var count = VehicleRules.SlotCount(sim, v, slot);
            if (count == 0) continue;
            var fitted = VehicleRules.InSlot(sim.Data, v, slot).ToList();
            var row = Widgets.Row(6);
            var label = Widgets.Label($"{slot} {fitted.Count}/{count}", "Small", Palette.TextMuted);
            label.CustomMinimumSize = new Vector2(84, 0);
            row.AddChild(Icons.Rect(Icons.Get($"slot_{slot.ToString().ToLowerInvariant()}", 18), 18, Palette.TextMuted));
            row.AddChild(label);
            var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation", 4);
            foreach (var f in fitted) flow.AddChild(UpgradeChip(sim, v, f));
            for (var i = fitted.Count; i < count; i++) flow.AddChild(EmptySlot(sim, v, slot, first: i == fitted.Count));
            row.AddChild(flow);
            _detail.AddChild(row);
        }

        if (_preview is { } previewId && sim.Data.VehicleUpgrades.TryGetValue(previewId, out var preview)) _detail.AddChild(FitBox(sim, v, preview));
    }

    /// <summary>Speed, fuel, cargo, noise, armor, seats, range and condition; with a preview, before → after in colour.</summary>
    private static GridContainer StatGrid(VehicleStats now, VehicleStats? after)
    {
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        void Stat(string icon, string name, double value, double? next, string format, bool higherIsBetter = true)
        {
            var row = Widgets.Row(4);
            row.AddChild(Icons.Rect(Icons.Get(icon, 14, "stat_check"), 14, Palette.TextMuted));
            row.AddChild(Widgets.Label(name, "Small", Palette.TextMuted));
            grid.AddChild(row);
            var text = value.ToString(format);
            var color = Palette.Text;
            if (next is { } n && Math.Abs(n - value) > 1e-6)
            {
                text += $" → {n.ToString(format)}";
                color = (n > value) == higherIsBetter ? Palette.Good : Palette.Danger;
            }
            grid.AddChild(Widgets.Label(text, "Number", color));
        }
        Stat("stat_travel", "Speed", now.Speed, after?.Speed, "×0.00");
        Stat("res_fuel", "Fuel/day", now.FuelPerDay, after?.FuelPerDay, "0.0", higherIsBetter: false);
        Stat("stat_load", "Cargo kg", now.CargoKg, after?.CargoKg, "0");
        Stat("stat_threat", "Noise", now.Noise * 100, after is null ? null : after.Noise * 100, "0'%'", higherIsBetter: false);
        Stat("stat_armor", "Armor", now.Armor, after?.Armor, "0.#");
        Stat("stat_pop", "Seats", now.Seats ?? 99, after is null ? null : after.Seats ?? 99, "0");
        Stat("stat_range", "Range d", now.RangeDays, after?.RangeDays, "0.#");
        Stat("stat_hp", "Max cond.", now.MaxCondition, after?.MaxCondition, "0");
        return grid;
    }

    private Control UpgradeChip(ColonySim sim, Vehicle v, FittedUpgrade f)
    {
        var def = sim.Data.VehicleUpgrades[f.UpgradeId];
        var broken = VehicleRules.IsBroken(sim.Data, f);
        var card = new PanelContainer { ThemeTypeVariation = "Chip", TooltipText = $"{def.Name}: {def.Description}\nCondition {f.Condition * 100:0}%{(broken ? " — broken, does nothing until repaired" : "")}" };
        var col = Widgets.Column(2);
        card.AddChild(col);
        var head = Widgets.Row(4);
        head.AddChild(Widgets.Label(def.Name, color: broken ? Palette.Danger : Palette.Text, size: 13));
        head.AddChild(Widgets.Label(VehicleRules.QualityName(sim, f.Quality), "Small", QualityColor(f.Quality)));
        var vid = v.Id;
        var upgradeId = f.UpgradeId;
        if (!v.Away)
            head.AddChild(Widgets.IconButton("ui_close", "", "Take it off (half its parts come back)",
                () => _session.Execute(new RemoveUpgrade(vid, upgradeId)), variation: "Flat"));
        col.AddChild(head);
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(110, 5), MaxValue = 1, Value = f.Condition };
        bar.AddThemeStyleboxOverride("fill", HudTheme.Box(Palette.Gauge(f.Condition), radius: 2, padX: 0, padY: 0));
        col.AddChild(bar);
        return card;
    }

    /// <summary>An empty slot: a picker of the upgrades that go there (locked ones say why).</summary>
    private Control EmptySlot(ColonySim sim, Vehicle v, VehicleSlot slot, bool first)
    {
        var picker = new OptionButton { FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false, CustomMinimumSize = new Vector2(150, 0) };
        var ids = new List<string?> { null };
        picker.AddItem("Empty — fit…");
        foreach (var u in sim.Data.Vehicles.Upgrades.Where(u => u.Slot == slot && sim.Data.VehicleTiers[v.TierId].Tier >= u.MinTier && !v.Has(u.Id)))
        {
            ids.Add(u.Id);
            var why = VehicleRules.CanFit(sim, v, u);
            var note = why?.Error switch
            {
                CommandError.RecipeLocked => "  [schematic]",
                CommandError.RecipeLevelTooLow => $"  [Garage L{u.GarageLevel}]",
                CommandError.SlotFull => "",
                null => "",
                _ => "  [—]",
            };
            picker.AddItem(u.Name + note);
        }
        // The upgrade being considered shows in the first free slot of its kind only.
        picker.Select(first ? Math.Max(0, ids.IndexOf(_preview)) : 0);
        picker.ItemSelected += i =>
        {
            _preview = ids[(int)i];
            _shown = "";
            Refresh();
        };
        return picker;
    }

    /// <summary>The considered upgrade: what it does, its cost with what is missing, why not (if so), and Fit.</summary>
    private Control FitBox(ColonySim sim, Vehicle v, VehicleUpgradeDef u)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Inset" };
        var col = Widgets.Column(4);
        card.AddChild(col);
        col.AddChild(Widgets.Label($"{u.Name} ({u.Slot})", "Heading"));
        col.AddChild(Widgets.Wrapped(u.Description, "Muted"));
        var costs = u.Cost.Select(kv => (kv.Key, sim.Data.Resources[kv.Key].Name, kv.Value, sim.State.Stockpile.Get(kv.Key)))
            .Concat(u.Parts.Select(kv => (kv.Key, sim.Scavenge?.Items.GetValueOrDefault(kv.Key)?.Name ?? kv.Key, (double)kv.Value,
                (double)PartsHave(sim, kv.Key))));
        col.AddChild(Widgets.CostChips(costs));
        if (u.Parts.Count > 0)
            col.AddChild(Widgets.Label("Best parts first: their quality scales the effect.", "Small", Palette.TextMuted));
        var why = VehicleRules.CanFit(sim, v, u);
        if (why is not null) col.AddChild(Widgets.Wrapped(why.Message, color: Palette.Danger));
        var vid = v.Id;
        var fit = Widgets.IconButton("ui_plus", "Fit", "Fit it now", () =>
        {
            if (_session.Execute(new FitUpgrade(vid, u.Id)).Ok) _preview = null;
            _shown = "";
            Refresh();
        }, variation: "Primary");
        fit.Disabled = why is not null;
        col.AddChild(fit);
        return card;
    }

    private void RebuildParts(ColonySim sim)
    {
        Widgets.Clear(_parts);
        if (sim.Scavenge is not { } items) return;
        var parts = sim.State.Inventory.Counts.Where(kv => items.Items.GetValueOrDefault(kv.Key)?.VehiclePart == true).ToList();
        if (parts.Count == 0) _parts.AddChild(Widgets.Label("No parts. The Garage makes them; wrecks in the field give some.", "Muted"));
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

    private void RebuildRepairs(ColonySim sim, List<Vehicle> vehicles)
    {
        Widgets.Clear(_repairs);
        var damaged = vehicles.Where(v => !v.Away && VehicleRules.PartsToRepair(sim, v) > 0)
            .OrderByDescending(v => VehicleRules.Broken(sim.Data, v)).ThenBy(v => v.Condition / sim.VehicleStats(v).MaxCondition).ToList();
        if (damaged.Count == 0) _repairs.AddChild(Widgets.Label("Nothing to repair.", "Muted"));
        var have = PartsHave(sim, sim.Data.Vehicles.RepairItem);
        foreach (var v in damaged)
        {
            var row = Widgets.Row(6);
            var need = VehicleRules.PartsToRepair(sim, v);
            var label = Widgets.Label($"{v.Name}: {need} spare part{(need == 1 ? "" : "s")}", size: 13);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            var vid = v.Id;
            var fix = Widgets.IconButton("skill_mechanics", "Repair", $"Use up to {need} spare parts (have {have})", () => _session.Execute(new RepairVehicle(vid)));
            fix.Disabled = have == 0;
            row.AddChild(fix);
            _repairs.AddChild(row);
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
