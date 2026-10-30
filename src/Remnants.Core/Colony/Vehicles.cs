using Remnants.Core.Data;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Colony;

/// <summary>
/// A colony vehicle (see <see cref="VehicleData"/>): its tier, the upgrades fitted into its slots, and its condition. A
/// vehicle out with a party is <see cref="Away"/>; one below <see cref="VehicleData.MinLaunchCondition"/> needs repair
/// before it leaves again. Condition is also its HP when it is parked in a mission. Damage and wear never delete a
/// vehicle; only being wrecked in a mission (or left there) does.
/// </summary>
public sealed class Vehicle
{
    public int Id { get; set; }
    public string TierId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Condition { get; set; }
    public List<FittedUpgrade> Upgrades { get; set; } = new();
    public bool Away { get; set; }

    public bool Has(string upgradeId) => Upgrades.Any(u => u.UpgradeId == upgradeId);
}

/// <summary>
/// An upgrade in one of a vehicle's slots: the quality of the parts it was built from (<see cref="Quality"/>, a quality tier
/// id) and the resulting <see cref="Scale"/> of its effect (the parts' stat multiplier, plus a Tuner's engine bonus), and
/// its own <see cref="Condition"/> (0..1). Below <see cref="VehicleData.PartBrokenBelow"/> it is broken and does nothing.
/// </summary>
public sealed class FittedUpgrade
{
    public string UpgradeId { get; set; } = "";
    public string Quality { get; set; } = "standard";
    public double Scale { get; set; } = 1;
    public double Condition { get; set; } = 1;

    public static FittedUpgrade Standard(string upgradeId) => new() { UpgradeId = upgradeId };
}

/// <summary>A vehicle's numbers with its working upgrades (see <see cref="VehicleTierDef"/> and <see cref="VehicleUpgradeDef"/>).</summary>
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
    double DroneRangeDays)
{
    public double FieldDamageMultiplier { get; init; } = 1;
    public double BlockedSpeedMultiplier { get; init; }
    public double RadioRange { get; init; }
    public double HealOnReturn { get; init; }
    public double TurretDamage { get; init; }
    public int TurretRange { get; init; }
    public bool Winch { get; init; }
    public bool SmokeLauncher { get; init; }
    public bool Workbench { get; init; }
    public bool SolarCharger { get; init; }
}

/// <summary>
/// One factor of a trip's speed: the vehicle, its upgrades, the dark, the weather. Terms multiply; the travel planner
/// lists them so the player sees why a trip takes as long as it does. Other features add their own terms here.
/// </summary>
public sealed record TravelTerm(string Source, string Label, double SpeedMultiplier);

/// <summary>
/// A one-way trip: the walking days of the zone, the terms that change its speed, the resulting days and ticks, the fuel
/// for the round trip, and whether the trip cannot be made (<see cref="Blocked"/> with a reason).
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
/// Vehicle rules: stats with upgrades, slots, travel time (<see cref="Plan"/>), the Garage's fit / remove / repair
/// commands, wear, and a vehicle rolling out of a finished Garage batch.
/// </summary>
public static class VehicleRules
{
    public static VehicleStats Stats(GameData data, Vehicle vehicle) =>
        Stats(data, data.VehicleTiers[vehicle.TierId], vehicle.Upgrades.Where(u => !IsBroken(data, u)));

    /// <summary>A tier with these upgrades at standard quality (for previews and trials).</summary>
    public static VehicleStats Stats(GameData data, VehicleTierDef tier, IEnumerable<string> upgrades) =>
        Stats(data, tier, upgrades.Select(FittedUpgrade.Standard));

    /// <summary>A tier with these fitted upgrades, each scaled by its <see cref="FittedUpgrade.Scale"/>.</summary>
    public static VehicleStats Stats(GameData data, VehicleTierDef tier, IEnumerable<FittedUpgrade> upgrades)
    {
        var fitted = upgrades.Select(f => (Def: data.VehicleUpgrades[f.UpgradeId], Q: f.Scale)).ToList();
        double Mul(Func<VehicleUpgradeDef, double> m) => fitted.Aggregate(1.0, (s, f) => s * (1 + (m(f.Def) - 1) * f.Q));
        double Sum(Func<VehicleUpgradeDef, double> a) => fitted.Sum(f => a(f.Def) * f.Q);
        bool Any(Func<VehicleUpgradeDef, bool> b) => fitted.Any(f => b(f.Def));
        var turret = fitted.Where(f => f.Def.TurretDamage > 0).ToList();
        var blocked = fitted.Where(f => f.Def.BlockedSpeedMultiplier > 0).Select(f => f.Def.BlockedSpeedMultiplier * f.Q).DefaultIfEmpty(0).Max();
        return new VehicleStats(
            tier,
            tier.Speed * Mul(u => u.SpeedMultiplier),
            tier.Seats,
            tier.CargoKg + Sum(u => u.CargoKg),
            tier.FuelPerDay * Mul(u => u.FuelMultiplier),
            tier.RangeDays + Sum(u => u.RangeDays),
            tier.Noise * Mul(u => u.NoiseMultiplier),
            tier.MaxCondition + Sum(u => u.MaxCondition),
            tier.Armor + Sum(u => u.Armor),
            tier.Armored || Any(u => u.Armored),
            Any(u => u.Headlights),
            Any(u => u.AllTerrain),
            tier.DroneRangeDays + Sum(u => u.DroneRangeDays))
        {
            FieldDamageMultiplier = Mul(u => u.FieldDamageMultiplier),
            BlockedSpeedMultiplier = Math.Min(1, blocked),
            RadioRange = Sum(u => u.RadioRange),
            HealOnReturn = Sum(u => u.HealOnReturn),
            TurretDamage = turret.Sum(f => f.Def.TurretDamage * f.Q),
            TurretRange = turret.Select(f => f.Def.TurretRange).DefaultIfEmpty(0).Max(),
            Winch = Any(u => u.Winch),
            SmokeLauncher = Any(u => u.SmokeLauncher),
            Workbench = Any(u => u.Workbench),
            SolarCharger = Any(u => u.SolarCharger),
        };
    }

    public static bool IsBroken(GameData data, FittedUpgrade upgrade) => upgrade.Condition < data.Vehicles.PartBrokenBelow - 1e-9;

    /// <summary>How many upgrades of a kind the vehicle takes: its tier's slots, one more utility slot at a high enough Garage.</summary>
    public static int SlotCount(ColonySim sim, Vehicle vehicle, VehicleSlot slot)
    {
        var count = sim.Data.VehicleTiers[vehicle.TierId].Slots.GetValueOrDefault(slot);
        var extraAt = sim.Data.Vehicles.ExtraUtilityAtGarageLevel;
        if (slot == VehicleSlot.Utility && extraAt > 0 && Garage(sim) is { } g && g.Level >= extraAt) count++;
        return count;
    }

    public static IEnumerable<FittedUpgrade> InSlot(GameData data, Vehicle vehicle, VehicleSlot slot) =>
        vehicle.Upgrades.Where(u => data.VehicleUpgrades[u.UpgradeId].Slot == slot);

    /// <summary>The colony's Garage (the highest level if there were several), or null.</summary>
    public static Building? Garage(ColonySim sim) =>
        sim.State.Buildings.Where(b => b.DefId == sim.Data.Vehicles.GarageBuilding).OrderByDescending(b => b.Level).FirstOrDefault();

    /// <summary>
    /// The Radio Mast range a radio booster adds while its vehicle is home. The Radio Mast (story-schematics) reads this
    /// one number; nothing else depends on it.
    /// </summary>
    public static double RadioRangeBonus(ColonySim sim) =>
        sim.State.Vehicles.Where(v => !v.Away).Select(v => Stats(sim.Data, v).RadioRange).DefaultIfEmpty(0).Max();

    /// <summary>
    /// A one-way trip to <paramref name="zone"/> leaving at <paramref name="departTick"/> with <paramref name="vehicle"/>
    /// (null = on foot). Terms: the vehicle's speed and upgrades; the weather on the road (<see cref="ColonySim.WeatherAt"/>
    /// read through <see cref="VehicleData.Weather"/>: it may block the vehicle — wide tyres or a ram drive through slowly
    /// instead — or slow it, mud slowing wheels unless fitted for rough ground); the dark for a motor vehicle without
    /// headlights, when it leaves or arrives at night. The fuel is for the round trip; a zone beyond the vehicle's range on
    /// a tank is blocked. A party on foot keeps the zone's travel days.
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
            if (weather.Blocks.Contains(stats.Tier.Id))
            {
                if (stats.BlockedSpeedMultiplier > 0) terms.Add(new("blocked", $"{name}: pushing through", stats.BlockedSpeedMultiplier));
                else blocked = $"{name}: the {stats.Tier.Name} cannot go out in it.";
            }
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

    /// <summary>The noise the vehicle makes arriving: its stats' noise, none under a smoke launcher's cover.</summary>
    public static double ArrivalNoise(GameData data, Vehicle vehicle)
    {
        var stats = Stats(data, vehicle);
        return stats.SmokeLauncher ? 0 : stats.Noise;
    }

    /// <summary>The vehicle's state as it parks at the zone's edge, for the scavenge run.</summary>
    public static FieldVehicle Field(GameData data, Vehicle vehicle)
    {
        var stats = Stats(data, vehicle);
        return new FieldVehicle(vehicle.TierId, vehicle.Name, stats.Tier.Tier, Math.Min(vehicle.Condition, stats.MaxCondition), stats.MaxCondition, stats.Armor,
            stats.CargoKg, stats.Armored, stats.Seats)
        {
            DamageMultiplier = stats.FieldDamageMultiplier, TurretDamage = stats.TurretDamage, TurretRange = stats.TurretRange,
            Winch = stats.Winch, Workbench = stats.Workbench, SolarCharger = stats.SolarCharger,
            Upgrades = vehicle.Upgrades.Where(u => !IsBroken(data, u)).Select(u => u.UpgradeId).ToList(),
        };
    }

    /// <summary>Condition lost to wear on <paramref name="days"/> of road.</summary>
    public static double Wear(GameData data, Vehicle vehicle, double days) =>
        Stats(data, vehicle).MaxCondition * data.Vehicles.WearPerTravelDay * Math.Max(0, days);

    /// <summary>
    /// The road wears the vehicle and every fitted upgrade by <see cref="VehicleData.WearPerTravelDay"/> a day. Wear never
    /// takes the vehicle below 1 or a part below 0; a worn-out part stays fitted, broken.
    /// </summary>
    public static void WearOut(GameData data, Vehicle vehicle, double days)
    {
        if (days <= 0) return;
        vehicle.Condition = Math.Max(1, vehicle.Condition - Wear(data, vehicle, days));
        foreach (var u in vehicle.Upgrades) u.Condition = Math.Max(0, u.Condition - data.Vehicles.WearPerTravelDay * days);
        ClampCondition(data, vehicle);
    }

    /// <summary>A part that broke takes its extra condition with it: the hull never stays above the max it has now.</summary>
    internal static void ClampCondition(GameData data, Vehicle vehicle) =>
        vehicle.Condition = Math.Clamp(vehicle.Condition, 1, Stats(data, vehicle).MaxCondition);

    /// <summary>A mission took <paramref name="share"/> of the vehicle's HP: its parts take <see cref="VehicleData.CombatWearToParts"/> of that.</summary>
    internal static void CombatWear(GameData data, Vehicle vehicle, double share)
    {
        foreach (var u in vehicle.Upgrades) u.Condition = Math.Max(0, u.Condition - share * data.Vehicles.CombatWearToParts);
        ClampCondition(data, vehicle);
    }

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

    /// <summary>The perk hooks of the Garage's best mechanic at work (a Grease Monkey's discount, a Tuner's engine bonus).</summary>
    public static double GarageHook(ColonySim sim, string hook) =>
        Garage(sim) is { } garage
            ? sim.State.WorkersAt(garage.Id).Select(w => sim.PerkEffects(w).Hook(hook)).DefaultIfEmpty(0).Max()
            : 0;

    /// <summary>
    /// Why an upgrade cannot be fitted to a vehicle right now (null = it can, if the colony can pay): vehicle home, Garage
    /// level, schematic, tier, not fitted yet, a free slot of its kind.
    /// </summary>
    public static CommandResult? CanFit(ColonySim sim, Vehicle vehicle, VehicleUpgradeDef upgrade)
    {
        if (Check(sim, vehicle, upgrade.GarageLevel) is { } fail) return fail;
        var tier = sim.Data.VehicleTiers[vehicle.TierId];
        if (vehicle.Has(upgrade.Id)) return CommandResult.Fail(CommandError.AlreadyFitted, $"{vehicle.Name} already has {upgrade.Name}.");
        if (tier.Tier < upgrade.MinTier || SlotCount(sim, vehicle, upgrade.Slot) == 0)
            return CommandResult.Fail(CommandError.UpgradeDoesNotFit, $"{upgrade.Name} does not fit a {tier.Name}.");
        if (InSlot(sim.Data, vehicle, upgrade.Slot).Count() >= SlotCount(sim, vehicle, upgrade.Slot))
            return CommandResult.Fail(CommandError.SlotFull, $"{vehicle.Name}'s {upgrade.Slot.ToString().ToLowerInvariant()} slots are full; take something off first.");
        if (!VehicleSchematics.Has(sim, upgrade.Schematic))
            return CommandResult.Fail(CommandError.RecipeLocked, $"{upgrade.Name} needs a schematic the colony has not studied.");
        return null;
    }

    /// <summary>
    /// Fits an upgrade at the Garage (see <see cref="CanFit"/>), paying its resources and its parts, best quality first. The
    /// parts' mean quality multiplier scales its effect; a Tuner at the Garage adds to an engine upgrade's. Extra condition
    /// comes with it.
    /// </summary>
    internal static CommandResult FitUpgrade(ColonySim sim, FitUpgrade c)
    {
        var state = sim.State;
        if (state.Vehicles.Find(v => v.Id == c.VehicleId) is not { } vehicle)
            return CommandResult.Fail(CommandError.UnknownVehicle, $"No vehicle {c.VehicleId}.");
        if (!sim.Data.VehicleUpgrades.TryGetValue(c.UpgradeId, out var upgrade))
            return CommandResult.Fail(CommandError.UnknownUpgrade, $"No vehicle upgrade '{c.UpgradeId}'.");
        if (CanFit(sim, vehicle, upgrade) is { } fail) return fail;
        if (Pay(sim, upgrade.Cost, upgrade.Parts, upgrade.Name, out var taken) is { } short_) return short_;

        var (quality, scale) = PartQuality(sim, taken);
        if (upgrade.Slot == VehicleSlot.Engine) scale += GarageHook(sim, PerkHooks.EngineTuneBonus);
        var before = Stats(sim.Data, vehicle).MaxCondition;
        vehicle.Upgrades.Add(new FittedUpgrade { UpgradeId = upgrade.Id, Quality = quality, Scale = scale });
        vehicle.Condition += Stats(sim.Data, vehicle).MaxCondition - before;
        var message = $"{upgrade.Name} ({QualityName(sim, quality)}) fitted to {vehicle.Name}.";
        state.Log(new(state.Tick, ColonyEventType.VehicleUpgraded, message, BuildingId: Garage(sim)!.Id));
        return CommandResult.Success(message, vehicle.Id);
    }

    /// <summary>
    /// Takes an upgrade off at the Garage: <see cref="VehicleData.DisassembleRefund"/> of its parts come back (rounded down,
    /// at least one of each kind) at the quality it was built with; its resources are gone. The vehicle keeps its damage.
    /// </summary>
    internal static CommandResult RemoveUpgrade(ColonySim sim, RemoveUpgrade c)
    {
        var state = sim.State;
        if (state.Vehicles.Find(v => v.Id == c.VehicleId) is not { } vehicle)
            return CommandResult.Fail(CommandError.UnknownVehicle, $"No vehicle {c.VehicleId}.");
        if (vehicle.Upgrades.Find(u => u.UpgradeId == c.UpgradeId) is not { } fitted)
            return CommandResult.Fail(CommandError.UnknownUpgrade, $"{vehicle.Name} has no '{c.UpgradeId}' fitted.");
        if (Check(sim, vehicle, 1) is { } fail) return fail;
        var upgrade = sim.Data.VehicleUpgrades[fitted.UpgradeId];

        var lost = Stats(sim.Data, vehicle).MaxCondition;
        vehicle.Upgrades.Remove(fitted);
        lost -= Stats(sim.Data, vehicle).MaxCondition;
        vehicle.Condition = Math.Max(1, vehicle.Condition - Math.Max(0, lost));
        var back = new List<string>();
        foreach (var (part, count) in upgrade.Parts)
        {
            var n = Math.Max(1, (int)Math.Floor(count * sim.Data.Vehicles.DisassembleRefund));
            var id = sim.Scavenge is { } items && items.Items.TryGetValue(part, out var def) && def.HasQuality ? items.QualityVariant(part, fitted.Quality) : part;
            state.Inventory.Add(id, n);
            back.Add($"{n} {sim.Scavenge?.Items.GetValueOrDefault(id)?.Name ?? part}");
        }
        var message = $"{upgrade.Name} taken off {vehicle.Name}{(back.Count > 0 ? $": {string.Join(", ", back)} back" : "")}.";
        state.Log(new(state.Tick, ColonyEventType.VehicleUpgraded, message, BuildingId: Garage(sim)!.Id));
        return CommandResult.Success(message, vehicle.Id);
    }

    /// <summary>
    /// Repairs a vehicle at the Garage with spare parts: first its condition (<see cref="VehicleData.RepairPerPart"/> of max
    /// each), then its worn upgrades, most worn first (<see cref="VehicleData.PartRepairPerSparePart"/> each), as far as the
    /// parts go. A Grease Monkey at the Garage needs fewer.
    /// </summary>
    internal static CommandResult Repair(ColonySim sim, RepairVehicle c)
    {
        var state = sim.State;
        var rules = sim.Data.Vehicles;
        if (state.Vehicles.Find(v => v.Id == c.VehicleId) is not { } vehicle)
            return CommandResult.Fail(CommandError.UnknownVehicle, $"No vehicle {c.VehicleId}.");
        if (Check(sim, vehicle, 1) is { } fail) return fail;
        var needed = PartsToRepair(sim, vehicle);
        if (needed == 0) return CommandResult.Fail(CommandError.NotDamaged, $"{vehicle.Name} is in full working order.");
        var have = SparePartCount(sim);
        if (have == 0) return CommandResult.Fail(CommandError.MissingItems, "No spare parts in the stores.");

        // A discount stretches each spare part: n parts do the work of n / (1 − discount).
        var stretch = 1 / Math.Max(0.1, 1 - GarageHook(sim, PerkHooks.RepairPartsDiscount));
        var used = Math.Min(needed, have);
        var work = used * stretch;
        var max = Stats(sim.Data, vehicle).MaxCondition;
        var hull = Math.Min(work, Math.Max(0, Math.Ceiling((max - vehicle.Condition) / (rules.RepairPerPart * max) - 1e-9)));
        vehicle.Condition = Math.Min(max, vehicle.Condition + hull * rules.RepairPerPart * max);
        work -= hull;
        foreach (var u in vehicle.Upgrades.OrderBy(u => u.Condition))
        {
            if (work <= 1e-9) break;
            var want = (1 - u.Condition) / rules.PartRepairPerSparePart;
            var spend = Math.Min(work, want);
            u.Condition = Math.Min(1, u.Condition + spend * rules.PartRepairPerSparePart);
            work -= spend;
        }
        TakeSpareParts(sim, used);
        max = Stats(sim.Data, vehicle).MaxCondition;
        var message = $"{vehicle.Name} repaired with {used} spare part{(used == 1 ? "" : "s")} ({vehicle.Condition:0}/{max:0}).";
        state.Log(new(state.Tick, ColonyEventType.VehicleRepaired, message, BuildingId: Garage(sim)!.Id));
        return CommandResult.Success(message, vehicle.Id);
    }

    /// <summary>Spare parts that would bring the vehicle and every fitted upgrade back to full (after a Grease Monkey's discount).</summary>
    public static int PartsToRepair(ColonySim sim, Vehicle vehicle)
    {
        var rules = sim.Data.Vehicles;
        var max = Stats(sim.Data, vehicle).MaxCondition;
        var work = Math.Max(0, Math.Ceiling((max - vehicle.Condition) / (rules.RepairPerPart * max) - 1e-9))
                   + vehicle.Upgrades.Sum(u => (1 - u.Condition) / rules.PartRepairPerSparePart);
        var discount = Math.Min(0.9, GarageHook(sim, PerkHooks.RepairPartsDiscount));
        return (int)Math.Ceiling(Math.Max(0, work) * (1 - discount) - 1e-9);
    }

    private static int SparePartCount(ColonySim sim) => Matching(sim, sim.Data.Vehicles.RepairItem).Sum(id => sim.State.Inventory.Count(id));

    /// <summary>Spare parts are used worst first (their quality does not matter for a repair).</summary>
    private static void TakeSpareParts(ColonySim sim, int count)
    {
        foreach (var id in Matching(sim, sim.Data.Vehicles.RepairItem).OrderBy(id => QualityMultiplier(sim, id)).ToList())
            while (count > 0 && sim.State.Inventory.TryTake(id)) count--;
    }

    /// <summary>Inventory ids of an item in any quality.</summary>
    private static IEnumerable<string> Matching(ColonySim sim, string baseId) =>
        sim.State.Inventory.Counts.Keys
            .Where(id => id == baseId || sim.Scavenge?.Items.GetValueOrDefault(id)?.DefinedId == baseId)
            .ToList();

    private static double QualityMultiplier(ColonySim sim, string itemId)
    {
        if (sim.Scavenge?.Items.GetValueOrDefault(itemId) is not { Quality: { } q }) return 1;
        return sim.Scavenge.Rules.Quality.Find(t => t.Id == q)?.StatMultiplier ?? 1;
    }

    /// <summary>The mean quality multiplier of the parts taken and the nearest quality tier (standard when none carry quality).</summary>
    private static (string Tier, double Scale) PartQuality(ColonySim sim, List<string> taken)
    {
        var tiers = sim.Scavenge?.Rules.Quality ?? [];
        var rated = taken.Where(id => sim.Scavenge?.Items.GetValueOrDefault(id)?.HasQuality == true).ToList();
        var standard = tiers.Find(t => t.Base)?.Id ?? "standard";
        if (rated.Count == 0 || tiers.Count == 0) return (standard, 1);
        var scale = rated.Average(id => QualityMultiplier(sim, id));
        var tier = tiers.OrderBy(t => Math.Abs(t.StatMultiplier - scale)).First().Id;
        return (tier, scale);
    }

    public static string QualityName(ColonySim sim, string tierId) =>
        sim.Scavenge?.Rules.Quality.Find(t => t.Id == tierId)?.Name ?? tierId;

    private static CommandResult? Check(ColonySim sim, Vehicle vehicle, int garageLevel)
    {
        if (vehicle.Away) return CommandResult.Fail(CommandError.VehicleAway, $"{vehicle.Name} is out with a party.");
        if (Garage(sim) is not { } garage) return CommandResult.Fail(CommandError.NoGarage, "Build a Garage first.");
        if (garage.Level < garageLevel)
            return CommandResult.Fail(CommandError.RecipeLevelTooLow, $"That needs Garage level {garageLevel} (have {garage.Level}).");
        return null;
    }

    /// <summary>Pays resources and parts (any quality of each part, best first); <paramref name="taken"/> lists the item ids used.</summary>
    private static CommandResult? Pay(ColonySim sim, Dictionary<string, double> cost, Dictionary<string, int> parts, string what, out List<string> taken)
    {
        var state = sim.State;
        taken = new();
        if (!state.Stockpile.CanAfford(cost))
            return CommandResult.Fail(CommandError.CannotAfford, $"Cannot afford {what}: {CommandHandlers.Describe(state.Stockpile.Shortfall(cost))}.");
        var missing = parts.Where(kv => Matching(sim, kv.Key).Sum(id => state.Inventory.Count(id)) < kv.Value).ToList();
        if (missing.Count > 0)
            return CommandResult.Fail(CommandError.MissingItems,
                "Missing parts: " + string.Join(", ", missing.Select(kv =>
                    $"{kv.Value - Matching(sim, kv.Key).Sum(id => state.Inventory.Count(id))} more {sim.Scavenge?.Items.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}")) + ".");
        state.Stockpile.Spend(cost);
        foreach (var (part, count) in parts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var left = count;
            foreach (var id in Matching(sim, part).OrderByDescending(id => QualityMultiplier(sim, id)).ThenBy(id => id, StringComparer.Ordinal).ToList())
                while (left > 0 && state.Inventory.TryTake(id))
                {
                    taken.Add(id);
                    left--;
                }
        }
        return null;
    }
}
