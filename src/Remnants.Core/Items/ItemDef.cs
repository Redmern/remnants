using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Items;

public enum ItemKind { Weapon, Armor, Clothing, Tool, Consumable, Material, Prosthetic, Schematic, Food, Mod, Headgear }

/// <summary>Where an item sits when equipped. Anything not equipped is carried in the pack.</summary>
public enum EquipSlot { Weapon, Body, Tool, Head }

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

/// <summary>
/// How a melee weapon strikes a tile (see <c>Combat.Covered</c>): a stab is a straight line of the weapon's reach in the
/// direction of the attack (<see cref="ActionDef.DiagonalReach"/> along a diagonal), a slash the tile in front and its
/// two flanking tiles.
/// </summary>
public enum StrikeMode { Stab, Slash }

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

    /// <summary>Reach along a diagonal (a spear's 3 straight is 2 diagonal); null: the same as <see cref="Reach"/>.</summary>
    public int? DiagonalReach { get; init; }

    /// <summary>The ways the weapon strikes tiles, the first the default; empty: it strikes the one tile aimed at.</summary>
    public List<StrikeMode> Modes { get; init; } = new();

    /// <summary>Tiles a landed hit throws the target straight back (a displacement, like a shove); 0: none.</summary>
    public int Knockback { get; init; }
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

    /// <summary>Unusable while the arms are badly hurt (see <c>ProfileDeriver.ArmsLocked</c>).</summary>
    public bool ArmLock { get; init; }

    /// <summary>A slow blow: it comes down late in the action phase (<see cref="Scavenge.PriorityRules.HeavyWeight"/>).</summary>
    public bool Heavy { get; init; }

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

    /// <summary>Armor on the target counts for this much less (spikes; gear mods).</summary>
    public double ArmorPierce { get; init; }

    /// <summary>Injury severity a hit causes × this (a serrated edge's wounds; gear mods).</summary>
    public double InjuryMultiplier { get; init; } = 1;

    /// <summary>Within this many tiles (Chebyshev) the attack costs <see cref="CloseApPenalty"/> more AP (a scope up close).</summary>
    public int CloseRange { get; init; }
    public int CloseApPenalty { get; init; }

    /// <summary>Share of the after-moving accuracy penalty this attack ignores (a stock).</summary>
    public double MovedPenaltyReduction { get; init; }

    /// <summary>Banes added by gear mods (an EMP coil, incendiary rounds), on top of <see cref="Bane"/>.</summary>
    public List<BaneDef> ExtraBanes { get; init; } = new();

    /// <summary>Chance per Network-grade prosthetic of the attacker's side within 1 tile to lock up this round when it fires (EMP).</summary>
    public double NetworkFeedback { get; init; }
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

    /// <summary>A hit stuns: the target loses AP at the start of its next turn, as if pinned (a shock coil).</summary>
    public bool Stun { get; init; }
}

/// <summary>Camo pattern: stealth while in a zone of <see cref="Biome"/> (gear_mods.json zoneBiomes).</summary>
public sealed record CamoDef
{
    public string Biome { get; init; } = "";
    public double Stealth { get; init; }
}

/// <summary>
/// Network weave: the wearer's Network-grade prosthetics lock up less (<see cref="InterferenceReduction"/>, EMP feedback
/// too), the Linked's sensors and cameras find the wearer harder (<see cref="LinkedStealth"/>), but Husks hear the wearer
/// <see cref="HuskLure"/> tiles further.
/// </summary>
public sealed record WeaveDef
{
    public double InterferenceReduction { get; init; }
    public double LinkedStealth { get; init; }
    public int HuskLure { get; init; }
}

/// <summary>
/// A weapon light. In the dark the carrier sees hostiles within <see cref="Range"/> in a cone of <see cref="ConeDegrees"/>
/// toward where it last faced, as by day; it loses <see cref="StealthPenalty"/> stealth, and with <see cref="Beacon"/> it is
/// seen by anyone with line of sight to it within their sight before the dark.
/// </summary>
public sealed record LightDef
{
    public double Range { get; init; } = 6;
    public double ConeDegrees { get; init; } = 90;
    public double StealthPenalty { get; init; }
    public bool Beacon { get; init; }
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

    /// <summary>Dresses the worst undressed wound (a bandage) or every wound clean (antiseptic, a clean dressing): see <see cref="Dressing"/>.</summary>
    public Dressing Dress { get; init; }

    /// <summary>Dresses every injured part, not only the worst one.</summary>
    public bool DressAll { get; init; }

    /// <summary>Condition ids it cures (antibiotics: infections; antidote: sicknesses): their severity drops by <see cref="CureStrength"/>.</summary>
    public List<string> Cures { get; init; } = new();
    public double CureStrength { get; init; } = 1;

    /// <summary>Splints a fracture (the worst unsplinted one).</summary>
    public bool Splint { get; init; }

    /// <summary>In the field: the worst injury's severity drops by this much (healed at 0). See <c>Treatment.Mend</c>.</summary>
    public double Mend { get; init; }

    /// <summary>Painkiller: this share of injury penalties is ignored, for <see cref="PainRounds"/> rounds in the field or <see cref="PainTicks"/> at home.</summary>
    public double PainRelief { get; init; }
    public int PainRounds { get; init; }
    public int PainTicks { get; init; }

    /// <summary>Stim: AP added right now; the next turn starts <see cref="CrashAp"/> short.</summary>
    public int StimAp { get; init; }
    public int CrashAp { get; init; }

    /// <summary>Linked "Calm" injector: immune to being pinned down for this many rounds (this one counts).</summary>
    public int CalmRounds { get; init; }

    /// <summary>Humanity it costs the one it is used on, for good (a Network-grade drug).</summary>
    public double HumanityCost { get; init; }

    /// <summary>
    /// Reusable devices (a motion sensor kit): uses per mission before it is flat. The item is not used up; spent charges
    /// come home with it and recharge at the colony (see <c>Power</c>). 0 = a one-shot item (<see cref="Consumed"/> applies).
    /// </summary>
    public int Charges { get; init; }

    /// <summary>EMP grenade: thrown at a tile; disables the Network's machines in a radius (see <see cref="EmpDef"/>).</summary>
    public EmpDef? Emp { get; init; }

    /// <summary>Holo-decoy: a fake survivor on the tile that draws Husks and the Linked's fire (see <see cref="DecoyDef"/>).</summary>
    public DecoyDef? Decoy { get; init; }

    /// <summary>Noise decoy: a racket at the tile; idle enemies within this radius come to look.</summary>
    public int NoiseRadius { get; init; }

    /// <summary>Used on a tile (<see cref="Remnants.Core.Scavenge.UseItem.At"/>) rather than on a unit.</summary>
    public bool TargetsTile => LightRadius > 0 || SensorRadius > 0 || Emp is not null || Decoy is not null || NoiseRadius > 0;

    /// <summary>Does something for a body (heals, dresses, cures, splints, numbs): a medical treatment the Med Bay can use too.</summary>
    public bool Treats => Heal > 0 || Dress != Dressing.None || Cures.Count > 0 || Splint || PainRelief > 0;
}

/// <summary>
/// An EMP burst (Chebyshev <see cref="Radius"/>): Linked units drop off the relay and lose their next turn's AP, drones in
/// the air (anyone's) fall, cameras and turrets are fried, and Network-grade implants nearby (your own too) lock for the round.
/// </summary>
public sealed record EmpDef
{
    public int Radius { get; init; } = 2;

    /// <summary>Rounds the Linked stay off the relay and stunned (this one and the next N − 1).</summary>
    public int Rounds { get; init; } = 2;
}

/// <summary>A holo-decoy: a fake unit of the user's side with <see cref="Hp"/> that lasts <see cref="Rounds"/> and calls out to enemies within <see cref="LureRadius"/>.</summary>
public sealed record DecoyDef
{
    public int Rounds { get; init; } = 2;
    public double Hp { get; init; } = 4;
    public int LureRadius { get; init; } = 6;
}

/// <summary>
/// A handheld radio, carried anywhere: every unit of its side within <see cref="Range"/> of a carrier coordinates
/// (<see cref="AccuracyBonus"/>) and team pings reach <see cref="PingRangeBonus"/> further.
/// </summary>
public sealed record RadioDef
{
    public int Range { get; init; } = 8;
    public double AccuracyBonus { get; init; }
    public int ExtraPings { get; init; }
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

    /// <summary>
    /// A Network deck talks the Network's own protocol: each hack action with it has this chance to be traced (the Linked
    /// hunt the hacker and the alarm rises, as on a failed hack, without the shock or lockout).
    /// </summary>
    public double TraceChance { get; init; }
}

/// <summary>What a drone does beyond scouting (see <c>Scavenge.DroneRoles</c>).</summary>
public enum DroneRole
{
    Scout,
    /// <summary>Applies a carried bandage or med-kit to a friendly unit next to it.</summary>
    Medic,
    /// <summary>Loads items from caches and ferries them out of the zone (secured even if the party falls).</summary>
    Cargo,
    /// <summary>Fires a light gun at hostiles it sees: loud, and it draws fire.</summary>
    Combat,
}

/// <summary>
/// A scout drone item: deployed it becomes a flying unit. Battery in rounds, how far one flight goes, and how far from its
/// operator it can be (before Drone Piloting and perks). Variants (medic, cargo, combat) add a <see cref="Role"/>.
/// </summary>
public sealed record DroneItemDef
{
    public int Battery { get; init; } = 6;
    public int FlyRange { get; init; } = 4;
    public int ControlRange { get; init; } = 6;

    public DroneRole Role { get; init; } = DroneRole.Scout;

    /// <summary>Overrides <c>drone.hp</c> (a combat drone is a little sturdier, a cargo drone frailer).</summary>
    public double? Hp { get; init; }

    /// <summary>Cargo drones: items it can hold.</summary>
    public int Cargo { get; init; }

    /// <summary>Combat drones: the natural weapon item it is fitted with.</summary>
    public string? Weapon { get; init; }

    /// <summary>AP the operator pays for the role's action (heal, load, ferry; a combat drone's shot costs its weapon's AP).</summary>
    public int ActionApCost { get; init; } = 1;

    /// <summary>A perk hook the operator needs (above 0) to deploy it (e.g. a combat drone needs Drone Operator I).</summary>
    public string? RequiresHook { get; init; }
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

    /// <summary>Personal card slots the gear adds while worn (see <c>CardSlots</c>).</summary>
    public int CardSlots { get; init; }
    public double SightBonus { get; init; }
    public double CarryBonus { get; init; }

    /// <summary>Added to the wearer's stealth: a rag coat is quiet, riot armor loud, a camo cloak hides (see <see cref="Remnants.Core.Scavenge.Detection"/>).</summary>
    public double Stealth { get; init; }

    /// <summary>Added to the carrier's detection (binoculars, scanner).</summary>
    public double Detection { get; init; }

    /// <summary>Equipped: added to the hit chance of ranged attacks (a rangefinder).</summary>
    public double RangedAccuracy { get; init; }

    /// <summary>Network-grade gear (cyan): built round Linked parts; an EMP or the Network may bite it.</summary>
    public bool NetworkGrade { get; init; }

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

    /// <summary>Handheld radios (see <see cref="RadioDef"/>).</summary>
    public RadioDef? Radio { get; init; }


    /// <summary>Schematic items: the schematic (data/schematics.json) they carry. Made from the catalogue, never in items.json.</summary>
    public string? Schematic { get; init; }

    /// <summary>Manuals: the skill they teach (see <see cref="ManualDef"/>).</summary>
    public ManualDef? Manual { get; init; }

    /// <summary>Part of an enemy (claws): never looted or crafted.</summary>
    public bool Natural { get; init; }

    /// <summary>A story quest item (the relay antenna): placed by a mission's map, never looted or crafted.</summary>
    public bool Quest { get; init; }
    /// <summary>
    /// Worn gear: share of its armor that counts per body region (missing regions: none). Null protects every part with
    /// all of it (the gear before coverage existed).
    /// </summary>
    public Dictionary<BodyRegion, double>? Coverage { get; init; }

    /// <summary>Worn: injury severity the wearer takes × (1 − this) (padding).</summary>
    public double InjuryResist { get; init; }

    /// <summary>Worn: extra armor against melee attacks (stab-proof lining).</summary>
    public double MeleeArmor { get; init; }

    /// <summary>Worn: tiles added to how far hostiles hear this unit move (plates clank; quiet fabric takes it off).</summary>
    public int MoveNoise { get; init; }

    /// <summary>Worn: stealth in zones of a biome (camo patterns).</summary>
    public CamoDef? Camo { get; init; }

    /// <summary>Weather ids whose effects do not reach the wearer's senses (waterproofing: rain; filters: dust).</summary>
    public List<string> WeatherProtection { get; init; } = new();

    /// <summary>Worn: stealth in the dark against the senses that see through it (the Linked, cameras), and no thermal scan.</summary>
    public double ThermalMask { get; init; }

    /// <summary>Worn: Linked-fibre cloth (see <see cref="WeaveDef"/>).</summary>
    public WeaveDef? NetworkWeave { get; init; }

    /// <summary>A light (weapon light): in the dark the carrier sees in a cone, and is seen.</summary>
    public LightDef? Light { get; init; }

    /// <summary>Gear mods: the Mk (1 = as built).</summary>
    public int Mk { get; init; } = 1;

    /// <summary>Gear mods: the mod item ids fitted, in slot order (empty for unmodded gear).</summary>
    public List<string> Mods { get; init; } = new();

    /// <summary>Gear mods: the item without its Mk and mods (its quality variant), or null for unmodded gear.</summary>
    public string? UnmoddedId { get; init; }

    /// <summary>Modded or upgraded gear (an instance with its own id; never stacked with the plain item).</summary>
    public bool IsModded => UnmoddedId is not null;

    /// <summary>A medical supply: crafted in quality tiers like gear (a Fine bandage heals more), see <c>medicine.json</c>.</summary>
    public bool Medical { get; init; }

    /// <summary>Quality variants: the item they are a variant of (null for a defined item).</summary>
    public string? BaseId { get; init; }

    /// <summary>Quality variants: the tier id (null for a defined item, which is the base tier).</summary>
    public string? Quality { get; init; }

    /// <summary>The defined item this is or is a variant of.</summary>
    public string DefinedId => BaseId ?? Id;

    /// <summary>A vehicle part (wheel, engine, armor plate...): it comes in quality tiers, which scale the upgrade built from it.</summary>
    public bool VehiclePart { get; init; }

    /// <summary>Gear, mods, medical items and vehicle parts come in quality tiers when crafted.</summary>
    public bool HasQuality => (Slot is not null || Kind == ItemKind.Mod || Medical || VehiclePart) && !Natural;

    public EquipSlot? Slot => Kind switch
    {
        ItemKind.Weapon => EquipSlot.Weapon,
        ItemKind.Armor or ItemKind.Clothing => EquipSlot.Body,
        ItemKind.Tool => EquipSlot.Tool,
        ItemKind.Headgear => EquipSlot.Head,
        _ => null,
    };
}
