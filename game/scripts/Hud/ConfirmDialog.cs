using System;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// A modal question over a dimmed screen: title, message, confirm and cancel. Esc cancels, Enter confirms.
/// Lives in the HUD's own canvas (not a Window) so it takes the HUD theme and UI scale.
/// </summary>
public partial class ConfirmDialog : Control
{
    private Action? _onConfirm;

    public ConfirmDialog() : this("", "", "OK", null) { }

    private ConfirmDialog(string title, string message, string confirm, Action? onConfirm, bool danger = false)
    {
        _onConfirm = onConfirm;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });

        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
        center.AddChild(panel);
        var box = Widgets.Column(12);
        panel.AddChild(box);
        box.AddChild(Widgets.Label(title, "Display"));
        box.AddChild(Widgets.Wrapped(message));
        var buttons = Widgets.Row(8);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        buttons.AddChild(Widgets.TextButton("Cancel", Close, "Esc", minWidth: 96));
        var ok = Widgets.TextButton(confirm, Confirm, "Enter", "Primary", 120);
        buttons.AddChild(ok);
        box.AddChild(buttons);
    }

    /// <summary>Asks; calls <paramref name="onConfirm"/> only on yes. Added on top of <paramref name="parent"/>.</summary>
    public static ConfirmDialog Ask(Node parent, string title, string message, string confirm, Action onConfirm, bool danger = false)
    {
        var dialog = new ConfirmDialog(title, message, confirm, onConfirm, danger) { Theme = HudTheme.Shared };
        parent.AddChild(dialog);
        dialog.Modulate = new Color(1, 1, 1, 0);
        dialog.CreateTween().TweenProperty(dialog, "modulate:a", 1f, Motion.Fast);
        return dialog;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape) Close();
        else if (key.Keycode is Key.Enter or Key.KpEnter) Confirm();
        else return;
        GetViewport().SetInputAsHandled();
    }

    private void Confirm()
    {
        var action = _onConfirm;
        _onConfirm = null;
        Close();
        action?.Invoke();
    }

    private void Close() => QueueFree();
}
