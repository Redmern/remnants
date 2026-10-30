using Remnants.Core.Colony;

namespace Remnants.Core.Raids;

/// <summary>
/// The colony's defenses and raids (<see cref="ColonyState.Defense"/>). Plain data; rules live in <see cref="RaidSystem"/>
/// and <see cref="DefenseHandlers"/>. Absent in older saves, which load with an empty one.
/// </summary>
public sealed class DefenseState
{
    public List<PlacedDefense> Defenses { get; set; } = new();
    public int NextDefenseId { get; set; } = 1;

    /// <summary>Survivors on guard duty: survivor id → the post (a placed defense with guard slots).</summary>
    public SortedDictionary<int, int> Guards { get; set; } = new();

    /// <summary>Pressure built up per threat (a raid is drawn at the threshold).</summary>
    public SortedDictionary<ThreatKind, double> Pressure { get; set; } = new();

    /// <summary>The raid on its way, if any (one at a time).</summary>
    public Raid? Raid { get; set; }
    public int NextRaidId { get; set; } = 1;

    /// <summary>Raids that have hit the colony, for escalation.</summary>
    public int RaidsSurvived { get; set; }
    public long? LastRaidTick { get; set; }

    /// <summary>Ticks at which an expedition came home (noise for a while).</summary>
    public List<long> Returns { get; set; } = new();

    /// <summary>The expedition out last tick (its going marks a return).</summary>
    public int? WatchedExpedition { get; set; }

    /// <summary>Building id → HP lost to raids (absent = undamaged).</summary>
    public SortedDictionary<int, double> BuildingDamage { get; set; } = new();

    /// <summary>Repairs in order; the first is worked.</summary>
    public List<RepairOrder> Repairs { get; set; } = new();
    public int NextRepairId { get; set; } = 1;

    /// <summary>
    /// Defense schematics the colony knows beyond the defaults (the adapter's store until story-schematics lands, see
    /// <see cref="DefenseSchematics"/>).
    /// </summary>
    public SortedSet<string> KnownSchematics { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The last raid's report, for the after-raid screen; older ones are in <see cref="History"/>.</summary>
    public RaidReport? LastReport { get; set; }
    public List<RaidReport> History { get; set; } = new();

    /// <summary>Upkeep was not paid today: these defenses stand but do nothing until it is.</summary>
    public SortedSet<int> Unpaid { get; set; } = new();

    public PlacedDefense? Find(int id) => Defenses.Find(d => d.Id == id);
}

/// <summary>A defense on the grid. <see cref="Facing"/> turns walls and gates (along x or y) and aims turrets.</summary>
public sealed class PlacedDefense
{
    public int Id { get; set; }
    public string DefId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public Side Facing { get; set; } = Side.South;
    public double Hp { get; set; }

    /// <summary>Traps: sprung and waiting to be re-armed (re-armable ones only; used-up traps are removed).</summary>
    public bool Sprung { get; set; }

    /// <summary>Gates: shut (they soak like a wall, but the gate is closed to traffic).</summary>
    public bool Closed { get; set; }

    /// <summary>Posts and towers: the guards here stand the night watch instead of the day.</summary>
    public bool NightWatch { get; set; }
}

/// <summary>Approaching (unseen), Warned (prep), Battle (at the fence, to be fought on the tactical map).</summary>
public enum RaidStage { Approaching, Warned, Battle }

/// <summary>A raid on its way. Hidden until <see cref="WarnTick"/>; it arrives (and resolves) at <see cref="ArrivalTick"/>.</summary>
public sealed class Raid
{
    public int Id { get; set; }
    public ThreatKind Kind { get; set; }
    public int Size { get; set; }
    public Side Approach { get; set; }
    public long SetOutTick { get; set; }
    public long ArrivalTick { get; set; }
    public long WarnTick { get; set; }
    public RaidStage Stage { get; set; }

    /// <summary>The size range the watch reports (fixed when warned).</summary>
    public int EstimateMin { get; set; }
    public int EstimateMax { get; set; }

    /// <summary>Prep: who defends (empty = everyone able), children in shelter, gates shut.</summary>
    public SortedSet<int> Defenders { get; set; } = new();
    public bool ChildrenSheltered { get; set; }
    public bool GatesClosed { get; set; }

    /// <summary>The player chose to fight it out; otherwise it auto-resolves on arrival.</summary>
    public bool Fight { get; set; }

    /// <summary>The fight in progress (<see cref="RaidBattle.Open"/>), carried in the colony save; its seed; attackers in it.</summary>
    public Scavenge.ScavengeState? Battle { get; set; }
    public ulong BattleSeed { get; set; }
    public int BattleAttackers { get; set; }
}

public enum RepairTarget { Building, Defense }

public sealed class RepairOrder
{
    public int Id { get; set; }
    public RepairTarget Target { get; set; }
    public int TargetId { get; set; }

    /// <summary>HP repaired so far; done when the target's damage is gone.</summary>
    public double Progress { get; set; }
    public bool Paid { get; set; }
    public Dictionary<string, double> Cost { get; set; } = new();
}

/// <summary>What happened in a raid, for the after-raid report and the log.</summary>
public sealed record RaidReport
{
    public int RaidId { get; init; }
    public long Tick { get; init; }
    public ThreatKind Kind { get; init; }
    public int Size { get; init; }
    public Side Approach { get; init; }
    public bool Repelled { get; init; }

    /// <summary>Fought on the tactical map (else auto-resolved).</summary>
    public bool Fought { get; init; }

    /// <summary>Attack and defense strength after soak, traps and modifiers; Pressure = attack / (attack + defense).</summary>
    public double Attack { get; init; }
    public double Defense { get; init; }
    public double Pressure { get; init; }
    public int AttackersKilled { get; init; }
    public List<string> Defenders { get; init; } = new();
    public List<string> TrapsSprung { get; init; } = new();
    public List<RaidDamage> Damaged { get; init; } = new();
    public Dictionary<string, double> Stolen { get; init; } = new();
    public List<RaidInjury> Injured { get; init; } = new();
    public List<string> Dead { get; init; } = new();
    public List<string> Taken { get; init; } = new();
    public Dictionary<string, int> Loot { get; init; } = new();
    public string? Schematic { get; init; }
    public double MoodChange { get; init; }

    /// <summary>The strength breakdown (line, value), for the report's tooltip.</summary>
    public List<StrengthLine> Breakdown { get; init; } = new();
}

public sealed record RaidDamage(string Name, double Damage, bool Destroyed);

public sealed record RaidInjury(string Name, string Injury);

/// <summary>One line of a strength breakdown: e.g. ("Wren (guard, pistol)", 9.4).</summary>
public sealed record StrengthLine(string Line, double Value);
