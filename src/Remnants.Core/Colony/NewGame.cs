using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// Builds the starting colony from rules.newGame: adults (at least one man and one woman),
/// children, starting stock and items and free starting buildings, all from one seed.
/// </summary>
public static class NewGame
{
    public static ColonySim Create(GameData data, ulong seed, ScavengeData? scavenge = null)
    {
        var rules = data.Rules.NewGame;
        var rng = new DeterministicRng(seed);
        var state = new ColonyState();

        foreach (var (res, amount) in rules.Stock) state.Stockpile.Add(res, amount);
        foreach (var (item, count) in rules.Items.OrderBy(kv => kv.Key, StringComparer.Ordinal)) state.Inventory.Add(item, count);
        for (var plot = 0; plot < rules.Buildings.Count; plot++)
            state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = rules.Buildings[plot], Level = 1, Plot = plot });

        var usedNames = new HashSet<string>();
        for (var i = 0; i < rules.Adults; i++)
        {
            // First two adults are one man and one woman; the rest are random.
            var sex = i switch { 0 => Sex.Male, 1 => Sex.Female, _ => rng.Next(0, 2) == 0 ? Sex.Male : Sex.Female };
            state.Survivors.Add(MakeSurvivor(state, data, rng, sex, LifeStage.Adult, usedNames));
        }
        for (var i = 0; i < rules.Children; i++)
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
