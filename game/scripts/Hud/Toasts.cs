using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// A stack of short-lived notices for important events (death, birth, someone leaving, a party home). Each slides
/// in, waits, fades out; clicking one dismisses it. At most <see cref="MaxShown"/> at once.
/// </summary>
public partial class Toasts : VBoxContainer
{
    private const int MaxShown = 4;
    private const float Seconds = 6;

    public Toasts()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
        CustomMinimumSize = new Vector2(320, 0);
    }

    public void Show(string title, string body, string icon, Color accent)
    {
        while (GetChildCount() >= MaxShown) GetChild(0).Free();

        var toast = new PanelContainer { ThemeTypeVariation = "Toast", MouseFilter = MouseFilterEnum.Stop, TooltipText = "Click to dismiss" };
        var row = Widgets.Row(10);
        row.MouseFilter = MouseFilterEnum.Ignore;
        toast.AddChild(row);
        row.AddChild(Icons.Rect(Icons.Get(icon, 26), 26, accent));
        var text = Widgets.Column(0);
        text.MouseFilter = MouseFilterEnum.Ignore;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(text);
        var heading = Widgets.Label(title, "Display", accent);
        heading.AddThemeFontSizeOverride("font_size", 17);
        text.AddChild(heading);
        if (body.Length > 0)
        {
            var line = Widgets.Wrapped(body);
            line.AddThemeFontSizeOverride("font_size", 14);
            text.AddChild(line);
        }
        toast.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true }) Dismiss(toast);
        };
        AddChild(toast);

        toast.Modulate = new Color(1, 1, 1, 0);
        var tween = toast.CreateTween();
        tween.TweenProperty(toast, "modulate:a", 1f, Motion.Normal);
        tween.TweenInterval(Seconds);
        tween.TweenProperty(toast, "modulate:a", 0f, 0.6f);
        tween.TweenCallback(Callable.From(toast.QueueFree));
    }

    private static void Dismiss(Control toast)
    {
        var tween = toast.CreateTween();
        tween.TweenProperty(toast, "modulate:a", 0f, Motion.Fast);
        tween.TweenCallback(Callable.From(toast.QueueFree));
    }
}
