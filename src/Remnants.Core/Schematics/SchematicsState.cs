namespace Remnants.Core.Schematics;

/// <summary>What a colony knows of schematics (<see cref="Colony.ColonyState.Schematics"/>). Plain data for the save.</summary>
public sealed class SchematicsState
{
    /// <summary>Schematics the colony has learned (for good).</summary>
    public SortedSet<string> Known { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Schematics the colony has heard of but does not know: id → where it was seen ("in the trader's stock", "on a
    /// terminal in Rail Depot"). Researchable.
    /// </summary>
    public SortedDictionary<string, string> HeardOf { get; set; } = new(StringComparer.Ordinal);

    /// <summary>How each known schematic was learned (id → source), for the archive and the playtest bot.</summary>
    public SortedDictionary<string, SchematicSource> LearnedFrom { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The tick each known schematic was learned (for "first schematic by day N" measures).</summary>
    public SortedDictionary<string, long> LearnedAt { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Research running at the Town Hall, if any (one at a time).</summary>
    public ResearchOrder? Research { get; set; }

    /// <summary>Copies found of schematics already known (or carried twice), turned into Research Data.</summary>
    public int Duplicates { get; set; }

    /// <summary>Schematics brought home from the field (items found, before decrypting and duplicates).</summary>
    public int Found { get; set; }

    /// <summary>The save was migrated from blueprints (version 10 or older).</summary>
    public bool Migrated { get; set; }
}

/// <summary>A researcher working out a schematic at the Town Hall. Paid at the start; cancelling refunds it.</summary>
public sealed class ResearchOrder
{
    public string SchematicId { get; set; } = "";
    public int ResearcherId { get; set; }
    public int BuildingId { get; set; }
    public double Progress { get; set; }
    public double Work { get; set; }
    public double Paid { get; set; }
}

/// <summary>What a call over the radio is about.</summary>
public enum RadioCallKind
{
    /// <summary>The trader is parked outside: open the trader screen.</summary>
    Trader,

    /// <summary>A contract offer: accept it from the call.</summary>
    Contract,

    /// <summary>A character calling (Story mode puts these in; Core only queues them).</summary>
    Story,

    /// <summary>Anything else worth hearing.</summary>
    News,
}

/// <summary>
/// A call in the Radio Mast's queue (<see cref="RadioState.Calls"/>). The story campaign queues its character calls with
/// <see cref="RadioMast.Enqueue"/> and reads <see cref="Tag"/> back when the player answers.
/// </summary>
public sealed class RadioCall
{
    public int Id { get; set; }
    public RadioCallKind Kind { get; set; }

    /// <summary>Who calls (a trader id, a story character id).</summary>
    public string CallerId { get; set; } = "";
    public string CallerName { get; set; } = "";
    public string Text { get; set; } = "";
    public long Tick { get; set; }

    /// <summary>Free data for whoever queued it (a dialogue id, a contract id).</summary>
    public string? Tag { get; set; }

    /// <summary>Unanswered calls drop out after this tick (null = they wait).</summary>
    public long? ExpiresTick { get; set; }
}

/// <summary>A radio contract: bring a party home from <see cref="ZoneId"/> before <see cref="DueTick"/>.</summary>
public sealed class RadioContract
{
    public int Id { get; set; }
    public string ZoneId { get; set; } = "";
    public long DueTick { get; set; }
    public Dictionary<string, double> Reward { get; set; } = new();
    public bool Accepted { get; set; }
}

/// <summary>The Radio Mast's state: the trader's visits, its stock, contracts and the call queue.</summary>
public sealed class RadioState
{
    /// <summary>The trader who visits (null = <c>rules.radio.defaultTrader</c>). Story mode swaps in its own.</summary>
    public string? TraderId { get; set; }

    /// <summary>A story has opened trading regardless of the trader's <c>requiresScouted</c>.</summary>
    public bool TraderUnlocked { get; set; }

    /// <summary>Tick the trader next arrives (0 = not scheduled yet).</summary>
    public long NextVisitTick { get; set; }

    /// <summary>While the trader is here: when it leaves (null = away).</summary>
    public long? TraderLeavesTick { get; set; }

    /// <summary>Schematics on offer while the trader is here.</summary>
    public List<string> Stock { get; set; } = new();

    public int Visits { get; set; }

    public long NextContractTick { get; set; }
    public RadioContract? Contract { get; set; }
    public int NextContractId { get; set; } = 1;
    public int ContractsDone { get; set; }

    public List<RadioCall> Calls { get; set; } = new();
    public int NextCallId { get; set; } = 1;

    public bool TraderHere => TraderLeavesTick is not null;
}
