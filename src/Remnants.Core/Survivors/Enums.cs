namespace Remnants.Core.Survivors;

public enum Sex { Male, Female }

/// <summary>Infants grow into children and children into adults (see <c>rules.life</c>).</summary>
public enum LifeStage { Infant, Child, Adult }

public enum AttributeKind { Strength, Agility, Endurance, Intellect, Creativity, Perception }

public enum Skill
{
    Woodcutting, Mining, Crafting, Tailoring, Electronics,
    Medicine, Firearms, Melee, Cooking, Farming,
}

/// <summary>Coarse body areas that jobs and modifiers care about.</summary>
public enum BodyRegion { Head, Torso, Arms, Legs }

public enum BodyPart { Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg }

public enum PartCondition { Healthy, Injured, Missing, Prosthetic }
