using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Data;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The build menu: one row of category tabs (icons, the name on hover) and a grid of tiles (icon, name, what it does in
/// one line, cost chips that say "short N" when the stock cannot cover them). Tiles the colony cannot build yet show a
/// lock and say why on the tile (a schematic, Town Hall level, already built). Picking a tile starts plot placement in
/// the town view; while placing, a tag at the cursor names the building, its cost and how to cancel.
/// </summary>
public partial class BuildMenu : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Label _hint = Widgets.Wrapped("", "Muted");
    private readonly GridContainer _grid = new() { Columns = 2 };
    private readonly Dictionary<string, Tile> _tiles = new();
    private readonly Dictionary<string, Button> _filters = new();
    private readonly PanelContainer _cursorTag = new() { ThemeTypeVariation = "Toast", TopLevel = true, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    private readonly Label _cursorText = Widgets.Label("", "Small", Palette.Text);
    private string _category = "All";
    private string? _placing;

    public event Action<string>? TypeChosen;
    public event Action? Closed;

    public BuildMenu(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(PanelWidth.Side, 0);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var box = Ui.Panel(this, Space.Sm);
        var title = Ui.TitleRow("Build", "ui_build", () => Closed?.Invoke(), Hotkeys.KeyName("open_build"));
        title.AddChild(new InfoTip("production"));
        title.MoveChild(title.GetChild(title.GetChildCount() - 1), 2);
        box.AddChild(title);

        // Categories as icon tabs on one row (seven labelled tabs wrapped to two rows in a 360 px column).
        var filters = Widgets.Row(2);
        var group = new ButtonGroup();
        foreach (var cat in new[] { "All" }.Concat(Enum.GetNames<BuildingCategory>()))
        {
            var icon = cat == "All" ? "ui_build" : $"bld_cat_{cat.ToLowerInvariant()}";
            var button = Motion.OnPress(Widgets.IconButton(icon, cat == "All" ? "All" : "", cat == "All" ? "Every building" : $"{cat} buildings", () => Filter(cat), variation: "Tab"));
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = cat == "All";
            button.FocusMode = FocusModeEnum.All;
            _filters[cat] = button;
            filters.AddChild(button);
        }
        box.AddChild(filters);

        _grid.AddThemeConstantOverride("h_separation", Space.Sm);
        _grid.AddThemeConstantOverride("v_separation", Space.Sm);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 340) };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_grid);
        box.AddChild(scroll);
        foreach (var def in session.Sim.Data.Buildings.Values)
        {
            var tile = new Tile(def);
            tile.Pressed += () => { if (!tile.Locked) TypeChosen?.Invoke(def.Id); };
            _tiles[def.Id] = tile;
            _grid.AddChild(tile);
        }
        box.AddChild(_hint);
        _cursorTag.AddChild(_cursorText);
        AddChild(_cursorTag);
        Visible = false;
        SetPlacing(null);
    }

    private void Filter(string category)
    {
        _category = category;
        Refresh();
    }

    public void SetPlacing(BuildingDef? def)
    {
        _placing = def?.Id;
        _hint.Text = def is null
            ? "Pick a building, then click a free plot in the town."
            : $"Placing {def.Name}: click a free plot. Right-click or Esc cancels.";
        _hint.AddThemeColorOverride("font_color", def is null ? Palette.TextMuted : Palette.Amber);
        if (def is not null)
        {
            var sim = _session.Sim;
            _cursorText.Text = $"{def.Name} · {ColonyText.Cost(def.Level(1).Cost, sim.Data)}\nRight-click to cancel";
        }
        _cursorTag.Visible = false;
        SetProcess(def is not null);
        Refresh();
    }

    /// <summary>The cursor tag follows the mouse over the town while placing (hidden over the HUD itself).</summary>
    public override void _Process(double delta)
    {
        var show = _placing is not null && IsVisibleInTree() && GetViewport().GuiGetHoveredControl() is null;
        if (_cursorTag.Visible != show) _cursorTag.Visible = show;
        if (show) _cursorTag.Position = GetViewport().GetMousePosition() + new Vector2(18, 18);
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        foreach (var (id, tile) in _tiles)
        {
            var def = sim.Data.Buildings[id];
            tile.Visible = _category == "All" || def.Category.ToString() == _category;
            if (!tile.Visible) continue;
            string? lockReason = null;
            if (sim.Data.Schematics.GateOfBuildingLevel(id, 1) is { } gate && !Remnants.Core.Schematics.Schematics.Has(sim, gate))
                lockReason = $"Needs the {sim.Data.Schematics.All[gate].Name} schematic";
            else if (def.MinTownHallLevel > sim.TownHallLevel) lockReason = $"Needs Town Hall level {def.MinTownHallLevel}";
            else if (def.Unique && sim.State.Buildings.Any(b => b.DefId == id)) lockReason = "Already built (only one)";
            tile.Refresh(sim, lockReason, id == _placing);
        }
    }

    /// <summary>
    /// What a building is for, in one line: "Makes Wood", "Food from Potatoes, Beans…", "Crafts Machete, Spear…",
    /// "Sleeps 4". <paramref name="brief"/> leaves out the rate (a build-menu tile).
    /// </summary>
    public static string Purpose(Remnants.Core.Colony.ColonySim sim, BuildingDef def, bool brief = false)
    {
        var l1 = def.Level(1);
        if (def.Produces is { } res)
            return brief ? $"Makes {sim.Data.Resources[res].Name}" : $"Makes {sim.Data.Resources[res].Name} ({def.BaseOutput:0.##}/h per worker at full skill).";
        if (Remnants.Core.Colony.Harvesting.Options(sim.Data, def.Id) is { Count: > 0 } crops)
            return $"Food from {string.Join(", ", crops.Take(3).Select(c => c.Name))}{(crops.Count > 3 ? ", …" : "")}.";
        var recipes = sim.Data.RecipesFor(def.Id);
        if (def.Job is not null && recipes.Count > 0)
            return $"Crafts {string.Join(", ", recipes.Take(3).Select(r => r.Name))}{(recipes.Count > 3 ? ", …" : "")}.";
        if (l1.Beds > 0) return $"Sleeps {l1.Beds} at level 1.";
        if (l1.Patients > 0) return $"Treats {l1.Patients} patient{(l1.Patients == 1 ? "" : "s")} at level 1.";
        if (def.Id == sim.Data.Food?.Rules.CellarBuilding) return "Keeps produce and meat fresh for longer.";
        if (def.Id == sim.Data.Rules.TownHallBuildingId) return "The colony's heart: its level caps every other building's.";
        return def.Category switch
        {
            BuildingCategory.Power => "Part of the colony power grid.",
            _ => $"A {def.Category.ToString().ToLowerInvariant()} building.",
        };
    }

    /// <summary>One building in the grid.</summary>
    private partial class Tile : Button
    {
        private readonly BuildingDef _def;
        private readonly Label _name = Widgets.Label("", "");
        private readonly Label _purpose = Widgets.Label("", "Small");
        private readonly Label _lockText = Widgets.Label("", "Small", Palette.Danger);
        private readonly CostRow _costs = new() { PassMouse = true };
        private readonly TextureRect _lock = Icons.Rect(Icons.Get("stat_lock", 16), 16, Palette.Danger);
        private readonly TextureRect _icon;
        private readonly Unchanged _set = new();
        private bool? _placing;

        public bool Locked { get; private set; }

        public Tile() : this(new BuildingDef()) { }

        public Tile(BuildingDef def)
        {
            _def = def;
            FocusMode = FocusModeEnum.All;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(150, 96);
            ToggleMode = false;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            margin.SetAnchorsPreset(LayoutPreset.FullRect);
            foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", Space.Sm);
            AddChild(margin);
            var col = Widgets.Column(2);
            col.MouseFilter = MouseFilterEnum.Ignore;
            margin.AddChild(col);
            var top = Widgets.Row(6);
            top.MouseFilter = MouseFilterEnum.Ignore;
            _icon = Icons.Rect(Icons.Building(def.Id, def.Category.ToString(), 24), 24, Palette.Rust);
            _icon.MouseFilter = MouseFilterEnum.Ignore;
            top.AddChild(_icon);
            _name.Text = def.Name;
            _name.AddThemeFontOverride("font", Fonts.BodyBold);
            _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _name.ClipText = true;
            _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            top.AddChild(_name);
            _lock.MouseFilter = MouseFilterEnum.Ignore;
            top.AddChild(_lock);
            col.AddChild(top);
            foreach (var label in new[] { _purpose, _lockText })
            {
                label.ClipText = true;
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                label.MouseFilter = MouseFilterEnum.Ignore;
                col.AddChild(label);
            }
            col.AddChild(_costs);
            Motion.PressFeedback(this);
        }

        public void Refresh(Remnants.Core.Colony.ColonySim sim, string? lockReason, bool placing)
        {
            Locked = lockReason is not null;
            _set.Visible(_lock, Locked);
            _set.Modulate(_icon, Locked ? Palette.TextDim : placing ? Palette.Amber : Palette.Rust);
            _set.FontColor(_name, Locked ? Palette.TextDim : Palette.Text);
            _set.Modulate(this, Locked ? new Color(1, 1, 1, 0.8f) : Colors.White);
            _set.Text(_purpose, Purpose(sim, _def, brief: true));
            _set.Visible(_purpose, !Locked);
            _set.Visible(_lockText, Locked);
            if (Locked) _set.Text(_lockText, lockReason!);
            var cost = _def.Level(1).Cost;
            _costs.Set(cost.Select(kv => (kv.Key, sim.Data.Resources[kv.Key].Name, kv.Value, sim.State.Stockpile.Get(kv.Key))));
            if (_placing != placing)
            {
                _placing = placing;
                AddThemeStyleboxOverride("normal", placing ? HudTheme.CardSelectedStyle : HudTheme.CardStyle);
            }
            _set.Tooltip(this, Describe(sim, lockReason));
        }

        private string Describe(Remnants.Core.Colony.ColonySim sim, string? lockReason)
        {
            var l1 = _def.Level(1);
            var lines = new List<string> { $"{_def.Name} — {_def.Category}" };
            if (_def.Job is { } job && _def.Produces is { } res)
                lines.Add($"Workers make {sim.Data.Resources[res].Name} ({_def.BaseOutput:0.##}/h each at full skill). Job: {sim.Data.Jobs[job].Skill}.");
            else if (_def.Job is { } craft && sim.Data.RecipesFor(_def.Id).Count > 0)
                lines.Add($"Crafts {string.Join(", ", sim.Data.RecipesFor(_def.Id).Take(4).Select(r => r.Name))}{(sim.Data.RecipesFor(_def.Id).Count > 4 ? ", …" : "")}. Job: {sim.Data.Jobs[craft].Skill}.");
            else lines.Add(Purpose(sim, _def));
            if (l1.WorkerSlots > 0) lines.Add($"Level 1: {l1.WorkerSlots} worker slot{(l1.WorkerSlots == 1 ? "" : "s")}");
            if (l1.Beds > 0) lines.Add($"Level 1: {l1.Beds} beds");
            if (l1.Patients > 0) lines.Add($"Level 1: {l1.Patients} patient beds");
            lines.Add($"Up to level {_def.MaxLevel}.");
            lines.Add("Cost: " + string.Join(", ", l1.Cost.Select(kv =>
                $"{kv.Value:0} {sim.Data.Resources[kv.Key].Name}{(sim.State.Stockpile.Get(kv.Key) < kv.Value ? $" (have {sim.State.Stockpile.Get(kv.Key):0}, short {kv.Value - sim.State.Stockpile.Get(kv.Key):0})" : "")}")));
            if (lockReason is not null) lines.Add($"Locked: {lockReason}.");
            return string.Join("\n", lines);
        }
    }
}
