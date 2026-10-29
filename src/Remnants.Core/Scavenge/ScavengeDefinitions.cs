using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>Pylon: a Network relay mast (impassable, gives cover); see <see cref="Network"/>.</summary>
public enum Terrain { Floor, Rubble, Cover, Water, Wall, Pylon }

public enum MapEdge { North, South, East, West }

public enum AiKind { Husk, Linked }

public sealed record TerrainDef
{
    public bool Passable { get; init; } = true;
    public int MoveCost { get; init; } = 1;
    public bool BlocksSight { get; init; }

    /// <summary>Hit chance subtracted when this tile shields a target from the attacker's side.</summary>
    public double Cover { get; init; }
}

public sealed record EnemyDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double Hp { get; init; } = 50;
    public AiKind Ai { get; init; }

    /// <summary>Survivors go Downed at 0 HP; most enemies just die.</summary>
    public bool CanBeDowned { get; init; }

    /// <summary>Linked: how far (Chebyshev) this unit links to other Network nodes. Drones relay further.</summary>
    public int RelayRange { get; init; }

    /// <summary>Missing attributes default to 50.</summary>
    public Dictionary<AttributeKind, double> Attributes { get; init; } = new();
    public Dictionary<Skill, double> Skills { get; init; } = new();

    /// <summary>Item ids it fights with (claws are an item too), so enemy profiles derive like ours.</summary>
    public List<string> Equipment { get; init; } = new();
}

public sealed record WeightedId
{
    public string Id { get; init; } = "";
    public double Weight { get; init; } = 1;

    /// <summary>Optional cap on how many of this enemy a zone spawns (early zones: "occasional 1–2 Linked").</summary>
    public int? Max { get; init; }
}

/// <summary>Reinforcements a zone's pylons can call in once the Network has been alarmed for long enough.</summary>
public sealed record ReinforcementDef
{
    /// <summary>How many times the pylons can call (0 = never).</summary>
    public int Waves { get; init; }

    /// <summary>Units per wave.</summary>
    public int[] Count { get; init; } = [1, 2];
    public List<WeightedId> Mix { get; init; } = new();
}

public sealed record ZoneTerrainDef
{
    public int[] Rooms { get; init; } = [2, 4];
    public int[] RoomSize { get; init; } = [4, 7];
    public double Rubble { get; init; } = 0.05;
    public double Cover { get; init; } = 0.04;
    public double Water { get; init; }
}

/// <summary>A zone template: the generator turns it plus a seed into a concrete map.</summary>
public sealed record ZoneDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Width { get; init; } = 24;
    public int Height { get; init; } = 24;
    public int Threat { get; init; } = 1;
    public int LootTier { get; init; } = 1;
    public ZoneTerrainDef Terrain { get; init; } = new();
    public MapEdge SpawnEdge { get; init; } = MapEdge.South;
    public MapEdge ExitEdge { get; init; } = MapEdge.North;
    public int Exits { get; init; } = 2;
    public int[] Enemies { get; init; } = [3, 5];
    public List<WeightedId> EnemyMix { get; init; } = new();
    public int[] Caches { get; init; } = [3, 5];
    public int[] ItemsPerCache { get; init; } = [1, 3];
    public int[] Npcs { get; init; } = [0, 1];

    /// <summary>Network pylons placed on the map (min, max).</summary>
    public int[] Pylons { get; init; } = [0, 0];
    public ReinforcementDef Reinforcements { get; init; } = new();

    // World map (colony side): where the zone is, how far, and how it recovers from being picked over.

    /// <summary>Region label on the world map.</summary>
    public string Region { get; init; } = "";

    /// <summary>Marker position on the world map, each axis 0..1 (x east, y south).</summary>
    public double[] MapPos { get; init; } = [0.5, 0.5];

    /// <summary>One-way travel time in colony days.</summary>
    public double TravelDays { get; init; }

    /// <summary>Depletion (0..1) a visit adds; fewer caches spawn while depleted.</summary>
    public double DepletionPerVisit { get; init; } = 0.4;

    /// <summary>Depletion recovered per colony day.</summary>
    public double ReplenishPerDay { get; init; } = 0.05;

    /// <summary>What the colony knows about the zone in a new game.</summary>
    public ZoneIntel StartIntel { get; init; } = ZoneIntel.Unknown;

    /// <summary>Zones a visit here reveals as rumors.</summary>
    public List<string> Reveals { get; init; } = new();
    public string Description { get; init; } = "";
}

/// <summary>
/// How much the colony knows about a zone: unknown (not on the map), rumored (name, distance, rough threat) or
/// scouted (everything: threat, reward tier, Linked presence, enemy count). Visiting a zone scouts it.
/// </summary>
public enum ZoneIntel { Unknown, Rumored, Scouted }

/// <summary>World map presentation data (scavenge.json <c>world</c>).</summary>
public sealed record WorldRules
{
    /// <summary>The colony's marker position, each axis 0..1.</summary>
    public double[] ColonyPos { get; init; } = [0.5, 0.5];
}

/// <summary>
/// Linked squad rules (scavenge.json <c>network</c>). Linked units and pylons form a relay graph; what one node sees,
/// every node in its connected component knows.
/// </summary>
public sealed record NetworkRules
{
    /// <summary>Relay range of a pylon tile (Chebyshev).</summary>
    public int PylonRange { get; init; } = 8;

    /// <summary>Rounds of alarm (a connected Linked knows a hostile) before an online pylon calls reinforcements.</summary>
    public int ReinforceAfterRounds { get; init; } = 3;

    /// <summary>HP fraction at or below which a Linked falls back (unless it is the last of its squad).</summary>
    public double RetreatHpFraction { get; init; } = 0.35;

    /// <summary>Below this hit chance a Linked lays down suppressing fire instead of a real shot.</summary>
    public double SuppressBelowHitChance { get; init; } = 0.3;

    /// <summary>AP a suppressed unit loses on its next turn.</summary>
    public int SuppressApPenalty { get; init; } = 1;

    /// <summary>Position scoring: value of cover at the end tile, per point of cover (0..1).</summary>
    public double CoverWeight { get; init; } = 10;

    /// <summary>Position scoring: value per AP left after moving (Linked prefer short moves).</summary>
    public double ApWeight { get; init; } = 0.5;
}

/// <summary>A movement modifier that applies while the body matches, e.g. a missing leg caps range at 1.</summary>
public sealed record BodyMovementRule
{
    public BodyRegion Region { get; init; } = BodyRegion.Legs;
    public PartCondition Condition { get; init; } = PartCondition.Missing;
    public int MinCount { get; init; } = 1;

    /// <summary>For prosthetics: only counts parts with at least this quality.</summary>
    public double MinQuality { get; init; }
    public MovementModifier Modifier { get; init; } = new();
}

public sealed record EncumbranceStep
{
    /// <summary>Load as a fraction of carry capacity at which this step kicks in.</summary>
    public double Load { get; init; }
    public int RangePenalty { get; init; }
}

public sealed record MovementRules
{
    public MovePattern BasePattern { get; init; } = MovePattern.King;
    public int BaseRange { get; init; } = 1;

    /// <summary>+1 range per this much Agility.</summary>
    public double AgilityPerRange { get; init; } = 30;
    public int ApCost { get; init; } = 1;
    public int MinRange { get; init; } = 1;
    public List<EncumbranceStep> Encumbrance { get; init; } = new();
    public List<BodyMovementRule> Body { get; init; } = new();
}

public sealed record TurnRules
{
    public int BaseAp { get; init; } = 3;
    public int PickUpApCost { get; init; } = 1;
    public int RecruitApCost { get; init; } = 1;
    public double InitiativePerAgility { get; init; } = 1.0;
    public double InitiativePerLoad { get; init; } = 0.5;
}

public sealed record SightRules
{
    public double Base { get; init; } = 4;
    public double PerPerception { get; init; } = 0.06;
}

public sealed record CarryRules
{
    public double Base { get; init; } = 10;
    public double PerStrength { get; init; } = 0.3;
}

public sealed record CombatRules
{
    public double SkillAccuracy { get; init; } = 0.003;

    /// <summary>Accuracy per Perception point above (or below) 50.</summary>
    public double PerceptionAccuracy { get; init; } = 0.002;

    /// <summary>Dodge per Agility point above 50 (negative below).</summary>
    public double DodgePerAgility { get; init; } = 0.002;
    public double RangeFalloff { get; init; } = 0.03;
    public double MinHitChance { get; init; } = 0.05;
    public double MaxHitChance { get; init; } = 0.95;
    public Dictionary<BodyPart, double> HitLocations { get; init; } = new();

    /// <summary>Damage for an injury severity of 1 on a part of this region.</summary>
    public Dictionary<BodyRegion, double> PartToughness { get; init; } = new();

    /// <summary>Chance a limb at severity 1 is lost.</summary>
    public double LimbLossChance { get; init; } = 0.5;
    public int BleedOutRounds { get; init; } = 3;

    /// <summary>HP at or below which a hit kills outright instead of downing.</summary>
    public double InstantDeathHp { get; init; } = -40;
    public ActionDef Unarmed { get; init; } = new();
}

public sealed record AiRules
{
    public double WanderChance { get; init; } = 0.5;
    public int MinSpawnDistance { get; init; } = 8;
}

public sealed record NpcRules
{
    public int[] AttributeRange { get; init; } = [20, 55];
    public List<string> Names { get; init; } = new();
}

public sealed record ScavengeRules
{
    public Dictionary<Terrain, TerrainDef> Terrain { get; init; } = new();
    public MovementRules Movement { get; init; } = new();
    public TurnRules Turn { get; init; } = new();
    public SightRules Sight { get; init; } = new();
    public CarryRules Carry { get; init; } = new();
    public CombatRules Combat { get; init; } = new();
    public AiRules Ai { get; init; } = new();
    public NpcRules Npc { get; init; } = new();
    public NetworkRules Network { get; init; } = new();
    public WorldRules World { get; init; } = new();

    /// <summary>Followers within this many tiles leave with the unit that extracts.</summary>
    public int EscortRadius { get; init; } = 2;

    /// <summary>Quality tiers of crafted gear, worst first. The <c>base</c> tier is the item as defined.</summary>
    public List<QualityTierDef> Quality { get; init; } = new();
}

/// <summary>
/// A quality tier for gear (Crude / Standard / Fine). Every equippable item gets a variant per non-base tier,
/// id <c>{item}@{tier}</c>, with armor, damage, dodge/initiative bonuses, sight and carry bonuses scaled by
/// <see cref="StatMultiplier"/> and weight by <see cref="WeightMultiplier"/>.
/// </summary>
public sealed record QualityTierDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double StatMultiplier { get; init; } = 1;
    public double WeightMultiplier { get; init; } = 1;
    public bool Base { get; init; }
}
