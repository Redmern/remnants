using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>The four parts of a colony day, in order (see <see cref="DayNightRules"/>).</summary>
public enum DayPhase { Dawn, Day, Dusk, Night }

/// <summary>
/// The colony clock: hour of day and day phase from the sim tick. Pure and deterministic; presentation (light, audio)
/// should switch on <see cref="PhaseAt"/> rather than on raw hours, and may use <see cref="HourOf"/> with a fractional
/// tick for smooth light.
/// </summary>
public static class DayNight
{
    /// <summary>Hour of day (0..ticksPerDay−1) at a tick.</summary>
    public static int Hour(long tick, RulesDef rules) => (int)(tick % rules.TicksPerDay);

    /// <summary>Day number, starting at 1.</summary>
    public static long Day(long tick, RulesDef rules) => tick / rules.TicksPerDay + 1;

    /// <summary>Fractional hour of day for a fractional tick (for smooth presentation between ticks).</summary>
    public static double HourOf(double tick, RulesDef rules)
    {
        var h = tick % rules.TicksPerDay;
        return h < 0 ? h + rules.TicksPerDay : h;
    }

    public static DayPhase PhaseAt(long tick, RulesDef rules) => PhaseAtHour(Hour(tick, rules), rules.DayNight);

    public static DayPhase PhaseAtHour(double hour, DayNightRules rules) =>
        hour >= rules.NightHour || hour < rules.DawnHour ? DayPhase.Night
        : hour < rules.DayHour ? DayPhase.Dawn
        : hour < rules.DuskHour ? DayPhase.Day
        : DayPhase.Dusk;

    /// <summary>The hour a phase starts.</summary>
    public static int StartHour(DayPhase phase, DayNightRules rules) => phase switch
    {
        DayPhase.Dawn => rules.DawnHour,
        DayPhase.Day => rules.DayHour,
        DayPhase.Dusk => rules.DuskHour,
        _ => rules.NightHour,
    };

    /// <summary>Whether <paramref name="hour"/> is in [start, end), wrapping past midnight when end ≤ start.</summary>
    public static bool InWindow(int hour, int[] window) =>
        window[0] <= window[1] ? hour >= window[0] && hour < window[1] : hour >= window[0] || hour < window[1];

    public static int WindowLength(int[] window, int ticksPerDay) =>
        window[0] <= window[1] ? window[1] - window[0] : ticksPerDay - window[0] + window[1];
}

/// <summary>
/// Who works and who sleeps at a given hour (see <see cref="DayNightRules"/>). With day/night off everyone assigned
/// works every tick at factor 1 and nobody sleeps.
/// </summary>
public static class Schedule
{
    public static int[] SleepWindow(Survivor s, DayNightRules rules) => s.IsChild ? rules.ChildSleep : rules.AdultSleep;

    /// <summary>Whether this hour is in the survivor's own day shift (children stop at their bedtime).</summary>
    public static bool IsDayShiftHour(Survivor s, int hour, DayNightRules rules) =>
        DayNight.InWindow(hour, rules.WorkHours) && !DayNight.InWindow(hour, SleepWindow(s, rules));

    /// <summary>A Night Shift may run here: the building has a job and is indoors or lit.</summary>
    public static bool IsLit(Building building, BuildingDef def, DayNightRules rules) =>
        !def.Outdoor || building.Level >= rules.LitMinLevel || building.Floodlight;

    public static bool CanRunNightShift(Building building, BuildingDef def, DayNightRules rules) =>
        def.Job is not null && IsLit(building, def, rules);

    public static bool IsTired(Survivor s, DayNightRules rules) => rules.Enabled && s.SleepDebt + 1e-9 >= rules.Tired.Threshold;

    /// <summary>
    /// Share of a full tick's output this worker gives at <paramref name="hour"/>: 1 on the day shift, the Night Shift
    /// multiplier at a night-shift building, 0 off shift; times the tired multiplier. 1 when day/night is off.
    /// </summary>
    public static double Factor(Survivor s, Building building, BuildingDef def, int hour, DayNightRules rules)
    {
        if (!rules.Enabled) return 1;
        var tired = IsTired(s, rules) ? rules.Tired.OutputMultiplier : 1;
        if (IsDayShiftHour(s, hour, rules)) return tired;
        if (IsNightShiftHour(s, building, def, hour, rules)) return rules.NightShift.OutputMultiplier * tired;
        return 0;
    }

    /// <summary>The worker is on the Night Shift at this hour (an adult, outside the day shift, at a lit night-shift building).</summary>
    public static bool IsNightShiftHour(Survivor s, Building building, BuildingDef def, int hour, DayNightRules rules) =>
        rules.Enabled && building.NightShift && !s.IsChild && !DayNight.InWindow(hour, rules.WorkHours)
        && CanRunNightShift(building, def, rules);

    /// <summary>Whether a survivor is working right now (assigned, home, not a patient, and on shift).</summary>
    public static bool OnShift(ColonyState state, GameData data, Survivor s)
    {
        if (s.AssignedBuildingId is not { } id || s.Away || s.PatientAtBuildingId is not null) return false;
        if (state.FindBuilding(id) is not { } b) return false;
        return Factor(s, b, data.Buildings[b.DefId], DayNight.Hour(state.Tick, data.Rules), data.Rules.DayNight) > 0;
    }

    /// <summary>Asleep right now: day/night on, at home, in their sleep window and not working it.</summary>
    public static bool IsAsleep(ColonyState state, GameData data, Survivor s) =>
        data.Rules.DayNight.Enabled && !s.Away
        && DayNight.InWindow(DayNight.Hour(state.Tick, data.Rules), SleepWindow(s, data.Rules.DayNight))
        && !OnShift(state, data, s);

    /// <summary>Full-output hours this worker gives per day at this building (for per-day estimates), ignoring tiredness.</summary>
    public static double ShiftHoursPerDay(Survivor s, Building building, BuildingDef def, RulesDef rules)
    {
        if (!rules.DayNight.Enabled) return rules.TicksPerDay;
        var hours = 0.0;
        for (var h = 0; h < rules.TicksPerDay; h++)
            hours += IsDayShiftHour(s, h, rules.DayNight) ? 1
                : IsNightShiftHour(s, building, def, h, rules.DayNight) ? rules.DayNight.NightShift.OutputMultiplier : 0;
        return hours;
    }

    /// <summary>The workers of a building who work this tick, with their shift factor.</summary>
    public static IEnumerable<(Survivor Worker, double Factor)> Crew(ColonyState state, GameData data, Building building)
    {
        var def = data.Buildings[building.DefId];
        var hour = DayNight.Hour(state.Tick, data.Rules);
        foreach (var w in state.WorkersAt(building.Id).OrderBy(w => w.Id))
        {
            if (w.Away) continue;
            var f = Factor(w, building, def, hour, data.Rules.DayNight);
            if (f > 0) yield return (w, f);
        }
    }
}

/// <summary>
/// Sleep, the Night Shift's toll and evenings. Each tick, for everyone at home: asleep in their window pays back sleep
/// debt and (in a bed or a Med Bay cot) restores mood and health; working the window adds debt; night work costs mood;
/// being tired costs mood; an evening off work at the campfire lifts it. Logs the start of a night shift and who woke
/// up tired, only when there is one.
/// </summary>
internal static class DayNightSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        var rules = data.Rules.DayNight;
        if (!rules.Enabled) return;
        var hour = DayNight.Hour(state.Tick, data.Rules);
        foreach (var s in state.Survivors.Where(s => !s.Away).OrderBy(s => s.Id))
        {
            var working = Schedule.OnShift(state, data, s);
            var nightWork = working && !Schedule.IsDayShiftHour(s, hour, rules);
            var inSleep = DayNight.InWindow(hour, Schedule.SleepWindow(s, rules));
            if (inSleep && working)
                s.SleepDebt = Math.Min(rules.Tired.MaxDebt, s.SleepDebt + rules.Tired.DebtPerMissedTick);
            else if (inSleep)
            {
                s.SleepDebt = Math.Max(0, s.SleepDebt - rules.Tired.RecoveryPerSleepTick);
                if (s.HasBed || s.PatientAtBuildingId is not null)
                {
                    s.Mood += rules.SleepMoodPerTick;
                    s.Health = Math.Min(Survivor.MaxHealth, s.Health + rules.SleepHealthPerTick);
                }
            }
            if (nightWork) s.Mood -= rules.NightShift.MoodPerTick;
            if (Schedule.IsTired(s, rules)) s.Mood -= rules.Tired.MoodPerTick;
            if (!inSleep && !working && s.PatientAtBuildingId is null && hour >= rules.DuskHour)
                s.Mood += rules.CampfireMoodPerTick;
            s.Mood = Math.Clamp(s.Mood, 0, Survivor.MaxMood);
        }

        if (hour == rules.WorkHours[1]) LogNightShifts(state, data);
        if (hour == rules.WorkHours[0]) LogTired(state, data);
    }

    private static void LogNightShifts(ColonyState state, GameData data)
    {
        var rules = data.Rules.DayNight;
        foreach (var b in state.Buildings.Where(b => b.NightShift).OrderBy(b => b.Id))
        {
            var def = data.Buildings[b.DefId];
            var crew = state.WorkersAt(b.Id).Count(w => !w.Away && !w.IsChild && w.PatientAtBuildingId is null);
            if (crew == 0) continue;
            var cut = (int)Math.Round((1 - rules.NightShift.OutputMultiplier) * 100);
            state.Log(new(state.Tick, ColonyEventType.NightShift,
                Schedule.CanRunNightShift(b, def, rules)
                    ? $"Night shift at {def.Name}: −{cut}% output."
                    : $"Too dark for a night shift at {def.Name}: it needs a Floodlight or level {rules.LitMinLevel}.",
                BuildingId: b.Id));
        }
    }

    private static void LogTired(ColonyState state, GameData data)
    {
        var tired = state.Survivors.Where(s => !s.Away && Schedule.IsTired(s, data.Rules.DayNight)).OrderBy(s => s.Id).ToList();
        if (tired.Count == 0) return;
        var names = tired.Count == 1 ? tired[0].Name : string.Join(", ", tired.Take(tired.Count - 1).Select(s => s.Name)) + " and " + tired[^1].Name;
        state.Log(new(state.Tick, ColonyEventType.Tired,
            $"{names} {(tired.Count == 1 ? "is" : "are")} tired after the night: −{(int)Math.Round((1 - data.Rules.DayNight.Tired.OutputMultiplier) * 100)}% output until they sleep.",
            tired.Count == 1 ? tired[0].Id : null));
    }
}

internal static class DayNightHandlers
{
    public static CommandResult SetNightShift(ColonySim sim, SetNightShift c)
    {
        if (sim.State.FindBuilding(c.BuildingId) is not { } building)
            return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = sim.Data.Buildings[building.DefId];
        if (def.Job is null) return CommandResult.Fail(CommandError.NoJobAtBuilding, $"{def.Name} has no job.");
        var rules = sim.Data.Rules.DayNight;
        if (c.On && !Schedule.IsLit(building, def, rules))
            return CommandResult.Fail(CommandError.NeedsLight,
                $"{def.Name} works outdoors: it needs a Floodlight or level {rules.LitMinLevel} for a night shift.");
        building.NightShift = c.On;
        return CommandResult.Success(c.On ? $"Night shift at {def.Name}." : $"{def.Name} works days only.", building.Id);
    }

    public static CommandResult InstallFloodlight(ColonySim sim, InstallFloodlight c)
    {
        if (sim.State.FindBuilding(c.BuildingId) is not { } building)
            return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = sim.Data.Buildings[building.DefId];
        var rules = sim.Data.Rules.DayNight;
        if (!def.Outdoor) return CommandResult.Fail(CommandError.NotOutdoor, $"{def.Name} works indoors; it needs no Floodlight.");
        if (Schedule.IsLit(building, def, rules)) return CommandResult.Fail(CommandError.AlreadyLit, $"{def.Name} is already lit.");
        if (!sim.State.Stockpile.CanAfford(rules.FloodlightCost))
            return CommandResult.Fail(CommandError.CannotAfford,
                $"Cannot afford a Floodlight: {CommandHandlers.Describe(sim.State.Stockpile.Shortfall(rules.FloodlightCost))}.");
        sim.State.Stockpile.Spend(rules.FloodlightCost);
        building.Floodlight = true;
        sim.State.Log(new(sim.State.Tick, ColonyEventType.FloodlightFitted, $"Fitted a Floodlight at {def.Name}.", BuildingId: building.Id));
        return CommandResult.Success($"Fitted a Floodlight at {def.Name}.", building.Id);
    }
}
