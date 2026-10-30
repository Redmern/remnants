namespace Remnants.Core.Survivors;

public sealed class Survivor
{
    public const double MaxHealth = 100.0;
    public const double MaxStat = 100.0;
    public const double MaxMood = 100.0;

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

    /// <summary>On an expedition: no job, does not eat colony food, is not treated. Set by <c>LaunchExpedition</c>.</summary>
    public bool Away { get; set; }

    /// <summary>The Med Bay this survivor is admitted to as a patient, if any. Patients do not work.</summary>
    public int? PatientAtBuildingId { get; set; }

    public bool HasBed { get; set; }
    public int HomelessTicks { get; set; }
    public int StarvingTicks { get; set; }

    /// <summary>Partner in a couple, if any (both sides point at each other).</summary>
    public int? PartnerId { get; set; }

    public int? MotherId { get; set; }
    public int? FatherId { get; set; }

    /// <summary>0 (despair) to 100 (content). Grief lowers it; it recovers over time. Low mood lowers job output.</summary>
    public double Mood { get; set; } = MaxMood;

    /// <summary>The child this survivor is carrying, if any.</summary>
    public Pregnancy? Pregnancy { get; set; }

    /// <summary>Infants and children: ticks grown so far (stage changes at <c>rules.life</c> thresholds).</summary>
    public long GrowthTicks { get; set; }

    /// <summary>Infants and children: ticks they went hungry while growing (they grow up weaker).</summary>
    public long MalnourishedTicks { get; set; }

    /// <summary>
    /// Infants and children: the attribute levels they grow towards (inherited from their parents). Empty for
    /// adults and for children from older saves, who then only change through training.
    /// </summary>
    public Dictionary<AttributeKind, double> Aptitude { get; set; } = new();

    /// <summary>Hours of sleep missed and not yet slept off; tired at <c>dayNight.tired.threshold</c>.</summary>
    public double SleepDebt { get; set; }

    /// <summary>The housing building this survivor sleeps in, while they have a bed.</summary>
    public int? HomeBuildingId { get; set; }

    /// <summary>Experience from work, crafting, study and missions. Level and perk points follow from it.</summary>
    public double Xp { get; set; }

    /// <summary>Level reached (1 at the start); kept in step with <see cref="Xp"/> by <c>Progression.GrantXp</c>.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Perk points earned on level-ups and not yet spent.</summary>
    public int PerkPoints { get; set; }

    /// <summary>Perk ids this survivor has learned (data/perks.json).</summary>
    public SortedSet<string> Perks { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Not an adult yet: an infant or a child. Children stay home from expeditions and eat first.</summary>
    public bool IsChild => LifeStage != LifeStage.Adult;

    public bool IsInfant => LifeStage == LifeStage.Infant;

    public bool IsInjured => Body.Parts.Values.Any(p => p.Condition == PartCondition.Injured);

    public double Get(AttributeKind attribute) => Attributes.GetValueOrDefault(attribute);
    public double Get(Skill skill) => Skills.GetValueOrDefault(skill);

    public void Set(AttributeKind attribute, double value) => Attributes[attribute] = Math.Clamp(value, 0, MaxStat);
    public void Set(Skill skill, double value) => Skills[skill] = Math.Clamp(value, 0, MaxStat);

    public bool HasPerk(string perkId) => Perks.Contains(perkId);

    /// <summary>
    /// Practice or study raises a skill: <paramref name="amount"/> shrinks as the skill nears 100
    /// (amount · (1 − skill/100)). Returns the points actually gained. This is the one entry point for skill growth
    /// (colony training, study, and the scavenge mode's field practice); XP is granted separately by the colony
    /// (<c>Progression.GrantXp</c>), or for field gains when the party comes home.
    /// </summary>
    public double GainSkill(Skill skill, double amount, XpSource source = XpSource.Work)
    {
        _ = source;
        if (amount <= 0) return 0;
        var before = Get(skill);
        Set(skill, before + amount * (1 - before / MaxStat));
        return Get(skill) - before;
    }
}
