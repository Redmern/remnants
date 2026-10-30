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
/// </summary>
public static class SaveGame
{
    public const int Version = 16;
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
        if (envelope.Version < 3) MigrateGrowth(data, colony);
        foreach (var s in colony.Survivors)
            if (envelope.Version < 8) Progression.Migrate(data, s);
            else foreach (var skill in Enum.GetValues<Survivors.Skill>()) s.Skills.TryAdd(skill, 0);
        StudySystem.Repair(colony);
        if (envelope.Version < 12) MigrateCybernetics(data, scavenge, colony);
        Validate(data, colony);
        if (scavenge is not null) Validate(scavenge, colony);
        var sim = new ColonySim(data, colony, scavenge);
        if (envelope.Version < 15) MigrateSchematics(sim, oldKnown, storyGranted, storyResearchData);
        return sim;
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
    /// the colony knows what it has fitted or owns, and a run in progress gets its units' cybernetic hooks.
    /// </summary>
    private static void MigrateCybernetics(GameData data, ScavengeData? scavenge, ColonyState colony)
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
        if (scavenge is not null && colony.Expedition?.Scavenge is { } run) ScavengeSave.MigrateCybernetics(scavenge, run);
    }

    /// <summary>Before version 3 children grew up by age in years; put them at the same share of the growth track.</summary>
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
        if (colony.Expedition is not { } expedition) return;
        if (!data.Zones.ContainsKey(expedition.ZoneId)) throw new JsonException($"Save references unknown zone '{expedition.ZoneId}'.");
        foreach (var id in expedition.Members.SelectMany(m => m.AllItems)) RequireItem(id);
        if (expedition.Scavenge is { } run) ScavengeSave.Validate(data, run);
        if (expedition.PendingResult is { } result)
            foreach (var id in result.Returned.Concat(result.Recruits).SelectMany(r => r.Equipment.Values.Concat(r.Pack))) RequireItem(id);
    }
}
