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
    public double ChildPerTick { get; init; } = 0.03;
    public double StarvationDamagePerTick { get; init; } = 1.0;
    public double RecoveryPerTick { get; init; } = 0.25;
}

public sealed record HousingRules
{
    public int HomelessGraceTicks { get; init; } = 72;
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
    public List<string> Buildings { get; init; } = new();
    public List<string> MaleNames { get; init; } = new();
    public List<string> FemaleNames { get; init; } = new();
}
