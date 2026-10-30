using Remnants.Core.Data;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Colony;

/// <summary>
/// A colony vehicle (see <see cref="VehicleData"/>): its tier, the upgrades fitted at the Garage and its condition. A
/// vehicle out with a party is <see cref="Away"/>; one below <see cref="VehicleData.MinLaunchCondition"/> needs repair
/// before it leaves again. Condition is also its HP when it is parked in a mission.
/// </summary>
public sealed class Vehicle
{
    public int Id { get; set; }
    public string TierId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Condition { get; set; }
    public SortedSet<string> Upgrades { get; set; } = new(StringComparer.Ordinal);
    public bool Away { get; set; }
}

/// <summary>A vehicle's numbers with its upgrades fitted (see <see cref="VehicleTierDef"/> for what each means).</summary>
public sealed record VehicleStats(
    VehicleTierDef Tier,
    double Speed,
    int? Seats,
    double CargoKg,
    double FuelPerDay,
    double RangeDays,
    double Noise,
    double MaxCondition,
    double Armor,
    bool Armored,
    bool Headlights,
    bool AllTerrain,
    double DroneRangeDays);

/// <summary>
/// One factor of a trip's speed: the vehicle, its upgrades, the dark, the weather. Terms multiply; the travel planner
/// lists them so the player sees why a trip takes as long as it does. Other features add their own terms here.
/// </summary>
public sealed record TravelTerm(string Source, string Label, double SpeedMultiplier);

/// <summary>
/// A one-way trip: the walking days of the zone, the terms that change its speed, the resulting days and ticks, the fuel
/// per member-less vehicle for the round trip, and whether the trip cannot be made (<see cref="Blocked"/> with a reason).
/// </summary>
public sealed record TravelPlan(
    double BaseDays,
    IReadOnlyList<TravelTerm> Terms,
    double Days,
    long Ticks,
    double FuelRoundTrip,
    string? Blocked)
{
    public double Speed => Terms.Aggregate(1.0, (s, t) => s * t.SpeedMultiplier);
}

/// <summary>
/// Vehicle rules: stats with upgrades, travel time (<see cref="Plan"/>), the Garage's upgrade and repair commands, and a
/// vehicle rolling out of a finished Garage batch.
/// </summary>
public static class VehicleRules
{
    public static VehicleStats Stats(GameData data, Vehicle vehicle) => Stats(data, data.VehicleTiers[vehicle.TierId], vehicle.Upgrades);

    public static VehicleStats Stats(GameData data, VehicleTierDef tier, IEnumerable<string> upgrades)
    {
        var fitted = upgrades.Select(id => data.VehicleUpgrades[id]).ToList();
        return new VehicleStats(
            tier,
            tier.Speed * fitted.Aggregate(1.0, (s, u) => s * u.SpeedMultiplier),
            tier.Seats,
            tier.CargoKg + fitted.Sum(u => u.CargoKg),
            tier.FuelPerDay,
            tier.RangeDays + fitted.Sum(u => u.RangeDays),
            tier.Noise * fitted.Aggregate(1.0, (s, u) => s * u.NoiseMultiplier),
            tier.MaxCondition + fitted.Sum(u => u.MaxCondition),
            tier.Armor + fitted.Sum(u => u.Armor),
            tier.Armored || fitted.Any(u => u.Armored),
            fitted.Any(u => u.Headlights),
            fitted.Any(u => u.AllTerrain),
            tier.DroneRangeDays);
    }

    /// <summary>The colony's Garage (the highest level if there were several), or null.</summary>
    public static Building? Garage(ColonySim sim) =>
        sim.State.Buildings.Where(b => b.DefId == sim.Data.Vehicles.GarageBuilding).OrderByDescending(b => b.Level).FirstOrDefault();

    /// <summary>
    /// A one-way trip to <paramref name="zone"/> leaving at <paramref name="departTick"/> with <paramref name="vehicle"/>
    /// (null = on foot). Terms: the vehicle's speed and upgrades; the weather on the road (<see cref="ColonySim.WeatherAt"/>
    /// read through <see cref="VehicleData.Weather"/>: it may block the vehicle or slow it, mud slowing wheels unless
    /// fitted for rough ground); the dark for a motor vehicle without headlights, when it leaves or arrives at night.
    /// The fuel is for the round trip; a zone beyond the vehicle's range on a tank is blocked. A party on foot keeps the
    /// zone's travel days.
    /// </summary>
    public static TravelPlan Plan(ColonySim sim, ZoneDef zone, Vehicle? vehicle, long departTick)
    {
        var data = sim.Data;
        var tpd = data.Rules.TicksPerDay;
        var baseDays = zone.TravelDays;
        if (vehicle is null || baseDays <= 0)
            return new TravelPlan(baseDays, [], baseDays, (long)Math.Round(baseDays * tpd), 0, null);

        var stats = Stats(data, vehicle);
        var terms = new List<TravelTerm> { new("vehicle", stats.Tier.Name, stats.Tier.Speed) };
        if (Math.Abs(stats.Speed / stats.Tier.Speed - 1) > 1e-9) terms.Add(new("upgrades", "Upgrades", stats.Speed / stats.Tier.Speed));
        string? blocked = null;
        if (sim.WeatherAt(departTick) is { } weatherId && data.Vehicles.Weather.TryGetValue(weatherId, out var weather))
        {
            var name = weather.Name.Length > 0 ? weather.Name : weatherId;
            if (weather.Blocks.Contains(stats.Tier.Id)) blocked = $"{name}: the {stats.Tier.Name} cannot go out in it.";
            if (Math.Abs(weather.SpeedMultiplier - 1) > 1e-9) terms.Add(new("weather", name, weather.SpeedMultiplier));
            if (stats.Tier.Wheeled && !stats.AllTerrain && Math.Abs(weather.WheeledSpeedMultiplier - 1) > 1e-9)
                terms.Add(new("mud", $"{name}: mud", weather.WheeledSpeedMultiplier));
        }

        double Days(IEnumerable<TravelTerm> ts) => baseDays / ts.Aggregate(1.0, (s, t) => s * t.SpeedMultiplier);
        if (stats.Tier.Motorized && !stats.Headlights && data.Rules.DayNight.Enabled)
        {
            var arrival = departTick + (long)Math.Round(Days(terms) * tpd);
            if (DayNight.PhaseAt(departTick, data.Rules) == DayPhase.Night || DayNight.PhaseAt(arrival, data.Rules) == DayPhase.Night)
                terms.Add(new("night", "Night, no headlights", data.Vehicles.NightSpeedMultiplier));
        }

        var days = Days(terms);
        if (blocked is null && baseDays > stats.RangeDays + 1e-9)
            blocked = $"{zone.Name} is beyond the {stats.Tier.Name}'s range ({stats.RangeDays:0.#} days on a tank).";
        return new TravelPlan(baseDays, terms, days, (long)Math.Round(days * tpd), Math.Max(0, Math.Ceiling(stats.FuelPerDay * days * 2 - 1e-9)), blocked);
    }

    /// <summary>The vehicle's state as it parks at the zone's edge, for the scavenge run.</summary>
    public static FieldVehicle Field(GameData data, Vehicle vehicle)
    {
        var stats = Stats(data, vehicle);
        return new FieldVehicle(vehicle.TierId, vehicle.Name, stats.Tier.Tier, vehicle.Condition, stats.MaxCondition, stats.Armor,
            stats.CargoKg, stats.Armored, stats.Seats);
    }

    /// <summary>Condition lost to wear on <paramref name="days"/> of road.</summary>
    public static double Wear(GameData data, Vehicle vehicle, double days) =>
        Stats(data, vehicle).MaxCondition * data.Vehicles.WearPerTravelDay * Math.Max(0, days);

    public static bool Broken(GameData data, Vehicle vehicle) =>
        vehicle.Condition < Stats(data, vehicle).MaxCondition * data.Vehicles.MinLaunchCondition - 1e-9;

    /// <summary>A new vehicle of a tier at full condition, named "{Tier} {n}".</summary>
    internal static Vehicle Add(ColonyState state, GameData data, string tierId)
    {
        var tier = data.VehicleTiers[tierId];
        var number = state.Vehicles.Count(v => v.TierId == tierId) + 1;
        var vehicle = new Vehicle
        {
            Id = state.NextVehicleId++, TierId = tierId, Name = number == 1 ? tier.Name : $"{tier.Name} {number}",
            Condition = tier.MaxCondition,
        };
        state.Vehicles.Add(vehicle);
        return vehicle;
    }

    /// <summary>A Garage batch finished: the vehicle rolls out.</summary>
    internal static void Built(ColonyState state, GameData data, string tierId, int buildingId)
    {
        var vehicle = Add(state, data, tierId);
        state.Log(new(state.Tick, ColonyEventType.VehicleBuilt, $"The Garage rolled out a {data.VehicleTiers[tierId].Name}: {vehicle.Name}.", BuildingId: buildingId));
    }

    /// <summary>
    /// Fits an upgrade at the Garage: the vehicle is home, the Garage has the level, the schematic is known, the tier is
    /// high enough, it is not fitted yet, and the colony pays its resources and parts. Extra condition comes with it.
    /// </summary>
    internal static CommandResult FitUpgrade(ColonySim sim, FitUpgrade c)
    {
        var state = sim.State;
        if (state.Vehicles.Find(v => v.Id == c.VehicleId) is not { } vehicle)
            return CommandResult.Fail(CommandError.UnknownVehicle, $"No vehicle {c.VehicleId}.");
        if (!sim.Data.VehicleUpgrades.TryGetValue(c.UpgradeId, out var upgrade))
            return CommandResult.Fail(CommandError.UnknownUpgrade, $"No vehicle upgrade '{c.UpgradeId}'.");
        if (Check(sim, vehicle, upgrade.GarageLevel) is { } fail) return fail;
        var tier = sim.Data.VehicleTiers[vehicle.TierId];
        if (vehicle.Upgrades.Contains(upgrade.Id)) return CommandResult.Fail(CommandError.AlreadyFitted, $"{vehicle.Name} already has {upgrade.Name}.");
        if (tier.Tier < upgrade.MinTier)
            return CommandResult.Fail(CommandError.UpgradeDoesNotFit, $"{upgrade.Name} does not fit a {tier.Name}.");
        if (!VehicleSchematics.Has(sim, upgrade.Schematic))
            return CommandResult.Fail(CommandError.RecipeLocked, $"{upgrade.Name} needs a schematic the colony has not studied.");
        if (Pay(sim, upgrade.Cost, upgrade.Parts, upgrade.Name) is { } short_) return short_;

        var before = Stats(sim.Data, vehicle).MaxCondition;
        vehicle.Upgrades.Add(upgrade.Id);
        vehicle.Condition += Stats(sim.Data, vehicle).MaxCondition - before;
        var message = $"{upgrade.Name} fitted to {vehicle.Name}.";
        state.Log(new(state.Tick, ColonyEventType.VehicleUpgraded, message, BuildingId: Garage(sim)!.Id));
        return CommandResult.Success(message, vehicle.Id);
    }

    /// <summary>Repairs a vehicle at the Garage with spare parts, as many as it takes to full condition (or as there are).</summary>
    internal static CommandResult Repair(ColonySim sim, RepairVehicle c)
    {
        var state = sim.State;
        var rules = sim.Data.Vehicles;
        if (state.Vehicles.Find(v => v.Id == c.VehicleId) is not { } vehicle)
            return CommandResult.Fail(CommandError.UnknownVehicle, $"No vehicle {c.VehicleId}.");
        if (Check(sim, vehicle, 1) is { } fail) return fail;
        var max = Stats(sim.Data, vehicle).MaxCondition;
        if (vehicle.Condition >= max - 1e-9) return CommandResult.Fail(CommandError.NotDamaged, $"{vehicle.Name} is in full working order.");
        var needed = PartsToRepair(sim.Data, vehicle);
        var parts = Math.Min(needed, state.Inventory.Count(rules.RepairItem));
        if (parts == 0) return CommandResult.Fail(CommandError.MissingItems, "No spare parts in the stores.");

        for (var i = 0; i < parts; i++) state.Inventory.TryTake(rules.RepairItem);
        vehicle.Condition = Math.Min(max, vehicle.Condition + parts * rules.RepairPerPart * max);
        var message = $"{vehicle.Name} repaired with {parts} spare part{(parts == 1 ? "" : "s")} ({vehicle.Condition:0}/{max:0}).";
        state.Log(new(state.Tick, ColonyEventType.VehicleRepaired, message, BuildingId: Garage(sim)!.Id));
        return CommandResult.Success(message, vehicle.Id);
    }

    /// <summary>Spare parts that would bring the vehicle back to full condition.</summary>
    public static int PartsToRepair(GameData data, Vehicle vehicle)
    {
        var max = Stats(data, vehicle).MaxCondition;
        return (int)Math.Ceiling((max - vehicle.Condition) / (data.Vehicles.RepairPerPart * max) - 1e-9);
    }

    private static CommandResult? Check(ColonySim sim, Vehicle vehicle, int garageLevel)
    {
        if (vehicle.Away) return CommandResult.Fail(CommandError.VehicleAway, $"{vehicle.Name} is out with a party.");
        if (Garage(sim) is not { } garage) return CommandResult.Fail(CommandError.NoGarage, "Build a Garage first.");
        if (garage.Level < garageLevel)
            return CommandResult.Fail(CommandError.RecipeLevelTooLow, $"That needs Garage level {garageLevel} (have {garage.Level}).");
        return null;
    }

    private static CommandResult? Pay(ColonySim sim, Dictionary<string, double> cost, Dictionary<string, int> parts, string what)
    {
        var state = sim.State;
        if (!state.Stockpile.CanAfford(cost))
            return CommandResult.Fail(CommandError.CannotAfford, $"Cannot afford {what}: {CommandHandlers.Describe(state.Stockpile.Shortfall(cost))}.");
        var items = parts.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
        if (!state.Inventory.HasAll(items))
            return CommandResult.Fail(CommandError.MissingItems,
                "Missing parts: " + string.Join(", ", parts.Where(kv => !state.Inventory.Has(kv.Key, kv.Value))
                    .Select(kv => $"{kv.Value - state.Inventory.Count(kv.Key)} more {kv.Key}")) + ".");
        state.Stockpile.Spend(cost);
        foreach (var id in items) state.Inventory.TryTake(id);
        return null;
    }
}
