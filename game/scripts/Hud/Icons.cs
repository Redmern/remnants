using System.Collections.Generic;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Remnants.Game.Hud;

/// <summary>
/// UI icons by id. Line icons are white SVGs in <c>game/ui/icons/{id}.svg</c> (24×24, 2px stroke, tinted with
/// <c>Modulate</c>), rasterised at twice the asked size with mipmaps so they stay crisp under the UI scale. The .svg
/// files are imported with the "keep" importer, so exports ship them as-is.
/// <para>Ids are <c>{group}_{id}</c>: <c>res_</c>, <c>attr_</c>, <c>skill_</c>, <c>job_</c>, <c>body_</c>, <c>item_</c>,
/// <c>stance_</c>, <c>bld_</c>, <c>cat_</c>, <c>stat_</c>, <c>ui_</c>. A missing id falls back (see <see cref="Get"/>),
/// so new data never breaks the HUD; rendered gear icons from the asset pipeline win over the line icon when present
/// (<see cref="Item"/>).</para>
/// </summary>
public static class Icons
{
    private const string Dir = "res://ui/icons/";

    /// <summary>Rendered gear icons (PNG, any size) the asset pipeline may add: <c>{baseItemId}.png</c>.</summary>
    private const string GearDir = "res://assets/generated/icons/";

    private static readonly Dictionary<(string, int), Texture2D> Cache = new();

    public static bool Has(string id) => FileAccess.FileExists(Dir + id + ".svg");

    /// <summary>The icon for <paramref name="id"/> at <paramref name="size"/> logical pixels; tries each fallback id in turn, then a "?" box.</summary>
    public static Texture2D Get(string id, int size = 18, params string[] fallbacks)
    {
        if (Cache.TryGetValue((id, size), out var cached)) return cached;
        Texture2D? texture = null;
        foreach (var candidate in Prepend(id, fallbacks))
            if ((texture = Rasterise(candidate, size)) is not null) break;
        texture ??= Rasterise("item_generic", size) ?? Placeholder(size);
        return Cache[(id, size)] = texture;
    }

    public static Texture2D Resource(string resourceId, int size = 18) => Get($"res_{resourceId}", size, "item_material");

    public static Texture2D Skill(string skill, int size = 18) => Get($"skill_{skill.ToLowerInvariant()}", size, "skill_crafting");

    public static Texture2D Attribute(string attribute, int size = 18) => Get($"attr_{attribute.ToLowerInvariant()}", size);

    public static Texture2D Building(string defId, string category, int size = 18) =>
        Get($"bld_{defId}", size, $"bld_cat_{category.ToLowerInvariant()}", "ui_build");

    public static Texture2D BodyPart(string part, int size = 18) => Get($"body_{Snake(part)}", size, "body_full");

    /// <summary>An item: its rendered gear icon if the asset pipeline made one, else the line icon of its kind.</summary>
    public static Texture2D Item(string baseItemId, string kind, int size = 18)
    {
        var key = ($"gear:{baseItemId}", size);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var path = GearDir + baseItemId + ".png";
        var texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : Get($"item_{kind.ToLowerInvariant()}", size);
        return Cache[key] = texture;
    }

    /// <summary>A TextureRect showing an icon at a fixed logical size, tinted.</summary>
    public static TextureRect Rect(Texture2D icon, int size = 18, Color? tint = null) => new()
    {
        Texture = icon,
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        Modulate = tint ?? Colors.White,
        MouseFilter = Control.MouseFilterEnum.Pass,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    /// <summary>"LeftArm" → "left_arm".</summary>
    public static string Snake(string pascal)
    {
        var chars = new List<char>();
        for (var i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(pascal[i]));
        }
        return new string(chars.ToArray());
    }

    private static IEnumerable<string> Prepend(string id, string[] rest)
    {
        yield return id;
        foreach (var r in rest) yield return r;
    }

    private static Texture2D? Rasterise(string id, int size)
    {
        var path = Dir + id + ".svg";
        if (!FileAccess.FileExists(path)) return null;
        var image = new Image();
        if (image.LoadSvgFromString(FileAccess.GetFileAsString(path), size * 2 / 24f) != Error.Ok) return null;
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D Placeholder(int size)
    {
        var px = size * 2;
        var image = Image.CreateEmpty(px, px, false, Image.Format.Rgba8);
        for (var i = 0; i < px; i++)
        {
            image.SetPixel(i, 0, Colors.White);
            image.SetPixel(i, px - 1, Colors.White);
            image.SetPixel(0, i, Colors.White);
            image.SetPixel(px - 1, i, Colors.White);
        }
        return ImageTexture.CreateFromImage(image);
    }
}
