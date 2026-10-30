using System;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The selected building: icon, name, category and level pips; its workers as portraits (empty slots shown);
/// what it makes per day (tooltip: per worker); beds or patient beds; and the next upgrade as cost chips (missing
/// resources marked) with the Town Hall cap. Core decides whether an upgrade goes through.
/// </summary>
public partial class BuildingPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly TextureRect _icon = Icons.Rect(Icons.Get("ui_build", 26), 26, Palette.Rust);
    private readonly Label _name = Widgets.Label("", "Display");
    private readonly Label _category = Widgets.Label("", "Small");
    private readonly Pips _levels = new();
    private readonly Label _workersLabel = Widgets.Label("", "Heading");
    private readonly HFlowContainer _workers = new();
    private readonly Label _output = Widgets.Label("", "");
    private readonly VBoxContainer _upgradeBox = Widgets.Column(6);
    private readonly Label _upgradeTitle = Widgets.Label("", "Heading");
    private readonly MarginContainer _costs = new();
    private readonly Label _cap = Widgets.Wrapped("", "Small");
    private readonly Button _upgrade;
    private string _workerSignature = "";
    private string _costSignature = "";

    public int? BuildingId { get; private set; }

    public event Action? Closed;

    public BuildingPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(0, 0);
        var box = Ui.Panel(this, 8);

        var head = Widgets.Row(8);
        head.AddChild(_icon);
        var names = Widgets.Column(0);
        names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.AddThemeFontSizeOverride("font_size", 22);
        names.AddChild(_name);
        var sub = Widgets.Row(8);
        sub.AddChild(_category);
        sub.AddChild(_levels);
        names.AddChild(sub);
        head.AddChild(names);
        var close = Widgets.IconButton("ui_close", "", "Deselect", () => Closed?.Invoke(), "Esc", "Flat");
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        head.AddChild(close);
        box.AddChild(head);

        box.AddChild(_workersLabel);
        _workers.AddThemeConstantOverride("h_separation", 4);
        box.AddChild(_workers);
        _output.MouseFilter = MouseFilterEnum.Stop;
        _output.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_output);

        _upgradeBox.AddChild(Widgets.SectionHeader("Upgrade", "ui_chevron_up"));
        _upgradeBox.AddChild(_upgradeTitle);
        _upgradeBox.AddChild(_costs);
        _upgradeBox.AddChild(_cap);
        _upgrade = Widgets.IconButton("ui_chevron_up", "Upgrade", "Upgrade this building", Upgrade, variation: "Primary");
        _upgradeBox.AddChild(_upgrade);
        box.AddChild(_upgradeBox);
        Visible = false;
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        _workerSignature = _costSignature = "";
        if (buildingId is null) Visible = false;
        else if (_session.Sim.State.FindBuilding(buildingId.Value) is not null) Motion.Reveal(this, new Vector2(12, 0));
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        if (building is null)
        {
            Visible = false;
            return;
        }

        var def = sim.Data.Buildings[building.DefId];
        var level = def.Level(building.Level);
        _icon.Texture = Icons.Building(def.Id, def.Category.ToString(), 26);
        _name.Text = def.Name;
        _category.Text = $"{def.Category} · level {building.Level}/{def.MaxLevel}";
        _levels.Set(building.Level, def.MaxLevel);
        _levels.TooltipText = $"Level {building.Level} of {def.MaxLevel}";

        RefreshWorkers(sim, building, def, level.WorkerSlots);
        _output.Text = OutputText(sim, building, def, level, out var tip);
        _output.TooltipText = tip;
        _output.Visible = _output.Text.Length > 0;
        RefreshUpgrade(sim, building, def);
    }

    private void RefreshWorkers(ColonySim sim, Building building, Remnants.Core.Data.BuildingDef def, int slots)
    {
        var workers = sim.State.WorkersAt(building.Id).OrderBy(s => s.Id).ToList();
        _workersLabel.Visible = _workers.Visible = def.Job is not null;
        if (def.Job is null) return;
        _workersLabel.Text = $"WORKERS {workers.Count}/{slots}";
        var signature = string.Join(",", workers.Select(w => w.Id)) + $"/{slots}";
        if (signature == _workerSignature) return;
        _workerSignature = signature;
        Widgets.Clear(_workers);
        foreach (var w in workers)
        {
            var portrait = new Portrait(38) { TooltipText = $"{w.Name}\nAssign and unassign from the survivor's panel." };
            portrait.MouseFilter = MouseFilterEnum.Stop;
            portrait.Set(PortraitStudio.For(GetTree(), w), PortraitStudio.Backdrop(w.Id));
            _workers.AddChild(portrait);
        }
        for (var i = workers.Count; i < slots; i++)
            _workers.AddChild(new EmptySlot { TooltipText = "Free slot: select a survivor and Assign them here." });
    }

    private static string OutputText(ColonySim sim, Building building, Remnants.Core.Data.BuildingDef def, Remnants.Core.Data.BuildingLevelDef level, out string tip)
    {
        tip = "";
        var perDay = sim.Data.Rules.TicksPerDay;
        if (def.Produces is { } res)
        {
            var name = sim.Data.Resources[res].Name;
            var total = sim.OutputPerTick(building) * perDay;
            tip = $"{def.BaseOutput:0.##} {name}/h per worker at full skill × level {Fmt.Factor(level.OutputMultiplier)}\n" +
                  string.Join("\n", sim.State.WorkersAt(building.Id).Select(w =>
                      $"  {w.Name}: {JobOutput.PerTick(def, building.Level, sim.Data.Jobs[def.Job!], w, sim.Data.Rules.Output, sim.Data) * perDay:0.#}/day")) +
                  "\nOpen a worker's Overview for the full formula.";
            return $"Makes about {total:0.#} {name} per day";
        }
        if (level.Beds > 0) return $"Sleeps {level.Beds}";
        if (level.Patients > 0) return $"{level.Patients} patient beds";
        return "";
    }

    private void RefreshUpgrade(ColonySim sim, Building building, Remnants.Core.Data.BuildingDef def)
    {
        var next = building.Level + 1;
        if (next > def.MaxLevel)
        {
            _upgradeTitle.Text = "MAX LEVEL REACHED";
            _costs.Visible = _cap.Visible = _upgrade.Visible = false;
            return;
        }
        _costs.Visible = _cap.Visible = _upgrade.Visible = true;
        var cost = def.Level(next).Cost;
        var nextLevel = def.Level(next);
        var gains = new System.Collections.Generic.List<string>();
        if (nextLevel.WorkerSlots > def.Level(building.Level).WorkerSlots) gains.Add($"{nextLevel.WorkerSlots} workers");
        if (nextLevel.OutputMultiplier > def.Level(building.Level).OutputMultiplier) gains.Add($"output {Fmt.Factor(nextLevel.OutputMultiplier)}");
        if (nextLevel.Beds > 0) gains.Add($"{nextLevel.Beds} beds");
        if (nextLevel.Patients > 0) gains.Add($"{nextLevel.Patients} patients");
        _upgradeTitle.Text = $"LEVEL {next}" + (gains.Count > 0 ? ": " + string.Join(", ", gains).ToUpperInvariant() : "");

        var signature = string.Join(",", cost.Select(kv => $"{kv.Key}:{kv.Value}:{sim.State.Stockpile.Get(kv.Key) >= kv.Value}"));
        if (signature != _costSignature)
        {
            _costSignature = signature;
            Widgets.Clear(_costs);
            _costs.AddChild(Widgets.CostChips(cost.Select(kv => (kv.Key, sim.Data.Resources[kv.Key].Name, kv.Value, sim.State.Stockpile.Get(kv.Key)))));
        }

        var cap = sim.MaxUpgradeLevel(def);
        var capped = def.Id != sim.Data.Rules.TownHallBuildingId && next > cap;
        _cap.Visible = def.Id != sim.Data.Rules.TownHallBuildingId;
        _cap.Text = capped ? $"⚠ Town Hall level {sim.TownHallLevel} allows up to level {cap}: upgrade the Town Hall first."
            : $"Town Hall level {sim.TownHallLevel} allows up to level {cap}.";
        _cap.AddThemeColorOverride("font_color", capped ? Palette.Danger : Palette.TextMuted);
        var affordable = sim.State.Stockpile.CanAfford(cost);
        // Core decides; a refused upgrade explains itself in the log. The button only signals what is likely.
        _upgrade.Disabled = capped;
        _upgrade.TooltipText = capped ? "Needs a higher Town Hall level." : affordable ? $"Upgrade to level {next}." : "Not enough resources yet (marked with !).";
    }

    private void Upgrade()
    {
        if (BuildingId is { } id) _session.Execute(new Upgrade(id));
    }

    /// <summary>A dashed square for a free worker slot.</summary>
    private partial class EmptySlot : Control
    {
        public EmptySlot()
        {
            CustomMinimumSize = new Vector2(38, 38);
            MouseFilter = MouseFilterEnum.Stop;
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(Palette.Inset, 0.7f));
            DrawDashedLine(new Vector2(1, 1), new Vector2(Size.X - 1, 1), Palette.EdgeLight, 1, 3);
            DrawDashedLine(new Vector2(1, Size.Y - 1), new Vector2(Size.X - 1, Size.Y - 1), Palette.EdgeLight, 1, 3);
            DrawDashedLine(new Vector2(1, 1), new Vector2(1, Size.Y - 1), Palette.EdgeLight, 1, 3);
            DrawDashedLine(new Vector2(Size.X - 1, 1), new Vector2(Size.X - 1, Size.Y - 1), Palette.EdgeLight, 1, 3);
            DrawLine(new Vector2(Size.X / 2 - 5, Size.Y / 2), new Vector2(Size.X / 2 + 5, Size.Y / 2), Palette.TextDim, 2);
            DrawLine(new Vector2(Size.X / 2, Size.Y / 2 - 5), new Vector2(Size.X / 2, Size.Y / 2 + 5), Palette.TextDim, 2);
        }
    }
}
