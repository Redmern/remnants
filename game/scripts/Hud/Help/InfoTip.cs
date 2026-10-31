using System;
using Godot;
using Remnants.Core.Help;

namespace Remnants.Game.Hud.Help;

/// <summary>
/// The small (i) placed next to a label, stat or section header. Hover, keyboard focus or a short touch-hold shows the
/// topic's short text (<see cref="HelpLayer.ShowTip"/>); a click, Enter or Space opens the detail popover with the long
/// text, live numbers and related topics. Drawn in code (a ring and an "i") so it needs no art and stays crisp at any UI
/// scale. <paramref name="context"/> adds what this spot knows (the survivor on the card, the zone on the row) to the
/// screen's ambient help context.
/// </summary>
public partial class InfoTip : Control
{
    public const float Size_ = 16;
    private const double HoldSeconds = 0.35;

    private bool _hot;
    private double _held = -1;

    public string Topic { get; set; } = "";
    public Func<HelpContext>? ContextFor { get; set; }

    public InfoTip() : this("") { }

    public InfoTip(string topic, Func<HelpContext>? context = null)
    {
        Topic = topic;
        ContextFor = context;
        CustomMinimumSize = new Vector2(Size_, Size_);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Help;
        MouseEntered += () => SetHot(true);
        MouseExited += () => SetHot(false);
        FocusEntered += () => SetHot(true);
        FocusExited += () => SetHot(false);
    }

    /// <summary>"Label (i)": a label and its tip in one row, the usual way to place one.</summary>
    public static HBoxContainer Beside(Control label, string topic, Func<HelpContext>? context = null)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(label);
        row.AddChild(new InfoTip(topic, context));
        return row;
    }

    private void SetHot(bool hot)
    {
        if (_hot == hot) return;
        _hot = hot;
        QueueRedraw();
        if (hot) HelpLayer.Instance?.ShowTip(this);
        else HelpLayer.Instance?.HideTip(this);
    }

    public void Open() => HelpLayer.Instance?.OpenDetail(Topic, ContextFor, this);

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                AcceptEvent();
                Open();
                break;
            case InputEventScreenTouch touch:
                AcceptEvent();
                if (touch.Pressed) _held = 0;
                else
                {
                    // A tap opens the detail; a hold showed the tooltip and lifting just hides it.
                    if (_held is >= 0 and < HoldSeconds) Open();
                    _held = -1;
                    SetHot(false);
                }
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter or Key.Space }:
                AcceptEvent();
                Open();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_held < 0) return;
        var was = _held;
        _held += delta;
        if (was < HoldSeconds && _held >= HoldSeconds) SetHot(true);
    }

    public override void _Draw()
    {
        var c = Size / 2;
        var r = Mathf.Min(Size.X, Size.Y) / 2 - 1;
        var tint = _hot ? Palette.Amber : Palette.TextMuted;
        if (_hot) DrawCircle(c, r, Palette.Amber with { A = 0.18f });
        DrawArc(c, r, 0, Mathf.Tau, 24, tint, 1.5f, true);
        DrawCircle(c + new Vector2(0, -r * 0.45f), 1.4f, tint);
        DrawLine(c + new Vector2(0, -r * 0.1f), c + new Vector2(0, r * 0.55f), tint, 1.8f, true);
        if (HasFocus()) DrawArc(c, r + 1.5f, 0, Mathf.Tau, 24, Palette.Amber, 1, true);
    }
}
