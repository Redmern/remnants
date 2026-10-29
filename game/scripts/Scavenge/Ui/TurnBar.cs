using System;
using Godot;
using Remnants.Core.Scavenge;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>Zone and round, the initiative order for this round (current unit marked), AP left and End Turn.</summary>
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
        _order.AddThemeConstantOverride("separation", 4);
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
            var chip = new Button
            {
                Text = known ? u.Name : "?",
                ToggleMode = true,
                ButtonPressed = current,
                FocusMode = FocusModeEnum.None,
                Disabled = i < state.TurnIndex,
                TooltipText = known ? $"{ScavengeText.Kind(u)} · {ScavengeText.Status(u)}" : "Unseen",
            };
            chip.AddThemeColorOverride("font_color", ChipColor(u, known));
            chip.AddThemeColorOverride("font_pressed_color", Colors.White);
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

    private Color ChipColor(Unit u, bool known) => !known ? UiKit.Muted
        : u.Kind == UnitKind.Enemy ? UiKit.Bad
        : _presenter.IsMine(u) ? UiKit.Good
        : Colors.White;
}
