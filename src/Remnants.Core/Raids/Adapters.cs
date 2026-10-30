using Remnants.Core.Colony;
using Remnants.Core.Schematics;

namespace Remnants.Core.Raids;

/// <summary>
/// Defenses and schematics. Every defense that is not ungated is unlocked by a schematic of kind
/// <see cref="UnlockKind.Defense"/> in data/schematics.defense.json, which the schematics catalogue loads with the rest; a
/// defense no schematic names is default content (wooden wall, gate, spike strip, alarm bell, lookout tower).
/// </summary>
public static class DefenseSchematics
{
    public const string FileName = "schematics.defense.json";

    /// <summary>The schematic that unlocks a defense, or null when it is default (ungated).</summary>
    public static string? GateOf(ColonySim sim, string defenseId) => sim.Data.Schematics.GateOf(UnlockKind.Defense, defenseId);

    /// <summary>Whether the colony may build this defense.</summary>
    public static bool Unlocked(ColonySim sim, string defenseId) => GateOf(sim, defenseId) is not { } gate || Schematics.Schematics.Has(sim, gate);

    /// <summary>
    /// Defense schematics a beaten raid could drop: not known yet, findable as loot (<see cref="SchematicSource.Loot"/>; the
    /// catalogue's Kill source is kept for enemy drop tables, and raiders carry plans as loot).
    /// </summary>
    public static IEnumerable<SchematicDef> Droppable(ColonySim sim) =>
        sim.Data.Schematics.All.Values.Where(s => s.Available && s.Has(SchematicSource.Loot) && s.Unlocks.Any(u => u.Kind == UnlockKind.Defense)
                                                  && !Schematics.Schematics.Has(sim, s.Id)).OrderBy(s => s.Id, StringComparer.Ordinal);
}

/// <summary>The colony's weather, for raids: the weather kind over the colony at a tick, and raids.json's <c>weather</c> modifiers for it.</summary>
public static class RaidWeather
{
    public static string? At(ColonySim sim, long tick) =>
        Weather.Enabled(sim.Data) ? Weather.SpellAt(sim.State, sim.Data, sim.Data.Weather.ColonyRegion, tick).Kind : null;

    public static WeatherModifiers Modifiers(ColonySim sim, RaidData raids, long tick) =>
        At(sim, tick) is { } id && raids.Def.Weather.TryGetValue(id, out var w) ? w : new WeatherModifiers();
}

/// <summary>
/// The colony power grid, for defenses (turrets, floodlights). Optional: a defense cabled to a powered building gets its
/// <see cref="DefenseDef.PoweredBonus"/>, never a hard requirement. A defense counts as cabled when a building the grid
/// fed last tick (<see cref="Power.IsPowered"/>) stands within <see cref="CableCells"/> of it.
/// </summary>
public static class DefensePower
{
    public const int CableCells = 6;

    public static bool IsPowered(ColonySim sim, PlacedDefense d)
    {
        if (sim.Raids is not { } raids || sim.State.Power.Powered.Count == 0) return false;
        var grid = DefenseGrid.Of(sim, raids);
        return sim.State.Buildings.Any(b => Power.IsPowered(sim.State, b)
                                            && grid.PlotCenter(b.Plot) is var c && Math.Max(Math.Abs(c.X - d.X), Math.Abs(c.Y - d.Y)) <= CableCells);
    }

    /// <summary>1 + the def's powered bonus when powered, else 1.</summary>
    public static double Multiplier(ColonySim sim, PlacedDefense d, DefenseDef def) => IsPowered(sim, d) ? 1 + def.PoweredBonus : 1;
}

/// <summary>
/// Food a raid spoils, through the food system: Husks trample the standing crops of outdoor harvests (farms), raiders
/// carry off pantry food. Spoil ids in raids.json: <see cref="Crops"/> and <see cref="Food"/>; any other id is a resource.
/// </summary>
public static class CropDamage
{
    public const string Crops = "crops";
    public const string Food = "food";

    /// <summary>Takes this share of every outdoor harvest's standing crop; returns the units lost.</summary>
    public static double Trample(ColonySim sim, double share)
    {
        var lost = 0.0;
        foreach (var b in sim.State.Buildings.OrderBy(b => b.Id))
        {
            if (Harvesting.Current(sim.Data, b) is null || !sim.Data.Buildings[b.DefId].Outdoor) continue;
            foreach (var (item, progress) in b.HarvestProgress.ToList())
            {
                lost += progress * share;
                b.HarvestProgress[item] = progress * (1 - share);
            }
        }
        return Math.Round(lost, 1);
    }

    /// <summary>Carries off this share of the pantry's food value (whole items); returns the food value taken.</summary>
    public static double Steal(ColonySim sim, double share)
    {
        var want = Math.Floor(Pantry.FoodValue(sim) * share);
        return want > 0 && Pantry.TakeFoodValue(sim.State, sim.Data, want) is { } taken ? want : 0;
    }
}
