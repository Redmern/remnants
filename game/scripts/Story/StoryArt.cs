using System.Collections.Generic;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Story;
using Remnants.Game.Hud;

namespace Remnants.Game.Story;

/// <summary>
/// Story art from the asset pipeline (<c>assets/generated</c>): character portraits by character id and mood (the radio
/// variant for calls), Halcyon's emblem, map markers and the dialogue frames. Missing art falls back to a drawn
/// placeholder (an initial on the character's tint), so the story plays before the art lands.
/// </summary>
public static class StoryArt
{
    private const string Root = "res://assets/generated/";
    private static readonly Dictionary<string, Texture2D?> Cache = new();

    /// <summary>The asset name of each story character's art (characters.json id → <c>story_*</c>).</summary>
    private static readonly Dictionary<string, string> Asset = new()
    {
        ["iris"] = "story_iris_vahn", ["marrow"] = "story_abel_marrow", ["dee"] = "story_dee", ["juniper"] = "story_juniper",
        ["hale"] = "story_doc_hale", ["cole"] = "story_cole_brannigan", ["seven"] = "story_seven", ["pell"] = "story_pell_child",
    };

    /// <summary>Moods without portraits of their own borrow the nearest one that ships before falling back to neutral.</summary>
    private static readonly Dictionary<string, string> MoodAlias = new()
    {
        ["afraid"] = "worried", ["grief"] = "worried", ["ashamed"] = "worried", ["tired"] = "worried",
    };

    /// <summary>
    /// The body model of a story character (characters.json id, also a joined survivor's <c>UniqueId</c>): Pell by age,
    /// Seven in its colony blanket once it lives with you. Null for anyone else.
    /// </summary>
    public static string? BodyModel(string? characterId, bool child = false, bool inColony = false) => characterId switch
    {
        null => null,
        "pell" => child ? "story_pell_child" : "story_pell_adult",
        "seven" when inColony => "story_seven_colony",
        _ => Asset.GetValueOrDefault(characterId),
    };

    /// <summary>
    /// The portrait for a speaker: Pell grown up uses his adult bust; Halcyon its emblem; colony survivors are rendered
    /// (<see cref="PortraitStudio"/>) by the caller. Null when there is no art (draw <see cref="Placeholder"/>).
    /// </summary>
    public static Texture2D? Portrait(string characterId, string mood, bool radio, ColonySim? sim = null)
    {
        if (characterId == "halcyon") return Load("portraits/halcyon_idle_00.png");
        if (!Asset.TryGetValue(characterId, out var asset)) return null;
        if (characterId == "pell" && sim?.State.Survivors.Find(s => s.UniqueId == Campaign.PellId) is { IsChild: false }) asset = "story_pell_adult";
        if (characterId == StoryCharacters.Seven && sim is not null && StoryCharacters.SurvivorOf(sim, characterId) is not null) asset = "story_seven_colony";
        var variant = radio ? "radio" : mood;
        return Load($"portraits/{asset}_{variant}.png")
               ?? (MoodAlias.TryGetValue(variant, out var alias) ? Load($"portraits/{asset}_{alias}.png") : null)
               ?? Load($"portraits/{asset}_neutral.png");
    }

    /// <summary>A world map marker (marker_mission_main / _side, marker_radio_contact, marker_caravan).</summary>
    public static Texture2D? Marker(string id) => Load($"ui/{id}.png");

    /// <summary>The dialogue panel's 9-slice frame (radio or in person), or the HUD panel when the art is missing.</summary>
    public static StyleBox Frame(bool radio)
    {
        if (Load(radio ? "ui/frame_radio.png" : "ui/frame_dialogue.png") is { } texture)
        {
            var box = new StyleBoxTexture { Texture = texture };
            foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
            {
                box.SetTextureMargin(side, 48);
                box.SetContentMargin(side, side is Side.Left or Side.Right ? 26 : 20);
            }
            return box;
        }
        return PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true, Strip: radio ? Palette.Amber : Palette.Rust, StripHeight: 3), 22, 18);
    }

    /// <summary>A stand-in portrait: the initial on a tint picked from the id.</summary>
    public static Texture2D Placeholder(string id, string name)
    {
        var key = $"placeholder:{id}";
        if (Cache.TryGetValue(key, out var cached) && cached is not null) return cached;
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        var hue = (uint)id.GetHashCode() % 360 / 360f;
        image.Fill(Color.FromHsv(hue, 0.35f, 0.35f));
        var texture = ImageTexture.CreateFromImage(image);
        Cache[key] = texture;
        return texture;
    }

    /// <summary>A generated PNG: through Godot's import when it has run, else straight from the file (fresh syncs in dev).</summary>
    private static Texture2D? Load(string file)
    {
        var path = Root + file;
        if (Cache.TryGetValue(path, out var hit)) return hit;
        Texture2D? texture = null;
        if (ResourceLoader.Exists(path)) texture = ResourceLoader.Load<Texture2D>(path);
        else if (Godot.FileAccess.FileExists(path))
        {
            var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
            if (image is not null && !image.IsEmpty()) texture = ImageTexture.CreateFromImage(image);
        }
        Cache[path] = texture;
        return texture;
    }
}
