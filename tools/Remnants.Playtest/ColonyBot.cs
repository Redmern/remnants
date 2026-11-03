using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Schematics;
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

    /// <summary>Every other run goes to the hardest zone the threat cap allows (off = always the easiest).</summary>
    public bool Stretch { get; init; } = true;

    /// <summary>Climb threat after two runs without a death (default: two runs without a death or a new injury).</summary>
    public bool Bold { get; init; }
    /// <summary>Craft and fit gear mods (off: <c>--no-mods</c>, to measure what they change).</summary>
    public bool Mods { get; init; } = true;
    /// <summary>Story mode: which option the party bot picks on a field prompt (0 = the first, e.g. talk to the Tally).</summary>
    public int StoryPrompt { get; init; }
    /// <summary>Story mode: which field missions to go on: "all", "main" (no side jobs), or "none" (calls and colony tasks only).</summary>
    public string StoryMissions { get; init; } = "all";
    /// <summary>Build vehicles at the Garage and drive them out (else every party walks).</summary>
    public bool Vehicles { get; init; } = true;

    /// <summary>Schematics the colony starts with (as if found or traded early), to sweep vehicle tiers.</summary>
    public IReadOnlyList<string> Schematics { get; init; } = [];

    /// <summary>Fight every raid it can on the tactical map (StoryBot plays the defenders) instead of auto-resolving.</summary>
    public bool FightRaids { get; init; }
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
        ("farm", 2), ("woodcutter", 2), ("quarry", 2), ("kitchen", 1), ("tailor", 1), ("town_hall", 2), ("radio_mast", 1),
        ("house", 1), ("armory", 1), ("scrapyard", 2), ("workshop", 2), ("farm", 3), ("electronics_lab", 1),
        ("hunting_lodge", 1), ("smokehouse", 1), ("kitchen", 2), ("garage", 1),
        ("tailor", 2), ("med_bay", 2), ("fuel_still", 1), ("garage", 2), ("armory", 2), ("woodcutter", 3), ("quarry", 3), ("town_hall", 3), ("garage", 3), ("house", 2),
        ("electronics_lab", 2),
        // Power is a mid-game boost: a generator once the Lab is up and the town is grown, solar and a bank when their schematics turn up.
        ("fuel_generator", 1), ("solar_array", 1), ("battery_bank", 1), ("electronics_lab", 3),
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
        foreach (var id in _options.Schematics) _sim.State.Schematics.Known.Add(id);
        Defense = new DefenseBot(sim, this, _options.FightRaids);
    }

    /// <summary>Defenses, guards and raid prep (does nothing without raid data).</summary>
    public DefenseBot Defense { get; }

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
        Repair();
        Story();
        Defense.Act();
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
        // A threat-2 story mission waits on weapons: the Armory jumps the queue (a player gears up before the fight).
        var order = GearingUp && Find("armory") is null && _sim.TownHallLevel >= Data.Buildings["armory"].MinTownHallLevel
            ? BuildOrder.Prepend(("armory", 1)) : BuildOrder;
        foreach (var (id, level) in order)
        {
            // A site counts as done (its builders are on it); a further level of that type waits for it to finish.
            var have = State.Buildings.Where(b => b.DefId == id).Select(b => b.Level)
                .Concat(State.Construction.Sites.Where(s => s.DefId == id).Select(s => s.Level)).DefaultIfEmpty(0).Max();
            if (have >= level) continue;
            if (State.Construction.Sites.Any(s => s.DefId == id)) continue;
            var def = Data.Buildings[id];
            if (level == 1 && def.MinTownHallLevel > _sim.TownHallLevel) continue;
            if (level == 1 && !Remnants.Core.Schematics.Schematics.LevelUnlocked(_sim, id, 1)) continue;
            if (level > 1 && level > _sim.MaxUpgradeLevel(def)) continue;
            // Vehicle buildings: only with vehicles on, and a level whose schematic is still missing waits.
            if (id is "garage" or "fuel_still" && !_options.Vehicles) continue;
            if (!VehicleSchematics.LevelUnlocked(_sim, id, level)) continue;
            if (!Remnants.Core.Schematics.Schematics.LevelUnlocked(_sim, id, level)) continue;
            return (id, level);
        }
        return null;
    }

    public Dictionary<string, double> GoalCost() =>
        NextGoal() is { } g ? new(Data.Buildings[g.Building].Level(g.Level).Cost) : new();

    private void Build()
    {
        // Past the first month, food running out with every farm slot taken: a second (or third) Farm is cheap and quick.
        if (Day >= 30 && _sim.FoodDays < 4 && !State.Construction.Sites.Any(s => s.DefId == "farm")
            && State.Buildings.Where(b => b.DefId == "farm").ToList() is { Count: > 0 and < 3 } farms
            && farms.All(f => _sim.FreeSlots(f) == 0) && State.Stockpile.CanAfford(Data.Buildings["farm"].Level(1).Cost))
        {
            var extra = _sim.Execute(new Construct("farm"));
            if (!extra.Ok) Problems.Add($"day {Day:0.0}: extra farm: {extra.Message}");
        }
        if (NextGoal() is not { } goal) return;
        // A sensible player keeps a couple of sites going at most, so builders are not spread thin.
        if (State.Construction.Sites.Count >= MaxSites) return;
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

    /// <summary>Construction sites the bot keeps open at once.</summary>
    private const int MaxSites = 2;

    /// <summary>One builder per this many hands at home (rounded up), across all sites.</summary>
    private static readonly int BuilderShare = int.TryParse(Environment.GetEnvironmentVariable("BOT_BUILDER_SHARE"), out var n) && n > 0 ? n : 4;

    /// <summary>Buildings struck by lightning are repaired as soon as the repair is affordable, before any new build.</summary>
    private void Repair()
    {
        foreach (var b in State.Buildings.Where(b => b.Damaged).OrderBy(b => b.Id).ToList())
        {
            if (!State.Stockpile.CanAfford(Data.Weather.Repair.Cost)) return;
            var r = _sim.Execute(new RepairBuilding(b.Id));
            if (!r.Ok) Problems.Add($"day {Day:0.0}: repair {b.DefId}: {r.Message}");
        }
    }

    // ---- crafting -------------------------------------------------------------------------------------------

    private Building? Find(string defId) => State.Buildings.FirstOrDefault(b => b.DefId == defId);

    private void Craft()
    {
        if (Find("workshop") is { } workshop && workshop.Queue.Count == 0)
        {
            var weapons = Count("pipe") + Count("spear") + Count("machete");
            if (State.Stockpile.Get("medicine") < 3 && _sim.FoodStock > 25) Queue(workshop, "herbal_remedy");
            else if (weapons < 3 && State.Stockpile.Get("scrap") >= 6) Queue(workshop, "spear");
            else if (ToolWanted(workshop) is { } tool) Queue(workshop, tool);
            else if (workshop.Level >= 2 && Count("med_kit") < 2 && State.Stockpile.Get("cloth") >= 2 && State.Stockpile.Get("medicine") >= 3)
                Queue(workshop, "med_kit");
            // Gear mods from the default set once the colony is armed and has scrap to spare: an edge for each blade, sights for guns.
            else if (_options.Mods && weapons >= 3 && State.Stockpile.Get("scrap") >= 25 && ModsOnHand() < 2)
                Queue(workshop, Count("pistol") + Count("rifle") > Count("mod_iron_sight") ? "mod_iron_sight" : "mod_sharpened_edge");
        }
        if (Find("tailor") is { } tailor && tailor.Queue.Count == 0)
        {
            if (State.Stockpile.Get("cloth") < 4 && _sim.FoodStock > 50) Queue(tailor, "fiber_cloth");
            else if (Count("bandage") < 4 && State.Stockpile.Get("cloth") >= 1) Queue(tailor, "bandage");
            else if (Count("padded_vest") < 2 && State.Stockpile.Get("cloth") >= 5) Queue(tailor, "padded_vest");
            // A couple of dust masks keep the woodcutter and quarry going (and healthy) in a dust storm.
            else if (Weather.Enabled(Data) && Count("dust_mask") < 2 && State.Stockpile.Get("cloth") >= 4 && State.Stockpile.Get("scrap") >= 3)
                Queue(tailor, "dust_mask");
            else if (_options.Mods && State.Stockpile.Get("cloth") >= 12 && ModsOnHand() < 2) Queue(tailor, Count("mod_armor_padding") <= Count("mod_armor_webbing") ? "mod_armor_padding" : "mod_armor_webbing");
        }
        Cook();
        if (Find("kitchen") is { } kitchen && kitchen.Queue.Count == 0 && State.Stockpile.Get("alcohol") < 2 && Find("med_bay") is not null && _sim.FoodDays > 6)
            Queue(kitchen, "spirits");
        if (Find("med_bay") is { } bay && bay.Queue.Count == 0) CraftMedicine(bay);
        if (Find("armory") is { } armory && armory.Queue.Count == 0 && Count("machete") < (GearingUp ? 4 : 2) && State.Stockpile.Get("scrap") >= 10)
            Queue(armory, "machete");
        if (Find("electronics_lab") is { } bench && bench.Queue.Count == 0) Bench(bench);
        HandleSchematics();
        if (_options.Vehicles) Garage();
    }

    /// <summary>
    /// Schematics: decrypt chips at the Lab when it can; research the cheapest heard-of schematic at the Town Hall with a
    /// spare adult; buy from the trader with Research Data, else scrap, keeping a reserve; sell chips it cannot decrypt.
    /// </summary>
    private void HandleSchematics()
    {
        var catalog = _sim.Data.Schematics;
        if (Find("electronics_lab") is { } lab)
            foreach (var id in State.Inventory.Counts.Keys.Where(id => catalog.Find(id) is not null).ToList())
            {
                var recipe = _sim.Data.Recipes.GetValueOrDefault(Remnants.Core.Schematics.Schematics.DecryptRecipeId(id));
                if (recipe is not null && lab.Queue.All(o => o.RecipeId != recipe.Id) && _sim.CanQueue(lab, recipe).Ok)
                    _sim.Execute(new QueueRecipe(lab.Id, recipe.Id, 1));
            }
        if (State.Schematics.Research is null)
        {
            var target = State.Schematics.HeardOf.Keys.Select(catalog.Find).OfType<SchematicDef>()
                .Where(s => Research.Check(_sim, s.Id).Ok).OrderBy(s => s.Tier).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
            var researcher = State.Survivors.Where(s => !s.IsChild && !s.IsInfant && !s.Away && s.PatientAtBuildingId is null
                    && _sim.ClassOf(s) is null && s.AssignedBuildingId is null)
                .OrderByDescending(s => s.Get(AttributeKind.Intellect)).FirstOrDefault();
            if (target is not null && researcher is not null) _sim.Execute(new StartResearch(target.Id, researcher.Id));
        }
        if (State.Radio.TraderHere)
            foreach (var id in State.Radio.Stock.ToList())
            {
                var prices = RadioMast.Prices(_sim, catalog.All[id]);
                for (var o = prices.Count - 1; o >= 0; o--)
                {
                    var cost = prices[o].Cost;
                    var keep = cost.Keys.Any(k => k == "scrap") ? 40 : cost.Keys.Any(k => k == "food") ? 60 : 0;
                    double Have(string k) => k == Pantry.FoodKey ? _sim.FoodStock : State.Stockpile.Get(k);
                    if (cost.All(kv => Have(kv.Key) >= kv.Value + (kv.Key is "scrap" or "food" ? keep : 0))
                        && _sim.Execute(new BuyFromTrader(id, o)).Ok) break;
                }
            }
        if (State.Radio.Contract is { Accepted: false } contract && _sim.ZoneInfo(contract.ZoneId).Zone.Threat <= 2)
            _sim.Execute(new AcceptContract(contract.Id));
        foreach (var call in State.Radio.Calls.ToList()) _sim.Execute(new AnswerCall(call.Id));
    }

    // ---- vehicles -------------------------------------------------------------------------------------------

    /// <summary>
    /// The Garage: repair what is broken (spare parts first), then build the next vehicle tier the colony can (parts
    /// first, one batch at a time), then fit headlights; the still makes fuel while it runs low and food is plentiful.
    /// </summary>
    private void Garage()
    {
        if (Find("fuel_still") is { } still && still.Queue.Count == 0 && State.Stockpile.Get("fuel") < 40 && _sim.FoodStock > 80)
            Queue(still, "biofuel");
        if (Find("garage") is not { } garage) return;
        foreach (var v in State.Vehicles.Where(v => !v.Away && v.Condition < _sim.VehicleStats(v).MaxCondition * 0.6))
            if (State.Inventory.Has("spare_parts")) _sim.Execute(new RepairVehicle(v.Id));
            else if (garage.Queue.Count == 0 && State.Stockpile.Get("scrap") >= 5) Queue(garage, "spare_parts");
        // Upgrades, in the bot's order of preference, while the stores can spare it (it never takes anything off).
        foreach (var v in State.Vehicles.Where(v => !v.Away))
            foreach (var id in UpgradeOrder)
                if (Data.VehicleUpgrades.TryGetValue(id, out var up) && VehicleRules.CanFit(_sim, v, up) is null
                    && up.Cost.All(kv => State.Stockpile.Get(kv.Key) >= kv.Value + GoalCost().GetValueOrDefault(kv.Key) + 10))
                    _sim.Execute(new FitUpgrade(v.Id, id));
        // Vehicles are a luxury: the Garage only builds while the stores have scrap to spare for the build order.
        if (garage.Queue.Count > 0 || State.Stockpile.Get("scrap") < GoalCost().GetValueOrDefault("scrap") + 30) return;

        var best = State.Vehicles.Select(v => Data.VehicleTiers[v.TierId].Tier).DefaultIfEmpty(-1).Max();
        var next = Data.RecipesFor("garage").Where(r => r.Vehicle is { } t && Data.VehicleTiers[t].Tier > best && _sim.CanQueue(garage, r).Ok)
            .OrderByDescending(r => Data.VehicleTiers[r.Vehicle!].Tier).FirstOrDefault();
        if (next is null) return;
        // Parts the build is short of come first, one batch at a time.
        foreach (var (part, count) in next.ItemInputs)
            if (Count(part) < count)
            {
                if (Data.Recipes.TryGetValue(part, out var r) && _sim.CanQueue(garage, r).Ok && State.Stockpile.CanAfford(r.Inputs)) Queue(garage, part);
                return;
            }
        if (State.Stockpile.CanAfford(next.Inputs)) Queue(garage, next.Id);
    }

    private static readonly string[] UpgradeOrder =
    [
        "headlights", "cargo_rack", "fuel_cans", "muffler", "offroad_tyres", "tuned_engine", "medkit_locker", "winch", "armor_plates", "cage",
        "trailer", "roof_turret",
    ];

    /// <summary>
    /// The vehicle a party of <paramref name="size"/> takes to a zone: the fastest working one whose trip is not blocked and
    /// whose fuel the colony has, with at least two seats (seats then cap the party). Null = on foot.
    /// </summary>
    private Vehicle? PickVehicle(string zoneId)
    {
        if (!_options.Vehicles) return null;
        return State.Vehicles.Where(v => !v.Away && !VehicleRules.Broken(Data, v) && (_sim.VehicleStats(v).Seats ?? 99) >= 2)
            .Select(v => (Vehicle: v, Plan: _sim.PlanTravel(zoneId, v.Id)))
            .Where(x => x.Plan.Blocked is null && State.Stockpile.Get(Data.Vehicles.FuelResource) >= x.Plan.FuelRoundTrip)
            .OrderBy(x => x.Plan.Days).ThenByDescending(x => _sim.VehicleStats(x.Vehicle).CargoKg).ThenBy(x => x.Vehicle.Id)
            .Select(x => x.Vehicle).FirstOrDefault();
    }

    /// <summary>
    /// The Med Bay's dispensary: antibiotics while anyone is infected, then a small stock of the basics (antiseptic,
    /// splints, clean dressings), and blood bags for the party once there is a donor to spare. Only what it may make.
    /// </summary>
    private void CraftMedicine(Building bay)
    {
        bool Can(string recipe) => _sim.CanQueue(bay, Data.Recipes[recipe]).Ok && State.Stockpile.CanAfford(Data.Recipes[recipe].Inputs);
        var infected = State.Survivors.Any(s => s.Body.Conditions.Any(c => Data.Medicine?.Condition(c.Id)?.Kind == ConditionKind.Infection));
        string? pick = null;
        if (infected && Count("antibiotics") < 1 && Can("antibiotics")) pick = "antibiotics";
        else if (Count("antiseptic") < 3 && Can("antiseptic")) pick = "antiseptic";
        else if (Count("splint") < 2 && Can("splint")) pick = "splint";
        else if (Count("clean_dressing") < 3 && Can("clean_dressing")) pick = "clean_dressing";
        else if (Count("antibiotics") < 2 && Can("antibiotics")) pick = "antibiotics";
        else if (Count("blood_bag") < 1 && Can("blood_bag") && Medicine.Donor(State, Data, Data.Recipes["blood_bag"].DonorHealth) is not null) pick = "blood_bag";
        if (pick is not null) Queue(bay, pick);
    }

    // ---- food ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Cooks and preserves sensibly: keeps about two days of cooked food on hand (not more, it spoils), water for the pot,
    /// stew at a Kitchen L2 when the ingredients are there, otherwise roast, boiled potatoes or flatbread from what is in
    /// stock; at the Campfire only until the Kitchen is up. The Smokehouse smokes meat and dries produce that would spoil
    /// within three days. Fields stay on potatoes, the lodge on game.
    /// </summary>
    private void Cook()
    {
        if (Data.Food is not { } food) return;
        var pot = Find("kitchen") ?? Find(food.Rules.CampfireBuilding);
        var perDay = _sim.FoodPerDay;
        var cooked = Pantry.Entries(_sim).Where(e => food.Items[e.ItemId].Meal is { Hot: true } || e.ItemId == "flatbread").Sum(e => e.FoodValue);
        if (pot is not null && pot.Queue.Count == 0 && _sim.FoodDays > 3 && cooked < 2 * perDay)
        {
            var at = pot.DefId == "kitchen" ? "" : "campfire_";
            bool Has(string key, int n) => Pantry.Available(State, Data, key) >= n;
            if (!Has("water", 2)) Queue(pot, at == "" ? "purify_water" : "campfire_water");
            else if (pot.DefId == "kitchen" && pot.Level >= 2 && Has("tag:veg", 2) && Has("tag:protein", 1)) Queue(pot, "stew");
            else if (Has("tag:fresh_meat", 2)) Queue(pot, at + "roast");
            else if (Has("potato", 6)) Queue(pot, at == "" ? "boiled_potatoes" : "campfire_potatoes");
            else if (Has("corn", 3)) Queue(pot, at + "flatbread");
        }
        if (Find("smokehouse") is { } smoke && smoke.Queue.Count == 0)
        {
            var soon = State.Food.Lots.Where(kv => food.Matches(kv.Key, "tag:fresh_veg"))
                .Sum(kv => kv.Value.Where(l => l.TicksLeft is { } t && t > 0 && t < 3 * TicksPerDay).Sum(l => l.Count));
            if (Pantry.Available(State, Data, "tag:fresh_meat") >= 6) Queue(smoke, "smoke_meat");
            else if (soon >= 6 && _sim.FoodDays > 6) Queue(smoke, "dry_food");
        }
    }

    /// <summary>
    /// The Electronics Lab: strip found junk first (never Linked cores: the limbs want them), keep a few components in
    /// stock, then the field devices a sensible player takes along (two radios, decoys, EMPs, a drone variant), else
    /// circuits from scrap as before. One order at a time, so the old circuits work never starves.
    /// </summary>
    private void Bench(Building lab)
    {
        if (Data.Electronics is null)
        {
            if (State.Stockpile.Get("scrap") >= 20) Queue(lab, "salvage_circuits");
            return;
        }
        bool Open(string recipe) => Data.Recipes.TryGetValue(recipe, out var r) && _sim.CanQueue(lab, r).Ok
                                    && (!r.NeedsPower || _sim.IsPowered(lab) || State.Power.Capacity > 0)
                                    && State.Stockpile.CanAfford(r.Inputs) && r.ItemInputs.All(kv => Count(kv.Key) >= kv.Value);
        var junk = State.Inventory.Counts.Keys.Where(id => id != "linked_core" && Data.Recipes.ContainsKey(DisassemblyDef.RecipePrefix + id)
                                                          && _sim.Scavenge!.Items[id].Kind == ItemKind.Material)
            .OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        if (junk is not null && Open(DisassemblyDef.RecipePrefix + junk)) { Queue(lab, DisassemblyDef.RecipePrefix + junk); return; }
        var wants = new (string Recipe, string Item, int Keep)[]
        {
            ("wiring", "wiring", 4), ("circuit_board", "circuit_board", 2), ("antenna", "antenna", 1),
            ("handheld_radio", "handheld_radio", 2), ("noise_decoy", "noise_decoy", 2), ("holo_decoy", "holo_decoy", 1),
            ("emp_grenade", "emp_grenade", 2), ("medic_drone", "medic_drone", 1), ("cargo_drone", "cargo_drone", 1),
            ("squad_radio", "squad_radio", 1), ("sensor_module", "sensor_module", 1),
        };
        foreach (var (recipe, item, keep) in wants)
            if (Count(item) < keep && Open(recipe))
            {
                Queue(lab, recipe);
                return;
            }
        if (State.Stockpile.Get("scrap") >= 20) Queue(lab, "salvage_circuits");
    }

    /// <summary>Spare mods in the stores (not fitted).</summary>
    private int ModsOnHand() => State.Inventory.Counts.Where(kv => _sim.Scavenge!.Items.TryGetValue(kv.Key, out var d) && d.Kind == ItemKind.Mod).Sum(kv => kv.Value);

    /// <summary>
    /// At a bench, fit the mods and kits in the stores to the gear the next party will take (<see cref="GearModder"/>):
    /// kits first, then per piece the best build from the spare mods, freed mods back to the stores.
    /// </summary>
    private void GearUp(bool night)
    {
        var data = _sim.Scavenge!;
        if (!_options.Mods || data.GearMods is not { } mods || !State.Buildings.Any(b => mods.Def.Rules.Benches.Contains(b.DefId))) return;
        var context = new ModContext(Night: night);
        var moddable = State.Inventory.Counts.Keys.Where(id => mods.Moddable(data.Items[id].DefinedId) is not null && data.Items[id].Slot is not null)
            .OrderByDescending(id => GearModder.Score(data, id, context)).ToList();
        foreach (var gear in moddable)
        {
            var id = gear;
            if (State.Inventory.Counts.Keys.FirstOrDefault(k => mods.KitFor(data.Items[id].DefinedId, data.Items[id].Mk) == k) is { } kit
                && _sim.Execute(new UpgradeGear(id, kit)) is { Ok: true })
                id = GearModsData.Parse(id) is var p ? mods.Canonical(p.BaseId, p.Mk + 1, p.Mods) : id;
            var spare = State.Inventory.Counts.Where(kv => data.Items[kv.Key].Kind == ItemKind.Mod).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value));
            var (best, used, freed) = GearModder.Best(data, id, spare, context);
            if (best == id) continue;
            foreach (var m in freed)
                if (_sim.Execute(new RemoveMod(id, mods.SlotOf(m)!)) is { Ok: true }) id = mods.Canonical(GearModsData.Parse(id).BaseId, GearModsData.Parse(id).Mk, GearModsData.Parse(id).Mods.Where(x => x != m));
            foreach (var m in used)
            {
                var r = _sim.Execute(new InstallMod(id, m));
                if (!r.Ok) { Problems.Add($"day {Day:0.0}: fit {m} to {id}: {r.Message}"); break; }
                id = GearBench.Install(data, id, m) is { Ok: true } o ? o.ItemId! : id;
            }
        }
    }

    /// <summary>
    /// A tool for a gatherer who works bare-handed, or a better one for a worker on a basic tool: the best the Workshop's
    /// level, the colony's Crafting and the stockpile allow (inputs plus 5 scrap to spare), else the basic one.
    /// </summary>
    private string? ToolWanted(Building workshop)
    {
        var crafting = State.Survivors.Where(s => !s.IsInfant).Select(s => s.Get(Skill.Crafting)).DefaultIfEmpty(0).Max();
        RecipeDef? Makeable(string item) =>
            Data.Recipes.Values.FirstOrDefault(r => r.Item == item && r.Building == "workshop" && workshop.Level >= r.MinLevel && crafting >= r.MinSkill
                && r.Inputs.All(kv => State.Stockpile.Get(kv.Key) >= kv.Value + (kv.Key == "scrap" ? 5 : 0)));
        foreach (var b in State.Buildings.Where(b => Data.Buildings[b.DefId].Produces is not null).OrderBy(b => b.Id))
        {
            var job = Data.Jobs[Data.Buildings[b.DefId].Job!];
            if (job.Tools.Count == 0) continue;
            var crew = State.Buildings.Where(o => Data.Buildings[o.DefId].Job == job.Id).Sum(o => State.WorkersAt(o.Id).Count());
            var tools = job.Tools.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
            var (basic, best) = (tools[0], tools[^1]);
            if (tools.Sum(Count) < crew && (Makeable(best) ?? Makeable(basic)) is { } any) return any.Id;
            if (Count(best) < crew && Makeable(best) is { } better) return better.Id;
        }
        return null;
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
        var med = Data.Medicine;
        foreach (var s in State.Survivors.Where(s => !s.Away && s.PatientAtBuildingId is null
                     && (s.Body.Parts.Values.Any(p => p.Condition == PartCondition.Injured && p.Severity >= natural)
                         || med is not null && s.Body.Conditions.Any(c => med.Condition(c.Id)?.Kind is ConditionKind.Infection or ConditionKind.Sickness))).ToList())
            if (_sim.MedBays.Any(b => _sim.FreePatientSlots(b) > 0))
                _sim.Execute(new AdmitPatient(s.Id));
        // Nobody in a bed: dress the wounds of those who work through them, so they do not go bad.
        foreach (var s in State.Survivors.Where(s => !s.Away && s.PatientAtBuildingId is null && s.IsInjured).ToList())
            if (State.Inventory.Counts.Keys.FirstOrDefault(id => _sim.Scavenge!.Items[id].DefinedId is "antiseptic" or "clean_dressing") is { } dressing
                && Treatment.Helps(med, s.Body, _sim.Scavenge!.Items[dressing].Use!))
                _sim.Execute(new TreatSurvivor(s.Id, dressing));
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
        if (State.Stockpile.Get("scrap") < 40 || _sim.FoodStock < 30) return;
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
        var workers = State.Survivors.Where(s => !s.Away && !s.IsInfant && s.PatientAtBuildingId is null && !Research.IsResearching(State, s.Id) && !State.Defense.Guards.ContainsKey(s.Id)
                                                 && StudySystem.ClassOf(State, s.Id) is null).ToList();
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
        var days = _sim.FoodStock / Math.Max(need * TicksPerDay, 1e-6);
        var margin = days < 3 ? 1.6 : days < 8 ? 1.25 : days > 25 ? 0.9 : 1.1;
        var produced = 0.0;
        // Food value per worker: job output times what the harvest brings in (potatoes 1, beans less, meat 1.5 a piece).
        double Food(Survivor s, Building b) =>
            Output(s, b) * (Harvesting.Current(Data, b) is { } h ? Harvesting.FoodValuePerEffort(State, Data, b, h) : 0);
        foreach (var farm in State.Buildings.Where(b => Harvesting.Current(Data, b) is not null)
                     .OrderBy(b => b.DefId == "farm" ? 0 : 1).ThenByDescending(b => b.Level))
            while (produced < need * margin && free.Count > 0)
            {
                // Children farm first (it is one of their jobs); adults fill the rest.
                var job = Data.Jobs[Data.Buildings[farm.DefId].Job!];
                var best = free.OrderBy(s => s.IsChild ? 0 : 1).ThenByDescending(s => Food(s, farm)).FirstOrDefault(s => !s.IsChild || job.ChildrenAllowed);
                if (best is null || !Take(best, farm)) break;
                produced += Food(best, farm);
                free.Remove(best);
            }

        // Builders, the best at it first (children too once food is safe): one on every site before anyone else gets
        // hands, so a site never stands still and blocks the build order; about a quarter of the hands in all, so wood
        // and stone keep coming in.
        var builders = new Dictionary<int, int>(); // survivor -> site building id
        var budget = Math.Max(1, (workers.Count + BuilderShare - 1) / BuilderShare);
        void Builders(ConstructionSite site, int want)
        {
            while (builders.Count < budget && builders.Values.Count(id => id == site.BuildingId) < want)
            {
                var pick = free.Where(s => !s.IsChild || days > 5).OrderBy(s => s.IsChild ? 1 : 0)
                    .ThenByDescending(s => ConstructionSystem.WorkRate(_sim, s)).ThenBy(s => s.Id).FirstOrDefault();
                if (pick is null) return;
                builders[pick.Id] = site.BuildingId;
                free.Remove(pick);
            }
        }
        foreach (var site in ConstructionSystem.Ordered(State).Where(s => !s.Paused)) Builders(site, 1);

        // A medic while anyone is in a bed, crafters for queues (one each), then gatherers by shortage of the goal.
        foreach (var b in State.Buildings.Where(b => b.Queue.Count > 0 || State.PatientsAt(b.Id).Any()).OrderBy(b => b.Id))
        {
            var pick = free.OrderByDescending(s => Output(s, b)).FirstOrDefault(s => !s.IsChild || Data.Jobs[Data.Buildings[b.DefId].Job!].ChildrenAllowed);
            if (pick is not null && Take(pick, b)) free.Remove(pick);
        }

        // More builders: up to two a site (three on an upgrade once there are hands to spare).
        foreach (var site in ConstructionSystem.Ordered(State).Where(s => !s.Paused))
            Builders(site, Math.Min(_sim.MaxBuilders(site), site.IsUpgrade && free.Count > 3 ? 3 : 2));

        var goal = GoalCost();
        double Short(string res) => Math.Max(0, goal.GetValueOrDefault(res) - State.Stockpile.Get(res)) + 5;
        var gatherers = State.Buildings.Where(b => Data.Buildings[b.DefId] is { Produces: not null }).ToList();
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
        foreach (var s in workers.Where(s => !builders.ContainsKey(s.Id) && State.Construction.Sites.Any(c => c.Builders.Contains(s.Id))))
            _sim.Execute(new Unassign(s.Id));
        foreach (var s in moving.Where(s => plan.ContainsKey(s.Id)))
        {
            var r = _sim.Execute(new AssignJob(s.Id, plan[s.Id]));
            if (!r.Ok) Problems.Add($"day {Day:0.0}: assign {s.Name}: {r.Message}");
        }
        foreach (var (id, site) in builders)
        {
            if (State.Construction.Find(site)?.Builders.Contains(id) == true) continue;
            var r = _sim.Execute(new AssignBuilder(id, site));
            if (!r.Ok) Problems.Add($"day {Day:0.0}: build {State.FindSurvivor(id)?.Name}: {r.Message}");
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
                if (out_.MissionId is { } traced)
                    Trace?.Invoke($"== story {traced} day {Day:0.0}: " + string.Join("; ", run.State.Units.Where(u => u.PlayerId == ColonySim.ExpeditionPlayerId)
                        .Select(u => $"{u.Name} hp {u.Hp:0}/{u.MaxHp:0} {string.Join(",", u.Equipment.Values)} pack {u.Pack.Count}")));
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
                // The tier before the result is applied: a vehicle lost out there is gone from the colony after.
                var vehicleName = out_.VehicleId is { } vid ? State.Vehicles.Find(v => v.Id == vid)?.TierId : null;
                var applied = _sim.Execute(new ApplyExtractionResult(result));
                var outcome = new ExpeditionOutcome(Day, zoneId, names, result.Returned.Count, result.Deaths.Count,
                    result.Returned.Count(r => r.Evacuated), result.Recruits.Count, injuries,
                    result.Loot.Sum(kv => kv.Value), stats, applied.Ok ? null : applied.Message)
                {
                    Vehicle = vehicleName, TravelDays = out_.TravelTicks / (double)TicksPerDay, Fuel = out_.Fuel,
                    StashedItems = result.Vehicle?.Stash.Count ?? 0, VehicleLost = result.Vehicle is { Wrecked: true },
                };
                Expeditions.Add(outcome);
                OnReturn(outcome);
                _lastReturnTick = State.Expedition?.ReturnTick ?? State.Tick;
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

        // Easiest known zone up to the current threat cap, prefer close and fresh ones; every other run (once the cap has
        // climbed) the hardest zone the cap allows, so the bot sees the next region (and its schematics and trader).
        var stretch = _options.Stretch && Expeditions.Count % 2 == 1 && _threatCap > 1;
        var zone = _sim.WorldZones().Where(z => z.Intel != ZoneIntel.Unknown && z.Zone.Threat <= _threatCap)
            .OrderBy(z => stretch ? -z.Zone.Threat : z.Zone.Threat).ThenBy(z => z.Depletion > 0.5 ? 1 : 0).ThenBy(z => z.Zone.TravelDays).ThenBy(z => z.Zone.Id)
            .FirstOrDefault();
        if (zone is null) return;
        var vehicle = PickVehicle(zone.Zone.Id);
        if (vehicle is not null) size = Math.Min(size, _sim.MaxPartySizeWith(vehicle.Id));
        var travel = _sim.PlanTravel(zone.Zone.Id, vehicle?.Id);
        // A party that would arrive in the dark waits outside for dawn (it costs rations, see PlanArrival).
        DayPhase? waitFor = _sim.PlanArrival(zone.Zone.Id, null, vehicle?.Id).Phase == DayPhase.Night ? DayPhase.Dawn : null;
        var plan = _sim.PlanArrival(zone.Zone.Id, waitFor, vehicle?.Id);
        // A dust storm over the zone closes the route: wait for it to pass rather than pick another zone.
        if (plan.Blocked) return;
        var rations = (WorldMap.RationsPerMember(Data, travel.MovingTicks / (double)TicksPerDay) + plan.ExtraRationsPerMember) * size;
        var needPerDay = State.Survivors.Count(s => !s.Away) * Data.Rules.Food.AdultPerTick * TicksPerDay;
        if (_sim.FoodStock < rations + needPerDay * (3 + 2 * travel.Days)) return;

        var party = adults.OrderByDescending(s => s.Get(AttributeKind.Strength) + s.Get(AttributeKind.Agility) + s.Get(AttributeKind.Endurance))
            .Take(size).ToList();
        GearUp(night: waitFor is null && _sim.Phase == Remnants.Core.Colony.DayPhase.Night);
        var members = Gear(party);
        // Light, long-keeping field rations when there are any, else a good hot meal for the road.
        var prefer = new[] { "field_rations", "stew", "pie", "preserved_rations" }.FirstOrDefault(id => State.Inventory.Count(id) >= size);
        var launched = _sim.Execute(new LaunchExpedition(zone.Zone.Id, members, waitFor, new RationChoice(prefer), vehicle?.Id));
        if (!launched.Ok) Problems.Add($"day {Day:0.0}: launch {zone.Zone.Id}: {launched.Message}");
    }

    // ---- story --------------------------------------------------------------------------------------------------

    /// <summary>A main story mission is waiting for better weapons than the colony has (see <see cref="StoryLaunch"/>).</summary>
    public bool GearingUp { get; private set; }

    /// <summary>The day the bot first wanted to launch each mission (its gear wait counts from there).</summary>
    private readonly Dictionary<string, double> _waitingSince = new(StringComparer.Ordinal);

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

    /// <summary>
    /// Sends the strongest healthy adults (health 90+ and no open wound: a hurt arm is what lost most M2 runs) on the first
    /// story mission that is ready to go, as a player reading the briefing would: a side job only with hands to spare and
    /// nothing in its briefing to warn about, a main field mission never with fewer than the briefing asks for (threat 1
    /// and a defense at home: for 10 days), nor with starting-tier weapons into a stand-up fight (the Armory jumps the
    /// build order meanwhile; after 20 days the party goes as it is).
    /// </summary>
    private bool StoryLaunch()
    {
        if (State.Campaign is null || _sim.Story is null || State.Expedition is not null) return false;
        var gearing = false;
        try
        {
            foreach (var mission in Campaign.Visible(_sim).Where(m => m.Def.Type == MissionType.Field && m.Progress.Status == MissionStatus.Available)
                         .Select(m => m.Def).Where(m => _options.StoryMissions == "all" || _options.StoryMissions == "main" && m.Main).ToList())
            {
                var adults = State.Survivors.Where(s => !s.IsChild && !s.Away && s.PatientAtBuildingId is null && s.Pregnancy is null && s.Health >= 90
                                                        && !s.IsInjured).ToList();
                var size = Math.Min(Math.Max(_sim.MaxPartySize, mission.MaxParty ?? 0), adults.Count);
                if (size < 2) return false;
                // A side job (a contract) only with hands to spare: three adults stay home beyond the party. A stealth job the
                // bot cannot play (it walks in plain sight) it leaves alone (Marrow's Debt: BALANCE.md "Full-game pass 2").
                var home = State.Survivors.Count(s => !s.IsChild && !s.Away);
                if (!mission.Main && home - size < 3) continue;
                if (!mission.Main && mission.Field!.Objectives.Any(o => o.Kind == ObjectiveKind.Undetected)) continue;
                // A mission that needs someone treated waits for something to treat them with.
                if (mission.Field!.Objectives.Any(o => o.Kind == ObjectiveKind.Aid) && Count("bandage") + Count("med_kit") == 0) continue;
                var zone = _sim.Scavenge!.Zones[mission.Zone!];
                if (_sim.FoodStock < WorldMap.RationsPerMember(Data, zone) * size + 5) continue;
                var party = adults.OrderByDescending(s => s.Get(AttributeKind.Strength) + s.Get(AttributeKind.Agility) + s.Get(AttributeKind.Endurance)).Take(size).ToList();
                GearUp(night: false);
                var members = Gear(party);
                var warnings = MissionReadiness.Check(_sim, mission, members);
                var waited = Day - (_waitingSince.TryGetValue(mission.Id, out var since) ? since : _waitingSince[mission.Id] = Day);
                if (!mission.Main && warnings.Count > 0) continue;
                if (mission.Main && warnings.Any(w => w.Id == "gear") && waited < 20) { gearing = true; continue; }
                // Never short-handed into a main fight: two into M7 was most of its dead.
                if (mission.Main && warnings.Any(w => w.Id == "party") && (mission.Threat >= 2 && !mission.Field!.AtHome || waited < 10)) continue;
                var launched = _sim.Execute(new LaunchStoryMission(mission.Id, members));
                if (!launched.Ok) Problems.Add($"day {Day:0.0}: launch {mission.Id}: {launched.Message}");
                return launched.Ok;
            }
            return false;
        }
        finally { GearingUp = gearing; }
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
            if (Take(d => d.Slot == EquipSlot.Head, d => d.Armor) is { } head) equipment[EquipSlot.Head] = head;
            members.Add(new ExpeditionMember(s.Id, equipment, []));
        }
        // Tech: a hacking kit for the best hacker, a scout drone for the best pilot (pack: the Tool slot holds the backpack).
        if (Take(d => d.HackTool is not null, d => d.HackTool!.ChanceBonus) is { } kit)
            members[party.IndexOf(party.MaxBy(s => s.Get(Skill.Hacking) + s.Get(AttributeKind.Intellect) / 10)!)].Pack!.Add(kit);
        var pilot = party.MaxBy(s => s.Get(Skill.DronePiloting) + s.Get(AttributeKind.Perception) / 10)!;
        var pilotHooks = _sim.Scavenge!.HooksFor(pilot.Perks);
        // A medic drone first (it keeps people up), then the longest battery; never one the pilot lacks the perk for.
        if (Take(d => d.Drone is { } dr && (dr.RequiresHook is not { } hook || pilotHooks.GetValueOrDefault(hook) > 0),
                d => (d.Drone!.Role == DroneRole.Medic ? 100 : 0) + d.Drone!.Battery) is { } drone)
            members[party.IndexOf(pilot)].Pack!.Add(drone);
        // Electronics: radios work in pairs, so two or none; EMPs and decoys spread over the party.
        if (Take(d => d.Radio is not null, d => d.Radio!.AccuracyBonus) is { } radio1)
        {
            if (members.Count > 1 && Take(d => d.Radio is not null, d => d.Radio!.AccuracyBonus) is { } radio2)
            {
                members[0].Pack!.Add(radio1);
                members[1].Pack!.Add(radio2);
            }
            else stock[radio1]++;
        }
        var spread = 0;
        foreach (var kind in new Func<ItemDef, bool>[] { d => d.Use?.Emp is not null, d => d.Use?.Decoy is not null, d => d.Use is { NoiseRadius: > 0 } })
            for (var n = 0; n < members.Count; n++)
                if (Take(kind, _ => 1) is { } device) members[spread++ % members.Count].Pack!.Add(device);
        // Medical: a blood bag or med-kit for the downed, then bandages, splints and painkillers spread over the party.
        for (var i = 0; ; i++)
        {
            var id = Take(d => d.DefinedId is "trauma_kit" or "blood_bag" or "med_kit", d => d.Use?.ReviveHp ?? 0)
                     ?? Take(d => d.DefinedId is "bandage" or "clean_dressing", d => d.Use?.Heal ?? 0)
                     ?? Take(d => d.DefinedId is "splint" or "painkiller" or "antiseptic", _ => 1);
            if (id is null || i >= party.Count * 3) break;
            members[i % members.Count].Pack!.Add(id);
        }
        return members;
    }

    /// <summary>The bot climbs threat only after two clean runs in a row at the current cap (nobody dead, and unless bold, nobody hurt).</summary>
    private void OnReturn(ExpeditionOutcome o)
    {
        if (o.Dead == 0 && (_options.Bold || o.NewInjuries == 0)) _cleanRunsAtThreat++;
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
    int ItemsBack, RunStats Stats, string? Error)
{
    /// <summary>The vehicle tier driven (null = on foot), one-way days on the road, fuel burnt, items brought home in it.</summary>
    public string? Vehicle { get; init; }
    public double TravelDays { get; init; }
    public double Fuel { get; init; }
    public int StashedItems { get; init; }
    public bool VehicleLost { get; init; }
}
