namespace Remnants.Core.Data;

/// <summary>Where an upgrade goes on a vehicle. Each tier has a number of each (<see cref="VehicleTierDef.Slots"/>).</summary>
public enum VehicleSlot { Engine, Tyres, Armor, Utility }

/// <summary>
/// Vehicles (data/vehicles.json): the tiers a Garage builds, the upgrades it fits into their slots, and how they change
/// travel on the world map and the parked vehicle in a mission. A vehicle is built by a Garage recipe naming its tier
/// (<see cref="RecipeDef.Vehicle"/>), so building takes workers and time like any batch; fitting, removing and repairing
/// upgrades are instant at the Garage.
/// </summary>
public sealed record VehicleData
{
    /// <summary>The building that builds, upgrades and repairs vehicles.</summary>
    public string GarageBuilding { get; init; } = "garage";

    /// <summary>The stockpile resource vehicles burn per travel day.</summary>
    public string FuelResource { get; init; } = "fuel";

    /// <summary>
    /// The item repairs use up: each restores <see cref="RepairPerPart"/> of the vehicle's max condition, or
    /// <see cref="PartRepairPerSparePart"/> of a fitted upgrade's condition.
    /// </summary>
    public string RepairItem { get; init; } = "spare_parts";
    public double RepairPerPart { get; init; } = 0.25;
    public double PartRepairPerSparePart { get; init; } = 0.5;

    /// <summary>Condition lost per day on the road (out and back), as a share of max condition; fitted upgrades wear alike.</summary>
    public double WearPerTravelDay { get; init; } = 0.03;

    /// <summary>A fitted upgrade loses this much of its condition per share of the vehicle's HP lost in a mission.</summary>
    public double CombatWearToParts { get; init; } = 0.5;

    /// <summary>A fitted upgrade below this condition is broken: it stays fitted but does nothing until repaired.</summary>
    public double PartBrokenBelow { get; init; } = 0.25;

    /// <summary>A vehicle below this share of its max condition is broken down and cannot leave until repaired.</summary>
    public double MinLaunchCondition { get; init; } = 0.25;

    /// <summary>Taking an upgrade off gives back this share of its parts (rounded down, at least one of each kind).</summary>
    public double DisassembleRefund { get; init; } = 0.5;

    /// <summary>Speed multiplier for a vehicle without headlights that leaves or arrives at night.</summary>
    public double NightSpeedMultiplier { get; init; } = 0.75;

    /// <summary>Every vehicle gets one more utility slot at this Garage level (0 = never).</summary>
    public int ExtraUtilityAtGarageLevel { get; init; } = 3;

    /// <summary>Tiers the colony starts with (a new game, and older saves when they load).</summary>
    public List<string> Starting { get; init; } = ["handcart"];

    public List<VehicleTierDef> Tiers { get; init; } = new();
    public List<VehicleUpgradeDef> Upgrades { get; init; } = new();

    /// <summary>
    /// Weather id → its effect on vehicles on the road. The weather feature decides which weather it is
    /// (<see cref="Colony.ColonySim.WeatherAt"/>); this table only says what that weather does.
    /// </summary>
    public Dictionary<string, VehicleWeatherDef> Weather { get; init; } = new();
}

/// <summary>
/// One vehicle tier. <see cref="Speed"/> divides the travel days; <see cref="Seats"/> (null = no limit) caps the party next
/// to the Town Hall; <see cref="CargoKg"/> is what the parked vehicle's stash holds; <see cref="FuelPerDay"/> is burnt per
/// travel day (out and back); <see cref="RangeDays"/> is the farthest one-way trip on a tank; <see cref="Noise"/> draws
/// that share more Husks to the zone on arrival. In a mission it has <see cref="MaxCondition"/> HP and
/// <see cref="Armor"/>, and <see cref="Armored"/> parks it as hard cover. <see cref="Slots"/>: how many upgrades of each
/// kind it takes.
/// </summary>
public sealed record VehicleTierDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Tier { get; init; }
    public double Speed { get; init; } = 1;
    public int? Seats { get; init; }
    public double CargoKg { get; init; }
    public double FuelPerDay { get; init; }
    public double RangeDays { get; init; } = 99;
    public double Noise { get; init; }
    public double MaxCondition { get; init; } = 30;
    public double Armor { get; init; }
    public bool Armored { get; init; }
    public Dictionary<VehicleSlot, int> Slots { get; init; } = new();

    /// <summary>Rolls on wheels: mud slows it (<see cref="VehicleWeatherDef.WheeledSpeedMultiplier"/>).</summary>
    public bool Wheeled { get; init; } = true;

    /// <summary>Has a motor: driving it in the dark without headlights is slower (<see cref="VehicleData.NightSpeedMultiplier"/>).</summary>
    public bool Motorized { get; init; }

    /// <summary>Days added to a scout drone's reach when it launches from this vehicle.</summary>
    public double DroneRangeDays { get; init; }

    /// <summary>Model base id (<c>vehicle_{model}</c>, <c>vehicle_token_t{tier}</c>, <c>vehicle_parked_extraction_t{tier}</c>).</summary>
    public string Model { get; init; } = "";
}

/// <summary>
/// An upgrade fitted into a <see cref="Slot"/> at the Garage (<c>FitUpgrade</c>): its cost (resources and part items), the
/// Garage level, an optional schematic, the lowest tier it fits, and what it changes. The parts' quality (Crude / Standard /
/// Fine, from the crafter's Mechanics) scales its effect: a multiplier m becomes 1 + (m − 1)·q and an amount a becomes a·q,
/// where q is the quality's stat multiplier. Flags (headlights, winch, ...) are on or off.
/// </summary>
public sealed record VehicleUpgradeDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public VehicleSlot Slot { get; init; } = VehicleSlot.Utility;
    public int GarageLevel { get; init; } = 1;
    public int MinTier { get; init; }
    public string? Schematic { get; init; }
    public Dictionary<string, double> Cost { get; init; } = new();
    public Dictionary<string, int> Parts { get; init; } = new();

    /// <summary>
    /// The upgrade's mesh (<c>upg_*</c> in the asset manifest), hung on the vehicle's slot node; armor adds the vehicle's
    /// tier (<c>{model}_t{tier}</c>). None for upgrades with nothing to see (a tuned engine).
    /// </summary>
    public string? Model { get; init; }

    public double SpeedMultiplier { get; init; } = 1;
    public double FuelMultiplier { get; init; } = 1;
    public double NoiseMultiplier { get; init; } = 1;
    public double CargoKg { get; init; }
    public double RangeDays { get; init; }
    public double MaxCondition { get; init; }
    public double Armor { get; init; }

    /// <summary>Damage the parked vehicle takes in a mission is multiplied by this (run-flat tyres).</summary>
    public double FieldDamageMultiplier { get; init; } = 1;

    /// <summary>A road the weather blocks can still be driven, at this speed multiplier (0 = it stays blocked).</summary>
    public double BlockedSpeedMultiplier { get; init; }

    /// <summary>Days added to a scout drone's reach from this vehicle (a drone pad).</summary>
    public double DroneRangeDays { get; init; }

    /// <summary>Added to the Radio Mast's range while the vehicle is home (radio booster; see <c>VehicleRules.RadioRangeBonus</c>).</summary>
    public double RadioRange { get; init; }

    /// <summary>Health every survivor who comes home in the vehicle gets back before the road (field medkit locker).</summary>
    public double HealOnReturn { get; init; }

    /// <summary>A mounted weapon on the parked vehicle: damage per round at the nearest hostile within range (roof turret).</summary>
    public double TurretDamage { get; init; }
    public int TurretRange { get; init; }

    /// <summary>No night penalty.</summary>
    public bool Headlights { get; init; }

    /// <summary>Mud does not slow it.</summary>
    public bool AllTerrain { get; init; }

    /// <summary>Parks as hard cover in a mission, like the Armored Rig.</summary>
    public bool Armored { get; init; }

    /// <summary>Wrecks give an extra salvage roll and their big parts (an engine) can be hauled off.</summary>
    public bool Winch { get; init; }

    /// <summary>The arrival is covered by smoke: the vehicle's noise draws no extra Husks.</summary>
    public bool SmokeLauncher { get; init; }

    /// <summary>Field repairs and gear swaps at the parked vehicle: it mends each round and items can come back out of the stash.</summary>
    public bool Workbench { get; init; }

    /// <summary>Charges devices: units next to the parked vehicle get a full drone battery each round (see <c>VehiclePower</c>).</summary>
    public bool SolarCharger { get; init; }
}

/// <summary>
/// What a weather does to vehicles: <see cref="Blocks"/> lists tier ids that cannot set out in it at all; the others go at
/// <see cref="SpeedMultiplier"/>, and wheeled ones also at <see cref="WheeledSpeedMultiplier"/> unless fitted for rough
/// ground. A party on foot is not affected.
/// </summary>
public sealed record VehicleWeatherDef
{
    public string Name { get; init; } = "";
    public List<string> Blocks { get; init; } = new();
    public double SpeedMultiplier { get; init; } = 1;
    public double WheeledSpeedMultiplier { get; init; } = 1;
}
