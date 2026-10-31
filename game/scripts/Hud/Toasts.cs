using System;
using System.Collections.Generic;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// A stack of short-lived notices for important events (death, birth, someone leaving, a party home). Each slides
/// in, waits, fades out; clicking one dismisses it. At most <see cref="MaxShown"/> at once, small enough for a screen
/// side. A toast raised with the merge key of one changed in the last <see cref="MergeSeconds"/> updates that toast
/// instead of stacking another, so a burst (two level-ups for one survivor) reads as one notice.
/// </summary>
public partial class Toasts : VBoxContainer
{
    private const int MaxShown = 3;
    private const float Seconds = 6;
    private const float MergeSeconds = 4;

    /// <summary>A toast on screen: its merge key, how many notices it holds, when it last changed.</summary>
    private sealed class Shown(PanelContainer panel, Label heading, Label body, string? key)
    {
        public PanelContainer Panel { get; } = panel;
        public Label Heading { get; } = heading;
        public Label Body { get; } = body;
        public string? Key { get; } = key;
        public int Count { get; set; } = 1;
        public double At { get; set; }
        public Tween? Life { get; set; }
    }

    private readonly Dictionary<Control, Shown> _shown = new();

    public Toasts()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 4);
        Alignment = AlignmentMode.End;
        CustomMinimumSize = new Vector2(260, 0);
    }

    /// <summary>
    /// Shows a toast. With a <paramref name="key"/>, a recent toast with the same key takes the notice instead:
    /// <paramref name="merged"/> gets how many notices it now holds and gives its title and body (default: the new
    /// title, and the new body with "(+N more)").
    /// </summary>
    public void Show(string title, string body, string icon, Color accent, string? key = null, Func<int, (string Title, string Body)>? merged = null)
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (key is not null && Recent(key, now) is { } shown)
        {
            shown.Count++;
            (title, body) = merged?.Invoke(shown.Count) ?? (title, $"{body.TrimEnd('.')} (+{shown.Count - 1} more).");
            shown.Heading.Text = title;
            shown.Body.Text = body;
            shown.Body.Visible = body.Length > 0;
            shown.At = now;
            Live(shown);
            Motion.Flash(shown.Panel, accent);
            return;
        }

        while (GetChildCount() >= MaxShown) GetChild(0).Free();

        var toast = new PanelContainer { ThemeTypeVariation = "Toast", MouseFilter = MouseFilterEnum.Stop, TooltipText = "Click to dismiss" };
        var row = Widgets.Row(8);
        row.MouseFilter = MouseFilterEnum.Ignore;
        toast.AddChild(row);
        row.AddChild(Icons.Rect(Icons.Get(icon, 20), 20, accent));
        var text = Widgets.Column(0);
        text.MouseFilter = MouseFilterEnum.Ignore;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(text);
        var heading = Widgets.Label(title, "Display", accent);
        heading.AddThemeFontSizeOverride("font_size", 15);
        text.AddChild(heading);
        var line = Widgets.Wrapped(body);
        line.AddThemeFontSizeOverride("font_size", 13);
        line.Visible = body.Length > 0;
        text.AddChild(line);
        var entry = new Shown(toast, heading, line, key) { At = now };
        _shown[toast] = entry;
        toast.TreeExiting += () => _shown.Remove(toast);
        toast.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true }) Dismiss(entry);
        };
        AddChild(toast);

        toast.Modulate = new Color(1, 1, 1, 0);
        Live(entry);
    }

    private Shown? Recent(string key, double now)
    {
        foreach (var s in _shown.Values)
            if (s.Key == key && now - s.At <= MergeSeconds && !s.Panel.IsQueuedForDeletion()) return s;
        return null;
    }

    /// <summary>(Re)starts a toast's life: fade in, wait, fade out, free.</summary>
    private static void Live(Shown s)
    {
        s.Life?.Kill();
        var toast = s.Panel;
        var tween = s.Life = toast.CreateTween();
        tween.TweenProperty(toast, "modulate:a", 1f, Motion.D(Motion.Normal));
        tween.TweenInterval(Seconds);
        tween.TweenProperty(toast, "modulate:a", 0f, Motion.D(0.6f));
        tween.TweenCallback(Callable.From(toast.QueueFree));
    }

    private static void Dismiss(Shown s)
    {
        s.Life?.Kill();
        var tween = s.Life = s.Panel.CreateTween();
        tween.TweenProperty(s.Panel, "modulate:a", 0f, Motion.D(Motion.Fast));
        tween.TweenCallback(Callable.From(s.Panel.QueueFree));
    }
}
