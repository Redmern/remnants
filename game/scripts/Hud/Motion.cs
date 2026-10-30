using Godot;

namespace Remnants.Game.Hud;

/// <summary>Subtle tweens shared by the HUD: panels fade/slide in, buttons dip when pressed, numbers flash on change.</summary>
public static class Motion
{
    public const float Fast = 0.12f;
    public const float Normal = 0.2f;

    /// <summary>Shows <paramref name="control"/> with a short fade and a slide from <paramref name="from"/> pixels away.</summary>
    public static void Reveal(Control control, Vector2? from = null)
    {
        var wasVisible = control.Visible;
        control.Visible = true;
        if (wasVisible || !control.IsInsideTree()) return;
        var offset = from ?? new Vector2(0, 8);
        control.Modulate = new Color(1, 1, 1, 0);
        var target = control.Position;
        control.Position = target + offset;
        var tween = control.CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(control, "modulate:a", 1f, Normal);
        tween.TweenProperty(control, "position", target, Normal);
    }

    /// <summary>Toggles visibility, revealing with <see cref="Reveal"/> when it opens.</summary>
    public static void Toggle(Control control, Vector2? from = null)
    {
        if (control.Visible) control.Visible = false;
        else Reveal(control, from);
    }

    /// <summary>A quick dip on press, back on release.</summary>
    public static void PressFeedback(Button button)
    {
        button.Resized += () => button.PivotOffset = button.Size / 2;
        button.ButtonDown += () => button.CreateTween().TweenProperty(button, "scale", new Vector2(0.96f, 0.96f), 0.05f);
        button.ButtonUp += () => button.CreateTween().TweenProperty(button, "scale", Vector2.One, Fast);
    }

    /// <summary>Tints a label briefly, then fades back (a stock number went up or down).</summary>
    public static void Flash(CanvasItem item, Color color)
    {
        if (!item.IsInsideTree()) return;
        item.SelfModulate = color.Lightened(0.2f);
        item.CreateTween().TweenProperty(item, "self_modulate", Colors.White, 0.6f);
    }
}
