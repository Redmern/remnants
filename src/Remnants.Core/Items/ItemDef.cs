using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Items;

public enum ItemKind { Weapon, Armor, Clothing, Tool, Consumable, Material, Prosthetic, Blueprint, Food }

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

    /// <summary>Splash radius (Chebyshev) around the target: other hostiles of the attacker in it are attacked too, each with its own roll.</summary>
    public int Area { get; init; }

    /// <summary>
    /// Rounds the attacker stays revealed to the victim's side after using this (muzzle flash). Stealth no longer hides
    /// it there, but it still has to be in the side's sight. Melee reveals less than gunfire.
    /// </summary>
    public int Reveal { get; init; } = 1;

    /// <summary>Extra punch against enemies with a tag (a Linked carbine against the Network).</summary>
    public BaneDef? Bane { get; init; }

    /// <summary>
    /// Accuracy a quality tier adds per point of its stat multiplier above 1 (Fine +0.25 × this; Crude loses it the
    /// same way). Gear whose handling depends on the build: a fine Linked carbine shoots straighter, not just harder.
    /// </summary>
    public double QualityAccuracy { get; init; }
}

/// <summary>
/// An attack's edge against enemies carrying <see cref="Tag"/> (<see cref="Remnants.Core.Scavenge.EnemyDef.Tags"/>):
/// <see cref="HitBonus"/> is added to the hit chance, the damage roll is multiplied, then the target's armor counts for
/// <see cref="ArmorPierce"/> less (not below 0). A Linked hit is knocked off the relay for <see cref="DisruptRounds"/>
/// rounds (this one and the next N − 1): it fights alone, as if jammed.
/// </summary>
public sealed record BaneDef
{
    public string Tag { get; init; } = "";
    public double HitBonus { get; init; }
    public double DamageMultiplier { get; init; } = 1;
    public double ArmorPierce { get; init; }
    public int DisruptRounds { get; init; }
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

    /// <summary>Flare: thrown at a tile within <see cref="Range"/>; lights this radius (Euclidean) so every side sees all of it.</summary>
    public double LightRadius { get; init; }

    /// <summary>Flare: rounds the light lasts (the current one counts).</summary>
    public int LightRounds { get; init; }

    /// <summary>Motion sensor: placed on a tile within <see cref="Range"/>; reveals hostiles that move within this radius (Chebyshev).</summary>
    public int SensorRadius { get; init; }

    /// <summary>Used on a tile (<see cref="Remnants.Core.Scavenge.UseItem.At"/>) rather than on a unit.</summary>
    public bool TargetsTile => LightRadius > 0 || SensorRadius > 0;
}

/// <summary>A replacement limb fitted at the Med Bay. Quality is the part's efficiency (above 1 beats the original).</summary>
public sealed record ProstheticDef
{
    public BodyRegion Region { get; init; } = BodyRegion.Legs;
    public double Quality { get; init; } = 0.6;
    public int MinMedBayLevel { get; init; } = 1;
}

/// <summary>What a looted item turns into when it reaches the colony stockpile (scrap → 3 Scrap Metal).</summary>
public sealed record SalvageDef
{
    public string Resource { get; init; } = "";
    public double Amount { get; init; } = 1;
}

/// <summary>What an equipped device does in the field.</summary>
public enum DeviceEffect
{
    /// <summary>Scanner: hostile units within the radius are seen by the carrier's side, through walls and stealth.</summary>
    Scanner,
    /// <summary>Signal jammer: Linked within the radius lose their Network coordination (they stop taking cover).</summary>
    Jammer,
}

public sealed record DeviceDef
{
    public DeviceEffect Effect { get; init; }
    public int Radius { get; init; } = 3;
}

/// <summary>
/// A hacking tool (kit, cyberdeck): carried (equipped or in the pack), it lets its carrier hack devices within
/// <see cref="Range"/> (beyond 1 tile with line of sight), adds <see cref="ChanceBonus"/> and puts <see cref="Progress"/>
/// points of work into the hack per action.
/// </summary>
public sealed record HackToolDef
{
    public int Range { get; init; } = 1;
    public double ChanceBonus { get; init; }
    public int Progress { get; init; } = 1;
}

/// <summary>
/// A scout drone item: deployed it becomes a flying unit. Battery in rounds, how far one flight goes, and how far from its
/// operator it can be (before Drone Piloting and perks).
/// </summary>
public sealed record DroneItemDef
{
    public int Battery { get; init; } = 6;
    public int FlyRange { get; init; } = 4;
    public int ControlRange { get; init; } = 6;
}

/// <summary>A blueprint is studied (and used up) at a building to unlock the recipes that name it.</summary>
public sealed record BlueprintDef
{
    /// <summary>Building type it is studied at (Town Hall, Electronics Lab).</summary>
    public string StudyAt { get; init; } = "";
    public int MinLevel { get; init; } = 1;
}

/// <summary>
/// A field manual (a Material item): used up when a skill study starts with it. It speeds the study up and lets a
/// student without a mentor go past the self-study cap, up to <see cref="Cap"/>.
/// </summary>
public sealed record ManualDef
{
    public Skill Skill { get; init; }
    public double StudyBonus { get; init; } = 0.5;
    public double Cap { get; init; } = 35;
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

    /// <summary>Added to the wearer's stealth: a rag coat is quiet, riot armor loud, a camo cloak hides (see <see cref="Remnants.Core.Scavenge.Detection"/>).</summary>
    public double Stealth { get; init; }

    /// <summary>Added to the carrier's detection (binoculars, scanner).</summary>
    public double Detection { get; init; }

    /// <summary>Movement and action modifiers only apply when this body requirement holds.</summary>
    public BodyRequirement? Requires { get; init; }

    public MovementModifier? Movement { get; init; }
    public List<ActionDef> Actions { get; init; } = new();
    public UseEffect? Use { get; init; }

    /// <summary>Prosthetic items: which limb they replace and how well.</summary>
    public ProstheticDef? Prosthetic { get; init; }

    /// <summary>If set, the colony stores this item as stockpile resources rather than as an item.</summary>
    public SalvageDef? Salvage { get; init; }

    /// <summary>Equipped devices (Tool slot) with a field effect.</summary>
    public DeviceDef? Device { get; init; }

    /// <summary>Hacking tools (see <see cref="HackToolDef"/>).</summary>
    public HackToolDef? HackTool { get; init; }

    /// <summary>Scout drones (see <see cref="DroneItemDef"/>).</summary>
    public DroneItemDef? Drone { get; init; }

    /// <summary>Blueprint items: where they are studied.</summary>
    public BlueprintDef? Blueprint { get; init; }

    /// <summary>Manuals: the skill they teach (see <see cref="ManualDef"/>).</summary>
    public ManualDef? Manual { get; init; }

    /// <summary>Part of an enemy (claws): never looted or crafted.</summary>
    public bool Natural { get; init; }

    /// <summary>Quality variants: the item they are a variant of (null for a defined item).</summary>
    public string? BaseId { get; init; }

    /// <summary>Quality variants: the tier id (null for a defined item, which is the base tier).</summary>
    public string? Quality { get; init; }

    /// <summary>The defined item this is or is a variant of.</summary>
    public string DefinedId => BaseId ?? Id;

    /// <summary>Gear that comes in quality tiers when crafted.</summary>
    public bool HasQuality => Slot is not null && !Natural;

    public EquipSlot? Slot => Kind switch
    {
        ItemKind.Weapon => EquipSlot.Weapon,
        ItemKind.Armor or ItemKind.Clothing => EquipSlot.Body,
        ItemKind.Tool => EquipSlot.Tool,
        _ => null,
    };
}
