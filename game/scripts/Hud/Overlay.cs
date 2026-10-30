using System;
using System.Linq;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// Base for full-screen HUD overlays (pause menu, settings): a dimmed backdrop that swallows clicks, a centred
/// panel, Esc to close, and a fade in. Lives in the HUD canvas so it gets the theme and the UI scale; runs while
/// the tree is paused.
/// </summary>
public partial class Overlay : Control
{
    protected readonly VBoxContainer Body = Widgets.Column(10);
    private readonly PanelContainer _panel = new();

    public event Action? Closed;

    public Overlay() : this(420) { }

    protected Overlay(float width)
    {
        Theme = HudTheme.Shared;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        var dim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.02f, 0.72f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        _panel.CustomMinimumSize = new Vector2(width, 0);
        _panel.AddThemeStyleboxOverride("panel",
            PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true, Strip: Palette.Rust, StripHeight: 3), 22, 18));
        center.AddChild(_panel);
        _panel.AddChild(Body);
    }

    public void Open()
    {
        Visible = true;
        Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(this, "modulate:a", 1f, Motion.Fast);
        _panel.PivotOffset = _panel.Size / 2;
        _panel.Scale = new Vector2(0.97f, 0.97f);
        CreateTween().SetEase(Tween.EaseType.Out).TweenProperty(_panel, "scale", Vector2.One, Motion.Normal);
        OnOpened();
    }

    protected virtual void OnOpened() { }

    public virtual void Close()
    {
        if (!Visible) return;
        Visible = false;
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || !Hotkeys.Pressed(e, "pause_menu")) return;
        GetViewport().SetInputAsHandled();
        Close();
    }

    protected static Label Title(string text)
    {
        var title = Widgets.Label(text, "Title");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        return title;
    }
}

/// <summary>
/// The pause menu (Esc): a column of actions the screen passes in (resume, save, load, settings, main menu, quit).
/// Actions marked <c>confirm</c> ask first.
/// </summary>
public partial class PauseMenu : Overlay
{
    public sealed record Item(string Label, string Icon, Action Action, string? Confirm = null, string Hint = "");

    public PauseMenu() : this([]) { }

    public PauseMenu(Item[] items, string title = "PAUSED") : base(340)
    {
        Visible = false;
        Body.AddChild(Title(title));
        Body.AddChild(new HSeparator());
        foreach (var item in items)
        {
            var button = Widgets.IconButton(item.Icon, item.Label, item.Hint, () => Run(item), variation: item.Label == "Resume" ? "Primary" : "");
            button.CustomMinimumSize = new Vector2(0, 40);
            button.Alignment = HorizontalAlignment.Left;
            button.AddThemeFontSizeOverride("font_size", 16);
            button.FocusMode = FocusModeEnum.All;
            Body.AddChild(button);
        }
    }

    protected override void OnOpened()
    {
        if (Body.GetChildren().OfType<Button>().FirstOrDefault() is { } first) first.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Run(Item item)
    {
        if (item.Confirm is null)
        {
            item.Action();
            return;
        }
        ConfirmDialog.Ask(this, item.Label, item.Confirm, item.Label, item.Action);
    }
}
