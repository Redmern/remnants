using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// What a survivor holds at once: each job, a class (as student), teaching (as mentor) and a construction site (as builder).
/// Up to <c>rules.maxActivities</c>; each activity gets 1/N of their effort (output, training, XP, study progress, building).
/// Guard duty, research, expeditions and Med Bay beds stay exclusive.
/// </summary>
public static class Activities
{
    /// <summary>How many activities the survivor holds now.</summary>
    public static int Count(ColonyState state, Survivor s) =>
        s.Jobs.Count
        + (state.Studies.Any(o => o.StudentId == s.Id) ? 1 : 0)
        + (state.Studies.Any(o => o.MentorId == s.Id) ? 1 : 0)
        + (state.Construction.Sites.Any(site => site.Builders.Contains(s.Id)) ? 1 : 0);

    /// <summary>The share of their effort each activity gets: 1/N (1 with one activity or none).</summary>
    public static double Share(ColonyState state, Survivor s) => 1.0 / Math.Max(1, Count(state, s));

    /// <summary>The share each would get after taking on one more activity.</summary>
    public static double ShareWithOneMore(ColonyState state, Survivor s) => 1.0 / (Count(state, s) + 1);

    /// <summary>Fails when the survivor already holds as many activities as the rules allow.</summary>
    public static CommandResult CanTakeOn(ColonyState state, GameData data, Survivor s)
    {
        var max = data.Rules.MaxActivities;
        return Count(state, s) >= max
            ? CommandResult.Fail(CommandError.TooManyActivities, $"{s.Name} already has {max} things to do; take one away first.")
            : CommandResult.Success("");
    }
}
