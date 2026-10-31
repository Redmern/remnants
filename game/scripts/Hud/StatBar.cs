using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// A slim labelled bar: optional icon, the bar (colour from <see cref="Palette.Gauge"/> unless given) and the value as
/// text, so the colour is never the only signal. Animates toward new values.
/// </summary>
public partial class StatBar : HBoxContainer
{
    private readonly Bar _bar = new();
    private readonly Label _value = Widgets.Label("", "Number");
    private string? _text;
    private readonly TextureRect? _icon;

    public StatBar() : this(null) { }

    public StatBar(string? icon, int barWidth = 0, bool showValue = true)
    {
        AddThemeConstantOverride("separation", 4);
        MouseFilter = MouseFilterEnum.Pass;
        if (icon is not null)
        {
            _icon = Icons.Rect(Icons.Get(icon, 14), 14, Palette.TextMuted);
            AddChild(_icon);
        }
        _bar.CustomMinimumSize = new Vector2(barWidth, 6);
        _bar.SizeFlagsHorizontal = barWidth == 0 ? SizeFlags.ExpandFill : SizeFlags.Fill;
        _bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        AddChild(_bar);
        _value.AddThemeFontSizeOverride("font_size", 13);
        _value.CustomMinimumSize = new Vector2(24, 0);
        _value.HorizontalAlignment = HorizontalAlignment.Right;
        _value.Visible = showValue;
        AddChild(_value);
    }

    /// <summary>Sets the fill (0..1), the text next to it and the colour (default: green/amber/red by fill).</summary>
    public void Set(double fraction, string text, Color? color = null)
    {
        var f = Mathf.Clamp((float)fraction, 0, 1);
        _bar.SetTarget(f, color ?? Palette.Gauge(f));
        if (text == _text) return;
        _text = text;
        _value.Text = text;
    }

    private partial class Bar : Control
    {
        private float _shown = -1;
        private float _target;
        private Color _color = Palette.Good;

        public void SetTarget(float fraction, Color color)
        {
            if (_shown >= 0 && fraction == _target && color == _color) return;
            _target = fraction;
            _color = color;
            if (_shown < 0) _shown = fraction;
            SetProcess(!Mathf.IsEqualApprox(_shown, _target));
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            _shown = Mathf.MoveToward(_shown, _target, (float)delta * 1.5f);
            if (Mathf.IsEqualApprox(_shown, _target)) SetProcess(false);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var rect = new Rect2(Vector2.Zero, Size);
            DrawRect(rect, Palette.Void);
            if (_shown > 0) DrawRect(new Rect2(1, 1, (Size.X - 2) * _shown, Size.Y - 2), _color);
            DrawRect(rect, Palette.EdgeDark, false, 1);
        }
    }
}
