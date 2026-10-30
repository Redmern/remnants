using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Hud;

/// <summary>
/// Shared strings and chips for modded gear (the Gear Bench, the scavenge unit card, the expedition planner): a piece's
/// short title ("Pistol Mk II"), its mods by name, and mod chips. Network-grade mods are cyan; nothing else is.
/// </summary>
public static class GearText
{
    /// <summary>"Fine Pistol Mk II": the item as made, with its Mk (the mods listed apart).</summary>
    public static string Title(ScavengeData data, ItemDef item)
    {
        var made = item.UnmoddedId is { } u && data.Items.TryGetValue(u, out var plain) ? plain.Name : item.Name;
        return item.Mk > 1 && data.GearMods?.Mark(item.Mk) is { } mark ? $"{made} {mark.Name}" : made;
    }

    public static string Title(ScavengeData data, string itemId) =>
        data.Items.TryGetValue(itemId, out var item) ? Title(data, item) : itemId;

    /// <summary>The fitted mods' names in slot order.</summary>
    public static IEnumerable<string> ModNames(ScavengeData data, ItemDef item) =>
        item.Mods.Select(m => data.Items.TryGetValue(m, out var mod) ? mod.Name : m);

    /// <summary>"Pistol Mk II · Reflex Sight, Suppressor".</summary>
    public static string Line(ScavengeData data, ItemDef item) =>
        item.Mods.Count == 0 ? Title(data, item) : $"{Title(data, item)} · {string.Join(", ", ModNames(data, item))}";

    /// <summary>Network-grade (made from Linked parts): the only mods drawn in cyan.</summary>
    public static bool IsNetwork(ScavengeData data, string modItemId) => data.GearMods?.Mod(modItemId)?.Network == true;

    /// <summary>A mod's colour: cyan for a Network mod, else <paramref name="normal"/>.</summary>
    public static Color ModColor(ScavengeData data, string modItemId, Color normal) => IsNetwork(data, modItemId) ? Palette.Network : normal;

    /// <summary>A small chip per fitted mod (name; slot in the tooltip).</summary>
    public static HFlowContainer ModChips(ScavengeData data, ItemDef item, int fontSize = 12)
    {
        var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", 3);
        flow.AddThemeConstantOverride("v_separation", 3);
        foreach (var m in item.Mods) flow.AddChild(ModChip(data, m, fontSize));
        return flow;
    }

    public static PanelContainer ModChip(ScavengeData data, string modItemId, int fontSize = 12, string? suffix = null)
    {
        var name = data.Items.TryGetValue(modItemId, out var mod) ? mod.Name : modItemId;
        var slot = data.GearMods?.SlotOf(modItemId) is { } s ? data.GearMods.SlotName(s) : "";
        var color = ModColor(data, modItemId, Palette.Text.Lerp(Palette.Amber, 0.35f));
        var chip = new PanelContainer { ThemeTypeVariation = "Chip", MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = $"{name} ({slot})" + (IsNetwork(data, modItemId) ? "\nNetwork-grade" : "") };
        if (IsNetwork(data, modItemId)) chip.AddThemeStyleboxOverride("panel", HudTheme.Box(Palette.Inset, Palette.Network, 1, radius: 3, padX: 5, padY: 1));
        var label = Widgets.Label(name + (suffix ?? ""), "", color, fontSize);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.AddChild(label);
        return chip;
    }
}
