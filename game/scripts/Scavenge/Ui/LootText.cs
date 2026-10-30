using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>Shared wording and chips for loot piles and carry weight (tooltip, pickup panel, unit panel).</summary>
public static class LootText
{
    /// <summary>"Husk 3's remains", "Cache", "Your stash", "Dropped by Ana".</summary>
    public static string PileName(ScavengePresenter presenter, LootCache pile) => pile.Kind switch
    {
        LootPileKind.Remains => pile.Source is { } who ? $"{who}'s remains" : "Remains",
        LootPileKind.Dropped => pile.Source is { } who ? $"Left by {who}" : "Dropped items",
        LootPileKind.Stash => pile.PlayerId == presenter.PlayerId ? "Your stash" : pile.Source ?? "A stash",
        _ => "Cache",
    };

    public static Texture2D Icon(ScavengeData data, string id) =>
        data.Items.TryGetValue(id, out var def) ? Icons.Item(def.DefinedId, def.Kind.ToString(), 14) : Icons.Get("item_generic", 14);

    /// <summary>One chip per item kind: name (with its quality), count and weight.</summary>
    public static IEnumerable<Control> Chips(ScavengeData data, IEnumerable<string> items) =>
        items.GroupBy(id => id).Select(g =>
        {
            var kg = data.Items.TryGetValue(g.Key, out var def) ? def.Weight * g.Count() : 0;
            return (Control)Widgets.Chip(Icon(data, g.Key), $"{ScavengeText.Item(data, g.Key)}{(g.Count() > 1 ? $" ×{g.Count()}" : "")} · {kg:0.#} kg");
        });

    /// <summary>
    /// How a load reads against the movement rules' encumbrance steps: rust when light, warning once the first step slows
    /// the unit, danger at the last one or over capacity, with a short note.
    /// </summary>
    public static (Color Color, string Note) Carry(ScavengeData data, double load, double capacity)
    {
        var fraction = capacity > 0 ? load / capacity : 0;
        var steps = data.Rules.Movement.Encumbrance.OrderBy(s => s.Load).ToList();
        if (fraction > 1 + 1e-9) return (Palette.Danger, "Over capacity: drop something.");
        if (steps.Count > 0 && fraction >= steps[^1].Load) return (Palette.Danger, $"Heavily loaded: −{steps[^1].RangePenalty} move range.");
        if (steps.Count > 0 && fraction >= steps[0].Load) return (Palette.Warn, $"Loaded: −{steps[0].RangePenalty} move range.");
        var next = steps.FirstOrDefault();
        return (Palette.Rust, next is null ? "" : $"{Mathf.Max(0, (float)(next.Load * capacity - load)):0.#} kg before it slows you.");
    }
}
