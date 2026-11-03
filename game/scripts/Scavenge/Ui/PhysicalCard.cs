using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// One card of the hand as a physical card: its face (the card button) sits in the hand's fan
/// (<see cref="ReplayJuice.FanAngle"/> / <see cref="ReplayJuice.FanDrop"/>), lifts, straightens and tilts toward the
/// mouse while hovered (<see cref="ReplayJuice.CardPose"/>), and flips in when drawn. With effects Off the cards lie flat
/// and still; reduce motion keeps the fan, a hovered card only comes to the front. Purely visual: the face is the same button, clicks go through as before.
/// </summary>
public partial class PhysicalCard : Control
{
    public const float Lift = 12, Drop = 10;

    public Button Face { get; }

    /// <summary>The key the hand diffs by (card id and slot owner).</summary>
    public string Key { get; }

    private readonly int _index, _count;
    private bool _hover;

    /// <summary>Card motion plays: effects on, no reduce motion, not skipping animations.</summary>
    public static bool Animate => GameSettings.EffectsLevel != EffectsLevel.Off && !GameSettings.ReduceMotion && !GameSettings.SkipAnimations;

    private static bool Fanned => GameSettings.EffectsLevel != EffectsLevel.Off;

    public PhysicalCard(Button face, string key, int index, int count)
    {
        Face = face;
        Key = key;
        MouseFilter = MouseFilterEnum.Ignore;
        var size = face.CustomMinimumSize;
        CustomMinimumSize = new Vector2(size.X, size.Y + Lift + Drop);
        (_index, _count) = (index, count);
        var rest = ReplayJuice.CardPose(index, count, hover: false, mouse: 0, GameSettings.Juice);
        AddChild(face);
        face.Size = size;
        face.PivotOffset = new Vector2(size.X / 2, size.Y);
        face.Position = new Vector2(0, Lift + (float)rest.Drop);
        face.RotationDegrees = (float)rest.Angle;
        face.MouseEntered += () => _hover = true;
        face.MouseExited += () => _hover = false;
    }

    /// <summary>A card just drawn: it flips face up after <paramref name="delay"/> seconds (the hand deals left to right).</summary>
    public void FlipIn(float delay)
    {
        if (!Animate) return;
        Face.Scale = new Vector2(0, 1);
        var back = Face.Modulate;
        Face.Modulate = new Color(0.35f, 0.3f, 0.25f, back.A);
        var tween = Face.CreateTween();
        tween.TweenInterval(delay);
        tween.TweenCallback(Callable.From(() => Audio.AudioManager.Fire("juice.card_draw")));
        tween.TweenProperty(Face, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(Face, "modulate", back, 0.12);
    }

    public override void _Process(double delta)
    {
        if (!Fanned) return;
        var hover = _hover && !Face.Disabled;
        var mouse = Face.GetLocalMousePosition().X / Mathf.Max(1, Face.Size.X) - 0.5f;
        var pose = ReplayJuice.CardPose(_index, _count, hover, mouse, GameSettings.Juice);
        var tilt = (float)pose.Angle;
        var y = Lift + (float)pose.Drop;
        var scale = (float)pose.Scale;
        ZIndex = pose.Raised ? 1 : 0;
        if (!Animate)
        {
            Face.RotationDegrees = tilt;
            Face.Position = new Vector2(0, y);
            if (Face.Scale.X > 0.99f) Face.Scale = Vector2.One * scale;
            return;
        }
        var k = Mathf.Min(1, (float)delta * 14);
        Face.RotationDegrees = Mathf.Lerp(Face.RotationDegrees, tilt, k);
        Face.Position = Face.Position.Lerp(new Vector2(0, y), k);
        // A drawn card's flip owns the scale until it is done.
        if (Face.Scale.X > 0.99f) Face.Scale = Face.Scale.Lerp(Vector2.One * scale, k);
    }
}
