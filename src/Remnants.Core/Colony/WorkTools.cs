using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// Tools in the stockpile make gatherers faster: a job's tools (jobs.json <c>tools</c>: item id → quality) go to the
/// workers at the job's buildings, one each, the best tool to the lowest survivor id. A tool out on an expedition is not
/// in the stockpile, so it is not used at home meanwhile. Variants count as their base item (a crafted
/// <c>crowbar@crude</c> is a crowbar). The quality feeds <see cref="JobOutput.ToolFactor"/>.
/// </summary>
public static class WorkTools
{
    /// <summary>Quality of the tool <paramref name="worker"/> uses at <paramref name="job"/> (0 = bare hands).</summary>
    public static double Quality(ColonyState state, GameData data, JobDef job, Survivor worker) =>
        job.Tools.Count == 0 ? 0 : Handout(state, data, job).GetValueOrDefault(worker.Id);

    /// <summary>
    /// Every tool-using job's handout at once (job id → survivor id → quality, bare hands left out), for a system that
    /// asks for many workers in one tick.
    /// </summary>
    public static Dictionary<string, Dictionary<int, double>> Handout(ColonyState state, GameData data) =>
        data.Jobs.Values.Where(j => j.Tools.Count > 0).ToDictionary(j => j.Id, j => Handout(state, data, j));

    private static Dictionary<int, double> Handout(ColonyState state, GameData data, JobDef job)
    {
        var crew = state.Buildings
            .Where(b => data.Buildings.TryGetValue(b.DefId, out var def) && def.Job == job.Id)
            .SelectMany(b => state.WorkersAt(b.Id))
            .Select(s => s.Id)
            .Distinct()
            .Order()
            .ToList();
        var tools = job.Tools.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .SelectMany(kv => Enumerable.Repeat(kv.Value, state.Inventory.Counts.Where(c => BaseId(c.Key) == kv.Key).Sum(c => c.Value)));
        return crew.Zip(tools).ToDictionary(p => p.First, p => p.Second);
    }

    /// <summary>The item an inventory id is a variant of (<c>base[@quality][+mkN][+mod]</c>).</summary>
    private static string BaseId(string id)
    {
        var end = id.IndexOfAny(['@', '+']);
        return end < 0 ? id : id[..end];
    }
}
