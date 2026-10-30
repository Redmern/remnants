using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>View-model text and colours for the power grid (<see cref="Power.Nodes"/>): building panel, overlay, top bar. No rules.</summary>
public static class PowerText
{
    /// <summary>"Makes 6 of 9 (Fuel)", "Holds up to 60, 5 an hour in or out", "Draws 3 · fed · +25% crafting speed".</summary>
    public static string Line(ColonySim sim, PowerNode n)
    {
        var e = sim.Data.Electronics!;
        var level = sim.State.FindBuilding(n.BuildingId)?.Level ?? 1;
        return n.Role switch
        {
            PowerRole.Generator when !n.On => $"Generator switched off ({e.Power.Generators[n.DefId].Output[level - 1]:0.#} max).",
            PowerRole.Generator => $"Makes {n.Amount:0.#} of {e.Power.Generators[n.DefId].Output[level - 1]:0.#} an hour ({n.Note}).",
            PowerRole.Storage => $"Holds up to {n.Amount:0.#}, {e.Power.Storage[n.DefId].Rate[level - 1]:0.#} an hour in or out" + (n.On ? "." : " (off)."),
            PowerRole.Floodlight => $"Floodlight draws {n.Amount:0.#} after dusk · {(!n.On ? "off" : n.Fed ? "lit" : "dark")} · {n.Note}.",
            _ => $"Draws {n.Amount:0.#} · {State(sim, n)} · {n.Note}.",
        };
    }

    /// <summary>A workplace draws only while someone is on shift there: unfed then is idle, not starved.</summary>
    public static bool Idle(ColonySim sim, PowerNode n) =>
        n.Role == PowerRole.Consumer && n.On && !n.Fed && sim.State.FindBuilding(n.BuildingId) is { } b
        && sim.Data.Buildings[b.DefId].Job is not null && !Schedule.Crew(sim.State, sim.Data, b).Any();

    public static string State(ColonySim sim, PowerNode n) => !n.On ? "switched off" : n.Fed ? "fed" : Idle(sim, n) ? "idle (nobody on shift)" : "NOT FED";

    /// <summary>Fed moss, unfed red, idle or switched off dim; sources amber while they give, dim when cold.</summary>
    public static Color Color(ColonySim sim, PowerNode n) => n.Role switch
    {
        _ when !n.On => Palette.TextDim,
        _ when Idle(sim, n) => Palette.TextMuted,
        PowerRole.Generator => n.Fed ? Palette.Warn : Palette.TextMuted,
        PowerRole.Storage => Palette.Warn,
        PowerRole.Floodlight when !n.Fed => Palette.TextMuted,
        _ => n.Fed ? Palette.Good : Palette.Danger,
    };

    /// <summary>"+6.0" for what a source gives, "−3" for what a consumer draws.</summary>
    public static string Amount(ColonySim sim, PowerNode n) => n.Role switch
    {
        PowerRole.Generator => $"+{n.Amount:0.#}",
        PowerRole.Storage => $"{sim.State.Power.Stored:0}/{sim.PowerStorageCapacity:0}",
        _ => $"−{n.Amount:0.#}",
    };
}
