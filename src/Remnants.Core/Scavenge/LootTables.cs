using System.Text.Json;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Serialization;

namespace Remnants.Core.Scavenge;

/// <summary>
/// One weighted pick in a loot table: <see cref="Qty"/> of the item, from find tier <see cref="MinTier"/> up to
/// <see cref="MaxTier"/>. Gear can come at a quality (<see cref="Quality"/>: tier id → weight; none = the defined item).
/// </summary>
public sealed record LootEntryDef
{
    public string Item { get; init; } = "";
    public double Weight { get; init; } = 1;
    public int[] Qty { get; init; } = [1, 1];
    public int MinTier { get; init; } = 1;
    public int MaxTier { get; init; } = 4;
    public Dictionary<string, double> Quality { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>A rare find: rolled on its own at <see cref="Chance"/> (scaled by the table's tier rules), from <see cref="MinTier"/>.</summary>
public sealed record LootRareDef
{
    public string Item { get; init; } = "";
    public double Chance { get; init; }
    public int[] Qty { get; init; } = [1, 1];
    public int MinTier { get; init; } = 1;
    public Dictionary<string, double> Quality { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// What a kill (or a salvage node, or a raider) leaves: with <see cref="Nothing"/> chance nothing at all (the
/// guaranteed entries still drop), else <see cref="Rolls"/> weighted picks from <see cref="Entries"/> and each
/// <see cref="Rare"/> entry on its own. <see cref="Guaranteed"/> always drop.
/// </summary>
public sealed record LootTableDef
{
    public string Id { get; init; } = "";
    public double Nothing { get; init; }
    public int[] Rolls { get; init; } = [1, 1];
    public List<LootEntryDef> Entries { get; init; } = new();
    public List<LootEntryDef> Guaranteed { get; init; } = new();
    public List<LootRareDef> Rare { get; init; } = new();
}

/// <summary>A feature file adding entries to a table another file defines (<c>loot_tables.&lt;feature&gt;.json</c> <c>extend</c>).</summary>
public sealed record LootTableExtension
{
    public string Table { get; init; } = "";
    public List<LootEntryDef> Entries { get; init; } = new();
    public List<LootEntryDef> Guaranteed { get; init; } = new();
    public List<LootRareDef> Rare { get; init; } = new();
}

/// <summary>
/// How the find tier (the zone's threat, 1..4) and the run change a roll. Per tier above 1: <see cref="ExtraRollsPerTier"/>
/// more picks (fractions are a chance of one more), <see cref="QuantityPerTier"/> more of each, <see cref="NothingPerTier"/>
/// off the empty chance and <see cref="RarePerTier"/> × the rare chances. Night and weather give extra picks
/// (people hole up and die with their things); <see cref="LootBonusRolls"/> × the killer's <c>loot_bonus</c> perk hook
/// too. <see cref="Difficulty"/>: quantity multiplier per Story difficulty id (missing = 1).
/// </summary>
public sealed record LootRules
{
    public double ExtraRollsPerTier { get; init; } = 0.35;
    public double QuantityPerTier { get; init; } = 0.15;
    public double NothingPerTier { get; init; } = 0.03;
    public double RarePerTier { get; init; } = 0.25;
    public Dictionary<DayPhase, double> NightRolls { get; init; } = new();
    public Dictionary<string, double> WeatherRolls { get; init; } = new(StringComparer.Ordinal);
    public double LootBonusRolls { get; init; } = 1;
    public Dictionary<string, double> Difficulty { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>One loot_tables*.json file.</summary>
public sealed record LootTableFile
{
    public LootRules? Rules { get; init; }
    public List<LootTableDef> Tables { get; init; } = new();
    public List<LootTableExtension> Extend { get; init; } = new();
}

/// <summary>
/// The modifiers one roll is made with: the find tier, extra picks (night, weather, perks), a quantity multiplier
/// (difficulty, salvage skill and tools) and an extra multiplier on the empty chance.
/// </summary>
public readonly record struct LootContext(int Tier, double ExtraRolls = 0, double QuantityMultiplier = 1, double NothingMultiplier = 1);

/// <summary>
/// Every loot table, merged from <c>loot_tables.json</c> and its feature extensions, with entries for items the data
/// does not have dropped (<see cref="Warnings"/> says which), so a feature's entries can ship before its items do.
/// </summary>
public sealed class LootCatalog
{
    public static readonly LootCatalog Empty = new(new LootRules(), new Dictionary<string, LootTableDef>(), []);

    public LootRules Rules { get; }
    public IReadOnlyDictionary<string, LootTableDef> Tables { get; }

    /// <summary>What was skipped on load (unknown items, extensions of unknown tables). Shown as warnings, never fatal.</summary>
    public IReadOnlyList<string> Warnings { get; }

    private LootCatalog(LootRules rules, IReadOnlyDictionary<string, LootTableDef> tables, IReadOnlyList<string> warnings)
    {
        Rules = rules;
        Tables = tables;
        Warnings = warnings;
    }

    public LootTableDef? Find(string? id) => id is null ? null : Tables.GetValueOrDefault(id);

    /// <summary>Merges the files (main first) and resolves them against the items; bad numbers are data errors.</summary>
    public static LootCatalog Build(IReadOnlyList<LootTableFile> files, IReadOnlyDictionary<string, Items.ItemDef> items, IReadOnlyCollection<string> qualityTiers)
    {
        if (files.Count == 0) return Empty;
        var warnings = new List<string>();
        var tables = new Dictionary<string, LootTableDef>(StringComparer.Ordinal);
        foreach (var t in files.SelectMany(f => f.Tables))
        {
            if (string.IsNullOrWhiteSpace(t.Id)) throw new GameDataException("Loot table without an id.");
            if (!tables.TryAdd(t.Id, t)) throw new GameDataException($"Duplicate loot table '{t.Id}'.");
        }
        foreach (var x in files.SelectMany(f => f.Extend))
        {
            if (!tables.TryGetValue(x.Table, out var t))
            {
                warnings.Add($"Loot extension for unknown table '{x.Table}' skipped.");
                continue;
            }
            tables[x.Table] = t with
            {
                Entries = [.. t.Entries, .. x.Entries],
                Guaranteed = [.. t.Guaranteed, .. x.Guaranteed],
                Rare = [.. t.Rare, .. x.Rare],
            };
        }

        bool Known(string table, string item, Dictionary<string, double> quality)
        {
            if (!items.TryGetValue(item, out var def))
            {
                warnings.Add($"Loot table '{table}': unknown item '{item}' skipped.");
                return false;
            }
            if (def.Natural) throw new GameDataException($"Loot table '{table}': '{item}' is a natural item and never drops.");
            foreach (var q in quality.Keys)
                if (!qualityTiers.Contains(q)) throw new GameDataException($"Loot table '{table}': unknown quality '{q}' for '{item}'.");
            return true;
        }

        var resolved = new SortedDictionary<string, LootTableDef>(StringComparer.Ordinal);
        foreach (var t in tables.Values)
        {
            var where = $"Loot table '{t.Id}'";
            if (t.Nothing is < 0 or > 1) throw new GameDataException($"{where}: nothing must be in 0..1.");
            Range(t.Rolls, $"{where} rolls");
            foreach (var e in t.Entries.Concat(t.Guaranteed))
            {
                Range(e.Qty, $"{where} '{e.Item}' qty");
                if (e.Weight <= 0 || e.MinTier > e.MaxTier) throw new GameDataException($"{where}: '{e.Item}' needs a positive weight and minTier <= maxTier.");
            }
            foreach (var r in t.Rare)
            {
                Range(r.Qty, $"{where} '{r.Item}' qty");
                if (r.Chance is <= 0 or > 1) throw new GameDataException($"{where}: rare '{r.Item}' needs a chance in (0, 1].");
            }
            resolved[t.Id] = t with
            {
                Entries = t.Entries.Where(e => Known(t.Id, e.Item, e.Quality)).ToList(),
                Guaranteed = t.Guaranteed.Where(e => Known(t.Id, e.Item, e.Quality)).ToList(),
                Rare = t.Rare.Where(r => Known(t.Id, r.Item, r.Quality)).ToList(),
            };
        }
        var rules = files.Select(f => f.Rules).FirstOrDefault(r => r is not null) ?? new LootRules();
        return new LootCatalog(rules, resolved, warnings);
    }

    private static void Range(int[] range, string name)
    {
        if (range.Length != 2 || range[0] < 0 || range[0] > range[1])
            throw new GameDataException($"{name} must be [min, max] with 0 <= min <= max.");
    }

    /// <summary>
    /// Rolls a table: item ids, one per piece (two scrap = "scrap" twice), guaranteed entries first. Deterministic for
    /// the RNG's state.
    /// </summary>
    public List<string> Roll(ScavengeData data, LootTableDef table, LootContext context, DeterministicRng rng)
    {
        var tier = Math.Clamp(context.Tier, 1, 4);
        var above = tier - 1;
        var quantity = context.QuantityMultiplier * (1 + Rules.QuantityPerTier * above);
        var drops = new List<string>();

        void Add(string item, int[] qty, Dictionary<string, double> quality)
        {
            var n = Stochastic(rng, rng.NextInclusive(qty[0], qty[1]) * quantity);
            for (var i = 0; i < n; i++) drops.Add(Quality(data, rng, item, quality));
        }

        foreach (var g in table.Guaranteed.Where(e => e.MinTier <= tier && tier <= e.MaxTier))
            Add(g.Item, g.Qty, g.Quality);

        var nothing = Math.Clamp((table.Nothing - Rules.NothingPerTier * above) * context.NothingMultiplier, 0, 1);
        if (rng.NextDouble() < nothing) return drops;

        var pool = table.Entries.Where(e => e.MinTier <= tier && tier <= e.MaxTier).ToList();
        if (pool.Count > 0)
        {
            var rolls = rng.NextInclusive(table.Rolls[0], table.Rolls[1]) + Stochastic(rng, Rules.ExtraRollsPerTier * above + context.ExtraRolls);
            for (var i = 0; i < rolls; i++)
            {
                var e = NewScavenge.PickWeighted(rng, pool, p => p.Weight);
                Add(e.Item, e.Qty, e.Quality);
            }
        }
        foreach (var r in table.Rare.Where(r => r.MinTier <= tier))
            if (rng.NextDouble() < Math.Min(1, r.Chance * (1 + Rules.RarePerTier * above)))
                Add(r.Item, r.Qty, r.Quality);
        return drops;
    }

    /// <summary>
    /// Expected pieces per item for one roll of the table (no RNG; extra rolls and quantities at their means), for the
    /// tooltip estimates, the playtest bot and tests.
    /// </summary>
    public SortedDictionary<string, double> Expected(LootTableDef table, LootContext context)
    {
        var tier = Math.Clamp(context.Tier, 1, 4);
        var above = tier - 1;
        var quantity = context.QuantityMultiplier * (1 + Rules.QuantityPerTier * above);
        var result = new SortedDictionary<string, double>(StringComparer.Ordinal);
        void Add(string item, int[] qty, double times) => result[item] = result.GetValueOrDefault(item) + times * (qty[0] + qty[1]) / 2.0 * quantity;

        foreach (var g in table.Guaranteed.Where(e => e.MinTier <= tier && tier <= e.MaxTier)) Add(g.Item, g.Qty, 1);
        var some = 1 - Math.Clamp((table.Nothing - Rules.NothingPerTier * above) * context.NothingMultiplier, 0, 1);
        var pool = table.Entries.Where(e => e.MinTier <= tier && tier <= e.MaxTier).ToList();
        var total = pool.Sum(e => e.Weight);
        var rolls = (table.Rolls[0] + table.Rolls[1]) / 2.0 + Rules.ExtraRollsPerTier * above + context.ExtraRolls;
        foreach (var e in pool) Add(e.Item, e.Qty, some * rolls * e.Weight / total);
        foreach (var r in table.Rare.Where(r => r.MinTier <= tier)) Add(r.Item, r.Qty, some * Math.Min(1, r.Chance * (1 + Rules.RarePerTier * above)));
        return result;
    }

    /// <summary>A whole number with this mean: the integer part, plus one more at the fraction's chance.</summary>
    internal static int Stochastic(DeterministicRng rng, double value)
    {
        if (value <= 0) return 0;
        var whole = (int)Math.Floor(value);
        return whole + (rng.NextDouble() < value - whole ? 1 : 0);
    }

    private static string Quality(ScavengeData data, DeterministicRng rng, string item, Dictionary<string, double> quality)
    {
        if (quality.Count == 0 || !data.Items[item].HasQuality) return item;
        var tier = NewScavenge.PickWeighted(rng, quality.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList(), kv => kv.Value).Key;
        return data.QualityVariant(item, tier);
    }
}

/// <summary>Reads <c>loot_tables.json</c> and the <c>loot_tables.&lt;feature&gt;.json</c> extensions (by name).</summary>
public static class LootTableLoader
{
    public const string FileName = "loot_tables.json";

    /// <summary>The main file then the extensions in a directory (none when there is no main file).</summary>
    public static IReadOnlyList<string> Files(string directory)
    {
        var main = Path.Combine(directory, FileName);
        if (!File.Exists(main)) return [];
        var extensions = Directory.GetFiles(directory, "loot_tables.*.json").Where(f => Path.GetFileName(f) != FileName).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal);
        return [main, .. extensions];
    }

    public static List<LootTableFile> Parse(IEnumerable<string> jsons) => jsons.Select((json, i) =>
    {
        try
        {
            return JsonSerializer.Deserialize<LootTableFile>(json, Json.Options) ?? throw new GameDataException($"Loot table file {i} is empty.");
        }
        catch (JsonException e)
        {
            throw new GameDataException($"Loot table file {i} is invalid: {e.Message}");
        }
    }).ToList();
}
