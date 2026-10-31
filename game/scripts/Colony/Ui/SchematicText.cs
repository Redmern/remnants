using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Schematics;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Shared wording and looks for schematics (archive, research, trader, toasts): the archive category of a schematic,
/// where it is found, its sources, Research Data amounts and the status chip. Reads Core only.
/// </summary>
public static class SchematicText
{
    /// <summary>The archive's groups, in display order.</summary>
    public static readonly string[] Categories = ["Buildings", "Weapons & armor", "Devices", "Prosthetics", "Techniques", "Vehicles"];

    /// <summary>Find tier → region (SCHEMATICS.md "The catalogue").</summary>
    private static readonly string[] Regions = ["Home Valley", "Ashgrove", "Iron Corridor", "The Black Grid"];

    public static string Region(int tier) => Regions[System.Math.Clamp(tier - 1, 0, Regions.Length - 1)];

    /// <summary>The archive group: from the first unlock's kind, and for recipes the item they make.</summary>
    public static string Category(ColonySim sim, SchematicDef def)
    {
        var u = def.Unlocks.FirstOrDefault();
        if (u is null) return "Devices";
        switch (u.Kind)
        {
            case UnlockKind.Building or UnlockKind.BuildingLevel: return "Buildings";
            case UnlockKind.Perk: return "Techniques";
            case UnlockKind.Prosthetic: return "Prosthetics";
            case UnlockKind.Vehicle: return "Vehicles";
        }
        var recipe = sim.Data.Recipes.GetValueOrDefault(u.Id);
        var kind = recipe?.Item is { } item ? sim.Scavenge?.Items.GetValueOrDefault(item)?.Kind : null;
        if (kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Clothing) return "Weapons & armor";
        if (kind is ItemKind.Prosthetic) return "Prosthetics";
        return recipe?.Building == "armory" ? "Weapons & armor" : "Devices";
    }

    public static string CategoryIcon(string category) => category switch
    {
        "Buildings" => "ui_build",
        "Weapons & armor" => "item_weapon",
        "Devices" => "item_tool",
        "Prosthetics" => "body_prosthetic",
        "Techniques" => "stat_perk",
        _ => "stat_travel",
    };

    /// <summary>"Hunting Rifle, Workshop L3".</summary>
    public static string Unlocks(ColonySim sim, SchematicDef def) => string.Join(", ", Schematics.DescribeUnlocks(sim.Data, def));

    /// <summary>" → Hunting Rifle", or "" when the unlock is named like the schematic (Watchtower → Watchtower).</summary>
    public static string Arrow(ColonySim sim, SchematicDef def)
    {
        var unlocks = Unlocks(sim, def);
        return unlocks == def.Name ? "" : $" → {unlocks}";
    }

    /// <summary>A story mission by name ("The Relay"), never its data id; "a story mission" when the story is not loaded.</summary>
    public static string Mission(ColonySim? sim, string missionId) =>
        sim?.Story?.Mission(missionId)?.Name is { Length: > 0 } name ? $"the \"{name}\" mission" : "a story mission";

    /// <summary>Player wording for where a known schematic came from ("Found on an expedition", "Researched").</summary>
    public static string LearnedFrom(SchematicSource source) => source switch
    {
        SchematicSource.Loot => "Found on an expedition",
        SchematicSource.Terminal => "Pulled from a terminal",
        SchematicSource.Kill => "Taken from an enemy",
        SchematicSource.Trade => "Bought from the trader",
        SchematicSource.Research => "Researched at the Town Hall",
        SchematicSource.Story => "A story reward",
        SchematicSource.Migration => "Known from before",
        _ => "Known from the start",
    };

    /// <summary>One line per source: "Loot: caches and lockers in Ashgrove or deeper", "Story: the "Relay" mission".</summary>
    public static IEnumerable<string> Sources(SchematicDef def, ColonySim? sim = null)
    {
        foreach (var source in def.Sources)
            yield return source switch
            {
                SchematicSource.Loot => $"Loot: caches and lockers in the {Region(def.Tier)} or deeper",
                SchematicSource.Terminal => "Terminal: hacked terminals",
                SchematicSource.Kill => "Kill: dropped by Linked enemies",
                SchematicSource.Trade => "Trade: the trader at the Radio Mast",
                SchematicSource.Research => "Research: at the Town Hall with Research Data",
                SchematicSource.Story => def.StoryMissions.Count > 0 ? $"Story: {string.Join(", ", def.StoryMissions.Select(m => Mission(sim, m)))}" : "Story: a mission reward",
                _ => source.ToString(),
            };
    }

    /// <summary>The archive tooltip: name, tier, what it unlocks, description, sources.</summary>
    public static string Tooltip(ColonySim sim, SchematicDef def)
    {
        var lines = new List<string> { $"{def.Name} · tier {def.Tier} ({Region(def.Tier)}){(def.Encrypted ? " · encrypted Network chip" : "")}" };
        lines.Add($"Unlocks: {Unlocks(sim, def)}");
        if (def.Description.Length > 0) lines.Add(def.Description);
        if (sim.State.Schematics.LearnedFrom.TryGetValue(def.Id, out var from)) lines.Add(LearnedFrom(from));
        lines.Add("Sources:");
        lines.AddRange(Sources(def, sim).Select(s => "  · " + s));
        return string.Join("\n", lines);
    }

    public static string ResearchDataId(ColonySim sim) => sim.Data.Schematics.Rules.ResearchDataResource;

    public static double ResearchData(ColonySim sim) => sim.State.Stockpile.Get(ResearchDataId(sim));

    /// <summary>A Research Data chip ("12 RD"), red with a mark when it is short of <paramref name="need"/>.</summary>
    public static PanelContainer DataChip(ColonySim sim, double? need = null, string tooltip = "")
    {
        var have = ResearchData(sim);
        var shortOf = need is { } n && have + 1e-9 < n;
        var text = need is { } amount ? $"{Fmt.Amount(amount)}{(shortOf ? " !" : "")}" : Fmt.Amount(have);
        var tip = tooltip.Length > 0 ? tooltip : need is { } x ? $"Research Data: need {Fmt.Amount(x)}, have {Fmt.Amount(have)}" : $"Research Data: {Fmt.Amount(have)}";
        return Widgets.Chip(Icons.Resource(ResearchDataId(sim), 16), text, shortOf ? Palette.Danger : Palette.Text, tip, shortOf ? Palette.Danger : Palette.Amber);
    }

    /// <summary>The icon for a schematic: a Network chip (cyan) for encrypted ones, the plan otherwise.</summary>
    public static string Icon(SchematicDef def) => def.Encrypted ? "item_chip" : "item_schematic";

    public static Color Accent(SchematicDef def) => def.Encrypted ? Palette.Network : Palette.Amber;

    /// <summary>A decrypt order for this schematic in some queue: (building, order, fraction done).</summary>
    public static (Building Building, double Done, bool Started)? Decrypting(ColonySim sim, string schematicId)
    {
        var recipe = Schematics.DecryptRecipeId(schematicId);
        foreach (var b in sim.State.Buildings)
            if (b.Queue.Find(o => o.RecipeId == recipe) is { } o)
                return (b, sim.Data.Recipes.TryGetValue(recipe, out var r) && r.Work > 0 ? o.Progress / r.Work : 0, o.Paid);
        return null;
    }

    /// <summary>
    /// The Lab that can decrypt this schematic now, or why none can ("Needs an Electronics Lab of level 2").
    /// </summary>
    public static Building? DecryptAt(ColonySim sim, SchematicDef def, out string why)
    {
        var rules = sim.Data.Schematics.Rules.Decrypt;
        var level = Schematics.DecryptLevel(sim, def);
        var name = sim.Data.Buildings.GetValueOrDefault(rules.Building)?.Name ?? rules.Building;
        var labs = sim.State.Buildings.Where(b => b.DefId == rules.Building).ToList();
        var lab = labs.Where(b => b.Level >= level).MaxBy(b => b.Level);
        why = lab is not null ? "" : labs.Count == 0 ? $"Needs an {name} (level {level})." : $"Needs the {name} at level {level} (it is level {labs.Max(b => b.Level)}).";
        return lab;
    }

    /// <summary>The schematic named in an event message (the longest name that appears), for toasts.</summary>
    public static SchematicDef? Named(ColonySim sim, string message) =>
        sim.Data.Schematics.All.Values.Where(s => message.Contains(s.Name)).MaxBy(s => s.Name.Length);
}
