using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Schematics;
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

    /// <summary>The story's missions, dialogue and maps (data/story). Optional; a colony with a campaign needs it to play the story.</summary>
    public Story.StoryData? Story { get; set; }

    public ColonySim(GameData data, ColonyState state, ScavengeData? scavenge = null)
    {
        Data = data;
        State = state;
        Scavenge = scavenge;
        if (scavenge is not null) ValidateLinks(data, scavenge);
        _rng = new DeterministicRng { State = state.RngState == 0 ? new DeterministicRng(0).State : state.RngState };
        // Story calls ring on the Radio Mast; answering one there opens its dialogue.
        RadioCallAnswered += call => global::Remnants.Core.Story.Campaign.Answered(this, call);
        SyncRng();
        if (state.Weather.Seed == 0) state.Weather.Seed = Colony.Weather.SeedFrom(state.RngState);
    }

    /// <summary>The colony learned a schematic (any source: field, decrypt, research, trade, story, migration).</summary>
    public event Action<SchematicUnlocked>? SchematicUnlocked;

    /// <summary>A call joined the Radio Mast's queue (<see cref="RadioMast.Enqueue"/>).</summary>
    public event Action<RadioCall>? RadioCallReceived;

    /// <summary>The player answered a call (<see cref="AnswerCall"/>); Story mode plays its dialogue from <see cref="RadioCall.Tag"/>.</summary>
    public event Action<RadioCall>? RadioCallAnswered;

    internal void RaiseSchematicUnlocked(SchematicUnlocked e) => SchematicUnlocked?.Invoke(e);
    internal void RaiseRadioCallReceived(RadioCall call) => RadioCallReceived?.Invoke(call);
    internal void RaiseRadioCallAnswered(RadioCall call) => RadioCallAnswered?.Invoke(call);

    /// <summary>Sim-owned randomness. Always draw through this so the state stays in the save.</summary>
    public DeterministicRng Rng => _rng;

    public int TownHallLevel =>
        State.Buildings.Where(b => b.DefId == Data.Rules.TownHallBuildingId).Select(b => b.Level).DefaultIfEmpty(0).Max();

    /// <summary>Highest level this building type may currently reach: its max level, capped by the Town Hall level.</summary>
    public int MaxUpgradeLevel(BuildingDef def) =>
        def.Id == Data.Rules.TownHallBuildingId ? def.MaxLevel : Math.Min(def.MaxLevel, TownHallLevel);

    public int BedCapacity => HousingSystem.Capacity(State, Data);

    /// <summary>Food the people at home eat per day (infants, children and adults; a party away lives on its rations).</summary>
    public double FoodPerDay
    {
        get
        {
            var food = Data.Rules.Food;
            return State.Survivors.Where(s => !s.Away)
                .Sum(s => s.IsInfant ? food.InfantPerTick : s.IsChild ? food.ChildPerTick : food.AdultPerTick) * Data.Rules.TicksPerDay;
        }
    }

    /// <summary>Food value in the pantry that is fit to eat (see <see cref="Pantry.FoodValue(ColonySim)"/>).</summary>
    public double FoodStock => Pantry.FoodValue(this);

    /// <summary>Days the food in the pantry lasts at <see cref="FoodPerDay"/> (infinity when nobody eats).</summary>
    public double FoodDays => FoodPerDay <= 0 ? double.PositiveInfinity : FoodStock / FoodPerDay;

    /// <summary>
    /// Units of its resource a gathering building's current workers make per tick (0 for buildings that produce none):
    /// the sum of <see cref="JobOutput.PerTick"/> over them, as <c>ProductionSystem</c> adds it. For a farm, greenhouse or
    /// hunting lodge: the food value its harvest brings in.
    /// </summary>
    public double OutputPerTick(Building building)
    {
        var def = Data.Buildings[building.DefId];
        var factor = OutputFactor(building, def);
        if (def.Job is null || factor <= 0) return 0;
        var job = Data.Jobs[def.Job];
        return State.WorkersAt(building.Id).Sum(w => JobOutput.PerTick(def, building.Level, job, w, Data.Rules.Output, Data)) * factor;
    }

    private double OutputFactor(Building building, BuildingDef def) =>
        def.Produces is not null ? 1
        : Harvesting.Current(Data, building) is { } harvest ? Harvesting.FoodValuePerEffort(State, Data, building, harvest)
        : 0;

    /// <summary>
    /// Units of its resource a gathering building's workers make per day on their schedule (day shift, plus the Night
    /// Shift at its reduced rate when on), at today's stats. Equals <see cref="OutputPerTick"/> · ticksPerDay with
    /// day/night off.
    /// </summary>
    public double OutputPerDay(Building building)
    {
        var def = Data.Buildings[building.DefId];
        var factor = OutputFactor(building, def);
        if (def.Job is null || factor <= 0) return 0;
        var job = Data.Jobs[def.Job];
        return State.WorkersAt(building.Id).Sum(w =>
            JobOutput.PerTick(def, building.Level, job, w, Data.Rules.Output, Data) * Schedule.ShiftHoursPerDay(w, building, def, Data.Rules)) * factor;
    }

    /// <summary>Hour of day now (0..ticksPerDay−1).</summary>
    public int Hour => DayNight.Hour(State.Tick, Data.Rules);

    /// <summary>The day phase now (dawn / day / dusk / night). Presentation and audio switch on this.</summary>
    public DayPhase Phase => DayNight.PhaseAt(State.Tick, Data.Rules);

    /// <summary>Working this tick (assigned, home, on the day shift or a Night Shift).</summary>
    public bool OnShift(Survivor s) => Schedule.OnShift(State, Data, s);

    public bool IsAsleep(Survivor s) => Schedule.IsAsleep(State, Data, s);

    public bool IsTired(Survivor s) => Schedule.IsTired(s, Data.Rules.DayNight);

    /// <summary>Whether a Night Shift may run here (has a job; indoors, level ≥ litMinLevel or a Floodlight).</summary>
    public bool CanRunNightShift(Building building) =>
        Schedule.CanRunNightShift(building, Data.Buildings[building.DefId], Data.Rules.DayNight);

    /// <summary>The summed effects of a survivor's perks, incl. the scavenge hooks (<see cref="PerkHooks"/>).</summary>
    public PerkEffects PerkEffects(Survivor s) => Colony.PerkEffects.For(s, Data);

    /// <summary>A perk's state for this survivor (owned / locked / missing requirements / open) with what is missing.</summary>
    public PerkStatus PerkStatus(Survivor s, PerkDef perk) => Progression.Status(this, s, perk);

    /// <summary>Whether a study can start, and its cap, rate and ETA (for the study panel).</summary>
    public StudyPlan PlanStudy(StartStudy study) => StudySystem.Plan(this, study);

    /// <summary>Ticks until a running class finishes at today's rate.</summary>
    public int? StudyEta(StudyOrder order) => StudySystem.Eta(this, order);

    /// <summary>Students this building can take at once (0 when it teaches nothing).</summary>
    public int StudySlots(Building building) => StudySystem.Slots(Data, building);

    /// <summary>The class a survivor is in as student or mentor, if any.</summary>
    public StudyOrder? ClassOf(Survivor s) => StudySystem.ClassOf(State, s.Id);

    /// <summary>
    /// XP for a survivor from outside the colony tick (the scavenge side's missions and field actions). Logs level-ups.
    /// </summary>
    public int GrantXp(int survivorId, double amount, XpSource source) =>
        State.FindSurvivor(survivorId) is { } s ? Progression.GrantXp(State, Data, s, amount, source) : 0;

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

    /// <summary>Recipe effort per tick averaged over a day on the workers' schedule (for ETAs).</summary>
    public double CraftEffortPerHourAveraged(Building building)
    {
        var def = Data.Buildings[building.DefId];
        if (def.Job is null) return 0;
        var job = Data.Jobs[def.Job];
        return State.WorkersAt(building.Id).Sum(w =>
            JobOutput.PerTick(def, building.Level, job, w, Data.Rules.Output, Data) * Schedule.AverageFactor(w, building, def, Data.Rules));
    }

    /// <summary>Recipe effort per tick from the building's current workers (while on shift).</summary>
    public double CraftEffortPerTick(Building building) => CraftingSystem.EffortPerTick(State, Data, building);

    /// <summary>Whether this recipe can be queued now, and why not (level, schematic, missing item data).</summary>
    public CommandResult CanQueue(Building building, RecipeDef recipe) => CraftingHandlers.CheckRecipe(this, building, recipe);

    /// <summary>The closest skill its workers lack for a queued order (they practise meanwhile), or null.</summary>
    public SkillGap? SkillGap(Building building) => CraftingSystem.Gap(State, Data, building);

    /// <summary>What the head of a building's queue is doing (for the production panel).</summary>
    public CraftStatus CraftStatus(Building building) => CraftingSystem.Status(State, Data, Scavenge, building);

    /// <summary>Chance of each quality tier for a batch of this recipe with the building's current workers (empty for non-gear).</summary>
    public IReadOnlyList<(string Tier, double Chance)> QualityChances(Building building, RecipeDef recipe) =>
        CraftingSystem.QualityChances(State, Data, Scavenge, building, recipe);

    /// <summary>The scavenge party for the active expedition, from the members' current colony state.</summary>
    /// <summary>The active expedition's phase now, or null when no party is out.</summary>
    public ExpeditionPhase? CurrentExpeditionPhase => State.Expedition?.PhaseAt(State.Tick);

    /// <summary>Every zone with what the colony knows about it, for the world map (unknown zones included; the UI hides them).</summary>
    public IEnumerable<ZoneInfo> WorldZones()
    {
        var scavenge = Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded.");
        return scavenge.Zones.Values.OrderBy(z => z.Threat).ThenBy(z => z.Id, StringComparer.Ordinal)
            .Select(z => WorldMap.Info(State, Data, scavenge, z));
    }

    public ZoneInfo ZoneInfo(string zoneId)
    {
        var scavenge = Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded.");
        return WorldMap.Info(State, Data, scavenge, scavenge.Zones[zoneId]);
    }

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
        if (expedition.PhaseAt(State.Tick) != ExpeditionPhase.InZone)
            throw new InvalidOperationException("The party is not in the zone (still on the road, or heading home).");
        if (expedition.Scavenge is { } running) return new ScavengeSim(data, running);
        var mission = expedition.MissionId is { } missionId ? global::Remnants.Core.Story.Campaign.Setup(this, missionId) : null;
        var sim = NewScavenge.Create(data, expedition.ZoneId, expedition.Seed, [ExpeditionParty()],
            new ZoneConditions(expedition.Depletion, MissionEnvironment(expedition.ArrivalTick, expedition.ZoneId), State.Schematics.Known), mission);
        expedition.Scavenge = sim.State;
        return sim;
    }

    /// <summary>
    /// The field environment of a mission that starts at <paramref name="tick"/>: its hour and day phase, and with a zone
    /// the weather over its region then, with the spells that follow within <see cref="FieldWeatherRules.LookaheadHours"/>
    /// as changes during the run (<see cref="FieldWeatherRules.RoundsPerHour"/> rounds to the hour).
    /// </summary>
    public ScavengeEnvironment MissionEnvironment(long tick, string? zoneId = null)
    {
        var env = Data.Rules.DayNight.Enabled
            ? ScavengeEnvironment.At(DayNight.Hour(tick, Data.Rules), DayNight.PhaseAt(tick, Data.Rules))
            : ScavengeEnvironment.At(12, DayPhase.Day);
        if (!Data.Weather.Enabled || zoneId is null || Scavenge?.Zones.GetValueOrDefault(zoneId) is not { } zone) return env;
        var region = Colony.Weather.RegionOf(Data, zone);
        var field = Data.Weather.Field;
        var spells = Colony.Weather.SpellsBetween(State, Data, region, tick, tick + field.LookaheadHours);
        env.Weather = spells[0].Kind;
        env.Intensity = spells[0].Intensity;
        env.Shifts = spells.Skip(1)
            .Select(s => new WeatherShift(1 + (int)(s.Start - tick) * field.RoundsPerHour, s.Kind, s.Intensity)).ToList();
        return env;
    }

    /// <summary>
    /// When a party launched now would reach a zone, in which phase, and — when waiting for <paramref name="waitFor"/> — the
    /// hours they wait outside for it to start and the extra food per member (<c>rationFoodPerMemberPerDay</c> per day
    /// waited, rounded up to whole units). No wait when they would arrive in that phase anyway, or with day/night off
    /// (every mission is then at midday).
    /// </summary>
    public ArrivalPlan PlanArrival(string zoneId, DayPhase? waitFor = null)
    {
        var scavenge = Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded.");
        var zone = scavenge.Zones[zoneId];
        var travel = WorldMap.Travel(this, zone, State.Tick);
        var arrival = State.Tick + travel.Ticks;
        var delay = travel.DelayTicks <= 0 ? 0 : Math.Ceiling(Data.Rules.Expedition.RationFoodPerMemberPerDay * travel.DelayTicks / Data.Rules.TicksPerDay);
        if (!Data.Rules.DayNight.Enabled) return new ArrivalPlan(arrival, DayPhase.Day, 0, 0, travel, delay);
        long wait = 0;
        if (waitFor is { } want && DayNight.PhaseAt(arrival, Data.Rules) != want)
        {
            var start = DayNight.StartHour(want, Data.Rules.DayNight);
            wait = ((start - DayNight.Hour(arrival, Data.Rules)) % Data.Rules.TicksPerDay + Data.Rules.TicksPerDay) % Data.Rules.TicksPerDay;
        }
        var food = wait == 0 ? 0 : Math.Ceiling(Data.Rules.Expedition.RationFoodPerMemberPerDay * wait / Data.Rules.TicksPerDay);
        return new ArrivalPlan(arrival + wait, DayNight.PhaseAt(arrival + wait, Data.Rules), wait, food, travel, delay);
    }

    /// <summary>A survivor's field profile (movement mode, AP, attacks) with this gear, for the expedition planner.</summary>
    public UnitProfile PreviewProfile(Survivor s, Dictionary<EquipSlot, string>? equipment = null, List<string>? pack = null) =>
        NewScavenge.PreviewProfile(Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded."),
            new PartyMember(s, equipment, pack));

    public bool IsPlotFree(int plot) => State.Buildings.All(b => b.Plot != plot);

    /// <summary>Unbuilt plots in ascending order.</summary>
    public IEnumerable<int> FreePlots() => Enumerable.Range(0, Data.Rules.Layout.PlotCount).Where(IsPlotFree);

    /// <summary>A trade courier arrived and handed over its goods (for a toast).</summary>
    public event Action<Trade.CourierDelivery>? CourierArrived;

    internal void RaiseCourierArrived(Trade.CourierDelivery delivery) => CourierArrived?.Invoke(delivery);

    public void Tick()
    {
        ProductionSystem.Run(State, Data);
        HarvestSystem.Run(State, Data);
        CraftingSystem.Run(State, Data, Scavenge, _rng, this);
        StudySystem.Run(State, Data, Scavenge);
        Research.Run(this);
        MedicalSystem.Run(State, Data);
        FoodSystem.Run(State, Data, Scavenge, _rng);
        DayNightSystem.Run(State, Data);
        WeatherSystem.Run(State, Data, _rng);
        RelationshipSystem.Run(State, Data);
        CyberneticsSystem.Run(this, _rng);
        FamilySystem.Run(State, Data, _rng);
        HousingSystem.Run(State, Data);
        GrowthSystem.Run(State, Data);
        State.Tick++;
        WorldMap.Run(this);
        global::Remnants.Core.Story.Campaign.Tick(this);
        RadioMast.Run(this);
        Trade.ColonyTrade.Run(this);
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
            SendScoutDrone c => WorldMap.SendDrone(this, c),
            ApplyExtractionResult c => ExpeditionHandlers.Apply(this, c),
            AdmitPatient c => MedicalHandlers.Admit(this, c),
            Discharge c => MedicalHandlers.Discharge(this, c),
            FitProsthetic c => MedicalHandlers.FitProsthetic(this, c),
            ScheduleSurgery c => CyberneticsHandlers.Schedule(this, c),
            CancelSurgery c => CyberneticsHandlers.Cancel(this, c),
            SetRecipe c => CraftingHandlers.SetRecipe(this, c),
            QueueRecipe c => CraftingHandlers.Queue(this, c),
            CancelOrder c => CraftingHandlers.Cancel(this, c),
            Matchmake c => LifeHandlers.Matchmake(this, c),
            ChoosePerk c => ProgressionHandlers.ChoosePerk(this, c),
            StartStudy c => StudyHandlers.Start(this, c),
            CancelStudy c => StudyHandlers.Cancel(this, c),
            SetNightShift c => DayNightHandlers.SetNightShift(this, c),
            SetHarvest c => HarvestHandlers.Set(this, c),
            InstallFloodlight c => DayNightHandlers.InstallFloodlight(this, c),
            RepairBuilding c => WeatherHandlers.Repair(this, c),
            global::Remnants.Core.Story.TakeCall or global::Remnants.Core.Story.AdvanceDialogue or global::Remnants.Core.Story.ChooseDialogue
                or global::Remnants.Core.Story.LaunchStoryMission => global::Remnants.Core.Story.Campaign.Execute(this, command),
            _ => SchematicHandlers.Handle(this, command)
                 ?? CommandResult.Fail(CommandError.UnknownCommand, $"Unknown command {command.GetType().Name}."),
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
        foreach (var r in data.Recipes.Values)
        {
            if (r.Item is not null && !scavenge.Items.ContainsKey(r.Item)) throw new GameDataException($"Recipe '{r.Id}': unknown item '{r.Item}'.");
            foreach (var id in r.ItemInputs.Keys)
                if (!scavenge.Items.ContainsKey(id)) throw new GameDataException($"Recipe '{r.Id}': unknown input item '{id}'.");
        }
        if (data.Schematics.All.Keys.Where(id => !scavenge.Schematics.All.ContainsKey(id)).FirstOrDefault() is { } missing)
            throw new GameDataException($"Schematic '{missing}' is in the colony data but not in the scavenge data (load both from one schematics.json).");
        foreach (var tier in data.Rules.Crafting.QualityThresholds.Keys)
            if (scavenge.Rules.Quality.All(q => q.Id != tier)) throw new GameDataException($"Rules.crafting.qualityThresholds: unknown quality tier '{tier}'.");
        foreach (var item in scavenge.Items.Values)
            if (item.Manual is { } manual && (item.Kind != ItemKind.Material || item.Salvage is not null || manual.StudyBonus < 0))
                throw new GameDataException($"Item '{item.Id}': manuals are Material items without salvage and with a non-negative study bonus.");
        foreach (var item in scavenge.Items.Values)
            if (item.Salvage is { } salvage && !data.Resources.ContainsKey(salvage.Resource))
                throw new GameDataException($"Item '{item.Id}' salvages into unknown resource '{salvage.Resource}'.");
        if (data.Cybernetics is { } cyber)
            foreach (var node in cyber.Nodes.Values)
            {
                if (!scavenge.Items.ContainsKey(node.Id)) throw new GameDataException($"Cybernetics node '{node.Id}': no item of that id.");
            }
    }
}
