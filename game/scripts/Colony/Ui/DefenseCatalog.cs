using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Raids;
using Side = Remnants.Core.Raids.Side;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Defend mode's catalogue (hotkey F / the TopBar's Defend): category tabs (walls and gates, posts and towers, lights /
/// turrets / alarm, traps) over a grid of tiles with cost chips (missing marked; the Engineer's trap discount included,
/// via <see cref="DefenseHandlers.Cost"/>) and a lock with the reason for a missing schematic or Town Hall level. Picking
/// a tile starts placement; the footer says what the ghost is doing (facing, R to turn, Core's reason when it cannot go
/// there) and opens the repair queue.
/// </summary>
public partial class DefenseCatalog : PanelContainer
{
    private readonly ColonySession _session;
    private readonly GridContainer _grid = new() { Columns = 2 };
    private readonly Dictionary<string, Tile> _tiles = new();
    private readonly Dictionary<DefenseCategory, Button> _tabs = new();
    private readonly Label _hint = Widgets.Wrapped("", "Small");
    private readonly PanelContainer _verdict;
    private readonly Button _repairs;
    private DefenseCategory _category = DefenseCategory.Walls;
    private string? _placing;

    public event Action<string>? TypeChosen;
    public event Action? Closed;
    public event Action? RepairsPressed;

    public DefenseCatalog(ColonySession session)
    {
        _session = session;
        var box = Ui.Panel(this, 8);
        box.AddChild(Ui.TitleRow("Defend", "ui_defend", () => Closed?.Invoke(), Hotkeys.KeyName("open_defend")));

        var tabs = new HFlowContainer();
        tabs.AddThemeConstantOverride("h_separation", 3);
        var group = new ButtonGroup();
        foreach (var cat in Enum.GetValues<DefenseCategory>())
        {
            var button = Widgets.IconButton(RaidText.CategoryIcon(cat), RaidText.CategoryName(cat), RaidText.CategoryTip(cat), () => Filter(cat), variation: "Tab");
            button.Icon = Icons.Get(RaidText.CategoryIcon(cat), 18, "ui_defend");
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = cat == _category;
            _tabs[cat] = button;
            tabs.AddChild(button);
        }
        box.AddChild(tabs);

        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        _grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 260) };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.AddChild(_grid);
        box.AddChild(scroll);
        if (session.Sim.Raids is { } raids)
            foreach (var def in raids.Def.Defenses)
            {
                var tile = new Tile(def);
                tile.Pressed += () => { if (!tile.Locked) TypeChosen?.Invoke(def.Id); };
                _tiles[def.Id] = tile;
                _grid.AddChild(tile);
            }

        _verdict = Widgets.Chip(Icons.Get("stat_warning", 16), "", Palette.Danger, "", Palette.Danger);
        _verdict.Visible = false;
        box.AddChild(_hint);
        box.AddChild(_verdict);
        var footer = Widgets.Row(6);
        _repairs = Widgets.IconButton("ui_repair", "Repairs", "The repair queue: damaged buildings and defenses", () => RepairsPressed?.Invoke());
        _repairs.Icon = Icons.Get("ui_repair", 18, "skill_crafting");
        footer.AddChild(_repairs);
        footer.AddChild(Widgets.Spacer());
        footer.AddChild(Widgets.Label("Right-click or Esc leaves", "Small"));
        box.AddChild(footer);
        Visible = false;
    }

    private void Filter(DefenseCategory category)
    {
        _category = category;
        Refresh();
    }

    /// <summary>Shows the tab holding this defense (when placement starts from elsewhere, e.g. a debug demo).</summary>
    public void ShowCategoryOf(string defId)
    {
        if (_session.Sim.Raids?.Defenses.GetValueOrDefault(defId) is { } def) _category = RaidText.Category(def.Kind);
        _tabs[_category].ButtonPressed = true;
        Refresh();
    }

    /// <summary>The defense being placed (or null), its facing and the ghost's verdict from Core.</summary>
    public void SetPlacing(string? defId, Side facing, Remnants.Core.Colony.CommandResult? verdict)
    {
        _placing = defId;
        var def = defId is null ? null : _session.Sim.Raids?.Defenses.GetValueOrDefault(defId);
        if (def is null)
        {
            _hint.Text = "Pick a defense, then click a free cell. Click a placed defense to manage it.";
            _hint.AddThemeColorOverride("font_color", Palette.TextMuted);
            _verdict.Visible = false;
        }
        else
        {
            var line = def.Kind is DefenseKind.Wall or DefenseKind.Barricade or DefenseKind.Trap ? " Drag for a line." : "";
            var turn = def.Kind is DefenseKind.Wall or DefenseKind.Gate or DefenseKind.Barricade ? "runs across" : "faces";
            _hint.Text = $"Placing {def.Name}: it {turn} the {RaidText.Side(facing)} (R turns it). Click a cell.{line}";
            _hint.AddThemeColorOverride("font_color", Palette.Amber);
            _verdict.Visible = verdict is { Ok: false };
            if (verdict is { Ok: false } v) _verdict.GetChild(0).GetChild<Label>(1).Text = v.Message;
        }
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        if (sim.Raids is not { } raids) return;
        foreach (var (id, tile) in _tiles)
        {
            var def = raids.Defense(id);
            tile.Visible = RaidText.Category(def.Kind) == _category;
            if (tile.Visible) tile.Refresh(sim, raids, RaidText.LockReason(sim, raids, def), id == _placing);
        }
        var queued = sim.State.Defense.Repairs.Count;
        var waiting = RaidText.Unqueued(sim, raids).Count();
        _repairs.Text = queued + waiting > 0 ? $"Repairs ({queued} queued{(waiting > 0 ? $", {waiting} damaged" : "")})" : "Repairs";
    }

    /// <summary>One defense in the grid: icon, name, lock, cost chips.</summary>
    private partial class Tile : Button
    {
        private readonly DefenseDef _def;
        private readonly Label _name = Widgets.Label("", "", size: 14);
        private readonly Label _stats = Widgets.Label("", "Small");
        private readonly MarginContainer _costs = new() { MouseFilter = MouseFilterEnum.Ignore };
        private readonly TextureRect _lock = Icons.Rect(Icons.Get("stat_lock", 16), 16, Palette.Danger);
        private readonly TextureRect _icon;
        private string _signature = "";

        public bool Locked { get; private set; }

        public Tile() : this(new DefenseDef()) { }

        public Tile(DefenseDef def)
        {
            _def = def;
            FocusMode = FocusModeEnum.None;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(150, 84);
            MouseDefaultCursorShape = CursorShape.PointingHand;
            var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            margin.SetAnchorsPreset(LayoutPreset.FullRect);
            foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 7);
            AddChild(margin);
            var col = Widgets.Column(3);
            col.MouseFilter = MouseFilterEnum.Ignore;
            margin.AddChild(col);
            var top = Widgets.Row(6);
            top.MouseFilter = MouseFilterEnum.Ignore;
            _icon = Icons.Rect(Icons.Get(RaidText.Icon(def), 22, RaidText.IconFallback(def), "ui_defend"), 22, Palette.Rust);
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
            _stats.Text = Short(def);
            _stats.ClipText = true;
            _stats.AddThemeFontSizeOverride("font_size", 12);
            col.AddChild(_stats);
            col.AddChild(_costs);
            Motion.PressFeedback(this);
        }

        /// <summary>The one or two numbers that matter for the kind.</summary>
        private static string Short(DefenseDef d) => d.Kind switch
        {
            DefenseKind.Wall or DefenseKind.Gate or DefenseKind.Barricade => $"soak {d.Soak:0.#} · {d.Hp:0} HP",
            DefenseKind.Post or DefenseKind.Tower => $"{d.GuardSlots} guard{(d.GuardSlots == 1 ? "" : "s")}" + (d.WarningHours > 0 ? $" · +{d.WarningHours:0} h" : $" · def +{d.Defense:0}"),
            DefenseKind.Turret => $"def +{d.Defense:0} · {d.Arc}° arc",
            DefenseKind.Light => $"lit, range {d.Range}",
            DefenseKind.Alarm => "wakes the colony",
            _ => d.TrapDamage > 0 ? $"hits {d.TrapDamage:0} · {(d.Rearmable ? "re-arms" : "once")}" : $"+{d.WarningHours:0} h warning",
        };

        public void Refresh(Remnants.Core.Colony.ColonySim sim, RaidData raids, string? lockReason, bool placing)
        {
            Locked = lockReason is not null;
            _lock.Visible = Locked;
            _icon.Modulate = Locked ? Palette.TextDim : placing ? Palette.Amber : Palette.Rust;
            _name.AddThemeColorOverride("font_color", Locked ? Palette.TextDim : Palette.Text);
            Modulate = Locked ? new Color(1, 1, 1, 0.75f) : Colors.White;
            var cost = DefenseHandlers.Cost(sim, _def, _def.Cost);
            var signature = RaidText.Signature(sim, cost) + Locked;
            if (signature != _signature)
            {
                _signature = signature;
                Widgets.Clear(_costs);
                if (Locked)
                {
                    var why = Widgets.Label(lockReason!, "Small", Palette.Danger);
                    why.ClipText = true;
                    why.MouseFilter = MouseFilterEnum.Ignore;
                    _costs.AddChild(why);
                }
                else
                {
                    var chips = Widgets.CostChips(RaidText.Costs(sim, cost));
                    SetIgnore(chips);
                    _costs.AddChild(chips);
                }
            }
            AddThemeStyleboxOverride("normal", placing ? HudTheme.CardSelectedStyle : HudTheme.CardStyle);
            TooltipText = RaidText.Describe(sim, _def, lockReason) + "\nCost: " + (cost.Count == 0 ? "free" : ColonyText.Cost(cost, sim.Data));
        }

        private static void SetIgnore(Control c)
        {
            c.MouseFilter = MouseFilterEnum.Ignore;
            foreach (var child in c.GetChildren().OfType<Control>()) SetIgnore(child);
        }
    }
}
