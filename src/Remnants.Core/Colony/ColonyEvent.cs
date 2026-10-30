namespace Remnants.Core.Colony;

public enum ColonyEventType
{
    Constructed, Upgraded, Starving, Died, Left, BecameAdult, Homeless, Housed,
    ExpeditionLaunched, ExpeditionReturned, Recruited, Admitted, Discharged, Healed, InjuryWorsened, LimbLost,
    ProstheticFitted, Crafted, RecipeChanged, OrderQueued, OrderCancelled,
    NewCouple, BrokeUp, Pregnant, Born, BecameChild, Grieving, Miscarried,
    ExpeditionArrived, ExpeditionHeadingHome, ZoneRevealed, DroneScouted, DroneLost, DroneReturned,
    LevelUp, PerkLearned, StudyStarted, StudyFinished, StudyEnded,
    NightShift, Tired, FloodlightFitted,
    SurgeryBooked, SurgeryDone, SurgeryComplication, SurgeryCancelled, ProstheticServiced, ProstheticNeedsService, Humanity,
    SchematicFound, SchematicLearned, SchematicHeardOf, ResearchStarted, ResearchFinished, ResearchEnded,
    TraderArrived, TraderLeft, Traded, ContractOffered, ContractAccepted, ContractDone, ContractFailed,
    StoryMission, RadioCall,
    WeatherChanged, Storm, LightningStruck, Repaired,
    HarvestChanged, FoodRotted, FoodPoisoning,
}

/// <summary>Something notable the UI can show in a log. Purely informational.</summary>
public sealed record ColonyEvent(long Tick, ColonyEventType Type, string Message, int? SurvivorId = null, int? BuildingId = null);
