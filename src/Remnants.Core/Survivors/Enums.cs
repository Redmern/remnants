namespace Remnants.Core.Survivors;

public enum Sex { Male, Female }

/// <summary>Infants grow into children and children into adults (see <c>rules.life</c>).</summary>
public enum LifeStage { Infant, Child, Adult }

public enum AttributeKind { Strength, Agility, Endurance, Intellect, Creativity, Perception }

public enum Skill
{
    Woodcutting, Mining, Crafting, Tailoring, Electronics,
    Medicine, Firearms, Melee, Cooking, Farming,
    Hacking, DronePiloting,
    Mechanics,
    Construction,
}

/// <summary>Where a skill gain or XP came from (for the log and for the scavenge side's own calls).</summary>
public enum XpSource { Work, Crafting, Study, Teaching, Mission, Field, Manual }

/// <summary>Coarse body areas that jobs and modifiers care about.</summary>
public enum BodyRegion { Head, Torso, Arms, Legs }

/// <summary>
/// Body parts. <see cref="Eyes"/> sit in the head region: they are never hit or lost in the field, only replaced by an
/// elective eye implant (see <c>data/cybernetics.json</c>).
/// </summary>
public enum BodyPart { Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg, Eyes }

public enum PartCondition { Healthy, Injured, Missing, Prosthetic }
