using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Items;

public enum ItemKind { Weapon, Armor, Clothing, Tool, Consumable, Material }

/// <summary>Where an item sits when equipped. Anything not equipped is carried in the pack.</summary>
public enum EquipSlot { Weapon, Body, Tool }

/// <summary>How a unit may move in one move action (chess-like).</summary>
public enum MovePattern
{
    /// <summary>Walk: steps in any of 8 directions, terrain costs summed up to the range.</summary>
    King,
    /// <summary>Straight orthogonal line up to the range.</summary>
    Rook,
    /// <summary>Straight diagonal line up to the range.</summary>
    Bishop,
    /// <summary>Straight line in any of 8 directions up to the range.</summary>
    Queen,
    /// <summary>L-shaped jump (2+1) over anything; lands on a free passable tile.</summary>
    Knight,
}

/// <summary>Which tiles an attack can reach.</summary>
public enum AttackPattern
{
    /// <summary>Any tile within reach in Chebyshev distance (adjacent at reach 1). No line of sight needed beyond adjacency.</summary>
    Melee,
    /// <summary>Any tile within reach (Euclidean) with line of sight.</summary>
    Ranged,
    /// <summary>Orthogonal or diagonal straight lines only, within reach, with line of sight.</summary>
    Line,
}

/// <summary>Body requirement for a modifier or action, e.g. a grappling hook needs working arms.</summary>
public sealed record BodyRequirement
{
    public BodyRegion Region { get; init; }
    public double MinEfficiency { get; init; }
}

/// <summary>One way of moving. A unit's MovementProfile holds one or more.</summary>
public sealed record MoveModeDef
{
    public MovePattern Pattern { get; init; } = MovePattern.King;
    public int Range { get; init; } = 1;
    public int ApCost { get; init; } = 1;

    /// <summary>Jumps ignore terrain cost and what lies between start and landing tile.</summary>
    public bool IgnoresTerrain { get; init; }
}

/// <summary>How an item (or a body state) changes movement. Applied in a fixed order during derivation.</summary>
public sealed record MovementModifier
{
    /// <summary>Replace the primary move pattern (heavy armor: King; scout gear: Rook).</summary>
    public MovePattern? SetPattern { get; init; }
    public int RangeBonus { get; init; }

    /// <summary>Hard cap on the primary range after everything else.</summary>
    public int? MaxRange { get; init; }

    /// <summary>Extra move modes, e.g. a grappling hook's knight jump.</summary>
    public List<MoveModeDef> AddModes { get; init; } = new();

    /// <summary>Per-terrain move cost overrides (climbing boots: Rubble 1). The cheapest override wins.</summary>
    public Dictionary<Terrain, int> TerrainCosts { get; init; } = new();
}

/// <summary>An attack an item (or bare hands) grants. Final numbers are derived per unit.</summary>
public sealed record ActionDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public AttackPattern Pattern { get; init; } = AttackPattern.Melee;
    public int Reach { get; init; } = 1;
    public int MinDamage { get; init; }
    public int MaxDamage { get; init; }
    public int ApCost { get; init; } = 1;

    /// <summary>Base hit chance 0..1 before attacker skill and target defenses.</summary>
    public double Accuracy { get; init; } = 0.7;
    public Skill Skill { get; init; } = Skill.Melee;

    /// <summary>Damage scales by (1 + (Strength - 50) / 100 * StrengthScaling).</summary>
    public double StrengthScaling { get; init; }

    /// <summary>Radius in tiles in which enemies hear the attack.</summary>
    public int Noise { get; init; } = 2;

    /// <summary>Needs both arms: arm factor is the average of both, not the best one.</summary>
    public bool TwoHanded { get; init; }
}

/// <summary>What using a consumable does.</summary>
public sealed record UseEffect
{
    /// <summary>0 = self only, 1 = self or an adjacent unit.</summary>
    public int Range { get; init; }
    public int ApCost { get; init; } = 1;
    public bool Consumed { get; init; } = true;

    /// <summary>HP restored on a conscious unit.</summary>
    public double Heal { get; init; }

    /// <summary>Stops a downed unit bleeding out.</summary>
    public bool Stabilize { get; init; }

    /// <summary>If above 0, a stabilized downed unit gets back up with this HP.</summary>
    public double ReviveHp { get; init; }

    /// <summary>Pulls the target off the map; it counts as returned home.</summary>
    public bool Evacuate { get; init; }
}

public sealed record ItemDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public ItemKind Kind { get; init; }
    public double Weight { get; init; }

    /// <summary>Loot tier; zones drop items with tier at or below their loot tier.</summary>
    public int Tier { get; init; } = 1;

    /// <summary>Relative chance in loot caches. 0 = never found as loot.</summary>
    public double LootWeight { get; init; }

    /// <summary>Flat damage reduction while equipped.</summary>
    public double Armor { get; init; }

    /// <summary>Hit chance subtracted from attacks against the wearer.</summary>
    public double Dodge { get; init; }

    public double Initiative { get; init; }
    public int ApBonus { get; init; }
    public double SightBonus { get; init; }
    public double CarryBonus { get; init; }

    /// <summary>Movement and action modifiers only apply when this body requirement holds.</summary>
    public BodyRequirement? Requires { get; init; }

    public MovementModifier? Movement { get; init; }
    public List<ActionDef> Actions { get; init; } = new();
    public UseEffect? Use { get; init; }

    public EquipSlot? Slot => Kind switch
    {
        ItemKind.Weapon => EquipSlot.Weapon,
        ItemKind.Armor or ItemKind.Clothing => EquipSlot.Body,
        ItemKind.Tool => EquipSlot.Tool,
        _ => null,
    };
}
