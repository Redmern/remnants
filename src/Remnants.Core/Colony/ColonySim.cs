using Remnants.Core.Data;
using Remnants.Core.Random;

namespace Remnants.Core.Colony;

/// <summary>
/// The colony simulation: definitions + state + seeded RNG. Advances only through <see cref="Tick"/>
/// (fixed step, no wall-clock) and changes only through <see cref="Execute"/>, so it is deterministic.
/// </summary>
public sealed class ColonySim
{
    private readonly DeterministicRng _rng;

    public GameData Data { get; }
    public ColonyState State { get; }

    public ColonySim(GameData data, ColonyState state)
    {
        Data = data;
        State = state;
        _rng = new DeterministicRng { State = state.RngState == 0 ? new DeterministicRng(0).State : state.RngState };
        SyncRng();
    }

    /// <summary>Sim-owned randomness. Always draw through this so the state stays in the save.</summary>
    public DeterministicRng Rng => _rng;

    public int TownHallLevel =>
        State.Buildings.Where(b => b.DefId == Data.Rules.TownHallBuildingId).Select(b => b.Level).DefaultIfEmpty(0).Max();

    public int BedCapacity => HousingSystem.Capacity(State, Data);

    public int FreeSlots(Building building) =>
        Data.Buildings[building.DefId].Level(building.Level).WorkerSlots - State.WorkersAt(building.Id).Count();

    public bool IsPlotFree(int plot) => State.Buildings.All(b => b.Plot != plot);

    /// <summary>Unbuilt plots in ascending order.</summary>
    public IEnumerable<int> FreePlots() => Enumerable.Range(0, Data.Rules.Layout.PlotCount).Where(IsPlotFree);

    public void Tick()
    {
        ProductionSystem.Run(State, Data);
        FoodSystem.Run(State, Data);
        HousingSystem.Run(State, Data);
        AgingSystem.Run(State, Data);
        State.Tick++;
        SyncRng();
    }

    public void Advance(int ticks)
    {
        for (var i = 0; i < ticks; i++) Tick();
    }

    public CommandResult Execute(IColonyCommand command)
    {
        var result = command switch
        {
            AssignJob c => CommandHandlers.AssignJob(this, c),
            Unassign c => CommandHandlers.Unassign(this, c),
            Construct c => CommandHandlers.Construct(this, c),
            Upgrade c => CommandHandlers.Upgrade(this, c),
            _ => CommandResult.Fail(CommandError.UnknownCommand, $"Unknown command {command.GetType().Name}."),
        };
        SyncRng();
        return result;
    }

    private void SyncRng() => State.RngState = _rng.State;
}
