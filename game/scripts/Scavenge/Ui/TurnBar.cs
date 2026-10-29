using System;
using Godot;
using Remnants.Core.Scavenge;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// Zone and round, the initiative order for this round as side-coloured chips (the acting unit framed and bright,
/// units that already acted faded, unseen ones as grey "?"), AP left and End Turn.
/// </summary>
public partial class TurnBar : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly Label _zone = UiKit.Label(size: 15);
    private readonly HBoxContainer _order = new();
    private readonly Label _ap = UiKit.Label(size: 15);
    private readonly Button _endTurn;

    public event Action? EndTurnPressed;
    public event Action? LeavePressed;
    public event Action<int>? UnitClicked;

    public TurnBar(ScavengePresenter presenter)
    {
        _presenter = presenter;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        UiKit.Panel(this).AddChild(row);
        row.AddChild(_zone);
        row.AddChild(new VSeparator());
        row.AddChild(UiKit.Label("Order", 13, UiKit.Muted));
        _order.AddThemeConstantOverride("separation", 3);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 34),
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(_order);
        row.AddChild(scroll);
        row.AddChild(_ap);
        _endTurn = UiKit.Button("End Turn", () => EndTurnPressed?.Invoke(), 96);
        row.AddChild(_endTurn);
        row.AddChild(UiKit.Button("Leave", () => LeavePressed?.Invoke(), 60));
    }

    public void Refresh(bool busy)
    {
        var state = _presenter.State;
        var zone = _presenter.Sim.Data.Zones.TryGetValue(state.ZoneId, out var z) ? z.Name : state.ZoneId;
        _zone.Text = $"{zone}  ·  Round {state.Round}";

        foreach (var child in _order.GetChildren()) child.QueueFree();
        for (var i = 0; i < state.TurnOrder.Count; i++)
        {
            if (state.FindUnit(state.TurnOrder[i]) is not { } u || !u.OnMap) continue;
            var current = i == state.TurnIndex && !state.Finished;
            var known = _presenter.CanSee(u);
            var chip = Chip(known ? u.Name : "?", known ? SidePalette.For(_presenter, u) : SidePalette.Unknown, current);
            chip.TooltipText = known ? $"{ScavengeText.Kind(u)} · {ScavengeText.Status(u)}" : "Unseen";
            if (i < state.TurnIndex) chip.Modulate = new Color(1, 1, 1, 0.4f);
            var id = u.Id;
            chip.Pressed += () => UnitClicked?.Invoke(id);
            _order.AddChild(chip);
        }

        var acting = _presenter.ActingUnit;
        _ap.Text = acting is null
            ? busy ? "Enemy turn…" : _presenter.Done ? "Run over" : "Waiting…"
            : $"{acting.Name}: {acting.Ap}/{_presenter.Sim.Profile(acting).MaxAp} AP";
        _endTurn.Disabled = acting is null || busy;
    }

    /// <summary>A turn-order chip: a side-colour stripe on the left; the acting unit filled with its colour and framed.</summary>
    private static Button Chip(string text, Color side, bool current)
    {
        var chip = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(current ? 64 : 28, 30) };
        var box = HudTheme.Box(current ? side.Darkened(0.55f) : new Color(0.12f, 0.115f, 0.1f), current ? side : null, borderWidth: 2, padX: 8, padY: 3);
        box.BorderColor = side;
        box.BorderWidthLeft = 4;
        if (!current) box.BorderWidthTop = box.BorderWidthRight = box.BorderWidthBottom = 0;
        var hover = (StyleBoxFlat)box.Duplicate();
        hover.BgColor = box.BgColor.Lightened(0.12f);
        chip.AddThemeStyleboxOverride("normal", box);
        chip.AddThemeStyleboxOverride("pressed", box);
        chip.AddThemeStyleboxOverride("hover", hover);
        chip.AddThemeColorOverride("font_color", current ? Colors.White : side.Lightened(0.15f));
        chip.AddThemeColorOverride("font_hover_color", Colors.White);
        if (current) chip.AddThemeFontSizeOverride("font_size", 15);
        return chip;
    }
}
