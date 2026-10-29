namespace Remnants.Core.Survivors;

public sealed class Survivor
{
    public const double MaxHealth = 100.0;
    public const double MaxStat = 100.0;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Sex Sex { get; set; }

    /// <summary>Age in sim ticks; years and life stage derive from it plus the rules.</summary>
    public long AgeTicks { get; set; }

    public LifeStage LifeStage { get; set; } = LifeStage.Adult;

    public Dictionary<AttributeKind, double> Attributes { get; set; } =
        Enum.GetValues<AttributeKind>().ToDictionary(a => a, _ => 0.0);

    public Dictionary<Skill, double> Skills { get; set; } =
        Enum.GetValues<Skill>().ToDictionary(s => s, _ => 0.0);

    public Body Body { get; set; } = new();

    public double Health { get; set; } = MaxHealth;

    /// <summary>Tool quality 0 (bare hands) to 1 (best tool). Items come later.</summary>
    public double ToolQuality { get; set; }

    /// <summary>The building this survivor works at, if any. Single source of truth for assignment.</summary>
    public int? AssignedBuildingId { get; set; }

    public bool HasBed { get; set; }
    public int HomelessTicks { get; set; }
    public int StarvingTicks { get; set; }

    public bool IsChild => LifeStage == LifeStage.Child;

    public double Get(AttributeKind attribute) => Attributes.GetValueOrDefault(attribute);
    public double Get(Skill skill) => Skills.GetValueOrDefault(skill);

    public void Set(AttributeKind attribute, double value) => Attributes[attribute] = Math.Clamp(value, 0, MaxStat);
    public void Set(Skill skill, double value) => Skills[skill] = Math.Clamp(value, 0, MaxStat);
}
