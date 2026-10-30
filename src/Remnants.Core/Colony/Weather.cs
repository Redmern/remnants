using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>A stretch of one kind of weather over a region, from <see cref="Start"/> (inclusive) to <see cref="End"/>.</summary>
public sealed record WeatherSpell(int Index, string Kind, long Start, long End, double Intensity)
{
    public long Hours => End - Start;
    public bool Covers(long tick) => tick >= Start && tick < End;
}

/// <summary>
/// The weather's own state in the save: the seed of every region's chain and the spells worked out so far (a cache:
/// each spell follows from the seed, the region and the one before it, so it never depends on when it was asked for).
/// </summary>
public sealed class WeatherState
{
    public ulong Seed { get; set; }

    /// <summary>Region → its spells in order, from a little before now onward.</summary>
    public SortedDictionary<string, List<WeatherSpell>> Spells { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One spell as the colony's forecast calls it: <see cref="Called"/> may be wrong for spells ahead.</summary>
public sealed record ForecastEntry(long Start, long End, string Called, double Intensity, bool Current);

/// <summary>The colony's forecast: how far it sees, how sure it is of the next spell, and the spells in that window.</summary>
public sealed record WeatherForecast(int Days, double Accuracy, IReadOnlyList<ForecastEntry> Entries);

/// <summary>
/// The weather: a seeded Markov chain of spells per region (data/weather.json), the forecast, and its effects at home.
/// Deterministic and separate from the colony RNG: the same seed gives the same weather whatever the colony does, so
/// turning weather on changes no other roll (lightning strikes are the one exception: they draw from the colony RNG,
/// only while a storm with lightning is overhead).
/// </summary>
public static class Weather
{
    /// <summary>Spells older than this many ticks are dropped from the cache.</summary>
    internal const long KeepPastTicks = 72;

    public static bool Enabled(GameData data) => data.Weather.Enabled;

    /// <summary>The spell over a region at a tick (always clear with weather off).</summary>
    public static WeatherSpell SpellAt(ColonyState state, GameData data, string region, long tick)
    {
        var rules = data.Weather;
        if (!rules.Enabled) return new WeatherSpell(0, WeatherRules.Clear, long.MinValue / 4, long.MaxValue / 4, 0);
        var spells = Chain(state, rules, region);
        Extend(state.Weather.Seed, rules, region, spells, tick);
        for (var i = spells.Count - 1; i >= 0; i--)
            if (spells[i].Start <= tick) return spells[i];
        return spells[0];
    }

    /// <summary>The spells over a region that overlap [from, to).</summary>
    public static List<WeatherSpell> SpellsBetween(ColonyState state, GameData data, string region, long from, long to)
    {
        var first = SpellAt(state, data, region, from);
        if (!data.Weather.Enabled) return [first];
        var spells = Chain(state, data.Weather, region);
        Extend(state.Weather.Seed, data.Weather, region, spells, to);
        return spells.Where(s => s.End > from && s.Start < to && s.Index >= first.Index).ToList();
    }

    /// <summary>The weather at home now.</summary>
    public static WeatherSpell Current(ColonyState state, GameData data) => SpellAt(state, data, data.Weather.ColonyRegion, state.Tick);

    public static WeatherKindDef KindOf(GameData data, WeatherSpell spell) => data.Weather.Kind(spell.Kind);

    /// <summary>Scout drones stay on the ground in this weather (a dust storm, heavy rain).</summary>
    public static bool GroundsDrones(GameData data, WeatherSpell spell)
    {
        var kind = KindOf(data, spell);
        return kind.GroundsDrones || (kind.GroundsDronesFrom is { } from && spell.Intensity + 1e-9 >= from);
    }

    /// <summary>The region a zone lies in, for its weather (the colony's own region when it has none).</summary>
    public static string RegionOf(GameData data, ZoneDef zone) => string.IsNullOrEmpty(zone.Region) ? data.Weather.ColonyRegion : zone.Region;

    // ---- forecast ----

    /// <summary>Days ahead the colony sees: by Town Hall level, plus a day with a good Electronics Lab.</summary>
    public static int ForecastDays(ColonyState state, GameData data)
    {
        var f = data.Weather.Forecast;
        var hall = Level(state, data.Rules.TownHallBuildingId);
        if (hall < 1) return 0;
        var days = f.DaysByTownHallLevel[Math.Min(hall, f.DaysByTownHallLevel.Count) - 1];
        return days + (Level(state, f.LabBuildingId) >= f.LabMinLevel ? f.LabDays : 0);
    }

    /// <summary>Chance the next spell is called right: base, a sky-reading instrument in the stores, the Lab's level.</summary>
    public static double ForecastAccuracy(ColonyState state, GameData data)
    {
        var f = data.Weather.Forecast;
        var acc = f.Accuracy + f.LabAccuracyPerLevel * Level(state, f.LabBuildingId);
        if (f.InstrumentItems.Any(id => state.Inventory.Has(id))) acc += f.InstrumentAccuracy;
        return Math.Clamp(acc, 0, f.MaxAccuracy);
    }

    /// <summary>
    /// The forecast at home: the spell now (known), and every spell that starts within the forecast's days, each called
    /// right with the forecast's accuracy less <see cref="ForecastRules.AccuracyLossPerDay"/> per day it lies ahead. A miss
    /// names the kind it is confused with. Stable: the same spell is always called the same way at the same accuracy.
    /// </summary>
    public static WeatherForecast Forecast(ColonyState state, GameData data)
    {
        var rules = data.Weather;
        var days = ForecastDays(state, data);
        var accuracy = ForecastAccuracy(state, data);
        if (!rules.Enabled) return new WeatherForecast(days, 1, []);
        var now = state.Tick;
        var region = rules.ColonyRegion;
        var entries = new List<ForecastEntry>();
        foreach (var spell in SpellsBetween(state, data, region, now, now + Math.Max(1, days) * data.Rules.TicksPerDay))
        {
            var current = spell.Covers(now);
            if (!current && days == 0) break;
            var called = spell.Kind;
            if (!current)
            {
                var ahead = (spell.Start - now) / (double)data.Rules.TicksPerDay;
                var chance = Math.Clamp(accuracy - rules.Forecast.AccuracyLossPerDay * Math.Floor(ahead), 0, 1);
                var roll = new DeterministicRng(Mix(state.Weather.Seed, region, spell.Index, 0xF0CA57)).NextDouble();
                if (roll >= chance) called = rules.Kind(spell.Kind).ConfusedWith ?? spell.Kind;
            }
            entries.Add(new ForecastEntry(spell.Start, spell.End, called, spell.Intensity, current));
        }
        return new WeatherForecast(days, accuracy, entries);
    }

    // ---- colony effects ----

    /// <summary>
    /// The weather's share of a worker's output this tick at a building: outdoor work slows or stops (a mask lets it go on
    /// in a dust storm), children stay in when the kind says so, the job's own multiplier (rain on the farm), and a
    /// building struck by lightning works at <see cref="RepairRules.DamagedOutput"/> until repaired.
    /// </summary>
    public static double WorkFactor(GameData data, WeatherSpell spell, Building building, BuildingDef def, Survivor worker, bool masked)
    {
        var f = building.Damaged ? data.Weather.Repair.DamagedOutput : 1;
        var colony = KindOf(data, spell).Colony;
        if (def.Outdoor)
        {
            if (colony.ChildrenStayIn && worker.IsChild) return 0;
            f *= masked && colony.MaskedOutput is { } m ? m : colony.OutdoorOutput;
        }
        if (def.Job is { } job && colony.JobOutput.TryGetValue(job, out var j)) f *= j;
        return f;
    }

    /// <summary>Dust masks in the stores (each covers one outdoor worker).</summary>
    public static int Masks(ColonyState state, GameData data) => data.Weather.MaskItems.Sum(id => state.Inventory.Count(id));

    /// <summary>
    /// Outdoor workers on shift now who wear a mask: the first <see cref="Masks"/> of them by id, when the weather makes
    /// masks matter; empty otherwise.
    /// </summary>
    public static HashSet<int> Masked(ColonyState state, GameData data, WeatherSpell spell)
    {
        if (KindOf(data, spell).Colony.MaskedOutput is null) return [];
        var masks = Masks(state, data);
        if (masks <= 0) return [];
        return OutdoorCrew(state, data).Take(masks).Select(s => s.Id).ToHashSet();
    }

    /// <summary>Workers on shift at outdoor buildings now (by the clock only), by id.</summary>
    public static IEnumerable<Survivor> OutdoorCrew(ColonyState state, GameData data)
    {
        var hour = DayNight.Hour(state.Tick, data.Rules);
        return state.Survivors
            .Where(s => !s.Away && s.PatientAtBuildingId is null && s.AssignedBuildingId is { } id
                        && state.FindBuilding(id) is { } b && data.Buildings[b.DefId].Outdoor
                        && Schedule.Factor(s, b, data.Buildings[b.DefId], hour, data.Rules.DayNight) > 0)
            .OrderBy(s => s.Id);
    }

    /// <summary>The colony is in the weather's grip: work at outdoor buildings is slowed.</summary>
    public static bool SlowsOutdoorWork(GameData data, WeatherSpell spell) => KindOf(data, spell).Colony.OutdoorOutput < 1 - 1e-9;

    // ---- travel ----

    /// <summary>
    /// The weather's term in a trip's travel time (<see cref="WorldMap.Travel"/>): the worse of the weather at home and over
    /// the zone when the party sets out adds its days; a kind that closes far routes, raging over the zone, blocks the
    /// trip out. Doorstep zones are never affected. Null when the weather changes nothing.
    /// </summary>
    public static TravelModifier? TravelTerm(ColonySim sim, ZoneDef zone, long departTick, bool homeward)
    {
        var data = sim.Data;
        if (!data.Weather.Enabled || zone.TravelDays <= 0) return null;
        var home = SpellAt(sim.State, data, data.Weather.ColonyRegion, departTick);
        var there = SpellAt(sim.State, data, RegionOf(data, zone), departTick);
        var worst = KindOf(data, home).Travel.ExtraDays >= KindOf(data, there).Travel.ExtraDays ? home : there;
        var extra = (long)Math.Round(KindOf(data, worst).Travel.ExtraDays * data.Rules.TicksPerDay);
        var blocks = !homeward && KindOf(data, there).Travel.BlocksFromDays is { } from && zone.TravelDays + 1e-9 >= from;
        if (extra == 0 && !blocks) return null;
        var reason = blocks
            ? $"{KindOf(data, there).Name} over {zone.Region}: the route is closed"
            : $"{KindOf(data, worst).Name}: +{extra / (double)data.Rules.TicksPerDay:0.#} days on the road";
        return new TravelModifier("weather", reason, ExtraTicks: extra, Blocks: blocks);
    }

    // ---- internals ----

    private static int Level(ColonyState state, string defId) =>
        state.Buildings.Where(b => b.DefId == defId).Select(b => b.Level).DefaultIfEmpty(0).Max();

    private static List<WeatherSpell> Chain(ColonyState state, WeatherRules rules, string region)
    {
        if (!state.Weather.Spells.TryGetValue(region, out var spells)) state.Weather.Spells[region] = spells = new List<WeatherSpell>();
        return spells;
    }

    /// <summary>Works out spells until one reaches past <paramref name="tick"/>. Spell 0 starts at tick 0 in the start kind.</summary>
    private static void Extend(ulong seed, WeatherRules rules, string region, List<WeatherSpell> spells, long tick)
    {
        if (spells.Count == 0) spells.Add(Roll(seed, rules, region, 0, rules.Start, 0, first: true));
        while (spells[^1].End <= tick)
        {
            var last = spells[^1];
            spells.Add(Roll(seed, rules, region, last.Index + 1, last.Kind, last.End, first: false));
        }
    }

    private static WeatherSpell Roll(ulong seed, WeatherRules rules, string region, int index, string previous, long start, bool first)
    {
        var rng = new DeterministicRng(Mix(seed, region, index, 0x5EA7));
        var kind = first ? previous : PickNext(rng, rules, region, previous);
        var def = rules.Kind(kind);
        var hours = rng.NextInclusive(def.Hours[0], def.Hours[1]);
        var intensity = def.Intensity[0] + (def.Intensity[1] - def.Intensity[0]) * rng.NextDouble();
        return new WeatherSpell(index, kind, start, start + hours, Math.Round(intensity, 3));
    }

    private static string PickNext(DeterministicRng rng, WeatherRules rules, string region, string previous)
    {
        var weights = rules.Next.GetValueOrDefault(previous) ?? new Dictionary<string, double>();
        var mults = rules.Regions.GetValueOrDefault(region);
        var options = weights.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (Kind: kv.Key, Weight: kv.Value * (mults?.GetValueOrDefault(kv.Key, 1) ?? 1)))
            .Where(o => o.Weight > 0).ToList();
        var total = options.Sum(o => o.Weight);
        if (total <= 0) return rules.Start;
        var roll = rng.NextDouble() * total;
        foreach (var o in options)
        {
            roll -= o.Weight;
            if (roll < 0) return o.Kind;
        }
        return options[^1].Kind;
    }

    /// <summary>FNV-1a over the seed, the region's name, the spell index and a salt, finished with a mix.</summary>
    internal static ulong Mix(ulong seed, string region, long index, ulong salt)
    {
        var h = 14695981039346656037UL;
        void Add(ulong v)
        {
            for (var i = 0; i < 8; i++)
            {
                h ^= (v >> (8 * i)) & 0xFF;
                h *= 1099511628211UL;
            }
        }
        Add(seed);
        foreach (var ch in region) Add(ch);
        Add((ulong)index);
        Add(salt);
        h ^= h >> 33;
        h *= 0xff51afd7ed558ccdUL;
        h ^= h >> 33;
        return h == 0 ? 1 : h;
    }

    /// <summary>The weather seed of a colony that has none yet (new games and older saves): from its RNG state.</summary>
    internal static ulong SeedFrom(ulong rngState) => Mix(rngState, "weather", 0, 0xC0FFEE);
}

/// <summary>
/// Weather at home each tick (after day and night): a log line when the weather at home turns, the mood it costs,
/// health lost by outdoor workers without a mask in a dust storm, lightning striking a building, and the spell cache
/// trimmed.
/// </summary>
internal static class WeatherSystem
{
    /// <summary>Dust never takes an unmasked worker's health below this.</summary>
    private const double DustHealthFloor = 25;

    public static void Run(ColonyState state, GameData data, DeterministicRng rng)
    {
        var rules = data.Weather;
        if (!rules.Enabled) return;
        var spell = Weather.Current(state, data);
        var kind = rules.Kind(spell.Kind);
        if (spell.Start == state.Tick && spell.Index > 0) LogChange(state, data, spell);

        var colony = kind.Colony;
        if (colony.MoodPerTick > 0)
            foreach (var s in state.Survivors.Where(s => !s.Away))
                s.Mood = Math.Clamp(s.Mood - colony.MoodPerTick, 0, Survivor.MaxMood);

        if (colony.UnmaskedHealthPerTick > 0 && colony.MaskedOutput is not null)
        {
            var masked = Weather.Masked(state, data, spell);
            foreach (var s in Weather.OutdoorCrew(state, data).Where(s => !masked.Contains(s.Id) && !(colony.ChildrenStayIn && s.IsChild)))
                if (s.Health > DustHealthFloor) s.Health = Math.Max(DustHealthFloor, s.Health - colony.UnmaskedHealthPerTick);
        }

        if (colony.LightningChancePerTick > 0 && state.Buildings.Count > 0
            && rng.NextDouble() < colony.LightningChancePerTick * spell.Intensity)
        {
            var targets = state.Buildings.Where(b => !b.Damaged).OrderBy(b => b.Id).ToList();
            if (targets.Count > 0)
            {
                var hit = rng.Pick(targets);
                hit.Damaged = true;
                var name = data.Buildings[hit.DefId].Name;
                state.Log(new(state.Tick, ColonyEventType.LightningStruck,
                    $"Lightning struck the {name}: it works at half pace until repaired.", BuildingId: hit.Id));
            }
        }

        foreach (var spells in state.Weather.Spells.Values)
        {
            var old = spells.FindLastIndex(s => s.End < state.Tick - Weather.KeepPastTicks);
            if (old >= 0 && old < spells.Count - 1) spells.RemoveRange(0, old + 1);
        }
    }

    private static void LogChange(ColonyState state, GameData data, WeatherSpell spell)
    {
        var rules = data.Weather;
        var kind = rules.Kind(spell.Kind);
        var before = Weather.SpellAt(state, data, rules.ColonyRegion, spell.Start - 1);
        var was = rules.Kind(before.Kind);
        var message = spell.Kind switch
        {
            WeatherRules.Clear => was.Severe ? $"The {was.Name.ToLowerInvariant()} has passed. Clear skies." : "The sky clears.",
            _ when kind.Severe => $"A {kind.Name.ToLowerInvariant()} rolls in ({spell.Hours} h).",
            _ => $"{kind.Name} sets in.",
        };
        state.Log(new(state.Tick, ColonyEventType.WeatherChanged, message));
    }
}

internal static class WeatherHandlers
{
    public static CommandResult Repair(ColonySim sim, RepairBuilding c)
    {
        if (sim.State.FindBuilding(c.BuildingId) is not { } building)
            return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = sim.Data.Buildings[building.DefId];
        if (!building.Damaged) return CommandResult.Fail(CommandError.NotDamaged, $"{def.Name} needs no repair.");
        var cost = sim.Data.Weather.Repair.Cost;
        if (!sim.State.Stockpile.CanAfford(cost))
            return CommandResult.Fail(CommandError.CannotAfford,
                $"Cannot afford the repair: {CommandHandlers.Describe(sim.State.Stockpile.Shortfall(cost))}.");
        sim.State.Stockpile.Spend(cost);
        building.Damaged = false;
        sim.State.Log(new(sim.State.Tick, ColonyEventType.Repaired, $"Repaired the {def.Name}.", BuildingId: building.Id));
        return CommandResult.Success($"Repaired the {def.Name}.", building.Id);
    }
}
