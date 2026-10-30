using System.Text.Json.Nodes;
using Remnants.Core.Colony;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Schematics;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class SchematicDataTests
{
    private static SchematicCatalog Catalog => TestData.Real.Schematics;

    [Fact]
    public void Every_schematic_has_a_source_that_is_not_a_story_mission()
    {
        var storyOnly = Catalog.All.Values.Where(s => s.Sources.All(src => src == SchematicSource.Story)).Select(s => s.Id).ToList();
        Assert.Empty(storyOnly);
        Assert.NotEmpty(Catalog.All);
    }

    [Fact]
    public void Tiers_one_to_three_can_be_researched_and_tier_four_cannot()
    {
        foreach (var s in Catalog.All.Values)
            Assert.True(s.Tier == 4 ? !s.Has(SchematicSource.Research) : s.Has(SchematicSource.Research), $"{s.Id} (tier {s.Tier})");
    }

    [Fact]
    public void Kill_sources_and_enemy_drop_tables_agree()
    {
        var dropped = ScavengeTestData.Real.Enemies.Values.SelectMany(e => e.Drops).Select(d => d.Item).Where(id => Catalog.Find(id) is not null).ToHashSet();
        foreach (var s in Catalog.All.Values.Where(s => s.Available))
            Assert.True(s.Has(SchematicSource.Kill) == dropped.Contains(s.Id), $"{s.Id}: Kill source vs drop tables");
        Assert.DoesNotContain(ScavengeTestData.Real.Enemies["husk"].Drops, d => Catalog.Find(d.Item) is not null);
    }

    [Fact]
    public void The_default_set_is_free_and_the_doc_s_gates_hold()
    {
        foreach (var b in new[] { "town_hall", "shelter", "woodcutter", "quarry", "scrapyard", "farm", "kitchen", "house" })
            for (var level = 1; level <= 3; level++) Assert.Null(Catalog.GateOfBuildingLevel(b, level));
        foreach (var b in new[] { "workshop", "tailor", "med_bay", "armory", "electronics_lab" })
        {
            Assert.Null(Catalog.GateOfBuildingLevel(b, 1));
            Assert.Null(Catalog.GateOfBuildingLevel(b, 2));
            Assert.NotNull(Catalog.GateOfBuildingLevel(b, 3));
        }
        Assert.Null(Catalog.GateOfBuilding("radio_mast"));
        Assert.Equal("sch_radio_mast_2", Catalog.GateOfBuildingLevel("radio_mast", 2));
        foreach (var r in new[] { "herbal_remedy", "peg_leg", "hook_arm", "pipe", "spear", "med_kit", "fiber_cloth", "bandage", "rag_coat",
                     "backpack", "padded_vest", "climbing_gear", "scout_jacket", "simple_meal", "hearty_stew", "canned_food", "machete",
                     "pistol", "salvage_circuits", "flare", "motion_sensor", "binoculars", "drone_parts", "hacking_kit" })
            Assert.Null(Catalog.GateOfRecipe(r));
        Assert.Equal("sch_camo_cloak", Catalog.GateOfRecipe("camo_cloak"));
        Assert.Equal("sch_grappling_hook", Catalog.GateOfRecipe("grappling_hook"));
        Assert.Equal("sch_powered_limbs", Catalog.GateOf(UnlockKind.Prosthetic, "tier:2"));
    }

    [Fact]
    public void Every_encrypted_schematic_has_a_decrypt_order_and_every_schematic_an_item()
    {
        foreach (var s in Catalog.All.Values)
        {
            Assert.Equal(Remnants.Core.Items.ItemKind.Schematic, ScavengeTestData.Real.Items[s.Id].Kind);
            var decrypt = TestData.Real.Recipes.GetValueOrDefault(Schematics.Schematics.DecryptRecipeId(s.Id));
            Assert.Equal(s.Encrypted && s.Available, decrypt is not null);
        }
    }

    [Fact]
    public void Extension_files_merge_and_a_reused_id_is_an_error()
    {
        var main = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "schematics.json"));
        const string extra = """{ "schematics": [ { "id": "sch_test_mod", "name": "Test Mod", "tier": 2, "planned": true, "unlocks": [ { "kind": "Vehicle", "id": "x" } ], "sources": ["Loot"] } ] }""";
        var merged = SchematicCatalogLoader.LoadFromJson(main, extra);
        Assert.NotNull(merged.Find("sch_test_mod"));
        Assert.Equal(TestData.Real.Schematics.Rules.Drops.Cache, merged.Rules.Drops.Cache);
        Assert.Throws<Remnants.Core.Data.GameDataException>(() => SchematicCatalogLoader.LoadFromJson(main, extra, extra));
    }
}

public class SchematicLearningTests
{
    private static (ColonySim Sim, Survivor A) Launched()
    {
        var sim = LoopTestData.Colony(townHallLevel: 2);
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [new ExpeditionMember(a.Id)])).Ok);
        return (sim, a);
    }

    [Fact]
    public void Plain_schematics_are_learned_when_the_party_gets_home_and_a_copy_becomes_research_data()
    {
        var (sim, a) = Launched();
        var unlocked = new List<SchematicUnlocked>();
        sim.SchematicUnlocked += unlocked.Add;
        var result = LoopTestData.Result(returned: [LoopTestData.Returned(a, pack: ["sch_rifle", "sch_rifle"])]);

        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);

        Assert.True(Schematics.Schematics.Has(sim, "sch_rifle"));
        Assert.Equal(0, sim.State.Inventory.Count("sch_rifle"));
        Assert.Equal(4, sim.State.Stockpile.Get("research_data"));  // the second copy: tier 2 → 4
        Assert.Equal(1, sim.State.Schematics.Duplicates);
        Assert.Equal(SchematicSource.Loot, Assert.Single(unlocked).Source);
        Assert.Equal(ReceiveOutcome.Duplicate, sim.State.LastReport!.Schematics["sch_rifle"]);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.SchematicLearned);
    }

    [Fact]
    public void Encrypted_schematics_come_home_as_chips_and_a_decrypt_order_at_the_lab_teaches_them()
    {
        var (sim, a) = Launched();
        var result = LoopTestData.Result(returned: [LoopTestData.Returned(a, pack: ["sch_signal_jammer"])]);
        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);
        Assert.Equal(SchematicStatus.ToDecrypt, Schematics.Schematics.Status(sim, "sch_signal_jammer"));
        Assert.Equal(1, sim.State.Inventory.Count("sch_signal_jammer"));

        var lab = sim.Build("electronics_lab");
        var decrypt = Schematics.Schematics.DecryptRecipeId("sch_signal_jammer");
        Assert.Equal(CommandError.RecipeLevelTooLow, sim.Execute(new QueueRecipe(lab.Id, decrypt)).Error);
        lab.Level = 2;
        var worker = sim.AddSurvivor(attributes: 60);
        worker.Set(Skill.Electronics, 30);
        Assert.True(sim.Execute(new AssignJob(worker.Id, lab.Id)).Ok);
        Assert.True(sim.Execute(new QueueRecipe(lab.Id, decrypt)).Ok);
        Assert.Equal(CommandError.MissingItems, sim.Execute(new QueueRecipe(lab.Id, decrypt)).Error);  // only one chip

        for (var i = 0; i < 200 && !Schematics.Schematics.Has(sim, "sch_signal_jammer"); i++) sim.Tick();

        Assert.True(Schematics.Schematics.Has(sim, "sch_signal_jammer"));
        Assert.Equal(0, sim.State.Inventory.Count("sch_signal_jammer"));
        Assert.Empty(lab.Queue);
    }

    [Fact]
    public void Grant_learns_at_once_and_pays_research_data_for_a_known_one()
    {
        var sim = LoopTestData.Colony();
        Assert.True(Schematics.Schematics.Grant(sim, "sch_cybernetics", SchematicSource.Story).Learned);
        var again = Schematics.Schematics.Grant(sim, "sch_cybernetics", SchematicSource.Story);
        Assert.Equal((ReceiveOutcome.Duplicate, 6.0), (again.Outcome, again.ResearchData));
        Assert.Equal(SchematicSource.Story, sim.State.Schematics.LearnedFrom["sch_cybernetics"]);
    }

    [Fact]
    public void Buildings_and_levels_outside_the_default_set_need_their_schematic()
    {
        var sim = LoopTestData.Colony(townHallLevel: 3);
        var shop = sim.Build("workshop");
        shop.Level = 2;
        sim.State.Stockpile.Add("wood", 500);
        sim.State.Stockpile.Add("stone", 500);
        sim.State.Stockpile.Add("scrap", 500);
        Assert.Equal(CommandError.SchematicNeeded, sim.Execute(new Upgrade(shop.Id)).Error);
        Assert.Equal(CommandError.SchematicNeeded, sim.Execute(new Construct("greenhouse")).Error);

        Schematics.Schematics.Grant(sim, "sch_workshop_3", SchematicSource.Debug);
        Schematics.Schematics.Grant(sim, "sch_greenhouse", SchematicSource.Debug);

        Assert.True(sim.Execute(new Upgrade(shop.Id)).Ok);
        Assert.True(sim.Execute(new Construct("greenhouse")).Ok);
        Assert.True(sim.Execute(new Construct("radio_mast")).Ok);
    }

    [Fact]
    public void Recipes_and_perks_wait_for_their_schematic()
    {
        var sim = LoopTestData.Colony(townHallLevel: 2);
        var tailor = sim.Build("tailor");
        tailor.Level = 2;
        Assert.Equal(CommandError.RecipeLocked, sim.CanQueue(tailor, sim.Data.Recipes["camo_cloak"]).Error);
        Schematics.Schematics.Grant(sim, "sch_camo_cloak", SchematicSource.Debug);
        Assert.True(sim.CanQueue(tailor, sim.Data.Recipes["camo_cloak"]).Ok);
        Assert.False(Schematics.Schematics.PerkUnlocked(sim, "override"));
    }
}

public class ResearchTests
{
    private static ColonySim Colony(int townHall = 2)
    {
        var sim = LoopTestData.Colony(townHallLevel: townHall, food: 1000);
        sim.Build("shelter");
        sim.State.Stockpile.Add("research_data", 30);
        return sim;
    }

    [Fact]
    public void Research_needs_to_have_heard_of_it_then_takes_data_and_a_researcher_s_time()
    {
        var sim = Colony();
        var r = sim.AddSurvivor(attributes: 60);
        Assert.Equal(CommandError.NotHeardOf, sim.Execute(new StartResearch("sch_rifle", r.Id)).Error);
        Assert.True(Schematics.Schematics.HearOf(sim, "sch_rifle", "in a test"));
        Assert.Equal(SchematicStatus.HeardOf, Schematics.Schematics.Status(sim, "sch_rifle"));

        Assert.True(sim.Execute(new StartResearch("sch_rifle", r.Id)).Ok);
        Assert.Equal(22, sim.State.Stockpile.Get("research_data"));
        Assert.Equal(CommandError.ResearchBusy, sim.Execute(new StartResearch("sch_camo_cloak", r.Id)).Error);
        var farm = sim.Build("farm");
        Assert.Equal(CommandError.IsResearching, sim.Execute(new AssignJob(r.Id, farm.Id)).Error);

        for (var i = 0; i < 24 * 30 && !Schematics.Schematics.Has(sim, "sch_rifle"); i++) sim.Tick();

        Assert.True(Schematics.Schematics.Has(sim, "sch_rifle"), string.Join("\n", sim.State.Events.TakeLast(8).Select(e => e.Message)) + $" {sim.State.Schematics.Research?.Progress}");
        Assert.Equal(SchematicSource.Research, sim.State.Schematics.LearnedFrom["sch_rifle"]);
        Assert.Null(sim.State.Schematics.Research);
    }

    [Fact]
    public void Tier_three_needs_town_hall_3_and_a_lab_2_and_tier_four_cannot_be_researched()
    {
        var sim = Colony();
        var r = sim.AddSurvivor();
        Schematics.Schematics.HearOf(sim, "sch_armory_3", "test");
        Schematics.Schematics.HearOf(sim, "sch_pulse_rifle", "test");
        Assert.Equal(CommandError.TownHallLevelTooLow, sim.Execute(new StartResearch("sch_armory_3", r.Id)).Error);
        sim.State.Buildings.Single(b => b.DefId == "town_hall").Level = 3;
        Assert.Equal(CommandError.NoStudyBuilding, sim.Execute(new StartResearch("sch_armory_3", r.Id)).Error);
        sim.Build("electronics_lab").Level = 2;
        Assert.Equal(CommandError.NotResearchable, sim.Execute(new StartResearch("sch_pulse_rifle", r.Id)).Error);
        Assert.True(sim.Execute(new StartResearch("sch_armory_3", r.Id)).Ok);
        Assert.True(sim.Execute(new CancelResearch()).Ok);
        Assert.Equal(30, sim.State.Stockpile.Get("research_data"));
    }
}

public class RadioTests
{
    private static ColonySim WithMast()
    {
        var sim = LoopTestData.Colony(townHallLevel: 2, food: 500);
        sim.Build("radio_mast");
        sim.State.Zones["suburb_ruins"] = new ZoneStatus { Intel = ZoneIntel.Scouted };
        return sim;
    }

    [Fact]
    public void The_trader_needs_a_mast_and_suburb_ruins_scouted_then_parks_with_unknown_schematics()
    {
        var none = LoopTestData.Colony(townHallLevel: 2);
        none.Build("radio_mast");
        Assert.False(RadioMast.TraderAvailable(none));

        var sim = WithMast();
        Assert.True(RadioMast.TraderAvailable(sim));
        var calls = new List<RadioCall>();
        sim.RadioCallReceived += calls.Add;
        for (var i = 0; i < 24 * 3 && !sim.State.Radio.TraderHere; i++) sim.Tick();

        Assert.True(sim.State.Radio.TraderHere);
        Assert.Equal(2, sim.State.Radio.Stock.Count);
        Assert.All(sim.State.Radio.Stock, id => Assert.True(sim.Data.Schematics.All[id].Tier <= 2));
        Assert.All(sim.State.Radio.Stock, id => Assert.Contains(id, sim.State.Schematics.HeardOf.Keys));
        Assert.Contains(calls, c => c.Kind == RadioCallKind.Trader);

        var id = sim.State.Radio.Stock[0];
        sim.State.Stockpile.Add("research_data", 20);
        var option = RadioMast.Prices(sim, sim.Data.Schematics.All[id]).ToList().FindIndex(p => p.Cost.ContainsKey("research_data"));
        Assert.True(sim.Execute(new BuyFromTrader(id, option)).Ok);
        Assert.NotEqual(SchematicStatus.Unknown, Schematics.Schematics.Status(sim, id));
        Assert.DoesNotContain(id, sim.State.Radio.Stock);
        Assert.True(sim.Execute(new SellToTrader(ResearchData: 2)).Ok);

        for (var i = 0; i < 24 * 3; i++) sim.Tick();
        Assert.False(sim.State.Radio.TraderHere);
        Assert.Equal(CommandError.NoTrader, sim.Execute(new SellToTrader(ResearchData: 1)).Error);
    }

    [Fact]
    public void Story_can_queue_calls_and_swap_the_trader()
    {
        var sim = WithMast();
        RadioCall? answered = null;
        sim.RadioCallAnswered += c => answered = c;
        var call = RadioMast.Enqueue(sim, RadioCallKind.Story, "marrow", "Abel Marrow", "You there?", tag: "m4_intro");
        Assert.Contains(call, RadioMast.Calls(sim));
        Assert.True(sim.Execute(new AnswerCall(call.Id)).Ok);
        Assert.Equal("m4_intro", answered!.Tag);
        Assert.Empty(RadioMast.Calls(sim));
        Assert.Throws<ArgumentException>(() => RadioMast.SetTrader(sim, "nobody"));
        RadioMast.SetTrader(sim, "caravan", unlocked: true);
        Assert.True(sim.State.Radio.TraderUnlocked);
    }

    [Fact]
    public void A_contract_pays_when_a_party_comes_home_from_its_zone()
    {
        var sim = WithMast();
        for (var i = 0; i < 24 * 8 && sim.State.Radio.Contract is null; i++) sim.Tick();
        var contract = sim.State.Radio.Contract!;
        Assert.True(sim.Execute(new AcceptContract(contract.Id)).Ok);
        var a = sim.AddSurvivor();
        sim.State.Stockpile.Add("food", 100);
        Assert.True(sim.Execute(new LaunchExpedition(contract.ZoneId, [new ExpeditionMember(a.Id)])).Ok, contract.ZoneId);
        for (var i = 0; i < 24 * 10 && sim.CurrentExpeditionPhase != ExpeditionPhase.InZone; i++) sim.Tick();
        var before = sim.State.Stockpile.Get("research_data");
        sim.Execute(new ApplyExtractionResult(LoopTestData.Result(returned: [LoopTestData.Returned(a)])));
        for (var i = 0; i < 24 * 10 && sim.State.Expedition is not null; i++) sim.Tick();
        Assert.Equal(1, sim.State.Radio.ContractsDone);
        Assert.True(sim.State.Stockpile.Get("research_data") > before);
    }

    [Fact]
    public void Radio_and_schematics_survive_a_save()
    {
        var sim = WithMast();
        for (var i = 0; i < 24 * 3; i++) sim.Tick();
        Schematics.Schematics.HearOf(sim, "sch_rifle", "test");
        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(TestData.Real, json, ScavengeTestData.Real);
        Assert.Equal(json, SaveGame.Save(loaded.State));
    }
}

public class SchematicDropTests
{
    private static ScavengeData Data => ScavengeTestData.Real;

    [Fact]
    public void Pools_hold_what_the_zone_s_tier_allows_and_known_ones_weigh_half()
    {
        var outskirts = Data.Zones["outskirts"];
        var pool = SchematicDrops.Pool(Data.Schematics, outskirts, DropSite.Cache, null);
        Assert.NotEmpty(pool);
        Assert.All(pool, p => Assert.True(p.Def.Tier == 1 && p.Def.Available && p.Def.Has(SchematicSource.Loot)));
        var terminal = SchematicDrops.Pool(Data.Schematics, Data.Zones["rail_depot"], DropSite.Terminal, null);
        Assert.All(terminal, p => Assert.True(p.Def.Has(SchematicSource.Terminal) && p.Def.Tier <= 3));
        Assert.Contains(terminal, p => p.Def.Tier == 3 && p.Weight == 2);

        var known = new HashSet<string> { pool[0].Def.Id };
        Assert.Equal(pool[0].Weight / 2, SchematicDrops.Pool(Data.Schematics, outskirts, DropSite.Cache, known)[0].Weight);
        Assert.Equal(0.25, SchematicDrops.Chance(Data.Schematics, outskirts, DropSite.Terminal));
        Assert.Equal(4, SchematicDrops.FindTier(Data.Zones["core_spire"]));
    }

    [Fact]
    public void A_kill_drops_at_most_one_schematic()
    {
        var warden = Data.Enemies["linked_warden"] with
        {
            Drops = [new() { Item = "sch_linked_carbine", Chance = 1 }, new() { Item = "sch_override", Chance = 1 }, new() { Item = "electronics", Chance = 1 }],
        };
        var drops = SchematicDrops.EnemyDrops(Data, warden, new DeterministicRng(3), null);
        Assert.Equal(["sch_linked_carbine", "electronics"], drops);
    }

    [Fact]
    public void Runs_find_schematics_at_about_the_rate_the_doc_expects()
    {
        // Caches and terminals over many seeds of the Suburb Ruins: some schematics, none above tier 2.
        var found = new List<string>();
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var party = new PartySetup(1, [new PartyMember(TestData.Survivor(50))]);
            var sim = NewScavenge.Create(Data, "suburb_ruins", seed, [party]);
            found.AddRange(sim.State.Caches.SelectMany(c => c.Items).Concat(sim.State.Devices.SelectMany(d => d.Items)).Where(id => Data.Schematics.Find(id) is not null));
        }
        Assert.InRange(found.Count, 10, 120);
        Assert.All(found, id => Assert.True(Data.Schematics.All[id].Tier <= 2));
    }
}

public class SchematicMigrationTests
{
    [Fact]
    public void Version_14_story_saves_hand_the_campaign_s_schematics_to_the_module()
    {
        var sim = StoryTestData.NewStory();
        var node = JsonNode.Parse(SaveGame.Save(sim.State))!;
        node["version"] = 14;
        var colony = node["colony"]!.AsObject();
        colony.Remove("schematics");
        colony.Remove("radio");
        colony["campaign"]!["schematics"] = new JsonArray("sch_watchtower");
        colony["campaign"]!["flags"]!.AsObject()["research_data"] = 3;

        var loaded = SaveGame.Load(sim.Data, node.ToJsonString(), sim.Scavenge);

        Assert.Equal(SchematicSource.Story, loaded.State.Schematics.LearnedFrom["sch_watchtower"]);
        Assert.Equal(3, loaded.State.Stockpile.Get("research_data"));
        Assert.Equal(0, loaded.State.Campaign!.Flag("research_data"));
    }

    [Fact]
    public void Version_12_saves_keep_what_they_could_do_and_rename_blueprints()
    {
        var sim = LoopTestData.Colony(townHallLevel: 3);
        var shop = sim.Build("workshop");
        shop.Level = 3;
        var tailor = sim.Build("tailor");
        tailor.Level = 2;
        var node = JsonNode.Parse(SaveGame.Save(sim.State))!;
        node["version"] = 12;
        var colony = node["colony"]!.AsObject();
        colony.Remove("schematics");
        colony.Remove("radio");
        colony["knownBlueprints"] = new JsonArray("bp_scanner", "ds_override");
        colony["inventory"]!["counts"]!.AsObject()["bp_rifle"] = 1;
        colony["inventory"]!["counts"]!.AsObject()["bp_scanner"] = 1;
        colony["inventory"]!["counts"]!.AsObject()["bp_cybernetics"] = 1;

        var loaded = SaveGame.Load(TestData.Real, node.ToJsonString(), ScavengeTestData.Real);
        var known = loaded.State.Schematics.Known;

        Assert.True(loaded.State.Schematics.Migrated);
        Assert.Contains("sch_scanner", known);
        Assert.Contains("sch_override", known);
        Assert.Contains("sch_workshop_3", known);
        Assert.Contains("sch_camo_cloak", known);
        Assert.Contains("sch_grappling_hook", known);
        Assert.Contains("sch_rifle", known);                      // a plain blueprint in stock is learned
        Assert.Equal(4, loaded.State.Stockpile.Get("research_data"));  // the known scanner copy: tier 2 → 4
        Assert.Equal(1, loaded.State.Inventory.Count("sch_cybernetics"));  // encrypted: waits for a Decrypt
        Assert.DoesNotContain("sch_tailor_3", known);
    }
}

public class SchematicTradeTests
{
    [Fact]
    public void A_known_schematic_can_be_copied_for_research_data()
    {
        var sim = LoopTestData.Colony();
        Assert.Equal(CommandError.SchematicNeeded, sim.Execute(new CopySchematic("sch_rifle")).Error);
        Schematics.Schematics.Grant(sim, "sch_rifle", SchematicSource.Debug);
        Assert.Equal(CommandError.CannotAfford, sim.Execute(new CopySchematic("sch_rifle")).Error);
        sim.State.Stockpile.Add("research_data", 2);
        Assert.True(sim.Execute(new CopySchematic("sch_rifle")).Ok);
        Assert.Equal(1, sim.State.Inventory.Count("sch_rifle"));
        Assert.Equal(0, sim.State.Stockpile.Get("research_data"));
    }

    [Fact]
    public void Trade_values_come_from_the_price_table()
    {
        var data = TestData.Real;
        var items = ScavengeTestData.Real;
        Assert.Equal(30, TradeValues.Of(data, items, "sch_camo_cloak"));    // tier 1: 30 scrap is the cheapest option
        Assert.Equal(1, TradeValues.OfResource(data, "scrap"));
        Assert.True(TradeValues.Of(data, items, "rifle") > TradeValues.Of(data, items, "pipe"));
        Assert.True(TradeValues.Of(data, items, "pipe@fine") > TradeValues.Of(data, items, "pipe"));
    }
}
