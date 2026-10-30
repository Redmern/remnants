using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// A framed portrait: the person's backdrop tint, the rendered figure from <see cref="PortraitStudio"/> and a bevelled
/// frame (amber when selected, red when <see cref="Alert"/>, dimmed when <see cref="Dim"/>, e.g. away or dead).
/// </summary>
public partial class Portrait : Control
{
    private Texture2D? _texture;
    private Color _backdrop = Palette.Raised;
    private bool _selected;
    private bool _alert;
    private bool _dim;

    public Portrait() : this(48) { }

    public Portrait(int size)
    {
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Pass;
        ClipContents = true;
    }

    public void Set(Texture2D texture, Color backdrop)
    {
        _texture = texture;
        _backdrop = backdrop;
        QueueRedraw();
    }

    public bool Selected { get => _selected; set { _selected = value; QueueRedraw(); } }
    public bool Alert { get => _alert; set { _alert = value; QueueRedraw(); } }
    public bool Dim { get => _dim; set { _dim = value; QueueRedraw(); } }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, _backdrop.Darkened(0.35f));
        // A soft lighter disc behind the head.
        DrawCircle(new Vector2(Size.X * 0.5f, Size.Y * 0.38f), Size.X * 0.42f, new Color(_backdrop, 0.9f));
        if (_texture is not null) DrawTextureRect(_texture, rect, false, _dim ? new Color(0.55f, 0.55f, 0.55f) : Colors.White);
        var frame = _selected ? Palette.Amber : _alert ? Palette.Danger : Palette.EdgeDark;
        DrawRect(rect, frame, false, _selected || _alert ? 2 : 1);
        if (!_selected && !_alert) DrawLine(new Vector2(1, 1), new Vector2(Size.X - 1, 1), new Color(1, 1, 1, 0.08f));
    }
}
