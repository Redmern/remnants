using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// The colony simulation: definitions + state + seeded RNG. Advances only through <see cref="Tick"/>
/// (fixed step, no wall-clock) and changes only through <see cref="Execute"/>, so it is deterministic.
/// </summary>
public sealed class ColonySim
{
    private readonly DeterministicRng _rng;

    /// <summary>The player id the colony's own party has in a scavenge instance.</summary>
    public const int ExpeditionPlayerId = 1;

    public GameData Data { get; }
    public ColonyState State { get; }

    /// <summary>Items, zones and scavenge rules. Needed for expeditions, prosthetics and item recipes; optional otherwise.</summary>
    public ScavengeData? Scavenge { get; }

    public ColonySim(GameData data, ColonyState state, ScavengeData? scavenge = null)
    {
        Data = data;
        State = state;
        Scavenge = scavenge;
        if (scavenge is not null) ValidateLinks(data, scavenge);
        _rng = new DeterministicRng { State = state.RngState == 0 ? new DeterministicRng(0).State : state.RngState };
        SyncRng();
    }

    /// <summary>Sim-owned randomness. Always draw through this so the state stays in the save.</summary>
    public DeterministicRng Rng => _rng;

    public int TownHallLevel =>
        State.Buildings.Where(b => b.DefId == Data.Rules.TownHallBuildingId).Select(b => b.Level).DefaultIfEmpty(0).Max();

    /// <summary>Highest level this building type may currently reach: its max level, capped by the Town Hall level.</summary>
    public int MaxUpgradeLevel(BuildingDef def) =>
        def.Id == Data.Rules.TownHallBuildingId ? def.MaxLevel : Math.Min(def.MaxLevel, TownHallLevel);

    public int BedCapacity => HousingSystem.Capacity(State, Data);

    public int FreeSlots(Building building) =>
        Data.Buildings[building.DefId].Level(building.Level).WorkerSlots - State.WorkersAt(building.Id).Count();

    /// <summary>Largest expedition party the current Town Hall allows (0 without a Town Hall).</summary>
    public int MaxPartySize
    {
        get
        {
            var sizes = Data.Rules.Expedition.PartySizeByTownHallLevel;
            return TownHallLevel < 1 ? 0 : sizes[Math.Min(TownHallLevel, sizes.Count) - 1];
        }
    }

    public IEnumerable<Building> MedBays => State.Buildings.Where(b => b.DefId == Data.Rules.Medical.BuildingId).OrderBy(b => b.Id);

    public int FreePatientSlots(Building medBay) =>
        Data.Buildings[medBay.DefId].Level(medBay.Level).Patients - State.PatientsAt(medBay.Id).Count();

    /// <summary>Severity healed per tick for a patient at this Med Bay while medicine lasts.</summary>
    public double HealPerTick(Building medBay) => Care.HealPerTick(State, Data, medBay);

    /// <summary>Ticks until a patient is healed at current staffing (null if not a patient or no progress).</summary>
    public int? HealEta(Survivor s)
    {
        if (s.PatientAtBuildingId is not { } id || State.FindBuilding(id) is not { } bay) return null;
        var rate = State.Stockpile.Has(Data.Rules.Medical.MedicineResourceId, Data.Rules.Medical.MedicinePerPatientTick)
            ? HealPerTick(bay)
            : Data.Rules.Medical.NaturalHealPerTick;
        return Care.TicksToHeal(s, rate);
    }

    public bool IsWorsening(Survivor s) => Care.IsWorsening(s, Data.Rules.Medical);

    /// <summary>Recipe effort per tick from the building's current workers.</summary>
    public double CraftEffortPerTick(Building building) => CraftingSystem.EffortPerTick(State, Data, building);

    /// <summary>The scavenge party for the active expedition, from the members' current colony state.</summary>
    public PartySetup ExpeditionParty() =>
        ExpeditionHandlers.Party(this, State.Expedition ?? throw new InvalidOperationException("No expedition is out."));

    /// <summary>
    /// The scavenge instance of the active expedition: resumed from <see cref="Expedition.Scavenge"/> if it was
    /// already started (e.g. loaded mid-mission), otherwise created from the expedition seed and stored there so
    /// colony saves carry the run in progress.
    /// </summary>
    public ScavengeSim OpenExpeditionScavenge()
    {
        var expedition = State.Expedition ?? throw new InvalidOperationException("No expedition is out.");
        var data = Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded.");
        if (expedition.Scavenge is { } running) return new ScavengeSim(data, running);
        var sim = NewScavenge.Create(data, expedition.ZoneId, expedition.Seed, [ExpeditionParty()]);
        expedition.Scavenge = sim.State;
        return sim;
    }

    /// <summary>A survivor's field profile (movement mode, AP, attacks) with this gear, for the expedition planner.</summary>
    public UnitProfile PreviewProfile(Survivor s, Dictionary<EquipSlot, string>? equipment = null, List<string>? pack = null) =>
        NewScavenge.PreviewProfile(Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded."),
            new PartyMember(s, equipment, pack));

    public bool IsPlotFree(int plot) => State.Buildings.All(b => b.Plot != plot);

    /// <summary>Unbuilt plots in ascending order.</summary>
    public IEnumerable<int> FreePlots() => Enumerable.Range(0, Data.Rules.Layout.PlotCount).Where(IsPlotFree);

    public void Tick()
    {
        ProductionSystem.Run(State, Data);
        CraftingSystem.Run(State, Data);
        MedicalSystem.Run(State, Data);
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
            LaunchExpedition c => ExpeditionHandlers.Launch(this, c),
            ApplyExtractionResult c => ExpeditionHandlers.Apply(this, c),
            AdmitPatient c => MedicalHandlers.Admit(this, c),
            Discharge c => MedicalHandlers.Discharge(this, c),
            FitProsthetic c => MedicalHandlers.FitProsthetic(this, c),
            SetRecipe c => CraftingHandlers.SetRecipe(this, c),
            _ => CommandResult.Fail(CommandError.UnknownCommand, $"Unknown command {command.GetType().Name}."),
        };
        SyncRng();
        return result;
    }

    private void SyncRng() => State.RngState = _rng.State;

    /// <summary>Colony data that names items, and item data that names resources, must agree.</summary>
    private static void ValidateLinks(GameData data, ScavengeData scavenge)
    {
        foreach (var id in data.Rules.NewGame.Items.Keys)
            if (!scavenge.Items.ContainsKey(id)) throw new GameDataException($"Rules.newGame.items: unknown item '{id}'.");
        foreach (var b in data.Buildings.Values)
            foreach (var r in b.Recipes.Where(r => r.Item is not null))
                if (!scavenge.Items.ContainsKey(r.Item!)) throw new GameDataException($"Building '{b.Id}' recipe '{r.Id}': unknown item '{r.Item}'.");
        foreach (var item in scavenge.Items.Values)
            if (item.Salvage is { } salvage && !data.Resources.ContainsKey(salvage.Resource))
                throw new GameDataException($"Item '{item.Id}' salvages into unknown resource '{salvage.Resource}'.");
    }
}
