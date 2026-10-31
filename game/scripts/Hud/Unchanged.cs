using System.Collections.Generic;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// Sets a label's text, a control's tooltip, texture, visibility or font colour only when the value differs from what
/// this cache last set. HUD panels refresh every sim tick, and each engine call (and each theme override, which
/// relayouts) costs even when nothing changed. One per widget owner; it remembers by control.
/// </summary>
public sealed class Unchanged
{
    private readonly Dictionary<(object, int), object?> _last = new();

    private bool Same(object control, int slot, object? value)
    {
        if (_last.TryGetValue((control, slot), out var old) && Equals(old, value)) return true;
        _last[(control, slot)] = value;
        return false;
    }

    public void Text(Label label, string text)
    {
        if (!Same(label, 0, text)) label.Text = text;
    }

    public void Tooltip(Control control, string text)
    {
        if (!Same(control, 1, text)) control.TooltipText = text;
    }

    public void Texture(TextureRect rect, Texture2D? texture)
    {
        if (!Same(rect, 2, texture)) rect.Texture = texture;
    }

    public void Visible(CanvasItem item, bool visible)
    {
        if (!Same(item, 3, visible)) item.Visible = visible;
    }

    public void FontColor(Control control, Color color)
    {
        if (!Same(control, 4, color)) control.AddThemeColorOverride("font_color", color);
    }

    public void Modulate(CanvasItem item, Color color)
    {
        if (!Same(item, 5, color)) item.Modulate = color;
    }
}
