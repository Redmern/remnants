using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Audio;

/// <summary>
/// Accessibility (Settings → Audio → Visual sound cues): when an event with a cue (<c>ambush</c>, <c>contact</c>,
/// <c>alarm</c> in <c>sound_events.json</c>) sounds somewhere off screen, a small HUD chip at the screen edge points
/// toward it for a few seconds. Sounds in view need no marker; cues without a position show at the top.
/// </summary>
public partial class SoundCues : CanvasLayer
{
    private const int MaxShown = 4;
    private const float Seconds = 2.6f, Inset = 56;

    private readonly Control _root = new() { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = HudTheme.Shared };

    public override void _Ready()
    {
        Layer = 40;
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        if (AudioManager.Instance is { } audio) audio.Cued += OnCued;
    }

    public override void _ExitTree()
    {
        if (AudioManager.Instance is { } audio) audio.Cued -= OnCued;
    }

    private void OnCued(string cue, Vector3? at)
    {
        if (!GameSettings.VisualSoundCues) return;
        var rect = _root.GetViewportRect();
        var camera = GetViewport().GetCamera3D();
        Vector2 spot;
        var arrow = "ui_chevron_up";
        if (at is { } world && camera is not null)
        {
            var screen = camera.UnprojectPosition(world);
            if (!camera.IsPositionBehind(world) && rect.Grow(-Inset / 2).HasPoint(screen)) return;  // in view
            if (camera.IsPositionBehind(world)) screen = rect.Size - screen;
            var centre = rect.Size / 2;
            var dir = (screen - centre).Normalized();
            arrow = Mathf.Abs(dir.X) > Mathf.Abs(dir.Y) ? dir.X < 0 ? "ui_chevron_left" : "ui_chevron_right" : dir.Y < 0 ? "ui_chevron_up" : "ui_chevron_down";
            spot = new Vector2(Mathf.Clamp(screen.X, Inset, rect.Size.X - Inset), Mathf.Clamp(screen.Y, Inset + 40, rect.Size.Y - Inset - 40));
        }
        else spot = new Vector2(rect.Size.X / 2, Inset + 50);
        Show(cue, spot, arrow);
    }

    private void Show(string cue, Vector2 spot, string arrow)
    {
        while (_root.GetChildCount() >= MaxShown) _root.GetChild(0).Free();
        var (text, icon, color) = cue switch
        {
            "ambush" => ("AMBUSH", "stat_warning", Palette.Danger),
            "alarm" => ("ALARM", "stat_linked", Palette.Network),
            _ => ("CONTACT", "stat_sight", Palette.Amber),
        };
        var chip = new PanelContainer { ThemeTypeVariation = "Chip", MouseFilter = Control.MouseFilterEnum.Ignore };
        var row = Widgets.Row(4);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.AddChild(row);
        row.AddChild(Icons.Rect(Icons.Get(arrow, 16), 16, color));
        row.AddChild(Icons.Rect(Icons.Get(icon, 16), 16, color));
        row.AddChild(Widgets.Label(text, "Number", color, 14));
        _root.AddChild(chip);
        chip.ResetSize();
        chip.Position = spot - chip.Size / 2;

        chip.Modulate = new Color(1, 1, 1, 0);
        var tween = chip.CreateTween();
        tween.TweenProperty(chip, "modulate:a", 1f, Motion.Fast);
        tween.TweenInterval(Seconds);
        tween.TweenProperty(chip, "modulate:a", 0f, 0.5f);
        tween.TweenCallback(Callable.From(chip.QueueFree));
    }
}
