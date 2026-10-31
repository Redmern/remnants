using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>Which site idle hands go to first; Low gets assigned builders only.</summary>
public enum SitePriority { Low, Normal, High }

/// <summary>The colony's construction sites (see <see cref="ConstructionSystem"/>).</summary>
public sealed class ConstructionState
{
    /// <summary>Sites in the order they were placed.</summary>
    public List<ConstructionSite> Sites { get; set; } = new();

    /// <summary>The site of a building: a new one being built under its future id, or an upgrade of a standing one.</summary>
    public ConstructionSite? Find(int buildingId) => Sites.Find(s => s.BuildingId == buildingId);

    /// <summary>The site a survivor builds at (as an assigned builder or an idle helper), if any.</summary>
    public ConstructionSite? SiteOf(int survivorId) =>
        Sites.Find(s => s.Builders.Contains(survivorId)) ?? Sites.Find(s => s.Helpers.Contains(survivorId));
}

/// <summary>
/// A building being built or upgraded. Its materials left the stockpile when it was placed (<see cref="Materials"/>);
/// builders haul them over (<see cref="Delivered"/>) and put in work towards <see cref="WorkNeeded"/>, never more than
/// the delivered share allows. A new building takes the id <see cref="BuildingId"/> when it is done; an upgrade site
/// keeps the standing building working at its old level until then.
/// </summary>
public sealed class ConstructionSite
{
    /// <summary>The building's id: the one it gets when finished (new), or the one being upgraded.</summary>
    public int BuildingId { get; set; }
    public string DefId { get; set; } = "";
    public int Plot { get; set; }

    /// <summary>The level being built: 1 for a new building, else the upgrade's target.</summary>
    public int Level { get; set; } = 1;

    public double Work { get; set; }
    public double WorkNeeded { get; set; }

    private SortedDictionary<string, double> _materials = new(StringComparer.Ordinal);
    private SortedDictionary<string, double> _delivered = new(StringComparer.Ordinal);

    /// <summary>Everything the level costs, taken from the stockpile when the site was placed (ordinal order, also after a load).</summary>
    public SortedDictionary<string, double> Materials
    {
        get => _materials;
        set => _materials = new(value, StringComparer.Ordinal);
    }

    /// <summary>The part of <see cref="Materials"/> hauled to the site so far.</summary>
    public SortedDictionary<string, double> Delivered
    {
        get => _delivered;
        set => _delivered = new(value, StringComparer.Ordinal);
    }

    /// <summary>Survivors assigned to build here (in assignment order).</summary>
    public List<int> Builders { get; set; } = new();

    /// <summary>Idle survivors lending a hand this tick (worked out each tick from the priorities).</summary>
    public List<int> Helpers { get; set; } = new();

    public bool Paused { get; set; }
    public SitePriority Priority { get; set; } = SitePriority.Normal;

    /// <summary>Builders keep going after the day shift (an upgrade of a lit building only; see <see cref="ConstructionSystem.IsLit"/>).</summary>
    public bool NightShift { get; set; }

    public long StartedTick { get; set; }

    public bool IsUpgrade => Level > 1;

    public double MaterialsTotal => Materials.Values.Sum();
    public double DeliveredTotal => Delivered.Values.Sum();

    /// <summary>Share of the materials on site (1 when the level costs nothing).</summary>
    public double DeliveredShare => MaterialsTotal <= 1e-9 ? 1 : Math.Clamp(DeliveredTotal / MaterialsTotal, 0, 1);

    /// <summary>Share of the work done (1 when the level needs none).</summary>
    public double Progress => WorkNeeded <= 1e-9 ? 1 : Math.Clamp(Work / WorkNeeded, 0, 1);

    /// <summary>Materials still in the stockpile's reserve, waiting to be hauled.</summary>
    public Dictionary<string, double> Reserved() =>
        Materials.ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value - Delivered.GetValueOrDefault(kv.Key)));

    public bool Done => Progress >= 1 - 1e-9 && DeliveredShare >= 1 - 1e-9;
}

/// <summary>What a site's crew does over the coming hours (for the panel and tooltip).</summary>
public sealed record SiteForecast(int Builders, int Helpers, double WorkPerHour, double HaulPerHour, double? EtaHours, bool Blocked, string? BlockedBy);

/// <summary>
/// Construction sites. Each tick (after production) every site that is not paused gets its crew's hour: assigned
/// builders first, then idle helpers by site priority. A builder on shift hauls materials while the delivered share
/// trails the work share by less than <c>haulLead</c>, and builds otherwise; children help at the child rate. Work
/// trains the Construction skill. The site follows the schedule (day shift; a Night Shift only at a lit upgrade) and
/// the weather (<c>buildOutput</c>: a dust storm halts building). When the work is done and everything is delivered,
/// the building appears (or reaches its new level) and the builders are free.
/// </summary>
public static class ConstructionSystem
{
    internal static void Run(ColonySim sim)
    {
        var state = sim.State;
        if (state.Construction.Sites.Count == 0) return;
        var data = sim.Data;
        var rules = data.Rules.Construction;
        var job = data.Jobs.GetValueOrDefault(rules.Job);
        Tidy(sim);
        AssignHelpers(sim);

        var hour = DayNight.Hour(state.Tick, data.Rules);
        var spell = Weather.Current(state, data);
        var masks = Math.Max(0, Weather.Masks(state, data) - Weather.OutdoorCrew(state, data).Count());
        foreach (var site in Ordered(state).ToList())
        {
            if (site.Paused) continue;
            foreach (var (s, share) in Crew(state, data, site))
            {
                var f = ShiftFactor(sim, site, s, hour);
                if (f <= 0) continue;
                var masked = false;
                if (Weather.KindOf(data, spell).Colony is { MaskedOutput: not null, BuildOutput: null } && masks > 0 && !s.IsChild)
                {
                    masked = true;
                    masks--;
                }
                f *= WeatherFactor(data, spell, s, masked) * share;
                if (f <= 0) continue;
                Step(sim, site, s, f);
                if (job is not null) Training.Work(state, data, job, s);
            }
            if (site.Done) Complete(sim, site);
        }
    }

    /// <summary>One builder's hour (scaled by <paramref name="f"/>): haul while the materials trail the work, else build.</summary>
    private static void Step(ColonySim sim, ConstructionSite site, Survivor s, double f)
    {
        var rules = sim.Data.Rules.Construction;
        var total = site.MaterialsTotal;
        var delivered = site.DeliveredTotal;
        if (Hauling(site, rules))
            Deliver(site, Math.Min(total - delivered, HaulRate(sim, s) * f));
        else if (site.Work < site.WorkNeeded)
            site.Work = Math.Min(site.WorkNeeded * site.DeliveredShare, site.Work + WorkRate(sim, s) * f);
    }

    /// <summary>The crew hauls now: materials are still to come and trail the work by less than <c>haulLead</c> (or work has caught up).</summary>
    public static bool Hauling(ConstructionSite site, ConstructionRules rules) =>
        site.DeliveredTotal < site.MaterialsTotal - 1e-9
        && (site.DeliveredShare < site.Progress + rules.HaulLead || site.Work >= site.WorkNeeded * site.DeliveredShare - 1e-9);

    /// <summary>Hauls <paramref name="amount"/> units, each material in proportion to what is still to come.</summary>
    private static void Deliver(ConstructionSite site, double amount)
    {
        var left = site.Reserved();
        var owed = left.Values.Sum();
        if (owed <= 1e-9 || amount <= 0) return;
        var take = Math.Min(1, amount / owed);
        foreach (var (res, need) in left)
            if (need > 0)
                site.Delivered[res] = take >= 1 ? site.Materials[res] : site.Delivered.GetValueOrDefault(res) + need * take;
    }

    private static readonly Dictionary<double, BuildingDef> SiteDefs = new();

    /// <summary>A builder's work units an hour on shift at full weather: the construction job's output formula.</summary>
    public static double WorkRate(ColonySim sim, Survivor s)
    {
        var rules = sim.Data.Rules.Construction;
        if (sim.Data.Jobs.GetValueOrDefault(rules.Job) is not { } job) return 0;
        BuildingDef def;
        lock (SiteDefs)
            if (!SiteDefs.TryGetValue(rules.WorkPerHour, out def!))
                SiteDefs[rules.WorkPerHour] = def = new BuildingDef { Id = "construction_site", BaseOutput = rules.WorkPerHour, Levels = [new()] };
        return JobOutput.PerTick(def, 1, job, s, sim.Data.Rules.Output, sim.Data);
    }

    /// <summary>Material units a builder hauls an hour on shift: <c>haulPerHour</c> · health · the child rate.</summary>
    public static double HaulRate(ColonySim sim, Survivor s)
    {
        var rules = sim.Data.Rules.Construction;
        var output = sim.Data.Rules.Output;
        var hp = Math.Clamp(s.Health / Survivor.MaxHealth, 0, 1);
        return rules.HaulPerHour * (output.LowHealthMultiplier + (1 - output.LowHealthMultiplier) * hp) * JobOutput.LifeFactor(s, output);
    }

    /// <summary>The weather's share of building: <c>buildOutput</c> when the kind sets it, else the outdoor (or masked) rate.</summary>
    public static double WeatherFactor(GameData data, WeatherSpell spell, Survivor s, bool masked)
    {
        var colony = Weather.KindOf(data, spell).Colony;
        if (colony.ChildrenStayIn && s.IsChild) return 0;
        if (colony.BuildOutput is { } b) return b;
        return masked && colony.MaskedOutput is { } m ? m : colony.OutdoorOutput;
    }

    /// <summary>
    /// A site is lit for a Night Shift when it upgrades a building that is lit already (indoors, level ≥ litMinLevel or a
    /// Floodlight). A new building's site is dark.
    /// </summary>
    public static bool IsLit(ColonySim sim, ConstructionSite site) =>
        site.IsUpgrade && sim.State.FindBuilding(site.BuildingId) is { } b && Schedule.IsLit(b, sim.Data.Buildings[b.DefId], sim.Data.Rules.DayNight);

    /// <summary>Share of an hour a builder works at this site now: the day shift, or the site's Night Shift; tired costs.</summary>
    public static double ShiftFactor(ColonySim sim, ConstructionSite site, Survivor s, int hour)
    {
        if (s.Away || s.PatientAtBuildingId is not null) return 0;
        var rules = sim.Data.Rules.DayNight;
        if (!rules.Enabled) return 1;
        var tired = Schedule.IsTired(s, rules) ? rules.Tired.OutputMultiplier : 1;
        if (Schedule.IsDayShiftHour(s, hour, rules)) return tired;
        if (IsNightShiftHour(sim, site, s, hour)) return rules.NightShift.OutputMultiplier * tired;
        return 0;
    }

    private static bool IsNightShiftHour(ColonySim sim, ConstructionSite site, Survivor s, int hour) =>
        site.NightShift && !s.IsChild && !DayNight.InWindow(hour, sim.Data.Rules.DayNight.WorkHours) && IsLit(sim, site);

    /// <summary>For <see cref="Schedule.OnShift"/>: building (or helping) at a running site right now counts as working.</summary>
    public static bool OnShift(ColonyState state, GameData data, Survivor s)
    {
        if (state.Construction.Sites.Count == 0 || state.Construction.SiteOf(s.Id) is not { Paused: false } site) return false;
        if (s.Away || s.PatientAtBuildingId is not null) return false;
        var rules = data.Rules.DayNight;
        if (!rules.Enabled) return true;
        var hour = DayNight.Hour(state.Tick, data.Rules);
        if (Schedule.IsDayShiftHour(s, hour, rules)) return true;
        return site.NightShift && !s.IsChild && !DayNight.InWindow(hour, rules.WorkHours) && site.IsUpgrade
               && state.FindBuilding(site.BuildingId) is { } b && Schedule.IsLit(b, data.Buildings[b.DefId], rules);
    }

    /// <summary>The site's crew with each one's share of output: builders in full, helpers at <c>helperShare</c>.</summary>
    public static IEnumerable<(Survivor Survivor, double Share)> Crew(ColonyState state, GameData data, ConstructionSite site)
    {
        foreach (var id in site.Builders)
            if (state.FindSurvivor(id) is { } s) yield return (s, 1);
        foreach (var id in site.Helpers)
            if (state.FindSurvivor(id) is { } s) yield return (s, data.Rules.Construction.HelperShare);
    }

    /// <summary>Sites by priority (high first), then in the order placed.</summary>
    public static IEnumerable<ConstructionSite> Ordered(ColonyState state) =>
        state.Construction.Sites.OrderByDescending(s => s.Priority).ThenBy(s => s.StartedTick).ThenBy(s => s.BuildingId);

    public static int MaxBuilders(GameData data, ConstructionSite site)
    {
        var level = data.Buildings[site.DefId].Level(site.Level).MaxBuilders;
        return level > 0 ? level : data.Rules.Construction.DefaultMaxBuilders;
    }

    /// <summary>Whether this survivor may build: at home, not a patient, researcher, student or baby; children only as the job allows.</summary>
    public static CommandResult CanBuild(ColonySim sim, Survivor s)
    {
        if (s.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{s.Name} is away on an expedition.");
        if (s.PatientAtBuildingId is not null) return CommandResult.Fail(CommandError.IsPatient, $"{s.Name} is a patient; discharge them first.");
        if (Schematics.Research.IsResearching(sim.State, s.Id))
            return CommandResult.Fail(CommandError.IsResearching, $"{s.Name} is researching at the Town Hall; stop the research first.");
        if (s.IsInfant) return CommandResult.Fail(CommandError.TooYoung, $"{s.Name} is a baby.");
        if (StudySystem.ClassOf(sim.State, s.Id) is not null) return CommandResult.Fail(CommandError.InClass, $"{s.Name} is in class; end the study first.");
        if (s.IsChild && sim.Data.Jobs.GetValueOrDefault(sim.Data.Rules.Construction.Job) is { ChildrenAllowed: false })
            return CommandResult.Fail(CommandError.NotForChildren, "Building is too hard for a child.");
        return CommandResult.Success("");
    }

    /// <summary>Takes a survivor off any site (taking a job, guard duty, an expedition, a bed in the Med Bay).</summary>
    public static void Release(ColonyState state, int survivorId)
    {
        foreach (var site in state.Construction.Sites)
        {
            site.Builders.Remove(survivorId);
            site.Helpers.Remove(survivorId);
        }
    }

    /// <summary>Drops builders who can no longer build (gone, away, a patient, at a job, on guard, in class or research).</summary>
    private static void Tidy(ColonySim sim)
    {
        var state = sim.State;
        foreach (var site in state.Construction.Sites)
            site.Builders.RemoveAll(id => state.FindSurvivor(id) is not { } s || s.AssignedBuildingId is not null
                                          || state.Defense.Guards.ContainsKey(id) || !CanBuild(sim, s).Ok);
    }

    /// <summary>Idle survivors at home (no job, post, site, bed, class or research) fill the open places at sites by priority.</summary>
    private static void AssignHelpers(ColonySim sim)
    {
        var state = sim.State;
        var data = sim.Data;
        foreach (var site in state.Construction.Sites) site.Helpers.Clear();
        if (data.Rules.Construction.HelperShare <= 0) return;
        var builders = state.Construction.Sites.SelectMany(s => s.Builders).ToHashSet();
        var idle = state.Survivors
            .Where(s => s.AssignedBuildingId is null && !builders.Contains(s.Id) && !state.Defense.Guards.ContainsKey(s.Id) && CanBuild(sim, s).Ok)
            .OrderBy(s => s.IsChild ? 1 : 0).ThenBy(s => s.Id).Select(s => s.Id).ToList();
        foreach (var site in Ordered(state))
        {
            if (idle.Count == 0) break;
            if (site.Paused || site.Priority == SitePriority.Low) continue;
            var room = MaxBuilders(data, site) - site.Builders.Count;
            while (room-- > 0 && idle.Count > 0)
            {
                site.Helpers.Add(idle[0]);
                idle.RemoveAt(0);
            }
        }
    }

    /// <summary>The building appears (or reaches its level); the builders are free.</summary>
    internal static void Complete(ColonySim sim, ConstructionSite site)
    {
        var state = sim.State;
        var def = sim.Data.Buildings[site.DefId];
        state.Construction.Sites.Remove(site);
        if (site.IsUpgrade && state.FindBuilding(site.BuildingId) is { } standing)
        {
            standing.Level = site.Level;
            state.Log(new(state.Tick, ColonyEventType.Upgraded, $"{def.Name} reached level {site.Level}.", BuildingId: standing.Id));
            return;
        }
        var building = new Building { Id = site.BuildingId, DefId = def.Id, Level = 1, Plot = site.Plot };
        state.Buildings.Add(building);
        Harvesting.Planted(state, sim.Data, building);
        state.Log(new(state.Tick, ColonyEventType.Constructed, $"Built {def.Name}.", BuildingId: building.Id));
    }

    /// <summary>
    /// Places a site for <paramref name="def"/> at <paramref name="level"/> (materials already paid). Finishes at once
    /// when construction is off, the sim builds instantly (scene setup), or the level needs neither work nor materials.
    /// </summary>
    internal static ConstructionSite? Place(ColonySim sim, BuildingDef def, int level, int buildingId, int plot)
    {
        var state = sim.State;
        var site = new ConstructionSite
        {
            BuildingId = buildingId, DefId = def.Id, Plot = plot, Level = level, StartedTick = state.Tick,
            WorkNeeded = def.Level(level).BuildWork,
            Materials = new(def.Level(level).Cost.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal),
        };
        if (!sim.Data.Rules.Construction.Enabled || sim.InstantConstruction || site.Done)
        {
            site.Work = site.WorkNeeded;
            site.Delivered = new(site.Materials, StringComparer.Ordinal);
            Complete(sim, site);
            return null;
        }
        state.Construction.Sites.Add(site);
        var what = level > 1 ? $"Started upgrading {def.Name} to level {level}." : $"Started building {def.Name}.";
        state.Log(new(state.Tick, ColonyEventType.SiteStarted, what, BuildingId: buildingId));
        return site;
    }

    /// <summary>Cancels a site: the materials not yet hauled come back in full, the delivered ones at <c>refundShare</c>.</summary>
    internal static Dictionary<string, double> Refund(ColonySim sim, ConstructionSite site)
    {
        var share = sim.Data.Rules.Construction.RefundShare;
        var back = new Dictionary<string, double>();
        foreach (var (res, amount) in site.Materials)
        {
            var delivered = site.Delivered.GetValueOrDefault(res);
            var r = Math.Floor((amount - delivered) + delivered * share + 1e-9);
            if (r > 0) back[res] = r;
        }
        foreach (var (res, amount) in back) sim.State.Stockpile.Add(res, amount);
        return back;
    }

    // ---- raids ----

    /// <summary>
    /// A raid hits a site. A new building's site stands at its share of level-1 HP (at least a tenth); a hit knocks the work
    /// back by the HP it takes. Returns the HP taken; the site is flattened when its work is gone (materials stay).
    /// </summary>
    public static double Hit(ConstructionSite site, double levelHp, double damage)
    {
        var hp = SiteHp(site, levelHp);
        var hit = Math.Min(hp, damage);
        if (levelHp > 0) site.Work = Math.Max(0, site.Work - hit / levelHp * site.WorkNeeded);
        return hit;
    }

    /// <summary>HP of a new building's site: its share of the finished building's, at least a tenth.</summary>
    public static double SiteHp(ConstructionSite site, double levelHp) => levelHp * Math.Max(0.1, site.Progress);

    // ---- estimates ----

    /// <summary>
    /// The crew's rates now (builders and helpers on shift averaged over the day, at clear weather) and the hours until the
    /// site is done at them; null ETA when nobody works it. Blocked says why nothing happens (paused, no builders).
    /// </summary>
    public static SiteForecast Forecast(ColonySim sim, ConstructionSite site)
    {
        var state = sim.State;
        var crew = Crew(state, sim.Data, site).ToList();
        double work = 0, haul = 0;
        foreach (var (s, share) in crew)
        {
            var day = ShiftHoursPerDay(sim, site, s) / sim.Data.Rules.TicksPerDay;
            work += WorkRate(sim, s) * share * day;
            haul += HaulRate(sim, s) * share * day;
        }
        var builders = site.Builders.Count;
        var helpers = site.Helpers.Count;
        string? blocked = site.Paused ? "Paused" : crew.Count == 0 ? "No builders" : null;
        double? eta = null;
        if (blocked is null && work > 1e-9 && haul > 1e-9)
            eta = Math.Max(0, site.MaterialsTotal - site.DeliveredTotal) / haul + Math.Max(0, site.WorkNeeded - site.Work) / work;
        return new SiteForecast(builders, helpers, work, haul, eta, blocked is not null, blocked);
    }

    /// <summary>Hours a builder works at this site in a day (a Night Shift counted tired).</summary>
    public static double ShiftHoursPerDay(ColonySim sim, ConstructionSite site, Survivor s)
    {
        var rules = sim.Data.Rules;
        if (!rules.DayNight.Enabled) return rules.TicksPerDay;
        var hours = 0.0;
        var nights = false;
        for (var h = 0; h < rules.TicksPerDay; h++)
        {
            if (Schedule.IsDayShiftHour(s, h, rules.DayNight)) hours += 1;
            else if (IsNightShiftHour(sim, site, s, h))
            {
                hours += rules.DayNight.NightShift.OutputMultiplier;
                nights = true;
            }
        }
        return nights ? hours * rules.DayNight.Tired.OutputMultiplier : hours;
    }

    /// <summary>
    /// Hours (on the clock) a level takes with <paramref name="builders"/> builders at the colony's typical rate (the mean of
    /// its adults at home, else a fresh adult), on the day shift: hauling plus work. For the build menu.
    /// </summary>
    public static double EstimateHours(ColonySim sim, BuildingDef def, int level, int builders = 2)
    {
        var lvl = def.Level(level);
        var rules = sim.Data.Rules;
        var adults = sim.State.Survivors.Where(s => !s.Away && !s.IsChild).ToList();
        var work = adults.Count == 0 ? rules.Construction.WorkPerHour * 0.45 : adults.Average(s => WorkRate(sim, s));
        var haul = adults.Count == 0 ? rules.Construction.HaulPerHour : adults.Average(s => HaulRate(sim, s));
        var n = Math.Max(1, Math.Min(builders, lvl.MaxBuilders > 0 ? lvl.MaxBuilders : rules.Construction.DefaultMaxBuilders));
        var shiftHours = rules.DayNight.Enabled ? (double)DayNight.WindowLength(rules.DayNight.WorkHours, rules.TicksPerDay) : rules.TicksPerDay;
        var crewHours = lvl.Cost.Values.Sum() / Math.Max(1e-9, haul * n) + lvl.BuildWork / Math.Max(1e-9, work * n);
        return crewHours * rules.TicksPerDay / Math.Max(1, shiftHours);
    }
}
