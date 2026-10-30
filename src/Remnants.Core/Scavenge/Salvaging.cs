using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>One line of the wreck salvage table: a part, its weight in the roll, the zone loot tier it needs, and whether it is big.</summary>
public sealed record SalvageEntry
{
    public string Item { get; init; } = "";
    public double Weight { get; init; } = 1;
    public int MinTier { get; init; } = 1;

    /// <summary>Too heavy to strip by hand: only when the party's vehicle is parked at the edge to haul it.</summary>
    public bool Big { get; init; }
}

/// <summary>
/// Stripping wrecked cars for vehicle parts (scavenge.json <c>salvage</c>): <see cref="ApCost"/> AP, as loud as a shot within
/// <see cref="NoiseRadius"/>, <see cref="RollsByLootTier"/> rolls on <see cref="Table"/> (a winch on the party's vehicle adds
/// <see cref="WinchExtraRolls"/>), parts at <see cref="QualityByLootTier"/>.
/// </summary>
public sealed record WreckSalvageRules
{
    public int ApCost { get; init; } = 2;
    public int NoiseRadius { get; init; } = 6;
    public List<int> RollsByLootTier { get; init; } = [1, 2, 2];
    public int WinchExtraRolls { get; init; } = 1;
    public List<string> QualityByLootTier { get; init; } = ["crude", "standard", "standard"];
    public List<SalvageEntry> Table { get; init; } = new();
}

/// <summary>
/// Wrecked cars on a scavenge map and the vehicle parts in them (rules in <c>scavenge.json salvage</c>). Stripping is the
/// generic <see cref="FieldSalvage"/> action on a <c>wrecked_car</c> node, which adds <see cref="Roll"/> to the node's own
/// loot (<see cref="FieldSalvage.Extras"/>). A wreck is two cover tiles side by side, paired the way the map
/// draws them (row by row, each cover tile with the free one east of it, else south), so what the player sees as a car is
/// what can be salvaged. Each wreck is salvaged once (<see cref="ScavengeState.SalvagedWrecks"/>); its parts drop as a loot
/// cache on the unit's side of it, to be picked up as usual.
/// </summary>
public static class Salvaging
{
    /// <summary>Every wreck on the map as its two tiles (the first in row order is its key).</summary>
    public static List<(Pos A, Pos B)> Wrecks(TileMap map)
    {
        var placed = new HashSet<Pos>();
        var wrecks = new List<(Pos, Pos)>();
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var p = new Pos(x, y);
                if (map[p] != Terrain.Cover || !placed.Add(p)) continue;
                foreach (var next in new[] { p + new Pos(1, 0), p + new Pos(0, 1) })
                    if (map.InBounds(next) && map[next] == Terrain.Cover && !placed.Contains(next))
                    {
                        placed.Add(next);
                        wrecks.Add((p, next));
                        break;
                    }
            }
        return wrecks;
    }

    /// <summary>The wreck that covers <paramref name="at"/>, if any.</summary>
    public static (Pos A, Pos B)? WreckAt(TileMap map, Pos at) =>
        Wrecks(map).Where(w => w.A == at || w.B == at).Cast<(Pos, Pos)?>().FirstOrDefault();

    /// <summary>Wrecks next to the unit that are still to be stripped.</summary>
    public static IEnumerable<(Pos A, Pos B)> Beside(ScavengeState state, Unit unit) =>
        Wrecks(state.Map).Where(w => !state.SalvagedWrecks.Contains(state.Map.Index(w.A))
                                     && (w.A.Chebyshev(unit.Pos) <= 1 || w.B.Chebyshev(unit.Pos) <= 1));

    /// <summary>
    /// The parts one wreck gives a player in this zone: the zone tier's rolls on the table at its quality, big parts only
    /// with the player's vehicle parked, a winch adding rolls. Draws from the sim RNG.
    /// </summary>
    public static List<string> Roll(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        var rules = sim.Data.Rules.Salvage;
        var tier = sim.Data.Zones.TryGetValue(state.ZoneId, out var zone) ? zone.LootTier : 1;
        var vehicle = ParkedVehicles.Of(state, playerId) is { } v && ParkedVehicles.InPlay(state, v) ? v : null;
        var rolls = rules.RollsByLootTier.Count == 0 ? 1 : rules.RollsByLootTier[Math.Clamp(tier - 1, 0, rules.RollsByLootTier.Count - 1)];
        if (vehicle is { Winch: true }) rolls += rules.WinchExtraRolls;
        var table = rules.Table.Where(e => e.MinTier <= tier && (!e.Big || vehicle is not null)).ToList();
        var quality = rules.QualityByLootTier.Count == 0 ? null : rules.QualityByLootTier[Math.Clamp(tier - 1, 0, rules.QualityByLootTier.Count - 1)];
        var parts = new List<string>();
        for (var i = 0; i < rolls && table.Count > 0; i++)
        {
            var entry = NewScavenge.PickWeighted(sim.Rng, table, e => e.Weight);
            parts.Add(quality is null ? entry.Item : sim.Data.QualityVariant(entry.Item, quality));
        }
        return parts;
    }
}
