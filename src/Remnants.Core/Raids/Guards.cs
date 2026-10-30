using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Raids;

/// <summary>A guard at a post, and whether they stand the night watch there.</summary>
public sealed record GuardDuty(Survivor Survivor, PlacedDefense Post, bool NightWatch);

/// <summary>
/// The Guard job. A guard has no building job; they stand at a post (a placed defense with guard slots) on the day shift,
/// or on the night watch (the hours outside it) when the post has it. On duty they train like workers at <c>guard.job</c> and earn work XP. The
/// night watch is night work for the day/night rules (<see cref="Schedule.OnShift"/> asks <see cref="OnShift"/>), so it builds
/// sleep debt and costs mood; the watch sleeps it off in the day (<c>guard.daySleepHours</c>).
/// </summary>
public static class Guards
{
    /// <summary>The post a survivor guards, if any (and if it still stands).</summary>
    public static PlacedDefense? PostOf(ColonyState state, int survivorId) =>
        state.Defense.Guards.TryGetValue(survivorId, out var post) ? state.Defense.Find(post) : null;

    /// <summary>Every guard able to stand watch (home, not a patient), with their post.</summary>
    public static IEnumerable<GuardDuty> All(ColonyState state)
    {
        foreach (var (id, postId) in state.Defense.Guards)
            if (state.FindSurvivor(id) is { Away: false, PatientAtBuildingId: null } s && state.Defense.Find(postId) is { } post)
                yield return new GuardDuty(s, post, post.NightWatch);
    }

    /// <summary>Guards on duty at this hour.</summary>
    public static IEnumerable<GuardDuty> OnDuty(ColonySim sim) =>
        All(sim.State).Where(g => OnDutyAt(g, DayNight.Hour(sim.State.Tick, sim.Data.Rules), sim.Data.Rules.DayNight));

    /// <summary>Day guards stand the day shift (<c>dayNight.workHours</c>), the night watch the hours outside it; always with day/night off.</summary>
    public static bool OnDutyAt(GuardDuty g, int hour, Data.DayNightRules rules) =>
        !rules.Enabled || DayNight.InWindow(hour, rules.WorkHours) != g.NightWatch;

    /// <summary>For <see cref="Schedule.OnShift"/>: a guard on duty right now counts as working.</summary>
    public static bool OnShift(ColonyState state, Data.GameData data, Survivor s) =>
        PostOf(state, s.Id) is { } post && !s.Away && s.PatientAtBuildingId is null
        && OnDutyAt(new GuardDuty(s, post, post.NightWatch), DayNight.Hour(state.Tick, data.Rules), data.Rules.DayNight);

    /// <summary>One tick: guards on duty train and earn XP; the night watch sleeps in the day and pays back its debt.</summary>
    internal static void Run(ColonySim sim, RaidData raids)
    {
        var state = sim.State;
        var rules = sim.Data.Rules;
        var hour = DayNight.Hour(state.Tick, rules);
        var job = sim.Data.Jobs.GetValueOrDefault(raids.Def.Guard.Job);
        foreach (var g in All(state).ToList())
        {
            if (OnDutyAt(g, hour, rules.DayNight))
            {
                if (job is not null) Training.Work(state, sim.Data, job, g.Survivor);
            }
            else if (g.NightWatch && rules.DayNight.Enabled && DayNight.InWindow(hour, raids.Def.Guard.DaySleepHours))
            {
                var tired = rules.DayNight.Tired;
                g.Survivor.SleepDebt = Math.Max(0, g.Survivor.SleepDebt - tired.RecoveryPerSleepTick);
            }
        }
        // Posts that are gone take their guards off duty.
        foreach (var (id, post) in state.Defense.Guards.ToList())
            if (state.Defense.Find(post) is null || state.FindSurvivor(id) is null) state.Defense.Guards.Remove(id);
    }

    /// <summary>Taking a job, going away or being admitted elsewhere ends guard duty.</summary>
    public static void Release(ColonyState state, int survivorId) => state.Defense.Guards.Remove(survivorId);
}
