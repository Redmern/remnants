using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// The HUD's motion vocabulary, one place for every duration and curve: panels fade/slide in and out, list rows enter
/// and leave, numbers count up, buttons dip when pressed, labels flash on change. Everything eases out over
/// <see cref="Fast"/> (120 ms: hover, press, small things) to <see cref="Slow"/> (220 ms: panels), and finishes at once
/// with the Reduce motion setting (<see cref="GameSettings.ReduceMotion"/>). Tweens only touch modulate, scale and the
/// position of free-standing controls, never a container child's position or size, so nothing makes the layout jump.
/// </summary>
public static class Motion
{
    public const float Fast = 0.12f;
    public const float Normal = 0.18f;
    public const float Slow = 0.22f;

    /// <summary>Delay between consecutive list rows entering; capped so a long list never waits.</summary>
    public const float Stagger = 0.02f;
    private const int MaxStagger = 6;

    /// <summary><paramref name="seconds"/>, or 0 with Reduce motion on.</summary>
    public static float D(float seconds) => GameSettings.ReduceMotion ? 0 : seconds;

    /// <summary>A tween on <paramref name="node"/> with the house curve (ease-out cubic), parallel when asked.</summary>
    public static Tween Tween(Node node, bool parallel = false)
    {
        var tween = node.CreateTween().SetEase(Godot.Tween.EaseType.Out).SetTrans(Godot.Tween.TransitionType.Cubic);
        return parallel ? tween.SetParallel() : tween;
    }

    // One running tween per control and purpose, so a new animation replaces the old one instead of fighting it.
    private static readonly ConditionalWeakTable<GodotObject, Tween> Running = new();
    private static readonly ConditionalWeakTable<GodotObject, Tween> Counting = new();
    private static readonly ConditionalWeakTable<GodotObject, Tween> Flashing = new();

    private static Tween Fresh(ConditionalWeakTable<GodotObject, Tween> table, Node node, bool parallel = false)
    {
        if (table.TryGetValue(node, out var old) && GodotObject.IsInstanceValid(old)) old.Kill();
        var tween = Tween(node, parallel);
        table.AddOrUpdate(node, tween);
        return tween;
    }

    /// <summary>Shows <paramref name="control"/> with a short fade and a slide from <paramref name="from"/> pixels away.</summary>
    public static void Reveal(Control control, Vector2? from = null)
    {
        var wasVisible = control.Visible && control.Modulate.A > 0.99f;
        control.Visible = true;
        if (wasVisible || !control.IsInsideTree() || GameSettings.ReduceMotion)
        {
            control.Modulate = Colors.White;
            return;
        }
        var tween = Fresh(Running, control, parallel: true);
        control.Modulate = new Color(1, 1, 1, 0);
        tween.TweenProperty(control, "modulate:a", 1f, Normal);
        // A container owns its children's positions: slide with the pivot-free scale trick instead of moving it.
        if (control.GetParent() is Container)
        {
            control.PivotOffset = from is { X: < 0 } ? new Vector2(0, 0) : new Vector2(control.Size.X, 0);
            control.Scale = new Vector2(0.98f, 0.98f);
            tween.TweenProperty(control, "scale", Vector2.One, Normal);
            return;
        }
        var target = control.Position;
        control.Position = target + (from ?? new Vector2(0, 8));
        tween.TweenProperty(control, "position", target, Normal);
    }

    /// <summary>Fades <paramref name="control"/> out (and slides it toward <paramref name="to"/>), then hides it.</summary>
    public static void Conceal(Control control, Vector2? to = null, Action? done = null)
    {
        if (!control.Visible) return;
        if (!control.IsInsideTree() || GameSettings.ReduceMotion)
        {
            control.Visible = false;
            done?.Invoke();
            return;
        }
        var tween = Fresh(Running, control, parallel: true);
        tween.TweenProperty(control, "modulate:a", 0f, Fast);
        Vector2? home = null;
        if (to is { } offset && control.GetParent() is not Container)
        {
            home = control.Position;
            tween.TweenProperty(control, "position", control.Position + offset, Fast);
        }
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            control.Visible = false;
            control.Modulate = Colors.White;
            if (home is { } p) control.Position = p;
            done?.Invoke();
        }));
    }

    /// <summary>Toggles visibility, revealing with <see cref="Reveal"/> when it opens and <see cref="Conceal"/> when it closes.</summary>
    public static void Toggle(Control control, Vector2? from = null)
    {
        if (control.Visible) Conceal(control);
        else Reveal(control, from);
    }

    /// <summary>A list row arriving: fades and grows in, a little after the row above it (<paramref name="index"/>).</summary>
    public static void Enter(Control row, int index = 0)
    {
        if (!row.IsInsideTree() || GameSettings.ReduceMotion) return;
        var tween = Fresh(Running, row, parallel: true);
        row.Modulate = new Color(1, 1, 1, 0);
        row.PivotOffset = new Vector2(0, row.Size.Y / 2);
        row.Scale = new Vector2(1, 0.9f);
        var delay = Math.Min(index, MaxStagger) * Stagger;
        tween.TweenProperty(row, "modulate:a", 1f, Normal).SetDelay(delay);
        tween.TweenProperty(row, "scale", Vector2.One, Normal).SetDelay(delay);
    }

    /// <summary>A list row leaving: fades out, then is freed.</summary>
    public static void Exit(Control row)
    {
        if (!row.IsInsideTree() || GameSettings.ReduceMotion)
        {
            row.QueueFree();
            return;
        }
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        var tween = Fresh(Running, row);
        tween.TweenProperty(row, "modulate:a", 0f, Fast);
        tween.TweenCallback(Callable.From(row.QueueFree));
    }

    /// <summary>A quick dip on press, back on release.</summary>
    public static void PressFeedback(Button button)
    {
        button.Resized += () => button.PivotOffset = button.Size / 2;
        button.ButtonDown += () =>
        {
            if (!GameSettings.ReduceMotion) Fresh(Running, button).TweenProperty(button, "scale", new Vector2(0.96f, 0.96f), 0.05f);
        };
        button.ButtonUp += () => Fresh(Running, button).TweenProperty(button, "scale", Vector2.One, D(Fast));
    }

    /// <summary>
    /// The button acts on press instead of release (toggles, tabs, list picks: nothing a slip of the mouse would regret),
    /// so it feels instant.
    /// </summary>
    public static T OnPress<T>(T button) where T : BaseButton
    {
        button.ActionMode = BaseButton.ActionModeEnum.Press;
        return button;
    }

    /// <summary>Tints a label briefly, then fades back (a stock number went up or down).</summary>
    public static void Flash(CanvasItem item, Color color)
    {
        if (!item.IsInsideTree() || GameSettings.ReduceMotion) return;
        item.SelfModulate = color.Lightened(0.2f);
        Fresh(Flashing, item).TweenProperty(item, "self_modulate", Colors.White, 0.6f);
    }

    /// <summary>
    /// Counts <paramref name="label"/> from <paramref name="from"/> to <paramref name="to"/> through <paramref name="format"/>
    /// (a resource stock changing). A new count replaces one still running, starting from where it got to.
    /// </summary>
    public static void CountUp(Label label, double from, double to, Func<double, string> format, float seconds = Slow)
    {
        if (!label.IsInsideTree() || GameSettings.ReduceMotion || Math.Abs(to - from) < 1e-9)
        {
            label.Text = format(to);
            return;
        }
        var tween = Fresh(Counting, label);
        tween.TweenMethod(Callable.From<double>(v => label.Text = format(v)), from, to, seconds);
    }
}
