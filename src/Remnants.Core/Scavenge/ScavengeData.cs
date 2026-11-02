using System.Text.Json;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Schematics;
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

    /// <summary>The schematics catalogue: drop odds and pools; each schematic is also an item with its id.</summary>
    public SchematicCatalog Schematics { get; }

    /// <summary>
    /// Perk id → its scavenge hooks (from perks.json when loaded with it), so a party's perks reach the field with the
    /// survivors alone (<see cref="HooksFor"/>), locally and on a server.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> PerkHooks { get; }

    /// <summary>
    /// The cybernetics tree (cybernetics.json, when loaded with it): tiers, specialisations, sets and interference, so a
    /// fitted part's hooks and set bonuses reach the field with the survivor alone. Null without it.
    /// </summary>
    public CyberneticsData? Cybernetics { get; }

    /// <summary>
    /// The medicine system (medicine.json, when loaded with it): conditions' field penalties, fractures and pain, so a
    /// survivor's conditions reach the field with them. Null without it (no fractures, conditions cost nothing in the field).
    /// </summary>
    public MedicineData? Medicine { get; }

    /// <summary>Electronics devices, drones and power (electronics.json, when loaded with it); null without them.</summary>
    public ElectronicsDef? Electronics { get; }

    /// <summary>Gear mods and Mk upgrades (gear_mods.json, when loaded with it); null without them.</summary>
    public GearModsData? GearMods { get; }

    /// <summary>Loot tables (loot_tables*.json): what kills, salvage and raiders leave. Empty without the file.</summary>
    public LootCatalog Loot { get; }

    /// <summary>The loot files <see cref="Loot"/> was built from, so data rebuilt from this one (tests, tools) keeps them.</summary>
    public IReadOnlyList<LootTableFile> LootFiles { get; }

    /// <summary>Salvage nodes and zone densities (salvage.json); none without the file.</summary>
    public SalvageRules Salvage { get; }

    /// <summary>
    /// Loot entries skipped on load (items or tables another branch has not shipped yet) and enemies naming a missing
    /// table. Never fatal; the game logs them.
    /// </summary>
    public IReadOnlyList<string> LootWarnings { get; }

    /// <summary>Tactical cards and the squad deck's rules (cards.json); <see cref="CardCatalog.Empty"/> without the file.</summary>
    public CardCatalog Cards { get; }

    public ScavengeData(IEnumerable<ItemDef> items, IEnumerable<EnemyDef> enemies, IEnumerable<ZoneDef> zones, ScavengeRules rules,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? perkHooks = null, CyberneticsDef? cybernetics = null,
        SchematicCatalog? schematics = null, ElectronicsDef? electronics = null, GearModsDef? gearMods = null,
        MedicineDef? medicine = null, IReadOnlyList<LootTableFile>? loot = null, SalvageRules? salvage = null, CardsDef? cards = null)
    {
        Cards = cards is null ? CardCatalog.Empty : new CardCatalog(cards);
        Schematics = schematics ?? SchematicCatalog.Empty;
        // Schematic items come from the catalogue; without one, data built from another ScavengeData's items keeps them.
        if (schematics is not null) items = items.Where(i => i.Kind != ItemKind.Schematic).Concat(Schematics.All.Values.Select(SchematicItem));
        PerkHooks = perkHooks ?? new Dictionary<string, IReadOnlyDictionary<string, double>>();
        Cybernetics = cybernetics is null ? null : new CyberneticsData(cybernetics);
        Medicine = medicine is null ? null : new MedicineData(medicine);
        Electronics = electronics;
        var defined = items.Where(i => i.BaseId is null).ToList();
        if (electronics is not null)
        {
            // Devices and components (skipped when the list already has them: data built from another's items).
            var have = defined.Select(i => i.Id).ToHashSet();
            defined.AddRange(electronics.Items.Where(i => !have.Contains(i.Id)));
            enemies = enemies.Select(e => electronics.Drops.TryGetValue(e.Id, out var extra) && extra.Count > 0 && e.Drops.All(d => d.Item != extra[0].Item)
                ? e with { Drops = e.Drops.Concat(extra).ToList() } : e);
        }
        GearMods = gearMods is null ? null : new GearModsData(gearMods);
        if (GearMods is { } mods)
        {
            // Mod, kit and headgear items (skipped when the list already has them: data built from another's items). Their
            // schematics are in the catalogue (schematics.gearmods.json), drops of them in Drops.
            var have = defined.Select(i => i.Id).ToHashSet();
            defined.AddRange(mods.ExtraItems().Where(i => !have.Contains(i.Id)));
            enemies = enemies.Select(e => mods.Def.Drops.TryGetValue(e.Id, out var extra) && !e.Drops.Contains(extra[0])
                ? e with { Drops = e.Drops.Concat(extra).ToList() } : e)
                .Select(e => mods.Def.EnemyTags.TryGetValue(e.Id, out var tags) ? e with { Tags = e.Tags.Union(tags).ToList() } : e);
        }
        // Variants are always derived (so data built from another ScavengeData's items gets them fresh).
        var index = Index(WithQualityVariants(defined, rules.Quality), i => i.Id, "item");
        Items = new ItemCatalog(index, GearMods is null ? null : ResolveModded);
        Enemies = Index(enemies, e => e.Id, "enemy");
        Zones = Index(zones, z => z.Id, "zone");
        Rules = rules;
        LootFiles = loot ?? [];
        Loot = LootCatalog.Build(LootFiles, Items, rules.Quality.Select(q => q.Id).ToList());
        Salvage = salvage ?? new SalvageRules();
        var warnings = Loot.Warnings.ToList();
        foreach (var e in Enemies.Values.Where(e => e.Loot is not null && Loot.Find(e.Loot) is null).OrderBy(e => e.Id, StringComparer.Ordinal))
            warnings.Add($"Enemy '{e.Id}': unknown loot table '{e.Loot}' (it drops only its schematics).");
        // Card rows need cards.json; data without it (tests, tools) only warns.
        foreach (var (table, card) in Loot.Tables.Values.SelectMany(t => t.Entries.Select(e => e.Card).Concat(t.Guaranteed.Select(e => e.Card))
                     .Concat(t.Rare.Select(r => r.Card)).OfType<string>().Select(c => (t.Id, c))))
            if (Cards.Find(card) is null)
            {
                if (Cards != CardCatalog.Empty) throw new GameDataException($"Loot table '{table}': unknown card '{card}'.");
                warnings.Add($"Loot table '{table}': card '{card}' without cards.json.");
            }
        LootWarnings = warnings;
        Validate();
        ValidateCybernetics();
        ValidateSalvage();
        if (Electronics is not null) ElectronicsValidation.Validate(Electronics, this);
        GearMods?.Validate(index, this);
        FieldSalvage.Validate(this);
    }

    /// <summary>A modded gear id as its item, or null when it is not a valid build in canonical form.</summary>
    private ItemDef? ResolveModded(string id)
    {
        var mods = GearMods!;
        var parsed = GearModsData.Parse(id);
        if (GearModsData.IsComposite(parsed.BaseId)) return null;
        var defined = (ItemCatalog)Items;
        if (mods.Check(defined, parsed) is not null) return null;
        if (mods.Canonical(parsed.BaseId, parsed.Mk, parsed.Mods) != id) return null;
        return mods.Build(defined, parsed, Rules.Quality);
    }

    public TerrainDef Terrain(Terrain terrain) => Rules.Terrain[terrain];

    /// <summary>Every node is a prosthetic item of its slot's region, and every prosthetic item a node.</summary>
    private void ValidateCybernetics()
    {
        if (Cybernetics is not { } cyber) return;
        foreach (var n in cyber.Nodes.Values)
        {
            var region = n.Slot switch { CyberSlot.Arm => BodyRegion.Arms, CyberSlot.Leg => BodyRegion.Legs, _ => BodyRegion.Head };
            if (!Items.TryGetValue(n.Id, out var item) || item.Prosthetic?.Region != region)
                throw new GameDataException($"Cybernetics node '{n.Id}': needs a prosthetic item of the same id for the {region}.");
        }
        foreach (var item in Items.Values.Where(i => i.Prosthetic is not null && cyber.Node(i.Id) is null))
            throw new GameDataException($"Item '{item.Id}': every prosthetic needs a node in cybernetics.json.");
    }
    /// <summary>The field item of a schematic: a plan or chip, light, never in the loot tables (drops roll separately).</summary>
    public static ItemDef SchematicItem(SchematicDef s) => new()
    {
        Id = s.Id, Name = s.ItemName, Kind = ItemKind.Schematic, Weight = 0.1, Tier = s.Tier, Schematic = s.Id,
    };

    /// <summary>The summed hooks of these perks (unknown perk ids are ignored).</summary>
    public Dictionary<string, double> HooksFor(IEnumerable<string> perkIds)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var id in perkIds)
            if (PerkHooks.TryGetValue(id, out var hooks))
                foreach (var (k, v) in hooks) result[k] = result.GetValueOrDefault(k) + v;
        return result;
    }

    /// <summary>The hooks of a body's fitted cybernetics and its paired sets, each with the parts it needs (empty without the tree).</summary>
    public List<CyberHookSource> CyberHooksFor(Body body)
    {
        var result = new List<CyberHookSource>();
        if (Cybernetics is not { } cyber) return result;
        foreach (var (part, state) in body.Parts.OrderBy(kv => kv.Key))
            if (state.Condition == PartCondition.Prosthetic && cyber.Node(state.ProstheticItemId) is { Effects.Hooks.Count: > 0 } node)
                result.Add(new CyberHookSource { Parts = [part], Hooks = new(node.Effects.Hooks, StringComparer.Ordinal) });
        foreach (var (set, parts) in cyber.ActiveSets(body))
            if (set.Effects.Hooks.Count > 0)
                result.Add(new CyberHookSource { Parts = parts, Hooks = new(set.Effects.Hooks, StringComparer.Ordinal) });
        return result;
    }

    /// <summary>
    /// The prosthetic items a unit has fitted and working (not missing, not locked by interference), with their part.
    /// Their item stats (armor, stealth, sight, attacks, move modes, a hacking tool) count as if equipped.
    /// </summary>
    public IEnumerable<(BodyPart Part, ItemDef Item)> FittedParts(Body body) =>
        body.Parts.OrderBy(kv => kv.Key)
            .Where(kv => kv.Value.Condition == PartCondition.Prosthetic && kv.Value.Efficiency > 0
                         && kv.Value.ProstheticItemId is { } id && Items.ContainsKey(id))
            .Select(kv => (kv.Key, Items[kv.Value.ProstheticItemId!]));

    public DeviceKindDef Device(DeviceKind kind) => Rules.Devices.TryGetValue(kind, out var def) ? def : new DeviceKindDef();

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
            Use = item.Use is { } use ? use with
            {
                Heal = use.Heal * m,
                ReviveHp = use.ReviveHp * m,
                CureStrength = use.CureStrength * m,
                PainRelief = Math.Min(1, use.PainRelief * m),
            } : null,
            RangedAccuracy = Bonus(item.RangedAccuracy),
            Device = item.Device is { } dev ? dev with { Radius = Math.Max(1, (int)Math.Round(dev.Radius * m)) } : null,
            Drone = item.Drone is { } dr ? dr with { Battery = Math.Max(1, (int)Math.Round(dr.Battery * m)) } : null,
            HackTool = item.HackTool is { } ht ? ht with { ChanceBonus = ht.ChanceBonus + (m - 1) * 0.1 } : null,
            Radio = item.Radio is { } ra ? ra with { Range = Math.Max(1, (int)Math.Round(ra.Range * m)), AccuracyBonus = ra.AccuracyBonus * m } : null,
            Actions = item.Actions.Select(a => a with
            {
                MinDamage = (int)Math.Round(a.MinDamage * m),
                MaxDamage = (int)Math.Round(a.MaxDamage * m),
                Accuracy = a.Accuracy + (m - 1) * a.QualityAccuracy,
                Bane = a.Bane is { } b ? b with { DamageMultiplier = 1 + (b.DamageMultiplier - 1) * m, ArmorPierce = b.ArmorPierce * m } : null,
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

    /// <summary>The salvage table names items, so it is checked after everything else.</summary>
    private void ValidateSalvage()
    {
        foreach (var e in Rules.Salvage.Table)
            if (!Items.TryGetValue(e.Item, out var part) || e.Weight <= 0 || e.MinTier < 1)
                throw new GameDataException($"Scavenge rules: salvage entry '{e.Item}' needs a known item, a positive weight and minTier ≥ 1.");
        foreach (var q in Rules.Salvage.QualityByLootTier)
            if (Rules.Quality.Count > 0 && Rules.Quality.All(t => t.Id != q)) throw new GameDataException($"Scavenge rules: salvage quality '{q}' is not a quality tier.");
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
            if (item.Id.Contains(ModdedId.Separator)) throw new GameDataException($"Item id '{item.Id}' must not contain '+' (reserved for gear mods).");
            if ((item.Kind == ItemKind.Schematic) != (item.Schematic is not null) || item.Kind == ItemKind.Schematic && item.LootWeight > 0)
                throw new GameDataException($"Item '{item.Id}': schematic items come from schematics.json, without a loot weight.");
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
            if (item.Prosthetic is { } p && (p.Quality <= 0 || p.MinMedBayLevel < 1 || p.Region is BodyRegion.Torso))
                throw new GameDataException($"Item '{item.Id}': prosthetics replace an arm, a leg or the eyes (Head), with positive quality and Med Bay level of at least 1.");
            if (item.Salvage is { } salvage && (string.IsNullOrWhiteSpace(salvage.Resource) || salvage.Amount <= 0))
                throw new GameDataException($"Item '{item.Id}': salvage needs a resource and a positive amount.");
            foreach (var mode in item.Movement?.AddModes ?? [])
                if (mode.Range < 1 || mode.ApCost < 1)
                    throw new GameDataException($"Item '{item.Id}': added move modes need range and AP cost of at least 1.");
        }

        foreach (var (kind, def) in Rules.Devices)
        {
            if (def.Work < 1) throw new GameDataException($"Scavenge rules: device '{kind}' needs work of at least 1.");
            RequireRange(def.LootCount, $"Scavenge rules device '{kind}' lootCount");
            if (def.Enemy is { } e && (!Enemies.TryGetValue(e, out var unit) || unit.Ai != AiKind.Device && unit.Ai != AiKind.Linked))
                throw new GameDataException($"Scavenge rules: device '{kind}' names unknown device unit '{e}'.");
            if (kind is DeviceKind.Camera or DeviceKind.Turret && def.Enemy is null)
                throw new GameDataException($"Scavenge rules: device '{kind}' needs an enemy definition for its unit.");
            foreach (var l in def.Loot)
                if (!Items.ContainsKey(l.Id)) throw new GameDataException($"Scavenge rules: device '{kind}' loot names unknown item '{l.Id}'.");
        }
        foreach (var id in Rules.Drone.Equipment)
            if (!Items.TryGetValue(id, out var frame) || frame.Slot is null)
                throw new GameDataException($"Scavenge rules: drone equipment '{id}' is not an equippable item.");
        if (Rules.Hacking.ShockDamage.Length != 2 || Rules.Hacking.ShockDamage[0] > Rules.Hacking.ShockDamage[1])
            throw new GameDataException("Scavenge rules: hacking.shockDamage must be [min, max].");
        foreach (var item in Items.Values)
        {
            if (item.HackTool is { } tool && (tool.Range < 1 || tool.Progress < 1))
                throw new GameDataException($"Item '{item.Id}': a hacking tool needs range and progress of at least 1.");
            if (item.Drone is { } drone && (drone.Battery < 1 || drone.FlyRange < 1 || drone.ControlRange < 1))
                throw new GameDataException($"Item '{item.Id}': a scout drone needs battery, flyRange and controlRange of at least 1.");
        }

        foreach (var enemy in Enemies.Values)
        {
            if (enemy.Hp <= 0) throw new GameDataException($"Enemy '{enemy.Id}' needs positive hp.");
            if (enemy.Ai == AiKind.Linked && enemy.RelayRange < 1) throw new GameDataException($"Enemy '{enemy.Id}': Linked need a relayRange of at least 1.");
            foreach (var item in enemy.Equipment)
                if (!Items.TryGetValue(item, out var def) || def.Slot is null)
                    throw new GameDataException($"Enemy '{enemy.Id}': '{item}' is not an equippable item.");
            foreach (var drop in enemy.Drops)
                if (!Items.ContainsKey(drop.Item) || Items[drop.Item].Natural || drop.Chance is <= 0 or > 1)
                    throw new GameDataException($"Enemy '{enemy.Id}': drop '{drop.Item}' needs a known, non-natural item and a chance in (0, 1].");
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
            foreach (var (kind, range) in zone.Devices) RequireRange(range, $"{where} devices.{kind}");
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
        if (a.Bane is { } bane && (string.IsNullOrWhiteSpace(bane.Tag) || bane.DamageMultiplier < 0 || bane.ArmorPierce < 0))
            throw new GameDataException($"{where}: action '{a.Id}' bane needs a tag and non-negative numbers.");
    }

    private static void RequireRange(int[] range, string name)
    {
        if (range.Length != 2 || range[0] < 0 || range[0] > range[1])
            throw new GameDataException($"{name} must be [min, max] with 0 <= min <= max.");
    }
}

/// <summary>Loads items.json, enemies.json, zones.json and scavenge.json (plus perks.json and cybernetics.json if there) from the data/ folder.</summary>
public static class ScavengeDataLoader
{
    public static ScavengeData LoadFromDirectory(string directory) => Load(DataSource.FromDirectory(directory));

    /// <summary>The scavenge definitions in <paramref name="data"/>, with the schematic catalogue, loot tables and salvage rules.</summary>
    public static ScavengeData Load(DataSource data)
    {
        var loaded = LoadFromJson(data.Read("items.json"), data.Read("enemies.json"), data.Read("zones.json"), data.Read("scavenge.json"),
            data.TryRead("perks.json"), data.TryRead("cybernetics.json"), data.TryRead("electronics.json"), data.TryRead("gear_mods.json"), data.TryRead("medicine.json"),
            data.ReadCatalogue(SchematicCatalogLoader.FileName));
        loaded = WithCards(loaded, data.TryRead(CardsFileName));
        return WithLoot(loaded, data.ReadCatalogue(LootTableLoader.FileName), data.TryRead(FieldSalvage.FileName));
    }

    public const string CardsFileName = "cards.json";

    /// <summary>The same data with the cards of <paramref name="cards"/> (cards.json; null: none).</summary>
    public static ScavengeData WithCards(ScavengeData data, string? cards) =>
        new(data.Items.Values.Where(i => i.BaseId is null), data.Enemies.Values, data.Zones.Values, data.Rules, data.PerkHooks, data.Cybernetics?.Def,
            data.Schematics, data.Electronics, data.GearMods?.Def, data.Medicine?.Def, data.LootFiles, data.Salvage,
            cards is null ? null : Parse<CardsDef>(cards, "cards"));

    /// <summary>
    /// The same data with loot tables (main file first, then <c>loot_tables.&lt;feature&gt;.json</c>) and salvage rules
    /// (salvage.json) added.
    /// </summary>
    public static ScavengeData WithLoot(ScavengeData data, IEnumerable<string> lootTables, string? salvage) =>
        new(data.Items.Values.Where(i => i.BaseId is null), data.Enemies.Values, data.Zones.Values, data.Rules, data.PerkHooks, data.Cybernetics?.Def,
            data.Schematics, data.Electronics, data.GearMods?.Def, data.Medicine?.Def, LootTableLoader.Parse(lootTables), FieldSalvage.Parse(salvage),
            data.Cards == CardCatalog.Empty ? null : data.Cards.Def);

    /// <param name="perks">perks.json (optional): only each perk's scavenge hooks are read (see <see cref="ScavengeData.PerkHooks"/>).</param>
    /// <param name="cybernetics">cybernetics.json (optional; see <see cref="ScavengeData.Cybernetics"/>).</param>
    /// <param name="electronics">electronics.json (optional; see <see cref="ScavengeData.Electronics"/>).</param>
    public static ScavengeData LoadFromJson(string items, string enemies, string zones, string rules, string? perks = null, string? cybernetics = null,
        string? electronics = null, params string[]? schematics) =>
        LoadFromJson(items, enemies, zones, rules, perks, cybernetics, electronics, gearMods: null, schematics ?? []);

    /// <param name="gearMods">gear_mods.json (optional; see <see cref="ScavengeData.GearMods"/>).</param>
    public static ScavengeData LoadFromJson(string items, string enemies, string zones, string rules, string? perks, string? cybernetics,
        string? electronics, string? gearMods, string[] schematics) =>
        LoadFromJson(items, enemies, zones, rules, perks, cybernetics, electronics, gearMods, medicine: null, schematics);

    /// <param name="medicine">medicine.json (optional; see <see cref="ScavengeData.Medicine"/>).</param>
    public static ScavengeData LoadFromJson(string items, string enemies, string zones, string rules, string? perks, string? cybernetics,
        string? electronics, string? gearMods, string? medicine, string[] schematics) =>
        new(
            Parse<List<ItemDef>>(items, "items"),
            Parse<List<EnemyDef>>(enemies, "enemies"),
            Parse<List<ZoneDef>>(zones, "zones"),
            Parse<ScavengeRules>(rules, "scavenge"),
            perks is null ? null : Parse<ProgressionDef>(perks, "perks").Perks
                .ToDictionary(p => p.Id, p => (IReadOnlyDictionary<string, double>)new Dictionary<string, double>(p.Effects.Hooks, StringComparer.Ordinal)),
            cybernetics is null ? null : Parse<CyberneticsDef>(cybernetics, "cybernetics"),
            SchematicCatalogLoader.LoadFromJson(schematics ?? []),
            electronics is null ? null : Parse<ElectronicsDef>(electronics, "electronics"),
            gearMods is null ? null : Parse<GearModsDef>(gearMods, "gear_mods"),
            medicine is null ? null : Parse<MedicineDef>(medicine, "medicine"));

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
