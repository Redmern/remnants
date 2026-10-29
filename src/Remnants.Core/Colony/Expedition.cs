using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>A survivor picked for an expedition and the gear they take from the colony inventory.</summary>
public sealed record ExpeditionMember(int SurvivorId, Dictionary<EquipSlot, string>? Equipment = null, List<string>? Pack = null)
{
    public IEnumerable<string> AllItems => (Equipment?.Values ?? Enumerable.Empty<string>()).Concat(Pack ?? []);
}

/// <summary>
/// A party out in a zone. The scavenge run is built from <see cref="Seed"/> and the members' state at launch;
/// while it is being played the presenter keeps the live <see cref="Scavenge"/> state here so a colony save
/// carries the mission in progress.
/// </summary>
public sealed class Expedition
{
    public int Id { get; set; }
    public string ZoneId { get; set; } = "";
    public ulong Seed { get; set; }
    public long LaunchedTick { get; set; }
    public List<ExpeditionMember> Members { get; set; } = new();

    /// <summary>The scavenge instance in progress, once started. Null until then.</summary>
    public ScavengeState? Scavenge { get; set; }
}

/// <summary>A party member as they came home.</summary>
public sealed record ReportSurvivor(int SurvivorId, string Name, bool Evacuated, double Health, List<string> NewInjuries);

/// <summary>What happened on an expedition, for the after-action screen.</summary>
public sealed record ExpeditionReport(
    int ExpeditionId,
    string ZoneId,
    long Tick,
    List<ReportSurvivor> Returned,
    List<string> Dead,
    List<string> Recruits,
    SortedDictionary<string, int> Loot,
    SortedDictionary<string, int> LostGear);

public sealed record MemorialEntry(int SurvivorId, string Name, long Tick, string Cause);

/// <summary>Body changes between two snapshots of the same survivor, as short lines ("LeftLeg lost").</summary>
public static class BodyDiff
{
    public static List<string> Describe(Body before, Body after)
    {
        var lines = new List<string>();
        foreach (var part in Enum.GetValues<BodyPart>())
        {
            var a = before[part];
            var b = after[part];
            if (b.Condition == PartCondition.Missing && a.Condition != PartCondition.Missing) lines.Add($"{part} lost");
            else if (b.Condition == PartCondition.Injured && (a.Condition != PartCondition.Injured || b.Severity > a.Severity + 1e-9))
                lines.Add($"{part} injured ({Math.Round(b.Severity * 100):0}%)");
        }
        return lines;
    }
}
