using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The squad's chain this round, top centre under the turn bar: "CHAIN ×3" and the round's running damage total.
/// It punches up and warms as the chain grows, drops grey on a miss, and clears when the round starts
/// (<see cref="Remnants.Core.Scavenge.HitChain"/>; UI only, no rule). Hidden until the first link.
/// </summary>
public partial class ChainMeter : PanelContainer
{
    private readonly Label _chain = Widgets.Label("", "Number", Palette.Amber, 20);
    private readonly Label _total = Widgets.Label("", "Number", Palette.Text, 15);
    private Tween? _tween;

    public ChainMeter()
    {
        Name = "ChainMeter";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        AnchorLeft = AnchorRight = 0.5f;
        OffsetTop = 84;
        GrowHorizontal = GrowDirection.Both;
        var row = Widgets.Row(10);
        row.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_chain);
        row.AddChild(_total);
        AddChild(row);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(Palette.Panel, 0.82f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 3,
            ContentMarginBottom = 3,
            BorderWidthBottom = 2,
            BorderColor = Palette.Amber,
        });
        Resized += () => PivotOffset = Size / 2;
    }

    /// <summary>The chain grew to <paramref name="count"/> links; <paramref name="total"/> is the round's damage so far.</summary>
    public void Grow(int count, double total, bool animate)
    {
        Visible = true;
        _chain.Text = $"CHAIN ×{count}";
        _total.Text = total > 0 ? $"{total:0}" : "";
        // Warmer and bigger as it climbs: amber at 1, white-hot from about 6.
        var heat = Mathf.Clamp((count - 1) / 5f, 0, 1);
        _chain.AddThemeColorOverride("font_color", Palette.Amber.Lerp(new Color(1f, 0.95f, 0.8f), heat));
        Modulate = Colors.White;
        _tween?.Kill();
        if (!animate)
        {
            Scale = Vector2.One;
            return;
        }
        Scale = Vector2.One * (1.25f + 0.05f * Mathf.Min(count, 8));
        _tween = CreateTween();
        _tween.TweenProperty(this, "scale", Vector2.One, 0.25).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A miss broke the chain: the count goes, the round's total stays, dimmed.</summary>
    public void Break(double total)
    {
        _tween?.Kill();
        Scale = Vector2.One;
        _chain.Text = "CHAIN BROKEN";
        _chain.AddThemeColorOverride("font_color", Palette.TextMuted);
        _total.Text = total > 0 ? $"{total:0}" : "";
        Modulate = new Color(1, 1, 1, 0.75f);
    }

    /// <summary>A new round: the meter clears.</summary>
    public void Reset()
    {
        _tween?.Kill();
        Visible = false;
        Scale = Vector2.One;
    }
}
