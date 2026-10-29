namespace Remnants.Core.Data;

public sealed class GameDataException(string message) : Exception(message);

/// <summary>All static definitions, indexed by id. Immutable after load.</summary>
public sealed class GameData
{
    public IReadOnlyDictionary<string, ResourceDef> Resources { get; }
    public IReadOnlyDictionary<string, JobDef> Jobs { get; }
    public IReadOnlyDictionary<string, BuildingDef> Buildings { get; }
    public IReadOnlyDictionary<string, RecipeDef> Recipes { get; }
    public RulesDef Rules { get; }

    private readonly Dictionary<string, List<RecipeDef>> _recipesByBuilding;

    public GameData(
        IEnumerable<ResourceDef> resources,
        IEnumerable<JobDef> jobs,
        IEnumerable<BuildingDef> buildings,
        RulesDef rules,
        IEnumerable<RecipeDef>? recipes = null)
    {
        Resources = Index(resources, r => r.Id, "resource");
        Jobs = Index(jobs, j => j.Id, "job");
        Buildings = Index(buildings, b => b.Id, "building");
        var recipeList = (recipes ?? []).ToList();
        Recipes = Index(recipeList, r => r.Id, "recipe");
        _recipesByBuilding = recipeList.GroupBy(r => r.Building).ToDictionary(g => g.Key, g => g.ToList());
        Rules = rules;
        Validate();
    }

    public BuildingDef TownHall => Buildings[Rules.TownHallBuildingId];

    /// <summary>The recipes a building type makes, in data order.</summary>
    public IReadOnlyList<RecipeDef> RecipesFor(string buildingDefId) =>
        _recipesByBuilding.TryGetValue(buildingDefId, out var list) ? list : [];

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
        void RequireResource(string id, string where)
        {
            if (!Resources.ContainsKey(id)) throw new GameDataException($"{where}: unknown resource '{id}'.");
        }

        foreach (var b in Buildings.Values)
        {
            if (b.Levels.Count == 0) throw new GameDataException($"Building '{b.Id}' has no levels.");
            if (b.Job is not null && !Jobs.ContainsKey(b.Job))
                throw new GameDataException($"Building '{b.Id}': unknown job '{b.Job}'.");
            if (b.Produces is not null) RequireResource(b.Produces, $"Building '{b.Id}'");
            if (b.MinTownHallLevel < 0 || b.MinTownHallLevel > TownHallMaxLevel())
                throw new GameDataException($"Building '{b.Id}': minTownHallLevel {b.MinTownHallLevel} is out of range.");
            for (var i = 0; i < b.Levels.Count; i++)
                foreach (var (res, amount) in b.Levels[i].Cost)
                {
                    RequireResource(res, $"Building '{b.Id}' level {i + 1} cost");
                    if (amount < 0) throw new GameDataException($"Building '{b.Id}' level {i + 1}: negative cost of '{res}'.");
                }
        }

        if (!Buildings.ContainsKey(Rules.TownHallBuildingId))
            throw new GameDataException($"Rules: unknown town hall building '{Rules.TownHallBuildingId}'.");
        foreach (var r in Recipes.Values)
        {
            var where = $"Recipe '{r.Id}'";
            if (!Buildings.TryGetValue(r.Building, out var b)) throw new GameDataException($"{where}: unknown building '{r.Building}'.");
            if (b.Job is null) throw new GameDataException($"{where}: building '{b.Id}' has no job to work it.");
            if ((r.Item is null) == (r.Resource is null))
                throw new GameDataException($"{where} must yield exactly one of item or resource.");
            if (r.Resource is not null) RequireResource(r.Resource, where);
            foreach (var res in r.Inputs.Keys) RequireResource(res, $"{where} input");
            if (r.Work <= 0 || r.Amount <= 0 || r.MinLevel < 1 || r.MinLevel > b.MaxLevel)
                throw new GameDataException($"{where} needs positive work and amount and a minLevel the building can reach.");
            if (r.Item is not null && r.Amount != Math.Round(r.Amount))
                throw new GameDataException($"{where} yields items, so its amount must be whole.");
            if (r.Inputs.Values.Any(v => v < 0) || r.ItemInputs.Values.Any(v => v < 1) || r.MinSkill < 0)
                throw new GameDataException($"{where}: inputs must be positive and minSkill not negative.");
        }
        var c = Rules.Crafting;
        if (c.MaxQueue < 1 || c.MaxBatches < 1 || c.QualitySpread < 0)
            throw new GameDataException("Rules.crafting needs maxQueue and maxBatches of at least 1 and a non-negative qualitySpread.");
        foreach (var meal in Rules.Food.Meals)
        {
            RequireResource(meal.Resource, "Rules.food.meals");
            if (meal.FoodValue <= 0) throw new GameDataException($"Rules.food.meals '{meal.Resource}' needs a positive foodValue.");
        }
        RequireResource(Rules.Food.ResourceId, "Rules.food");
        foreach (var res in Rules.NewGame.Stock.Keys) RequireResource(res, "Rules.newGame.stock");
        foreach (var b in Rules.NewGame.Buildings)
            if (!Buildings.ContainsKey(b)) throw new GameDataException($"Rules.newGame: unknown building '{b}'.");
        var ng = Rules.NewGame;
        if (Rules.Layout.PlotColumns < 1 || Rules.Layout.PlotRows < 1)
            throw new GameDataException("Rules.layout needs at least one plot column and row.");
        if (ng.Buildings.Count > Rules.Layout.PlotCount)
            throw new GameDataException("Rules.newGame.buildings do not fit on the plots in rules.layout.");
        if (ng.MaleNames.Count == 0 || ng.FemaleNames.Count == 0)
            throw new GameDataException("Rules.newGame needs male and female names.");
        if (ng.Adults < 2) throw new GameDataException("Rules.newGame.adults must be at least 2 (one man, one woman).");
        RequireRange(ng.AdultAgeRange, "adultAgeRange");
        RequireRange(ng.ChildAgeRange, "childAgeRange");
        RequireRange(ng.AttributeRange, "attributeRange");
        RequireRange(ng.ChildAttributeRange, "childAttributeRange");
        if (ng.ChildAgeRange[1] >= Rules.AdultAge)
            throw new GameDataException("Rules.newGame.childAgeRange must stay below adultAge.");

        RequireResource(Rules.Medical.MedicineResourceId, "Rules.medical");
        if (!Buildings.ContainsKey(Rules.Medical.BuildingId))
            throw new GameDataException($"Rules.medical: unknown building '{Rules.Medical.BuildingId}'.");
        if (Rules.Expedition.PartySizeByTownHallLevel.Count == 0 || Rules.Expedition.PartySizeByTownHallLevel.Any(n => n < 1))
            throw new GameDataException("Rules.expedition.partySizeByTownHallLevel needs at least one entry, each at least 1.");
        if (Rules.Expedition.RationFoodPerMember < 0) throw new GameDataException("Rules.expedition.rationFoodPerMember must not be negative.");
        RequireRange(Rules.Expedition.RecruitAgeRange, "expedition.recruitAgeRange");

        var life = Rules.Life;
        if (life.InfantTicks < 1 || life.ChildTicks < 1) throw new GameDataException("Rules.life needs infantTicks and childTicks of at least 1.");
        if (life.AttributeGrowthPerTick is < 0 or > 1 || life.BirthAttributeFraction is < 0 or > 1 || life.InheritVariation < 0)
            throw new GameDataException("Rules.life: attributeGrowthPerTick and birthAttributeFraction must be in 0..1, inheritVariation not negative.");
        if (Rules.Relationships.CoupleThreshold <= 0) throw new GameDataException("Rules.relationships.coupleThreshold must be positive.");
        var family = Rules.Family;
        if (family.ConceptionCheckTicks < 1 || family.PregnancyTicks < 1)
            throw new GameDataException("Rules.family needs conceptionCheckTicks and pregnancyTicks of at least 1.");
        if (family.ConceptionChance is < 0 or > 1) throw new GameDataException("Rules.family.conceptionChance must be in 0..1.");
        if (family.MaleNames.Count == 0 || family.FemaleNames.Count == 0)
            throw new GameDataException("Rules.family needs male and female names for newborns.");
    }

    private int TownHallMaxLevel() => Buildings.TryGetValue(Rules.TownHallBuildingId, out var th) ? th.MaxLevel : 0;

    private static void RequireRange(int[] range, string name)
    {
        if (range.Length != 2 || range[0] > range[1])
            throw new GameDataException($"Rules.{(name.Contains('.') ? name : "newGame." + name)} must be [min, max] with min <= max.");
    }
}
