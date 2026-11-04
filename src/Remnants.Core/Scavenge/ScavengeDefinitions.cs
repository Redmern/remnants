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

/// <summary>Where an enemy puts a blow: <see cref="EnemyAim"/>.</summary>
public enum AimKind { Current, Closest, Lead }

/// <summary>What sets off a habit, all of it what the enemy can see: <see cref="EnemyHabits"/>.</summary>
public enum HabitWhen { Always, Hurt, Threatened, AfterGuard, Cornered, FlankOpen }

/// <summary>What a habit does. Strike ends the list: the unit goes on to its usual close and blow.</summary>
public enum HabitDo { Strike, Defend, Push, Dash }

public sealed record EnemyHabit
{
    public HabitWhen When { get; init; }
    public HabitDo Do { get; init; }
    /// <summary>For <see cref="HabitWhen.Hurt"/>: at under this share of its HP.</summary>
    public double HpBelow { get; init; } = 0.5;
}

/// <summary>A type's aim and habits, first match wins, and <see cref="Read"/>, the same thing in words for the player.</summary>
public sealed record EnemyTactics
{
    public AimKind Aim { get; init; }
    /// <summary><see cref="AimKind.Closest"/>: with the target this near (and out of reach), swing at the next tile toward it.</summary>
    public int SwingRange { get; init; } = 2;
    /// <summary><see cref="AimKind.Lead"/>: how many tiles ahead of a target that moved last round.</summary>
    public int LeadTiles { get; init; } = 2;
    public List<EnemyHabit> Habits { get; init; } = new();
    public string Read { get; init; } = "";
}

public sealed record EnemyDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double Hp { get; init; } = 50;
    public AiKind Ai { get; init; }

    /// <summary>Per-type overrides of the round pools (<see cref="TurnRules"/>); null: as any unit.</summary>
    public int? Mp { get; init; }
    public int? Ap { get; init; }
    public int? Stamina { get; init; }

    /// <summary>The share of a stun's chance this type shrugs off, and the adrenaline it starts a mission with.</summary>
    public double StunResist { get; init; }
    public double AdrenalineStart { get; init; }

    /// <summary>Survivors go Downed at 0 HP; most enemies just die.</summary>
    public bool CanBeDowned { get; init; }

    /// <summary>Linked: how far (Chebyshev) this unit links to other Network nodes. Drones relay further.</summary>
    public int RelayRange { get; init; }

    /// <summary>
    /// An authored enemy's (a boss's, a named squad's) cards: they join the enemy side's deck (<see cref="EnemyCards"/>).
    /// Most enemies have none.
    /// </summary>
    public List<string> Cards { get; init; } = new();

    /// <summary>How this type aims and what it does besides strike; null: it aims where the target stands and only strikes.</summary>
    public EnemyTactics? Tactics { get; init; }

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

    /// <summary>
    /// Building materials a party brings home from a plain run besides what it carried (timber, stone, scrap gathered on
    /// the way): colony resource id → [min, max], rolled per run and scaled by (1 − depletion). Early zones pay this
    /// generously so scavenging is the early answer to a slow Woodcutter (BALANCE.md "Early resource scarcity").
    /// </summary>
    public Dictionary<string, int[]> Haul { get; init; } = new();

    /// <summary>
    /// Colony days the haul takes to come back after one is taken: a run sooner gets elapsed / this of it (so runs
    /// chained on one day, possible where the road takes no time, haul next to nothing).
    /// </summary>
    public double HaulRecoveryDays { get; init; } = 1;

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

    /// <summary>Below this hit chance a Linked closes in first, and shoots only when it can get no closer.</summary>
    public double CloseInBelowHitChance { get; init; } = 0.3;

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

/// <summary>
/// Stun (<see cref="Stuns"/>, scavenge.json <c>stun</c>): what it costs next round; the chance a block stuns, base +
/// (block - power) * <see cref="PerBlock"/> with power = average damage * <see cref="PowerPerDamage"/>, within
/// <see cref="Min"/>..<see cref="Max"/>; <see cref="RepeatFactor"/> per earlier stun; stamina the parties lose.
/// </summary>
public sealed record StunRules
{
    public int ApLoss { get; init; } = 1;
    public int MpLoss { get; init; } = 2;
    public double PriorityPenalty { get; init; } = 3;
    public double Base { get; init; } = 0.15;
    public double PerBlock { get; init; } = 0.5;
    public double PowerPerDamage { get; init; } = 0.02;
    public double PerfectBonus { get; init; } = 0.15;
    public double Min { get; init; } = 0.05;
    public double Max { get; init; } = 0.6;
    public double RepeatFactor { get; init; } = 0.6;
    public int BlockedStamina { get; init; } = 1;
    public int StunnedStamina { get; init; } = 2;
    public int DefenderStamina { get; init; } = 1;
}

/// <summary>Adrenaline (<see cref="Adrenaline"/>, scavenge.json <c>adrenaline</c>): tiers, the share each adds, and what moves the meter.</summary>
public sealed record AdrenalineRules
{
    public double Max { get; init; } = 100;
    public double[] Tiers { get; init; } = [25, 50, 75, 100];
    public double[] TierBonus { get; init; } = [0.05, 0.1, 0.15, 0.3];

    /// <summary>Priority a bonus of 1.0 would add.</summary>
    public double PriorityPoints { get; init; } = 10;
    public double GainHit { get; init; } = 10;
    public double GainPush { get; init; } = 20;
    public double GainDash { get; init; } = 5;
    public double GainBlock { get; init; } = 10;
    public double GainStun { get; init; } = 15;
    public double DrainPerRound { get; init; } = 5;
    public double DrainFailed { get; init; } = 15;
    public double DrainStunned { get; init; } = 15;
    public double DrainHeavyHit { get; init; } = 15;

    /// <summary>A hit for at least this share of max HP is a heavy one.</summary>
    public double HeavyHitShare { get; init; } = 0.25;
}

/// <summary>The push action (<see cref="Pushes"/>, scavenge.json <c>combat.push</c>): its tiles and how far, and what a collision does to who is pushed and who is in the way.</summary>
public sealed record PushRules
{
    public int ApCost { get; init; } = 1;
    public int Tiles { get; init; } = 1;
    public CardPattern Pattern { get; init; } = new() { Shape = PatternShape.Stab };
    public double CollisionDamage { get; init; } = 4;
    public double BlockerDamage { get; init; } = 2;
}

/// <summary>The dash action (<see cref="Pushes"/>, scavenge.json <c>combat.dash</c>); <see cref="AttackHit"/> is what the run-up adds to a dash card's blow.</summary>
public sealed record DashRules
{
    public int ApCost { get; init; } = 1;
    public int Tiles { get; init; } = 3;
    public double AttackHit { get; init; } = 0.1;
}

/// <summary>The defend action (<see cref="Defences"/>, scavenge.json <c>combat.defend</c>): its AP, and the share and pattern of a unit with no guarding gear.</summary>
/// <summary>The chance a landed blow is a crit, what the side and the rear add to it, and what a crit multiplies damage by (inside the damage cap).</summary>
public sealed record CritRules
{
    public double Chance { get; init; } = 0.05;
    public double Side { get; init; } = 0.05;
    public double Rear { get; init; } = 0.15;
    public double Damage { get; init; } = 1.5;
}

/// <summary>
/// How the bots play (scavenge.json <c>bot</c>): how often a healthy one guards against a neighbour that stood still, the health share under
/// which it stops trading blows, what each seen reach takes off a tile it might stand on, and how much better a tile must be to walk there.
/// </summary>
public sealed record BotRules
{
    public int GuardEvery { get; init; } = 3;
    public double HurtBelow { get; init; } = 0.5;
    public double PositionRisk { get; init; } = 1;
    public double PositionGain { get; init; } = 1.05;
}

/// <summary>What an enemy pays next round for a blow or push on an empty tile.</summary>
public sealed record ExposedRules
{
    public double PriorityPenalty { get; init; } = 3;
    public int Stamina { get; init; } = 2;
}

public sealed record DefendRules
{
    public int ApCost { get; init; } = 1;
    public double Block { get; init; } = 0.2;
    public CardPattern Pattern { get; init; } = new() { Offsets = [[0, 0], [1, 0]] };
}

/// <summary>What a defended tile takes off a hit, beyond its card (<see cref="Defences"/>, scavenge.json <c>combat.block</c>).</summary>
public sealed record BlockRules
{
    public Skill Skill { get; init; } = Skill.Melee;
    public double PerSkill { get; init; } = 0.002;
    public double PerArmor { get; init; } = 0.01;
    public double Max { get; init; } = 0.9;
}

/// <summary>
/// The resolve order (<see cref="Priority"/>, scavenge.json <c>priority</c>): what speed and stamina are worth, how steep the
/// fall below the low-stamina cliff is, what kinds of action add, and what is left of an action tied with a hostile's.
/// </summary>
public sealed record PriorityRules
{
    public double SpeedPerInitiative { get; init; } = 0.1;
    public double StaminaWeight { get; init; } = 10;
    public double CliffBelow { get; init; } = 0.3;
    public double CliffPenalty { get; init; } = 6;
    public double QuickWeight { get; init; } = 2;
    public double HeavyWeight { get; init; } = -2;
    public double EarlyWeight { get; init; } = 4;

    /// <summary>A melee blow in one tile's reach doing at most this much is a quick one.</summary>
    public double LightMaxDamage { get; init; } = 12;

    /// <summary>What a card that hurries a unit along or holds it up shifts its score by, for the round.</summary>
    public double CardShift { get; init; } = 3;
    public double TieEffect { get; init; } = 0.6;
}

public sealed record TurnRules
{
    public int BaseAp { get; init; } = 1;

    /// <summary>What a unit may plan a round before cards and perks: moves, actions, cards (<see cref="Plans.Limits"/>).</summary>
    public int PlanMoves { get; init; } = 1;

    /// <summary>How far from its leader a recruit lets itself get before it walks: keeps it from stepping back into a doorway the leader needs.</summary>
    public int FollowWithin { get; init; } = 1;
    public int PlanActions { get; init; } = 1;
    public int PlanCards { get; init; } = 1;

    /// <summary>MP a round for a unit of baseline movement range; each step of range above or below it shifts this by <see cref="MpPerRange"/>.</summary>
    public int BaseMp { get; init; } = 4;
    public int MpPerRange { get; init; } = 2;
    public int BaseStamina { get; init; } = 10;
    public int StaminaPerMp { get; init; } = 1;
    public int StaminaPerAp { get; init; } = 2;

    /// <summary>Share of its maximum stamina a unit regains at a round start, less <see cref="RegenLoadPenalty"/> times how full its pack is, at least <see cref="MinStaminaRegen"/>.</summary>
    public double StaminaRegen { get; init; } = 0.4;
    public double RegenLoadPenalty { get; init; } = 2;
    public int MinStaminaRegen { get; init; } = 1;

    /// <summary>The recover action: its AP (which costs no stamina) and the stamina it gives back.</summary>
    public int RecoverApCost { get; init; } = 1;
    public int RecoverStamina { get; init; } = 4;

    /// <summary>What a landed blow on a recovering unit adds to its damage, as a share; it also calls the recover off.</summary>
    public double RecoverHitDamage { get; init; } = 0.25;
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

    /// <summary>Arm efficiency (best arm, or both averaged for two-handed) below which arm-locked attacks cannot be used.</summary>
    public double ArmLockBelow { get; init; } = 0.5;

    /// <summary>How a landed hit resolves: graze, hit or solid (see <see cref="HitTierRules"/>).</summary>
    public HitTierRules Tiers { get; init; } = new();

    /// <summary>A landed blow may crit, more so from the side and from behind (see <see cref="CritRules"/>).</summary>
    public CritRules Crit { get; init; } = new();

    /// <summary>Blows from the side and from behind land harder (see <see cref="FacingRules"/>).</summary>
    public FacingRules Facing { get; init; } = new();

    /// <summary>The most the stacked damage multipliers of a blow (ambush × facing × exposed) may come to.</summary>
    public double MaxDamageMultiplier { get; init; } = 2;

    public BlockRules Block { get; init; } = new();
    public DefendRules Defend { get; init; } = new();
    public ExposedRules Exposed { get; init; } = new();
    public PushRules Push { get; init; } = new();
    public DashRules Dash { get; init; } = new();

}

/// <summary>
/// Facing in combat (scavenge.json <c>combat.facing</c>): a landed blow's damage (before armor) is multiplied by
/// <see cref="SideDamage"/> when the attacker stands off the target's flank, by <see cref="RearDamage"/> when behind it
/// (<see cref="Scavenge.Combat.ArcOf"/>).
/// </summary>
public sealed record FacingRules
{
    public double SideDamage { get; init; } = 1.25;
    public double RearDamage { get; init; } = 1.5;
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

    /// <summary>Cover against the attacker (terrain plus cards) from which the hit shifts down a tier.</summary>
    public double CoverShiftFrom { get; init; } = 0.25;
}

/// <summary>
/// Fog of war beyond plain sight (scavenge.json <c>fog</c>): stealth against detection, contacts, ambushes, and the
/// defenses against them. See <see cref="Detection"/>.
/// </summary>
public sealed record FogRules
{
    /// <summary>A fogged walk ending within this many tiles of the side gives a hint: the map cell of this size it ends in, no finer.</summary>
    public int MoveHintRange { get; init; } = 12;
    public int MoveHintCell { get; init; } = 4;

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

    /// <summary>How the bots play (<see cref="BotTactics"/>): habits, not rules of the game.</summary>
    public BotRules Bot { get; init; } = new();
    public PriorityRules Priority { get; init; } = new();
    public StunRules Stun { get; init; } = new();
    public AdrenalineRules Adrenaline { get; init; } = new();
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
