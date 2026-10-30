using Remnants.Core.Data;

namespace Remnants.Core.Colony;

/// <summary>
/// The one place vehicles ask whether a schematic is known. Today a schematic is a studied blueprint item
/// (<see cref="ColonyState.KnownBlueprints"/>) and building levels name theirs in <see cref="BuildingLevelDef.Schematic"/>;
/// when the schematics feature lands, these bodies become <c>Schematics.Has(sim, id)</c> and
/// <c>Schematics.LevelUnlocked(sim, building, level)</c> (the catalogue entries are in data/schematics.vehicles.json).
/// </summary>
public static class VehicleSchematics
{
    public static bool Has(ColonySim sim, string? schematicId) => schematicId is null || sim.State.KnownBlueprints.Contains(schematicId);

    public static bool LevelUnlocked(ColonySim sim, string buildingId, int level) =>
        Has(sim, sim.Data.Buildings[buildingId].Level(level).Schematic);
}
