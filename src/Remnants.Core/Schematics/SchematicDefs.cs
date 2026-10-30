using System.Text.Json;
using Remnants.Core.Data;
using Remnants.Core.Serialization;

namespace Remnants.Core.Schematics;

/// <summary>What a schematic unlocks (see SCHEMATICS.md "What a schematic is").</summary>
public enum UnlockKind
{
    /// <summary>A building type: it shows up in the build menu (its level 1).</summary>
    Building,

    /// <summary>One level of a building type (<see cref="SchematicUnlock.Level"/>): the upgrade to it works.</summary>
    BuildingLevel,

    /// <summary>A recipe (items, weapons, armor, devices, today's prosthetic tiers): it can be queued.</summary>
    Recipe,

    /// <summary>A perk (the old datashards, "techniques"): it can be learned.</summary>
    Perk,

    /// <summary>A cybernetics tier (<c>tier:N</c>) or specialisation (<c>spec:ID</c>) of data/cybernetics.json.</summary>
    Prosthetic,

    /// <summary>A vehicle chassis, engine or upgrade (checked by a registered validator, see <see cref="SchematicCatalog.UnlockValidators"/>).</summary>
    Vehicle,
}

/// <summary>Where a schematic can come from. Every schematic lists at least one source that is not <see cref="Story"/>.</summary>
public enum SchematicSource
{
    /// <summary>Known from day one (the default set).</summary>
    Default,

    /// <summary>Caches and lockers in zones of its find tier or higher.</summary>
    Loot,

    /// <summary>Hacked terminals.</summary>
    Terminal,

    /// <summary>Dropped by a killed enemy (the drop tables in enemies.json).</summary>
    Kill,

    /// <summary>Bought from the trader who visits via the Radio Mast.</summary>
    Trade,

    /// <summary>Researched at the Town Hall with Research Data.</summary>
    Research,

    /// <summary>A story mission reward.</summary>
    Story,

    /// <summary>Not a source in data: granted by the save migration.</summary>
    Migration,

    /// <summary>Not a source in data: debug and demo setups.</summary>
    Debug,
}

/// <summary>One thing a schematic unlocks. <see cref="Level"/> only for <see cref="UnlockKind.BuildingLevel"/>.</summary>
public sealed record SchematicUnlock
{
    public UnlockKind Kind { get; init; }
    public string Id { get; init; } = "";
    public int? Level { get; init; }
}

/// <summary>
/// A piece of knowledge that lets a colony build or make something new (data/schematics.json). In the field it is an
/// item with the same id; once the colony learns it, it is known for good. Tier is the find tier (1 Home Valley … 4 The
/// Black Grid), not the unlocked item's tier.
/// </summary>
public sealed record SchematicDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Tier { get; init; } = 1;

    /// <summary>A Network chip: comes home as an item and needs a Decrypt order at the Electronics Lab.</summary>
    public bool Encrypted { get; init; }

    /// <summary>Every new colony knows it (the default set beyond what simply has no schematic).</summary>
    public bool Default { get; init; }

    /// <summary>
    /// Its content is not in the game yet (vehicles, the prosthetics tree): listed in the archive as coming, never
    /// dropped, sold or researched. Its unlock ids are not checked against data.
    /// </summary>
    public bool Planned { get; init; }

    public List<SchematicUnlock> Unlocks { get; init; } = new();
    public List<SchematicSource> Sources { get; init; } = new();

    /// <summary>Story missions that reward it (names for the archive; the campaign grants it with <see cref="Schematics.Grant"/>).</summary>
    public List<string> StoryMissions { get; init; } = new();

    /// <summary>Short flavour line for the archive tooltip.</summary>
    public string Description { get; init; } = "";

    /// <summary>The field item's display name.</summary>
    public string ItemName => $"Schematic: {Name}";

    public bool Has(SchematicSource source) => Sources.Contains(source);

    /// <summary>Can drop, be sold or researched (not planned).</summary>
    public bool Available => !Planned;
}

/// <summary>Per find tier (index 0 = tier 1): the chance that a cache item, locker item or terminal item is a schematic.</summary>
public sealed record SchematicDropRules
{
    public double[] Cache { get; init; } = [0.02, 0.04, 0.06, 0.08];
    public double[] Locker { get; init; } = [0.03, 0.05, 0.06, 0.08];
    public double[] Terminal { get; init; } = [0.25, 0.35, 0.45, 0.60];

    /// <summary>Weight of schematics of the zone's own tier (lower tiers weigh 1).</summary>
    public double OwnTierWeight { get; init; } = 2;

    /// <summary>Weight multiplier for schematics the colony already knows (duplicates happen, but do not dominate).</summary>
    public double KnownWeight { get; init; } = 0.5;
}

/// <summary>Decrypting a Network chip: a production order with no inputs at <see cref="Building"/>.</summary>
public sealed record DecryptRules
{
    public string Building { get; init; } = "electronics_lab";

    /// <summary>Building level needed, per tier (index 0 = tier 1).</summary>
    public int[] MinLevel { get; init; } = [2, 2, 2, 3];

    /// <summary>Work per tier (index 0 = tier 1).</summary>
    public double[] Work { get; init; } = [12, 12, 12, 24];
    public double MinSkill { get; init; } = 20;
}

/// <summary>Research at the Town Hall: Research Data plus a researcher's time.</summary>
public sealed record ResearchRules
{
    public string Building { get; init; } = "town_hall";

    /// <summary>Research Data per tier (index 0 = tier 1); 0 = cannot be researched (tier 4).</summary>
    public double[] Cost { get; init; } = [4, 8, 16, 0];

    /// <summary>Work per tier: a researcher at factor 1 adds 1 per on-shift hour.</summary>
    public double[] Work { get; init; } = [42, 84, 168, 0];

    /// <summary>Town Hall level needed per tier.</summary>
    public int[] TownHallLevel { get; init; } = [2, 2, 3, 99];

    /// <summary>Other building levels needed per tier (e.g. tier 3 needs an Electronics Lab L2), by building id.</summary>
    public List<Dictionary<string, int>> Needs { get; init; } = new();

    /// <summary>Researcher factor = base + Intellect·perIntellect + Electronics·perSkill (attribute/skill 0..100).</summary>
    public double BaseFactor { get; init; } = 0.4;
    public double PerIntellect { get; init; } = 0.006;
    public double PerSkill { get; init; } = 0.006;

    /// <summary>Research Data a duplicate turns into, per tier (index 0 = tier 1).</summary>
    public double[] DuplicateValue { get; init; } = [2, 4, 6, 10];
}

/// <summary>One way to pay for a schematic: resources (Research Data is the resource <c>research_data</c>).</summary>
public sealed record PriceOption
{
    public Dictionary<string, double> Cost { get; init; } = new();
}

/// <summary>A trader who calls over the radio and parks outside the colony. Data-driven so Story mode can swap one in.</summary>
public sealed record TraderDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>What the radio says when the trader calls in.</summary>
    public string Greeting { get; init; } = "";

    /// <summary>Visits start once this zone is Scouted (null = with the Radio Mast alone).</summary>
    public string? RequiresScouted { get; init; }

    /// <summary>Only when a story sets it (<see cref="RadioMast.SetTrader"/>): never the default trader.</summary>
    public bool StoryOnly { get; init; }

    /// <summary>May offer tier-4 schematics (Sandbox traders do not).</summary>
    public int MaxTier { get; init; } = 3;
}

/// <summary>Trading and radio contracts (the Radio Mast).</summary>
public sealed record RadioRules
{
    public string Building { get; init; } = "radio_mast";

    /// <summary>The trader used unless a story sets another.</summary>
    public string DefaultTrader { get; init; } = "caravan";

    /// <summary>Days between visits per Radio Mast level (index 0 = L1).</summary>
    public double[] VisitEveryDays { get; init; } = [10, 5];
    public double StayDays { get; init; } = 2;

    /// <summary>Stock: schematics the colony does not know. Before <see cref="LateStockDay"/> all are tier ≤ early max, then one is ≤ late max.</summary>
    public int StockSize { get; init; } = 2;
    public int EarlyMaxTier { get; init; } = 2;
    public int LateMaxTier { get; init; } = 3;
    public int LateStockDay { get; init; } = 60;

    /// <summary>Price options per tier (index 0 = tier 1).</summary>
    public List<List<PriceOption>> Prices { get; init; } = new();

    /// <summary>What the trader pays for a schematic item, per tier, and per unit of Research Data.</summary>
    public List<Dictionary<string, double>> SellPrices { get; init; } = new();
    public Dictionary<string, double> ResearchDataPrice { get; init; } = new();

    /// <summary>Contracts: one offer at a time, every this many days; done when an expedition comes home from the zone in time.</summary>
    public double ContractEveryDays { get; init; } = 6;
    public double ContractDays { get; init; } = 8;

    /// <summary>Highest zone threat a contract names, per Radio Mast level (index 0 = L1).</summary>
    public int[] ContractMaxThreat { get; init; } = [2, 4];

    /// <summary>Reward per zone threat (index 0 = threat 1).</summary>
    public List<Dictionary<string, double>> ContractRewards { get; init; } = new();

    /// <summary>Calls older than this many days drop out of the queue unanswered.</summary>
    public double CallExpiryDays { get; init; } = 3;
}

/// <summary>Numbers of the schematics system (the <c>rules</c> block of schematics.json).</summary>
public sealed record SchematicRules
{
    /// <summary>The colony resource Research Data is kept as.</summary>
    public string ResearchDataResource { get; init; } = "research_data";

    public SchematicDropRules Drops { get; init; } = new();
    public DecryptRules Decrypt { get; init; } = new();
    public ResearchRules Research { get; init; } = new();
    public RadioRules Radio { get; init; } = new();

    /// <summary>Research Data a copy of a known schematic costs, per tier (index 0 = tier 1).</summary>
    public double[] CopyCost { get; init; } = [1, 2, 3, 5];

    /// <summary>Reference value of one unit of each resource, in scrap (for trade values; see <see cref="TradeValues"/>).</summary>
    public Dictionary<string, double> ResourceValues { get; init; } = new();
}

/// <summary>The schematics.json file: rules, traders and the catalogue.</summary>
public sealed record SchematicFile
{
    public SchematicRules Rules { get; init; } = new();
    public List<TraderDef> Traders { get; init; } = new();
    public List<SchematicDef> Schematics { get; init; } = new();
}

/// <summary>
/// The schematics catalogue, indexed: by id, and what gates each recipe, building, building level and perk. Shared by
/// the colony (<see cref="GameData.Schematics"/>) and the field (<see cref="Scavenge.ScavengeData.Schematics"/>). Empty
/// when no schematics.json is loaded: then nothing is gated.
/// </summary>
public sealed class SchematicCatalog
{
    public static readonly SchematicCatalog Empty = new(new SchematicFile());

    public IReadOnlyDictionary<string, SchematicDef> All { get; }
    public IReadOnlyDictionary<string, TraderDef> Traders { get; }
    public SchematicRules Rules { get; }

    private readonly Dictionary<(UnlockKind, string, int), string> _gates = new();

    public SchematicCatalog(SchematicFile file)
    {
        Rules = file.Rules;
        var all = new Dictionary<string, SchematicDef>(StringComparer.Ordinal);
        foreach (var s in file.Schematics)
        {
            if (string.IsNullOrWhiteSpace(s.Id)) throw new GameDataException("A schematic has no id.");
            if (!all.TryAdd(s.Id, s)) throw new GameDataException($"Duplicate schematic id '{s.Id}'.");
        }
        All = all;
        var traders = new Dictionary<string, TraderDef>(StringComparer.Ordinal);
        foreach (var t in file.Traders)
            if (string.IsNullOrWhiteSpace(t.Id) || !traders.TryAdd(t.Id, t)) throw new GameDataException($"Trader id '{t.Id}' is empty or used twice.");
        Traders = traders;
        foreach (var s in all.Values)
            foreach (var u in s.Unlocks)
            {
                var key = (u.Kind, u.Id, u.Level ?? 0);
                if (!_gates.TryAdd(key, s.Id))
                    throw new GameDataException($"Schematic '{s.Id}': {u.Kind} '{u.Id}' is already unlocked by '{_gates[key]}'.");
            }
        ValidateSelf();
    }

    public SchematicDef? Find(string id) => All.GetValueOrDefault(id);

    /// <summary>
    /// Extra unlock checks per kind, for features whose data Core's own checks do not know (vehicles): given the colony
    /// data and an unlock id, an error message or null. Kinds without a validator here and without a built-in check
    /// (<see cref="UnlockKind.Vehicle"/> by default) are not checked. Register at startup, before data loads.
    /// </summary>
    public static readonly Dictionary<UnlockKind, Func<GameData, string, string?>> UnlockValidators = new();

    /// <summary>The schematic that gates this unlock, or null when it is free (in the default set).</summary>
    public string? GateOf(UnlockKind kind, string id, int level = 0) => _gates.GetValueOrDefault((kind, id, level));

    public string? GateOfRecipe(string recipeId) => GateOf(UnlockKind.Recipe, recipeId);
    public string? GateOfPerk(string perkId) => GateOf(UnlockKind.Perk, perkId);
    public string? GateOfBuilding(string buildingId) => GateOf(UnlockKind.Building, buildingId);
    public string? GateOfBuildingLevel(string buildingId, int level) =>
        level <= 1 ? GateOfBuilding(buildingId) : GateOf(UnlockKind.BuildingLevel, buildingId, level);

    /// <summary>The duplicate value of a schematic in Research Data.</summary>
    public double DuplicateValue(SchematicDef s) => At(Rules.Research.DuplicateValue, s.Tier);

    /// <summary>Per-tier array lookup (tier 1 = index 0), clamped to the array.</summary>
    public static T At<T>(IReadOnlyList<T> perTier, int tier) => perTier.Count == 0 ? default! : perTier[Math.Clamp(tier - 1, 0, perTier.Count - 1)];

    /// <summary>The trader def by id (the default trader for null).</summary>
    public TraderDef? Trader(string? id) => Traders.GetValueOrDefault(id ?? Rules.Radio.DefaultTrader);

    private void ValidateSelf()
    {
        foreach (var s in All.Values)
        {
            var where = $"Schematic '{s.Id}'";
            if (s.Tier is < 1 or > 4) throw new GameDataException($"{where}: tier must be 1..4.");
            if (s.Unlocks.Count == 0) throw new GameDataException($"{where} unlocks nothing.");
            if (s.Sources.Count == 0) throw new GameDataException($"{where} has no sources.");
            if (s.Sources.Contains(SchematicSource.Migration) || s.Sources.Contains(SchematicSource.Debug))
                throw new GameDataException($"{where}: Migration and Debug are not data sources.");
            foreach (var u in s.Unlocks)
            {
                if (string.IsNullOrWhiteSpace(u.Id)) throw new GameDataException($"{where}: an unlock has no id.");
                if ((u.Kind == UnlockKind.BuildingLevel) != (u.Level is not null))
                    throw new GameDataException($"{where}: a level is given for (and only for) buildingLevel unlocks.");
                if (u.Kind == UnlockKind.BuildingLevel && u.Level < 2)
                    throw new GameDataException($"{where}: buildingLevel unlocks start at level 2 (level 1 is the building unlock).");
            }
            if (s.Has(SchematicSource.Research) && At(Rules.Research.Cost, s.Tier) <= 0)
                throw new GameDataException($"{where}: tier {s.Tier} cannot be researched (rules.research.cost is 0).");
        }
        var r = Rules;
        foreach (var arr in new[] { r.Drops.Cache, r.Drops.Locker, r.Drops.Terminal })
            if (arr.Length == 0 || arr.Any(p => p is < 0 or > 1)) throw new GameDataException("Schematics rules.drops: chances per tier in 0..1.");
        if (r.Drops.OwnTierWeight <= 0 || r.Drops.KnownWeight < 0) throw new GameDataException("Schematics rules.drops: positive weights.");
        if (r.Radio.VisitEveryDays.Length == 0 || r.Radio.VisitEveryDays.Any(d => d <= 0) || r.Radio.StayDays <= 0)
            throw new GameDataException("Schematics rules.radio: positive visit and stay days.");
        if (All.Count > 0 && Trader(null) is null) throw new GameDataException($"Schematics rules.radio: unknown default trader '{r.Radio.DefaultTrader}'.");
        if (Trader(null) is { StoryOnly: true }) throw new GameDataException("Schematics rules.radio: the default trader cannot be story-only.");
    }

    private static bool ProstheticKnown(GameData data, string id) => data.Cybernetics is { } cyber && (
        id.StartsWith("tier:") && int.TryParse(id[5..], out var tier) && cyber.Def.Tiers.Any(t => t.Tier == tier)
        || id.StartsWith("spec:") && cyber.Specs.ContainsKey(id[5..]));

    /// <summary>Checks the unlocks against the colony data (buildings, levels, recipes, perks, resources).</summary>
    internal void ValidateAgainst(GameData data)
    {
        if (All.Count == 0) return;
        if (!data.Resources.ContainsKey(Rules.ResearchDataResource))
            throw new GameDataException($"Schematics: unknown Research Data resource '{Rules.ResearchDataResource}'.");
        foreach (var s in All.Values.Where(s => !s.Planned))
            foreach (var u in s.Unlocks)
            {
                var where = $"Schematic '{s.Id}'";
                switch (u.Kind)
                {
                    case UnlockKind.Building when !data.Buildings.ContainsKey(u.Id):
                        throw new GameDataException($"{where}: unknown building '{u.Id}'.");
                    case UnlockKind.BuildingLevel when !data.Buildings.TryGetValue(u.Id, out var b) || u.Level > b.MaxLevel:
                        throw new GameDataException($"{where}: building '{u.Id}' has no level {u.Level}.");
                    case UnlockKind.Recipe when !data.Recipes.ContainsKey(u.Id):
                        throw new GameDataException($"{where}: unknown recipe '{u.Id}'.");
                    case UnlockKind.Perk when !data.Perks.ContainsKey(u.Id):
                        throw new GameDataException($"{where}: unknown perk '{u.Id}'.");
                    case UnlockKind.Prosthetic when !ProstheticKnown(data, u.Id):
                        throw new GameDataException($"{where}: '{u.Id}' is not a cybernetics tier:N or spec:ID (or cybernetics.json is not loaded).");
                }
                if (UnlockValidators.TryGetValue(u.Kind, out var check) && check(data, u.Id) is { } error)
                    throw new GameDataException($"{where}: {error}");
            }
        foreach (var id in new[] { Rules.Decrypt.Building, Rules.Research.Building, Rules.Radio.Building })
            if (!data.Buildings.ContainsKey(id)) throw new GameDataException($"Schematics rules: unknown building '{id}'.");
        foreach (var t in Traders.Values)
            if (t.MaxTier is < 1 or > 4) throw new GameDataException($"Trader '{t.Id}': maxTier 1..4.");
        var radio = Rules.Radio;
        foreach (var res in radio.Prices.SelectMany(p => p).SelectMany(o => o.Cost.Keys)
                     .Concat(radio.SellPrices.SelectMany(p => p.Keys)).Concat(radio.ResearchDataPrice.Keys)
                     .Concat(radio.ContractRewards.SelectMany(p => p.Keys)))
            if (res != Colony.Pantry.FoodKey && !data.Resources.ContainsKey(res))
                throw new GameDataException($"Schematics rules.radio: unknown resource '{res}'.");
        foreach (var needs in Rules.Research.Needs)
            foreach (var (b, level) in needs)
                if (!data.Buildings.TryGetValue(b, out var def) || level < 1 || level > def.MaxLevel)
                    throw new GameDataException($"Schematics rules.research.needs: '{b}' at a level it has.");
    }
}

/// <summary>
/// Reads data/schematics.json plus any per-feature extension files <c>data/schematics.*.json</c> (vehicles, weapon mods,
/// story…). The main file holds the rules; an extension adds <c>schematics</c> and <c>traders</c> only (its
/// <c>rules</c> are ignored). Files merge in name order; an id used twice is an error.
/// </summary>
public static class SchematicCatalogLoader
{
    public const string FileName = "schematics.json";

    /// <summary>The main file then the extensions (<c>schematics.*.json</c>, by name) in a directory.</summary>
    public static IReadOnlyList<string> Files(string directory)
    {
        var main = Path.Combine(directory, FileName);
        if (!File.Exists(main)) return [];
        var extensions = Directory.GetFiles(directory, "schematics.*.json").Where(f => Path.GetFileName(f) != FileName).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal);
        return [main, .. extensions];
    }

    /// <summary>The catalogue in <paramref name="directory"/>, or <see cref="SchematicCatalog.Empty"/> when there is no file.</summary>
    public static SchematicCatalog LoadFromDirectory(string directory) =>
        LoadFromJson(Files(directory).Select(File.ReadAllText).ToArray());

    /// <summary>Main file first, then extensions; none = the empty catalogue.</summary>
    public static SchematicCatalog LoadFromJson(params string?[] files)
    {
        var parsed = files.OfType<string>().Select((json, i) => Parse(json, i)).ToList();
        if (parsed.Count == 0) return SchematicCatalog.Empty;
        return new SchematicCatalog(parsed[0] with
        {
            Traders = parsed.SelectMany(f => f.Traders).ToList(),
            Schematics = parsed.SelectMany(f => f.Schematics).ToList(),
        });
    }

    private static SchematicFile Parse(string json, int index)
    {
        try
        {
            return JsonSerializer.Deserialize<SchematicFile>(json, Json.Options)
                ?? throw new GameDataException($"schematics file {index} is empty.");
        }
        catch (JsonException e)
        {
            throw new GameDataException($"schematics file {index} is invalid: {e.Message}");
        }
    }
}
