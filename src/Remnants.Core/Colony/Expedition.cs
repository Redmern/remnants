using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>A survivor picked for an expedition and the gear they take from the colony inventory.</summary>
public sealed record ExpeditionMember(int SurvivorId, Dictionary<EquipSlot, string>? Equipment = null, List<string>? Pack = null)
{
    public IEnumerable<string> AllItems => (Equipment?.Values ?? Enumerable.Empty<string>()).Concat(Pack ?? []);
}

/// <summary>Where an expedition is: on the road out, in the zone (the mission can be played), or on the way home.</summary>
public enum ExpeditionPhase { Outbound, InZone, Returning }

/// <summary>
/// A party out in a zone. The scavenge run is built from <see cref="Seed"/> and the members' state at launch;
/// while it is being played the presenter keeps the live <see cref="Scavenge"/> state here so a colony save
/// carries the mission in progress. Far zones take colony time: the party walks <see cref="TravelTicks"/> out
/// (the mission opens at <see cref="ArrivalTick"/>) and the same back; the finished run waits in
/// <see cref="PendingResult"/> until <see cref="ReturnTick"/>, when it is applied.
/// </summary>
public sealed class Expedition
{
    public int Id { get; set; }
    public string ZoneId { get; set; } = "";
    public ulong Seed { get; set; }
    public long LaunchedTick { get; set; }
    public List<ExpeditionMember> Members { get; set; } = new();

    /// <summary>Story mode: the field mission this party is on (null for a plain scavenge run).</summary>
    public string? MissionId { get; set; }

    /// <summary>
    /// One-way travel time in ticks (0 for a zone on the doorstep): the way out with its weather, and once the party heads
    /// home, the way back (<see cref="WorldMap.Travel"/>).
    /// </summary>
    public long TravelTicks { get; set; }

    /// <summary>When the party reaches the zone (and the mission starts: its time of day is this tick's).</summary>
    public long ArrivalTick { get; set; }

    /// <summary>Hours the party waits outside the zone for a better time (included in <see cref="ArrivalTick"/>).</summary>
    public long WaitTicks { get; set; }

    /// <summary>The zone's depletion at launch; the run is generated with it.</summary>
    public double Depletion { get; set; }

    /// <summary>The scavenge instance in progress, once started. Null until then.</summary>
    public ScavengeState? Scavenge { get; set; }

    /// <summary>The finished run, carried home; applied at <see cref="ReturnTick"/>.</summary>
    public ExtractionResult? PendingResult { get; set; }
    public long ReturnTick { get; set; }

    public ExpeditionPhase PhaseAt(long tick) =>
        PendingResult is not null ? ExpeditionPhase.Returning : tick < ArrivalTick ? ExpeditionPhase.Outbound : ExpeditionPhase.InZone;
}

/// <summary>What the colony knows about a zone and how picked-over it is. Absent = untouched, intel as the zone starts.</summary>
public sealed class ZoneStatus
{
    public ZoneIntel Intel { get; set; }

    /// <summary>0 = fresh, 1 = stripped bare. Recovers <see cref="ZoneDef.ReplenishPerDay"/> per day.</summary>
    public double Depletion { get; set; }
    public int Visits { get; set; }
    public long? LastVisitTick { get; set; }
}

/// <summary>
/// A scout drone flying over a zone: it reaches the zone at <see cref="ArriveTick"/> (intel comes in, and it may be shot
/// down), and is back in the inventory, recharged, at <see cref="ReturnTick"/>.
/// </summary>
public sealed class DroneFlight
{
    public int Id { get; set; }
    public string ZoneId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int? PilotId { get; set; }
    public long LaunchTick { get; set; }
    public long ArriveTick { get; set; }
    public long ReturnTick { get; set; }
    public bool Arrived { get; set; }
}

/// <summary>When a party would reach its zone and in which day phase; how long it waits for it and the food that costs.</summary>
/// <paramref name="Travel"/> is the trip out with its terms (weather); <paramref name="DelayRationsPerMember"/> the food for the
/// days they add.
public sealed record ArrivalPlan(long ArrivalTick, DayPhase Phase, long WaitTicks, double WaitRationsPerMember,
    TravelPlan? Travel = null, double DelayRationsPerMember = 0)
{
    /// <summary>Food per member on top of the zone's rations: the wait and the delays on the road.</summary>
    public double ExtraRationsPerMember => WaitRationsPerMember + DelayRationsPerMember;

    public bool Blocked => Travel?.Blocked == true;
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
    SortedDictionary<string, int> LostGear)
{
    /// <summary>Schematics that came home and what became of each (learned, to decrypt, Research Data).</summary>
    public SortedDictionary<string, Schematics.ReceiveOutcome> Schematics { get; init; } = new(StringComparer.Ordinal);
}

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
