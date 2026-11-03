using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// What the player picks for a new colony. <see cref="StartingHabitants"/>: the Sandbox's number of people (null = the
/// rules' default start); clamped to <c>rules.newGame.habitantsRange</c> when the colony is made.
/// </summary>
public sealed record NewGameOptions
{
    public int? StartingHabitants { get; init; }
}

/// <summary>
/// Builds the starting colony from rules.newGame: adults (at least one man and one woman),
/// children, starting stock and items and free starting buildings, all from one seed. A chosen number of habitants
/// (<see cref="NewGameOptions"/>) keeps the default's share of children (rounded down; at least one from three people up).
/// </summary>
public static class NewGame
{
    public static ColonySim Create(GameData data, ulong seed, ScavengeData? scavenge = null, NewGameOptions? options = null)
    {
        var rules = data.Rules.NewGame;
        var rng = new DeterministicRng(seed);
        options ??= new NewGameOptions();
        if (options.StartingHabitants is { } asked)
            options = options with { StartingHabitants = Math.Clamp(asked, rules.HabitantsRange[0], rules.HabitantsRange[1]) };
        var (adultCount, childCount) = Composition(rules, options.StartingHabitants);
        var state = new ColonyState { Tick = rules.StartHour, NewGameOptions = options };

        foreach (var (res, amount) in rules.Stock) state.Stockpile.Add(res, amount);
        foreach (var (item, count) in rules.Items.OrderBy(kv => kv.Key, StringComparer.Ordinal)) state.Inventory.Add(item, count);
        foreach (var tier in data.Vehicles.Starting) VehicleRules.Add(state, data, tier);
        if (scavenge is not null) ColonyCards.GrantStarter(state, scavenge.Cards);
        Pantry.Reconcile(state, data);
        for (var plot = 0; plot < rules.Buildings.Count; plot++)
            state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = rules.Buildings[plot], Level = 1, Plot = plot });

        var usedNames = new HashSet<string>();
        for (var i = 0; i < adultCount; i++)
        {
            // First two adults are one man and one woman; the rest are random.
            var sex = i switch { 0 => Sex.Male, 1 => Sex.Female, _ => rng.Next(0, 2) == 0 ? Sex.Male : Sex.Female };
            state.Survivors.Add(MakeSurvivor(state, data, rng, sex, LifeStage.Adult, usedNames));
        }
        for (var i = 0; i < childCount; i++)
        {
            var sex = rng.Next(0, 2) == 0 ? Sex.Male : Sex.Female;
            state.Survivors.Add(MakeSurvivor(state, data, rng, sex, LifeStage.Child, usedNames));
        }

        state.RngState = rng.State;
        var sim = new ColonySim(data, state, scavenge);
        // Hand out the starting beds so nobody begins the game homeless (or logs a "found a bed" event).
        foreach (var s in state.Survivors.OrderBy(s => s.Id).Take(sim.BedCapacity)) s.HasBed = true;
        return sim;
    }

    /// <summary>Adults and children for a start of <paramref name="habitants"/> people (null = the rules' default).</summary>
    public static (int Adults, int Children) Composition(NewGameRules rules, int? habitants)
    {
        if (habitants is not { } n) return (rules.Adults, rules.Children);
        var children = rules.Habitants == 0 ? 0 : n * rules.Children / rules.Habitants;
        if (n >= 3 && rules.Children > 0) children = Math.Max(1, children);
        return (n - children, children);
    }

    private static Survivor MakeSurvivor(
        ColonyState state, GameData data, DeterministicRng rng, Sex sex, LifeStage stage, HashSet<string> usedNames)
    {
        var rules = data.Rules.NewGame;
        var ages = stage == LifeStage.Adult ? rules.AdultAgeRange : rules.ChildAgeRange;
        var stats = stage == LifeStage.Adult ? rules.AttributeRange : rules.ChildAttributeRange;

        var survivor = new Survivor
        {
            Id = state.NextSurvivorId++,
            Name = PickName(rng, sex == Sex.Male ? rules.MaleNames : rules.FemaleNames, usedNames),
            Sex = sex,
            LifeStage = stage,
            AgeTicks = rng.NextInclusive(ages[0], ages[1]) * data.Rules.TicksPerYear + rng.Next(0, (int)data.Rules.TicksPerYear),
        };
        foreach (var attribute in Enum.GetValues<AttributeKind>())
            survivor.Set(attribute, rng.NextInclusive(stats[0], stats[1]));
        if (stage != LifeStage.Adult)
        {
            // Children are part-way through growing up, towards adult-range aptitudes.
            survivor.GrowthTicks = Bonds.GrowthAtAge(survivor.AgeTicks, data.Rules);
            survivor.LifeStage = Bonds.StageAt(survivor.GrowthTicks, data.Rules.Life);
            survivor.AgeTicks = Bonds.AgeTicksAt(survivor.GrowthTicks, data.Rules);
            foreach (var attribute in Enum.GetValues<AttributeKind>())
                survivor.Aptitude[attribute] = Math.Max(survivor.Get(attribute), rng.NextInclusive(rules.AttributeRange[0], rules.AttributeRange[1]));
        }
        return survivor;
    }

    private static string PickName(DeterministicRng rng, List<string> names, HashSet<string> used)
    {
        var free = names.Where(n => !used.Contains(n)).ToList();
        var name = free.Count > 0 ? rng.Pick(free) : $"{rng.Pick(names)} {used.Count + 1}";
        used.Add(name);
        return name;
    }
}
