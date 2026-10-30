using System.Text.Json;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>Static scavenge definitions (items, enemies, zones, rules), indexed by id. Immutable after load.</summary>
public sealed class ScavengeData
{
    public IReadOnlyDictionary<string, ItemDef> Items { get; }
    public IReadOnlyDictionary<string, EnemyDef> Enemies { get; }
    public IReadOnlyDictionary<string, ZoneDef> Zones { get; }
    public ScavengeRules Rules { get; }

    public ScavengeData(IEnumerable<ItemDef> items, IEnumerable<EnemyDef> enemies, IEnumerable<ZoneDef> zones, ScavengeRules rules)
    {
        // Variants are always derived (so data built from another ScavengeData's items gets them fresh).
        Items = Index(WithQualityVariants(items.Where(i => i.BaseId is null).ToList(), rules.Quality), i => i.Id, "item");
        Enemies = Index(enemies, e => e.Id, "enemy");
        Zones = Index(zones, z => z.Id, "zone");
        Rules = rules;
        Validate();
    }

    public TerrainDef Terrain(Terrain terrain) => Rules.Terrain[terrain];

    /// <summary>The item id of <paramref name="itemId"/> at a quality tier (the defined id for the base tier).</summary>
    public string QualityVariant(string itemId, string tierId)
    {
        var item = Items[itemId];
        var tier = Rules.Quality.Find(q => q.Id == tierId) ?? throw new ArgumentException($"Unknown quality '{tierId}'.", nameof(tierId));
        if (!item.HasQuality || tier.Base) return item.DefinedId;
        return $"{item.DefinedId}@{tier.Id}";
    }

    /// <summary>The quality tier an item is at (the base tier for defined items), or null when there are no tiers.</summary>
    public QualityTierDef? QualityOf(ItemDef item) =>
        item.Quality is { } q ? Rules.Quality.Find(t => t.Id == q) : Rules.Quality.Find(t => t.Base);

    private static IEnumerable<ItemDef> WithQualityVariants(List<ItemDef> items, List<QualityTierDef> tiers)
    {
        foreach (var item in items) yield return item;
        foreach (var item in items.Where(i => i.HasQuality))
            foreach (var tier in tiers.Where(t => !t.Base))
                yield return Variant(item, tier);
    }

    /// <summary>Gear at a quality tier: what it does scales with the tier, how heavy it is with its weight multiplier.</summary>
    private static ItemDef Variant(ItemDef item, QualityTierDef tier)
    {
        var m = tier.StatMultiplier;
        double Bonus(double value) => value > 0 ? value * m : value;
        return item with
        {
            Id = $"{item.Id}@{tier.Id}",
            Name = $"{tier.Name} {item.Name}",
            BaseId = item.Id,
            Quality = tier.Id,
            LootWeight = 0,
            Weight = Math.Round(item.Weight * tier.WeightMultiplier, 2),
            Armor = item.Armor * m,
            Dodge = Bonus(item.Dodge),
            Initiative = Bonus(item.Initiative),
            SightBonus = item.SightBonus * m,
            CarryBonus = item.CarryBonus * m,
            Stealth = Bonus(item.Stealth),
            Detection = Bonus(item.Detection),
            Actions = item.Actions.Select(a => a with
            {
                MinDamage = (int)Math.Round(a.MinDamage * m),
                MaxDamage = (int)Math.Round(a.MaxDamage * m),
            }).ToList(),
        };
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> id, string kind)
    {
        var result = new Dictionary<string, T>();
        foreach (var item in items)
        {
            var key = id(item);
            if (string.IsNullOrWhiteSpace(key)) throw new GameDataException($"A {kind} has no id.");
            if (!result.TryAdd(key, item)) throw new GameDataException($"Duplicate {kind} id '{key}'.");
        }
        return result;
    }

    private void Validate()
    {
        foreach (var t in Enum.GetValues<Terrain>())
            if (!Rules.Terrain.ContainsKey(t)) throw new GameDataException($"Scavenge rules: terrain '{t}' is not defined.");
        if (Rules.Turn.BaseAp < 1) throw new GameDataException("Scavenge rules: turn.baseAp must be at least 1.");
        if (Rules.Movement.AgilityPerRange <= 0) throw new GameDataException("Scavenge rules: movement.agilityPerRange must be positive.");
        if (Rules.Combat.HitLocations.Count == 0 || Rules.Combat.HitLocations.Values.Sum() <= 0)
            throw new GameDataException("Scavenge rules: combat.hitLocations needs positive weights.");
        foreach (var region in Enum.GetValues<BodyRegion>())
            if (Rules.Combat.PartToughness.GetValueOrDefault(region) <= 0)
                throw new GameDataException($"Scavenge rules: combat.partToughness.{region} must be positive.");
        ValidateAction(Rules.Combat.Unarmed, "Scavenge rules unarmed");
        if (Rules.Network.PylonRange < 1 || Rules.Network.ReinforceAfterRounds < 1)
            throw new GameDataException("Scavenge rules: network.pylonRange and network.reinforceAfterRounds must be at least 1.");
        if (Rules.World.ColonyPos.Length != 2) throw new GameDataException("Scavenge rules: world.colonyPos must be [x, y].");
        if (Rules.Npc.Names.Count == 0) throw new GameDataException("Scavenge rules: npc.names is empty.");
        RequireRange(Rules.Npc.AttributeRange, "Scavenge rules npc.attributeRange");

        if (Rules.Quality.Count > 0)
        {
            if (Rules.Quality.Count(q => q.Base) != 1) throw new GameDataException("Scavenge rules: quality needs exactly one base tier.");
            if (Rules.Quality.Any(q => string.IsNullOrWhiteSpace(q.Id) || q.Id.Contains('@') || q.StatMultiplier <= 0 || q.WeightMultiplier <= 0))
                throw new GameDataException("Scavenge rules: quality tiers need an id without '@' and positive multipliers.");
            if (Rules.Quality.Select(q => q.Id).Distinct().Count() != Rules.Quality.Count)
                throw new GameDataException("Scavenge rules: duplicate quality tier ids.");
        }

        foreach (var item in Items.Values)
        {
            if (item.BaseId is null && item.Id.Contains('@')) throw new GameDataException($"Item id '{item.Id}' must not contain '@' (reserved for quality variants).");
            if ((item.Kind == ItemKind.Blueprint) != (item.Blueprint is not null))
                throw new GameDataException($"Item '{item.Id}': blueprint items (and only they) need a blueprint definition.");
            if (item.Device is { } device && (item.Kind != ItemKind.Tool || device.Radius < 1))
                throw new GameDataException($"Item '{item.Id}': devices are tools with a radius of at least 1.");
            if (item.Weight < 0) throw new GameDataException($"Item '{item.Id}' has negative weight.");
            foreach (var a in item.Actions) ValidateAction(a, $"Item '{item.Id}'");
            if (item.Kind == ItemKind.Weapon && item.Actions.Count == 0)
                throw new GameDataException($"Item '{item.Id}' is a weapon without actions.");
            if (item.Kind == ItemKind.Consumable && item.Use is null)
                throw new GameDataException($"Item '{item.Id}' is a consumable without a use effect.");
            if (item.Use is { LightRadius: > 0 } flare && flare.LightRounds < 1)
                throw new GameDataException($"Item '{item.Id}': a flare needs lightRounds of at least 1.");
            if (item.Use is { TargetsTile: true } placed && placed.Range < 1)
                throw new GameDataException($"Item '{item.Id}': items used on a tile need a range of at least 1.");
            if (item.Kind == ItemKind.Prosthetic && item.Prosthetic is null)
                throw new GameDataException($"Item '{item.Id}' is a prosthetic without a prosthetic definition.");
            if (item.Prosthetic is { } p && (p.Quality <= 0 || p.MinMedBayLevel < 1 || p.Region is BodyRegion.Head or BodyRegion.Torso))
                throw new GameDataException($"Item '{item.Id}': prosthetics replace an arm or leg, with positive quality and Med Bay level of at least 1.");
            if (item.Salvage is { } salvage && (string.IsNullOrWhiteSpace(salvage.Resource) || salvage.Amount <= 0))
                throw new GameDataException($"Item '{item.Id}': salvage needs a resource and a positive amount.");
            foreach (var mode in item.Movement?.AddModes ?? [])
                if (mode.Range < 1 || mode.ApCost < 1)
                    throw new GameDataException($"Item '{item.Id}': added move modes need range and AP cost of at least 1.");
        }

        foreach (var enemy in Enemies.Values)
        {
            if (enemy.Hp <= 0) throw new GameDataException($"Enemy '{enemy.Id}' needs positive hp.");
            if (enemy.Ai == AiKind.Linked && enemy.RelayRange < 1) throw new GameDataException($"Enemy '{enemy.Id}': Linked need a relayRange of at least 1.");
            foreach (var item in enemy.Equipment)
                if (!Items.TryGetValue(item, out var def) || def.Slot is null)
                    throw new GameDataException($"Enemy '{enemy.Id}': '{item}' is not an equippable item.");
        }

        foreach (var zone in Zones.Values)
        {
            var where = $"Zone '{zone.Id}'";
            if (zone.Width < 8 || zone.Height < 8) throw new GameDataException($"{where} must be at least 8x8.");
            if (zone.Exits < 1) throw new GameDataException($"{where} needs at least one exit.");
            if (zone.SpawnEdge == zone.ExitEdge) throw new GameDataException($"{where}: spawn and exit edge must differ.");
            RequireRange(zone.Enemies, $"{where} enemies");
            RequireRange(zone.Caches, $"{where} caches");
            RequireRange(zone.ItemsPerCache, $"{where} itemsPerCache");
            RequireRange(zone.Npcs, $"{where} npcs");
            RequireRange(zone.Terrain.Rooms, $"{where} terrain.rooms");
            RequireRange(zone.Terrain.RoomSize, $"{where} terrain.roomSize");
            if (zone.Terrain.RoomSize[0] < 3) throw new GameDataException($"{where}: rooms must be at least 3 wide.");
            if (zone.Enemies[1] > 0 && zone.EnemyMix.Count == 0) throw new GameDataException($"{where} has enemies but no enemyMix.");
            foreach (var e in zone.EnemyMix.Concat(zone.Reinforcements.Mix))
                if (!Enemies.ContainsKey(e.Id)) throw new GameDataException($"{where}: unknown enemy '{e.Id}'.");
            RequireRange(zone.Pylons, $"{where} pylons");
            RequireRange(zone.Reinforcements.Count, $"{where} reinforcements.count");
            if (zone.Reinforcements.Waves > 0 && zone.Reinforcements.Mix.Count == 0)
                throw new GameDataException($"{where} has reinforcement waves but no reinforcement mix.");
            if (zone.TravelDays < 0) throw new GameDataException($"{where}: travelDays must not be negative.");
            if (zone.DepletionPerVisit is < 0 or > 1 || zone.ReplenishPerDay < 0)
                throw new GameDataException($"{where}: depletionPerVisit must be 0..1 and replenishPerDay not negative.");
            if (zone.MapPos.Length != 2 || zone.MapPos.Any(v => v is < 0 or > 1))
                throw new GameDataException($"{where}: mapPos must be [x, y] with both in 0..1.");
            foreach (var other in zone.Reveals)
                if (!Zones.ContainsKey(other)) throw new GameDataException($"{where} reveals unknown zone '{other}'.");
            if (zone.Caches[1] > 0 && !Items.Values.Any(i => i.LootWeight > 0 && i.Tier <= zone.LootTier))
                throw new GameDataException($"{where} has caches but no items at loot tier {zone.LootTier}.");
        }
    }

    private static void ValidateAction(ActionDef a, string where)
    {
        if (string.IsNullOrWhiteSpace(a.Id)) throw new GameDataException($"{where}: an action has no id.");
        if (a.Reach < 1 || a.ApCost < 1) throw new GameDataException($"{where}: action '{a.Id}' needs reach and AP cost of at least 1.");
        if (a.MinDamage < 0 || a.MaxDamage < a.MinDamage)
            throw new GameDataException($"{where}: action '{a.Id}' needs 0 <= minDamage <= maxDamage.");
    }

    private static void RequireRange(int[] range, string name)
    {
        if (range.Length != 2 || range[0] < 0 || range[0] > range[1])
            throw new GameDataException($"{name} must be [min, max] with 0 <= min <= max.");
    }
}

/// <summary>Loads items.json, enemies.json, zones.json and scavenge.json from the data/ folder.</summary>
public static class ScavengeDataLoader
{
    public static ScavengeData LoadFromDirectory(string directory)
    {
        string Read(string file)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path)) throw new GameDataException($"Missing data file '{path}'.");
            return File.ReadAllText(path);
        }

        return LoadFromJson(Read("items.json"), Read("enemies.json"), Read("zones.json"), Read("scavenge.json"));
    }

    public static ScavengeData LoadFromJson(string items, string enemies, string zones, string rules) =>
        new(
            Parse<List<ItemDef>>(items, "items"),
            Parse<List<EnemyDef>>(enemies, "enemies"),
            Parse<List<ZoneDef>>(zones, "zones"),
            Parse<ScavengeRules>(rules, "scavenge"));

    private static T Parse<T>(string json, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json.Options)
                ?? throw new GameDataException($"{name}.json is empty.");
        }
        catch (JsonException e)
        {
            throw new GameDataException($"{name}.json is invalid: {e.Message}");
        }
    }
}
