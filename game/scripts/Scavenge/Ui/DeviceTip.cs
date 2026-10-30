using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// A small card next to the cursor while it is over a known device: its name, what it does now and for whom (cyan while
/// it works for the Network), your hack in progress, and what hacking it does. Purely visual; the screen feeds it.
/// </summary>
public partial class DeviceTip : PanelContainer
{
    private readonly Label _title = Widgets.Label("", "Heading");
    private readonly Label _state = Widgets.Label("", "Small");
    private readonly Label _effect = Widgets.Wrapped("", "Small");

    public DeviceTip()
    {
        ThemeTypeVariation = "Card";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(260, 0);
        _effect.CustomMinimumSize = new Vector2(244, 0);
        Visible = false;
        var col = Widgets.Column(2);
        col.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(col);
        col.AddChild(_title);
        col.AddChild(_state);
        col.AddChild(_effect);
    }

    public void Show(ScavengePresenter presenter, ZoneDevice device, Vector2 screen)
    {
        var controller = presenter.ControllerOf(device);
        var mine = controller == presenter.Side;
        var network = controller == Unit.EnemySide;
        _title.Text = ScavengeText.Device(device.Kind).ToUpperInvariant();
        _title.AddThemeColorOverride("font_color", device.Disabled ? Palette.TextDim : mine ? Palette.Amber : network ? Palette.Network : SidePalette.OtherPlayer);
        var state = ScavengeText.DeviceState(device, controller, presenter.Side);
        if (Hacking.ProgressOf(device, presenter.Side) is { } p) state += $" · your hack {p.Done}/{p.Needed}";
        if (Hacking.LockedOut(presenter.State, device, presenter.Side)) state += " · you are locked out";
        _state.Text = state;
        _effect.Text = device.Disabled || device.Used || mine ? "" : $"Hack it: {ScavengeText.HackEffect(device.Kind)}.";
        _effect.Visible = _effect.Text.Length > 0;
        Position = screen + new Vector2(18, 18);
        Visible = true;
        Size = Vector2.Zero;
        CallDeferred(Control.MethodName.ResetSize);
    }

    public void HideTip() => Visible = false;
}
