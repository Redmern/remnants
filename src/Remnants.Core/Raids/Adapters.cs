using Remnants.Core.Colony;

namespace Remnants.Core.Raids;

/// <summary>An entry of data/schematics.defense.json, in the shape of story-schematics' <c>SchematicDef</c>.</summary>
public sealed record DefenseSchematicDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Tier { get; init; } = 1;
    public bool Default { get; init; }
    public List<DefenseUnlock> Unlocks { get; init; } = new();
    public List<string> Sources { get; init; } = new();
    public string Description { get; init; } = "";
}

public sealed record DefenseUnlock
{
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
}

internal sealed record DefenseSchematicFile
{
    public List<DefenseSchematicDef> Schematics { get; init; } = new();
    public List<object> Traders { get; init; } = new();
}

/// <summary>
/// The small adapter between defenses and schematics. Until story-schematics is merged the colony's known defense
/// schematics live in <see cref="DefenseState.KnownSchematics"/>; the method names match
/// <c>Remnants.Core.Schematics.Schematics</c> (Has / Grant), so the switch-over is a one-line change per method.
/// </summary>
public static class DefenseSchematics
{
    public const string FileName = "schematics.defense.json";

    /// <summary>The unlock kind these schematics use (story-schematics registers a validator for it).</summary>
    public const string UnlockKind = "Defense";

    public static bool Has(ColonySim sim, string schematicId) =>
        sim.Raids?.Schematics.FirstOrDefault(s => s.Id == schematicId) is { } def
        && (def.Default || sim.State.Defense.KnownSchematics.Contains(schematicId));

    /// <summary>Learns a schematic. False when already known (story-schematics turns a duplicate into Research Data).</summary>
    public static bool Grant(ColonySim sim, string schematicId, string source)
    {
        if (sim.Raids?.Schematics.FirstOrDefault(s => s.Id == schematicId) is not { } def || Has(sim, schematicId)) return false;
        sim.State.Defense.KnownSchematics.Add(schematicId);
        sim.State.Log(new(sim.State.Tick, ColonyEventType.DefenseSchematicLearned, $"Schematic learned: {def.Name} ({source})."));
        return true;
    }

    /// <summary>The schematic that unlocks a defense (null when none does: validation forbids that).</summary>
    public static DefenseSchematicDef? GateOf(RaidData raids, string defenseId) =>
        raids.Schematics.FirstOrDefault(s => s.Unlocks.Any(u => u.Id == defenseId));

    /// <summary>Whether the colony may build this defense.</summary>
    public static bool Unlocked(ColonySim sim, string defenseId) =>
        sim.Raids is { } raids && GateOf(raids, defenseId) is { } gate && Has(sim, gate.Id);

    /// <summary>Schematics a beaten raid could drop: known by nobody yet and not default.</summary>
    public static IEnumerable<DefenseSchematicDef> Droppable(ColonySim sim) =>
        sim.Raids?.Schematics.Where(s => !s.Default && !Has(sim, s.Id) && s.Sources.Contains("Kill")) ?? [];
}

/// <summary>
/// The colony's weather, for raids. The colony has none yet: once the weather branch lands it sets <see cref="Provider"/>
/// at startup (weather id at a tick, e.g. "dust_storm"), and raids.json's <c>weather</c> modifiers apply.
/// </summary>
public static class RaidWeather
{
    public static Func<ColonySim, long, string?>? Provider { get; set; }

    public static string? At(ColonySim sim, long tick) => Provider?.Invoke(sim, tick);

    public static WeatherModifiers Modifiers(ColonySim sim, RaidData raids, long tick) =>
        At(sim, tick) is { } id && raids.Def.Weather.TryGetValue(id, out var w) ? w : new WeatherModifiers();
}

/// <summary>
/// The colony power grid, for defenses (turrets, floodlights). Optional: powered defenses get their
/// <see cref="DefenseDef.PoweredBonus"/>, never a hard requirement. Always false until the electronics-devices branch
/// lands and sets <see cref="Provider"/> (its <c>Power.IsPowered</c>).
/// </summary>
public static class DefensePower
{
    public static Func<ColonySim, PlacedDefense, bool>? Provider { get; set; }

    public static bool IsPowered(ColonySim sim, PlacedDefense d) => Provider?.Invoke(sim, d) ?? false;

    /// <summary>1 + the def's powered bonus when powered, else 1.</summary>
    public static double Multiplier(ColonySim sim, PlacedDefense d, DefenseDef def) => IsPowered(sim, d) ? 1 + def.PoweredBonus : 1;
}

/// <summary>
/// Crops the Husks trample. Until food-cooking (Pantry/ingredients) lands it takes raw food from the stockpile and,
/// over a share, damages the farms; then this one method calls the Pantry instead.
/// </summary>
public static class CropDamage
{
    public static double Trample(ColonySim sim, string resource, double share)
    {
        var have = sim.State.Stockpile.Get(resource);
        var lost = Math.Round(have * share, 1);
        if (lost > 0) sim.State.Stockpile.Spend(new Dictionary<string, double> { [resource] = lost });
        return lost;
    }
}
