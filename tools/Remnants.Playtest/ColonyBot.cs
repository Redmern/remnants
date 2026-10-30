using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Story;
using Remnants.Core.Survivors;

namespace Remnants.Playtest;

/// <summary>Knobs for the scripted strategy.</summary>
public sealed record BotOptions
{
    /// <summary>First day an expedition may leave (players usually look around first).</summary>
    public int FirstExpeditionDay { get; init; } = 2;

    /// <summary>Days to rest between expeditions.</summary>
    public int ExpeditionCooldownDays { get; init; } = 3;

    /// <summary>Send expeditions at all.</summary>
    public bool Expeditions { get; init; } = true;

    /// <summary>Highest zone threat the bot will risk (it climbs only after a clean run at the level below).</summary>
    public int MaxThreat { get; init; } = 2;

    /// <summary>Story mode: which option the party bot picks on a field prompt (0 = the first, e.g. talk to the Tally).</summary>
    public int StoryPrompt { get; init; }
}

/// <summary>
/// A simple scripted player for a fresh colony, using only Core commands: keep food positive, build down a fixed
/// order of buildings and upgrades, staff gathering by what the next goal is short of, craft basic gear and
/// medicine, admit the injured, and send the strongest adults on expeditions to the easiest known zone (resolved
/// by <see cref="PartyBot"/>). It is deliberately not clever: it stands in for a sensible first-time player.
/// </summary>
public sealed class ColonyBot
{
    private static readonly (string Building, int Level)[] BuildOrder =
    [
        ("farm", 1), ("woodcutter", 1), ("quarry", 1), ("workshop", 1), ("scrapyard", 1), ("med_bay", 1),
        ("farm", 2), ("woodcutter", 2), ("quarry", 2), ("kitchen", 1), ("tailor", 1), ("town_hall", 2),
        ("house", 1), ("armory", 1), ("scrapyard", 2), ("workshop", 2), ("farm", 3), ("electronics_lab", 1),
        ("tailor", 2), ("med_bay", 2), ("armory", 2), ("woodcutter", 3), ("quarry", 3), ("town_hall", 3), ("house", 2), ("electronics_lab", 2),
    ];

    private readonly ColonySim _sim;
    private readonly BotOptions _options;
    private long _lastReturnTick = long.MinValue / 2;
    private int _cleanRunsAtThreat;
    private int _threatCap = 1;
    private bool _stuck;

    public ColonyBot(ColonySim sim, BotOptions? options = null)
    {
        _sim = sim;
        _options = options ?? new BotOptions();
    }

    public List<ExpeditionOutcome> Expeditions { get; } = [];

    /// <summary>Gets every scavenge event of the bot's runs, when set.</summary>
    public Action<string>? Trace { get; set; }

    private ColonyState State => _sim.State;
    private GameData Data => _sim.Data;
    private int TicksPerDay => Data.Rules.TicksPerDay;
    private double Day => State.Tick / (double)TicksPerDay;

    /// <summary>One decision pass. Call it every few ticks.</summary>
    public void Act()
    {
        Story();
        Build();
        Craft();
        Care();
        Cyber();
        Staff();
        Perks();
        if (_options.Expeditions) Expedition();
    }

    // ---- perks ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Spends perk points as they come: an open perk that helps the survivor's current job (output or quality) first,
    /// then any open perk, lowest tier first. The bot does not study (classes take people off work).
    /// </summary>
    private void Perks()
    {
        foreach (var s in State.Survivors.Where(s => s.PerkPoints > 0).ToList())
        {
            var job = s.AssignedBuildingId is { } id && State.FindBuilding(id) is { } b ? Data.Buildings[b.DefId].Job : null;
            var open = Data.Perks.Values.Where(p => _sim.PerkStatus(s, p).Block == PerkBlock.Ok)
                .OrderByDescending(p => job is not null && (p.Effects.JobOutput.ContainsKey(job) || p.Effects.CraftQuality.ContainsKey(job)))
                .ThenBy(p => p.Tier).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (open is not null) _sim.Execute(new ChoosePerk(s.Id, open.Id));
        }
    }

    // ---- building -------------------------------------------------------------------------------------------

    /// <summary>The next build-order step not done yet, skipping ones the Town Hall does not allow yet.</summary>
    public (string Building, int Level)? NextGoal()
    {
        foreach (var (id, level) in BuildOrder)
        {
            var have = State.Buildings.Where(b => b.DefId == id).Select(b => b.Level).DefaultIfEmpty(0).Max();
            if (have >= level) continue;
            var def = Data.Buildings[id];
            if (level == 1 && def.MinTownHallLevel > _sim.TownHallLevel) continue;
            if (level > 1 && level > _sim.MaxUpgradeLevel(def)) continue;
            return (id, level);
        }
        return null;
    }

    public Dictionary<string, double> GoalCost() =>
        NextGoal() is { } g ? new(Data.Buildings[g.Building].Level(g.Level).Cost) : new();

    private void Build()
    {
        if (NextGoal() is not { } goal) return;
        var cost = Data.Buildings[goal.Building].Level(goal.Level).Cost;
        // Keep a little food-making wood in reserve only for the first farm; otherwise build as soon as affordable.
        if (!State.Stockpile.CanAfford(cost)) return;
        var existing = State.Buildings.Where(b => b.DefId == goal.Building).OrderBy(b => b.Level).FirstOrDefault();
        var result = goal.Level == 1 && existing is null
            ? _sim.Execute(new Construct(goal.Building))
            : _sim.Execute(new Upgrade(existing!.Id));
        if (!result.Ok) Problems.Add($"day {Day:0.0}: build {goal.Building} L{goal.Level}: {result.Message}");
    }

    public List<string> Problems { get; } = [];

    // ---- crafting -------------------------------------------------------------------------------------------

    private Building? Find(string defId) => State.Buildings.FirstOrDefault(b => b.DefId == defId);

    private void Craft()
    {
        if (Find("workshop") is { } workshop && workshop.Queue.Count == 0)
        {
            var weapons = Count("pipe") + Count("spear") + Count("machete");
            if (State.Stockpile.Get("medicine") < 3 && State.Stockpile.Get("food") > 25) Queue(workshop, "herbal_remedy");
            else if (weapons < 3 && State.Stockpile.Get("scrap") >= 6) Queue(workshop, "spear");
            else if (workshop.Level >= 2 && Count("med_kit") < 2 && State.Stockpile.Get("cloth") >= 2 && State.Stockpile.Get("medicine") >= 3)
                Queue(workshop, "med_kit");
        }
        if (Find("tailor") is { } tailor && tailor.Queue.Count == 0)
        {
            if (State.Stockpile.Get("cloth") < 4 && State.Stockpile.Get("food") > 50) Queue(tailor, "fiber_cloth");
            else if (Count("bandage") < 4 && State.Stockpile.Get("cloth") >= 1) Queue(tailor, "bandage");
            else if (Count("padded_vest") < 2 && State.Stockpile.Get("cloth") >= 5) Queue(tailor, "padded_vest");
        }
        if (Find("kitchen") is { } kitchen && kitchen.Queue.Count == 0 && State.Stockpile.Get("food") > 30)
            Queue(kitchen, "simple_meal");
        if (Find("armory") is { } armory && armory.Queue.Count == 0 && Count("machete") < 2 && State.Stockpile.Get("scrap") >= 10)
            Queue(armory, "machete");
        if (Find("electronics_lab") is { } lab && lab.Queue.Count == 0 && State.Stockpile.Get("scrap") >= 20)
            Queue(lab, "salvage_circuits");
        foreach (var bp in State.Inventory.Counts.Keys.Where(id => _sim.Scavenge!.Items[id].Kind == ItemKind.Blueprint).ToList())
            _sim.Execute(new StudyBlueprint(bp));
    }

    private int Count(string baseId) =>
        State.Inventory.Counts.Where(kv => _sim.Scavenge!.Items.TryGetValue(kv.Key, out var d) && d.DefinedId == baseId).Sum(kv => kv.Value);

    private void Queue(Building b, string recipe)
    {
        var r = _sim.Execute(new QueueRecipe(b.Id, recipe, 1));
        if (!r.Ok) Problems.Add($"day {Day:0.0}: queue {recipe}: {r.Message}");
    }

    // ---- care -----------------------------------------------------------------------------------------------

    private void Care()
    {
        if (!_sim.MedBays.Any()) return;
        // Light wounds heal on their own while people work; only the ones that would not (or would worsen) need a bed.
        var natural = Data.Rules.Medical.NaturalHealMaxSeverity;
        foreach (var s in State.Survivors.Where(s => !s.Away && s.PatientAtBuildingId is null
                     && s.Body.Parts.Values.Any(p => p.Condition == PartCondition.Injured && p.Severity >= natural)).ToList())
            if (_sim.MedBays.Any(b => _sim.FreePatientSlots(b) > 0))
                _sim.Execute(new AdmitPatient(s.Id));
    }

    // ---- cybernetics -----------------------------------------------------------------------------------------

    private static readonly BodyPart[] Limbs = [BodyPart.LeftArm, BodyPart.RightArm, BodyPart.LeftLeg, BodyPart.RightLeg];

    /// <summary>
    /// Replacements only (no elective augmentation): fit the best part the inventory has for a missing limb, or for a
    /// crude / mechanical one once something better is in hand — straps on below the surgery tier, else surgery when the
    /// Med Bay can operate. A limb still without a part gets the best design the tree allows crafted, components first.
    /// </summary>
    private void Cyber()
    {
        if (Data.Cybernetics is not { } cyber || _sim.Scavenge is not { } items || _sim.MedBays.FirstOrDefault() is not { } bay) return;
        var wanted = new HashSet<CyberSlot>();
        foreach (var s in State.Survivors.Where(s => !s.Away && !s.IsInfant).OrderBy(s => s.Id).ToList())
            foreach (var part in Limbs)
            {
                var limb = s.Body[part];
                if (limb.Condition is not (PartCondition.Missing or PartCondition.Prosthetic)) continue;
                if (State.Surgeries.Any(o => o.SurvivorId == s.Id && o.Part == part)) continue;
                var tierNow = limb.Condition == PartCondition.Prosthetic ? cyber.TierOf(limb) : -1;
                if (tierNow >= 2) continue;
                var best = State.Inventory.Counts.Keys.Select(id => items.Items[id])
                    .Where(i => i.Prosthetic is { } p && p.Region == Body.RegionOf(part) && p.MinMedBayLevel <= bay.Level
                                && cyber.Node(i.DefinedId) is { } n && n.Tier > tierNow)
                    .OrderByDescending(i => cyber.Node(i.DefinedId)!.Tier).ThenByDescending(i => i.Prosthetic!.Quality).ThenBy(i => i.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (best is null)
                {
                    if (CyberneticsData.SlotOf(part) is { } slot) wanted.Add(slot);
                    continue;
                }
                var tier = cyber.Node(best.DefinedId)!.Tier;
                CommandResult r;
                if (tier < cyber.Surgery.FromTier && limb.Condition == PartCondition.Missing)
                    r = _sim.Execute(new FitProsthetic(s.Id, part, best.Id));
                else if (tier < cyber.Surgery.CyberSurgeonFromTier
                         || State.Survivors.Any(m => !m.Away && PerkEffects.For(m, Data).Has(PerkHooks.CyberSurgery)))
                    r = _sim.Execute(new ScheduleSurgery(s.Id, part, best.Id));
                else continue;
                if (!r.Ok && r.Error != CommandError.MedBayFull) Problems.Add($"day {Day:0.0}: fit {best.Id}: {r.Message}");
            }
        foreach (var slot in wanted) CraftBest(cyber, slot);
        if (wanted.Count == 0) Explore(cyber);
    }

    /// <summary>
    /// A curious player builds one part of each tier as it opens up (the next tier needs one known), while the stock is
    /// comfortable: the cheapest open design of the lowest tier the colony does not know yet, arms and legs first.
    /// </summary>
    private void Explore(CyberneticsData cyber)
    {
        if (State.Stockpile.Get("scrap") < 40 || State.Stockpile.Get("food") < 30) return;
        var next = cyber.Nodes.Values.Where(n => n.Slot != CyberSlot.Eye)
            .Select(n => Cybernetics.Status(_sim, n))
            .Where(st => st.State == CyberNodeState.Available && st.Recipe is not null && Find(st.Recipe.Building) is not null)
            .GroupBy(st => st.Node.Tier).OrderBy(g => g.Key).FirstOrDefault(g => cyber.Nodes.Values.Where(n => n.Tier == g.Key).All(n => !Cybernetics.IsKnown(State, n)));
        if (next is null) return;
        var slot = next.OrderBy(st => st.Recipe!.Inputs.Values.Sum() + st.Recipe.ItemInputs.Values.Sum() * 5).ThenBy(st => st.Node.Id, StringComparer.Ordinal).First().Node.Slot;
        CraftBest(cyber, slot, next.Key);
    }

    /// <summary>Queues the best open design for the slot (its missing components first) unless one is already on the way.</summary>
    private void CraftBest(CyberneticsData cyber, CyberSlot slot, int? tier = null)
    {
        bool Queued(string recipeId) => State.Buildings.Any(b => b.Queue.Any(o => o.RecipeId == recipeId));
        var options = cyber.Nodes.Values.Where(n => n.Slot == slot && (tier is null || n.Tier == tier))
            .Select(n => Cybernetics.Status(_sim, n))
            .Where(st => st.State != CyberNodeState.Locked && st.Recipe is not null && Find(st.Recipe.Building) is not null)
            .OrderByDescending(st => st.Node.Tier).ThenByDescending(st => st.Node.Spec is null).ThenBy(st => st.Node.Id, StringComparer.Ordinal)
            .ToList();
        if (options.Any(st => Queued(st.Recipe!.Id))) return;
        foreach (var st in options)
        {
            var recipe = st.Recipe!;
            var missing = recipe.ItemInputs.Where(kv => Count(kv.Key) < kv.Value).Select(kv => kv.Key).ToList();
            var parts = missing.Select(id => Data.Recipes.Values.FirstOrDefault(r => r.Item == id)).ToList();
            if (parts.Any(r => r is null || Find(r.Building) is not { } at || at.Level < r.MinLevel)) continue;
            foreach (var r in parts)
                if (!Queued(r!.Id)) Queue(Find(r.Building)!, r.Id);
            if (missing.Count == 0) Queue(Find(recipe.Building)!, recipe.Id);
            return;
        }
    }

    // ---- staffing -------------------------------------------------------------------------------------------

    /// <summary>
    /// Re-staff from scratch: farmers until food is safely positive, then crafters for buildings with a queue, then
    /// gatherers weighted by what the next build goal is short of (wood and stone by default).
    /// </summary>
    private void Staff()
    {
        var workers = State.Survivors.Where(s => !s.Away && !s.IsInfant && s.PatientAtBuildingId is null).ToList();
        var slots = State.Buildings.Where(b => Data.Buildings[b.DefId].Job is not null)
            .ToDictionary(b => b.Id, b => Data.Buildings[b.DefId].Level(b.Level).WorkerSlots);
        var plan = new Dictionary<int, int>(); // survivor -> building
        bool Take(Survivor s, Building b)
        {
            var job = Data.Jobs[Data.Buildings[b.DefId].Job!];
            if (s.IsChild && !job.ChildrenAllowed) return false;
            if (plan.Values.Count(id => id == b.Id) >= slots[b.Id]) return false;
            plan[s.Id] = b.Id;
            return true;
        }

        var free = workers.ToList();
        // Averaged over the day: workers only produce on their shift (see Schedule).
        double Output(Survivor s, Building b) =>
            JobOutput.PerTick(Data.Buildings[b.DefId], b.Level, Data.Jobs[Data.Buildings[b.DefId].Job!], s, Data.Rules.Output)
            * Schedule.ShiftHoursPerDay(s, b, Data.Buildings[b.DefId], Data.Rules) / TicksPerDay;

        // Food: aim for need × margin, more while the stock is low.
        var food = Data.Rules.Food;
        var need = State.Survivors.Where(s => !s.Away).Sum(s => s.LifeStage switch
        {
            LifeStage.Infant => food.InfantPerTick,
            LifeStage.Child => food.ChildPerTick,
            _ => food.AdultPerTick,
        });
        var days = State.Stockpile.Get("food") / Math.Max(need * TicksPerDay, 1e-6);
        var margin = days < 3 ? 1.6 : days < 8 ? 1.25 : days > 25 ? 0.9 : 1.1;
        var produced = 0.0;
        foreach (var farm in State.Buildings.Where(b => b.DefId == "farm").OrderByDescending(b => b.Level))
            while (produced < need * margin && free.Count > 0)
            {
                // Children farm first (it is one of their jobs); adults fill the rest.
                var best = free.OrderBy(s => s.IsChild ? 0 : 1).ThenByDescending(s => Output(s, farm)).FirstOrDefault(s => !s.IsChild || Data.Jobs["farming"].ChildrenAllowed);
                if (best is null || !Take(best, farm)) break;
                produced += Output(best, farm);
                free.Remove(best);
            }

        // A medic while anyone is in a bed, crafters for queues (one each), then gatherers by shortage of the goal.
        foreach (var b in State.Buildings.Where(b => b.Queue.Count > 0 || State.PatientsAt(b.Id).Any()).OrderBy(b => b.Id))
        {
            var pick = free.OrderByDescending(s => Output(s, b)).FirstOrDefault(s => !s.IsChild || Data.Jobs[Data.Buildings[b.DefId].Job!].ChildrenAllowed);
            if (pick is not null && Take(pick, b)) free.Remove(pick);
        }

        var goal = GoalCost();
        double Short(string res) => Math.Max(0, goal.GetValueOrDefault(res) - State.Stockpile.Get(res)) + 5;
        var gatherers = State.Buildings.Where(b => Data.Buildings[b.DefId] is { Produces: { } p } && p != "food").ToList();
        while (free.Count > 0)
        {
            var target = gatherers.Where(b => plan.Values.Count(id => id == b.Id) < slots[b.Id])
                .OrderByDescending(b => Short(Data.Buildings[b.DefId].Produces!) / (1 + plan.Values.Count(id => id == b.Id)))
                .FirstOrDefault(b => free.Any(s => !s.IsChild || Data.Jobs[Data.Buildings[b.DefId].Job!].ChildrenAllowed));
            // Spare hands go back to the farm (food never hurts), or idle.
            target ??= State.Buildings.FirstOrDefault(b => b.DefId == "farm" && plan.Values.Count(id => id == b.Id) < slots[b.Id]);
            if (target is null) break;
            var pick = free.OrderByDescending(s => Output(s, target)).FirstOrDefault(s => !s.IsChild || Data.Jobs[Data.Buildings[target.DefId].Job!].ChildrenAllowed);
            if (pick is null || !Take(pick, target)) break;
            free.Remove(pick);
        }

        // Free everyone who moves first, so nobody blocks the slot they are leaving.
        var moving = workers.Where(s => s.AssignedBuildingId != (plan.TryGetValue(s.Id, out var b) ? b : (int?)null)).ToList();
        foreach (var s in moving.Where(s => s.AssignedBuildingId is not null)) _sim.Execute(new Unassign(s.Id));
        foreach (var s in moving.Where(s => plan.ContainsKey(s.Id)))
        {
            var r = _sim.Execute(new AssignJob(s.Id, plan[s.Id]));
            if (!r.Ok) Problems.Add($"day {Day:0.0}: assign {s.Name}: {r.Message}");
        }
    }

    // ---- expeditions ----------------------------------------------------------------------------------------

    private void Expedition()
    {
        var scavenge = _sim.Scavenge!;
        if (State.Expedition is { } out_)
        {
            if (out_.PhaseAt(State.Tick) == ExpeditionPhase.InZone && out_.PendingResult is null)
            {
                var run = _sim.OpenExpeditionScavenge();
                var before = State.Survivors.ToDictionary(s => s.Id, s => s.Name);
                if (_stuck) return;
                var (result, stats) = out_.MissionId is not null
                    ? (StoryBot.Play(run, ColonySim.ExpeditionPlayerId, _options.StoryPrompt, Trace).Result, new RunStats(run.State.Round, 0, 0, 0, 0, 0, 0, 0, 0, 0))
                    : PartyBot.Play(run, ColonySim.ExpeditionPlayerId, Trace);
                if (out_.MissionId is { } mid) StoryRuns.Add((Day, mid, result.Mission?.Outcome.ToString() ?? "?", result.Returned.Count, result.Deaths.Count, run.State.Round));
                if (!result.Complete)
                {
                    // The bot could not finish the run; leave the party out rather than replaying it every pass.
                    _stuck = true;
                    Problems.Add($"day {Day:0.0}: run in {out_.ZoneId} stalled after {stats.Rounds} rounds; no more expeditions");
                    return;
                }
                var zoneId = out_.ZoneId;
                var names = out_.Members.Select(m => before[m.SurvivorId]).ToList();
                var injuries = result.Returned.Sum(r => BodyDiff.Describe(State.FindSurvivor(r.Survivor.Id)!.Body, r.Survivor.Body).Count);
                var applied = _sim.Execute(new ApplyExtractionResult(result));
                var outcome = new ExpeditionOutcome(Day, zoneId, names, result.Returned.Count, result.Deaths.Count,
                    result.Returned.Count(r => r.Evacuated), result.Recruits.Count, injuries,
                    result.Loot.Sum(kv => kv.Value), stats, applied.Ok ? null : applied.Message);
                Expeditions.Add(outcome);
                OnReturn(outcome);
                _lastReturnTick = State.Tick + out_.TravelTicks;
            }
            return;
        }

        ScoutAhead();
        if (StoryLaunch()) return;
        if (Day < _options.FirstExpeditionDay || State.Tick < _lastReturnTick + _options.ExpeditionCooldownDays * TicksPerDay) return;
        var adults = State.Survivors.Where(s => !s.IsChild && !s.Away && s.PatientAtBuildingId is null && s.Pregnancy is null
                                                && !s.IsInjured && s.Health >= 80).ToList();
        var home = State.Survivors.Count(s => !s.IsChild && !s.Away) - 1;
        var size = Math.Min(_sim.MaxPartySize, Math.Min(adults.Count, home));
        // Never alone: a lone survivor who goes down has nobody to patch them up.
        if (size < Math.Min(2, _sim.MaxPartySize)) return;

        // Easiest known zone up to the current threat cap; prefer close and fresh ones.
        var zone = _sim.WorldZones().Where(z => z.Intel != ZoneIntel.Unknown && z.Zone.Threat <= _threatCap)
            .OrderBy(z => z.Zone.Threat).ThenBy(z => z.Depletion > 0.5 ? 1 : 0).ThenBy(z => z.Zone.TravelDays).ThenBy(z => z.Zone.Id)
            .FirstOrDefault();
        if (zone is null) return;
        // A party that would arrive in the dark waits outside for dawn (it costs rations, see PlanArrival).
        DayPhase? waitFor = _sim.PlanArrival(zone.Zone.Id).Phase == DayPhase.Night ? DayPhase.Dawn : null;
        var rations = (WorldMap.RationsPerMember(Data, zone.Zone) + _sim.PlanArrival(zone.Zone.Id, waitFor).WaitRationsPerMember) * size;
        var needPerDay = State.Survivors.Count(s => !s.Away) * Data.Rules.Food.AdultPerTick * TicksPerDay;
        if (State.Stockpile.Get("food") < rations + needPerDay * (3 + 2 * zone.Zone.TravelDays)) return;

        var party = adults.OrderByDescending(s => s.Get(AttributeKind.Strength) + s.Get(AttributeKind.Agility) + s.Get(AttributeKind.Endurance))
            .Take(size).ToList();
        var members = Gear(party);
        var launched = _sim.Execute(new LaunchExpedition(zone.Zone.Id, members, waitFor));
        if (!launched.Ok) Problems.Add($"day {Day:0.0}: launch {zone.Zone.Id}: {launched.Message}");
    }

    // ---- story --------------------------------------------------------------------------------------------------

    /// <summary>Story runs: day, mission, outcome, back, dead, rounds.</summary>
    public List<(double Day, string Mission, string Outcome, int Back, int Dead, int Rounds)> StoryRuns { get; } = [];

    /// <summary>
    /// Story mode: take every waiting call and play it through (first choice), put up the Radio Mast once the Workshop
    /// stands, and a Watchtower once the Town Hall is level 2.
    /// </summary>
    private void Story()
    {
        if (State.Campaign is null || _sim.Story is null) return;
        foreach (var call in Campaign.Waiting(_sim).ToList())
        {
            _sim.Execute(new TakeCall(call.Id));
            for (var i = 0; i < 100 && Dialogue.View(_sim) is { } view; i++)
                _sim.Execute(view.Choices.Count > 0 ? new ChooseDialogue(view.NodeId, view.Choices[0].Index) : new AdvanceDialogue());
        }
        foreach (var (id, after) in new[] { ("radio_mast", (Func<bool>)(() => Find("workshop") is not null)), ("watchtower", () => _sim.TownHallLevel >= 2) })
        {
            if (Find(id) is not null || !after()) continue;
            var cost = Data.Buildings[id].Level(1).Cost;
            if (State.Stockpile.CanAfford(cost)) _sim.Execute(new Construct(id));
        }
    }

    /// <summary>Sends the strongest healthy adults on the first available story mission (it waits for its own party size).</summary>
    private bool StoryLaunch()
    {
        if (State.Campaign is null || _sim.Story is null || State.Expedition is not null) return false;
        var mission = Campaign.Visible(_sim).Where(m => m.Def.Type == MissionType.Field && m.Progress.Status == MissionStatus.Available)
            .Select(m => m.Def).FirstOrDefault();
        if (mission is null) return false;
        var adults = State.Survivors.Where(s => !s.IsChild && !s.Away && s.PatientAtBuildingId is null && s.Pregnancy is null && s.Health >= 70).ToList();
        var size = Math.Min(Math.Max(_sim.MaxPartySize, mission.MaxParty ?? 0), adults.Count);
        if (size < 2) return false;
        var zone = _sim.Scavenge!.Zones[mission.Zone!];
        if (State.Stockpile.Get("food") < WorldMap.RationsPerMember(Data, zone) * size + 5) return false;
        var party = adults.OrderByDescending(s => s.Get(AttributeKind.Strength) + s.Get(AttributeKind.Agility) + s.Get(AttributeKind.Endurance)).Take(size).ToList();
        var launched = _sim.Execute(new LaunchStoryMission(mission.Id, Gear(party)));
        if (!launched.Ok) Problems.Add($"day {Day:0.0}: launch {mission.Id}: {launched.Message}");
        return launched.Ok;
    }

    /// <summary>A scout drone in the stores flies over the nearest rumored zone in reach (flown by the best pilot at home).</summary>
    private void ScoutAhead()
    {
        var scavenge = _sim.Scavenge!;
        if (WorldMap.DroneItem(State, scavenge) is null) return;
        var target = _sim.WorldZones().Where(z => z.Intel == ZoneIntel.Rumored && z.Zone.TravelDays <= scavenge.Rules.World.DroneScout.MaxTravelDays
                                                  && State.DroneFlights.All(f => f.ZoneId != z.Zone.Id))
            .OrderBy(z => z.Zone.Threat).ThenBy(z => z.Zone.TravelDays).ThenBy(z => z.Zone.Id).FirstOrDefault();
        if (target is null) return;
        var pilot = State.Survivors.Where(s => !s.IsChild && !s.Away).MaxBy(s => s.Get(Skill.DronePiloting));
        var sent = _sim.Execute(new SendScoutDrone(target.Zone.Id, pilot?.Id));
        if (!sent.Ok) Problems.Add($"day {Day:0.0}: scout drone: {sent.Message}");
    }

    /// <summary>Hand out the best weapon, body armor and clothing, a backpack, and medical items from the inventory.</summary>
    private List<ExpeditionMember> Gear(List<Survivor> party)
    {
        var items = _sim.Scavenge!.Items;
        var stock = new Dictionary<string, int>(State.Inventory.Counts);
        string? Take(Func<ItemDef, bool> match, Func<ItemDef, double> score)
        {
            var id = stock.Where(kv => kv.Value > 0 && match(items[kv.Key])).OrderByDescending(kv => score(items[kv.Key])).ThenBy(kv => kv.Key)
                .Select(kv => kv.Key).FirstOrDefault();
            if (id is not null) stock[id]--;
            return id;
        }
        double Damage(ItemDef d) => d.Actions.Select(a => (a.MinDamage + a.MaxDamage) / 2 * a.Accuracy).DefaultIfEmpty(0).Max();

        var members = new List<ExpeditionMember>();
        foreach (var s in party)
        {
            var equipment = new Dictionary<EquipSlot, string>();
            if (Take(d => d.Slot == EquipSlot.Weapon, Damage) is { } w) equipment[EquipSlot.Weapon] = w;
            if (Take(d => d.Slot == EquipSlot.Body && d.Kind == ItemKind.Armor, d => d.Armor) is { } a) equipment[EquipSlot.Body] = a;
            else if (Take(d => d.Slot == EquipSlot.Body, d => d.Armor) is { } c) equipment[EquipSlot.Body] = c;
            if (Take(d => d.Id == "backpack", _ => 1) is { } bag) equipment[EquipSlot.Tool] = bag;
            members.Add(new ExpeditionMember(s.Id, equipment, []));
        }
        // Tech: a hacking kit for the best hacker, a scout drone for the best pilot (pack: the Tool slot holds the backpack).
        if (Take(d => d.HackTool is not null, d => d.HackTool!.ChanceBonus) is { } kit)
            members[party.IndexOf(party.MaxBy(s => s.Get(Skill.Hacking) + s.Get(AttributeKind.Intellect) / 10)!)].Pack!.Add(kit);
        if (Take(d => d.Drone is not null, _ => 1) is { } drone)
            members[party.IndexOf(party.MaxBy(s => s.Get(Skill.DronePiloting) + s.Get(AttributeKind.Perception) / 10)!)].Pack!.Add(drone);
        // Medical: one med-kit and bandages spread over the party.
        for (var i = 0; ; i++)
        {
            var id = Take(d => d.DefinedId == "med_kit", _ => 1) ?? Take(d => d.DefinedId == "bandage", _ => 1);
            if (id is null || i >= party.Count * 2) break;
            members[i % members.Count].Pack!.Add(id);
        }
        return members;
    }

    /// <summary>The bot climbs threat only after two clean runs (nobody hurt or dead) at the current cap.</summary>
    private void OnReturn(ExpeditionOutcome o)
    {
        if (o.Dead == 0 && o.NewInjuries == 0) _cleanRunsAtThreat++;
        else _cleanRunsAtThreat = 0;
        if (_cleanRunsAtThreat >= 2 && _threatCap < _options.MaxThreat)
        {
            _threatCap++;
            _cleanRunsAtThreat = 0;
        }
    }
}

public sealed record ExpeditionOutcome(
    double Day, string Zone, List<string> Party, int Returned, int Dead, int Evacuated, int Recruits, int NewInjuries,
    int ItemsBack, RunStats Stats, string? Error);
