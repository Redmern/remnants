namespace Remnants.Core.Data;

/// <summary>Global balancing numbers, loaded from data/rules.json.</summary>
public sealed record RulesDef
{
    public int TicksPerDay { get; init; } = 24;
    public int DaysPerYear { get; init; } = 30;
    public int AdultAge { get; init; } = 16;
    public string TownHallBuildingId { get; init; } = "town_hall";
    public OutputRules Output { get; init; } = new();
    public TrainingRules Training { get; init; } = new();
    public FoodRules Food { get; init; } = new();
    public HousingRules Housing { get; init; } = new();
    public CraftingRules Crafting { get; init; } = new();
    public ExpeditionRules Expedition { get; init; } = new();
    public MedicalRules Medical { get; init; } = new();
    public LifeRules Life { get; init; } = new();
    public RelationshipRules Relationships { get; init; } = new();
    public FamilyRules Family { get; init; } = new();
    public MoodRules Mood { get; init; } = new();
    public DayNightRules DayNight { get; init; } = new();
    public LayoutRules Layout { get; init; } = new();
    public NewGameRules NewGame { get; init; } = new();

    public long TicksPerYear => (long)TicksPerDay * DaysPerYear;
}

public sealed record OutputRules
{
    /// <summary>Performance of a worker with all-zero stats; scales linearly to 1 at all-100.</summary>
    public double PerformanceFloor { get; init; } = 0.25;
    public double ChildOutputMultiplier { get; init; } = 0.5;

    /// <summary>Output multiplier at 0 health; scales linearly to 1 at full health.</summary>
    public double LowHealthMultiplier { get; init; } = 0.5;

    /// <summary>Output multiplier at mood 0; scales linearly to 1 at full mood.</summary>
    public double LowMoodMultiplier { get; init; } = 0.8;
}

public sealed record TrainingRules
{
    public double SkillPerTick { get; init; } = 0.05;
    public double AttributePerTick { get; init; } = 0.01;
    public double ChildMultiplier { get; init; } = 2.0;
}

public sealed record FoodRules
{
    public string ResourceId { get; init; } = "food";
    public double AdultPerTick { get; init; } = 0.05;
    public double ChildPerTick { get; init; } = 0.04;
    public double InfantPerTick { get; init; } = 0.02;
    public double StarvationDamagePerTick { get; init; } = 1.0;
    public double RecoveryPerTick { get; init; } = 0.25;

    /// <summary>Cooked food, best first: eaten before raw food when in stock.</summary>
    public List<MealRules> Meals { get; init; } = new();
}

/// <summary>
/// A Kitchen product. One unit covers <see cref="FoodValue"/> food of someone's need (so cooking stretches food), and
/// each tick someone eats it they gain extra mood and health.
/// </summary>
public sealed record MealRules
{
    public string Resource { get; init; } = "";
    public double FoodValue { get; init; } = 1;
    public double MoodPerTick { get; init; }
    public double HealthPerTick { get; init; }
}

/// <summary>
/// Production queues and the quality roll of crafted gear:
/// <code>
/// score = best worker's job skill / 100 + (level − 1) · qualityPerLevel + uniform(−qualitySpread, +qualitySpread)
/// </code>
/// The result is the highest tier whose threshold the score reaches; below every threshold, the lowest tier.
/// </summary>
public sealed record CraftingRules
{
    /// <summary>Orders a building's queue may hold.</summary>
    public int MaxQueue { get; init; } = 8;

    /// <summary>Largest batch count of one order.</summary>
    public int MaxBatches { get; init; } = 20;
    public double QualitySpread { get; init; } = 0.2;
    public double QualityPerLevel { get; init; } = 0.05;

    /// <summary>Quality tier id → minimum score (tier ids from <c>scavenge.json</c> quality).</summary>
    public Dictionary<string, double> QualityThresholds { get; init; } = new();
}

public sealed record HousingRules
{
    /// <summary>Ticks a survivor may spend without a bed; they leave on the tick after.</summary>
    public int HomelessGraceTicks { get; init; } = 72;
}

public sealed record ExpeditionRules
{
    /// <summary>Party size cap at Town Hall level 1, 2, ...; levels past the end use the last entry.</summary>
    public List<int> PartySizeByTownHallLevel { get; init; } = [2, 3, 4];

    /// <summary>Food each member takes from the stockpile at launch. Away survivors do not eat colony food.</summary>
    public double RationFoodPerMember { get; init; } = 3;

    /// <summary>Extra rations per member per travel day, both ways (zones farther out cost more food).</summary>
    public double RationFoodPerMemberPerDay { get; init; } = 2;

    /// <summary>Age in years given to recruits (who arrive without a known age).</summary>
    public int[] RecruitAgeRange { get; init; } = [18, 40];
}

/// <summary>
/// Injury care. An admitted patient's injured parts lose severity each tick:
/// <code>
/// care       = unstaffedCare + Σ medic performance · health · life       (medicine job)
/// heal/tick  = healPerTick · level.outputMultiplier · care                (needs medicinePerPatientTick)
/// </code>
/// Without medicine an admitted patient only heals at the natural rate. Outside the Med Bay injuries up to
/// <see cref="NaturalHealMaxSeverity"/> heal naturally, and those at or above <see cref="WorsenThreshold"/> worsen:
/// a limb that reaches severity 1 is lost; head/torso injuries drain health.
/// </summary>
public sealed record MedicalRules
{
    public string BuildingId { get; init; } = "med_bay";
    public string MedicineResourceId { get; init; } = "medicine";
    public double HealPerTick { get; init; } = 0.01;
    public double UnstaffedCare { get; init; } = 0.25;
    public double MedicinePerPatientTick { get; init; } = 0.02;
    public double PatientHealthPerTick { get; init; } = 0.5;
    public double NaturalHealPerTick { get; init; } = 0.002;
    public double NaturalHealMaxSeverity { get; init; } = 0.3;
    public double WorsenThreshold { get; init; } = 0.6;
    public double WorsenPerTick { get; init; } = 0.002;
    public double SevereHealthDrainPerTick { get; init; } = 0.5;
}

/// <summary>
/// Growing up. The time scale is for fun, not realism: an infant becomes a child after <see cref="InfantTicks"/>
/// and an adult <see cref="ChildTicks"/> later. While growing, each fed tick moves every attribute
/// <see cref="AttributeGrowthPerTick"/> of the way to the inherited aptitude; hungry ticks do not, so malnourished
/// children grow up weaker. A minor's age in years is shown as their share of <c>adultAge</c>.
/// </summary>
public sealed record LifeRules
{
    public int InfantTicks { get; init; } = 120;
    public int ChildTicks { get; init; } = 336;
    public double AttributeGrowthPerTick { get; init; } = 0.006;

    /// <summary>A newborn's attributes as a fraction of their aptitude.</summary>
    public double BirthAttributeFraction { get; init; } = 0.2;

    /// <summary>Aptitude = mean of the parents' attributes ± up to this much (uniform).</summary>
    public double InheritVariation { get; init; } = 12;

    /// <summary>A coming-of-age note says "malnourished" when at least this share of growing up was hungry.</summary>
    public double MalnourishedNoteShare { get; init; } = 0.2;

    public int GrowthTicks => InfantTicks + ChildTicks;
}

/// <summary>
/// Affinity between two adults at home who are both single grows every tick:
/// <code>
/// gain = (base + sharedWorkplace? + sharedHome? + compatibility · similarity) · (encouraged ? encourageMultiplier : 1)
/// similarity = 1 − mean |attribute difference| / 100
/// </code>
/// A discouraged pair gains nothing. At <see cref="CoupleThreshold"/> they become a couple.
/// </summary>
public sealed record RelationshipRules
{
    public double BasePerTick { get; init; } = 0.05;
    public double SharedWorkplacePerTick { get; init; } = 0.15;
    public double SharedHomePerTick { get; init; } = 0.05;
    public double CompatibilityPerTick { get; init; } = 0.1;
    public double EncourageMultiplier { get; init; } = 3;
    public double CoupleThreshold { get; init; } = 100;
}

/// <summary>
/// Every <see cref="ConceptionCheckTicks"/> a woman and man in a couple, both at home, may conceive with
/// <see cref="ConceptionChance"/> when the colony has a bed free for the baby (beds &gt; people + pregnancies), at
/// least <see cref="MinFoodPerPerson"/> food per person in stock, and she has no infant already.
/// </summary>
public sealed record FamilyRules
{
    public int ConceptionCheckTicks { get; init; } = 24;
    public double ConceptionChance { get; init; } = 0.35;
    public double MinFoodPerPerson { get; init; } = 2;
    public int PregnancyTicks { get; init; } = 96;
    public List<string> MaleNames { get; init; } = new();
    public List<string> FemaleNames { get; init; } = new();
}

/// <summary>Mood drops when someone close dies or leaves and recovers by <see cref="RecoveryPerTick"/>.</summary>
public sealed record MoodRules
{
    public double PartnerGrief { get; init; } = 50;
    public double FamilyGrief { get; init; } = 25;
    public double Heartbreak { get; init; } = 20;
    public double RecoveryPerTick { get; init; } = 0.15;
}

/// <summary>
/// The colony's day: phases by hour, who works and sleeps when, the Night Shift and sleep debt. An hour is a tick
/// (<c>hour = tick % ticksPerDay</c>). Hour windows are [start, end) and may wrap past midnight. Off by default (everyone
/// works every tick, nobody sleeps), so hand-built rules in tests keep the old per-tick math; data/rules.json turns it on.
/// <code>
/// on day shift     : hour in workHours (children: also outside childSleep)      → output × 1
/// on night shift   : building.nightShift, adult, lit or indoor, outside workHours → output × nightShift.outputMultiplier
/// tired            : sleepDebt ≥ tired.threshold                                  → output × tired.outputMultiplier
/// </code>
/// </summary>
public sealed record DayNightRules
{
    public bool Enabled { get; init; }

    /// <summary>Hour each phase starts: dawn, day, dusk, night (in that order through the day).</summary>
    public int DawnHour { get; init; } = 5;
    public int DayHour { get; init; } = 7;
    public int DuskHour { get; init; } = 18;
    public int NightHour { get; init; } = 20;

    /// <summary>The day shift [start, end) for adults; children work it too, minus their earlier bedtime.</summary>
    public int[] WorkHours { get; init; } = [6, 20];
    public int[] AdultSleep { get; init; } = [22, 6];
    public int[] ChildSleep { get; init; } = [19, 7];

    /// <summary>Asleep in a bed (or a Med Bay cot): mood and health gained per tick.</summary>
    public double SleepMoodPerTick { get; init; } = 0.2;
    public double SleepHealthPerTick { get; init; } = 0.3;

    /// <summary>Awake and off work from dusk until bedtime: an evening at the campfire.</summary>
    public double CampfireMoodPerTick { get; init; } = 0.15;

    /// <summary>Extra mood for eating a cooked meal from dusk until dawn (a hot supper).</summary>
    public double NightMealMoodPerTick { get; init; } = 0.1;

    public NightShiftRules NightShift { get; init; } = new();
    public TiredRules Tired { get; init; } = new();

    /// <summary>Outdoor buildings at this level or above are lit (lamps on the bigger site) and may run a Night Shift.</summary>
    public int LitMinLevel { get; init; } = 2;

    /// <summary>A Floodlight lights a level-1 outdoor building.</summary>
    public Dictionary<string, double> FloodlightCost { get; init; } = new();
}

public sealed record NightShiftRules
{
    public double OutputMultiplier { get; init; } = 0.7;

    /// <summary>Mood each tick of night work costs (on top of the lost sleep).</summary>
    public double MoodPerTick { get; init; } = 0.5;
}

/// <summary>
/// Sleep debt: each tick of a survivor's sleep window spent working adds <see cref="DebtPerMissedTick"/>; each tick
/// asleep pays back <see cref="RecoveryPerSleepTick"/>. At <see cref="Threshold"/> or more they are tired.
/// </summary>
public sealed record TiredRules
{
    public double DebtPerMissedTick { get; init; } = 1;
    public double RecoveryPerSleepTick { get; init; } = 1.5;
    public double Threshold { get; init; } = 4;
    public double MaxDebt { get; init; } = 24;
    public double MoodPerTick { get; init; } = 0.1;
    public double OutputMultiplier { get; init; } = 0.85;
}

/// <summary>The town is a grid of building plots; a plot index is row-major (plot = row · columns + column).</summary>
public sealed record LayoutRules
{
    public int PlotColumns { get; init; } = 6;
    public int PlotRows { get; init; } = 4;

    public int PlotCount => PlotColumns * PlotRows;
}

public sealed record NewGameRules
{
    public int Adults { get; init; } = 3;
    public int Children { get; init; } = 2;

    /// <summary>Hour of day 1 the game starts at (the first tick).</summary>
    public int StartHour { get; init; }
    public int[] AdultAgeRange { get; init; } = [20, 45];
    public int[] ChildAgeRange { get; init; } = [6, 12];
    public int[] AttributeRange { get; init; } = [25, 60];
    public int[] ChildAttributeRange { get; init; } = [10, 35];
    public Dictionary<string, double> Stock { get; init; } = new();

    /// <summary>Starting colony inventory: item id → count (item ids are checked against items.json).</summary>
    public Dictionary<string, int> Items { get; init; } = new();
    public List<string> Buildings { get; init; } = new();
    public List<string> MaleNames { get; init; } = new();
    public List<string> FemaleNames { get; init; } = new();
}
