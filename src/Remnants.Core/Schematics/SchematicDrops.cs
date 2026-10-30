using Remnants.Core.Random;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Schematics;

/// <summary>Which kind of container an item roll is for (each has its own schematic chance per find tier).</summary>
public enum DropSite { Cache, Locker, Terminal }

/// <summary>
/// Schematics in the field (SCHEMATICS.md "Where schematics come from"): each cache, locker and terminal item first rolls
/// "is it a schematic?" at the zone's find tier (its threat, 1..4); if so it picks one allowed there — tier up to the
/// zone's, the zone's own tier ×<c>ownTierWeight</c>, ones the colony knows ×<c>knownWeight</c>. Caches and lockers draw
/// from the <c>Loot</c> source, terminals from <c>Terminal</c>. Enemy drops are in enemies.json; a kill rolls its
/// schematic drops in order and the first hit wins (a known one at <c>knownWeight</c> × its chance).
/// </summary>
public static class SchematicDrops
{
    /// <summary>The find tier of a zone: its threat, 1..4.</summary>
    public static int FindTier(ZoneDef zone) => Math.Clamp(zone.Threat, 1, 4);

    /// <summary>The chance one item at this site in this zone is a schematic.</summary>
    public static double Chance(SchematicCatalog catalog, ZoneDef zone, DropSite site)
    {
        if (catalog.All.Count == 0) return 0;
        var d = catalog.Rules.Drops;
        var perTier = site switch { DropSite.Cache => d.Cache, DropSite.Locker => d.Locker, _ => d.Terminal };
        return SchematicCatalog.At(perTier, FindTier(zone));
    }

    /// <summary>The weighted pool for a site in a zone (empty when nothing can drop there).</summary>
    public static List<(SchematicDef Def, double Weight)> Pool(SchematicCatalog catalog, ZoneDef zone, DropSite site, IReadOnlySet<string>? known)
    {
        var tier = FindTier(zone);
        var source = site == DropSite.Terminal ? SchematicSource.Terminal : SchematicSource.Loot;
        var d = catalog.Rules.Drops;
        return catalog.All.Values
            .Where(s => s.Available && s.Has(source) && s.Tier <= tier)
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => (s, (s.Tier == tier ? d.OwnTierWeight : 1) * (known?.Contains(s.Id) == true ? d.KnownWeight : 1)))
            .Where(p => p.Item2 > 0)
            .ToList();
    }

    /// <summary>Roll one item slot: a schematic id, or null (then the site's normal loot applies).</summary>
    public static string? Roll(ScavengeData data, ZoneDef zone, DropSite site, DeterministicRng rng, IReadOnlySet<string>? known)
    {
        var chance = Chance(data.Schematics, zone, site);
        if (chance <= 0 || rng.NextDouble() >= chance) return null;
        var pool = Pool(data.Schematics, zone, site, known);
        return pool.Count == 0 ? null : NewScavenge.PickWeighted(rng, pool, p => p.Weight).Def.Id;
    }

    /// <summary>
    /// Expected schematics per item at a site (the chance), and the share of each schematic in the pool — for the playtest
    /// bot and tests.
    /// </summary>
    public static IEnumerable<(string Id, double Share)> Shares(SchematicCatalog catalog, ZoneDef zone, DropSite site, IReadOnlySet<string>? known)
    {
        var pool = Pool(catalog, zone, site, known);
        var total = pool.Sum(p => p.Weight);
        return pool.Select(p => (p.Def.Id, total <= 0 ? 0 : p.Weight / total));
    }

    /// <summary>
    /// An enemy's drops: every non-schematic drop rolls on its own; schematic drops roll in order and only the first hit
    /// drops (a known schematic at <c>knownWeight</c> × its chance).
    /// </summary>
    public static List<string> EnemyDrops(ScavengeData data, EnemyDef enemy, DeterministicRng rng, IReadOnlySet<string>? known)
    {
        var drops = new List<string>();
        var schematicDropped = false;
        foreach (var d in enemy.Drops)
        {
            var isSchematic = data.Items.GetValueOrDefault(d.Item)?.Schematic is not null;
            var chance = isSchematic && known?.Contains(d.Item) == true ? d.Chance * data.Schematics.Rules.Drops.KnownWeight : d.Chance;
            if (rng.NextDouble() >= chance) continue;
            if (isSchematic)
            {
                if (schematicDropped) continue;
                schematicDropped = true;
            }
            drops.Add(d.Item);
        }
        return drops;
    }
}
