using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// What a unit is. <see cref="Drone"/>: a player's scout drone, flown by its operator (no turns of its own).
/// <see cref="Device"/>: a hackable machine that acts as a unit (a camera, a turret; see <see cref="ZoneDevice"/>).
/// </summary>
/// <see cref="Decoy"/>: a holo-decoy (a fake survivor that draws fire for a few rounds; no turns, nobody controls it).
public enum UnitKind { Survivor, Enemy, Npc, Drone, Device, Decoy }

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

    /// <summary>Movement points left this round (terrain cost per tile).</summary>
    public int Mp { get; set; }

    /// <summary>Stamina spent of the profile's maximum (<see cref="Economy"/>): 0 is fresh.</summary>
    public int Fatigue { get; set; }

    /// <summary>What cards added to (or took off) its resolve priority this round (<see cref="Priority"/>).</summary>
    public double PriorityBonus { get; set; }
    public UnitStatus Status { get; set; }
    public bool CanBeDowned { get; set; } = true;

    /// <summary>Rounds left before a downed, unstabilized unit dies.</summary>
    public int BleedOut { get; set; }
    public bool Stabilized { get; set; }

    /// <summary>Linked: knocked off the relay (as if jammed) through this round (see <see cref="Items.BaneDef.DisruptRounds"/>).</summary>
    public int OffRelayUntilRound { get; set; }

    /// <summary>AP added (negative: taken, never below 1) at the start of its first-round turn: a good meal or bad rations on the way.</summary>
    public int FirstRoundAp { get; set; }

    /// <summary>The round a stun costs this unit AP, MP and priority (<see cref="Stuns"/>); its stuns so far this mission.</summary>
    public int StunnedRound { get; set; }
    public int StunCount { get; set; }

    /// <summary>The meter of <see cref="Scavenge.Adrenaline"/>.</summary>
    public double Adrenaline { get; set; }

    /// <summary>A stim wears off: its next turn starts this many AP short.</summary>
    public int CrashAp { get; set; }

    /// <summary>Under a Linked "Calm" injector: cannot be pinned down through this round.</summary>
    public int CalmUntilRound { get; set; }

    /// <summary>Moved this turn (a ranged attack after it loses accuracy, see <see cref="GearEffects.MovedPenalty"/>).</summary>
    public bool Moved { get; set; }

    /// <summary>The direction it last moved or attacked in (sign per axis; a weapon light points there). Null until then.</summary>
    public Pos? Facing { get; set; }

    /// <summary>Personal card slots: cards this character always has to hand (not shuffled), paid from the squad's points.</summary>
    public List<string> SlotCards { get; set; } = new();

    /// <summary>In another player's view: how many slot cards the unit holds (<see cref="SlotCards"/> is not sent).</summary>
    public int HiddenSlots { get; set; }

    /// <summary>Slot cards played this round: they come back at the next round start (consumable ones are gone instead).</summary>
    public List<string> SlotSpent { get; set; } = new();

    /// <summary>Move range cards added this turn.</summary>
    public int CardMove { get; set; }

    /// <summary>Cards played on the unit this round: one of its plan slots (<see cref="Plans.Limits"/>).</summary>
    public int CardsPlayed { get; set; }

    /// <summary>This round's extra plan slots, from cards; perks add theirs on top (<see cref="Plans.Limits"/>).</summary>
    public int ExtraMoves { get; set; }
    public int ExtraActions { get; set; }
    public int ExtraCards { get; set; }

    /// <summary>Actions it may still plan this round for no AP and no stamina.</summary>
    public int FreeActions { get; set; }

    /// <summary>Hit chance cards added to this unit's next attack.</summary>
    public double CardHit { get; set; }

    /// <summary>Tiers the next hit this unit takes is shifted down (cards; used up by that hit).</summary>
    public int Guard { get; set; }

    /// <summary>Cover cards added until this unit's next turn.</summary>
    public double CardCover { get; set; }

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

    /// <summary>
    /// A building to defend (a handmade map's or a raid battle's structure): it only has HP. Hits name no body part and
    /// injure nothing, and at 0 HP it is destroyed.
    /// </summary>
    public bool Structure { get; set; }

    /// <summary>Story missions: waypoints an idle unit walks in a loop (a patrol the player can watch), and the next one.</summary>
    public List<Pos> Patrol { get; set; } = new();
    public int PatrolIndex { get; set; }

    /// <summary>Stands on a raised tile (a watchtower): more sight and detection (see <see cref="StoryFieldRules"/>).</summary>
    public bool Raised { get; set; }

    /// <summary>Hit by an EMP: a Linked loses its turns' AP through this round (see <see cref="ElectronicWarfare"/>).</summary>
    public int EmpUntilRound { get; set; }

    /// <summary>A holo-decoy: it fades at the start of the round after this one.</summary>
    public int DecoyUntilRound { get; set; }

    /// <summary>Charges spent this mission per device item id (reusable devices, see <see cref="Items.UseEffect.Charges"/>).</summary>
    public Dictionary<string, int> ChargesUsed { get; set; } = new(StringComparer.Ordinal);

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
/// <summary>What a pile of loot is: a zone cache, a body's remains, items someone dropped, a side's stash at an exit.</summary>
public enum LootPileKind { Cache, Remains, Dropped, Stash }

/// <summary>
/// Items on a tile anyone next to it can take (first come, first served). <see cref="Source"/> names whose remains or
/// what was salvaged ("Husk 3"); a <see cref="LootPileKind.Stash"/> belongs to <see cref="PlayerId"/> and what is left in
/// it waits for them at the zone's exit next time (<see cref="ExtractionResult.Stash"/>).
/// </summary>
public sealed class LootCache
{
    public int Id { get; set; }
    public Pos Pos { get; set; }
    public List<string> Items { get; set; } = new();
    public LootPileKind Kind { get; set; }
    public string? Source { get; set; }
    public int? PlayerId { get; set; }
}

