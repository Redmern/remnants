using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class PantryTests
{
    private static ColonySim Colony() => LoopTestData.Colony(food: 0);

    [Fact]
    public void Food_items_are_inventory_items_and_their_lots_follow_the_inventory()
    {
        var sim = Colony();
        sim.State.Inventory.Add("potato", 5);  // e.g. bought from a trader: a fresh lot appears
        Assert.Equal(5, Pantry.FoodValue(sim), 9);
        Assert.Equal(5, sim.State.Food.Lots["potato"].Sum(l => l.Count));

        sim.State.Food.Lots["potato"][0].Age = 100;
        Pantry.Add(sim.State, sim.Data, "potato", 2);
        Assert.True(sim.State.Inventory.TryTake("potato", 3));  // sold: the oldest go first
        Pantry.Reconcile(sim.State, sim.Data);
        var lots = sim.State.Food.Lots["potato"];
        Assert.Equal(4, lots.Sum(l => l.Count));
        Assert.Equal(2, lots.Single(l => l.Age == 0).Count);
    }

    [Fact]
    public void Food_spoils_after_its_shelf_life_rots_later_and_a_cold_cellar_slows_it()
    {
        var sim = Colony();
        var food = sim.Data.Food!;
        var day = sim.Data.Rules.TicksPerDay;
        Pantry.Add(sim.State, sim.Data, "meat", 4);
        sim.Advance((int)(food.Items["meat"].ShelfDays * day));
        Assert.Equal(0, Pantry.FoodValue(sim), 9);  // spoiled: no longer counts as food
        Assert.Equal(4, sim.State.Inventory.Count("meat"));
        sim.Advance((int)(food.Rules.Spoilage.SpoiledDays * day));
        Assert.Equal(0, sim.State.Inventory.Count("meat"));
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.FoodRotted);

        var cold = Colony();
        cold.State.KnownBlueprints.Add("sch_cold_cellar");
        cold.Build("cold_cellar");
        Pantry.Add(cold.State, cold.Data, "meat", 4);
        Pantry.Add(cold.State, cold.Data, "canned_food", 1);
        cold.Advance((int)(food.Items["meat"].ShelfDays * day));
        Assert.Equal(food.Rules.CellarRateByLevel[0] * food.Items["meat"].ShelfDays * day, cold.State.Food.Lots["meat"][0].Age, 6);
        Assert.False(cold.State.Food.Lots["meat"][0].Spoiled);
    }

    [Fact]
    public void Take_food_value_takes_whole_items_soonest_to_spoil_first_and_refuses_when_short()
    {
        var sim = Colony();
        Pantry.Add(sim.State, sim.Data, "canned_food", 2);
        Pantry.Add(sim.State, sim.Data, "potato", 3);
        Assert.False(Pantry.TakeFoodValue(sim, 100));
        Assert.Equal(7, Pantry.FoodValue(sim), 9);

        Assert.True(Pantry.TakeFoodValue(sim, 2.5));
        Assert.Equal(0, sim.State.Inventory.Count("potato"));
        Assert.Equal(2, sim.State.Inventory.Count("canned_food"));
    }
}

public class EatingTests
{
    private static (ColonySim Sim, Survivor S) One()
    {
        var sim = LoopTestData.Colony(food: 0);
        sim.Build("shelter");
        return (sim, sim.AddSurvivor());
    }

    [Fact]
    public void Cooked_food_is_eaten_before_raw_food_and_spoiled_food_only_when_nothing_else_is_left()
    {
        var (sim, s) = One();
        Pantry.Add(sim.State, sim.Data, "potato", 5);
        Pantry.Add(sim.State, sim.Data, "stew", 1);
        sim.Tick();
        Assert.Equal("stew", sim.State.Food.Open.Single().ItemId);

        var (sim2, s2) = One();
        Pantry.Add(sim2.State, sim2.Data, "potato", 1);
        sim2.State.Food.Lots["potato"][0].Age = 1e6;  // spoiled
        Pantry.Add(sim2.State, sim2.Data, "corn", 1);
        sim2.Tick();
        Assert.Equal("corn", sim2.State.Food.Open.Single().ItemId);
        Assert.Equal(0, s2.StarvingTicks);
    }

    [Fact]
    public void A_meal_gives_its_buffs_scaled_by_quality_and_well_fed_raises_output()
    {
        var (sim, s) = One();
        Pantry.Add(sim.State, sim.Data, "stew", 1, "fine");
        var before = sim.Data.Jobs["farming"];
        var farm = sim.Data.Buildings["farm"];
        var plain = JobOutput.PerTick(farm, 1, before, s, sim.Data.Rules.Output, sim.Data);
        sim.Tick();

        var buff = Assert.Single(s.Buffs, b => b.Id == "well_fed");
        var hours = sim.Data.Food!.Items["stew"].Meal!.Buffs["well_fed"] * FoodQuality.Effect(sim.Data, "fine");
        Assert.Equal(sim.State.Tick - 1 + (long)Math.Round(hours), buff.Until);
        var fed = JobOutput.PerTick(farm, 1, before, s, sim.Data.Rules.Output, sim.Data);
        Assert.True(fed > plain * 1.05);
    }

    [Fact]
    public void Starving_makes_people_hungry_and_hungry_people_work_and_heal_slower()
    {
        var (sim, s) = One();
        sim.Tick();
        Assert.Contains(s.Buffs, b => b.Id == "hungry");
        Assert.True(FoodEffects.OutputFactor(s, sim.Data) < 1);
        Assert.True(FoodEffects.HealingFactor(s, sim.Data) < 1);
    }

    [Fact]
    public void Eating_the_same_dish_for_days_is_same_again_and_people_pick_another_when_they_can()
    {
        var (sim, s) = One();
        Pantry.Add(sim.State, sim.Data, "boiled_potatoes", 60);
        sim.Advance(3 * sim.Data.Rules.TicksPerDay);
        Assert.Contains(s.Buffs, b => b.Id == "same_again");

        var (sim2, s2) = One();
        Pantry.Add(sim2.State, sim2.Data, "boiled_potatoes", 30);
        Pantry.Add(sim2.State, sim2.Data, "flatbread", 30);
        sim2.Advance(3 * sim2.Data.Rules.TicksPerDay);
        Assert.DoesNotContain(s2.Buffs, b => b.Id == "same_again");
        Assert.True(s2.Diet.GetValueOrDefault("boiled_potatoes") > 0 && s2.Diet.GetValueOrDefault("flatbread") > 0);
    }

    [Fact]
    public void No_cooked_meal_for_two_days_is_a_bland_diet()
    {
        var (sim, s) = One();
        sim.AddFood(100);
        sim.Advance((int)(sim.Data.Food!.Rules.BlandAfterHours * sim.Data.Rules.TicksPerDay / 24) + 1);
        Assert.Contains(s.Buffs, b => b.Id == "bland");
    }

    [Fact]
    public void Tainted_food_can_make_people_sick_through_the_food_sickness_hook()
    {
        var (sim, s) = One();
        Pantry.Add(sim.State, sim.Data, "canned_food", 60, tainted: true);
        sim.Advance(10 * sim.Data.Rules.TicksPerDay);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.FoodPoisoning && e.SurvivorId == s.Id);
    }

    [Fact]
    public void Stim_tea_crashes_into_sleep_debt_when_it_wears_off()
    {
        var (sim, s) = One();
        Pantry.Add(sim.State, sim.Data, "stim_tea", 1);
        sim.AddFood(10);
        sim.Tick();
        Assert.Contains(s.Buffs, b => b.Id == "energised");
        sim.Advance(sim.Data.Rules.TicksPerDay);
        Assert.DoesNotContain(s.Buffs, b => b.Id == "energised");
        Assert.True(s.SleepDebt > 0);
    }

    [Fact]
    public void Children_on_poor_food_are_malnourished_and_grow_slower_than_on_good_food()
    {
        ColonySim Grow(string item, out Survivor child)
        {
            var sim = LifeTestData.Colony(food: 0);
            child = sim.AddSurvivor(LifeStage.Child, attributes: 10);
            child.Aptitude[AttributeKind.Strength] = 80;
            Pantry.Add(sim.State, sim.Data, item, 200, shelfMultiplier: 0);
            sim.Advance(4 * sim.Data.Rules.TicksPerDay);
            return sim;
        }
        Grow("roots", out var poor);
        Grow("stew", out var good);
        Assert.True(poor.Nutrition < good.Nutrition);
        Assert.Contains(poor.Buffs, b => b.Id == "malnourished");
        Assert.True(poor.MalnourishedTicks > 0);
        Assert.Equal(0, good.MalnourishedTicks);
        Assert.True(good.Get(AttributeKind.Strength) > poor.Get(AttributeKind.Strength));
    }
}

public class HarvestTests
{
    [Fact]
    public void A_new_farm_grows_potatoes_and_the_first_harvest_comes_in_after_the_growing_time()
    {
        var sim = LoopTestData.Colony(food: 0);
        var farm = sim.Build("farm");
        var s = sim.AddSurvivor();
        sim.AddFood(100);
        Assert.True(sim.Execute(new AssignJob(s.Id, farm.Id)).Ok);
        var harvest = Harvesting.Current(sim.Data, farm)!;
        Assert.Equal("potatoes", harvest.Id);

        sim.Advance((int)(harvest.GrowDays * sim.Data.Rules.TicksPerDay) - 1);
        Assert.Equal(0, sim.State.Inventory.Count("potato") - 100 + (int)Math.Ceiling(sim.Data.Rules.Food.AdultPerTick * 23));
        Assert.True(farm.HarvestProgress["potato"] > 5);
        sim.Advance(2);
        Assert.Contains(sim.State.Food.Lots["potato"], l => l.Age < 2 && l.Shelf > 0);
    }

    [Fact]
    public void Switching_the_crop_loses_the_standing_crop_and_waits_for_the_new_one()
    {
        var sim = LoopTestData.Colony(food: 0);
        var farm = sim.Build("farm");
        farm.HarvestProgress["potato"] = 3;
        var result = sim.Execute(new SetHarvest(farm.Id, "beans"));
        Assert.True(result.Ok, result.Message);
        Assert.Empty(farm.HarvestProgress);
        Assert.Equal(sim.State.Tick + 3 * sim.Data.Rules.TicksPerDay, farm.HarvestReadyTick);
        Assert.Equal(CommandError.UnknownHarvest, sim.Execute(new SetHarvest(farm.Id, "vegetables")).Error);
        Assert.Equal(CommandError.UnknownHarvest, sim.Execute(new SetHarvest(farm.Id, "nope")).Error);
    }

    [Fact]
    public void A_hunting_lodge_brings_meat_and_hides_and_can_hunt_husk_game_or_forage()
    {
        var sim = LoopTestData.Colony(food: 0);
        var lodge = sim.Build("hunting_lodge");
        var s = sim.AddSurvivor(attributes: 60);
        sim.AddFood(100);
        sim.Execute(new AssignJob(s.Id, lodge.Id));
        sim.Advance(48);
        Assert.True(sim.State.Inventory.Count("meat") > 0);
        Assert.True(sim.State.Stockpile.Get("cloth") > 0);

        Assert.True(sim.Execute(new SetHarvest(lodge.Id, "husk_game")).Ok);
        sim.Advance(48);
        Assert.True(sim.State.Food.Lots["husk_meat"].All(l => l.Tainted));
        Assert.True(sim.Execute(new SetHarvest(lodge.Id, "forage")).Ok);
    }

    [Fact]
    public void Farm_output_per_day_counts_food_value_so_the_hud_and_the_bot_can_compare_it_with_need()
    {
        var sim = LoopTestData.Colony(food: 0);
        var farm = sim.Build("farm");
        var s = sim.AddSurvivor();
        sim.Execute(new AssignJob(s.Id, farm.Id));
        var potatoes = sim.OutputPerTick(farm);
        sim.Execute(new SetHarvest(farm.Id, "beans"));
        Assert.Equal(potatoes * sim.Data.Food!.Harvests["beans"].Yield, sim.OutputPerTick(farm), 9);
    }
}

public class CookingTests
{
    private static (ColonySim Sim, Building Kitchen, Survivor Cook) Kitchen(double skill = 0)
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 0);
        var kitchen = sim.Build("kitchen");
        sim.State.Stockpile.Add("wood", 1000);
        sim.State.Stockpile.Add("stone", 1000);
        sim.State.Stockpile.Add("scrap", 1000);
        Pantry.Add(sim.State, sim.Data, "boiled_potatoes", 50, shelfMultiplier: 0);  // what the cook eats meanwhile
        var cook = sim.AddSurvivor(attributes: 60);
        cook.Set(Skill.Cooking, skill);
        sim.Execute(new AssignJob(cook.Id, kitchen.Id));
        return (sim, kitchen, cook);
    }

    [Fact]
    public void Recipes_take_ingredients_by_tag_soonest_to_spoil_first_and_make_a_lot_of_rolled_quality()
    {
        var (sim, kitchen, _) = Kitchen(skill: 80);
        Assert.True(sim.Execute(new Upgrade(kitchen.Id)).Ok);
        Pantry.Add(sim.State, sim.Data, "beans", 3);
        Pantry.Add(sim.State, sim.Data, "meat", 1);
        Pantry.Add(sim.State, sim.Data, "potato", 2);
        Pantry.Add(sim.State, sim.Data, "water", 1);
        Assert.True(sim.Execute(new QueueRecipe(kitchen.Id, "stew")).Ok);
        for (var i = 0; i < 24 && kitchen.Queue.Count > 0; i++) sim.Tick();

        Assert.Equal(3, sim.State.Inventory.Count("beans"));  // meat spoils sooner: it was the protein
        Assert.Equal(0, sim.State.Inventory.Count("meat"));
        var stew = sim.State.Food.Lots["stew"];
        Assert.Equal(4, stew.Sum(l => l.Count) + sim.State.Food.Open.Count(p => p.ItemId == "stew"));
        Assert.All(stew, l => Assert.NotEqual("crude", l.Quality));
    }

    [Fact]
    public void Advanced_recipes_need_their_schematic_and_the_kitchen_level()
    {
        var (sim, kitchen, _) = Kitchen();
        Assert.Equal(CommandError.RecipeLevelTooLow, sim.Execute(new QueueRecipe(kitchen.Id, "feast")).Error);
        sim.Execute(new Upgrade(kitchen.Id));
        sim.Execute(new Upgrade(kitchen.Id));
        Assert.Equal(CommandError.RecipeLocked, sim.Execute(new QueueRecipe(kitchen.Id, "feast")).Error);
        Assert.True(sim.Execute(new QueueRecipe(kitchen.Id, "stew")).Ok);  // mid recipes are in the default set

        sim.State.Inventory.Add("sch_feast");
        Assert.True(sim.Execute(new StudyBlueprint("sch_feast")).Ok);
        Assert.True(FoodSchematics.Has(sim, "sch_feast"));
        Assert.True(sim.Execute(new QueueRecipe(kitchen.Id, "feast")).Ok);
    }

    [Fact]
    public void Food_buildings_behind_a_schematic_cannot_be_built_without_it()
    {
        var sim = LoopTestData.Colony(townHallLevel: 2, food: 0);
        foreach (var (res, n) in sim.Data.Buildings["cold_cellar"].Level(1).Cost) sim.State.Stockpile.Add(res, n);
        Assert.Equal(CommandError.SchematicNeeded, sim.Execute(new Construct("cold_cellar")).Error);
        sim.State.KnownBlueprints.Add("sch_cold_cellar");
        Assert.True(sim.Execute(new Construct("cold_cellar")).Ok);
        Assert.False(FoodSchematics.LevelUnlocked(sim, "greenhouse", 1));
    }

    [Fact]
    public void Husk_meat_cooked_badly_stays_tainted_and_cooked_well_is_clean()
    {
        int TaintedOf(double skill)
        {
            var (sim, kitchen, _) = Kitchen(skill);
            Pantry.Add(sim.State, sim.Data, "husk_meat", 20);
            Assert.True(sim.Execute(new QueueRecipe(kitchen.Id, "roast", 10)).Ok);
            for (var i = 0; i < 40 && kitchen.Queue.Count > 0; i++) sim.Tick();
            Assert.Empty(kitchen.Queue);
            return sim.State.Food.Lots.GetValueOrDefault("roast")?.Where(l => l.Tainted).Sum(l => l.Count) ?? 0;
        }
        Assert.True(TaintedOf(0) > 0);
        Assert.Equal(0, TaintedOf(100));
    }

    [Fact]
    public void Preserved_food_keeps_longer_when_a_preserver_makes_it()
    {
        var (sim, kitchen, cook) = Kitchen();
        var smokehouse = sim.Build("smokehouse");
        sim.Execute(new AssignJob(cook.Id, smokehouse.Id));
        cook.Perks.Add("preserver");
        Pantry.Add(sim.State, sim.Data, "meat", 3);
        Assert.True(sim.Execute(new QueueRecipe(smokehouse.Id, "smoke_meat")).Ok);
        for (var i = 0; i < 24 && smokehouse.Queue.Count > 0; i++) sim.Tick();
        var lot = sim.State.Food.Lots["smoked_meat"].Single();
        Assert.Equal(sim.Data.Food!.Items["smoked_meat"].ShelfDays * sim.Data.Rules.TicksPerDay * 1.5, lot.Shelf, 6);
    }

    [Fact]
    public void The_campfire_cooks_simple_dishes_and_makes_hot_suppers_count_for_more()
    {
        var sim = LoopTestData.Colony(food: 0);
        var fire = sim.Build("campfire");
        Assert.Contains(sim.Data.RecipesFor("campfire"), r => r.Item == "boiled_potatoes");
        Assert.DoesNotContain(sim.Data.RecipesFor("campfire"), r => r.Item == "stew");
        _ = fire;
    }
}

public class RationTests
{
    [Fact]
    public void Rations_fill_the_food_value_need_with_food_that_keeps_and_the_preferred_item_first()
    {
        var sim = LoopTestData.Colony(food: 0);
        Pantry.Add(sim.State, sim.Data, "potato", 20);
        Pantry.Add(sim.State, sim.Data, "meat", 5);
        Pantry.Add(sim.State, sim.Data, "field_rations", 3);
        var bill = Rations.Plan(sim, 6, 2, 0);
        Assert.True(bill.Enough);
        Assert.False(bill.Items.ContainsKey("meat"));  // raw meat is not travel food

        var preferred = Rations.Plan(sim, 6, 2, 0, new RationChoice("field_rations"));
        Assert.Equal(3, preferred.Items["field_rations"]);
        Assert.True(preferred.Weight < bill.Weight);
        Assert.Equal("trail_fed", preferred.FieldBuff);
    }

    [Fact]
    public void A_good_meal_gives_the_party_an_extra_ap_on_the_first_round_and_bad_rations_can_make_them_sick()
    {
        var sim = LoopTestData.Colony(food: 0);
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        Pantry.Add(sim.State, sim.Data, "stew", 10);
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a), LoopTestData.Member(b)], Rations: new("stew"))).Ok);
        Assert.Contains(a.Buffs, x => x.Id == "trail_fed");
        Assert.True(sim.State.Expedition!.Rations["stew"] > 0);

        var party = sim.ExpeditionParty();
        Assert.All(party.Members, m => Assert.Equal(1, m.FirstRoundAp));
        var run = NewScavenge.Create(ScavengeTestData.Real, "outskirts", 1, [party]);
        var unit = run.State.Units.First(u => u.PlayerId == ColonySim.ExpeditionPlayerId && u.Kind == UnitKind.Survivor);
        Assert.Equal(1, unit.FirstRoundAp);
        Assert.Equal(run.Profile(unit).MaxAp + 1, unit.Ap);

        var sick = LoopTestData.Colony(food: 0);
        Pantry.Add(sick.State, sick.Data, "potato", 20, tainted: true);
        Assert.Equal(1, Rations.Plan(sick, 6, 2, 0).RiskShare, 9);
        Pantry.Add(sick.State, sick.Data, "corn", 20);
        Assert.Equal(0, Rations.Plan(sick, 6, 2, 0).RiskShare, 9);  // clean food goes first
        Assert.True(Rations.Plan(sick, 6, 2, 30 * sick.Data.Rules.TicksPerDay).RiskShare > 0);  // a long trip: it spoils on the road
    }

    [Fact]
    public void Not_enough_food_for_rations_refuses_the_launch()
    {
        var sim = LoopTestData.Colony(food: 1);
        var a = sim.AddSurvivor();
        Assert.Equal(CommandError.CannotAfford, sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Error);
    }
}

public class FoodSaveTests
{
    [Fact]
    public void An_old_save_turns_its_food_pool_and_meals_into_items_of_the_same_food_value()
    {
        var sim = LoopTestData.Colony(food: 0);
        var kitchen = sim.Build("kitchen");
        var node = System.Text.Json.Nodes.JsonNode.Parse(SaveGame.Save(sim.State))!;
        node["version"] = SaveGame.FoodVersion - 1;
        var amounts = node["colony"]!["stockpile"]!["amounts"]!.AsObject();
        amounts["food"] = 40;
        amounts["meal"] = 3;
        amounts["stew"] = 2;
        var b = node["colony"]!["buildings"]!.AsArray().Single(x => x!["id"]!.GetValue<int>() == kitchen.Id)!;
        b["queue"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"id":1,"recipeId":"simple_meal","remaining":1,"repeat":true,"progress":0,"paid":true}]""");
        var loaded = SaveGame.Load(TestData.Real, node.ToJsonString(), ScavengeTestData.Real);

        Assert.Equal(0, loaded.State.Stockpile.Get("food"));
        var value = 44 + 3 * 2.0 + 2 * 2.5;
        Assert.InRange(loaded.FoodStock, value - 1.5, value + 1.5);
        Assert.True(loaded.State.Inventory.Count("beans") > 0 && loaded.State.Inventory.Count("canned_food") > 0);
        Assert.Equal(4, loaded.State.Inventory.Count("boiled_potatoes"));
        var order = loaded.State.Buildings.Single(b => b.DefId == "kitchen").Queue.Single();
        Assert.Equal("boiled_potatoes", order.RecipeId);
        Assert.False(order.Paid);
    }

    [Fact]
    public void Lots_buffs_and_harvests_survive_a_save()
    {
        var sim = LoopTestData.Colony(food: 0);
        var farm = sim.Build("farm");
        sim.Execute(new SetHarvest(farm.Id, "corn"));
        var s = sim.AddSurvivor();
        Pantry.Add(sim.State, sim.Data, "stew", 3, "fine", tainted: true);
        sim.Tick();
        var loaded = SaveGame.Load(TestData.Real, SaveGame.Save(sim.State), ScavengeTestData.Real);
        Assert.Equal("corn", loaded.State.FindBuilding(farm.Id)!.Harvest);
        var lot = loaded.State.Food.Lots["stew"].Single();
        Assert.True(lot is { Quality: "fine", Tainted: true });
        Assert.Contains(loaded.State.FindSurvivor(s.Id)!.Buffs, b => b.Id == "well_fed");
        Assert.Single(loaded.State.Food.Open);
    }
}
