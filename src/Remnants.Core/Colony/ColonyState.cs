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
    public List<ColonyEvent> Events { get; set; } = new();

    public Survivor? FindSurvivor(int id) => Survivors.Find(s => s.Id == id);
    public Building? FindBuilding(int id) => Buildings.Find(b => b.Id == id);

    public IEnumerable<Survivor> WorkersAt(int buildingId) => Survivors.Where(s => s.AssignedBuildingId == buildingId);

    public void Log(ColonyEvent e)
    {
        Events.Add(e);
        if (Events.Count > MaxEvents) Events.RemoveRange(0, Events.Count - MaxEvents);
    }
}
