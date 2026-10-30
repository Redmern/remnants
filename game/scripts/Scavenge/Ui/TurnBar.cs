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
    private readonly Label _round = Widgets.Label("", "Heading");
    private readonly HBoxContainer _order = Widgets.Row(4);
    private readonly Label _ap = Widgets.Label("", "Number");
    private readonly Pips _apPips = new();
    private readonly Button _endTurn;
    private string _orderSignature = "";

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
        row.AddChild(new VSeparator());
        row.AddChild(Icons.Rect(Icons.Get("stat_initiative", 18), 18, Palette.TextMuted));
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
        ap.AddChild(_apPips);
        row.AddChild(ap);
        _endTurn = Widgets.IconButton("ui_end_turn", "End turn", "End this unit's turn", () => EndTurnPressed?.Invoke(), Hotkeys.KeyName("end_turn"), "Primary");
        _endTurn.CustomMinimumSize = new Vector2(120, 38);
        row.AddChild(_endTurn);
        row.AddChild(Widgets.IconButton("ui_leave", "Leave", "Leave the zone now: the run is suspended, the party stays out", () => LeavePressed?.Invoke(), variation: "Flat"));
        row.AddChild(Widgets.IconButton("ui_menu", "", "Menu: settings, leave, quit", () => MenuPressed?.Invoke(), Hotkeys.KeyName("pause_menu")));
    }

    public void Refresh(bool busy)
    {
        var state = _presenter.State;
        _zone.Text = _presenter.Sim.Data.Zones.TryGetValue(state.ZoneId, out var z) ? z.Name : state.ZoneId;
        _round.Text = $"ROUND {state.Round}";

        var entries = state.TurnOrder.Select((id, i) => (Unit: state.FindUnit(id), Index: i)).Where(e => e.Unit is { OnMap: true }).ToList();
        var signature = string.Join(",", entries.Select(e => $"{e.Unit!.Id}:{_presenter.CanSee(e.Unit)}:{e.Unit.Hp:0}"))
                        + $"|{state.TurnIndex}|{state.Finished}";
        if (signature != _orderSignature)
        {
            _orderSignature = signature;
            Widgets.Clear(_order);
            foreach (var (u, i) in entries)
            {
                var current = i == state.TurnIndex && !state.Finished;
                var chip = Chip(u!, current, _presenter.CanSee(u!));
                if (i < state.TurnIndex) chip.Modulate = new Color(1, 1, 1, 0.45f);
                _order.AddChild(chip);
            }
        }

        _busy = busy;
        _ap.Text = Status();
        var acting = _presenter.ActingUnit;
        _apPips.Visible = acting is not null;
        if (acting is not null) _apPips.Set(acting.Ap, _presenter.Sim.Profile(acting).MaxAp);
        _endTurn.Disabled = acting is null || busy;
    }

    /// <summary>AP of the acting unit; in multiplayer also whose turn it is and the turn timer.</summary>
    private string Status()
    {
        var acting = _presenter.ActingUnit;
        var remote = _presenter.Driver.TurnStatus();
        if (acting is not null)
            return $"{acting.Name.ToUpperInvariant()} · {acting.Ap}/{_presenter.Sim.Profile(acting).MaxAp} AP" + (remote is null ? "" : $" · {remote}");
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

    /// <summary>A turn-order chip: portrait with a side-colour edge; the acting unit larger and named.</summary>
    private Control Chip(Unit u, bool current, bool known)
    {
        var side = known ? SidePalette.For(_presenter, u) : SidePalette.Unknown;
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        var box = HudTheme.Box(current ? side.Darkened(0.6f) : Palette.Inset, side, current ? 2 : 1, radius: 3, padX: 3, padY: 3);
        box.BorderWidthBottom = 4;
        chip.AddThemeStyleboxOverride("panel", box);
        var row = Widgets.Row(6);
        row.MouseFilter = MouseFilterEnum.Ignore;
        chip.AddChild(row);
        var size = current ? 40 : 32;
        if (known)
        {
            var portrait = new Portrait(size) { MouseFilter = MouseFilterEnum.Ignore };
            portrait.Set(UnitPortrait.For(GetTree(), u), side.Darkened(0.55f));
            portrait.Alert = u.Status == UnitStatus.Downed;
            row.AddChild(portrait);
        }
        else
        {
            var unknown = new CenterContainer { CustomMinimumSize = new Vector2(size, size), MouseFilter = MouseFilterEnum.Ignore };
            unknown.AddChild(Widgets.Label("?", "Display", Palette.TextMuted));
            row.AddChild(unknown);
        }
        if (current)
        {
            var name = Widgets.Label(known ? u.Name : "Unseen", "", Colors.White, 15);
            name.AddThemeFontOverride("font", Fonts.BodyBold);
            row.AddChild(name);
        }
        chip.TooltipText = known ? $"{u.Name} · {ScavengeText.Kind(u)} · {ScavengeText.Status(u)} · HP {u.Hp:0}/{u.MaxHp:0}\nClick to select" : "Unseen";
        var id = u.Id;
        chip.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) UnitClicked?.Invoke(id);
        };
        return chip;
    }
}
