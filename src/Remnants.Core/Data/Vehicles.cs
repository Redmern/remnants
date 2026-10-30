namespace Remnants.Core.Data;

/// <summary>
/// Vehicles (data/vehicles.json): the tiers a Garage builds, the upgrades it fits, and how they change travel on the world
/// map and the parked vehicle in a mission. A vehicle is built by a Garage recipe naming its tier
/// (<see cref="RecipeDef.Vehicle"/>), so building takes workers and time like any batch; upgrades and repairs are instant.
/// </summary>
public sealed record VehicleData
{
    /// <summary>The building that builds, upgrades and repairs vehicles.</summary>
    public string GarageBuilding { get; init; } = "garage";

    /// <summary>The stockpile resource vehicles burn per travel day.</summary>
    public string FuelResource { get; init; } = "fuel";

    /// <summary>The item a repair uses up; each restores <see cref="RepairPerPart"/> of the vehicle's max condition.</summary>
    public string RepairItem { get; init; } = "spare_parts";
    public double RepairPerPart { get; init; } = 0.25;

    /// <summary>Condition lost per day on the road (out and back), as a share of max condition.</summary>
    public double WearPerTravelDay { get; init; } = 0.03;

    /// <summary>A vehicle below this share of its max condition is broken down and cannot leave until repaired.</summary>
    public double MinLaunchCondition { get; init; } = 0.25;

    /// <summary>Speed multiplier for a vehicle without headlights that leaves or arrives at night.</summary>
    public double NightSpeedMultiplier { get; init; } = 0.75;

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
/// <see cref="Armor"/>, and <see cref="Armored"/> parks it as hard cover.
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
/// An upgrade fitted at the Garage (<c>FitUpgrade</c>): its cost (resources and part items), the Garage level, an optional
/// schematic, the lowest tier it fits, and what it changes. Each upgrade fits once per vehicle.
/// </summary>
public sealed record VehicleUpgradeDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int GarageLevel { get; init; } = 1;
    public int MinTier { get; init; }
    public string? Schematic { get; init; }
    public Dictionary<string, double> Cost { get; init; } = new();
    public Dictionary<string, int> Parts { get; init; } = new();

    public double SpeedMultiplier { get; init; } = 1;
    public double CargoKg { get; init; }
    public double RangeDays { get; init; }
    public double NoiseMultiplier { get; init; } = 1;
    public double MaxCondition { get; init; }
    public double Armor { get; init; }

    /// <summary>No night penalty.</summary>
    public bool Headlights { get; init; }

    /// <summary>Mud does not slow it.</summary>
    public bool AllTerrain { get; init; }

    /// <summary>Parks as hard cover in a mission, like the Armored Rig.</summary>
    public bool Armored { get; init; }
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
