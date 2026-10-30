using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Colony;

/// <summary>The colony grid as of the last tick (see <see cref="Power"/>). Plain data, saved with the colony.</summary>
public sealed class PowerState
{
    /// <summary>Power held in battery banks.</summary>
    public double Stored { get; set; }

    /// <summary>Buildings the player switched off the grid (consumers draw nothing, generators stay cold).</summary>
    public SortedSet<int> Off { get; set; } = new();

    /// <summary>Consumer buildings fed last tick.</summary>
    public SortedSet<int> Powered { get; set; } = new();

    /// <summary>Buildings whose Floodlight was fed last tick.</summary>
    public SortedSet<int> Lit { get; set; } = new();

    /// <summary>The charging bench was fed last tick.</summary>
    public bool Charging { get; set; }

    /// <summary>What the grid could supply last tick (generators at the fuel there was, solar now, battery discharge).</summary>
    public double Capacity { get; set; }

    /// <summary>What the switched-on consumers asked for, and what they got.</summary>
    public double Demand { get; set; }
    public double Used { get; set; }

    /// <summary>Power each generator made last tick, by building id.</summary>
    public SortedDictionary<int, double> Output { get; set; } = new();

    public bool Brownout { get; set; }
    public int OverloadTicks { get; set; }

    /// <summary>The breaker is tripped (every consumer cut) until this tick.</summary>
    public long BreakerUntil { get; set; }
}

/// <summary>A device that came home with spent charges; it goes back into the inventory when <see cref="Done"/> reaches <see cref="Work"/>.</summary>
public sealed class ChargingItem
{
    public string ItemId { get; set; } = "";
    public double Work { get; set; }
    public double Done { get; set; }
}

/// <summary>One line of the grid for the UI.</summary>
public sealed record PowerNode(int BuildingId, string DefId, PowerRole Role, double Amount, bool On, bool Fed, string Note);

public enum PowerRole { Generator, Storage, Consumer, Floodlight }

/// <summary>
/// The colony power grid (electronics.json <c>power</c>), opt-in: nothing needs power to work, so a colony without a
/// generator plays exactly as before. Each tick, before production:
/// <list type="number">
/// <item>Demand: every consumer building (in the data's priority order, then by id) not switched off draws its level's
/// draw, less the best <c>power_efficiency</c> (Electrician) among the people at home; Floodlights draw after dusk; the
/// charging bench draws per device it charges.</item>
/// <item>Supply: solar (output × the phase's share × the weather factor), the battery banks (up to their rate), then fuel
/// generators (up to output, as far as their fuel lasts).</item>
/// <item>Consumers are fed in priority order; one that does not fit is shed (a <b>brownout</b>) and the next is tried.
/// Power is taken from solar first, then the batteries, then the generators, which burn fuel for what they supply. Spare
/// solar charges the batteries.</item>
/// <item><b>Overload</b>: demand above <c>overloadRatio</c> × capacity for <c>overloadTicks</c> in a row trips the breaker:
/// nothing is fed for <c>breakerTicks</c>.</item>
/// </list>
/// Powered consumers get their bonus (<see cref="PowerEffect"/>); other features ask <see cref="IsPowered"/>.
/// </summary>
public static class Power
{
    public const string FloodlightConsumer = "floodlight";
    public const string ChargingConsumer = "charging";

    /// <summary>Whether a consumer building was fed last tick (the seam for the cold cellar, turrets, the radio mast).</summary>
    public static bool IsPowered(ColonyState state, Building building) => state.Power.Powered.Contains(building.Id);

    /// <summary>The bonus a powered consumer of this building type gets for an effect (0 when unpowered or none).</summary>
    public static double Bonus(ColonyState state, GameData data, Building building, PowerEffect effect) =>
        data.Electronics is { } e && IsPowered(state, building)
            ? e.Power.Consumers.Where(c => c.Building == building.DefId && c.Effect == effect).Select(c => c.Value).DefaultIfEmpty(0).Max()
            : 0;

    /// <summary>Night Shift output bonus from a powered Floodlight on this building.</summary>
    public static double NightShiftBonus(ColonyState state, GameData data, Building building) =>
        data.Electronics is { } e && state.Power.Lit.Contains(building.Id)
            ? e.Power.Consumers.Where(c => c.Building == FloodlightConsumer).Select(c => c.Value).DefaultIfEmpty(0).Max()
            : 0;

    /// <summary>Whether the building makes, stores or draws power (it can be switched).</summary>
    public static bool OnGrid(GameData data, Building building) =>
        data.Electronics is { } e && (e.Power.Generators.ContainsKey(building.DefId) || e.Power.Storage.ContainsKey(building.DefId)
                                      || e.Power.Consumers.Any(c => c.Building == building.DefId && c.DrawAt(building.Level) > 0)
                                      || building.Floodlight && e.Power.Consumers.Any(c => c.Building == FloodlightConsumer));

    /// <summary>Total battery capacity.</summary>
    public static double StorageCapacity(ColonyState state, GameData data) =>
        data.Electronics is not { } e ? 0
            : state.Buildings.Where(b => e.Power.Storage.ContainsKey(b.DefId) && !state.Power.Off.Contains(b.Id))
                .Sum(b => e.Power.Storage[b.DefId].Capacity[b.Level - 1]);

    /// <summary>Share taken off draws: the best Electrician at home.</summary>
    public static double Efficiency(ColonyState state, GameData data) =>
        Math.Clamp(state.Present.Select(s => PerkEffects.For(s, data).Hook(PerkHooks.PowerEfficiency)).DefaultIfEmpty(0).Max(), 0, 0.75);

    /// <summary>
    /// Weather adapter: solar output factor now (1 until the weather feature reads its dust and storms here; that branch
    /// replaces this body with its colony weather query).
    /// </summary>
    public static double SolarWeatherFactor(ColonyState state, GameData data) => 1;

    /// <summary>
    /// Vehicles adapter: charge work per tick a powered parked vehicle adds to the charging bench (0 until the vehicles
    /// feature reports one here).
    /// </summary>
    public static double VehicleChargeRate(ColonyState state, GameData data) => 0;

    /// <summary>The grid's parts for the overlay and the top bar.</summary>
    public static List<PowerNode> Nodes(ColonyState state, GameData data)
    {
        var result = new List<PowerNode>();
        if (data.Electronics is not { } e) return result;
        var p = state.Power;
        foreach (var b in state.Buildings.OrderBy(b => b.Id))
        {
            var on = !p.Off.Contains(b.Id);
            if (e.Power.Generators.TryGetValue(b.DefId, out var g))
            {
                var made = p.Output.GetValueOrDefault(b.Id);
                result.Add(new(b.Id, b.DefId, PowerRole.Generator, made, on, made > 0,
                    g.Solar ? "solar" : FuelFor(state, data, g) is { } f ? data.Resources[f.Resource].Name : "no fuel"));
            }
            else if (e.Power.Storage.TryGetValue(b.DefId, out var s))
                result.Add(new(b.Id, b.DefId, PowerRole.Storage, s.Capacity[b.Level - 1], on, true, ""));
            foreach (var c in e.Power.Consumers.Where(c => c.Building == b.DefId && c.DrawAt(b.Level) > 0).Take(1))
                result.Add(new(b.Id, b.DefId, PowerRole.Consumer, c.DrawAt(b.Level), on, p.Powered.Contains(b.Id), c.Describe));
            if (b.Floodlight && e.Power.Consumers.FirstOrDefault(c => c.Building == FloodlightConsumer) is { } fl)
                result.Add(new(b.Id, b.DefId, PowerRole.Floodlight, fl.DrawAt(1), on, p.Lit.Contains(b.Id), fl.Describe));
        }
        return result;
    }

    internal static void Run(ColonyState state, GameData data)
    {
        if (data.Electronics is not { } e) return;
        var def = e.Power;
        var p = state.Power;
        // No generator or bank built: there is no grid, nothing is powered and nothing browns out (the early game).
        if (!state.Buildings.Any(b => def.Generators.ContainsKey(b.DefId) || def.Storage.ContainsKey(b.DefId)))
        {
            p.Powered.Clear();
            p.Lit.Clear();
            p.Output.Clear();
            p.Charging = false;
            p.Brownout = false;
            p.Capacity = p.Demand = p.Used = 0;
            p.OverloadTicks = 0;
            Charge(state, data, e);
            return;
        }
        var efficiency = Efficiency(state, data);
        var night = DayNight.PhaseAt(state.Tick, data.Rules) is DayPhase.Dusk or DayPhase.Night || !data.Rules.DayNight.Enabled;

        // Demand, in priority order.
        var wants = new List<(Building? Building, ConsumerDef Def, double Draw)>();
        foreach (var c in def.Consumers)
        {
            if (c.Building == ChargingConsumer)
            {
                var n = Math.Min(state.Charging.Count, e.Charging.MaxAtOnce);
                if (n > 0) wants.Add((null, c, n * e.Charging.DrawPerItem * (1 - efficiency)));
                continue;
            }
            var floodlight = c.Building == FloodlightConsumer;
            foreach (var b in state.Buildings.Where(b => floodlight ? b.Floodlight && night : b.DefId == c.Building).OrderBy(b => b.Id))
            {
                // A workplace draws only while someone works it.
                if (p.Off.Contains(b.Id) || !floodlight && data.Buildings[b.DefId].Job is not null && !Schedule.Crew(state, data, b).Any()) continue;
                var draw = (floodlight ? c.DrawAt(1) : c.DrawAt(b.Level)) * (1 - efficiency);
                if (draw > 0) wants.Add((b, c, draw));
            }
        }
        var demand = wants.Sum(w => w.Draw);

        // Supply.
        var solar = 0.0;
        var output = new SortedDictionary<int, double>();
        var fuelGens = new List<(Building B, GeneratorDef G, double Cap)>();
        foreach (var b in state.Buildings.Where(b => def.Generators.ContainsKey(b.DefId) && !p.Off.Contains(b.Id)).OrderBy(b => b.Id))
        {
            var g = def.Generators[b.DefId];
            var full = g.Output[b.Level - 1];
            if (g.Solar)
            {
                var made = full * def.SolarByPhase.GetValueOrDefault(DayNight.PhaseAt(state.Tick, data.Rules)) * SolarWeatherFactor(state, data);
                if (!data.Rules.DayNight.Enabled) made = full * def.SolarByPhase.GetValueOrDefault(DayPhase.Day) * SolarWeatherFactor(state, data);
                solar += made;
                output[b.Id] = made;
            }
            else if (FuelFor(state, data, g) is { } fuel)
                fuelGens.Add((b, g, Math.Min(full, state.Stockpile.Get(fuel.Resource) / fuel.PerPower)));
        }
        var banks = state.Buildings.Where(b => def.Storage.ContainsKey(b.DefId) && !p.Off.Contains(b.Id)).ToList();
        var capacityStored = banks.Sum(b => def.Storage[b.DefId].Capacity[b.Level - 1]);
        var rate = banks.Sum(b => def.Storage[b.DefId].Rate[b.Level - 1]);
        p.Stored = Math.Min(p.Stored, capacityStored);
        var fromBattery = Math.Min(rate, p.Stored);
        var capacity = solar + fromBattery + fuelGens.Sum(f => f.Cap);

        // Overload and the breaker.
        // An overload is a grid asked for far more than it has; a grid with nothing left to give just browns out.
        p.OverloadTicks = capacity > 1e-9 && demand > def.OverloadRatio * capacity + 1e-9 ? p.OverloadTicks + 1 : 0;
        if (p.OverloadTicks >= def.OverloadTicks)
        {
            p.OverloadTicks = 0;
            p.BreakerUntil = state.Tick + def.BreakerTicks;
            state.Log(new(state.Tick, ColonyEventType.BreakerTripped,
                $"Overload: the breaker tripped. The grid is down for {def.BreakerTicks} h — switch something off or add power."));
        }
        var tripped = state.Tick < p.BreakerUntil;

        // Feed consumers in priority order.
        var left = tripped ? 0 : capacity;
        var powered = new SortedSet<int>();
        var lit = new SortedSet<int>();
        var charging = false;
        var used = 0.0;
        var shed = false;
        foreach (var (b, c, draw) in wants)
        {
            if (draw > left + 1e-9)
            {
                shed = true;
                continue;
            }
            left -= draw;
            used += draw;
            if (c.Building == ChargingConsumer) charging = true;
            else if (c.Building == FloodlightConsumer) lit.Add(b!.Id);
            else powered.Add(b!.Id);
        }

        // Take it from solar, then the batteries, then the generators (which burn fuel); spare solar charges the batteries.
        var need = used;
        var fromSolar = Math.Min(need, solar);
        need -= fromSolar;
        var discharge = Math.Min(need, fromBattery);
        need -= discharge;
        p.Stored -= discharge;
        foreach (var (b, g, cap) in fuelGens)
        {
            var made = Math.Min(need, cap);
            need -= made;
            output[b.Id] = made;
            if (made > 0 && FuelFor(state, data, g) is { } fuel) state.Stockpile.Spend(new Dictionary<string, double> { [fuel.Resource] = made * fuel.PerPower });
        }
        var spareSolar = solar - fromSolar;
        if (spareSolar > 0) p.Stored = Math.Min(capacityStored, p.Stored + Math.Min(spareSolar, Math.Max(0, rate - discharge)));

        var brownout = shed || tripped && wants.Count > 0;
        if (brownout && !p.Brownout && !tripped)
            state.Log(new(state.Tick, ColonyEventType.Brownout,
                $"Brownout: the grid cannot feed everything ({demand:0.#} wanted, {capacity:0.#} there). The lowest-priority buildings go dark."));
        else if (!brownout && p.Brownout)
            state.Log(new(state.Tick, ColonyEventType.PowerRestored, "Power restored: every building on the grid is fed."));

        p.Brownout = brownout;
        p.Capacity = capacity;
        p.Demand = demand;
        p.Used = used;
        p.Output = output;
        p.Powered = powered;
        p.Lit = lit;
        p.Charging = charging;

        Charge(state, data, e);
    }

    /// <summary>The fuel a generator burns now: the first of its fuels that is a resource and in stock.</summary>
    public static FuelDef? FuelFor(ColonyState state, GameData data, GeneratorDef g) =>
        g.Fuel.FirstOrDefault(f => data.Resources.ContainsKey(f.Resource) && state.Stockpile.Get(f.Resource) > 1e-9);

    /// <summary>The charging bench works the first <c>maxAtOnce</c> drained devices; done ones go back into the inventory.</summary>
    private static void Charge(ColonyState state, GameData data, ElectronicsDef e)
    {
        if (state.Charging.Count == 0) return;
        var rate = (state.Power.Charging ? e.Charging.PoweredRate : e.Charging.UnpoweredRate) + VehicleChargeRate(state, data);
        foreach (var item in state.Charging.Take(e.Charging.MaxAtOnce).ToList())
        {
            item.Done += rate;
            if (item.Done + 1e-9 < item.Work) continue;
            state.Charging.Remove(item);
            state.Inventory.Add(item.ItemId);
            state.Log(new(state.Tick, ColonyEventType.DeviceCharged, $"{item.ItemId} is charged and back in the stores."));
        }
    }

    /// <summary>A device home with <paramref name="spent"/> charges used goes on the charging bench instead of into the stores.</summary>
    internal static void Drained(ColonyState state, GameData data, string itemId, int spent)
    {
        if (data.Electronics is not { } e || spent <= 0)
        {
            state.Inventory.Add(itemId);
            return;
        }
        state.Charging.Add(new ChargingItem { ItemId = itemId, Work = spent * e.Charging.WorkPerCharge });
    }

    /// <summary>Ticks until a charging device is done at today's rate (null when it is not charging at all).</summary>
    public static int? ChargeEta(ColonyState state, GameData data, ChargingItem item)
    {
        if (data.Electronics is not { } e) return null;
        var index = state.Charging.IndexOf(item);
        if (index < 0) return null;
        var rate = (state.Power.Charging ? e.Charging.PoweredRate : e.Charging.UnpoweredRate) + VehicleChargeRate(state, data);
        if (rate <= 0) return null;
        // Beyond the bench's slots it waits for one to free up (roughly: the ones ahead finish in order).
        var max = e.Charging.MaxAtOnce;
        var queued = index < max ? 0
            : state.Charging.Take(max).Select(i => (i.Work - i.Done) / rate).OrderBy(t => t).ElementAt(Math.Min(index - max, max - 1));
        return (int)Math.Ceiling(queued + (item.Work - item.Done) / rate);
    }

    public static CommandResult SetPower(ColonySim sim, SetPower c)
    {
        var state = sim.State;
        if (state.FindBuilding(c.BuildingId) is not { } building) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = sim.Data.Buildings[building.DefId];
        if (!OnGrid(sim.Data, building)) return CommandResult.Fail(CommandError.NotOnGrid, $"{def.Name} is not on the power grid.");
        if (c.On) state.Power.Off.Remove(building.Id);
        else state.Power.Off.Add(building.Id);
        var message = $"{def.Name} switched {(c.On ? "on" : "off")} the grid.";
        state.Log(new(state.Tick, ColonyEventType.PowerSwitched, message, BuildingId: building.Id));
        return CommandResult.Success(message, building.Id);
    }
}
