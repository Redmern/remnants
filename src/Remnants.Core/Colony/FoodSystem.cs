using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// Food each tick (data/food.json): the pantry ages (slower in a Cold Cellar) and spoiled food rots away after
/// <c>spoilage.spoiledDays</c>; then everyone at home eats (away parties live on their rations), infants and children
/// first. Each picks the best food in stock: a cooked dish they have not had too much of lately, before raw food,
/// before spoiled or raw meat (only when there is nothing else, and it can make them sick). One unit covers its food
/// value of the need; a partly eaten unit stays open for the next. A dish adds its mood and health and its buffs (all
/// scaled by its quality), a hot dish between dusk and dawn is a hot supper, and nutrition moves towards the food's.
/// Whoever goes hungry takes damage and may die. Last, the debuffs of going without (Hungry, Bland, Malnourished,
/// Same Again) are set, buffs that ran out are removed (a stim crash adds sleep debt), and buff mood and health apply.
/// </summary>
internal static class FoodSystem
{
    public static void Run(ColonyState state, GameData data, Scavenge.ScavengeData? items, DeterministicRng rng)
    {
        if (data.Food is not { } food) return;
        var rules = data.Rules.Food;
        Pantry.Reconcile(state, data);
        Age(state, data, items, food);

        var dead = new List<Survivor>();
        var decay = Math.Pow(0.5, 1 / Math.Max(1, food.Rules.Variety.HalfLifeHours * data.Rules.TicksPerDay / 24.0));
        foreach (var s in state.Survivors.Where(s => !s.Away).OrderBy(s => s.LifeStage).ThenBy(s => s.Id))
        {
            foreach (var key in s.Diet.Keys.ToList())
                if ((s.Diet[key] *= decay) < 1e-4) s.Diet.Remove(key);
            var need = s.LifeStage switch
            {
                LifeStage.Infant => rules.InfantPerTick,
                LifeStage.Child => rules.ChildPerTick,
                _ => rules.AdultPerTick,
            };
            s.TicksSinceCooked++;
            if (Eat(state, data, food, rng, s, need))
            {
                s.StarvingTicks = 0;
                s.Health = Math.Min(Survivor.MaxHealth, s.Health + rules.RecoveryPerTick);
                continue;
            }

            if (s.StarvingTicks == 0)
                state.Log(new(state.Tick, ColonyEventType.Starving, $"{s.Name} is starving.", s.Id));
            s.StarvingTicks++;
            FoodEffects.Give(s, food.Rules.BuffIds.Hungry, state.Tick + Hours(data, food.Rules.HungryHours), 1);
            s.Health -= rules.StarvationDamagePerTick;
            if (s.Health <= 0) dead.Add(s);
        }
        foreach (var s in dead) ColonyDeaths.Remove(state, data, s, "starved to death", "Starvation");

        foreach (var s in state.Survivors) Conditions(state, data, food, s);
    }

    /// <summary>Lots and open portions age; rotten food (spoiled for longer than <c>spoiledDays</c>) is thrown out.</summary>
    private static void Age(ColonyState state, GameData data, Scavenge.ScavengeData? items, FoodData food)
    {
        var cellar = CellarRate(state, data);
        var grace = food.Rules.Spoilage.SpoiledDays * data.Rules.TicksPerDay;
        var rotted = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, lots) in state.Food.Lots)
        {
            var rate = food.Items.TryGetValue(id, out var def) && def.Cellar ? cellar : 1;
            foreach (var lot in lots)
            {
                if (lot.Shelf <= 0) continue;
                lot.Age += rate;
                if (lot.Age < lot.Shelf + grace) continue;
                rotted[id] = rotted.GetValueOrDefault(id) + lot.Count;
                state.Inventory.TryTake(id, lot.Count);
                lot.Count = 0;
            }
        }
        foreach (var p in state.Food.Open)
        {
            if (p.Shelf <= 0) continue;
            p.Age += food.Items.TryGetValue(p.ItemId, out var def) && def.Cellar ? cellar : 1;
        }
        state.Food.Open.RemoveAll(p => p.Shelf > 0 && p.Age >= p.Shelf + grace);
        Pantry.Prune(state);
        if (rotted.Count > 0)
            state.Log(new(state.Tick, ColonyEventType.FoodRotted,
                $"Food rotted and was thrown out: {string.Join(", ", rotted.Select(kv => $"{kv.Value} {ItemName(items, kv.Key)}"))}."));
    }

    /// <summary>How fast cellar food ages: the Cold Cellar's rate at its level (slower still when powered), 1 without one.</summary>
    public static double CellarRate(ColonyState state, GameData data)
    {
        if (data.Food is not { } food) return 1;
        var cellar = state.Buildings.Where(b => b.DefId == food.Rules.CellarBuilding).OrderByDescending(b => b.Level).FirstOrDefault();
        if (cellar is null || food.Rules.CellarRateByLevel.Count == 0) return 1;
        var rate = food.Rules.CellarRateByLevel[Math.Clamp(cellar.Level - 1, 0, food.Rules.CellarRateByLevel.Count - 1)];
        return FoodPower.IsPowered(state, data, cellar) ? rate * food.Rules.PoweredCellarMultiplier : rate;
    }

    /// <summary>Eats <paramref name="need"/> food value; false when there was nothing at all to eat.</summary>
    private static bool Eat(ColonyState state, GameData data, FoodData food, DeterministicRng rng, Survivor s, double need)
    {
        var left = need;
        var ate = false;
        for (var guard = 0; left > 1e-9 && guard < 16; guard++)
        {
            var pick = Choose(state, data, food, s);
            if (pick is null) break;
            var (id, def) = pick.Value;
            var portion = Open(state, data, id);
            if (portion is null) break;
            var units = Math.Min(portion.Left, left / def.FoodValue);
            portion.Left -= units;
            if (portion.Left <= 1e-9) state.Food.Open.Remove(portion);
            var share = units * def.FoodValue / need;
            left -= units * def.FoodValue;
            ate = true;
            Effects(state, data, food, rng, s, def, portion, share, units * def.FoodValue);
        }
        return ate;
    }

    /// <summary>
    /// The food a survivor goes for: by a score of how good it is (cooked first, its mood, health, buffs and quality),
    /// less how much of that dish they had lately, plus a nudge for what spoils within a day and minus one for
    /// long-keeping food (save it for the road). Spoiled food and raw meat only when nothing else is left.
    /// </summary>
    private static (string Id, FoodItemDef Def)? Choose(ColonyState state, GameData data, FoodData food, Survivor s)
    {
        var day = data.Rules.TicksPerDay;
        var diet = 0.0;
        foreach (var v in s.Diet.Values) diet += v;
        (string, FoodItemDef)? best = null;
        var bestScore = double.NegativeInfinity;
        void Consider(string id, bool spoiled, string quality, double? ticksLeft)
        {
            if (!food.Items.TryGetValue(id, out var def) || def.FoodValue <= 0) return;
            var desperate = spoiled || !def.Edible;
            var score = desperate ? -100 + (spoiled ? 0 : -1) : Score(data, def, quality);
            if (!desperate)
            {
                if (def.Meal is not null && diet > 0) score -= food.Rules.Variety.ChoiceWeight * s.Diet.GetValueOrDefault(id) / diet;
                if (ticksLeft is { } t && t < day) score += 0.3;
                if (def.ShelfDays >= 20 || def.ShelfDays == 0) score -= 1;
            }
            if (score > bestScore + 1e-12 || score > bestScore - 1e-12 && best is { } b && string.CompareOrdinal(id, b.Item1) < 0)
            {
                bestScore = score;
                best = (id, def);
            }
        }
        foreach (var p in state.Food.Open) Consider(p.ItemId, p.Spoiled, p.Quality, p.Shelf <= 0 ? null : p.Shelf - p.Age);
        foreach (var (id, lots) in state.Food.Lots)
            foreach (var lot in lots)
                if (lot.Count > 0) Consider(id, lot.Spoiled, lot.Quality, lot.TicksLeft);
        return best;
    }

    private static double Score(GameData data, FoodItemDef def, string quality)
    {
        if (def.Meal is not { } meal) return def.Nutrition;
        var q = FoodQuality.Effect(data, quality);
        return 2 + def.Nutrition + q * (meal.MoodPerTick * 10 + meal.HealthPerTick * 2 + meal.Buffs.Count * 0.2);
    }

    /// <summary>The open portion of an item, opening a unit (best lot: fresh and soonest to spoil) when none is.</summary>
    private static OpenPortion? Open(ColonyState state, GameData data, string id)
    {
        var lot = Pantry.FirstLot(state, id);
        if (state.Food.Open.Find(p => p.ItemId == id) is { } open)
        {
            // A spoiled leftover is thrown out when there is a fresh unit of the same food to open instead.
            if (!open.Spoiled || lot is not { Spoiled: false }) return open;
            state.Food.Open.Remove(open);
        }
        if (lot is null) return null;
        var portion = new OpenPortion { ItemId = id, Left = 1, Age = lot.Age, Shelf = lot.Shelf, Quality = lot.Quality, Tainted = lot.Tainted };
        Pantry.TakeFromLot(state, id, lot, 1);
        Pantry.Prune(state);
        state.Food.Open.Add(portion);
        return portion;
    }

    /// <summary>What eating <paramref name="share"/> of this tick's need of a food does.</summary>
    private static void Effects(ColonyState state, GameData data, FoodData food, DeterministicRng rng, Survivor s, FoodItemDef def,
        OpenPortion portion, double share, double value)
    {
        var q = FoodQuality.Effect(data, portion.Quality);
        var n = food.Rules.Nutrition;
        var target = Math.Clamp(def.Nutrition + (q - 1) * n.QualityBonus, 0, 1);
        s.Nutrition += (target - s.Nutrition) * share / Math.Max(1, Hours(data, n.AverageHours));
        s.Diet[def.Id] = s.Diet.GetValueOrDefault(def.Id) + value;

        if (def.Meal is { } meal)
        {
            s.Health = Math.Min(Survivor.MaxHealth, s.Health + meal.HealthPerTick * q * share);
            var supper = 0.0;
            if (meal.Hot && data.Rules.DayNight.Enabled && DayNight.PhaseAt(state.Tick, data.Rules) is DayPhase.Dusk or DayPhase.Night)
            {
                supper = data.Rules.DayNight.NightMealMoodPerTick;
                if (state.Buildings.Any(b => b.DefId == food.Rules.CampfireBuilding) && !Weather.KindOf(data, Weather.Current(state, data)).Colony.NoCampfire)
                    supper *= food.Rules.CampfireSupperMultiplier;
            }
            s.Mood = Math.Min(Survivor.MaxMood, s.Mood + (meal.MoodPerTick * q + supper) * share);
            if (meal.Hot || meal.MoodPerTick > 0) s.TicksSinceCooked = 0;
            foreach (var (buff, hours) in meal.Buffs) FoodEffects.Give(s, buff, state.Tick + Hours(data, hours * q), q);
        }

        var sick = food.Rules.Sickness;
        var chance = portion.Spoiled ? sick.SpoiledPerTick : 0;
        if (portion.Tainted) chance = Math.Max(chance, sick.TaintedPerTick);
        if (!def.Edible) chance = Math.Max(chance, def.RawSicknessPerTick);
        if (chance > 0 && rng.NextDouble() < chance * share)
            FoodSickness.Apply(state, data, s, portion.Tainted ? "tainted" : portion.Spoiled ? "spoiled" : "raw", 1);
    }

    /// <summary>Hungry is set while starving; here Bland, Malnourished and Same Again follow the diet, then buffs run.</summary>
    private static void Conditions(ColonyState state, GameData data, FoodData food, Survivor s)
    {
        var ids = food.Rules.BuffIds;
        var now = state.Tick;
        if (!s.Away)
        {
            Toggle(s, ids.Malnourished, s.Nutrition + 1e-9 < food.Rules.Nutrition.MalnourishedBelow, now);
            Toggle(s, ids.Bland, !s.IsInfant && s.TicksSinceCooked >= Hours(data, food.Rules.BlandAfterHours), now);
            Toggle(s, ids.SameAgain, !s.IsInfant && SameAgain(data, food, s), now);
        }

        for (var i = s.Buffs.Count - 1; i >= 0; i--)
        {
            var b = s.Buffs[i];
            if (b.Until > now) continue;
            s.Buffs.RemoveAt(i);
            if (food.Buffs.TryGetValue(b.Id, out var def) && def.SleepDebtOnEnd > 0)
                s.SleepDebt = Math.Min(data.Rules.DayNight.Tired.MaxDebt, s.SleepDebt + def.SleepDebtOnEnd * b.Strength);
        }
        if (s.Away) return;
        foreach (var b in s.Buffs)
        {
            if (!food.Buffs.TryGetValue(b.Id, out var def)) continue;
            s.Mood = Math.Clamp(s.Mood + def.MoodPerTick * b.Strength, 0, Survivor.MaxMood);
            if (def.HealthPerTick != 0)
                s.Health = Math.Clamp(s.Health + def.HealthPerTick * b.Strength, Math.Min(s.Health, 1), Survivor.MaxHealth);
        }
    }

    /// <summary>One cooked dish is more than <c>variety.share</c> of what they ate lately (and they ate enough cooked food for it to count).</summary>
    public static bool SameAgain(GameData data, FoodData food, Survivor s)
    {
        var cooked = s.Diet.Where(kv => food.Items.GetValueOrDefault(kv.Key)?.Meal is not null).ToList();
        var total = cooked.Sum(kv => kv.Value);
        if (total < food.Rules.Variety.MinFood) return false;
        return cooked.Max(kv => kv.Value) / Math.Max(total, s.Diet.Values.Sum()) > food.Rules.Variety.Share;
    }

    private static void Toggle(Survivor s, string id, bool on, long now)
    {
        if (on) FoodEffects.Give(s, id, now + 1, 1);
        else s.Buffs.RemoveAll(b => b.Id == id && b.Until <= now + 1);
    }

    private static long Hours(GameData data, double hours) => (long)Math.Round(hours * data.Rules.TicksPerDay / 24.0);

    internal static string ItemName(Scavenge.ScavengeData? items, string id) => items?.Items.GetValueOrDefault(id)?.Name ?? id.Replace('_', ' ');
}

/// <summary>What active food buffs do to a survivor; the numbers other systems read.</summary>
public static class FoodEffects
{
    /// <summary>Adds a buff or extends it to <paramref name="until"/> (the stronger strength wins).</summary>
    public static void Give(Survivor s, string id, long until, double strength)
    {
        if (s.Buffs.Find(b => b.Id == id) is { } have)
        {
            have.Until = Math.Max(have.Until, until);
            have.Strength = Math.Max(have.Strength, strength);
            return;
        }
        s.Buffs.Add(new ActiveBuff { Id = id, Until = until, Strength = strength });
    }

    public static IEnumerable<(BuffDef Def, ActiveBuff Active)> Active(Survivor s, GameData data) =>
        data.Food is not { } food ? []
            : s.Buffs.Where(b => food.Buffs.ContainsKey(b.Id)).Select(b => (food.Buffs[b.Id], b));

    /// <summary>Job output multiplier from buffs (Well Fed ×1.08, Hungry ×0.85, ...); strength scales the part above or below 1.</summary>
    public static double OutputFactor(Survivor s, GameData? data) =>
        data?.Food is null || s.Buffs.Count == 0 ? 1 : Active(s, data).Aggregate(1.0, (f, x) => f * (1 + (x.Def.Output - 1) * x.Active.Strength));

    /// <summary>Injury healing multiplier from buffs (Well Fed, Soothed; Hungry slows it).</summary>
    public static double HealingFactor(Survivor s, GameData? data) =>
        data?.Food is null || s.Buffs.Count == 0 ? 1 : Active(s, data).Aggregate(1.0, (f, x) => f * (1 + (x.Def.Healing - 1) * x.Active.Strength));

    /// <summary>AP added on a field run's first round by the buffs a survivor has at <paramref name="tick"/>.</summary>
    public static int FieldFirstRoundAp(Survivor s, GameData data, long tick) =>
        Active(s, data).Where(x => x.Active.Until > tick).Sum(x => x.Def.FieldFirstRoundAp);
}

/// <summary>
/// Food poisoning: the one place food makes someone sick. When the medicine-crafting illness model merges, this
/// becomes its <c>Sickness.Apply(survivor, kind, severity)</c>; until then it is the Food Poisoning debuff.
/// </summary>
/// <summary>
/// Food sickness (bad, spoiled, raw or tainted food, bad rations). With medicine loaded it is a medicine condition
/// (<see cref="Sickness.Apply"/>: Husk sickness for tainted food, food sickness otherwise; an antidote cures it, the Med
/// Bay treats it); without medicine, the food system's own "sick" buff. <paramref name="severity"/> is in food's scale
/// (1 = an ordinary case), turned into a condition severity by medicine.json <c>foodSicknessScale</c>.
/// </summary>
public static class FoodSickness
{
    public static void Apply(ColonyState state, GameData data, Survivor s, string kind, double severity)
    {
        if (data.Medicine is { } med)
        {
            var hadCondition = s.Body.Conditions.Any(c => med.Condition(c.Id)?.Kind == ConditionKind.Sickness);
            if (Sickness.Apply(state, data, s, kind, Math.Min(1, severity * med.Def.FoodSicknessScale), log: false) is not null && !hadCondition)
                state.Log(new(state.Tick, ColonyEventType.FoodPoisoning, $"{s.Name} has food poisoning ({kind} food).", s.Id));
            return;
        }
        if (data.Food is not { } food) return;
        var had = s.Buffs.Any(b => b.Id == food.Rules.BuffIds.Sick);
        FoodEffects.Give(s, food.Rules.BuffIds.Sick, state.Tick + (long)Math.Round(food.Rules.Sickness.Hours * severity * data.Rules.TicksPerDay / 24.0), 1);
        if (!had)
            state.Log(new(state.Tick, ColonyEventType.FoodPoisoning, $"{s.Name} has food poisoning ({kind} food).", s.Id));
    }
}

/// <summary>Adapter for the electronics-devices power grid: a powered Cold Cellar keeps food longer. False until it merges.</summary>
public static class FoodPower
{
    public static bool IsPowered(ColonyState state, GameData data, Building building) => false;
}

/// <summary>Kitchen, Campfire and Smokehouse helpers.</summary>
public static class Cooking
{
    /// <summary>Shelf life multiplier of food a cook makes: preserved food lasts longer by their Preserver perk hook.</summary>
    public static double ShelfMultiplier(GameData data, string itemId, double shelfLifeBonus) =>
        data.Food?.Items.GetValueOrDefault(itemId)?.Tags.Contains("preserved") == true
            ? 1 + shelfLifeBonus * data.Food.Rules.Spoilage.PerkShelfPerPoint
            : 1;
}
