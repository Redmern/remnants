using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>Names, timers and descriptions of food for the Kitchen, pantry, farm and expedition panels.</summary>
public static class FoodText
{
    public static string ItemName(ColonySim sim, string id) => sim.Scavenge?.Items.GetValueOrDefault(id)?.Name ?? id.Replace('_', ' ');

    /// <summary>A recipe input key: the item's name, or "any meat" for <c>tag:meat</c>.</summary>
    public static string KeyName(ColonySim sim, string key) =>
        key.StartsWith(FoodData.TagPrefix) ? "any " + key[FoodData.TagPrefix.Length..].Replace('_', ' ') : ItemName(sim, key);

    public static Texture2D Icon(ColonySim sim, string itemId, int size = 16) =>
        sim.Scavenge?.Items.GetValueOrDefault(itemId) is { } def ? Icons.Item(def.DefinedId, def.Kind.ToString(), size) : Icons.Get("item_food", size);

    /// <summary>"2d 4h" until it spoils, "spoiled", or "keeps" for food that never spoils.</summary>
    public static string SpoilsIn(ColonySim sim, double? ticks) =>
        ticks is not { } t ? "keeps" : t <= 0 ? "spoiled" : ColonyText.Duration((int)System.Math.Ceiling(t), sim.Data.Rules);

    /// <summary>Colour of a spoil timer: red under a day, amber under three, muted otherwise.</summary>
    public static Color SpoilColor(ColonySim sim, double? ticks)
    {
        var day = sim.Data.Rules.TicksPerDay;
        return ticks is not { } t ? Palette.TextMuted : t <= 0 ? Palette.Danger : t < day ? Palette.Danger : t < 3 * day ? Palette.Warn : Palette.TextMuted;
    }

    /// <summary>Soonest spoil timer among fresh lots matching a recipe input key (null: none in stock, or none spoil).</summary>
    public static double? SoonestSpoil(ColonySim sim, string key)
    {
        if (sim.Data.Food is not { } food) return null;
        return sim.State.Food.Lots.Where(kv => food.Matches(kv.Key, key))
            .SelectMany(kv => kv.Value).Where(l => !l.Spoiled && l.TicksLeft is not null)
            .Select(l => l.TicksLeft).DefaultIfEmpty(null).Min();
    }

    public static string QualityName(ColonySim sim, string tier) =>
        sim.Scavenge?.Rules.Quality.Find(q => q.Id == tier)?.Name ?? tier;

    public static Color QualityColor(string tier) => tier switch
    {
        "fine" => Palette.Good,
        "crude" => Palette.Warn,
        _ => Palette.Text,
    };

    /// <summary>What a dish does: food value, mood and health per hour, buffs and their hours.</summary>
    public static string Effects(ColonySim sim, FoodItemDef def)
    {
        var parts = new List<string> { $"food {def.FoodValue:0.#}" };
        if (def.Meal is { } meal)
        {
            if (meal.MoodPerTick > 0) parts.Add($"mood +{meal.MoodPerTick:0.##}/h");
            if (meal.HealthPerTick > 0) parts.Add($"health +{meal.HealthPerTick:0.##}/h");
            foreach (var (buff, hours) in meal.Buffs) parts.Add($"{BuffName(sim, buff)} {hours:0}h");
            if (meal.FieldBuff is { } fb) parts.Add($"road: {BuffName(sim, fb)}");
            if (meal.Hot) parts.Add("hot");
        }
        parts.Add(def.ShelfDays <= 0 ? "keeps" : $"keeps {def.ShelfDays:0}d");
        return string.Join(" · ", parts);
    }

    public static string BuffName(ColonySim sim, string id) => sim.Data.Food?.Buffs.GetValueOrDefault(id)?.Name ?? id;

    /// <summary>A buff's description with its numbers ("+8% output, wounds heal ×1.25").</summary>
    public static string BuffTip(BuffDef def)
    {
        var parts = new List<string>();
        if (def.Output != 1) parts.Add($"{(def.Output > 1 ? "+" : "−")}{System.Math.Abs(def.Output - 1) * 100:0}% output");
        if (def.Healing != 1) parts.Add($"wounds heal ×{def.Healing:0.##}");
        if (def.MoodPerTick != 0) parts.Add($"mood {def.MoodPerTick:+0.##;−0.##}/h");
        if (def.HealthPerTick != 0) parts.Add($"health {def.HealthPerTick:+0.##;−0.##}/h");
        if (def.SleepDebtOnEnd > 0) parts.Add($"{def.SleepDebtOnEnd:0} h sleep debt when it wears off");
        if (def.FieldFirstRoundAp != 0) parts.Add($"{def.FieldFirstRoundAp:+0;−0} AP on the first round in the field");
        return $"{def.Name}: {def.Description}" + (parts.Count > 0 ? $"\n{string.Join(", ", parts)}" : "");
    }

    public static string BuffIcon(string id) => Icons.Has($"buff_{id}") ? $"buff_{id}" : "stat_mood";
}
