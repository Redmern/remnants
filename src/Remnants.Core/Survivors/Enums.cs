namespace Remnants.Core.Survivors;

public enum Sex { Male, Female }

public enum LifeStage { Child, Adult }

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
