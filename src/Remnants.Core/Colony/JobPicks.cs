using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// What the job menus send and show (survivor panel, roster menu, a building's "+" slot and card drops). Picking a
/// workplace adds a job and keeps the others; moving (a separate, labelled action) replaces them all. Leaving one
/// workplace leaves only that job. Past <c>rules.maxActivities</c> Core refuses with <see cref="CommandError.TooManyActivities"/>.
/// </summary>
public static class JobPicks
{
    /// <summary>One job a survivor holds and the share of their effort it gets.</summary>
    public readonly record struct Held(int BuildingId, string Name, double Share);

    /// <summary>The default pick: work here too.</summary>
    public static IColonyCommand Add(int survivorId, int buildingId) => new AssignJob(survivorId, buildingId);

    /// <summary>"Move here (leave other jobs)": work only here.</summary>
    public static IColonyCommand Move(int survivorId, int buildingId) => new AssignJob(survivorId, buildingId, Move: true);

    /// <summary>Taking a worker off one workplace: their other jobs stay.</summary>
    public static IColonyCommand Leave(int survivorId, int buildingId) => new Unassign(survivorId, buildingId);

    /// <summary>Their jobs in order, each with its building's name and its 1/N share.</summary>
    public static IReadOnlyList<Held> Jobs(ColonyState state, GameData data, Survivor s)
    {
        var share = Activities.Share(state, s);
        return s.Jobs.Select(id => state.FindBuilding(id) is { } b
                ? new Held(id, data.Buildings[b.DefId].Name, share)
                : new Held(id, $"#{id}", share))
            .ToList();
    }

    /// <summary>"Woodcutter 50% · Farm 50%"; a lone activity has no percent ("Woodcutter"); empty with no job.</summary>
    public static string Summary(ColonyState state, GameData data, Survivor s)
    {
        var jobs = Jobs(state, data, s);
        var shared = Activities.Count(state, s) > 1;
        return string.Join(" · ", jobs.Select(j => shared ? $"{j.Name} {Percent(j.Share)}" : j.Name));
    }

    /// <summary>Whether one more activity would be refused (the menus say so up front and offer a move instead).</summary>
    public static bool AtCap(ColonyState state, GameData data, Survivor s) => Activities.Count(state, s) >= data.Rules.MaxActivities;

    /// <summary>What the menus say at the cap.</summary>
    public static string CapNote(GameData data, Survivor s) =>
        $"{s.Name} already has {data.Rules.MaxActivities} things to do: remove a job, or move them (leaves their other jobs).";

    public static string Percent(double share) => $"{Math.Round(share * 100):0}%";
}
