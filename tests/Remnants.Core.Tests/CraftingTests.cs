using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class CraftingDataTests
{
    [Fact]
    public void Every_item_has_a_source_in_a_recipe_loot_or_enemy_drops()
    {
        var made = TestData.Real.Recipes.Values.Where(r => r.Item is not null).Select(r => r.Item!)
            .Concat(ScavengeTestData.Real.Enemies.Values.SelectMany(e => e.Drops).Select(d => d.Item)).ToHashSet();
        var orphans = ScavengeTestData.Real.Items.Values
            .Where(i => i.BaseId is null && !i.Natural && i.LootWeight <= 0 && !made.Contains(i.Id))
            .Select(i => i.Id).ToList();
        Assert.Empty(orphans);
    }

    [Fact]
    public void Every_blueprint_unlocks_a_recipe_or_perk_and_new_buildings_have_recipes()
    {
        foreach (var bp in ScavengeTestData.Real.Items.Values.Where(i => i.Kind == ItemKind.Blueprint))
            Assert.True(TestData.Real.Recipes.Values.Any(r => r.Blueprint == bp.Id) || TestData.Real.Perks.Values.Any(p => p.Requires.Blueprint == bp.Id),
                $"{bp.Id} unlocks nothing");
        foreach (var id in new[] { "workshop", "tailor", "electronics_lab", "armory", "kitchen" })
            Assert.NotEmpty(TestData.Real.RecipesFor(id));
        Assert.Equal(3, TestData.Real.Buildings["house"].MaxLevel);
    }

    [Fact]
    public void The_real_data_links_check_out()
    {
        // Constructing a sim with item data cross-checks recipes, blueprints and quality tiers.
        _ = new ColonySim(TestData.Real, new ColonyState(), ScavengeTestData.Real);
    }

    [Fact]
    public void Gear_comes_in_quality_variants_with_scaled_stats()
    {
        var items = ScavengeTestData.Real.Items;
        var fine = items["riot_armor@fine"];
        var crude = items["pipe@crude"];
        Assert.Equal("Fine Riot Armor", fine.Name);
        Assert.Equal("riot_armor", fine.DefinedId);
        Assert.True(fine.Armor > items["riot_armor"].Armor);
        Assert.True(fine.Weight < items["riot_armor"].Weight);
        Assert.True(crude.Actions[0].MaxDamage < items["pipe"].Actions[0].MaxDamage);
        Assert.Equal(0, fine.LootWeight);
        Assert.DoesNotContain("bandage@fine", items.Keys);  // only equippable gear has quality
        Assert.DoesNotContain("husk_claws@fine", items.Keys);
    }

    [Fact]
    public void A_recipe_for_an_unknown_building_is_rejected()
    {
        var ex = Assert.Throws<GameDataException>(() => GameDataLoader.LoadFromJson(
            """[{ "id": "wood" }, { "id": "food" }]""", "[]", """[{ "id": "town_hall", "levels": [{}] }]""",
            """{ "newGame": { "maleNames": ["A"], "femaleNames": ["B"] } }""",
            """[{ "id": "x", "building": "nowhere", "resource": "wood" }]"""));
        Assert.Contains("nowhere", ex.Message);
    }
}

public class ProductionQueueTests
{
    private static (ColonySim Sim, Building Shop, Survivor Worker) Shop(string defId = "tailor", int level = 1, double skill = 0)
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 1000);
        var shop = sim.Build(defId);
        shop.Level = level;
        var worker = sim.AddSurvivor(attributes: 60);
        var job = sim.Data.Jobs[sim.Data.Buildings[defId].Job!];
        worker.Set(job.Skill, skill);
        Assert.True(sim.Execute(new AssignJob(worker.Id, shop.Id)).Ok);
        return (sim, shop, worker);
    }

    private static void RunUntilDone(ColonySim sim, Building shop, int maxTicks = 2000)
    {
        for (var i = 0; i < maxTicks && shop.Queue.Count > 0; i++) sim.Tick();
    }

    [Fact]
    public void A_counted_order_makes_its_batches_then_leaves_the_queue()
    {
        var (sim, shop, worker) = Shop();
        sim.State.Stockpile.Add("cloth", 10);
        var result = sim.Execute(new QueueRecipe(shop.Id, "rag_coat", 3));
        Assert.True(result.Ok, result.Message);

        RunUntilDone(sim, shop);

        Assert.Empty(shop.Queue);
        Assert.Equal(3, sim.State.Inventory.Counts.Where(kv => kv.Key.StartsWith("rag_coat")).Sum(kv => kv.Value));
        Assert.Equal(1, sim.State.Stockpile.Get("cloth"), 6);
        Assert.True(worker.Get(Skill.Tailoring) > 0);
        Assert.True(worker.Get(AttributeKind.Creativity) > 60);  // tailoring trains Creativity and Agility
    }

    [Fact]
    public void Orders_run_in_turn_and_skip_one_that_cannot_start()
    {
        var (sim, shop, _) = Shop();
        sim.State.Stockpile.Add("cloth", 1);
        Assert.True(sim.Execute(new QueueRecipe(shop.Id, "padded_vest")).Ok);  // needs 5 cloth: waits
        Assert.True(sim.Execute(new QueueRecipe(shop.Id, "bandage")).Ok);      // needs 1 cloth: goes first

        RunUntilDone(sim, shop, 50);

        Assert.Equal(2, sim.State.Inventory.Count("bandage"));
        Assert.Equal("padded_vest", Assert.Single(shop.Queue).RecipeId);
        Assert.Equal(CraftStatus.WaitingForInputs, sim.CraftStatus(shop));
    }

    [Fact]
    public void A_repeating_order_goes_to_the_back_after_each_batch()
    {
        var (sim, shop, _) = Shop();
        sim.State.Stockpile.Add("cloth", 20);
        var repeat = sim.Execute(new QueueRecipe(shop.Id, "bandage", Repeat: true)).EntityId;
        var coat = sim.Execute(new QueueRecipe(shop.Id, "rag_coat")).EntityId;

        for (var i = 0; i < 200 && sim.State.Inventory.Count("bandage") == 0; i++) sim.Tick();

        Assert.Equal([coat!.Value, repeat!.Value], shop.Queue.Select(o => o.Id));
    }

    [Fact]
    public void Cancelling_refunds_a_paid_batch_including_items()
    {
        var (sim, shop, _) = Shop("armory", level: 3);
        sim.State.KnownBlueprints.Add("bp_linked_plating");
        sim.State.Stockpile.Add("scrap", 12);
        sim.State.Stockpile.Add("electronics", 3);
        sim.State.Inventory.Add("padded_vest@crude");
        var order = sim.Execute(new QueueRecipe(shop.Id, "linked_plating")).EntityId!.Value;
        sim.Tick();
        Assert.Equal(0, sim.State.Inventory.Count("padded_vest@crude"));

        Assert.True(sim.Execute(new CancelOrder(shop.Id, order)).Ok);

        Assert.Empty(shop.Queue);
        Assert.Equal(12, sim.State.Stockpile.Get("scrap"), 6);
        Assert.Equal(1, sim.State.Inventory.Count("padded_vest@crude"));
        Assert.Equal(CommandError.UnknownOrder, sim.Execute(new CancelOrder(shop.Id, order)).Error);
    }

    [Fact]
    public void Queueing_is_validated()
    {
        var (sim, shop, _) = Shop("armory");
        var data = sim.Data.Rules.Crafting;
        Assert.Equal(CommandError.UnknownRecipe, sim.Execute(new QueueRecipe(shop.Id, "rag_coat")).Error);  // a tailor recipe
        Assert.Equal(CommandError.RecipeLevelTooLow, sim.Execute(new QueueRecipe(shop.Id, "pistol")).Error);
        shop.Level = 2;
        Assert.Equal(CommandError.RecipeLocked, sim.Execute(new QueueRecipe(shop.Id, "riot_armor")).Error);
        Assert.Equal(CommandError.InvalidCount, sim.Execute(new QueueRecipe(shop.Id, "machete", 0)).Error);
        Assert.Equal(CommandError.InvalidCount, sim.Execute(new QueueRecipe(shop.Id, "machete", data.MaxBatches + 1)).Error);
        for (var i = 0; i < data.MaxQueue; i++) Assert.True(sim.Execute(new QueueRecipe(shop.Id, "machete")).Ok);
        Assert.Equal(CommandError.QueueFull, sim.Execute(new QueueRecipe(shop.Id, "machete")).Error);
    }

    [Fact]
    public void A_recipe_needing_more_skill_waits_for_a_skilled_worker()
    {
        var (sim, shop, worker) = Shop("tailor", level: 2, skill: 0);
        sim.State.Stockpile.Add("cloth", 10);
        sim.State.Stockpile.Add("scrap", 5);
        Assert.True(sim.Execute(new QueueRecipe(shop.Id, "scout_jacket")).Ok);
        sim.Advance(5);
        Assert.Equal(CraftStatus.NeedsSkill, sim.CraftStatus(shop));
        Assert.False(shop.Queue[0].Paid);

        worker.Set(Skill.Tailoring, 40);
        RunUntilDone(sim, shop);
        Assert.Equal(1, sim.State.Inventory.Counts.Where(kv => kv.Key.StartsWith("scout_jacket")).Sum(kv => kv.Value));
    }

    [Fact]
    public void Workers_practise_while_every_order_needs_more_skill_and_then_make_it()
    {
        // A fresh colony has no skills: an Armory with only a Pistol (Firearms 15) queued must not stall forever.
        var (sim, shop, worker) = Shop("armory", level: 2, skill: 0);
        sim.Build("shelter"); // practice takes days; the worker needs a bed meanwhile
        sim.State.Stockpile.Add("scrap", 20);
        sim.State.Stockpile.Add("electronics", 2);
        Assert.True(sim.Execute(new QueueRecipe(shop.Id, "pistol")).Ok);
        var gap = sim.SkillGap(shop)!;
        Assert.Equal((Skill.Firearms, 0.0, 15.0), (gap.Skill, gap.Best, gap.Needed));
        Assert.NotNull(gap.Ticks);

        sim.Advance(gap.Ticks!.Value - 1);
        Assert.Equal(CraftStatus.NeedsSkill, sim.CraftStatus(shop));
        Assert.True(worker.Get(Skill.Firearms) > 10);
        sim.Advance(2);
        Assert.Null(sim.SkillGap(shop));
        RunUntilDone(sim, shop);
        Assert.Equal(1, sim.State.Inventory.Counts.Where(kv => kv.Key.StartsWith("pistol")).Sum(kv => kv.Value));
    }

    [Fact]
    public void New_production_buildings_need_the_town_hall_level()
    {
        var sim = LoopTestData.Colony(townHallLevel: 1);
        foreach (var (res, amount) in sim.Data.Buildings["armory"].Level(1).Cost) sim.State.Stockpile.Add(res, amount);
        Assert.Equal(CommandError.TownHallLevelTooLow, sim.Execute(new Construct("armory")).Error);
        sim.State.Buildings[0].Level = 2;
        Assert.True(sim.Execute(new Construct("armory")).Ok);
    }

    [Fact]
    public void A_house_gives_more_beds_than_a_shelter()
    {
        var data = TestData.Real.Buildings;
        Assert.True(data["house"].Level(1).Beds > data["shelter"].Level(1).Beds);
        var sim = LoopTestData.Colony(townHallLevel: 2);
        sim.Build("house");
        Assert.Equal(data["house"].Level(1).Beds, sim.BedCapacity);
    }
}

public class QualityTests
{
    private static ColonySim Armory(double skill, int level, out Building shop, ulong seed = 1)
    {
        var state = new ColonyState { RngState = new Random.DeterministicRng(seed).State };
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = 3 });
        var sim = new ColonySim(TestData.Real, state, ScavengeTestData.Real);
        shop = sim.Build("armory");
        shop.Level = level;
        var w = sim.AddSurvivor(attributes: 60);
        w.Set(Skill.Firearms, skill);
        Assert.True(sim.Execute(new AssignJob(w.Id, shop.Id)).Ok);
        return sim;
    }

    private static Dictionary<string, double> Chances(ColonySim sim, Building shop) =>
        sim.QualityChances(shop, sim.Data.Recipes["machete"]).ToDictionary(c => c.Tier, c => c.Chance);

    [Fact]
    public void Skill_and_building_level_shift_the_quality_odds()
    {
        var novice = Chances(Armory(0, 1, out var a), a);
        var master = Chances(Armory(90, 3, out var b), b);
        Assert.Equal(1, novice.Values.Sum(), 6);
        Assert.Equal(1, master.Values.Sum(), 6);
        Assert.True(novice["crude"] > 0.5);
        Assert.Equal(0, novice["fine"], 6);
        Assert.Equal(1, master["fine"], 6);
        Assert.Empty(Armory(0, 1, out var c).QualityChances(c, TestData.Real.Recipes["herbal_remedy"]));  // resources have no quality
    }

    [Fact]
    public void A_master_makes_fine_gear_and_a_novice_mostly_crude()
    {
        string Make(double skill, int level, ulong seed)
        {
            var sim = Armory(skill, level, out var shop, seed);
            sim.State.Stockpile.Add("scrap", 6);
            sim.State.Stockpile.Add("wood", 1);
            Assert.True(sim.Execute(new QueueRecipe(shop.Id, "machete")).Ok);
            for (var i = 0; i < 500 && shop.Queue.Count > 0; i++) sim.Tick();
            return Assert.Single(sim.State.Inventory.Counts).Key;
        }

        Assert.Equal("machete@fine", Make(90, 3, 1));
        var novice = Enumerable.Range(1, 20).Select(s => Make(0, 1, (ulong)s)).ToList();
        Assert.True(novice.Count(id => id == "machete@crude") > 10);
        Assert.DoesNotContain("machete@fine", novice);
    }

    [Fact]
    public void Crafted_gear_feeds_the_field_profile()
    {
        var plain = ProfileDeriver.Derive(ScavengeTestData.Real, SurvivorUnit(body: "riot_armor"));
        var fine = ProfileDeriver.Derive(ScavengeTestData.Real, SurvivorUnit(body: "riot_armor@fine", weapon: "linked_carbine@fine"));
        Assert.Equal(MovePattern.King, fine.Movement.Primary.Pattern);  // still heavy armor
        Assert.Equal(1, fine.Movement.Primary.Range);
        Assert.True(fine.Armor > plain.Armor);
        Assert.True(fine.Action("burst")!.MaxDamage > ProfileDeriver.Derive(ScavengeTestData.Real, SurvivorUnit(weapon: "linked_carbine")).Action("burst")!.MaxDamage);
        var climber = ProfileDeriver.Derive(ScavengeTestData.Real, SurvivorUnit(tool: "climbing_gear@crude"));
        Assert.Equal(1, climber.Movement.Cost(Terrain.Rubble));
    }
}

public class BlueprintTests
{
    [Fact]
    public void Studying_a_blueprint_uses_it_up_and_unlocks_its_recipes()
    {
        var sim = LoopTestData.Colony(townHallLevel: 2, items: new() { ["bp_riot_armor"] = 1 });
        var armory = sim.Build("armory");
        armory.Level = 2;
        Assert.Equal(CommandError.RecipeLocked, sim.CanQueue(armory, sim.Data.Recipes["riot_armor"]).Error);

        var result = sim.Execute(new StudyBlueprint("bp_riot_armor"));

        Assert.True(result.Ok, result.Message);
        Assert.Contains("Riot Armor", result.Message);
        Assert.Contains("bp_riot_armor", sim.State.KnownBlueprints);
        Assert.Equal(0, sim.State.Inventory.Count("bp_riot_armor"));
        Assert.True(sim.Execute(new QueueRecipe(armory.Id, "riot_armor")).Ok);
    }

    [Fact]
    public void Studying_is_validated()
    {
        var sim = LoopTestData.Colony(townHallLevel: 2, items: new() { ["bp_scanner"] = 2, ["pipe"] = 1 });
        Assert.Equal(CommandError.UnknownItem, sim.Execute(new StudyBlueprint("bp_laser")).Error);
        Assert.Equal(CommandError.NotABlueprint, sim.Execute(new StudyBlueprint("pipe")).Error);
        Assert.Equal(CommandError.MissingItems, sim.Execute(new StudyBlueprint("bp_rifle")).Error);
        Assert.Equal(CommandError.NoStudyBuilding, sim.Execute(new StudyBlueprint("bp_scanner")).Error);  // needs an Electronics Lab
        sim.Build("electronics_lab");
        Assert.True(sim.Execute(new StudyBlueprint("bp_scanner")).Ok);
        Assert.Equal(CommandError.AlreadyKnown, sim.Execute(new StudyBlueprint("bp_scanner")).Error);
    }

    [Fact]
    public void Blueprints_turn_up_in_higher_tier_zone_loot()
    {
        var items = ScavengeTestData.Real.Items.Values;
        Assert.DoesNotContain(items, i => i.Kind == ItemKind.Blueprint && i.LootWeight > 0 && i.Tier <= ScavengeTestData.Real.Zones["outskirts"].LootTier);
        Assert.Contains(items, i => i.Kind == ItemKind.Blueprint && i.LootWeight > 0 && i.Tier <= ScavengeTestData.Real.Zones["suburb_ruins"].LootTier);
    }
}

public class KitchenTests
{
    [Fact]
    public void Meals_stretch_food_and_lift_mood()
    {
        var sim = LoopTestData.Colony(food: 0);
        sim.Build("shelter");
        var s = sim.AddSurvivor();
        s.Mood = 50;
        var meal = sim.Data.Rules.Food.Meals.Single(m => m.Resource == "meal");
        sim.State.Stockpile.Add("meal", 1);

        sim.Tick();

        Assert.Equal(1 - sim.Data.Rules.Food.AdultPerTick / meal.FoodValue, sim.State.Stockpile.Get("meal"), 6);
        Assert.Equal(0, s.StarvingTicks);
        Assert.True(s.Mood > 50 + sim.Data.Rules.Mood.RecoveryPerTick - 1e-9);
    }

    [Fact]
    public void The_kitchen_cooks_more_food_value_than_it_uses()
    {
        var recipe = TestData.Real.Recipes["simple_meal"];
        var value = TestData.Real.Rules.Food.Meals.Single(m => m.Resource == recipe.Resource).FoodValue;
        Assert.True(recipe.Amount * value > recipe.Inputs["food"]);

        var sim = LoopTestData.Colony(food: 100);
        var kitchen = sim.Build("kitchen");
        var cook = sim.AddSurvivor(attributes: 60);
        Assert.True(sim.Execute(new AssignJob(cook.Id, kitchen.Id)).Ok);
        Assert.True(sim.Execute(new QueueRecipe(kitchen.Id, "simple_meal")).Ok);
        for (var i = 0; i < 100 && kitchen.Queue.Count > 0; i++) sim.Tick();
        Assert.True(sim.State.Stockpile.Get("meal") > 0);
        Assert.True(cook.Get(Skill.Cooking) > 0);
    }
}

public class DeviceTests
{
    [Fact]
    public void A_jammer_keeps_linked_from_taking_cover()
    {
        var u = SurvivorUnit(pos: new(2, 2), tool: "signal_jammer");
        var linked = EnemyUnit("linked", new(6, 2));
        var sim = Arena(9, 5, [u, linked], [
            ".........",
            ".........",
            ".........",
            "......c..",
            ".........",
        ]);
        Assert.True(Devices.IsJammed(sim, linked));
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.Equal(new Pos(6, 2), linked.Pos);  // shot from where it stood
        Assert.Contains(sim.State.Events, e => e.UnitId == linked.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void Jammers_reach_only_their_radius_and_scanners_cover_theirs()
    {
        var u = SurvivorUnit(pos: new(0, 2), tool: "signal_jammer");
        var far = EnemyUnit("linked", new(8, 2));
        var sim = Arena(12, 5, [u, far]);
        Assert.False(Devices.IsJammed(sim, far));

        var scout = SurvivorUnit(pos: new(5, 2), tool: "scanner");
        var sim2 = Arena(12, 5, [scout]);
        Assert.Contains(new Pos(11, 2), Devices.ScannedBy(sim2, scout));
        Assert.True(ProfileDeriver.Derive(ScavengeTestData.Real, scout).Sight > ProfileDeriver.Derive(ScavengeTestData.Real, SurvivorUnit()).Sight);
    }
}

public class CraftingSaveTests
{
    [Fact]
    public void Queues_and_blueprints_survive_a_save()
    {
        var sim = LoopTestData.Colony(townHallLevel: 2);
        var tailor = sim.Build("tailor");
        sim.State.KnownBlueprints.Add("bp_scanner");
        sim.Execute(new QueueRecipe(tailor.Id, "rag_coat", 2));
        var loaded = SaveGame.Load(TestData.Real, SaveGame.Save(sim.State), ScavengeTestData.Real);
        Assert.Equal("rag_coat", Assert.Single(loaded.State.FindBuilding(tailor.Id)!.Queue).RecipeId);
        Assert.Contains("bp_scanner", loaded.State.KnownBlueprints);
        Assert.Equal(SaveGame.Save(sim.State), SaveGame.Save(loaded.State));
    }

    [Fact]
    public void Version_3_saves_migrate_recipes_and_old_blueprints()
    {
        var sim = LoopTestData.Colony(townHallLevel: 2);
        var shop = sim.Build("workshop");
        var node = System.Text.Json.Nodes.JsonNode.Parse(SaveGame.Save(sim.State))!;
        node["version"] = 3;
        var colony = node["colony"]!.AsObject();
        colony.Remove("knownBlueprints");
        colony.Remove("nextOrderId");
        colony["inventory"]!["counts"]!.AsObject()["blueprint"] = 2;
        var b = colony["buildings"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == shop.Id)!.AsObject();
        b.Remove("queue");
        b["recipeId"] = "peg_leg";
        b["craftProgress"] = 4.5;
        b["craftPaid"] = true;

        var loaded = SaveGame.Load(TestData.Real, node.ToJsonString(), ScavengeTestData.Real);

        var order = Assert.Single(loaded.State.FindBuilding(shop.Id)!.Queue);
        Assert.Equal(("peg_leg", true, 4.5, true), (order.RecipeId, order.Repeat, order.Progress, order.Paid));
        Assert.Equal(2, loaded.State.Inventory.Count(SaveGame.LegacyBlueprintItem));
        Assert.True(loaded.Execute(new QueueRecipe(shop.Id, "pipe")).EntityId > order.Id);
    }
}
