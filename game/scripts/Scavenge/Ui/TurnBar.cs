using System;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// Zone and round, the initiative order for this round as portrait chips with a side-colour edge (the acting unit
/// larger, framed and named; those that already acted faded; unseen ones a "?"), the acting unit's AP as pips, and
/// End Turn (Space), Leave and the menu (Esc).
/// </summary>
public partial class TurnBar : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private bool _busy;
    private double _sinceTick;
    private readonly Label _zone = Widgets.Label("", "Display");
    private readonly PanelContainer _time;
    private readonly Label _round = Widgets.Label("", "Heading");
    private readonly HBoxContainer _order = Widgets.Row(4);
    private readonly Label _ap = Widgets.Label("", "Number");
    private readonly Pips _apPips = new();
    private readonly Button _endTurn;

    public event Action? EndTurnPressed;
    public event Action? LeavePressed;
    public event Action? MenuPressed;
    public event Action<int>? UnitClicked;

    public TurnBar(ScavengePresenter presenter)
    {
        _presenter = presenter;
        AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true, Strip: Palette.RustDark), 10, 4));
        var row = Widgets.Row(10);
        AddChild(row);
        var title = Widgets.Column(-4);
        _zone.AddThemeFontSizeOverride("font_size", 18);
        title.AddChild(_zone);
        title.AddChild(_round);
        row.AddChild(title);
        // The run's time of day (fixed for the mission; the server's in multiplayer), with what it means in a tooltip.
        var env = presenter.State.Environment;
        var (icon, color) = Colony.Ui.TopBar.PhaseLook(env.Phase);
        _time = Widgets.Chip(Icons.Get(icon, 16, "stat_time"), $"{env.Hour:00}:00 {env.Phase.ToString().ToUpperInvariant()}", color,
            env.Phase switch
            {
                Remnants.Core.Colony.DayPhase.Night => "Night: short sight and higher stealth, except to the Linked and cameras. More Husks, and they hear further. Flares light further.",
                Remnants.Core.Colony.DayPhase.Day => "Daylight.",
                _ => "Half light: sight a little shorter.",
            }, color);
        row.AddChild(_time);
        // The run's weather (it may change mid-run); its field effects in a tooltip.
        _weather = Widgets.Chip(Icons.Get("weather_clear", 16), "", Palette.TextMuted);
        row.AddChild(_weather);
        row.AddChild(new VSeparator());
        var orderIcon = Icons.Rect(Icons.Get("stat_initiative", 18), 18, Palette.TextMuted);
        orderIcon.MouseFilter = MouseFilterEnum.Stop;
        orderIcon.TooltipText = "Turn order this round, by initiative: the acting unit is large and named, those that already acted are faded.";
        row.AddChild(orderIcon);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 52),
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(_order);
        row.AddChild(scroll);
        row.AddChild(new VSeparator());
        var ap = Widgets.Column(2);
        ap.Alignment = BoxContainer.AlignmentMode.Center;
        _ap.AddThemeFontSizeOverride("font_size", 14);
        ap.AddChild(_ap);
        var pips = Widgets.Row(4);
        pips.AddChild(_apPips);
        pips.AddChild(new Hud.Help.InfoTip("controls"));
        ap.AddChild(pips);
        ap.MouseFilter = MouseFilterEnum.Pass;
        ap.TooltipText = "Action points of the unit whose turn it is. Moving and every order cost AP (■ = 1 AP);\nwhat is left is lost at End turn.";
        row.AddChild(ap);
        _endTurn = Widgets.IconButton("ui_end_turn", "End turn", "End this unit's turn", () => EndTurnPressed?.Invoke(), Hotkeys.KeyName("end_turn"), "Primary");
        _endTurn.CustomMinimumSize = new Vector2(120, 38);
        row.AddChild(_endTurn);
        row.AddChild(Widgets.IconButton("ui_leave", "Leave", "Leave the zone now: the run is suspended, the party stays out", () => LeavePressed?.Invoke(), variation: "Flat"));
        row.AddChild(Widgets.IconButton("ui_menu", "", "Menu: settings, leave, quit", () => MenuPressed?.Invoke(), Hotkeys.KeyName("pause_menu")));
    }

    private PanelContainer _weather = null!;
    private string? _weatherShown;

    private void RefreshWeather()
    {
        var env = _presenter.State.Environment;
        var key = $"{env.Weather}|{env.Intensity}|{env.Shifts.Count}";
        if (key == _weatherShown) return;
        _weatherShown = key;
        var kind = string.IsNullOrEmpty(env.Weather) ? "clear" : env.Weather;
        var color = Colony.Ui.TopBar.WeatherColor(kind);
        var row = _weather.GetChild(0);
        var icon = row.GetChild<TextureRect>(0);
        icon.Texture = Icons.Get($"weather_{kind}", 16, "weather_clear");
        icon.Modulate = color;
        var label = row.GetChild<Label>(1);
        label.Text = ScavengeText.WeatherName(env.Weather, env.Intensity).ToUpperInvariant();
        label.AddThemeColorOverride("font_color", color);
        _weather.TooltipText = ScavengeText.WeatherTip(_presenter.Sim.Data, env);
    }

    public void Refresh(bool busy)
    {
        RefreshWeather();
        var state = _presenter.State;
        _zone.Text = _presenter.Sim.Data.Zones.TryGetValue(state.ZoneId, out var z) ? z.Name : state.ZoneId;
        var subRounds = _presenter.Sim.Data.Rules.Turn.SubRounds;
        _round.Text = subRounds > 1 ? $"ROUND {state.Round} · {state.SubRound}/{subRounds}" : $"ROUND {state.Round}";

        // One chip per unit, kept across rounds and updated in place (side, acting, acted, HP tint); only a unit never
        // seen in the order before gets a new chip.
        var at = 0;
        _inOrder.Clear();
        for (var i = 0; i < state.TurnOrder.Count; i++)
        {
            if (state.FindUnit(state.TurnOrder[i]) is not { OnMap: true } u) continue;
            if (!_chips.TryGetValue(u.Id, out var chip))
            {
                chip = new OrderChip(u.Id);
                chip.Clicked += id => UnitClicked?.Invoke(id);
                _chips[u.Id] = chip;
                _order.AddChild(chip);
            }
            _inOrder.Add(u.Id);
            if (!chip.Visible) chip.Visible = true;
            if (chip.GetIndex() != at) _order.MoveChild(chip, at);
            at++;
            chip.Set(_presenter, u, current: i == state.TurnIndex && !state.Finished, acted: i < state.TurnIndex, _presenter.CanSee(u));
        }
        foreach (var (id, chip) in _chips)
            if (!_inOrder.Contains(id) && chip.Visible) chip.Visible = false;

        _busy = busy;
        _ap.Text = Status();
        var acting = _presenter.ActingUnit;
        _apPips.Visible = acting is not null;
        if (acting is not null) _apPips.Set(acting.Ap, _presenter.Sim.Profile(acting).MaxAp, acting.Suppressed ? Palette.Warn : Palette.Amber);
        _endTurn.Disabled = acting is null || busy;
    }

    private readonly System.Collections.Generic.Dictionary<int, OrderChip> _chips = new();
    private readonly System.Collections.Generic.HashSet<int> _inOrder = new();

    /// <summary>AP of the acting unit; in multiplayer also whose turn it is and the turn timer.</summary>
    private string Status()
    {
        var acting = _presenter.ActingUnit;
        var remote = _presenter.Driver.TurnStatus();
        // The acting chip names the unit; this is its AP, next to End turn.
        if (acting is not null)
            return $"{acting.Ap}/{_presenter.Sim.Profile(acting).MaxAp} AP LEFT" + (remote is null ? "" : $" · {remote}");
        if (_presenter.Done) return "RUN OVER";
        return remote ?? (_busy ? "ENEMY TURN…" : "WAITING…");
    }

    /// <summary>Keeps a multiplayer turn timer counting down between server updates.</summary>
    public override void _Process(double delta)
    {
        _sinceTick += delta;
        if (_sinceTick < 0.25 || _presenter.Driver is LocalDriver) return;
        _sinceTick = 0;
        _ap.Text = Status();
    }
}

/// <summary>
/// A turn-order chip, kept for its unit across rounds: portrait with a side-colour edge (a "?" while unseen), the acting
/// unit larger, framed and named, those that already acted faded, the bottom edge tinted by HP. Updated in place.
/// </summary>
public partial class OrderChip : PanelContainer
{
    private readonly int _unitId;
    private readonly Portrait _portrait = new(32) { MouseFilter = MouseFilterEnum.Ignore };
    private readonly CenterContainer _unknown = new() { CustomMinimumSize = new Vector2(32, 32), MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _name = Widgets.Label("", "", Colors.White, 15);
    private readonly StyleBoxFlat _box;
    private readonly Unchanged _set = new();
    private string _look = "";
    private string _portraitKey = "";

    public event Action<int>? Clicked;

    public OrderChip() : this(0) { }

    public OrderChip(int unitId)
    {
        _unitId = unitId;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        _box = HudTheme.Box(Palette.Inset, SidePalette.Unknown, 1, radius: 3, padX: 3, padY: 3);
        _box.BorderWidthBottom = 4;
        AddThemeStyleboxOverride("panel", _box);
        var row = Widgets.Row(6);
        row.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(row);
        row.AddChild(_portrait);
        _unknown.AddChild(Widgets.Label("?", "Display", Palette.TextMuted));
        row.AddChild(_unknown);
        _name.AddThemeFontOverride("font", Fonts.BodyBold);
        row.AddChild(_name);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Clicked?.Invoke(_unitId);
    }

    public void Set(ScavengePresenter presenter, Unit u, bool current, bool acted, bool known)
    {
        var side = known ? SidePalette.For(presenter, u) : SidePalette.Unknown;
        // The bottom edge shades from the side colour toward red as a seen unit loses HP.
        var health = known && u.MaxHp > 0 ? Mathf.Clamp((float)(u.Hp / u.MaxHp), 0, 1) : 1;
        var edge = health >= 0.999f ? side : side.Lerp(Palette.Danger, 1 - health);
        var look = $"{side}|{edge}|{current}";
        if (look != _look)
        {
            _look = look;
            _box.BgColor = current ? side.Darkened(0.6f) : Palette.Inset;
            _box.BorderColor = edge;
            _box.SetBorderWidthAll(current ? 2 : 1);
            _box.BorderWidthBottom = 4;
            _box.BorderBlend = false;
            QueueRedraw();
        }
        var size = current ? 40 : 32;
        var square = new Vector2(size, size);
        if (_portrait.CustomMinimumSize != square) _portrait.CustomMinimumSize = square;
        if (_unknown.CustomMinimumSize != square) _unknown.CustomMinimumSize = square;
        _set.Visible(_portrait, known);
        _set.Visible(_unknown, !known);
        if (known)
        {
            var key = $"{u.Id}|{string.Join(",", u.Equipment.Values)}|{side}";
            if (key != _portraitKey)
            {
                _portraitKey = key;
                _portrait.Set(UnitPortrait.For(GetTree(), u), side.Darkened(0.55f));
            }
            _portrait.Alert = u.Status == UnitStatus.Downed || health < 0.35f;
        }
        _set.Visible(_name, current);
        _set.Text(_name, known ? u.Name : "Unseen");
        _set.Modulate(this, acted ? new Color(1, 1, 1, 0.45f) : Colors.White);
        _set.Tooltip(this, known
            ? $"{u.Name} · {ScavengeText.Kind(u)} · {ScavengeText.Status(u)} · {Bestiary.Health(u, presenter.KnowledgeOf(u))}"
              + (acted ? "\nActed this round" : current ? "\nActing now" : "") + "\nClick to select"
            : "Unseen: heard or known to be there, but not in sight. It acts at this place in the order.");
    }
}

