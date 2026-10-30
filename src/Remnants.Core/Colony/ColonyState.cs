using Remnants.Core.Schematics;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// The complete mutable colony state. Plain data so it serializes as a save game;
/// all rules live in <see cref="ColonySim"/> and its systems.
/// </summary>
public sealed class ColonyState
{
    public const int MaxEvents = 200;

    public long Tick { get; set; }
    public ulong RngState { get; set; }
    public int NextSurvivorId { get; set; } = 1;
    public int NextBuildingId { get; set; } = 1;
    public List<Survivor> Survivors { get; set; } = new();
    public List<Building> Buildings { get; set; } = new();
    public Stockpile Stockpile { get; set; } = new();

    /// <summary>Items the colony owns (gear, consumables, prosthetics, food), by item id.</summary>
    public ItemInventory Inventory { get; set; } = new();

    /// <summary>The food lots behind the food items in <see cref="Inventory"/>: age, shelf life, quality (see <see cref="Pantry"/>).</summary>
    public FoodState Food { get; set; } = new();

    /// <summary>The party currently out in a zone, if any. One expedition at a time.</summary>
    public Expedition? Expedition { get; set; }

    public int NextExpeditionId { get; set; } = 1;

    /// <summary>World map knowledge and depletion per zone id; zones never touched are not listed.</summary>
    public SortedDictionary<string, ZoneStatus> Zones { get; set; } = new(StringComparer.Ordinal);

    public int NextOrderId { get; set; } = 1;

    /// <summary>Schematics known, heard of and being researched (see <see cref="Schematics.Schematics"/>).</summary>
    public SchematicsState Schematics { get; set; } = new();

    /// <summary>The Radio Mast: trader visits, contracts and the call queue (see <see cref="RadioMast"/>).</summary>
    public RadioState Radio { get; set; } = new();

    /// <summary>Classes running at study buildings (see <see cref="StudySystem"/>).</summary>
    public List<StudyOrder> Studies { get; set; } = new();

    /// <summary>Scout drones out over the world map (<see cref="WorldMap.SendDrone"/>).</summary>
    public List<DroneFlight> DroneFlights { get; set; } = new();
    public int NextFlightId { get; set; } = 1;

    public int NextStudyId { get; set; } = 1;

    /// <summary>Booked surgeries, in booking order; each Med Bay works its first (see <see cref="CyberneticsSystem"/>).</summary>
    public List<Surgery> Surgeries { get; set; } = new();
    public int NextSurgeryId { get; set; } = 1;

    /// <summary>
    /// Cybernetics node ids the colony knows: crafted, fitted or owned at some point (tier 0 is always known). A node of the
    /// next tier needs one of its requirements in here.
    /// </summary>
    public SortedSet<string> KnownCybernetics { get; set; } = new(StringComparer.Ordinal);
    /// <summary>The weather's seed and the spells worked out so far (see <see cref="Colony.Weather"/>).</summary>
    public WeatherState Weather { get; set; } = new();

    /// <summary>The power grid as of the last tick (see <see cref="Colony.Power"/>); empty in saves from before the grid.</summary>
    public PowerState Power { get; set; } = new();

    /// <summary>Devices home with spent charges, in arrival order, until the charging bench has them full again.</summary>
    public List<ChargingItem> Charging { get; set; } = new();

    /// <summary>The after-action report of the last expedition that came home (for the UI).</summary>
    public ExpeditionReport? LastReport { get; set; }

    /// <summary>Affinity between pairs of adults (and the player's stance on them). See <see cref="Bonds"/>.</summary>
    public List<Relationship> Relationships { get; set; } = new();

    /// <summary>Story mode's campaign (missions, flags, the radio inbox); null in Sandbox.</summary>
    public Story.CampaignState? Campaign { get; set; }

    /// <summary>Everyone who died, oldest first. Never trimmed.</summary>
    public List<MemorialEntry> Memorial { get; set; } = new();
    public List<ColonyEvent> Events { get; set; } = new();

    /// <summary>Player trading: goods in escrow, couriers on the road, settled transactions (see <see cref="Trade.ColonyTrade"/>).</summary>
    public Trade.TradeState Trade { get; set; } = new();

    public Survivor? FindSurvivor(int id) => Survivors.Find(s => s.Id == id);
    public Building? FindBuilding(int id) => Buildings.Find(b => b.Id == id);

    public IEnumerable<Survivor> WorkersAt(int buildingId) => Survivors.Where(s => s.AssignedBuildingId == buildingId);

    public IEnumerable<Survivor> PatientsAt(int buildingId) => Survivors.Where(s => s.PatientAtBuildingId == buildingId);

    /// <summary>Survivors at home (not away on an expedition).</summary>
    public IEnumerable<Survivor> Present => Survivors.Where(s => !s.Away);

    /// <summary>The relationship record of two survivors, if there is one.</summary>
    public Relationship? FindRelationship(int a, int b) =>
        Relationships.Find(r => r.A == Math.Min(a, b) && r.B == Math.Max(a, b));

    public void Log(ColonyEvent e)
    {
        Events.Add(e);
        if (Events.Count > MaxEvents) Events.RemoveRange(0, Events.Count - MaxEvents);
    }
}
