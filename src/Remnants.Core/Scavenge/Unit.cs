using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

public enum UnitKind { Survivor, Enemy, Npc }

public enum UnitStatus
{
    Active,
    /// <summary>At 0 HP: cannot act, bleeds out unless stabilized.</summary>
    Downed,
    Dead,
    /// <summary>Walked off an exit tile.</summary>
    Extracted,
    /// <summary>Pulled out by an evac beacon.</summary>
    Evacuated,
}

public enum AiMode { Idle, Hunting }

/// <summary>What an AI-controlled unit remembers between turns.</summary>
public sealed class AiMemory
{
    public AiMode Mode { get; set; }
    public int? TargetId { get; set; }

    /// <summary>Where it last saw its target or heard a noise.</summary>
    public Pos? LastKnown { get; set; }
}

/// <summary>
/// Anything on the map that takes turns: a party survivor, an enemy, or an NPC survivor.
/// Plain data; profiles (movement, actions, AP, initiative) are derived from it by <see cref="ProfileDeriver"/>.
/// </summary>
public sealed class Unit
{
    public const string EnemySide = "enemies";
    public const string NeutralSide = "neutral";

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public UnitKind Kind { get; set; }

    /// <summary>Faction. Player parties use <see cref="PlayerSide"/>; units on different sides are hostile, neutral is nobody's foe.</summary>
    public string Side { get; set; } = "";

    /// <summary>Owning player for party units and recruits; null for enemies and unrecruited NPCs.</summary>
    public int? PlayerId { get; set; }

    /// <summary>Recruits follow this unit and are moved by the AI, not the player.</summary>
    public int? FollowingId { get; set; }

    /// <summary>Enemy definition id for enemies.</summary>
    public string? EnemyDefId { get; set; }

    public Pos Pos { get; set; }
    public double Hp { get; set; }
    public double MaxHp { get; set; }
    public int Ap { get; set; }
    public UnitStatus Status { get; set; }
    public bool CanBeDowned { get; set; } = true;

    /// <summary>Rounds left before a downed, unstabilized unit dies.</summary>
    public int BleedOut { get; set; }
    public bool Stabilized { get; set; }

    public Dictionary<AttributeKind, double> Attributes { get; set; } = new();
    public Dictionary<Skill, double> Skills { get; set; } = new();
    public Body Body { get; set; } = new();

    public Dictionary<EquipSlot, string> Equipment { get; set; } = new();

    /// <summary>Carried but not equipped item ids: loot, consumables, spare gear.</summary>
    public List<string> Pack { get; set; } = new();

    public AiMemory Ai { get; set; } = new();

    /// <summary>For survivors and NPCs: the survivor as they entered the map (results are built from it).</summary>
    public Survivor? Origin { get; set; }

    public static string PlayerSide(int playerId) => $"player:{playerId}";

    public bool IsActive => Status == UnitStatus.Active;

    /// <summary>Still on the map (alive and not extracted).</summary>
    public bool OnMap => Status is UnitStatus.Active or UnitStatus.Downed;

    /// <summary>Takes orders from a player rather than the AI.</summary>
    public bool PlayerControlled => PlayerId is not null && FollowingId is null && Kind != UnitKind.Npc;

    public double Get(AttributeKind attribute) => Attributes.GetValueOrDefault(attribute, 50);
    public double Get(Skill skill) => Skills.GetValueOrDefault(skill);

    public bool HostileTo(Unit other) =>
        Side != other.Side && Side != NeutralSide && other.Side != NeutralSide;

    public IEnumerable<string> AllItems => Equipment.Values.Concat(Pack);
}

/// <summary>A pile of items on a tile: a loot cache, or what a dead unit dropped.</summary>
public sealed class LootCache
{
    public int Id { get; set; }
    public Pos Pos { get; set; }
    public List<string> Items { get; set; } = new();
}
