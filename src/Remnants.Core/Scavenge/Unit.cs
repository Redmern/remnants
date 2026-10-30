using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// What a unit is. <see cref="Drone"/>: a player's scout drone, flown by its operator (no turns of its own).
/// <see cref="Device"/>: a hackable machine that acts as a unit (a camera, a turret; see <see cref="ZoneDevice"/>).
/// </summary>
public enum UnitKind { Survivor, Enemy, Npc, Drone, Device }

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

/// <summary>A defensive stance held from the end of a unit's turn until its next turn starts.</summary>
public enum Stance
{
    None,
    /// <summary>Reaction attack on the first hostile that moves or reveals itself in sight and reach.</summary>
    Overwatch,
    /// <summary>Hunkered down: no ambush bonus against it, extra cover, fewer AP next turn.</summary>
    Brace,
}

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

    /// <summary>
    /// Faction. A player's party uses <see cref="PlayerSide"/>, or the team's <see cref="TeamSide"/> while allied (allies
    /// share one side, so they never shoot each other and see as one); units on different sides are hostile, neutral is
    /// nobody's foe.
    /// </summary>
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

    /// <summary>Linked: knocked off the relay (as if jammed) through this round (see <see cref="Items.BaneDef.DisruptRounds"/>).</summary>
    public int OffRelayUntilRound { get; set; }

    /// <summary>AP added (negative: taken, never below 1) at the start of its first-round turn: a good meal or bad rations on the way.</summary>
    public int FirstRoundAp { get; set; }

    /// <summary>Pinned by suppressing fire: loses AP at the start of its next turn.</summary>
    public bool Suppressed { get; set; }

    /// <summary>Held until this unit's next turn starts.</summary>
    public Stance Stance { get; set; }

    /// <summary>Overwatch reactions left this stance.</summary>
    public int Reactions { get; set; }

    /// <summary>On lookout (scanning) until its next turn: more sight and detection.</summary>
    public bool Lookout { get; set; }

    /// <summary>The downed unit this one carries (co-op): it moves along and leaves with it; moving costs range.</summary>
    public int? CarryingId { get; set; }

    /// <summary>Set on a downed unit while someone carries it: it shares the carrier's tile and does not block it.</summary>
    public int? CarriedById { get; set; }

    /// <summary>Team pings used this turn (see <see cref="CoopRules.PingsPerTurn"/>).</summary>
    public int PingsUsed { get; set; }

    /// <summary>
    /// The survivor's perk hooks (id → summed value, see <c>PerkHooks</c>), resolved from <see cref="Survivor.Perks"/> when
    /// the party enters the zone, so the field reads them without the colony's data.
    /// </summary>
    public Dictionary<string, double> Hooks { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Hooks from fitted cybernetics (each node, and each paired set, with the parts it needs), resolved when the party
    /// enters the zone. They count in <see cref="Hook"/> only while all their parts work (not locked by interference).
    /// </summary>
    public List<CyberHookSource> CyberHooks { get; set; } = new();

    /// <summary>XP earned in the field beyond skill gains (hacks, drone flights); paid out when the survivor gets home.</summary>
    public double FieldXp { get; set; }

    /// <summary>Operator: the scout drone they have deployed.</summary>
    public int? DroneId { get; set; }

    /// <summary>Operator: battery (rounds) of their drone while it is packed; null = full. It recharges each round.</summary>
    public int? DroneBattery { get; set; }

    /// <summary>Drone: the unit flying it.</summary>
    public int? OperatorId { get; set; }

    /// <summary>Drone: battery left in rounds.</summary>
    public int Battery { get; set; }

    /// <summary>Drone: the item it was deployed from (goes back on recall); whether it came out of the Tool slot.</summary>
    public string? DroneItemId { get; set; }
    public bool DroneFromTool { get; set; }

    /// <summary>Device units (camera, turret): the <see cref="ZoneDevice"/> this unit is.</summary>
    public int? DeviceId { get; set; }

    /// <summary>A Linked turned by Override: fights for its hacker's side through this round, then goes back to the Network.</summary>
    public int? OverrideUntilRound { get; set; }

    /// <summary>Story missions: a script tag ("patrol", "tally", "juniper") that objectives and triggers point at.</summary>
    public string? Tag { get; set; }

    /// <summary>Story missions: the named character this unit is (it never joins the colony as a plain recruit).</summary>
    public string? CharacterId { get; set; }

    /// <summary>Story missions: cannot be recruited (until a trigger makes it follow).</summary>
    public bool NoRecruit { get; set; }

    /// <summary>Story missions: waypoints an idle unit walks in a loop (a patrol the player can watch), and the next one.</summary>
    public List<Pos> Patrol { get; set; } = new();
    public int PatrolIndex { get; set; }

    /// <summary>Stands on a raised tile (a watchtower): more sight and detection (see <see cref="StoryFieldRules"/>).</summary>
    public bool Raised { get; set; }

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

    /// <summary>The shared side of an alliance of players (see <see cref="Alliances"/>).</summary>
    public static string TeamSide(int teamId) => $"team:{teamId}";

    public bool IsActive => Status == UnitStatus.Active;

    /// <summary>Still on the map (alive and not extracted).</summary>
    public bool OnMap => Status is UnitStatus.Active or UnitStatus.Downed;

    /// <summary>Takes orders from a player rather than the AI.</summary>
    public bool PlayerControlled => PlayerId is not null && FollowingId is null && Kind is UnitKind.Survivor or UnitKind.Enemy;

    public double Get(AttributeKind attribute) => Attributes.GetValueOrDefault(attribute, 50);
    public double Get(Skill skill) => Skills.GetValueOrDefault(skill);

    /// <summary>A hook's value: perks plus working cybernetics (0 without either).</summary>
    public double Hook(string id)
    {
        var value = Hooks.GetValueOrDefault(id);
        foreach (var source in CyberHooks)
            if (source.Hooks.TryGetValue(id, out var v) && source.Parts.All(p => !Body[p].Interfered)) value += v;
        return value;
    }

    /// <summary>Field practice, diminishing like <see cref="Survivor.GainSkill"/>; returns the gain.</summary>
    public double GainSkill(Skill skill, double amount)
    {
        if (amount <= 0) return 0;
        var before = Get(skill);
        Skills[skill] = Math.Min(Survivor.MaxStat, before + amount * (1 - before / Survivor.MaxStat));
        return Skills[skill] - before;
    }

    public bool HostileTo(Unit other) =>
        Side != other.Side && Side != NeutralSide && other.Side != NeutralSide;

    public IEnumerable<string> AllItems => Equipment.Values.Concat(Pack);
}

/// <summary>Scavenge hooks that a fitted prosthetic (or a paired set of them) gives while its parts work.</summary>
public sealed class CyberHookSource
{
    public List<BodyPart> Parts { get; set; } = new();
    public Dictionary<string, double> Hooks { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>A pile of items on a tile: a loot cache, or what a dead unit dropped.</summary>
public sealed class LootCache
{
    public int Id { get; set; }
    public Pos Pos { get; set; }
    public List<string> Items { get; set; } = new();
}
