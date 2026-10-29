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
    public ExpeditionRules Expedition { get; init; } = new();
    public MedicalRules Medical { get; init; } = new();
    public LifeRules Life { get; init; } = new();
    public RelationshipRules Relationships { get; init; } = new();
    public FamilyRules Family { get; init; } = new();
    public MoodRules Mood { get; init; } = new();
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
