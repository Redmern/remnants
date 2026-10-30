using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Schematics;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>What a production building's queue is doing this tick.</summary>
public enum CraftStatus { Idle, NoWorkers, NeedsSkill, WaitingForInputs, Working, NeedsPower }

/// <summary>
/// The closest skill requirement the building's workers fall short of: which skill, the best worker's level, the level
/// needed, and the ticks of practice until the best worker gets there.
/// </summary>
public sealed record SkillGap(Skill Skill, double Best, double Needed, int? Ticks);

/// <summary>
/// Production buildings work their queue. Each tick the building works one order: the first whose batch is already
/// paid, else the first that can start (a worker has the recipe's <c>minSkill</c> and its inputs are in stock), whose
/// inputs are then paid. Its workers add their job output as effort (and train). A finished batch yields its item —
/// gear at a quality rolled from the best worker's skill — or resource; a counted order then loses a batch (and leaves
/// the queue at zero), a repeating order moves to the back. When nothing can go because an order needs more skill than
/// any worker has, the workers practise: they train as if working, so a fresh colony grows into its recipes.
/// </summary>
internal static class CraftingSystem
{
    public static void Run(ColonyState state, GameData data, ScavengeData? items, DeterministicRng rng, ColonySim? sim = null)
    {
        foreach (var building in state.Buildings.Where(b => b.Queue.Count > 0))
        {
            var def = data.Buildings[building.DefId];
            if (def.Job is null) continue;
            var crew = Schedule.Crew(state, data, building).ToList();
            var workers = crew.Select(c => c.Worker).ToList();
            if (workers.Count == 0) continue;
            var job = data.Jobs[def.Job];
            var order = Pick(state, data, items, building, workers, job);
            if (order is null)
            {
                var skill = BestSkill(workers, job);
                if (building.Queue.Any(o => data.Recipes[o.RecipeId].MinSkill > skill + 1e-9))
                    foreach (var w in workers) Training.Work(state, data, job, w);
                continue;
            }
            var recipe = data.Recipes[order.RecipeId];
            // A paid tier 3 batch waits out a power cut (its workers still practise).
            if (recipe.NeedsPower && !Power.IsPowered(state, building))
            {
                foreach (var w in workers) Training.Work(state, data, job, w);
                continue;
            }
            if (!order.Paid) Pay(state, data, items, recipe, order);

            var powered = 1 + Power.Bonus(state, data, building, PowerEffect.CraftSpeed);
            foreach (var (w, shift) in crew)
            {
                order.Progress += JobOutput.PerTick(def, building.Level, job, w, data.Rules.Output, data) * shift * powered;
                Training.Work(state, data, job, w);
            }
            if (order.Progress + 1e-9 < recipe.Work) continue;
            Finish(state, data, items, rng, building, def, recipe, order, BestSkill(workers, job), QualityBonus(data, workers, job),
                workers.Max(w => PerkEffects.For(w, data).Hook(PerkHooks.ShelfLifeBonus)), YieldBonus(data, workers, job));
            if (recipe.Learns is { } learns && sim is not null) Schematics.Schematics.Grant(sim, learns, SchematicSource.Terminal);
            foreach (var w in workers) Progression.GrantXp(state, data, w, data.Progression.Xp.PerCraft, XpSource.Crafting);
        }
    }

    public static CraftStatus Status(ColonyState state, GameData data, ScavengeData? items, Building building)
    {
        var def = data.Buildings[building.DefId];
        if (building.Queue.Count == 0 || def.Job is null) return CraftStatus.Idle;
        var workers = state.WorkersAt(building.Id).ToList();
        if (workers.Count == 0) return CraftStatus.NoWorkers;
        var job = data.Jobs[def.Job];
        if (Pick(state, data, items, building, workers, job) is { } picked)
            return data.Recipes[picked.RecipeId].NeedsPower && !Power.IsPowered(state, building) ? CraftStatus.NeedsPower : CraftStatus.Working;
        var skill = BestSkill(workers, job);
        if (!Power.IsPowered(state, building) && building.Queue.Any(o => data.Recipes[o.RecipeId] is { NeedsPower: true } r && r.MinSkill <= skill + 1e-9
                && state.Stockpile.CanAfford(r.Inputs) && HasItemInputs(state, items, r)))
            return CraftStatus.NeedsPower;
        return building.Queue.All(o => data.Recipes[o.RecipeId].MinSkill > skill + 1e-9) ? CraftStatus.NeedsSkill : CraftStatus.WaitingForInputs;
    }

    /// <summary>The smallest skill shortfall among the queued orders, or null when the workers can make all of them.</summary>
    public static SkillGap? Gap(ColonyState state, GameData data, Building building)
    {
        var def = data.Buildings[building.DefId];
        if (def.Job is null) return null;
        var job = data.Jobs[def.Job];
        var workers = state.WorkersAt(building.Id).ToList();
        var best = workers.OrderByDescending(w => w.Get(job.Skill)).FirstOrDefault();
        var skill = best?.Get(job.Skill) ?? 0;
        var needed = building.Queue.Select(o => data.Recipes[o.RecipeId].MinSkill).Where(m => m > skill + 1e-9).DefaultIfEmpty(0).Min();
        if (needed <= 0) return null;
        int? ticks = best is null ? null : Training.TicksToReach(job, best, needed, data.Rules.Training);
        return new SkillGap(job.Skill, skill, needed, ticks);
    }

    /// <summary>Effort per tick the building's current workers add (for an ETA in the UI).</summary>
    public static double EffortPerTick(ColonyState state, GameData data, Building building)
    {
        var def = data.Buildings[building.DefId];
        if (def.Job is null) return 0;
        var job = data.Jobs[def.Job];
        return state.WorkersAt(building.Id).Sum(w => JobOutput.PerTick(def, building.Level, job, w, data.Rules.Output, data));
    }

    /// <summary>The order worked this tick, or null when nothing can go.</summary>
    private static CraftOrder? Pick(ColonyState state, GameData data, ScavengeData? items, Building building, List<Survivor> workers, JobDef job)
    {
        if (building.Queue.FirstOrDefault(o => o.Paid) is { } paid) return paid;
        var skill = BestSkill(workers, job);
        return building.Queue.FirstOrDefault(o =>
        {
            var r = data.Recipes[o.RecipeId];
            return r.MinSkill <= skill + 1e-9 && (!r.NeedsPower || Power.IsPowered(state, building))
                   && state.Stockpile.CanAfford(r.Inputs) && HasItemInputs(state, items, r) && HasIngredients(state, data, r);
        });
    }

    private static double BestSkill(IEnumerable<Survivor> workers, JobDef job) =>
        workers.Select(w => w.Get(job.Skill)).DefaultIfEmpty(0).Max();

    /// <summary>Perk bonus to the quality score: the best worker's (by skill, then id) crafting-quality perks at this job.</summary>
    private static double QualityBonus(GameData data, IEnumerable<Survivor> workers, JobDef job) =>
        workers.OrderByDescending(w => w.Get(job.Skill)).ThenBy(w => w.Id).FirstOrDefault() is { } best
            ? PerkEffects.For(best, data).CraftQualityBonus(job.Id)
            : 0;

    /// <summary>Enough fresh food in the pantry for each ingredient (an input key's own units are not counted twice: keys are checked in turn on a copy).</summary>
    public static bool HasIngredients(ColonyState state, GameData data, RecipeDef recipe)
    {
        if (recipe.Ingredients.Count == 0) return true;
        if (data.Food is not { } food) return false;
        Pantry.Reconcile(state, data);
        var stock = state.Food.Lots.ToDictionary(kv => kv.Key, kv => kv.Value.Where(l => !l.Spoiled).Sum(l => l.Count));
        // Exact item keys first, then tags (the order Pay takes them in).
        foreach (var (key, count) in recipe.Ingredients.OrderBy(kv => kv.Key.StartsWith(FoodData.TagPrefix, StringComparison.Ordinal) ? 1 : 0))
        {
            var left = count;
            foreach (var id in stock.Keys.Where(id => food.Matches(id, key)).OrderBy(id => id, StringComparer.Ordinal).ToList())
            {
                var n = Math.Min(left, stock[id]);
                stock[id] -= n;
                left -= n;
                if (left == 0) break;
            }
            if (left > 0) return false;
        }
        return true;
    }

    /// <summary>Disassembly: the best worker's (by skill, then id) extra-component chance (Circuit Tinkerer, skill).</summary>
    private static double YieldBonus(GameData data, IEnumerable<Survivor> workers, JobDef job) =>
        data.Electronics is { } e && workers.OrderByDescending(w => w.Get(job.Skill)).ThenBy(w => w.Id).FirstOrDefault() is { } best
            ? PerkEffects.For(best, data).Hook(PerkHooks.DisassemblyYieldBonus) + best.Get(job.Skill) * e.Disassembly.SkillYieldPerPoint
            : 0;

    public static bool HasItemInputs(ColonyState state, ScavengeData? items, RecipeDef recipe) =>
        recipe.ItemInputs.All(kv => Matching(state, items, kv.Key).Sum(id => state.Inventory.Count(id)) >= kv.Value);

    /// <summary>Inventory ids that satisfy an item input: the item at any quality, worst quality first (never modded gear).</summary>
    private static List<string> Matching(ColonyState state, ScavengeData? items, string itemId)
    {
        if (items is null) return [itemId];
        int Rank(string id) => items.Rules.Quality.FindIndex(q => q == items.QualityOf(items.Items[id]));
        return state.Inventory.Counts.Keys
            .Where(id => items.Items.TryGetValue(id, out var def) && def.DefinedId == itemId && !def.IsModded)
            .OrderBy(Rank).ThenBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static void Pay(ColonyState state, GameData data, ScavengeData? items, RecipeDef recipe, CraftOrder order)
    {
        state.Stockpile.Spend(recipe.Inputs);
        foreach (var (key, count) in recipe.Ingredients.OrderBy(kv => kv.Key.StartsWith(FoodData.TagPrefix, StringComparison.Ordinal) ? 1 : 0))
            foreach (var t in Pantry.TakeIngredient(state, data, key, count) ?? throw new InvalidOperationException($"Missing ingredient {key}."))
            {
                for (var i = 0; i < t.Count; i++) order.PaidItems.Add(t.ItemId);
                if (t.Tainted) order.Tainted = true;
            }
        foreach (var (itemId, count) in recipe.ItemInputs)
        {
            var left = count;
            foreach (var id in Matching(state, items, itemId))
                while (left > 0 && state.Inventory.TryTake(id))
                {
                    order.PaidItems.Add(id);
                    left--;
                }
        }
        order.Paid = true;
    }

    /// <summary>Gives back a paid batch's inputs (when its order is cancelled or replaced).</summary>
    public static void Refund(ColonyState state, GameData data, CraftOrder order)
    {
        if (!order.Paid) return;
        foreach (var (res, amount) in data.Recipes[order.RecipeId].Inputs) state.Stockpile.Add(res, amount);
        foreach (var id in order.PaidItems)
            if (data.Food?.IsFood(id) == true) Pantry.Add(state, data, id, 1, tainted: order.Tainted && data.Food.Items[id].Tainted);
            else state.Inventory.Add(id);
        order.PaidItems.Clear();
        order.Paid = false;
        order.Tainted = false;
        order.Progress = 0;
    }

    private static void Finish(ColonyState state, GameData data, ScavengeData? items, DeterministicRng rng,
        Building building, BuildingDef def, RecipeDef recipe, CraftOrder order, double skill, double qualityBonus, double preserver, double yieldBonus)
    {
        string made;
        var disassembly = recipe.Group == "Disassembly";
        // Stripping a device: each component has a chance of one more (skill and Circuit Tinkerer); nothing is drawn otherwise.
        int Extra(int count)
        {
            if (!disassembly || yieldBonus <= 0) return count;
            var more = 0;
            for (var i = 0; i < count; i++)
                if (rng.NextDouble() < yieldBonus) more++;
            return count + more;
        }
        if (recipe.Learns is { } learns)
        {
            made = $"a decrypted {data.Schematics.All[learns].Name} schematic";
            order.Repeat = false;
            order.Remaining = 1;
        }
        else if (recipe.Item is { } food && data.Food?.IsFood(food) == true)
        {
            var count = (int)Math.Round(recipe.Amount);
            var quality = FoodQuality.Roll(data, Score(data, building, skill) + qualityBonus, rng);
            var taint = order.Tainted && rng.NextDouble() < data.Food.Rules.Quality.TaintKept.GetValueOrDefault(quality, 1);
            Pantry.Add(state, data, food, count, quality, taint, Cooking.ShelfMultiplier(data, food, preserver));
            var name = items?.Items.GetValueOrDefault(food)?.Name ?? recipe.Name;
            made = $"{count} {name} ({quality}{(taint ? ", tainted" : "")})";
        }
        else if (recipe.Item is { } item)
        {
            var count = Extra((int)Math.Round(recipe.Amount));
            var id = item;
            if (Tiers(data, items, item) is { } tiers)
                id = items!.QualityVariant(item, Roll(tiers, Score(data, building, skill) + qualityBonus, data.Rules.Crafting.QualitySpread, rng));
            state.Inventory.Add(id, count);
            made = $"{count} {items?.Items.GetValueOrDefault(id)?.Name ?? recipe.Name}";
            foreach (var (extra, n) in recipe.Byproducts)
            {
                var k = Extra(n);
                state.Inventory.Add(extra, k);
                made += $", {k} {items?.Items.GetValueOrDefault(extra)?.Name ?? extra}";
            }
            if (recipe.ResearchChance > 0 && rng.NextDouble() < recipe.ResearchChance)
            {
                state.Stockpile.Add(data.Schematics.Rules.ResearchDataResource, 1);
                made += " and Research Data";
            }
        }
        else
        {
            state.Stockpile.Add(recipe.Resource!, recipe.Amount);
            made = $"{recipe.Amount:0.##} {data.Resources[recipe.Resource!].Name}";
        }

        order.Progress = 0;
        order.Paid = false;
        order.Tainted = false;
        order.PaidItems.Clear();
        if (order.Repeat)
        {
            building.Queue.Remove(order);
            building.Queue.Add(order);
        }
        else if (--order.Remaining <= 0) building.Queue.Remove(order);
        state.Log(new(state.Tick, disassembly ? ColonyEventType.Disassembled : ColonyEventType.Crafted,
            disassembly ? $"{def.Name} stripped {recipe.Name[6..]} into {made}." : $"{def.Name} made {made}.", BuildingId: building.Id));
    }

    /// <summary>Quality tiers (worst first) with their score thresholds, or null when the output has no quality.</summary>
    private static List<(string Tier, double Threshold)>? Tiers(GameData data, ScavengeData? items, string itemId)
    {
        if (items is null || items.Rules.Quality.Count == 0 || !items.Items.TryGetValue(itemId, out var def) || !def.HasQuality) return null;
        var thresholds = data.Rules.Crafting.QualityThresholds;
        return items.Rules.Quality
            .Select((q, i) => (q.Id, i == 0 ? double.NegativeInfinity : thresholds.GetValueOrDefault(q.Id, double.PositiveInfinity)))
            .ToList();
    }

    internal static double Score(GameData data, Building building, double skill) =>
        skill / Survivor.MaxStat + (building.Level - 1) * data.Rules.Crafting.QualityPerLevel;

    /// <summary>The best tier whose threshold the score reaches.</summary>
    private static string Roll(List<(string Tier, double Threshold)> tiers, double score, double spread, DeterministicRng rng)
    {
        var rolled = score + (rng.NextDouble() * 2 - 1) * spread;
        return tiers.Last(t => t.Threshold <= rolled || t.Threshold == double.NegativeInfinity).Tier;
    }

    public static IReadOnlyList<(string Tier, double Chance)> QualityChances(ColonyState state, GameData data, ScavengeData? items,
        Building building, RecipeDef recipe)
    {
        if (recipe.Item is { } food && data.Food?.IsFood(food) == true && data.Food.Items[food].Meal is not null)
        {
            var bdef = data.Buildings[building.DefId];
            var crew = state.WorkersAt(building.Id).ToList();
            var s = bdef.Job is null ? 0 : BestSkill(crew, data.Jobs[bdef.Job]);
            return FoodQuality.Chances(data, Score(data, building, s) + (bdef.Job is null ? 0 : QualityBonus(data, crew, data.Jobs[bdef.Job])));
        }
        if (recipe.Item is null || Tiers(data, items, recipe.Item) is not { } tiers) return [];
        var def = data.Buildings[building.DefId];
        var workers = state.WorkersAt(building.Id).ToList();
        var skill = def.Job is null ? 0 : BestSkill(workers, data.Jobs[def.Job]);
        var score = Score(data, building, skill) + (def.Job is null ? 0 : QualityBonus(data, workers, data.Jobs[def.Job]));
        var spread = data.Rules.Crafting.QualitySpread;

        double AtLeast(double t) => double.IsNegativeInfinity(t) ? 1 : double.IsPositiveInfinity(t) ? 0
            : spread <= 0 ? (score >= t ? 1 : 0) : Math.Clamp((score + spread - t) / (2 * spread), 0, 1);

        return tiers.Select((t, i) =>
        {
            var above = tiers.Skip(i + 1).Select(n => n.Threshold).DefaultIfEmpty(double.PositiveInfinity).Min();
            return (t.Tier, above <= t.Threshold ? 0 : Math.Max(0, AtLeast(t.Threshold) - AtLeast(above)));
        }).ToList();
    }
}

internal static class CraftingHandlers
{
    public static CommandResult Queue(ColonySim sim, QueueRecipe c)
    {
        var state = sim.State;
        if (Find(sim, c.BuildingId, c.RecipeId, out var building, out var def, out var recipe) is { } error) return error;
        if (CheckRecipe(sim, building, recipe) is { Ok: false } locked) return locked;
        if (!c.Repeat && (c.Count < 1 || c.Count > sim.Data.Rules.Crafting.MaxBatches))
            return CommandResult.Fail(CommandError.InvalidCount, $"Order 1 to {sim.Data.Rules.Crafting.MaxBatches} batches.");
        if (building.Queue.Count >= sim.Data.Rules.Crafting.MaxQueue)
            return CommandResult.Fail(CommandError.QueueFull, $"{def.Name}'s queue is full.");

        var order = new CraftOrder { Id = state.NextOrderId++, RecipeId = recipe.Id, Remaining = c.Repeat ? 1 : c.Count, Repeat = c.Repeat };
        building.Queue.Add(order);
        var message = $"{def.Name} queued {recipe.Name}{(c.Repeat ? " (repeat)" : c.Count > 1 ? $" ×{c.Count}" : "")}.";
        state.Log(new(state.Tick, ColonyEventType.OrderQueued, message, BuildingId: building.Id));
        return CommandResult.Success(message, order.Id);
    }

    public static CommandResult Cancel(ColonySim sim, CancelOrder c)
    {
        var state = sim.State;
        var building = state.FindBuilding(c.BuildingId);
        if (building is null) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var order = building.Queue.Find(o => o.Id == c.OrderId);
        if (order is null) return CommandResult.Fail(CommandError.UnknownOrder, $"No order {c.OrderId} at that building.");
        CraftingSystem.Refund(state, sim.Data, order);
        building.Queue.Remove(order);
        var def = sim.Data.Buildings[building.DefId];
        var message = $"{def.Name} cancelled {sim.Data.Recipes[order.RecipeId].Name}.";
        state.Log(new(state.Tick, ColonyEventType.OrderCancelled, message, BuildingId: building.Id));
        return CommandResult.Success(message, building.Id);
    }

    public static CommandResult SetRecipe(ColonySim sim, SetRecipe c)
    {
        var state = sim.State;
        BuildingDef def;
        RecipeDef? recipe = null;
        Building building;
        if (c.RecipeId is null)
        {
            building = state.FindBuilding(c.BuildingId)!;
            if (building is null) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
            def = sim.Data.Buildings[building.DefId];
        }
        else
        {
            if (Find(sim, c.BuildingId, c.RecipeId, out building, out def, out recipe) is { } error) return error;
            if (CheckRecipe(sim, building, recipe) is { Ok: false } locked) return locked;
        }
        if (recipe is not null && building.Queue is [{ Repeat: true } only] && only.RecipeId == recipe.Id)
            return CommandResult.Success($"{def.Name} is already on that.", building.Id);

        foreach (var order in building.Queue) CraftingSystem.Refund(state, sim.Data, order);
        building.Queue.Clear();
        if (recipe is not null)
            building.Queue.Add(new CraftOrder { Id = state.NextOrderId++, RecipeId = recipe.Id, Repeat = true });
        var message = recipe is null ? $"{def.Name} stopped working." : $"{def.Name} now makes {recipe.Name}.";
        state.Log(new(state.Tick, ColonyEventType.RecipeChanged, message, BuildingId: building.Id));
        return CommandResult.Success(message, building.Id);
    }

    /// <summary>Building level, schematic, item data and (for cybernetics) the tree a recipe needs before it can be queued.</summary>
    public static CommandResult CheckRecipe(ColonySim sim, Building building, RecipeDef recipe)
    {
        var def = sim.Data.Buildings[building.DefId];
        if (recipe.MinLevel > building.Level)
            return CommandResult.Fail(CommandError.RecipeLevelTooLow, $"{recipe.Name} needs {def.Name} level {recipe.MinLevel}.");
        if (sim.Data.Schematics.GateOfRecipe(recipe.Id) is { } gate && !Schematics.Schematics.Has(sim, gate))
            return CommandResult.Fail(CommandError.RecipeLocked, $"{recipe.Name} needs the {sim.Data.Schematics.All[gate].Name} schematic.");
        if (recipe.Learns is { } learns)
        {
            var name = sim.Data.Schematics.All[learns].Name;
            if (Schematics.Schematics.Has(sim, learns)) return CommandResult.Fail(CommandError.AlreadyKnown, $"The {name} schematic is already known.");
            var queued = sim.State.Buildings.SelectMany(b => b.Queue).Where(o => o.RecipeId == recipe.Id)
                .Sum(o => o.Paid ? 0 : o.Repeat ? 1 : o.Remaining);
            if (!sim.State.Inventory.Has(learns, queued + 1))
                return CommandResult.Fail(CommandError.MissingItems, $"There is no {name} chip to decrypt.");
        }
        if ((recipe.Item is not null || recipe.ItemInputs.Count > 0) && sim.Scavenge is null) return ExpeditionHandlers.NoData();
        if (Cybernetics.CraftGate(sim, recipe) is { } locked) return locked;
        return CommandResult.Success("");
    }

    private static CommandResult? Find(ColonySim sim, int buildingId, string recipeId, out Building building, out BuildingDef def, out RecipeDef recipe)
    {
        building = sim.State.FindBuilding(buildingId)!;
        def = null!;
        recipe = null!;
        if (building is null) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {buildingId}.");
        def = sim.Data.Buildings[building.DefId];
        if (!sim.Data.Recipes.TryGetValue(recipeId, out recipe!) || recipe.Building != def.Id)
            return CommandResult.Fail(CommandError.UnknownRecipe, $"{def.Name} has no recipe '{recipeId}'.");
        return null;
    }
}
