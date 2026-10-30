using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Data;

/// <summary>
/// data/electronics.json: the electronics and power feature in one file, merged into the other data at load (like
/// gear_mods.json). Its <see cref="Items"/> join items.json's (the scavenge data), its <see cref="Recipes"/> and
/// <see cref="Buildings"/> join recipes.json's and buildings.json's, and <see cref="Disassembly"/> becomes one recipe per
/// salvageable item. <see cref="Power"/> and <see cref="Charging"/> drive the colony grid (<see cref="Colony.Power"/>).
/// </summary>
public sealed record ElectronicsDef
{
    public List<ItemDef> Items { get; init; } = new();
    public List<RecipeDef> Recipes { get; init; } = new();
    public List<BuildingDef> Buildings { get; init; } = new();

    /// <summary>Enemy id → extra drops (Linked parts to strip).</summary>
    public Dictionary<string, List<EnemyDropDef>> Drops { get; init; } = new();

    public DisassemblyDef Disassembly { get; init; } = new();
    public PowerDef Power { get; init; } = new();
    public ChargingDef Charging { get; init; } = new();

    /// <summary>The disassembly table as bench recipes (<c>disassemble_{item}</c>), in table order.</summary>
    public IEnumerable<RecipeDef> DisassemblyRecipes() =>
        Disassembly.Items.Select(kv =>
        {
            var yields = kv.Value.Yields.ToList();
            return new RecipeDef
            {
                Id = DisassemblyDef.RecipePrefix + kv.Key,
                Name = $"Strip {kv.Value.Name ?? kv.Key}",
                Building = Disassembly.Building,
                MinLevel = Disassembly.MinLevel,
                Work = kv.Value.Work,
                ItemInputs = new() { [kv.Key] = 1 },
                Item = yields.Count > 0 ? yields[0].Key : null,
                Amount = yields.Count > 0 ? yields[0].Value : 1,
                Byproducts = yields.Skip(1).ToDictionary(y => y.Key, y => y.Value),
                ResearchChance = kv.Value.ResearchChance,
                Group = "Disassembly",
            };
        });

    /// <summary>Every recipe this file adds (its own and the disassembly ones).</summary>
    public IEnumerable<RecipeDef> AllRecipes() => Recipes.Concat(DisassemblyRecipes());
}

/// <summary>Taking found devices apart at the bench: per item, what it yields and the chance of Research Data.</summary>
public sealed record DisassemblyDef
{
    public const string RecipePrefix = "disassemble_";

    public string Building { get; init; } = "electronics_lab";
    public int MinLevel { get; init; } = 1;

    /// <summary>
    /// Chance per yielded component (each one) of one more, per point of <c>disassembly_yield_bonus</c> on the bench's best
    /// worker (Circuit Tinkerer), plus <see cref="SkillYieldPerPoint"/> per Electronics skill point.
    /// </summary>
    public double SkillYieldPerPoint { get; init; } = 0.002;

    /// <summary>Item id → what stripping one gives. Ordered: the first yield is the recipe's main output.</summary>
    public Dictionary<string, DisassemblyEntry> Items { get; init; } = new();
}

public sealed record DisassemblyEntry
{
    public string? Name { get; init; }
    public double Work { get; init; } = 4;
    public Dictionary<string, int> Yields { get; init; } = new();
    public double ResearchChance { get; init; }
}

/// <summary>
/// The colony grid. Generators (by building id) make power per tick, storage banks hold it, consumers draw it in list order
/// (priority: earlier first) and get a bonus while powered. Nothing needs power to work: an unpowered building simply
/// misses its bonus (and tier 3 electronics wait), so the early game is untouched.
/// </summary>
public sealed record PowerDef
{
    public Dictionary<string, GeneratorDef> Generators { get; init; } = new();
    public Dictionary<string, StorageDef> Storage { get; init; } = new();
    public List<ConsumerDef> Consumers { get; init; } = new();

    /// <summary>Solar output share per day phase (missing phases: 0).</summary>
    public Dictionary<DayPhase, double> SolarByPhase { get; init; } = new();

    /// <summary>Demand above this share of what the grid can supply, for <see cref="OverloadTicks"/> in a row, is an overload.</summary>
    public double OverloadRatio { get; init; } = 1.5;

    /// <summary>Ticks of overload in a row after which the breaker trips: every consumer is cut for <see cref="BreakerTicks"/>.</summary>
    public int OverloadTicks { get; init; } = 6;
    public int BreakerTicks { get; init; } = 2;
}

/// <summary>
/// A generator building. <see cref="Output"/> per level (power per tick at full); a fuel generator burns the first of its
/// <see cref="Fuel"/> the stockpile has (vehicles' fuel when there is such a resource, else wood) per unit of power it
/// actually supplies; a solar one makes <see cref="Output"/> × the phase share × the weather factor.
/// </summary>
public sealed record GeneratorDef
{
    public List<double> Output { get; init; } = new();
    public List<FuelDef> Fuel { get; init; } = new();
    public bool Solar { get; init; }
}

public sealed record FuelDef
{
    public string Resource { get; init; } = "";

    /// <summary>Resource burnt per unit of power supplied.</summary>
    public double PerPower { get; init; } = 0.1;
}

/// <summary>A battery bank: holds up to <see cref="Capacity"/> power, moving at most <see cref="Rate"/> in or out per tick (per level).</summary>
public sealed record StorageDef
{
    public List<double> Capacity { get; init; } = new();
    public List<double> Rate { get; init; } = new();
}

/// <summary>What a powered consumer gets.</summary>
public enum PowerEffect
{
    /// <summary>A hook only: another feature reads <c>Power.IsPowered</c> (cold cellar, turrets, radio mast).</summary>
    None,
    /// <summary>Crafting effort × (1 + value); recipes with <c>needsPower</c> only progress while powered.</summary>
    CraftSpeed,
    /// <summary>Surgery success + value at this Med Bay.</summary>
    SurgeryBonus,
    /// <summary>Night Shift output × (1 + value) (the <c>floodlight</c> consumer: any building with a Floodlight, after dusk).</summary>
    NightShiftBonus,
    /// <summary>The <c>charging</c> consumer: the bench that recharges drained devices (draw per item charging).</summary>
    Charging,
}

/// <summary>
/// A building that draws power: <see cref="Draw"/> per level (two special ids: <c>floodlight</c> is every building with a
/// Floodlight fitted, drawing at dusk and night; <c>charging</c> is the charging bench, drawing per device it charges) (from <see cref="MinLevel"/>; a 0 draw is not on the grid at that
/// level). A consumer for a building type the data does not have (another feature's) is ignored until it exists.
/// </summary>
public sealed record ConsumerDef
{
    public string Building { get; init; } = "";
    public int MinLevel { get; init; } = 1;
    public List<double> Draw { get; init; } = new();
    public PowerEffect Effect { get; init; }
    public double Value { get; init; }

    /// <summary>One line for tooltips ("+25% crafting speed").</summary>
    public string Describe { get; init; } = "";

    public double DrawAt(int level) =>
        level < MinLevel || Draw.Count == 0 ? 0 : Draw[Math.Clamp(level - 1, 0, Draw.Count - 1)];
}

/// <summary>
/// Device charging at home. A drained device (charges spent in the field) needs <see cref="WorkPerCharge"/> per charge;
/// the charging bench does <see cref="PoweredRate"/> per tick while the grid feeds it (<see cref="DrawPerItem"/> each, up
/// to <see cref="MaxAtOnce"/> items), else <see cref="UnpoweredRate"/> (hand-cranked). A powered parked vehicle adds its
/// own rate through <c>Power.VehicleChargeRate</c>.
/// </summary>
public sealed record ChargingDef
{
    public double WorkPerCharge { get; init; } = 3;
    public double PoweredRate { get; init; } = 1;
    public double UnpoweredRate { get; init; } = 0.2;
    public double DrawPerItem { get; init; } = 0.5;
    public int MaxAtOnce { get; init; } = 4;
}

internal static class ElectronicsValidation
{
    /// <summary>Colony-side checks (buildings, recipes): generators, storage and defaults name real buildings and recipes.</summary>
    public static void Validate(ElectronicsDef def, GameData data)
    {
        foreach (var (id, g) in def.Power.Generators)
        {
            if (!data.Buildings.TryGetValue(id, out var b)) throw new GameDataException($"electronics.json power.generators: unknown building '{id}'.");
            if (g.Output.Count != b.MaxLevel || g.Output.Any(o => o < 0))
                throw new GameDataException($"electronics.json generator '{id}': one non-negative output per level.");
            if (!g.Solar && g.Fuel.Count == 0) throw new GameDataException($"electronics.json generator '{id}': a fuel generator needs fuel.");
            if (g.Fuel.All(f => !data.Resources.ContainsKey(f.Resource)) && !g.Solar)
                throw new GameDataException($"electronics.json generator '{id}': none of its fuels is a known resource.");
        }
        foreach (var (id, s) in def.Power.Storage)
            if (!data.Buildings.TryGetValue(id, out var b) || s.Capacity.Count != b.MaxLevel || s.Rate.Count != b.MaxLevel)
                throw new GameDataException($"electronics.json power.storage '{id}': a known building with capacity and rate per level.");
        foreach (var c in def.Power.Consumers)
            if (c.Draw.Any(d => d < 0)) throw new GameDataException($"electronics.json consumer '{c.Building}': draws must not be negative.");
        if (def.Power.OverloadRatio < 1) throw new GameDataException("electronics.json power: overloadRatio at least 1.");
        if (def.Power.OverloadTicks < 1 || def.Power.BreakerTicks < 0)
            throw new GameDataException("electronics.json power: overloadTicks at least 1, breakerTicks not negative.");
        var ch = def.Charging;
        if (ch.WorkPerCharge <= 0 || ch.PoweredRate < 0 || ch.UnpoweredRate < 0 || ch.MaxAtOnce < 1)
            throw new GameDataException("electronics.json charging: positive workPerCharge, non-negative rates, maxAtOnce at least 1.");
    }

    /// <summary>Field-side checks (items): charges, EMP and decoy numbers, drone roles, disassembly yields.</summary>
    public static void Validate(ElectronicsDef def, ScavengeData data)
    {
        foreach (var item in data.Items.Values.Where(i => i.BaseId is null))
        {
            var where = $"Item '{item.Id}'";
            if (item.Use is { Charges: < 0 }) throw new GameDataException($"{where}: charges must not be negative.");
            if (item.Use?.Emp is { } emp && (emp.Radius < 1 || emp.Rounds < 1)) throw new GameDataException($"{where}: an EMP needs radius and rounds of at least 1.");
            if (item.Use?.Decoy is { } d && (d.Rounds < 1 || d.Hp <= 0)) throw new GameDataException($"{where}: a decoy needs rounds of at least 1 and positive hp.");
            if (item.Radio is { Range: < 1 }) throw new GameDataException($"{where}: a radio needs a range of at least 1.");
            if (item.HackTool is { TraceChance: < 0 or > 1 }) throw new GameDataException($"{where}: traceChance must be in 0..1.");
            if (item.Drone is { } drone)
            {
                if (drone.Role == DroneRole.Cargo && drone.Cargo < 1) throw new GameDataException($"{where}: a cargo drone needs cargo of at least 1.");
                if (drone.Role == DroneRole.Combat && (drone.Weapon is not { } w || !data.Items.TryGetValue(w, out var gun) || gun.Actions.Count == 0))
                    throw new GameDataException($"{where}: a combat drone needs a weapon item with an attack.");
                if (drone.RequiresHook is { } hook && !PerkHooks.All.ContainsKey(hook))
                    throw new GameDataException($"{where}: unknown hook '{hook}'.");
                if (drone.ActionApCost < 1) throw new GameDataException($"{where}: a drone's action costs at least 1 AP.");
            }
        }
        foreach (var (id, entry) in def.Disassembly.Items)
        {
            if (!data.Items.ContainsKey(id)) throw new GameDataException($"electronics.json disassembly: unknown item '{id}'.");
            if (entry.Yields.Count == 0 || entry.Yields.Values.Any(v => v < 1) || entry.Yields.Keys.Any(k => !data.Items.ContainsKey(k)))
                throw new GameDataException($"electronics.json disassembly '{id}': yields of known items, at least 1 each.");
            if (entry.ResearchChance is < 0 or > 1 || entry.Work <= 0)
                throw new GameDataException($"electronics.json disassembly '{id}': positive work and a research chance in 0..1.");
        }
    }
}
