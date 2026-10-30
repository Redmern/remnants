namespace Remnants.Core.Data;

/// <summary>
/// Food and cooking (data/food.json). Every food is a normal inventory item (items.json kind <c>Food</c>); this file
/// says what each one does when eaten or kept: its food value, nutrition, shelf life, tags for recipe inputs, and for
/// cooked food its mood, health and timed buffs. It also lists what farms, greenhouses and hunting lodges can grow or
/// bring in (<see cref="Harvests"/>), the buffs and their mirror debuffs, and the rules for eating, spoilage, quality,
/// variety, nutrition and expedition rations. See ARCHITECTURE.md "Food and cooking".
/// </summary>
public sealed record FoodDef
{
    public List<FoodItemDef> Items { get; init; } = new();
    public List<HarvestDef> Harvests { get; init; } = new();
    public List<BuffDef> Buffs { get; init; } = new();
    public FoodRulesDef Rules { get; init; } = new();
}

/// <summary>
/// One food item. <see cref="FoodValue"/> is in the old food units (an adult eats 1.2 a day), so food days stay readable.
/// Raw ingredients have no <see cref="Meal"/>; cooked and preserved food does.
/// </summary>
public sealed record FoodItemDef
{
    /// <summary>The items.json id.</summary>
    public string Id { get; init; } = "";

    /// <summary>Need covered by one unit (0: an ingredient that is not eaten, like water or salt).</summary>
    public double FoodValue { get; init; }

    /// <summary>0..1: how good it is for the body. Children's growth reads it (see <see cref="NutritionRules"/>).</summary>
    public double Nutrition { get; init; } = 0.5;

    /// <summary>Days until it spoils at room temperature; 0 never spoils.</summary>
    public double ShelfDays { get; init; }

    /// <summary>Keeps longer in a Cold Cellar (raw produce and meat; not what is already preserved).</summary>
    public bool Cellar { get; init; }

    /// <summary>Can be eaten as it is. Raw meat cannot (only when starving, at <see cref="RawSicknessPerTick"/>).</summary>
    public bool Edible { get; init; } = true;

    /// <summary>Sickness chance per tick of eating it raw while it is not <see cref="Edible"/>.</summary>
    public double RawSicknessPerTick { get; init; }

    /// <summary>Husk-zone food: whatever is made from it may stay tainted unless cooked well (<see cref="QualityRules.TaintKept"/>).</summary>
    public bool Tainted { get; init; }

    /// <summary>Recipe inputs can ask for a tag (<c>tag:meat</c>) instead of an item.</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>Set for cooked and preserved food: what eating it does on top of feeding.</summary>
    public MealDef? Meal { get; init; }
}

/// <summary>What a cooked or preserved food does to whoever eats it. Effects scale with the lot's quality.</summary>
public sealed record MealDef
{
    public double MoodPerTick { get; init; }
    public double HealthPerTick { get; init; }

    /// <summary>A hot meal: eaten between dusk and dawn it counts as a hot supper (<c>dayNight.nightMealMoodPerTick</c>).</summary>
    public bool Hot { get; init; } = true;

    /// <summary>Buff id → hours it lasts at standard quality; eating refreshes it.</summary>
    public Dictionary<string, double> Buffs { get; init; } = new();

    /// <summary>Taken as expedition rations (one per member at least): every member gets this buff for the road.</summary>
    public string? FieldBuff { get; init; }
}

/// <summary>
/// Something a farm, greenhouse or hunting lodge can work on. Workers' job output is the effort; each unit of effort
/// yields <see cref="Yield"/> items split over <see cref="Outputs"/> (whole items as they add up). Nothing comes in for
/// <see cref="GrowDays"/> after it is picked (planting, or setting traps); the effort meanwhile is the standing crop and
/// is harvested when it is ready. Weather terms (<c>Crops.Modifiers</c>) apply when <see cref="Weather"/> is set and the
/// building is outdoors.
/// </summary>
public sealed record HarvestDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Building types that can work it; the first harvest listed for a building is its default.</summary>
    public List<string> Buildings { get; init; } = new();

    /// <summary>Item id → share of the yield (shares add up to 1).</summary>
    public Dictionary<string, double> Outputs { get; init; } = new();
    public double Yield { get; init; } = 1;
    public double GrowDays { get; init; }

    /// <summary>Stockpile resources per item harvested (hides → cloth).</summary>
    public Dictionary<string, double> Byproducts { get; init; } = new();

    /// <summary>Rain, storms and dust change it (outdoor buildings only).</summary>
    public bool Weather { get; init; }

    /// <summary>Building level needed (1: always).</summary>
    public int MinLevel { get; init; } = 1;
}

/// <summary>
/// A timed effect on a survivor: from a meal (Well Fed, Comfort Food, Energised, Soothed) or from going without
/// (Hungry, Bland, Malnourished, Sick, Same Again). Multipliers are 1 when unused.
/// </summary>
public sealed record BuffDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Debuff { get; init; }

    /// <summary>Job output multiplier.</summary>
    public double Output { get; init; } = 1;

    /// <summary>Injury healing multiplier (Med Bay care and natural healing).</summary>
    public double Healing { get; init; } = 1;
    public double MoodPerTick { get; init; }
    public double HealthPerTick { get; init; }

    /// <summary>Sleep debt added when it wears off (a stim crash).</summary>
    public double SleepDebtOnEnd { get; init; }

    /// <summary>In the field: AP added on the first round (negative: taken).</summary>
    public int FieldFirstRoundAp { get; init; }
}

public sealed record FoodRulesDef
{
    /// <summary>Buff ids the food system itself hands out.</summary>
    public FoodBuffIds BuffIds { get; init; } = new();
    public SpoilageRules Spoilage { get; init; } = new();
    public QualityRules Quality { get; init; } = new();
    public VarietyRules Variety { get; init; } = new();
    public NutritionRules Nutrition { get; init; } = new();
    public SicknessRules Sickness { get; init; } = new();
    public RationRules Rations { get; init; } = new();

    /// <summary>Cold Cellar building id and its spoilage rate per level (0.6 = ages at 60% speed).</summary>
    public string CellarBuilding { get; init; } = "cold_cellar";
    public List<double> CellarRateByLevel { get; init; } = new();

    /// <summary>A powered Cold Cellar (electronics-devices power grid) ages food this much slower again.</summary>
    public double PoweredCellarMultiplier { get; init; } = 0.75;

    /// <summary>A building of this type makes hot suppers count for more (<see cref="CampfireSupperMultiplier"/>).</summary>
    public string CampfireBuilding { get; init; } = "campfire";
    public double CampfireSupperMultiplier { get; init; } = 1.5;

    /// <summary>Hours without a cooked meal before Bland sets in.</summary>
    public double BlandAfterHours { get; init; } = 48;

    /// <summary>Hours Hungry lasts after the last starving tick.</summary>
    public double HungryHours { get; init; } = 12;
}

public sealed record FoodBuffIds
{
    public string Hungry { get; init; } = "hungry";
    public string Bland { get; init; } = "bland";
    public string Malnourished { get; init; } = "malnourished";
    public string Sick { get; init; } = "sick";
    public string SameAgain { get; init; } = "same_again";
}

/// <summary>Spoiled food can still be eaten for <see cref="SpoiledDays"/> (only when there is nothing else) and then rots away.</summary>
public sealed record SpoilageRules
{
    public double SpoiledDays { get; init; } = 2;

    /// <summary>Shelf life multiplier of preserved food made by a cook with the Preserver perk hook, per point.</summary>
    public double PerkShelfPerPoint { get; init; } = 1;
}

/// <summary>
/// Cooked food quality: rolled like crafted gear (<c>crafting.qualityThresholds</c> on the cook's Cooking skill,
/// building level and perks), stored on the lot. Index order follows the scavenge quality tiers.
/// </summary>
public sealed record QualityRules
{
    /// <summary>Quality tier id → multiplier on nutrition bonus, mood, health and buff hours.</summary>
    public Dictionary<string, double> Effect { get; init; } = new();

    /// <summary>Quality tier id → chance a dish made with tainted (Husk) food stays tainted.</summary>
    public Dictionary<string, double> TaintKept { get; init; } = new();

    /// <summary>Lowest quality that keeps a meal's field buff.</summary>
    public string FieldBuffMin { get; init; } = "standard";
}

/// <summary>
/// Eating the same dish over and over. Each survivor keeps a fading record of what they ate (half-life
/// <see cref="HalfLifeHours"/>); when one cooked dish is more than <see cref="Share"/> of it and they have eaten at least
/// <see cref="MinFood"/> food of cooked dishes, Same Again applies. They pick something else when they can
/// (<see cref="ChoiceWeight"/>: how much the share counts against a dish when choosing).
/// </summary>
public sealed record VarietyRules
{
    public double HalfLifeHours { get; init; } = 48;
    public double Share { get; init; } = 0.7;
    public double MinFood { get; init; } = 2;
    public double ChoiceWeight { get; init; } = 1;
}

/// <summary>
/// A survivor's nutrition is a running average of what they eat (time constant <see cref="AverageHours"/>), the lot's
/// nutrition plus its quality bonus. Below <see cref="MalnourishedBelow"/>: Malnourished, and a child counts the tick
/// as hungry growth. A growing child's attributes move at <c>lerp(GrowthAtLow, GrowthAtHigh, nutrition)</c> of the base rate.
/// </summary>
public sealed record NutritionRules
{
    public double AverageHours { get; init; } = 48;
    public double MalnourishedBelow { get; init; } = 0.45;
    public double Start { get; init; } = 0.6;
    public double GrowthAtLow { get; init; } = 0.8;
    public double GrowthAtHigh { get; init; } = 1.2;

    /// <summary>Nutrition added per point of quality effect above 1 (fine food is better for you).</summary>
    public double QualityBonus { get; init; } = 0.2;
}

/// <summary>Food poisoning: chance per tick of eating spoiled or tainted food; it lasts <see cref="Hours"/>.</summary>
public sealed record SicknessRules
{
    public double SpoiledPerTick { get; init; } = 0.02;
    public double TaintedPerTick { get; init; } = 0.02;
    public double Hours { get; init; } = 24;

    /// <summary>Expedition rations: chance per member when any spoiled or tainted food is packed, times its share.</summary>
    public double RationChance { get; init; } = 0.5;

    /// <summary>Health a member loses when the rations made them sick before they arrive.</summary>
    public double RationHealthLoss { get; init; } = 10;
}

/// <summary>Packing rations for an expedition.</summary>
public sealed record RationRules
{
    /// <summary>Hours of field buff after arrival.</summary>
    public double FieldBuffHours { get; init; } = 24;
}

/// <summary>
/// The schematics that gate food content (data/schematics.food.json, in the story-schematics catalogue format: a
/// <c>schematics</c> list with <c>unlocks</c> of kind <c>Recipe</c>, <c>Building</c> or <c>BuildingLevel</c>). Read here
/// until that catalogue merges; <c>FoodSchematics</c> is the adapter the rules go through.
/// </summary>
public sealed record FoodSchematicFile
{
    public List<FoodSchematicDef> Schematics { get; init; } = new();
}

public sealed record FoodSchematicDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Tier { get; init; } = 1;
    public List<FoodUnlockDef> Unlocks { get; init; } = new();
    public List<string> Sources { get; init; } = new();
    public string Description { get; init; } = "";
}

public sealed record FoodUnlockDef
{
    /// <summary><c>Recipe</c>, <c>Building</c> (level 1) or <c>BuildingLevel</c>.</summary>
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
    public int Level { get; init; } = 1;
}

/// <summary>Food definitions indexed by id, checked against the colony data.</summary>
public sealed class FoodData
{
    public FoodDef Def { get; }
    public IReadOnlyDictionary<string, FoodItemDef> Items { get; }
    public IReadOnlyDictionary<string, HarvestDef> Harvests { get; }
    public IReadOnlyDictionary<string, BuffDef> Buffs { get; }
    public FoodRulesDef Rules => Def.Rules;
    public FoodSchematicFile Schematics { get; }

    private readonly Dictionary<string, List<HarvestDef>> _harvestsByBuilding;
    private readonly Dictionary<string, string> _recipeGates = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Building, int Level), string> _levelGates = new();

    public FoodData(FoodDef def, FoodSchematicFile? schematics = null)
    {
        Def = def;
        Items = Index(def.Items, i => i.Id, "food item");
        Harvests = Index(def.Harvests, h => h.Id, "harvest");
        Buffs = Index(def.Buffs, b => b.Id, "buff");
        Schematics = schematics ?? new FoodSchematicFile();
        _harvestsByBuilding = new();
        foreach (var h in def.Harvests)
            foreach (var b in h.Buildings)
            {
                if (!_harvestsByBuilding.TryGetValue(b, out var list)) _harvestsByBuilding[b] = list = new();
                list.Add(h);
            }
        foreach (var s in Schematics.Schematics)
            foreach (var u in s.Unlocks)
            {
                var added = u.Kind switch
                {
                    "Recipe" => _recipeGates.TryAdd(u.Id, s.Id),
                    "Building" => _levelGates.TryAdd((u.Id, 1), s.Id),
                    "BuildingLevel" => _levelGates.TryAdd((u.Id, u.Level), s.Id),
                    _ => throw new GameDataException($"Food schematic '{s.Id}': unknown unlock kind '{u.Kind}'."),
                };
                if (!added) throw new GameDataException($"Food schematic '{s.Id}': '{u.Id}' is already gated by another schematic.");
            }
    }

    /// <summary>What a building type can harvest, default first (empty: it does not harvest).</summary>
    public IReadOnlyList<HarvestDef> HarvestsFor(string buildingDefId) =>
        _harvestsByBuilding.TryGetValue(buildingDefId, out var list) ? list : [];

    public bool IsFood(string itemId) => Items.ContainsKey(itemId);

    /// <summary>An item satisfies a recipe input key: its own id, or <c>tag:x</c> when it carries tag x.</summary>
    public bool Matches(string itemId, string key) =>
        key.StartsWith(TagPrefix, StringComparison.Ordinal)
            ? Items.TryGetValue(itemId, out var def) && def.Tags.Contains(key[TagPrefix.Length..])
            : itemId == key;

    public const string TagPrefix = "tag:";

    /// <summary>The schematic a recipe needs, if any.</summary>
    public string? GateOfRecipe(string recipeId) => _recipeGates.GetValueOrDefault(recipeId);

    /// <summary>The schematic a building type at this level needs (level 1 = the building itself), if any.</summary>
    public string? GateOfBuildingLevel(string buildingId, int level) => _levelGates.GetValueOrDefault((buildingId, level));

    public FoodSchematicDef? Schematic(string id) => Schematics.Schematics.Find(s => s.Id == id);

    internal void Validate(GameData data)
    {
        var tags = Items.Values.SelectMany(i => i.Tags).ToHashSet();
        foreach (var i in Items.Values)
        {
            var where = $"Food item '{i.Id}'";
            if (i.FoodValue < 0 || i.ShelfDays < 0 || i.Nutrition is < 0 or > 1 || i.RawSicknessPerTick < 0)
                throw new GameDataException($"{where}: foodValue and shelfDays not negative, nutrition in 0..1.");
            foreach (var b in i.Meal?.Buffs.Keys ?? Enumerable.Empty<string>())
                if (!Buffs.ContainsKey(b)) throw new GameDataException($"{where}: unknown buff '{b}'.");
            if (i.Meal?.FieldBuff is { } fb && !Buffs.ContainsKey(fb)) throw new GameDataException($"{where}: unknown field buff '{fb}'.");
        }
        foreach (var h in Harvests.Values)
        {
            var where = $"Harvest '{h.Id}'";
            if (h.Buildings.Count == 0 || h.Outputs.Count == 0 || h.Yield <= 0 || h.GrowDays < 0)
                throw new GameDataException($"{where} needs buildings, outputs, a positive yield and growDays not negative.");
            if (Math.Abs(h.Outputs.Values.Sum() - 1) > 1e-6) throw new GameDataException($"{where}: output shares must add up to 1.");
            foreach (var b in h.Buildings)
                if (!data.Buildings.TryGetValue(b, out var bd) || bd.Job is null || h.MinLevel > bd.MaxLevel)
                    throw new GameDataException($"{where}: '{b}' is not a building with a job (at level {h.MinLevel}).");
            foreach (var item in h.Outputs.Keys)
                if (!Items.ContainsKey(item)) throw new GameDataException($"{where}: unknown food item '{item}'.");
            foreach (var res in h.Byproducts.Keys)
                if (!data.Resources.ContainsKey(res)) throw new GameDataException($"{where}: unknown resource '{res}'.");
        }
        var ids = Rules.BuffIds;
        foreach (var id in new[] { ids.Hungry, ids.Bland, ids.Malnourished, ids.Sick, ids.SameAgain })
            if (!Buffs.ContainsKey(id)) throw new GameDataException($"Food rules: unknown buff '{id}'.");
        if (!data.Buildings.ContainsKey(Rules.CellarBuilding)) throw new GameDataException($"Food rules: unknown cellar building '{Rules.CellarBuilding}'.");
        if (!data.Buildings.ContainsKey(Rules.CampfireBuilding)) throw new GameDataException($"Food rules: unknown campfire building '{Rules.CampfireBuilding}'.");
        foreach (var r in data.Recipes.Values)
            foreach (var (key, count) in r.Ingredients)
            {
                if (count < 1) throw new GameDataException($"Recipe '{r.Id}': ingredient counts must be at least 1.");
                var known = key.StartsWith(TagPrefix, StringComparison.Ordinal) ? tags.Contains(key[TagPrefix.Length..]) : Items.ContainsKey(key);
                if (!known) throw new GameDataException($"Recipe '{r.Id}': unknown ingredient '{key}'.");
            }
        foreach (var r in _recipeGates.Keys)
            if (!data.Recipes.ContainsKey(r)) throw new GameDataException($"Food schematics: unknown recipe '{r}'.");
        foreach (var (b, level) in _levelGates.Keys)
            if (!data.Buildings.TryGetValue(b, out var bd) || level < 1 || level > bd.MaxLevel)
                throw new GameDataException($"Food schematics: unknown building level '{b}' L{level}.");
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> id, string kind)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = id(item);
            if (string.IsNullOrWhiteSpace(key)) throw new GameDataException($"A {kind} has no id.");
            if (!result.TryAdd(key, item)) throw new GameDataException($"Duplicate {kind} id '{key}'.");
        }
        return result;
    }
}
