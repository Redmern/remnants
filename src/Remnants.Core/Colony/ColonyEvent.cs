namespace Remnants.Core.Colony;

public enum ColonyEventType
{
    Constructed, Upgraded, Starving, Died, Left, BecameAdult, Homeless, Housed,
}

/// <summary>Something notable the UI can show in a log. Purely informational.</summary>
public sealed record ColonyEvent(long Tick, ColonyEventType Type, string Message, int? SurvivorId = null, int? BuildingId = null);
