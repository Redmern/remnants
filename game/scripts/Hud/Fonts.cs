using Godot;
using Godot.Collections;

namespace Remnants.Game.Hud;

/// <summary>
/// The bundled OFL font pair (game/ui/fonts): Barlow for body text, Barlow Condensed for headers and numbers.
/// Each falls back to Godot's default font for symbols Barlow lacks (♥, †, arrows).
/// </summary>
public static class Fonts
{
    private const string Dir = "res://ui/fonts/";

    private static Font? _body, _bodyBold, _bodyItalic, _display, _displayBold, _numbers;

    public static Font Body => _body ??= Load("Barlow-Regular.ttf");
    public static Font BodyBold => _bodyBold ??= Load("Barlow-SemiBold.ttf");
    public static Font BodyItalic => _bodyItalic ??= Load("Barlow-Italic.ttf");

    /// <summary>Condensed caps for headers and titles, slightly tracked out.</summary>
    public static Font Display => _display ??= Tracked(Load("BarlowCondensed-SemiBold.ttf"), 1);

    public static Font DisplayBold => _displayBold ??= Tracked(Load("BarlowCondensed-Bold.ttf"), 2);

    /// <summary>Condensed medium for numbers in chips, bars and counters.</summary>
    public static Font Numbers => _numbers ??= Load("BarlowCondensed-Medium.ttf");

    private static Font Load(string file)
    {
        var font = GD.Load<FontFile>(Dir + file);
        if (font is null) return ThemeDB.FallbackFont;
        font.Fallbacks = new Array<Font> { ThemeDB.FallbackFont };
        return font;
    }

    private static Font Tracked(Font font, int spacing)
    {
        var variation = new FontVariation { BaseFont = font };
        variation.SetSpacing(TextServer.SpacingType.Glyph, spacing);
        return variation;
    }
}
