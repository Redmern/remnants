using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

public enum Terrain { Floor, Rubble, Cover, Water, Wall }

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

    /// <summary>Followers within this many tiles leave with the unit that extracts.</summary>
    public int EscortRadius { get; init; } = 2;
}
