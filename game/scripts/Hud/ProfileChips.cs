using System.Linq;
using Godot;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Hud;

/// <summary>A field profile (<see cref="UnitProfile"/>) as icon chips: movement, AP, initiative, sight, load, armor, dodge, attacks.</summary>
public static class ProfileChips
{
    public static HFlowContainer Build(UnitProfile p)
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        var moves = string.Join(" / ", p.Movement.Modes.Select(m => $"{m.Pattern} {m.Range}" + (m.ApCost > 1 ? $" ({m.ApCost} AP)" : "")));
        var sources = string.Join("\n", p.Movement.Modes.Select(m => $"{m.Pattern} {m.Range}: {m.Source ?? "on foot"}"));
        flow.AddChild(Widgets.Chip(Icons.Get("ui_move", 14), moves, null, $"Movement (like chess pieces)\n{sources}"));
        flow.AddChild(Widgets.Chip(Icons.Get("stat_ap", 14), $"{p.MaxAp}", null, "Action points per turn"));
        flow.AddChild(Widgets.Chip(Icons.Get("stat_initiative", 14), $"{p.Initiative:0}", null, "Initiative: who acts first each round"));
        flow.AddChild(Widgets.Chip(Icons.Get("stat_sight", 14), $"{p.Sight:0.#}", null, "Sight range in tiles"));
        var over = p.Load > p.CarryCapacity;
        flow.AddChild(Widgets.Chip(Icons.Get("stat_load", 14), $"{p.Load:0.#}/{p.CarryCapacity:0}{(over ? " !" : "")}", over ? Palette.Danger : null,
            over ? "Overloaded: slower and clumsier" : "Load / carry capacity (kg)"));
        flow.AddChild(Widgets.Chip(Icons.Get("stat_armor", 14), $"{p.Armor:0.#}", null, "Armor: damage taken off each hit"));
        if (p.Dodge > 0.005) flow.AddChild(Widgets.Chip(Icons.Get("stat_dodge", 14), Fmt.Percent(p.Dodge), null, "Dodge: chance to make a hit miss"));
        foreach (var a in p.Actions)
            flow.AddChild(Widgets.Chip(Icons.Get("ui_attack", 14), $"{a.Name} {a.MinDamage:0}–{a.MaxDamage:0}", Palette.Amber,
                $"{a.Name}: {a.Pattern} reach {a.Reach}, {a.MinDamage:0}–{a.MaxDamage:0} damage, {a.ApCost} AP, accuracy {Fmt.Percent(a.Accuracy)}", Palette.Amber));
        return flow;
    }
}
