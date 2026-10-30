using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// Procedural 9-slice textures for the worn-metal look: a grainy fill, a dark outline, a one-pixel bevel (light on
/// top/left for raised surfaces, dark for sunken ones), optional corner rivets and an optional coloured top strip.
/// Generated once at startup, so the repo carries no panel bitmaps.
/// </summary>
public static class PanelArt
{
    private const int Size = 32;
    private const int Margin = 6;

    public sealed record Look(
        Color Fill,
        Color Outline,
        float Bevel = 0.06f,
        bool Sunken = false,
        bool Rivets = false,
        float Grain = 0.018f,
        Color? Strip = null,
        int StripHeight = 2);

    public static StyleBoxTexture Box(Look look, int padX = 10, int padY = 8)
    {
        var box = new StyleBoxTexture
        {
            Texture = Texture(look),
            TextureMarginLeft = Margin, TextureMarginRight = Margin, TextureMarginTop = Margin, TextureMarginBottom = Margin,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
            ContentMarginLeft = padX, ContentMarginRight = padX,
            ContentMarginTop = padY + (look.Strip is null ? 0 : look.StripHeight), ContentMarginBottom = padY,
        };
        return box;
    }

    public static ImageTexture Texture(Look look)
    {
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        var light = look.Fill.Lightened(look.Bevel * 4);
        var dark = look.Fill.Darkened(0.35f);
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var c = Grainy(look.Fill, x, y, look.Grain);
            var edge = x == 0 || y == 0 || x == Size - 1 || y == Size - 1;
            if (edge) c = look.Outline;
            else if (y == 1 || x == 1) c = look.Sunken ? dark : light;
            else if (y == Size - 2 || x == Size - 2) c = look.Sunken ? light.Lerp(look.Fill, 0.6f) : dark;
            if (look.Strip is { } strip && !edge && y >= 1 && y <= look.StripHeight) c = strip;
            image.SetPixel(x, y, c);
        }
        if (look.Rivets)
            foreach (var (rx, ry) in new[] { (3, 3), (Size - 5, 3), (3, Size - 5), (Size - 5, Size - 5) })
            {
                var top = look.Strip is null ? ry : ry + look.StripHeight;
                image.SetPixel(rx, top, look.Fill.Lightened(0.28f));
                image.SetPixel(rx + 1, top, look.Fill.Lightened(0.12f));
                image.SetPixel(rx, top + 1, look.Fill.Lightened(0.12f));
                image.SetPixel(rx + 1, top + 1, look.Fill.Darkened(0.45f));
            }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Deterministic per-pixel grain so every run (and every screenshot) looks the same.</summary>
    private static Color Grainy(Color c, int x, int y, float amount)
    {
        var h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        var n = (h & 0xffff) / 65535f * 2 - 1;
        return new Color(c.R + n * amount, c.G + n * amount, c.B + n * amount * 0.9f, c.A);
    }
}
