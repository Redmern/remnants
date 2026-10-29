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
        Items = Index(items, i => i.Id, "item");
        Enemies = Index(enemies, e => e.Id, "enemy");
        Zones = Index(zones, z => z.Id, "zone");
        Rules = rules;
        Validate();
    }

    public TerrainDef Terrain(Terrain terrain) => Rules.Terrain[terrain];

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
        if (Rules.Npc.Names.Count == 0) throw new GameDataException("Scavenge rules: npc.names is empty.");
        RequireRange(Rules.Npc.AttributeRange, "Scavenge rules npc.attributeRange");

        foreach (var item in Items.Values)
        {
            if (item.Weight < 0) throw new GameDataException($"Item '{item.Id}' has negative weight.");
            foreach (var a in item.Actions) ValidateAction(a, $"Item '{item.Id}'");
            if (item.Kind == ItemKind.Weapon && item.Actions.Count == 0)
                throw new GameDataException($"Item '{item.Id}' is a weapon without actions.");
            if (item.Kind == ItemKind.Consumable && item.Use is null)
                throw new GameDataException($"Item '{item.Id}' is a consumable without a use effect.");
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
            foreach (var e in zone.EnemyMix)
                if (!Enemies.ContainsKey(e.Id)) throw new GameDataException($"{where}: unknown enemy '{e.Id}'.");
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
