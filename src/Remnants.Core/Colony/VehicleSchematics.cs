using Remnants.Core.Schematics;

namespace Remnants.Core.Colony;

/// <summary>
/// The one place vehicles ask whether a schematic is known (<see cref="Schematics.Schematics.Has"/>); building levels and
/// recipes are gated by the schematics catalogue itself (data/schematics.vehicles.json). Registers the catalogue's check
/// for <see cref="UnlockKind.Vehicle"/> unlocks: they name vehicle upgrades.
/// </summary>
public static class VehicleSchematics
{
    public static bool Has(ColonySim sim, string? schematicId) => schematicId is null || Schematics.Schematics.Has(sim, schematicId);

    public static bool LevelUnlocked(ColonySim sim, string buildingId, int level) => Schematics.Schematics.LevelUnlocked(sim, buildingId, level);

    /// <summary>Called by <see cref="Data.GameData"/>'s static constructor, so it is in place before any data loads.</summary>
    internal static void Register() =>
        SchematicCatalog.UnlockValidators[UnlockKind.Vehicle] =
            (data, id) => data.Vehicles.Upgrades.Count == 0 || data.VehicleUpgrades.ContainsKey(id) ? null : $"unknown vehicle upgrade '{id}'";
}
