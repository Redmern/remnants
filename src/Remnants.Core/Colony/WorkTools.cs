using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// Tools in the stockpile make gatherers faster: a job's tools (jobs.json <c>tools</c>: item id → quality) go to the
/// workers at the job's buildings, one each, the best tool to the lowest survivor id. A tool out on an expedition is not
/// in the stockpile, so it is not used at home meanwhile. The quality feeds <see cref="JobOutput.ToolFactor"/>.
/// </summary>
public static class WorkTools
{
    /// <summary>Quality of the tool <paramref name="worker"/> uses at <paramref name="job"/> (0 = bare hands).</summary>
    public static double Quality(ColonyState state, GameData data, JobDef job, Survivor worker)
    {
        if (job.Tools.Count == 0) return 0;
        var crew = state.Buildings
            .Where(b => data.Buildings.TryGetValue(b.DefId, out var def) && def.Job == job.Id)
            .SelectMany(b => state.WorkersAt(b.Id))
            .Select(s => s.Id)
            .Distinct()
            .Order()
            .ToList();
        var index = crew.IndexOf(worker.Id);
        if (index < 0) return 0;
        foreach (var (item, quality) in job.Tools.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var count = state.Inventory.Count(item);
            if (index < count) return quality;
            index -= count;
        }
        return 0;
    }
}
