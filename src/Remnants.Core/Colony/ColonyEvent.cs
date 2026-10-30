namespace Remnants.Core.Colony;

public enum ColonyEventType
{
    Constructed, Upgraded, Starving, Died, Left, BecameAdult, Homeless, Housed,
    ExpeditionLaunched, ExpeditionReturned, Recruited, Admitted, Discharged, Healed, InjuryWorsened, LimbLost,
    ProstheticFitted, Crafted, RecipeChanged, OrderQueued, OrderCancelled, BlueprintStudied,
    NewCouple, BrokeUp, Pregnant, Born, BecameChild, Grieving, Miscarried,
    ExpeditionArrived, ExpeditionHeadingHome, ZoneRevealed, DroneScouted, DroneLost, DroneReturned,
    LevelUp, PerkLearned, StudyStarted, StudyFinished, StudyEnded,
    NightShift, Tired, FloodlightFitted,
}

/// <summary>Something notable the UI can show in a log. Purely informational.</summary>
public sealed record ColonyEvent(long Tick, ColonyEventType Type, string Message, int? SurvivorId = null, int? BuildingId = null);
