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

    /// <summary>Skills, XP, study and the perk tree (data/perks.json).</summary>
    public ProgressionDef Progression { get; }

    public IReadOnlyDictionary<string, PerkDef> Perks { get; }
    public IReadOnlyDictionary<string, DisciplineDef> Disciplines { get; }

    /// <summary>The cybernetics tree (data/cybernetics.json); null when not loaded (no tree, no surgery, no humanity effects).</summary>
    public CyberneticsData? Cybernetics { get; }

    /// <summary>Weather at home and over the world (data/weather.json); disabled when absent.</summary>
    public WeatherRules Weather { get; }

    private readonly Dictionary<string, List<RecipeDef>> _recipesByBuilding;

    public GameData(
        IEnumerable<ResourceDef> resources,
        IEnumerable<JobDef> jobs,
        IEnumerable<BuildingDef> buildings,
        RulesDef rules,
        IEnumerable<RecipeDef>? recipes = null,
        ProgressionDef? progression = null,
        CyberneticsDef? cybernetics = null,
        WeatherRules? weather = null)
    {
        Resources = Index(resources, r => r.Id, "resource");
        Jobs = Index(jobs, j => j.Id, "job");
        Buildings = Index(buildings, b => b.Id, "building");
        var recipeList = (recipes ?? []).ToList();
        Recipes = Index(recipeList, r => r.Id, "recipe");
        _recipesByBuilding = recipeList.GroupBy(r => r.Building).ToDictionary(g => g.Key, g => g.ToList());
        Rules = rules;
        Progression = progression ?? new ProgressionDef();
        Perks = Index(Progression.Perks, p => p.Id, "perk");
        Disciplines = Index(Progression.Disciplines, d => d.Id, "discipline");
        Cybernetics = cybernetics is null ? null : new CyberneticsData(cybernetics);
        Weather = weather ?? new WeatherRules();
        Validate();
        ValidateWeather();
        ValidateProgression();
        ValidateCybernetics();
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

        var day = Rules.DayNight;
        var tpd = Rules.TicksPerDay;
        if (!(0 <= day.DawnHour && day.DawnHour < day.DayHour && day.DayHour < day.DuskHour && day.DuskHour < day.NightHour && day.NightHour < tpd))
            throw new GameDataException("Rules.dayNight: phase hours must run dawn < day < dusk < night within the day.");
        foreach (var (name, w) in new[] { ("workHours", day.WorkHours), ("adultSleep", day.AdultSleep), ("childSleep", day.ChildSleep) })
            if (w.Length != 2 || w.Any(h => h < 0 || h >= tpd) || w[0] == w[1])
                throw new GameDataException($"Rules.dayNight.{name}: [start, end) hours within the day.");
        if (Rules.NewGame.StartHour < 0 || Rules.NewGame.StartHour >= tpd)
            throw new GameDataException("Rules.newGame.startHour must be an hour of the day.");
        foreach (var res in day.FloodlightCost.Keys) RequireResource(res, "Rules.dayNight.floodlightCost");

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

    /// <summary>A skill's display name and learning attributes (a bare definition when perks.json does not list it).</summary>
    public SkillDef SkillInfo(Survivors.Skill skill) =>
        Progression.Skills.Find(d => d.Skill == skill) ?? new SkillDef { Skill = skill, Name = skill.ToString() };

    /// <summary>The perks of a discipline, by tier then data order.</summary>
    public IEnumerable<PerkDef> PerksOf(string disciplineId) =>
        Progression.Perks.Where(p => p.Discipline == disciplineId).OrderBy(p => p.Tier);

    private void ValidateProgression()
    {
        var p = Progression;
        if (p.Skills.GroupBy(d => d.Skill).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new GameDataException($"Perks: skill {twice.Key} is listed twice.");
        var xp = p.Xp;
        if (xp.PerLevel <= 0 || xp.PerLevelStep < 0 || xp.MaxLevel < 1 || xp.PointsPerLevel < 0)
            throw new GameDataException("Perks.xp needs a positive perLevel, non-negative perLevelStep and pointsPerLevel, and maxLevel of at least 1.");
        var study = p.Study;
        if (study.SkillPerTick < 0 || study.AptitudeFloor is < 0 or > 1 || study.MentorMultiplier < 1 || study.MaxStudentsPerMentor < 1 || study.PerkWorkPerTier <= 0)
            throw new GameDataException("Perks.study: skillPerTick ≥ 0, aptitudeFloor in 0..1, mentorMultiplier ≥ 1, maxStudentsPerMentor ≥ 1, perkWorkPerTier > 0.");
        foreach (var (building, rules) in study.Buildings)
        {
            if (!Buildings.ContainsKey(building)) throw new GameDataException($"Perks.study: unknown building '{building}'.");
            if (rules.Slots.Count == 0 || rules.Slots.Any(n => n < 0)) throw new GameDataException($"Perks.study '{building}' needs slots per level.");
        }
        foreach (var d in Disciplines.Values)
            if (!study.Buildings.ContainsKey(d.StudyAt))
                throw new GameDataException($"Discipline '{d.Id}': studyAt '{d.StudyAt}' is not a study building.");
        foreach (var perk in Perks.Values)
        {
            var where = $"Perk '{perk.Id}'";
            if (!Disciplines.ContainsKey(perk.Discipline)) throw new GameDataException($"{where}: unknown discipline '{perk.Discipline}'.");
            if (perk.Tier < 1 || perk.Cost < 0) throw new GameDataException($"{where}: tier must be at least 1 and cost not negative.");
            foreach (var other in perk.Requires.Perks)
                if (!Perks.ContainsKey(other) || other == perk.Id) throw new GameDataException($"{where}: requires unknown perk '{other}'.");
            if (perk.Requires.Building is { } b && (!Buildings.TryGetValue(b, out var bd) || perk.Requires.BuildingLevel < 1 || perk.Requires.BuildingLevel > bd.MaxLevel))
                throw new GameDataException($"{where}: requires building '{b}' at a level it has.");
            if (perk.Requires.Level < 1 || perk.Requires.Level > xp.MaxLevel) throw new GameDataException($"{where}: required level out of range.");
            foreach (var job in perk.Effects.JobOutput.Keys.Concat(perk.Effects.CraftQuality.Keys))
                if (!Jobs.ContainsKey(job)) throw new GameDataException($"{where}: effect names unknown job '{job}'.");
            foreach (var hook in perk.Effects.Hooks.Keys)
                if (!Colony.PerkHooks.All.ContainsKey(hook)) throw new GameDataException($"{where}: unknown hook '{hook}' (see PerkHooks).");
        }
        // Prerequisite chains must end: no perk can (indirectly) require itself.
        foreach (var perk in Perks.Values)
        {
            var seen = new HashSet<string>();
            var open = new Stack<string>(perk.Requires.Perks);
            while (open.TryPop(out var id))
            {
                if (id == perk.Id) throw new GameDataException($"Perk '{perk.Id}' requires itself through other perks.");
                if (seen.Add(id)) foreach (var next in Perks[id].Requires.Perks) open.Push(next);
            }
        }
        foreach (var job in Jobs.Values)
            if (job.Trains.Keys.Contains(job.Skill) || job.Trains.Values.Any(v => v < 0))
                throw new GameDataException($"Job '{job.Id}': trains lists other skills, with non-negative shares.");
    }

    /// <summary>Node perks, effect jobs and hooks, resources a service takes (items, blueprints and recipes: <c>ColonySim.ValidateLinks</c>).</summary>
    private void ValidateCybernetics()
    {
        if (Cybernetics is not { } cyber) return;
        void Effects(CyberEffectsDef e, string where)
        {
            foreach (var job in e.JobOutput.Keys.Concat(e.CraftQuality.Keys))
                if (!Jobs.ContainsKey(job)) throw new GameDataException($"{where}: effect names unknown job '{job}'.");
            foreach (var hook in e.Hooks.Keys)
                if (!Colony.PerkHooks.All.ContainsKey(hook)) throw new GameDataException($"{where}: unknown hook '{hook}' (see PerkHooks).");
        }
        foreach (var n in cyber.Nodes.Values)
        {
            if (n.Perk is { } perk && !Perks.ContainsKey(perk)) throw new GameDataException($"Cybernetics node '{n.Id}': unknown perk '{perk}'.");
            Effects(n.Effects, $"Cybernetics node '{n.Id}'");
        }
        foreach (var set in cyber.Def.Sets) Effects(set.Effects, $"Cybernetics set '{set.Id}'");
        foreach (var tier in cyber.Def.Tiers)
            foreach (var res in tier.ServiceCost.Keys)
                if (!Resources.ContainsKey(res)) throw new GameDataException($"Cybernetics tier {tier.Tier}: unknown resource '{res}'.");
    }

    private int TownHallMaxLevel() => Buildings.TryGetValue(Rules.TownHallBuildingId, out var th) ? th.MaxLevel : 0;

    private static void RequireRange(int[] range, string name)
    {
        if (range.Length != 2 || range[0] > range[1])
            throw new GameDataException($"Rules.{(name.Contains('.') ? name : "newGame." + name)} must be [min, max] with min <= max.");
    }

    private void ValidateWeather()
    {
        var w = Weather;
        if (!w.Enabled) return;
        if (!w.Kinds.ContainsKey(w.Start)) throw new GameDataException($"weather.start: unknown kind '{w.Start}'.");
        foreach (var (id, kind) in w.Kinds)
        {
            if (kind.Hours.Length != 2 || kind.Hours[0] < 1 || kind.Hours[1] < kind.Hours[0])
                throw new GameDataException($"weather.kinds.{id}.hours: [min, max] with 1 ≤ min ≤ max.");
            if (kind.Intensity.Length != 2 || kind.Intensity[0] < 0 || kind.Intensity[1] > 1 || kind.Intensity[1] < kind.Intensity[0])
                throw new GameDataException($"weather.kinds.{id}.intensity: [min, max] within 0..1.");
            if (kind.ConfusedWith is { } c && !w.Kinds.ContainsKey(c))
                throw new GameDataException($"weather.kinds.{id}.confusedWith: unknown kind '{c}'.");
            foreach (var job in kind.Colony.JobOutput.Keys)
                if (!Jobs.ContainsKey(job)) throw new GameDataException($"weather.kinds.{id}.colony.jobOutput: unknown job '{job}'.");
            if (!w.Next.TryGetValue(id, out var next) || next.Count == 0 || next.Values.Sum() <= 0)
                throw new GameDataException($"weather.next.{id}: every kind needs somewhere to go.");
        }
        foreach (var (from, next) in w.Next)
        {
            if (!w.Kinds.ContainsKey(from)) throw new GameDataException($"weather.next: unknown kind '{from}'.");
            foreach (var (to, weight) in next)
                if (!w.Kinds.ContainsKey(to) || to == from || weight < 0)
                    throw new GameDataException($"weather.next.{from}.{to}: a known other kind with a weight ≥ 0.");
        }
        foreach (var (region, mults) in w.Regions)
            foreach (var (kind, m) in mults)
                if (!w.Kinds.ContainsKey(kind) || m < 0) throw new GameDataException($"weather.regions.{region}.{kind}: a known kind with a multiplier ≥ 0.");
        if (w.Forecast.DaysByTownHallLevel.Count == 0) throw new GameDataException("weather.forecast.daysByTownHallLevel is empty.");
        if (w.Field.RoundsPerHour < 1) throw new GameDataException("weather.field.roundsPerHour must be at least 1.");
        foreach (var res in w.Repair.Cost.Keys)
            if (!Resources.ContainsKey(res)) throw new GameDataException($"weather.repair.cost: unknown resource '{res}'.");
    }
}
