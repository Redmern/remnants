namespace Remnants.Core.Data;

public sealed class GameDataException(string message) : Exception(message);

/// <summary>All static definitions, indexed by id. Immutable after load.</summary>
public sealed class GameData
{
    public IReadOnlyDictionary<string, ResourceDef> Resources { get; }
    public IReadOnlyDictionary<string, JobDef> Jobs { get; }
    public IReadOnlyDictionary<string, BuildingDef> Buildings { get; }
    public RulesDef Rules { get; }

    public GameData(
        IEnumerable<ResourceDef> resources,
        IEnumerable<JobDef> jobs,
        IEnumerable<BuildingDef> buildings,
        RulesDef rules)
    {
        Resources = Index(resources, r => r.Id, "resource");
        Jobs = Index(jobs, j => j.Id, "job");
        Buildings = Index(buildings, b => b.Id, "building");
        Rules = rules;
        Validate();
    }

    public BuildingDef TownHall => Buildings[Rules.TownHallBuildingId];

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
            foreach (var r in b.Recipes)
            {
                if (string.IsNullOrWhiteSpace(r.Id)) throw new GameDataException($"Building '{b.Id}' has a recipe without an id.");
                if (b.Job is null) throw new GameDataException($"Building '{b.Id}' has recipes but no job to work them.");
                if ((r.Item is null) == (r.Resource is null))
                    throw new GameDataException($"Building '{b.Id}' recipe '{r.Id}' must yield exactly one of item or resource.");
                if (r.Resource is not null) RequireResource(r.Resource, $"Building '{b.Id}' recipe '{r.Id}'");
                foreach (var res in r.Inputs.Keys) RequireResource(res, $"Building '{b.Id}' recipe '{r.Id}' input");
                if (r.Work <= 0 || r.Amount <= 0 || r.MinLevel < 1)
                    throw new GameDataException($"Building '{b.Id}' recipe '{r.Id}' needs positive work and amount and minLevel >= 1.");
            }
            if (b.Recipes.Select(r => r.Id).Distinct().Count() != b.Recipes.Count)
                throw new GameDataException($"Building '{b.Id}' has duplicate recipe ids.");
            for (var i = 0; i < b.Levels.Count; i++)
                foreach (var (res, amount) in b.Levels[i].Cost)
                {
                    RequireResource(res, $"Building '{b.Id}' level {i + 1} cost");
                    if (amount < 0) throw new GameDataException($"Building '{b.Id}' level {i + 1}: negative cost of '{res}'.");
                }
        }

        if (!Buildings.ContainsKey(Rules.TownHallBuildingId))
            throw new GameDataException($"Rules: unknown town hall building '{Rules.TownHallBuildingId}'.");
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

    private static void RequireRange(int[] range, string name)
    {
        if (range.Length != 2 || range[0] > range[1])
            throw new GameDataException($"Rules.{(name.Contains('.') ? name : "newGame." + name)} must be [min, max] with min <= max.");
    }
}
