using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Raids;

/// <summary>Who comes for the colony. Each kind has its own pressure, approach and consequences.</summary>
public enum ThreatKind { Husk, Linked, Raider }

/// <summary>Edges of the defense grid: North is −y (away from the default camera), South +y (the road and the main gate).</summary>
public enum Side { North, East, South, West }

/// <summary>How a defense works: structures stand and soak, traps spring once per raid, posts hold guards.</summary>
public enum DefenseKind { Wall, Gate, Barricade, Post, Tower, Light, Turret, Alarm, Trap }

/// <summary>
/// The colony defense layer and raid model (data/raids.json). All numbers are balance; Core reads them only through
/// <see cref="RaidData"/>. See ARCHITECTURE.md "Raids and defenses" and BALANCE.md "Raids".
/// </summary>
public sealed record RaidsDef
{
    public GridRules Grid { get; init; } = new();
    public SchedulerRules Scheduler { get; init; } = new();
    public Dictionary<ThreatKind, ThreatDef> Threats { get; init; } = new();
    public NoiseRules Noise { get; init; } = new();
    public WarningRules Warning { get; init; } = new();
    public Dictionary<DayPhase, PhaseModifiers> Phases { get; init; } = new();

    /// <summary>Weather id → modifiers. The colony has no weather yet: <see cref="RaidWeather.Provider"/> supplies it once it does.</summary>
    public Dictionary<string, WeatherModifiers> Weather { get; init; } = new();
    public StrengthRules Strength { get; init; } = new();
    public ConsequenceRules Consequences { get; init; } = new();
    public GuardRules Guard { get; init; } = new();
    public RepairRules Repair { get; init; } = new();
    public List<DefenseDef> Defenses { get; init; } = new();
}

/// <summary>
/// The defense grid: the plot grid at <see cref="CellsPerPlot"/> cells per plot side plus a <see cref="Margin"/> of cells
/// all round. A cell is one scavenge tile (<see cref="CellSize"/> m), so the grid maps 1:1 onto a tactical map.
/// </summary>
public sealed record GridRules
{
    public int CellsPerPlot { get; init; } = 4;
    public int Margin { get; init; } = 4;
    public double CellSize { get; init; } = 2;
}

/// <summary>
/// When raids come. Pressure per threat builds every hour (<see cref="ThreatDef"/>); a raid is drawn once a threat's pressure
/// reaches <see cref="Threshold"/>, never during the grace period and never closer than <see cref="MinGapDays"/> to the last.
/// It sets out <see cref="TravelHours"/> before it arrives; the warning shows when the watch spots it (lead time).
/// </summary>
public sealed record SchedulerRules
{
    /// <summary>No raid before this day ...</summary>
    public int GraceDays { get; init; } = 6;

    /// <summary>... nor before the colony has this many buildings (the basics are up).</summary>
    public int GraceBuildings { get; init; } = 5;

    public double Threshold { get; init; } = 100;
    public double MinGapDays { get; init; } = 3;
    public int TravelHours { get; init; } = 24;

    /// <summary>Size grows by this share per raid survived (escalation) and per day since the grace ended.</summary>
    public double EscalationPerRaid { get; init; } = 0.15;
    public double EscalationPerDay { get; init; } = 0.01;

    /// <summary>The first raid is capped at this size, whatever the pressure.</summary>
    public int FirstRaidMaxSize { get; init; } = 3;

    /// <summary>A raid at or below this attack/defense ratio is "small" (auto-resolve is offered as the default).</summary>
    public double SmallRaidRatio { get; init; } = 1.0;
}

public sealed record ThreatDef
{
    public string Name { get; init; } = "";

    /// <summary>Enemy ids (enemies.json) a raid of this kind is made of, in order of how common they are.</summary>
    public List<string> Enemies { get; init; } = new();

    /// <summary>Pressure per hour from the start, before inputs.</summary>
    public double BasePerHour { get; init; }

    /// <summary>Pressure per hour per point of the threat's input (noise for Husks, Network presence for the Linked, wealth for raiders).</summary>
    public double PerInputPerHour { get; init; }

    /// <summary>First day this kind can raid at all.</summary>
    public int MinDay { get; init; }

    public int MinSize { get; init; } = 2;
    public int MaxSize { get; init; } = 12;

    /// <summary>Size per 100 pressure at the moment the raid is drawn.</summary>
    public double SizePerPressure { get; init; } = 3;

    /// <summary>Attack strength per attacker.</summary>
    public double StrengthPerUnit { get; init; } = 6;

    /// <summary>Weight of each approach side (missing = 1). Raiders like the road (South).</summary>
    public Dictionary<Side, double> Approach { get; init; } = new();

    /// <summary>Weight of arriving in each phase (missing = 1). Husks favour night.</summary>
    public Dictionary<DayPhase, double> ArrivalPhase { get; init; } = new();

    /// <summary>Trap tags this kind trips (a trap hits it when one of its <see cref="DefenseDef.Hits"/> tags is listed here).</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>Raiders: share of the stockpile they can carry off at worst. Husks: share of food trampled. Linked: none.</summary>
    public double SpoilShare { get; init; }

    /// <summary>Resources they go for first (raiders), or the one they trample (Husks: food).</summary>
    public List<string> SpoilResources { get; init; } = new();

    /// <summary>Linked: at worst this many survivors are carried off (they are lost like the dead, but "taken").</summary>
    public int MaxTaken { get; init; }

    /// <summary>Items a beaten raid leaves behind: item id → count per attacker killed (fractions roll).</summary>
    public Dictionary<string, double> Loot { get; init; } = new();

    /// <summary>Chance per beaten raid of a defense schematic the colony lacks (see <see cref="DefenseSchematics"/>).</summary>
    public double SchematicChance { get; init; }
}

/// <summary>Husks hear the colony: what each thing adds to its noise (the Husk threat's input).</summary>
public sealed record NoiseRules
{
    public double PerSurvivor { get; init; } = 0.5;
    public double PerNightShift { get; init; } = 2;

    /// <summary>A lit Floodlight (building or defense) at night.</summary>
    public double PerFloodlight { get; init; } = 1.5;

    /// <summary>Each expedition that comes home adds this for a day (gunfire on the way back).</summary>
    public double PerReturn { get; init; } = 4;
    public int ReturnNoiseHours { get; init; } = 24;

    /// <summary>Items that are noisy while the colony owns them (vehicles, once that branch lands): item id → noise.</summary>
    public Dictionary<string, double> Items { get; init; } = new();

    /// <summary>Noise-maker lures pull Husk pressure away: noise removed per armed lure.</summary>
    public double PerLure { get; init; } = 3;
}

/// <summary>
/// Lead time: how many hours before a raid arrives the colony sees it coming, from the best watch it has. Sources add up
/// to <see cref="MaxHours"/>; weather multiplies. The size estimate's error shrinks as the lead grows.
/// </summary>
public sealed record WarningRules
{
    public int BaseHours { get; init; } = 1;
    public int MaxHours { get; init; } = 24;

    /// <summary>Plot buildings that watch (e.g. a Watchtower or Radio Mast once that data lands): id → hours per level.
    /// Ids not in buildings.json are ignored, so the entry lights up when the building arrives.</summary>
    public Dictionary<string, double> BuildingHoursPerLevel { get; init; } = new();

    /// <summary>Each guard on duty at a post adds this, up to <see cref="MaxGuardHours"/>.</summary>
    public double PerGuardHours { get; init; } = 2;
    public double MaxGuardHours { get; init; } = 6;

    /// <summary>A scout drone in the inventory and someone home with Drone Piloting ≥ <see cref="DronePilotSkill"/>: patrols.</summary>
    public string DroneItem { get; init; } = "scout_drone";
    public double DronePilotSkill { get; init; } = 10;
    public double DroneHours { get; init; } = 6;

    /// <summary>Error of the size estimate (± share) with no warning; it falls linearly to <see cref="MinError"/> at max lead.</summary>
    public double MaxError { get; init; } = 0.6;
    public double MinError { get; init; } = 0.1;
}

public sealed record PhaseModifiers
{
    /// <summary>Per threat: attack strength multiplier at this time of day.</summary>
    public Dictionary<ThreatKind, double> Attack { get; init; } = new();

    /// <summary>Defenders' strength multiplier (night: they cannot see). Lit floodlights cancel <see cref="LitRecovers"/> of the loss.</summary>
    public double Defense { get; init; } = 1;
    public double LitRecovers { get; init; } = 0.75;

    /// <summary>Defenders who are asleep when it starts fight at this share unless an alarm wakes them.</summary>
    public double AsleepShare { get; init; } = 1;
}

public sealed record WeatherModifiers
{
    public double Warning { get; init; } = 1;
    public Dictionary<ThreatKind, double> Pressure { get; init; } = new();
    public Dictionary<ThreatKind, double> Attack { get; init; } = new();
    public double Defense { get; init; } = 1;
}

/// <summary>
/// Auto-resolve. Defense = Σ defenders + structures + traps' kills, attack = attackers × strength; see <see cref="AutoResolve"/>.
/// </summary>
public sealed record StrengthRules
{
    /// <summary>A defender with average stats and no gear.</summary>
    public double PerDefender { get; init; } = 6;

    /// <summary>Share of a defender's strength from combat skill (best of Firearms and Melee, 0..100).</summary>
    public double SkillWeight { get; init; } = 0.6;

    /// <summary>Per point of weapon power (average damage × accuracy) and per point of armor.</summary>
    public double PerWeaponPower { get; init; } = 0.6;
    public double PerArmor { get; init; } = 0.8;

    /// <summary>A guard at a post fights this much better (the post's own bonus adds on top).</summary>
    public double GuardBonus { get; init; } = 0.25;

    /// <summary>Gates left open let attackers past the walls: this share of the walls' soak is lost.</summary>
    public double OpenGateLoss { get; init; } = 0.5;

    /// <summary>Walls soak at most this share of the attack.</summary>
    public double MaxSoakShare { get; init; } = 0.6;

    /// <summary>Soak per point of wall strength relative to attack (soak share = s / (s + attack · soakScale)).</summary>
    public double SoakScale { get; init; } = 1;

    /// <summary>Defenses on the far side of the colony help at this share (they reposition late).</summary>
    public double FarSideShare { get; init; } = 0.5;

    /// <summary>Recruitable defenders: adults at home, not patients. Each child unsheltered is a risk, never a fighter.</summary>
    public double ChildRisk { get; init; } = 0.5;
}

/// <summary>What a raid costs. The pressure p = attack / (attack + defense) after soak and traps; 0.5 is a draw.</summary>
public sealed record ConsequenceRules
{
    /// <summary>Damage to structures and buildings per point of the attack that got through, split over targets.</summary>
    public double DamagePerAttack { get; init; } = 1.5;

    /// <summary>Spoils (stolen or trampled) scale from 0 at p = <see cref="SpoilFrom"/> to the full share at p = 1.</summary>
    public double SpoilFrom { get; init; } = 0.35;

    /// <summary>Injuries: each defender is hurt with chance p² · this; severity 0.2..0.2+p·0.6.</summary>
    public double InjuryChance { get; init; } = 0.8;

    /// <summary>A hurt defender dies with chance (p − this)·2 when p is above it (a lost fight).</summary>
    public double DeathFrom { get; init; } = 0.6;

    public double WinMood { get; init; } = 8;
    public double LossMood { get; init; } = 12;

    /// <summary>A building at or below this share of its HP is wrecked (rubble): no work there until repaired.</summary>
    public double RubbleAt { get; init; } = 0;

    /// <summary>Building HP per level (index 0 = level 1).</summary>
    public List<double> BuildingHp { get; init; } = [60, 100, 150];
}

/// <summary>
/// The Guard job. Guards stand at posts (sandbag nests, towers) on the day shift (<c>dayNight.workHours</c>) or on night watch (the rest). Night watch counts as night work
/// (sleep debt, mood); the watch sleeps in the day. Guards train like workers at the <see cref="Job"/> (Firearms, Perception) and earn work XP.
/// </summary>
public sealed record GuardRules
{
    /// <summary>Hours a night-watch guard sleeps in the day, off duty (paid back like night sleep).</summary>
    public int[] DaySleepHours { get; init; } = [8, 16];

    /// <summary>The job (jobs.json) guards practise: its skill, attributes and side skills train like any work, with work XP.</summary>
    public string Job { get; init; } = "guard";
}

/// <summary>Repairs: idle adults at home on their day shift work the repair queue in order; materials are paid up front.</summary>
public sealed record RepairRules
{
    /// <summary>HP per tick an idle adult repairs (at 50 Crafting; scales 0.5..1.5 with the skill).</summary>
    public double HpPerWorkerTick { get; init; } = 4;

    /// <summary>HP per tick everyone chips in by day, even with nobody idle.</summary>
    public double BaseHpPerTick { get; init; } = 1;

    /// <summary>A building's repair cost = this share of its current level's cost × the share of HP missing.</summary>
    public double BuildingCostShare { get; init; } = 0.5;

    /// <summary>Removing a defense refunds this share of its cost.</summary>
    public double RemoveRefund { get; init; } = 0.5;
}

/// <summary>
/// One defense structure or trap (data/raids.json "defenses"). Walls, gates and barricades soak; posts and towers take
/// guards; lights and turrets cover a radius (a turret an arc around its facing); traps hit what they are tagged for.
/// </summary>
public sealed record DefenseDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public DefenseKind Kind { get; init; }
    public string Description { get; init; } = "";

    /// <summary>The model id (defense_{id} by convention; the game falls back by kind).</summary>
    public string? Model { get; init; }
    public int MinTownHallLevel { get; init; } = 1;
    public Dictionary<string, double> Cost { get; init; } = new();

    /// <summary>Resources per day to keep it working (turret ammo, lamp fuel). Unpaid: it stands but does nothing that day.</summary>
    public Dictionary<string, double> Upkeep { get; init; } = new();
    public double Hp { get; init; } = 50;

    /// <summary>Walls, gates, barricades: soak strength (times the HP share left).</summary>
    public double Soak { get; init; }

    /// <summary>Flat defense strength it adds (towers, nests, turrets).</summary>
    public double Defense { get; init; }

    /// <summary>Extra lead time in hours (towers, alarms, tripwires).</summary>
    public double WarningHours { get; init; }

    /// <summary>Guards it takes (posts and towers).</summary>
    public int GuardSlots { get; init; }

    /// <summary>A guard posted here fights this much better on top of <see cref="StrengthRules.GuardBonus"/>.</summary>
    public double PostBonus { get; init; }

    /// <summary>Radius in cells it covers (fire, light, alarm). 0 = its own cell.</summary>
    public int Range { get; init; }

    /// <summary>Turrets: the arc in degrees around its facing (360 = all round).</summary>
    public int Arc { get; init; } = 360;

    /// <summary>Lights: lit at night (cancels the night penalty on its side).</summary>
    public bool Lights { get; init; }

    /// <summary>Alarm: sleepers wake in time (no asleep penalty).</summary>
    public bool Wakes { get; init; }

    /// <summary>Traps: attackers' strength removed when it springs, against the tags it hits.</summary>
    public double TrapDamage { get; init; }
    public List<string> Hits { get; init; } = new();

    /// <summary>Traps: re-armable (true: it stays, sprung, until <see cref="RearmCost"/> is paid) or used up (false: gone).</summary>
    public bool Rearmable { get; init; }
    public Dictionary<string, double> RearmCost { get; init; } = new();

    /// <summary>Noise-maker: lures Husks (pressure off, and a raid's Husks are drawn to it).</summary>
    public bool Lure { get; init; }

    /// <summary>The next tier this upgrades into (wood wall → scrap wall), or a tower's next level: paid as the difference in cost.</summary>
    public string? UpgradesTo { get; init; }

    /// <summary>Optional power bonus (see <see cref="DefensePower"/>): defense, range and soak multipliers when powered.</summary>
    public double PoweredBonus { get; init; }

    /// <summary>Blocks movement in a tactical map (walls, closed gates, barricades, nests).</summary>
    public bool Blocks => Kind is DefenseKind.Wall or DefenseKind.Gate or DefenseKind.Barricade;
    public bool IsTrap => Kind == DefenseKind.Trap;
}

/// <summary>Stat helpers the threat model and auto-resolve share.</summary>
internal static class Stats
{
    public static double Combat(Survivor s) => Math.Max(s.Get(Skill.Firearms), s.Get(Skill.Melee));
}
