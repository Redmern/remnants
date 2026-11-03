using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Pylon: a Network relay mast (impassable, gives cover); see <see cref="Network"/>. Vehicle / ArmoredVehicle: a party's
/// parked vehicle (impassable; light or hard cover); see <see cref="ParkedVehicles"/>.
/// </summary>
public enum Terrain { Floor, Rubble, Cover, Water, Wall, Pylon, Vehicle, ArmoredVehicle }

public enum MapEdge { North, South, East, West }

/// <summary>
/// How an enemy definition is run: Husk and Linked as before; Device for fixed machines that act as units (a camera, a
/// turret): they never move and take their side's orders (see <see cref="ZoneDevice"/>).
/// </summary>
public enum AiKind { Husk, Linked, Device }

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

    /// <summary>
    /// An authored enemy's (a boss's, a named squad's) cards: they join the enemy side's deck (<see cref="EnemyCards"/>).
    /// Most enemies have none.
    /// </summary>
    public List<string> Cards { get; init; } = new();

    /// <summary>Added to the derived stealth: Linked move like hunters, Husks shamble.</summary>
    public double Stealth { get; init; }

    /// <summary>Missing attributes default to 50.</summary>
    public Dictionary<AttributeKind, double> Attributes { get; init; } = new();
    public Dictionary<Skill, double> Skills { get; init; } = new();

    /// <summary>Item ids it fights with (claws are an item too), so enemy profiles derive like ours.</summary>
    public List<string> Equipment { get; init; } = new();

    /// <summary>What it is ("network"), for weapons with a <see cref="Remnants.Core.Items.BaneDef"/> against it.</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>Schematics (and a few fixed items) it may leave when killed, each rolled on its own. Its equipment never drops.</summary>
    public List<EnemyDropDef> Drops { get; init; } = new();

    /// <summary>The loot table (data/loot_tables.json) a kill rolls; null = only <see cref="Drops"/>.</summary>
    public string? Loot { get; init; }
}

/// <summary>An item an enemy drops on death with this chance (0..1).</summary>
public sealed record EnemyDropDef
{
    public string Item { get; init; } = "";
    public double Chance { get; init; }
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

    /// <summary>Rounds of alarm before a call; null: the rules' <c>network.reinforceAfterRounds</c>.</summary>
    public int? AfterRounds { get; init; }
}

/// <summary>
/// Which runs take a zone's <see cref="ZoneDef.Retune"/> (a setting, to compare): none, free expeditions only (story
/// missions keep the zone as it is), or every run there.
/// </summary>
public enum LateZoneTuning
{
    Current,
    Expeditions,
    Everywhere,
}

public static class ZoneTuning
{
    /// <summary>Whether a run takes the zone's retune under <paramref name="mode"/>; <paramref name="story"/>: a story mission.</summary>
    public static bool Applies(LateZoneTuning mode, bool story) =>
        mode == LateZoneTuning.Everywhere || mode == LateZoneTuning.Expeditions && !story;

    /// <summary>The names a flag or setting may use (lower case).</summary>
    public const string Names = "current, expeditions or everywhere";

    /// <summary>A mode from its name (any case); false for anything else, numbers included.</summary>
    public static bool TryParse(string? text, out LateZoneTuning mode)
    {
        mode = LateZoneTuning.Current;
        if (string.IsNullOrWhiteSpace(text) || !char.IsLetter(text.Trim()[0]) || text.Contains(',')) return false;
        return Enum.TryParse(text.Trim(), ignoreCase: true, out mode) && Enum.IsDefined(mode);
    }
}

/// <summary>
/// A zone's alternative numbers (zones.json <c>retune</c>): what is named replaces the zone's own (devices per kind;
/// reinforcement waves, count and delay, keeping the mix). The late zones carry one, to bring back their difficulty
/// now that enemies telegraph (BALANCE.md, "Late-zone re-tune").
/// </summary>
public sealed record ZoneRetuneDef
{
    public int[]? Enemies { get; init; }
    public int[]? Pylons { get; init; }
    public Dictionary<DeviceKind, int[]>? Devices { get; init; }
    public int? Waves { get; init; }
    public int[]? ReinforcementCount { get; init; }
    public int? ReinforceAfterRounds { get; init; }
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

    /// <summary>Hackable devices placed in the zone: [min, max] per kind (see <see cref="DeviceKindDef"/>).</summary>
    public Dictionary<DeviceKind, int[]> Devices { get; init; } = new();

    /// <summary>The zone's alternative numbers, taken by the runs <see cref="LateZoneTuning"/> picks; null: none.</summary>
    public ZoneRetuneDef? Retune { get; init; }

    /// <summary>This zone with its <see cref="Retune"/> applied (itself when it has none).</summary>
    public ZoneDef Retuned()
    {
        if (Retune is not { } r) return this;
        var devices = new Dictionary<DeviceKind, int[]>(Devices);
        foreach (var (kind, range) in r.Devices ?? new()) devices[kind] = range;
        return this with
        {
            Enemies = r.Enemies ?? Enemies,
            Pylons = r.Pylons ?? Pylons,
            Devices = devices,
            Reinforcements = Reinforcements with
            {
                Waves = r.Waves ?? Reinforcements.Waves,
                Count = r.ReinforcementCount ?? Reinforcements.Count,
                AfterRounds = r.ReinforceAfterRounds ?? Reinforcements.AfterRounds,
            },
        };
    }

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

    /// <summary>Sending a scout drone over a zone from the world map (see <c>WorldMap.SendDrone</c>).</summary>
    public DroneScoutRules DroneScout { get; init; } = new();
}

/// <summary>
/// A scout drone flown over a zone from the colony: it reveals the zone's intel without anyone going. The battery limits
/// how far it reaches (<see cref="MaxTravelDays"/>); the flight takes <see cref="DaysPerTravelDay"/> days per travel day
/// each way, the drone then recharges for <see cref="RechargeDays"/>, and over a dangerous zone it may be shot down
/// (<see cref="LossByThreat"/>, less <see cref="LossReductionPerSkill"/> per Drone Piloting point of the pilot, relative).
/// </summary>
public sealed record DroneScoutRules
{
    public double MaxTravelDays { get; init; } = 2.5;
    public double DaysPerTravelDay { get; init; } = 0.5;
    public double RechargeDays { get; init; } = 1;

    /// <summary>Chance to lose the drone by zone threat (index = threat; the last entry covers anything higher).</summary>
    public double[] LossByThreat { get; init; } = [0, 0, 0.05, 0.15, 0.3];
    public double LossReductionPerSkill { get; init; } = 0.006;
    public double PilotSkillGain { get; init; } = 2;
    public double PilotXp { get; init; } = 10;
}

/// <summary>The kinds of hackable device a zone can hold.</summary>
public enum DeviceKind
{
    /// <summary>Hacked: its data (datashards, blueprints, manuals) and the zone's map intel.</summary>
    Terminal,
    /// <summary>A Network sensor that spots hostiles and alerts the Linked; hacked, it sees for the hacker's side.</summary>
    Camera,
    /// <summary>A fixed gun that shoots whatever is hostile to the side controlling it.</summary>
    Turret,
    /// <summary>A blast door: only the controlling side passes; hacked, a shortcut for you that locks enemies out.</summary>
    Door,
    /// <summary>A locked container; hacked, its loot spills out as a cache.</summary>
    Locker,
    /// <summary>The zone's uplink: hacked, it cuts the Linked around it off the Network and stops reinforcements.</summary>
    Relay,
}

/// <summary>
/// One device kind: how long a hack takes (<see cref="Work"/> progress points), how much harder than baseline
/// (<see cref="Difficulty"/>, off the chance), whether it is on the Network (harder, and a failed hack alerts the Linked
/// over it), the enemy definition of its unit for devices that act (camera, turret), loot for terminals and lockers, and
/// a radius (camera: how far its alert carries; relay: the jam field once hacked).
/// </summary>
public sealed record DeviceKindDef
{
    public int Work { get; init; } = 2;
    public double Difficulty { get; init; }
    public bool Networked { get; init; } = true;
    public string? Enemy { get; init; }
    public List<WeightedId> Loot { get; init; } = new();

    /// <summary>Lockers: also draw from the zone's own loot table.</summary>
    public bool ZoneLoot { get; init; }
    public int[] LootCount { get; init; } = [0, 0];
    public int Radius { get; init; }
}

/// <summary>
/// Hacking (<see cref="Hacking"/>): AP cost, the chance formula, what a failure does, skill and XP gains, and Override
/// (hacking a Linked unit, with the <c>can_hack_linked</c> perk).
/// </summary>
public sealed record HackingRules
{
    public int ApCost { get; init; } = 2;
    public int MinApCost { get; init; } = 1;

    /// <summary>chance = base + Hacking·perSkill + (Intellect − 50)·perIntellect + tool + perks − difficulty − Network penalty.</summary>
    public double BaseChance { get; init; } = 0.55;
    public double PerSkill { get; init; } = 0.005;
    public double PerIntellect { get; init; } = 0.004;
    public double MinChance { get; init; } = 0.05;
    public double MaxChance { get; init; } = 0.95;

    /// <summary>Taken off the chance for a device on the Network, unless a jammer hostile to the Network covers it.</summary>
    public double NetworkPenalty { get; init; } = 0.15;

    /// <summary>A failure is heard this far (less <c>hack_alarm_reduction</c>).</summary>
    public int AlarmRadius { get; init; } = 8;

    /// <summary>Rounds the side cannot try the device again after a failure.</summary>
    public int LockoutRounds { get; init; } = 3;

    /// <summary>Feedback shock damage [min, max] to the hacker's arm on a failure.</summary>
    public double[] ShockDamage { get; init; } = [4, 10];
    public double SkillOnSuccess { get; init; } = 2;
    public double SkillOnFailure { get; init; } = 0.75;
    public double XpOnSuccess { get; init; } = 8;
    public double XpOnFailure { get; init; } = 2;

    /// <summary>Override: harder than a device, and turns the Linked for this many rounds.</summary>
    public double OverrideDifficulty { get; init; } = 0.25;
    public int OverrideRounds { get; init; } = 2;
}

/// <summary>
/// The player's scout drone (<see cref="Drones"/>): a flying unit its operator flies with their own AP. It ignores
/// terrain, sees far, has little HP, and a battery counted in rounds; recalled, it recharges in the pack.
/// </summary>
public sealed record DroneRules
{
    public double Hp { get; init; } = 14;
    public Dictionary<AttributeKind, double> Attributes { get; init; } = new();

    /// <summary>Natural items of the flying unit (its frame: sight, dodge, stealth).</summary>
    public List<string> Equipment { get; init; } = new();
    public int DeployApCost { get; init; } = 1;
    public int FlyApCost { get; init; } = 1;
    public int RecallApCost { get; init; } = 1;
    public int MarkApCost { get; init; } = 1;
    public int PayloadApCost { get; init; } = 1;

    /// <summary>Control range grows by this per Drone Piloting point (25 → +1 at 0.04).</summary>
    public double ControlPerSkill { get; init; } = 0.04;

    /// <summary>Least battery (rounds) a drone needs to take off.</summary>
    public int MinBattery { get; init; } = 2;
    public int RechargePerRound { get; init; } = 1;

    /// <summary>A drone inside any working jammer's field drains this many rounds of battery per round and flies 1 tile.</summary>
    public int JammedDrain { get; init; } = 2;

    /// <summary>Rounds a mark lasts (the current one counts) and what it adds to the side's hit chance.</summary>
    public int MarkRounds { get; init; } = 2;
    public double MarkHitBonus { get; init; } = 0.1;
    public double SkillPerFlight { get; init; } = 0.4;
    public double XpPerFlight { get; init; } = 1;
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

    /// <summary>
    /// Sub-rounds in a round: every unit acts in each, in initiative order, on one turn's AP (refilled only when the round
    /// starts), and the tile attacks declared in a sub-round come down at its end (<see cref="TileStrikes"/>).
    /// </summary>
    public int SubRounds { get; init; } = 1;
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

    /// <summary>Arm efficiency (best arm, or both averaged for two-handed) below which arm-locked attacks cannot be used.</summary>
    public double ArmLockBelow { get; init; } = 0.5;

    /// <summary>How a landed hit resolves: graze, hit or solid (see <see cref="HitTierRules"/>).</summary>
    public HitTierRules Tiers { get; init; } = new();
}

/// <summary>
/// Hit tiers (scavenge.json <c>combat.tiers</c>). The margin of a landed hit is the share of the hit chance the roll came
/// in under (0 = barely, 1 = dead on): below <see cref="GrazeBelow"/> a graze, from <see cref="SolidFrom"/> a solid hit,
/// a plain hit between. Armor from <see cref="ArmorShiftFrom"/> and cover from <see cref="CoverShiftFrom"/> each shift
/// it down a tier, never below a graze.
/// </summary>
public sealed record HitTierRules
{
    public double GrazeBelow { get; init; } = 0.25;
    public double SolidFrom { get; init; } = 0.8;

    /// <summary>A graze does this share of the damage (at least 1) and injures no body part.</summary>
    public double GrazeDamage { get; init; } = 0.4;

    /// <summary>A solid hit does full damage and this many times the injury severity.</summary>
    public double SolidSeverity { get; init; } = 1.5;

    /// <summary>Armor on the hit region (after pierce) from which the hit shifts down a tier.</summary>
    public double ArmorShiftFrom { get; init; } = 4;

    /// <summary>Cover against the attacker (terrain plus brace) from which the hit shifts down a tier.</summary>
    public double CoverShiftFrom { get; init; } = 0.25;
}

/// <summary>
/// Fog of war beyond plain sight (scavenge.json <c>fog</c>): stealth against detection, contacts, ambushes, and the
/// defenses against them. See <see cref="Detection"/>.
/// </summary>
public sealed record FogRules
{
    /// <summary>Stealth = base + (Agility - 50) · perAgility - load · perKg + gear + (leg efficiency - 1) · legEfficiency.</summary>
    public StealthRules Stealth { get; init; } = new();

    /// <summary>Detection = (base + (Perception - 50) · perPerception + gear + stance bonuses) · head efficiency.</summary>
    public DetectionRules Detection { get; init; } = new();

    /// <summary>Within this Chebyshev distance a unit in sight is always seen, however stealthy.</summary>
    public int CloseRange { get; init; } = 1;

    /// <summary>Detection lost per tile of distance beyond <see cref="CloseRange"/>.</summary>
    public double PerTile { get; init; } = 2.5;

    /// <summary>Rounds a contact (a "?" for something heard, not seen) stays on a side's map.</summary>
    public int ContactRounds { get; init; } = 2;

    /// <summary>A contact is placed up to this many tiles off the real position (each axis).</summary>
    public int ContactFuzz { get; init; } = 1;

    /// <summary>Unseen hostiles moving within this distance (Chebyshev) of a side's unit become movement contacts.</summary>
    public int HearMovement { get; init; } = 3;

    /// <summary>
    /// A revealed unit (muzzle flash, motion sensor) is seen by an observer with line of sight out to this multiple of
    /// its sight: a flash carries further than a figure in the dark.
    /// </summary>
    public double RevealSightMultiplier { get; init; } = 1.5;

    /// <summary>Rounds a unit stays revealed to a side after tripping its motion sensor.</summary>
    public int SensorRevealRounds { get; init; } = 1;

    public AmbushRules Ambush { get; init; } = new();
    public OverwatchRules Overwatch { get; init; } = new();
    public BraceRules Brace { get; init; } = new();
    public LookoutRules Lookout { get; init; } = new();
    public FogAiRules Ai { get; init; } = new();
}

public sealed record StealthRules
{
    public double Base { get; init; } = 10;
    public double PerAgility { get; init; } = 0.3;
    public double PerKg { get; init; } = 1;

    /// <summary>Hurt or peg legs are loud; cybernetic legs above 1 are quieter.</summary>
    public double LegEfficiency { get; init; } = 20;
}

public sealed record DetectionRules
{
    public double Base { get; init; } = 30;
    public double PerPerception { get; init; } = 0.5;
}

/// <summary>An attack whose target's side cannot see the attacker when it resolves.</summary>
public sealed record AmbushRules
{
    public double HitBonus { get; init; } = 0.2;
    public double DamageMultiplier { get; init; } = 1.25;

    /// <summary>The target cannot dodge what it does not see coming.</summary>
    public bool IgnoreDodge { get; init; } = true;

    /// <summary>"Watching each other's backs": share of the bonuses removed when an ally stands next to the target.</summary>
    public double BacksReduction { get; init; } = 0.5;
}

/// <summary>
/// Overwatch stance: ends the turn; reacts to hostiles that move or reveal themselves in sight and reach. The AP left
/// after paying for it become reactions (at least one, at most <see cref="Reactions"/>).
/// </summary>
public sealed record OverwatchRules
{
    /// <summary>AP needed to go on overwatch.</summary>
    public int ApCost { get; init; } = 1;

    /// <summary>Most reactions one overwatch holds.</summary>
    public int Reactions { get; init; } = 2;
    public double AccuracyPenalty { get; init; } = 0.1;
    public double DetectionBonus { get; init; } = 10;
}

/// <summary>Brace / hunker: ends the turn; no ambush bonus against the unit, extra cover, fewer AP next turn.</summary>
public sealed record BraceRules
{
    public int ApCost { get; init; } = 1;
    public double CoverBonus { get; init; } = 0.15;
    public int NextTurnApPenalty { get; init; } = 1;
}

/// <summary>Lookout / scan: spend AP for sight and detection until the unit's next turn.</summary>
public sealed record LookoutRules
{
    public int ApCost { get; init; } = 1;
    public double SightBonus { get; init; } = 2;
    public double DetectionBonus { get; init; } = 15;
    public double DetectionPerPerception { get; init; } = 0.2;
}

/// <summary>How the Linked use the fog.</summary>
public sealed record FogAiRules
{
    /// <summary>Position scoring: value of a tile no known hostile would detect the unit on.</summary>
    public double HiddenWeight { get; init; } = 4;

    /// <summary>A Linked that knows of nobody waits on overwatch instead of wandering.</summary>
    public bool LinkedLurk { get; init; } = true;
}

/// <summary>
/// Co-op between players (scavenge.json <c>coop</c>): alliances, team pings, handing items over, carrying the downed,
/// and how the zone scales with the number of players.
/// </summary>
public sealed record CoopRules
{
    /// <summary>Rounds an alliance offer stays open.</summary>
    public int ProposalRounds { get; init; } = 2;

    /// <summary>Full rounds of warning between announcing a break and the teams splitting.</summary>
    public int BreakWarningRounds { get; init; } = 1;

    public int PingsPerTurn { get; init; } = 2;

    /// <summary>Rounds a ping stays on the team's maps (the round it was made counts).</summary>
    public int PingRounds { get; init; } = 2;

    public int GiveApCost { get; init; } = 1;

    /// <summary>AP to lift an adjacent downed unit; putting it down is free.</summary>
    public int CarryApCost { get; init; } = 1;

    /// <summary>Movement range lost while carrying (never below 1), and no special moves (jumps, grapples).</summary>
    public int CarryRangePenalty { get; init; } = 1;

    /// <summary>Enemies in a zone × (1 + this × (players − 1)); per-type caps scale the same (rounded up).</summary>
    public double EnemiesPerExtraPlayer { get; init; } = 1.0;
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

/// <summary>A raised tile (a watchtower on the colony perimeter) adds this much sight and detection to whoever stands on it.</summary>
public sealed record StoryFieldRules
{
    public double RaisedSight { get; init; } = 3;
    public double RaisedDetection { get; init; } = 10;
}

public sealed record ScavengeRules
{
    /// <summary>Encounters each bestiary tier needs (see <see cref="Scavenge.Bestiary"/>).</summary>
    public BestiaryRules Bestiary { get; init; } = new();
    public Dictionary<Terrain, TerrainDef> Terrain { get; init; } = new();
    public MovementRules Movement { get; init; } = new();
    public TurnRules Turn { get; init; } = new();
    public SightRules Sight { get; init; } = new();
    public CarryRules Carry { get; init; } = new();
    public CombatRules Combat { get; init; } = new();
    public AiRules Ai { get; init; } = new();
    public NpcRules Npc { get; init; } = new();
    public NetworkRules Network { get; init; } = new();
    public FogRules Fog { get; init; } = new();
    public CoopRules Coop { get; init; } = new();
    public WorldRules World { get; init; } = new();
    public HackingRules Hacking { get; init; } = new();

    /// <summary>Hackable device kinds (terminal, camera, turret, door, locker, relay node).</summary>
    public Dictionary<DeviceKind, DeviceKindDef> Devices { get; init; } = new();
    public DroneRules Drone { get; init; } = new();

    /// <summary>Time of day and weather in the field (see <see cref="FieldEnvironment"/>).</summary>
    public EnvironmentRules Environment { get; init; } = new();

    /// <summary>Followers within this many tiles leave with the unit that extracts.</summary>
    public int EscortRadius { get; init; } = 2;

    /// <summary>Story missions in the field (raised watchtower tiles).</summary>
    public StoryFieldRules Story { get; init; } = new();
    /// <summary>Parked vehicles in the field (see <see cref="ParkedVehicles"/>).</summary>
    public VehicleFieldRules Vehicle { get; init; } = new();

    /// <summary>Stripping wrecked cars for vehicle parts (see <see cref="Salvaging"/>).</summary>
    public WreckSalvageRules Salvage { get; init; } = new();

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

/// <summary>
/// A parked vehicle under attack: each hostile enemy next to it at the start of a round deals
/// <see cref="DamagePerEnemy"/> less the vehicle's armor, but at least <see cref="MinDamage"/>.
/// </summary>
public sealed record VehicleFieldRules
{
    public double DamagePerEnemy { get; init; } = 6;
    public double MinDamage { get; init; } = 1;

    /// <summary>HP a parked vehicle with a mobile workbench mends each round.</summary>
    public double WorkbenchRepairPerRound { get; init; } = 2;

    /// <summary>A multiplayer server refuses a joining vehicle with more HP or cargo than any tier can have.</summary>
    public double MaxHp { get; init; } = 250;
    public double MaxCargoKg { get; init; } = 300;
    public double MaxTurretDamage { get; init; } = 15;
}
