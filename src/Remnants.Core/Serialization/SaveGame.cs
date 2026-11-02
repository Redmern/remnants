using System.Text.Json;
using System.Text.Json.Nodes;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Serialization;

/// <summary>
/// Save/load the colony state as JSON. Definitions are not saved; they reload from data/.
/// Version 2 adds the item inventory, the expedition (with the scavenge run in progress), patients,
/// recipes and the memorial. Version 3 adds family life: partners, parents, mood, pregnancies, growth, homes and
/// relationships. Older saves still load: missing parts start empty, and their children are placed on the new
/// growth track by age (without inherited aptitudes, so they only change by training).
/// Version 4 adds crafting tiers: production queues (a building's old single recipe becomes one repeating order with
/// its progress), studied blueprints, and the generic "blueprint" item became specific blueprints (old ones turn into
/// <see cref="LegacyBlueprintItem"/>).
/// Version 5 adds the world map: per-zone intel and depletion (<see cref="ColonyState.Zones"/>) and the expedition's
/// road (travel, arrival, the result carried home). Older saves start with every zone as data says and an expedition
/// already in its zone; an older scavenge run in progress loads as scavenge save version 1.
/// Version 6 carries the scavenge run's fog of war (stances, lookout, contacts, reveals, flares, motion sensors; see
/// <see cref="ScavengeSave"/> version 3). A version 5 run in progress loads with none of it: nobody in a stance, nothing
/// placed, no contacts.
/// Version 7 carries the run's co-op state (<see cref="ScavengeSave"/> version 4: alliances, pings, carrying); older runs
/// load with nobody allied or carried.
/// Version 8 adds experience and perks (survivors' XP, level, perk points, perks) and classes at study buildings
/// (<see cref="ColonyState.Studies"/>), plus the Hacking and Drone Piloting skills. Older survivors get XP for the skills
/// they already have (<c>xp.migrationPerSkillPoint</c>), so veterans arrive with levels and points to spend.
/// Version 9 adds scout drones over the world map (<see cref="ColonyState.DroneFlights"/>) and carries the scavenge run's
/// hacking and drones (<see cref="ScavengeSave"/> version 5); older saves load with no drones out and a run without
/// devices.
/// Version 10 adds the day and night cycle: per building the Night Shift and a Floodlight, per survivor the sleep debt.
/// Older saves load with no night shifts, no Floodlights and everyone rested; their clock hour is whatever their tick
/// says (version 9 and older games started at midnight).
/// Version 11 adds night missions: the expedition's wait for a better hour (<see cref="Expedition.WaitTicks"/>) and the run's
/// environment (<see cref="ScavengeSave"/> version 6); older runs in progress load at midday.
/// Version 12 adds cybernetics: the eyes as a body part, each prosthetic's humanity cost, elective flag and upkeep, booked
/// surgeries (<see cref="ColonyState.Surgeries"/>) and the designs the colony knows (<see cref="ColonyState.KnownCybernetics"/>);
/// the scavenge run carries the units' cybernetic hooks (<see cref="ScavengeSave"/> version 7). Older prosthetics are
/// mapped into the tree by <see cref="MigrateCybernetics"/>: an item outside the tree (none before items existed) becomes the
/// crude part below quality 1 and the standard cyborg part from there, and pays its tier's replacement humanity.
/// Version 14 adds Story mode: <see cref="ColonyState.Campaign"/> (null = Sandbox, so every older save loads as Sandbox),
/// survivors' <c>UniqueId</c> (Pell), the expedition's story mission, and carries the scavenge run's mission
/// (<see cref="ScavengeSave"/> version 8).
/// Version 15 replaces blueprints with schematics (<see cref="ColonyState.Schematics"/>, <see cref="ColonyState.Radio"/>):
/// see <see cref="MigrateSchematics"/>.
/// Version 16 adds the weather (<see cref="ColonyState.Weather"/>: its seed and spells) and lightning damage
/// (<see cref="Building.Damaged"/>), and carries the run's changing weather (<see cref="ScavengeSave"/> version 9). Older
/// saves get a weather seed from their RNG state and nothing damaged.
/// Version <see cref="FoodVersion"/> (17) adds food and cooking: food is inventory items with lots (<see cref="ColonyState.Food"/>),
/// buildings harvest (farm crops, greenhouse, hunting lodge), survivors carry nutrition, buffs and a diet. Older saves
/// are converted by <see cref="MigrateFood"/>: the single food pool becomes a mix of potatoes, beans, corn and canned
/// food of the same food value, Simple Meals become Boiled Potatoes and Hearty Stew becomes Stew (by food value), their
/// Kitchen orders follow (a paid batch gives its food back), and farms keep growing potatoes, already ripe.
/// Version <see cref="ElectronicsVersion"/> (18) adds electronics and power: the grid (<see cref="ColonyState.Power"/>: switched-off buildings, battery charge,
/// the breaker) and the charging bench (<see cref="ColonyState.Charging"/>), and carries the scavenge run's decoys,
/// EMP stuns, spent charges and secured cargo (<see cref="ScavengeSave"/> version 10). The migration is keyed on the data,
/// not the number: a save without them loads with an empty grid (everything on, batteries empty) and nothing charging.
/// Version 19 adds player trading (<see cref="ColonyState.Trade"/>: goods in escrow with their food lots, couriers on the
/// road, settled transaction ids). Older saves load with none of it.
/// Version <see cref="GearModsVersion"/> (20) adds gear mods: modded and upgraded gear as item ids
/// (<c>base[@quality][+mkN][+mod]...</c>, see <see cref="Items.GearModsData"/>) in the inventory, on fitted gun arms and in
/// the expedition, the Head equipment slot, and the run's <see cref="ScavengeSave"/> version 11. Nothing to migrate: older
/// saves hold no modded ids and load unchanged; every modded id is checked against gear_mods.json like any item.
/// Version <see cref="RaidsVersion"/> (21) adds colony defense and raids (<see cref="ColonyState.Defense"/>: placed defenses,
/// guards, the raid on its way, repairs, building damage, raid reports). Older saves load with no defenses and no raid
/// pressure, so the grace period applies from the day they are loaded on.
/// Version <see cref="MedicineVersion"/> (22) adds medicine: bodies carry conditions, dressings and painkiller doses, the colony its
/// Med Bay priorities (<see cref="ColonyState.MedicalCare"/>). Older saves load with no conditions and the default priorities,
/// and <see cref="MigrateMedicine"/> turns part of the old medicine stock into antiseptic (medicine.json <c>migration</c>).
/// Version <see cref="VehiclesVersion"/> (23) adds vehicles (<see cref="ColonyState.Vehicles"/>; the expedition's vehicle, noise,
/// fuel and way home) and carries the run's parked vehicles (<see cref="ScavengeSave"/> version 12). Older colonies get the
/// starting vehicles (<c>vehicles.starting</c>: the handcart) and their party out on foot.
/// Version <see cref="LootVersion"/> (24) adds each zone's exit stash (<see cref="ZoneStatus.Stash"/>) and carries the run's field loot
/// and salvage (<see cref="ScavengeSave.LootVersion"/>); older saves load with no stash.
/// Version 25 adds Story Acts 2–4: the campaign's world effects, the survivors Halcyon took, chain parties and the
/// Network-grade parts Doc's oath has counted (<see cref="Story.CampaignState"/>), survivors' story traits and trait hooks; the
/// run's <see cref="ScavengeSave"/> version 14. Nothing to migrate: every new field starts empty or at zero.
/// Version <see cref="ConstructionVersion"/> (26) adds construction sites (<see cref="ColonyState.Construction"/>): Construct and
/// Upgrade place a site that builders finish over time. Older saves had none pending (building was instant), so every
/// building in them stays finished and they load with no sites; the Construction skill starts at 0. A save with sites
/// loaded on data with construction off has them finished at once (see <see cref="FinishSites"/>).
/// Version <see cref="CardsVersion"/> (27) adds tactical cards (<see cref="ColonyState.Cards"/>: the collection, the squad deck,
/// personal slots and deck upgrades) and carries the run's <see cref="ScavengeSave"/> version 15 (plans, decks). Older colonies
/// get the starter set as their collection and deck, and an empty bestiary (<see cref="ColonyState.Bestiary"/>, added with
/// the cards on the unreleased branch). A run in progress inside an older save (an expedition in the zone)
/// cannot be carried over (old runs are not migrated, <see cref="ScavengeSave"/>): it is dropped and starts over from the
/// expedition's seed with the party as it set out, which is what a run not yet opened does anyway. The party stays out.
/// Version <see cref="MultiActivityVersion"/> (28) lets a survivor hold several jobs: each survivor's single
/// <c>assignedBuildingId</c> becomes the list <c>jobs</c> (one entry, or none), so older saves load with one job each.
/// </summary>
public static class SaveGame
{
    public const int Version = MultiActivityVersion;

    /// <summary>The save version that brought several jobs per survivor (<see cref="Survivors.Survivor.Jobs"/>).</summary>
    public const int MultiActivityVersion = 28;

    /// <summary>The save version that brought tactical cards (keyed so a merge renumbers it in one place).</summary>
    public const int CardsVersion = 27;

    /// <summary>The save version that brought construction sites.</summary>
    public const int ConstructionVersion = 26;

    /// <summary>The save version that brought food and cooking (one constant so a renumbering on merge is one edit).</summary>
    public const int FoodVersion = 17;

    /// <summary>The save version that brought vehicles.</summary>
    public const int VehiclesVersion = 23;
    /// <summary>The save version that brought electronics and power.</summary>
    public const int ElectronicsVersion = 18;

    /// <summary>The save version that brought gear mods (nothing to migrate; see the remarks).</summary>
    public const int GearModsVersion = 20;
    /// <summary>The save version that added raids (keyed, so a rebase only renumbers it).</summary>
    public const int RaidsVersion = 21;

    /// <summary>The version that added medicine (keyed so a merge renumbers it in one place).</summary>
    public const int MedicineVersion = 22;

    /// <summary>The version that added field loot and salvage (keyed so a merge renumbers it in one place).</summary>
    public const int LootVersion = 24;
    private const int OldestVersion = 1;

    /// <summary>Old blueprint and datashard item ids and the schematics they became (version 13).</summary>
    public static readonly IReadOnlyList<(string From, string To)> SchematicRenames =
    [
        ("bp_riot_armor", "sch_riot_armor"), ("bp_rifle", "sch_rifle"), ("bp_scanner", "sch_scanner"),
        ("bp_evac_beacon", "sch_evac_beacon"), ("bp_linked_carbine", "sch_linked_carbine"), ("bp_linked_plating", "sch_linked_plating"),
        ("bp_signal_jammer", "sch_signal_jammer"), ("bp_cybernetics", "sch_cybernetics"), ("bp_cyberdeck", "sch_cyberdeck"),
        ("bp_scout_drone", "sch_scout_drone"), ("bp_network_grade", "sch_network_grade"),
        ("ds_network_ghost", "sch_network_ghost"), ("ds_override", "sch_override"), ("ds_drone_payload", "sch_drone_payload"),
    ];

    /// <summary>
    /// Before version 15: the studied blueprints become known schematics, and nobody loses anything they could do
    /// before — the colony is granted every newly gated schematic it was already using: the levels its buildings have
    /// (and the buildings themselves), the recipes in its queues, Camo Cloak and Grappling Hook with a Tailor / Workshop at
    /// L2, and the cybernetics tiers and specialisations of every design it knows, owns or has fitted. Plain schematic
    /// items in the inventory are learned; copies of known ones become Research Data. Story saves (14) hand over the
    /// schematics and Research Data the campaign kept for itself.
    /// </summary>
    private static void MigrateSchematics(ColonySim sim, List<string> oldKnown, List<string> storyGranted, int storyResearchData)
    {
        var state = sim.State;
        var catalog = sim.Data.Schematics;
        state.Schematics.Migrated = true;
        if (catalog.All.Count == 0) return;
        void Grant(string? id)
        {
            if (id is null || catalog.Find(id) is null || state.Schematics.Known.Contains(id)) return;
            Remnants.Core.Schematics.Schematics.Grant(sim, id, Remnants.Core.Schematics.SchematicSource.Migration);
        }
        foreach (var id in oldKnown) Grant(id);
        foreach (var id in storyGranted)
            if (catalog.Find(id) is not null && !state.Schematics.Known.Contains(id))
                Remnants.Core.Schematics.Schematics.Grant(sim, id, Remnants.Core.Schematics.SchematicSource.Story);
        if (storyResearchData > 0) state.Stockpile.Add(catalog.Rules.ResearchDataResource, storyResearchData);
        foreach (var b in state.Buildings)
            for (var level = 1; level <= b.Level; level++) Grant(catalog.GateOfBuildingLevel(b.DefId, level));
        foreach (var order in state.Buildings.SelectMany(b => b.Queue)) Grant(catalog.GateOfRecipe(order.RecipeId));
        if (state.Buildings.Any(b => b.DefId == "tailor" && b.Level >= 2)) Grant(catalog.GateOfRecipe("camo_cloak"));
        if (state.Buildings.Any(b => b.DefId == "workshop" && b.Level >= 2)) Grant(catalog.GateOfRecipe("grappling_hook"));
        if (sim.Data.Cybernetics is { } cyber)
        {
            string Base(string id) => sim.Scavenge?.Items.GetValueOrDefault(id)?.DefinedId ?? id;
            var designs = state.KnownCybernetics
                .Concat(state.Inventory.Counts.Keys.Select(Base))
                .Concat(state.Survivors.SelectMany(s => s.Body.Parts.Values).Select(p => p.ProstheticItemId).OfType<string>().Select(Base))
                .Select(cyber.Node).OfType<CyberNodeDef>().Distinct();
            foreach (var node in designs)
            {
                Grant(Cybernetics.TierGate(sim, node));
                Grant(Cybernetics.SpecGate(sim, node));
            }
        }
        foreach (var (id, count) in state.Inventory.Counts.ToList())
        {
            if (catalog.Find(id) is not { } def) continue;
            for (var i = 0; i < count; i++)
                if (state.Schematics.Known.Contains(id) || !def.Encrypted)
                {
                    state.Inventory.TryTake(id);
                    Remnants.Core.Schematics.Schematics.Receive(sim, id, Remnants.Core.Schematics.SchematicSource.Migration);
                }
        }
    }

    /// <summary>What a pre-version-4 generic "blueprint" item becomes.</summary>
    public const string LegacyBlueprintItem = "bp_scanner";  // renamed again to sch_scanner in version 13

    private sealed record Envelope(int Version, ColonyState Colony);

    public static string Save(ColonyState state) => JsonSerializer.Serialize(new Envelope(Version, state), Json.Options);

    /// <summary>
    /// Loads a colony. Pass <paramref name="scavenge"/> to also check item ids and the expedition's scavenge run
    /// (the game always does; without it those parts are taken on trust).
    /// </summary>
    public static ColonySim Load(GameData data, string json, ScavengeData? scavenge = null)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new JsonException("Save file is empty.");
        if (node["version"]?.GetValue<int>() is < 4 and >= OldestVersion && node["colony"] is JsonObject old) MigrateCrafting(old);
        if (node["version"]?.GetValue<int>() == 1 && node["colony"] is JsonObject v1) MigratePlots(v1);
        if (node["version"]?.GetValue<int>() is { } v && v < MultiActivityVersion && v >= OldestVersion) MigrateJobs(node["colony"]);
        var oldKnown = new List<string>();
        var storyGranted = new List<string>();
        var storyResearchData = 0;
        if (node["version"]?.GetValue<int>() is < 15 and >= OldestVersion && node["colony"] is JsonObject pre15)
        {
            foreach (var (from, to) in SchematicRenames) RenameItem(pre15, from, to);
            oldKnown = pre15["knownBlueprints"]?.AsArray().Select(v => v!.GetValue<string>()).ToList() ?? [];
            pre15.Remove("knownBlueprints");
            // Story saves (14) kept the story's schematics and Research Data in the campaign until this module existed.
            if (pre15["campaign"] is JsonObject campaign)
            {
                storyGranted = campaign["schematics"]?.AsArray().Select(v => v!.GetValue<string>()).ToList() ?? [];
                campaign.Remove("schematics");
                if (campaign["flags"] is JsonObject flags && flags["research_data"] is { } rd)
                {
                    storyResearchData = rd.GetValue<int>();
                    flags.Remove("research_data");
                }
            }
        }
        var envelope = node.Deserialize<Envelope>(Json.Options)
            ?? throw new JsonException("Save file is empty.");
        if (envelope.Version < OldestVersion || envelope.Version > Version)
            throw new JsonException($"Unsupported save version {envelope.Version} (expected {OldestVersion}..{Version}).");
        var colony = envelope.Colony ?? throw new JsonException("Save file has no colony.");
        if (envelope.Version < CardsVersion && colony.Expedition is { PendingResult: null } running) running.Scavenge = null;
        if (envelope.Version < 3) MigrateGrowth(data, colony);
        foreach (var s in colony.Survivors)
            if (envelope.Version < 8) Progression.Migrate(data, s);
            else foreach (var skill in Enum.GetValues<Survivors.Skill>()) s.Skills.TryAdd(skill, 0);
        StudySystem.Repair(colony);
        if (envelope.Version < 12) MigrateCybernetics(data, colony);
        if (envelope.Version < VehiclesVersion && colony.Vehicles.Count == 0)
            foreach (var tier in data.Vehicles.Starting) VehicleRules.Add(colony, data, tier);
        if (envelope.Version < FoodVersion) MigrateFood(data, colony);
        if (envelope.Version < RaidsVersion) MigrateRaids(colony);
        RepairDefense(data, colony);
        if (envelope.Version < MedicineVersion) MigrateMedicine(data, scavenge, colony);
        if (envelope.Version < CardsVersion && scavenge is not null) ColonyCards.GrantStarter(colony, scavenge.Cards);
        Validate(data, colony);
        if (scavenge is not null) Validate(scavenge, colony);
        var sim = new ColonySim(data, colony, scavenge);
        if (envelope.Version < 15) MigrateSchematics(sim, oldKnown, storyGranted, storyResearchData);
        FinishSites(sim);
        return sim;
    }

    /// <summary>
    /// Sites whose building type or level no longer exists, or that no longer fit (plot outside the town or taken, an upgrade
    /// that does not follow its building's level), are dropped (their materials refunded); with construction off in
    /// the data, every site left is finished as if built.
    /// </summary>
    private static void FinishSites(ColonySim sim)
    {
        foreach (var site in sim.State.Construction.Sites.ToList())
        {
            var standing = sim.State.FindBuilding(site.BuildingId);
            var gone = !sim.Data.Buildings.TryGetValue(site.DefId, out var def) || site.Level < 1 || site.Level > def.MaxLevel
                       || site.Plot < 0 || site.Plot >= sim.Data.Rules.Layout.PlotCount
                       || (site.IsUpgrade
                           ? standing is null || standing.DefId != site.DefId || standing.Level != site.Level - 1
                           : standing is not null || sim.State.Buildings.Any(b => b.Plot == site.Plot)
                             || sim.State.Construction.Sites.Any(o => o != site && o.Plot == site.Plot && !o.IsUpgrade));
            if (gone)
            {
                ConstructionSystem.Refund(sim, site);
                sim.State.Construction.Sites.Remove(site);
            }
            else if (!sim.Data.Rules.Construction.Enabled) ConstructionSystem.Complete(sim, site);
        }
    }

    /// <summary>
    /// The first version 1 saves predate the town grid (plots came within version 1): buildings without a plot get one in
    /// building order, as New Game and Construct hand them out (the first free plot), so they load instead of all
    /// claiming plot 0.
    /// </summary>
    private static void MigratePlots(JsonObject colony)
    {
        if (colony["buildings"] is not JsonArray buildings) return;
        var taken = buildings.Select(b => b?["plot"]?.GetValue<int>()).OfType<int>().ToHashSet();
        var next = 0;
        foreach (var b in buildings.OfType<JsonObject>().Where(b => b["plot"] is null).OrderBy(b => b["id"]?.GetValue<int>() ?? 0))
        {
            while (taken.Contains(next)) next++;
            b["plot"] = next;
            taken.Add(next);
        }
    }

    /// <summary>
    /// Before version 4 a production building had one recipe (<c>recipeId</c>, <c>craftProgress</c>, <c>craftPaid</c>):
    /// it becomes a repeating order. The generic "blueprint" item id becomes <see cref="LegacyBlueprintItem"/>
    /// everywhere it appears (inventory, packs, caches, loot); no state property was ever named "blueprint".
    /// </summary>
    private static void MigrateCrafting(JsonObject colony)
    {
        var nextOrder = 1;
        foreach (var b in colony["buildings"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var recipe = b["recipeId"]?.GetValue<string>();
            var progress = b["craftProgress"]?.GetValue<double>() ?? 0;
            var paid = b["craftPaid"]?.GetValue<bool>() ?? false;
            b.Remove("recipeId");
            b.Remove("craftProgress");
            b.Remove("craftPaid");
            if (recipe is null) continue;
            b["queue"] = new JsonArray(new JsonObject
            {
                ["id"] = nextOrder++, ["recipeId"] = recipe, ["remaining"] = 1, ["repeat"] = true,
                ["progress"] = progress, ["paid"] = paid,
            });
        }
        colony["nextOrderId"] = nextOrder;
        RenameItem(colony, "blueprint", LegacyBlueprintItem);
    }

    /// <summary>Before version 28: every survivor's (anywhere in the colony) <c>assignedBuildingId</c> becomes <c>jobs</c>.</summary>
    private static void MigrateJobs(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                if (o.ContainsKey("assignedBuildingId"))
                {
                    var id = o["assignedBuildingId"]?.GetValue<int>();
                    o.Remove("assignedBuildingId");
                    o["jobs"] = id is { } job ? new JsonArray(job) : new JsonArray();
                }
                foreach (var (_, child) in o.ToList()) MigrateJobs(child);
                break;
            case JsonArray a:
                foreach (var child in a) MigrateJobs(child);
                break;
        }
    }

    private static void RenameItem(JsonNode node, string from, string to)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    if (child is null) continue;
                    if (child is JsonValue v && v.TryGetValue<string>(out var str) && str == from) obj[key] = to;
                    else RenameItem(child, from, to);
                    if (key == from)
                    {
                        var value = obj[key];
                        obj.Remove(key);
                        obj[to] = value?.DeepClone();
                    }
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue v && v.TryGetValue<string>(out var str) && str == from) array[i] = to;
                    else if (array[i] is { } child) RenameItem(child, from, to);
                }
                break;
        }
    }

    /// <summary>Crude parts the tree maps legacy prosthetics to (below quality 1), and the standard cyborg parts (from 1).</summary>
    public static readonly IReadOnlyDictionary<Survivors.BodyRegion, (string Crude, string Cyborg)> LegacyProsthetics =
        new Dictionary<Survivors.BodyRegion, (string, string)>
        {
            [Survivors.BodyRegion.Arms] = ("hook_arm", "cyber_arm"),
            [Survivors.BodyRegion.Legs] = ("peg_leg", "cyber_leg"),
        };

    /// <summary>
    /// Before version 12: prosthetics join the cybernetics tree (see the class remarks) and pay humanity as replacements,
    /// and the colony knows what it has fitted or owns.
    /// </summary>
    private static void MigrateCybernetics(GameData data, ColonyState colony)
    {
        var cyber = data.Cybernetics;
        foreach (var s in colony.Survivors)
            foreach (var (part, state) in s.Body.Parts)
            {
                if (state.Condition != Survivors.PartCondition.Prosthetic) continue;
                if (cyber?.Node(state.ProstheticItemId) is null && LegacyProsthetics.TryGetValue(Survivors.Body.RegionOf(part), out var legacy))
                    state.ProstheticItemId = state.ProstheticQuality < 1 ? legacy.Crude : legacy.Cyborg;
                state.Upkeep = 1;
                state.ServiceTicks = 0;
                if (cyber is null) continue;
                state.HumanityCost = cyber.Tier(cyber.TierOf(state)).Humanity;
                colony.KnownCybernetics.Add(state.ProstheticItemId!);
            }
        if (cyber is not null)
            foreach (var id in colony.Inventory.Counts.Keys.Where(id => cyber.Node(id) is not null))
                colony.KnownCybernetics.Add(id);
        // A run in progress is not migrated: a save this old (before cards) drops it, and one heading home has none.
    }

    /// <summary>
    /// Before medicine: the old medicine stock was every kind of care at once. Part of it (<c>shareConverted</c>) becomes
    /// antiseptic, <c>medicinePerAntiseptic</c> each, rounded down; the rest stays as medicine (herbal remedies).
    /// </summary>
    internal static void MigrateMedicine(GameData data, ScavengeData? scavenge, ColonyState colony)
    {
        if (data.Medicine is not { } med) return;
        var rules = med.Def.Migration;
        if (scavenge is not null && !scavenge.Items.ContainsKey(rules.AntisepticItem)) return;
        var medicineId = data.Rules.Medical.MedicineResourceId;
        var count = (int)Math.Floor(colony.Stockpile.Get(medicineId) * rules.ShareConverted / rules.MedicinePerAntiseptic + 1e-9);
        if (count <= 0 || !colony.Stockpile.TryTake(medicineId, count * rules.MedicinePerAntiseptic)) return;
        colony.Inventory.Add(rules.AntisepticItem, count);
    }

    /// <summary>Before raids: an empty defense layer; the grace period counts from the day the save was made.</summary>
    private static void MigrateRaids(ColonyState colony)
    {
        colony.Defense = new Raids.DefenseState { LastRaidTick = colony.Tick };
    }

    /// <summary>
    /// Guard posts of survivors who are gone, and repairs of things that are gone, are dropped. Defense schematics learned
    /// before the schematics merge (kept in <c>Defense.KnownSchematics</c>) move into <see cref="ColonyState.Schematics"/>.
    /// </summary>
    private static void RepairDefense(GameData data, ColonyState colony)
    {
        colony.Defense ??= new();
        foreach (var id in colony.Defense.KnownSchematics.Where(id => data.Schematics.Find(id) is not null))
            if (colony.Schematics.Known.Add(id))
            {
                colony.Schematics.LearnedFrom[id] = Remnants.Core.Schematics.SchematicSource.Migration;
                colony.Schematics.LearnedAt[id] = colony.Tick;
            }
        colony.Defense.KnownSchematics.Clear();
        foreach (var (id, post) in colony.Defense.Guards.ToList())
            if (colony.FindSurvivor(id) is null || colony.Defense.Find(post) is null) colony.Defense.Guards.Remove(id);
        colony.Defense.Repairs.RemoveAll(r => r.Target == Raids.RepairTarget.Building ? colony.FindBuilding(r.TargetId) is null : colony.Defense.Find(r.TargetId) is null);
    }

    /// <summary>Before version 3 children grew up by age in years; put them at the same share of the growth track.</summary>
    /// <summary>Old food pool: share of its food value per new item (the rest of the rounding goes to potatoes).</summary>
    private static readonly (string Item, double Share)[] LegacyFoodMix = [("beans", 0.25), ("corn", 0.15), ("canned_food", 0.10)];

    /// <summary>Old Kitchen resources and recipes → the new dish (item, recipe) and its food value per old unit.</summary>
    private static readonly Dictionary<string, (string Item, string OldRecipe, string Recipe, double OldValue, double OldFood)> LegacyMeals =
        new(StringComparer.Ordinal)
        {
            ["meal"] = ("boiled_potatoes", "simple_meal", "boiled_potatoes", 2.0, 4),
            ["stew"] = ("stew", "hearty_stew", "stew", 2.5, 5),
        };

    private static void MigrateFood(GameData data, ColonyState colony)
    {
        var amounts = colony.Stockpile.Amounts;
        var pool = amounts.GetValueOrDefault("food");
        amounts.Remove("food");
        foreach (var b in colony.Buildings)
            foreach (var order in b.Queue)
            {
                var old = LegacyMeals.Values.FirstOrDefault(m => m.OldRecipe == order.RecipeId);
                if (old.Item is null) continue;
                order.RecipeId = old.Recipe;
                if (!order.Paid) continue;
                pool += old.OldFood;
                order.Paid = false;
                order.Progress = 0;
                order.PaidItems.Clear();
            }

        void Give(string item, int count)
        {
            if (count <= 0) return;
            if (data.Food is not null) Pantry.Add(colony, data, item, count);
            else colony.Inventory.Add(item, count);
        }
        foreach (var (resource, meal) in LegacyMeals)
        {
            var units = amounts.GetValueOrDefault(resource);
            amounts.Remove(resource);
            var value = data.Food?.Items.GetValueOrDefault(meal.Item)?.FoodValue ?? 1;
            Give(meal.Item, (int)Math.Round(units * meal.OldValue / value));
        }
        var given = 0.0;
        foreach (var (item, share) in LegacyFoodMix)
        {
            var value = data.Food?.Items.GetValueOrDefault(item)?.FoodValue ?? 1;
            var count = (int)Math.Floor(pool * share / value);
            Give(item, count);
            given += count * value;
        }
        Give("potato", (int)Math.Ceiling(pool - given - 1e-9));
    }

    private static void MigrateGrowth(GameData data, ColonyState colony)
    {
        foreach (var s in colony.Survivors.Where(s => s.IsChild))
        {
            s.GrowthTicks = Math.Max(data.Rules.Life.InfantTicks, Bonds.GrowthAtAge(s.AgeTicks, data.Rules));
            s.LifeStage = Bonds.StageAt(s.GrowthTicks, data.Rules.Life);
            s.AgeTicks = Bonds.AgeTicksAt(s.GrowthTicks, data.Rules);
        }
    }

    /// <summary>A save must still fit the current data/ (ids can be renamed, levels removed by a balancing edit).</summary>
    private static void Validate(GameData data, ColonyState colony)
    {
        foreach (var b in colony.Buildings)
        {
            if (!data.Buildings.TryGetValue(b.DefId, out var def))
                throw new JsonException($"Save references unknown building type '{b.DefId}'.");
            if (b.Level < 1 || b.Level > def.MaxLevel)
                throw new JsonException($"Save has {def.Name} at level {b.Level}; data allows 1..{def.MaxLevel}.");
            if (b.Plot < 0 || b.Plot >= data.Rules.Layout.PlotCount)
                throw new JsonException($"Save has {def.Name} on plot {b.Plot}; the town has plots 0..{data.Rules.Layout.PlotCount - 1}.");
        }
        if (colony.Buildings.GroupBy(b => b.Plot).FirstOrDefault(g => g.Count() > 1) is { } shared)
            throw new JsonException($"Save has several buildings on plot {shared.Key}.");
        foreach (var res in colony.Stockpile.Amounts.Keys)
            if (!data.Resources.ContainsKey(res))
                throw new JsonException($"Save references unknown resource '{res}'.");
        foreach (var b in colony.Buildings)
            foreach (var order in b.Queue)
                if (!data.Recipes.TryGetValue(order.RecipeId, out var recipe) || recipe.Building != b.DefId)
                    throw new JsonException($"Save has {data.Buildings[b.DefId].Name} on unknown recipe '{order.RecipeId}'.");
        foreach (var s in colony.Survivors.Where(s => s.PatientAtBuildingId is not null))
            if (colony.FindBuilding(s.PatientAtBuildingId!.Value) is not { } bay || bay.DefId != data.Rules.Medical.BuildingId)
                throw new JsonException($"Save has {s.Name} admitted to building {s.PatientAtBuildingId}, which is not a Med Bay.");

        var ids = colony.Survivors.Select(s => s.Id).ToHashSet();
        foreach (var op in colony.Surgeries)
            if (!ids.Contains(op.SurvivorId) || colony.FindBuilding(op.BuildingId)?.DefId != data.Rules.Medical.BuildingId)
                throw new JsonException($"Save has surgery {op.Id} for a survivor or at a building that does not fit.");
        foreach (var id in colony.KnownCybernetics)
            if (data.Cybernetics is { } cyber && cyber.Node(id) is null) throw new JsonException($"Save knows unknown cybernetics design '{id}'.");
        foreach (var s in colony.Survivors)
            foreach (var perk in s.Perks)
                if (!data.Perks.ContainsKey(perk)) throw new JsonException($"Save has {s.Name} with unknown perk '{perk}'.");
        foreach (var o in colony.Studies)
        {
            if (colony.FindBuilding(o.BuildingId) is not { } at || !data.Progression.Study.Buildings.ContainsKey(at.DefId))
                throw new JsonException($"Save has a class at building {o.BuildingId}, which is not a study building.");
            if (!ids.Contains(o.StudentId) || o.MentorId is { } m && !ids.Contains(m))
                throw new JsonException($"Save has class {o.Id} with a student or mentor who is not in the colony.");
            if ((o.Skill is null) == (o.PerkId is null) || o.PerkId is { } p && !data.Perks.ContainsKey(p))
                throw new JsonException($"Save has class {o.Id} studying an unknown subject.");
        }
        foreach (var s in colony.Survivors.Where(s => s.PartnerId is not null))
            if (colony.FindSurvivor(s.PartnerId!.Value)?.PartnerId != s.Id)
                throw new JsonException($"Save has {s.Name} partnered with {s.PartnerId}, who is not partnered back.");
        foreach (var r in colony.Relationships)
            if (r.A >= r.B || !ids.Contains(r.A) || !ids.Contains(r.B))
                throw new JsonException($"Save has a relationship between {r.A} and {r.B} that does not fit the survivors.");

        foreach (var v in colony.Vehicles)
        {
            if (!data.VehicleTiers.ContainsKey(v.TierId)) throw new JsonException($"Save has {v.Name} of unknown vehicle tier '{v.TierId}'.");
            if (v.Upgrades.FirstOrDefault(u => !data.VehicleUpgrades.ContainsKey(u.UpgradeId)) is { } upgrade)
                throw new JsonException($"Save has {v.Name} with unknown upgrade '{upgrade.UpgradeId}'.");
        }
        if (colony.Vehicles.Select(v => v.Id).Distinct().Count() != colony.Vehicles.Count) throw new JsonException("Save has two vehicles with one id.");
        var driven = colony.Expedition?.VehicleId;
        if (driven is { } vid && colony.Vehicles.Find(v => v.Id == vid) is not { Away: true })
            throw new JsonException($"Save has the expedition in vehicle {vid}, which is not out with it.");
        if (colony.Vehicles.FirstOrDefault(v => v.Away && v.Id != driven) is { } strayVehicle)
            throw new JsonException($"Save has {strayVehicle.Name} away without an expedition.");

        var members = colony.Expedition?.Members.Select(m => m.SurvivorId).ToHashSet() ?? [];
        foreach (var id in members)
            if (colony.FindSurvivor(id) is not { Away: true })
                throw new JsonException($"Save has expedition member {id} who is not away.");
        if (colony.Survivors.FirstOrDefault(s => s.Away && !members.Contains(s.Id)) is { } stray)
            throw new JsonException($"Save has {stray.Name} away without an expedition.");
    }

    /// <summary>Items and the expedition must still fit items.json and zones.json.</summary>
    private static void Validate(ScavengeData data, ColonyState colony)
    {
        void RequireItem(string id)
        {
            if (!data.Items.ContainsKey(id)) throw new JsonException($"Save references unknown item '{id}'.");
        }

        foreach (var id in colony.Inventory.Counts.Keys) RequireItem(id);
        foreach (var zoneId in colony.Zones.Keys)
            if (!data.Zones.ContainsKey(zoneId)) throw new JsonException($"Save references unknown zone '{zoneId}'.");
        foreach (var id in colony.Zones.Values.SelectMany(z => z.Stash)) RequireItem(id);
        foreach (var f in colony.DroneFlights)
        {
            RequireItem(f.ItemId);
            if (!data.Zones.ContainsKey(f.ZoneId)) throw new JsonException($"Save references unknown zone '{f.ZoneId}'.");
        }
        foreach (var id in colony.Studies.Select(o => o.ManualItemId).OfType<string>()) RequireItem(id);
        foreach (var id in colony.Buildings.SelectMany(b => b.Queue).SelectMany(o => o.PaidItems)) RequireItem(id);
        foreach (var part in colony.Survivors.SelectMany(s => s.Body.Parts.Values))
            if (part.ProstheticItemId is { } id) RequireItem(id);
        foreach (var op in colony.Surgeries) RequireItem(op.ItemId);
        foreach (var id in colony.Cards.Owned.Keys.Concat(colony.Cards.Deck).Concat(colony.Cards.Slots.Values.SelectMany(s => s)).Concat(colony.Cards.Taught))
            if (data.Cards.Find(id) is null) throw new JsonException($"Save references unknown card '{id}'.");
        foreach (var id in colony.Bestiary.Keys)
            if (!data.Enemies.ContainsKey(id)) throw new JsonException($"Save's bestiary references unknown enemy '{id}'.");
        if (colony.Defense.Raid?.Battle is { } battle) ScavengeSave.Validate(data, battle);
        if (colony.Expedition is not { } expedition) return;
        if (!data.Zones.ContainsKey(expedition.ZoneId)) throw new JsonException($"Save references unknown zone '{expedition.ZoneId}'.");
        foreach (var id in expedition.Members.SelectMany(m => m.AllItems)) RequireItem(id);
        if (expedition.Scavenge is { } run) ScavengeSave.Validate(data, run);
        if (expedition.PendingResult is { } result)
            foreach (var id in result.Returned.Concat(result.Recruits).SelectMany(r => r.Equipment.Values.Concat(r.Pack))
                         .Concat(result.Vehicle?.Stash ?? [])) RequireItem(id);
    }
}
