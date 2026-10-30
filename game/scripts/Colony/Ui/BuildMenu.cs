using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Data;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The build menu: category tabs and a grid of tiles (icon, name, cost chips). Tiles the colony cannot build yet show
/// a lock and say why (Town Hall level, already built); unaffordable costs are marked. Tooltips describe what the
/// building does. Picking a tile starts plot placement in the town view.
/// </summary>
public partial class BuildMenu : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Label _hint = Widgets.Wrapped("", "Small");
    private readonly GridContainer _grid = new() { Columns = 2 };
    private readonly Dictionary<string, Tile> _tiles = new();
    private readonly Dictionary<string, Button> _filters = new();
    private string _category = "All";
    private string? _placing;

    public event Action<string>? TypeChosen;
    public event Action? Closed;

    public BuildMenu(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(0, 0);
        var box = Ui.Panel(this, 8);
        box.AddChild(Ui.TitleRow("Build", "ui_build", () => Closed?.Invoke(), Hotkeys.KeyName("open_build")));

        var filters = new HFlowContainer();
        filters.AddThemeConstantOverride("h_separation", 3);
        var group = new ButtonGroup();
        foreach (var cat in new[] { "All" }.Concat(Enum.GetNames<BuildingCategory>()))
        {
            var icon = cat == "All" ? "ui_build" : $"bld_cat_{cat.ToLowerInvariant()}";
            var button = Widgets.IconButton(icon, cat, $"Show {cat.ToLowerInvariant()} buildings", () => Filter(cat), variation: "Tab");
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = cat == "All";
            _filters[cat] = button;
            filters.AddChild(button);
        }
        box.AddChild(filters);

        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
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
            ? "Pick a building, then click a free plot."
            : $"Placing {def.Name}: click a free plot. Right-click or Esc cancels.";
        _hint.AddThemeColorOverride("font_color", def is null ? Palette.TextMuted : Palette.Amber);
        Refresh();
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
            if (def.MinTownHallLevel > sim.TownHallLevel) lockReason = $"Needs Town Hall level {def.MinTownHallLevel}";
            else if (def.Unique && sim.State.Buildings.Any(b => b.DefId == id)) lockReason = "Already built (only one allowed)";
            tile.Refresh(sim, lockReason, id == _placing);
        }
    }

    /// <summary>One building in the grid.</summary>
    private partial class Tile : Button
    {
        private readonly BuildingDef _def;
        private readonly Label _name = Widgets.Label("", "", size: 14);
        private readonly MarginContainer _costs = new() { MouseFilter = MouseFilterEnum.Ignore };
        private readonly TextureRect _lock = Icons.Rect(Icons.Get("stat_lock", 16), 16, Palette.Danger);
        private readonly TextureRect _icon;
        private string _signature = "";

        public bool Locked { get; private set; }

        public Tile() : this(new BuildingDef()) { }

        public Tile(BuildingDef def)
        {
            _def = def;
            FocusMode = FocusModeEnum.None;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(150, 80);
            ToggleMode = false;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            margin.SetAnchorsPreset(LayoutPreset.FullRect);
            foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 8);
            AddChild(margin);
            var col = Widgets.Column(4);
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
            top.AddChild(_name);
            _lock.MouseFilter = MouseFilterEnum.Ignore;
            top.AddChild(_lock);
            col.AddChild(top);
            col.AddChild(_costs);
            Motion.PressFeedback(this);
        }

        public void Refresh(Remnants.Core.Colony.ColonySim sim, string? lockReason, bool placing)
        {
            Locked = lockReason is not null;
            _lock.Visible = Locked;
            _icon.Modulate = Locked ? Palette.TextDim : placing ? Palette.Amber : Palette.Rust;
            _name.AddThemeColorOverride("font_color", Locked ? Palette.TextDim : Palette.Text);
            Modulate = Locked ? new Color(1, 1, 1, 0.75f) : Colors.White;
            var cost = _def.Level(1).Cost;
            var signature = string.Join(",", cost.Select(kv => $"{kv.Key}{sim.State.Stockpile.Get(kv.Key) >= kv.Value}")) + Locked;
            if (signature != _signature)
            {
                _signature = signature;
                Widgets.Clear(_costs);
                var chips = Widgets.CostChips(cost.Select(kv => (kv.Key, sim.Data.Resources[kv.Key].Name, kv.Value, sim.State.Stockpile.Get(kv.Key))));
                SetIgnore(chips);
                _costs.AddChild(chips);
            }
            AddThemeStyleboxOverride("normal", placing ? HudTheme.CardSelectedStyle : HudTheme.CardStyle);
            TooltipText = Describe(sim, lockReason);
        }

        /// <summary>Chips inside a button must not eat its clicks, but their tooltips are folded into the tile's.</summary>
        private static void SetIgnore(Control c)
        {
            c.MouseFilter = MouseFilterEnum.Ignore;
            foreach (var child in c.GetChildren().OfType<Control>()) SetIgnore(child);
        }

        private string Describe(Remnants.Core.Colony.ColonySim sim, string? lockReason)
        {
            var l1 = _def.Level(1);
            var lines = new List<string> { $"{_def.Name} — {_def.Category}" };
            if (_def.Job is { } job && _def.Produces is { } res)
                lines.Add($"Workers make {sim.Data.Resources[res].Name} ({_def.BaseOutput:0.##}/h each at full skill). Job: {sim.Data.Jobs[job].Skill}.");
            else if (_def.Job is { } craft && sim.Data.RecipesFor(_def.Id).Count > 0)
                lines.Add($"Crafts {string.Join(", ", sim.Data.RecipesFor(_def.Id).Take(4).Select(r => r.Name))}{(sim.Data.RecipesFor(_def.Id).Count > 4 ? ", …" : "")}. Job: {sim.Data.Jobs[craft].Skill}.");
            if (l1.WorkerSlots > 0) lines.Add($"Level 1: {l1.WorkerSlots} worker slot{(l1.WorkerSlots == 1 ? "" : "s")}");
            if (l1.Beds > 0) lines.Add($"Level 1: {l1.Beds} beds");
            if (l1.Patients > 0) lines.Add($"Level 1: {l1.Patients} patient beds");
            lines.Add($"Up to level {_def.MaxLevel}.");
            lines.Add("Cost: " + string.Join(", ", l1.Cost.Select(kv =>
                $"{kv.Value:0} {sim.Data.Resources[kv.Key].Name}{(sim.State.Stockpile.Get(kv.Key) < kv.Value ? $" (have {sim.State.Stockpile.Get(kv.Key):0})" : "")}")));
            if (lockReason is not null) lines.Add($"Locked: {lockReason}.");
            return string.Join("\n", lines);
        }
    }
}
