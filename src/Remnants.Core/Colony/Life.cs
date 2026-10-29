using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// Couples, families and growing up: the formulas (see <see cref="RelationshipRules"/>, <see cref="FamilyRules"/>,
/// <see cref="LifeRules"/>) and the queries the UI needs. Pure except for <see cref="OnGone"/>.
/// </summary>
public static class Bonds
{
    /// <summary>1 for identical attributes, 0 for opposite extremes.</summary>
    public static double Similarity(Survivor a, Survivor b) =>
        1 - Enum.GetValues<AttributeKind>().Average(k => Math.Abs(a.Get(k) - b.Get(k))) / Survivor.MaxStat;

    /// <summary>Two single adults at home can grow closer.</summary>
    public static bool CanBond(Survivor a, Survivor b) =>
        a.Id != b.Id && !a.IsChild && !b.IsChild && a.PartnerId is null && b.PartnerId is null && !a.Away && !b.Away;

    public static double AffinityPerTick(Survivor a, Survivor b, PairStance stance, RelationshipRules rules)
    {
        if (stance == PairStance.Discourage) return 0;
        var gain = rules.BasePerTick + rules.CompatibilityPerTick * Similarity(a, b);
        if (a.AssignedBuildingId is { } work && work == b.AssignedBuildingId) gain += rules.SharedWorkplacePerTick;
        if (a.HomeBuildingId is { } home && home == b.HomeBuildingId) gain += rules.SharedHomePerTick;
        return stance == PairStance.Encourage ? gain * rules.EncourageMultiplier : gain;
    }

    public static double Affinity(ColonyState state, int a, int b) => state.FindRelationship(a, b)?.Affinity ?? 0;

    public static PairStance Stance(ColonyState state, int a, int b) => state.FindRelationship(a, b)?.Stance ?? PairStance.Neutral;

    public static IEnumerable<Survivor> Children(ColonyState state, Survivor s) =>
        state.Survivors.Where(c => c.MotherId == s.Id || c.FatherId == s.Id).OrderBy(c => c.Id);

    /// <summary>The people this survivor has grown close to, highest affinity first.</summary>
    public static IEnumerable<(Survivor Other, Relationship Bond)> Closest(ColonyState state, Survivor s) =>
        state.Relationships.Where(r => r.Involves(s.Id))
            .Select(r => (Other: state.FindSurvivor(r.Other(s.Id))!, Bond: r))
            .Where(x => x.Other is not null)
            .OrderByDescending(x => x.Bond.Affinity).ThenBy(x => x.Other.Id);

    /// <summary>Beds not yet taken by someone living here or promised to a baby on the way.</summary>
    public static int FreeBedsForBabies(ColonyState state, GameData data) =>
        HousingSystem.Capacity(state, data) - state.Survivors.Count - state.Survivors.Count(s => s.Pregnancy is not null);

    /// <summary>Food in stock is enough to start a family: at least <c>minFoodPerPerson</c> per person.</summary>
    public static bool EnoughFoodForBabies(ColonyState state, GameData data) =>
        state.Stockpile.Get(data.Rules.Food.ResourceId) + 1e-9
        >= data.Rules.Family.MinFoodPerPerson * (state.Survivors.Count + state.Survivors.Count(s => s.Pregnancy is not null));

    /// <summary>A woman and a man in a couple, both adults at home; she is not pregnant and has no infant.</summary>
    public static bool CanConceive(ColonyState state, Survivor mother, Survivor father) =>
        mother.Sex == Sex.Female && father.Sex == Sex.Male
        && mother.PartnerId == father.Id && father.PartnerId == mother.Id
        && !mother.IsChild && !father.IsChild && !mother.Away && !father.Away
        && mother.Pregnancy is null
        && !state.Survivors.Any(c => c.MotherId == mother.Id && c.IsInfant);

    /// <summary>The stage that <paramref name="growthTicks"/> of growing up reaches.</summary>
    public static LifeStage StageAt(long growthTicks, LifeRules rules) =>
        growthTicks < rules.InfantTicks ? LifeStage.Infant
        : growthTicks < rules.GrowthTicks ? LifeStage.Child
        : LifeStage.Adult;

    /// <summary>Share of the current stage done, 0..1 (1 for adults).</summary>
    public static double StageProgress(Survivor s, LifeRules rules) => s.LifeStage switch
    {
        LifeStage.Infant => Math.Clamp((double)s.GrowthTicks / rules.InfantTicks, 0, 1),
        LifeStage.Child => Math.Clamp((double)(s.GrowthTicks - rules.InfantTicks) / rules.ChildTicks, 0, 1),
        _ => 1,
    };

    /// <summary>Ticks until the next life stage, or null for adults.</summary>
    public static long? TicksToNextStage(Survivor s, LifeRules rules) => s.LifeStage switch
    {
        LifeStage.Infant => Math.Max(0, rules.InfantTicks - s.GrowthTicks),
        LifeStage.Child => Math.Max(0, rules.GrowthTicks - s.GrowthTicks),
        _ => null,
    };

    /// <summary>A minor's age: their share of growing up, mapped onto <c>adultAge</c> years.</summary>
    public static long AgeTicksAt(long growthTicks, RulesDef rules) =>
        growthTicks * rules.AdultAge * rules.TicksPerYear / rules.Life.GrowthTicks;

    /// <summary>The growth a minor of this age has done (inverse of <see cref="AgeTicksAt"/>).</summary>
    public static long GrowthAtAge(long ageTicks, RulesDef rules) =>
        Math.Min(rules.Life.GrowthTicks - 1, ageTicks * rules.Life.GrowthTicks / (rules.AdultAge * rules.TicksPerYear));

    /// <summary>Ticks of the pregnancy left, or null when not pregnant.</summary>
    public static long? TicksToBirth(ColonyState state, Survivor s) =>
        s.Pregnancy is { } p ? Math.Max(0, p.DueTick - state.Tick) : null;

    /// <summary>The child's tendencies: the parents' mean attribute ± <c>inheritVariation</c>.</summary>
    internal static Dictionary<AttributeKind, double> InheritAptitude(Survivor mother, Survivor father, LifeRules rules, DeterministicRng rng) =>
        Enum.GetValues<AttributeKind>().ToDictionary(k => k, k =>
            Math.Clamp((mother.Get(k) + father.Get(k)) / 2 + (rng.NextDouble() * 2 - 1) * rules.InheritVariation, 0, Survivor.MaxStat));

    /// <summary>
    /// Someone died or left: their partner and (if they died) family grieve, the couple ends, their bonds are
    /// dropped and a pregnancy ends with them. Call after removing them from the colony.
    /// </summary>
    internal static void OnGone(ColonyState state, GameData data, Survivor gone, bool died)
    {
        var mood = data.Rules.Mood;
        if (gone.PartnerId is { } partnerId && state.FindSurvivor(partnerId) is { } partner)
        {
            partner.PartnerId = null;
            partner.Mood = Math.Max(0, partner.Mood - (died ? mood.PartnerGrief : mood.Heartbreak));
            if (died) state.Log(new(state.Tick, ColonyEventType.Grieving, $"{partner.Name} grieves for {gone.Name}.", partner.Id));
        }
        gone.PartnerId = null;
        if (died)
        {
            var family = state.Survivors.Where(s => s.Id == gone.MotherId || s.Id == gone.FatherId || s.MotherId == gone.Id || s.FatherId == gone.Id);
            foreach (var s in family) s.Mood = Math.Max(0, s.Mood - mood.FamilyGrief);
        }
        state.Relationships.RemoveAll(r => r.Involves(gone.Id));
    }

    internal static void Couple(ColonyState state, Survivor a, Survivor b)
    {
        a.PartnerId = b.Id;
        b.PartnerId = a.Id;
        state.Log(new(state.Tick, ColonyEventType.NewCouple, $"{a.Name} and {b.Name} are now a couple.", a.Id));
    }

    internal static void BreakUp(ColonyState state, GameData data, Survivor a, Survivor b)
    {
        a.PartnerId = null;
        b.PartnerId = null;
        a.Mood = Math.Max(0, a.Mood - data.Rules.Mood.Heartbreak);
        b.Mood = Math.Max(0, b.Mood - data.Rules.Mood.Heartbreak);
        if (state.FindRelationship(a.Id, b.Id) is { } r) r.Affinity = 0;
        state.Log(new(state.Tick, ColonyEventType.BrokeUp, $"{a.Name} and {b.Name} split up.", a.Id));
    }
}

/// <summary>
/// Moods recover; single adults at home grow closer (see <see cref="Bonds.AffinityPerTick"/>) and pairs that
/// reach the threshold become couples, closest pair first.
/// </summary>
internal static class RelationshipSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        var rules = data.Rules.Relationships;
        foreach (var s in state.Survivors)
            s.Mood = Math.Min(Survivor.MaxMood, s.Mood + data.Rules.Mood.RecoveryPerTick);

        var singles = state.Survivors.Where(s => !s.IsChild && s.PartnerId is null && !s.Away).OrderBy(s => s.Id).ToList();
        for (var i = 0; i < singles.Count; i++)
            for (var j = i + 1; j < singles.Count; j++)
            {
                var (a, b) = (singles[i], singles[j]);
                var r = state.FindRelationship(a.Id, b.Id);
                var gain = Bonds.AffinityPerTick(a, b, r?.Stance ?? PairStance.Neutral, rules);
                if (gain <= 0) continue;
                if (r is null) state.Relationships.Add(r = new Relationship { A = a.Id, B = b.Id });
                r.Affinity = Math.Min(rules.CoupleThreshold, r.Affinity + gain);
            }

        var ready = state.Relationships
            .Where(r => r.Affinity >= rules.CoupleThreshold && r.Stance != PairStance.Discourage)
            .OrderByDescending(r => r.Affinity).ThenBy(r => r.A).ThenBy(r => r.B).ToList();
        foreach (var r in ready)
        {
            if (state.FindSurvivor(r.A) is not { } a || state.FindSurvivor(r.B) is not { } b || !Bonds.CanBond(a, b)) continue;
            Bonds.Couple(state, a, b);
        }
    }
}

/// <summary>
/// Births first (a newborn joins as an infant without a bed; housing runs next), then every
/// <c>conceptionCheckTicks</c> each eligible couple may conceive (see <see cref="FamilyRules"/>).
/// </summary>
internal static class FamilySystem
{
    public static void Run(ColonyState state, GameData data, DeterministicRng rng)
    {
        foreach (var mother in state.Survivors.Where(s => s.Pregnancy is { } p && state.Tick >= p.DueTick).OrderBy(s => s.Id).ToList())
            Birth(state, data, rng, mother);

        var rules = data.Rules.Family;
        if (state.Tick % rules.ConceptionCheckTicks != 0) return;
        foreach (var mother in state.Survivors.Where(s => s.Sex == Sex.Female && s.PartnerId is not null).OrderBy(s => s.Id).ToList())
        {
            if (state.FindSurvivor(mother.PartnerId!.Value) is not { } father || !Bonds.CanConceive(state, mother, father)) continue;
            if (Bonds.FreeBedsForBabies(state, data) <= 0 || !Bonds.EnoughFoodForBabies(state, data)) continue;
            if (rng.NextDouble() >= rules.ConceptionChance) continue;
            mother.Pregnancy = new Pregnancy
            {
                FatherId = father.Id,
                ConceivedTick = state.Tick,
                DueTick = state.Tick + rules.PregnancyTicks,
                Aptitude = Bonds.InheritAptitude(mother, father, data.Rules.Life, rng),
            };
            state.Log(new(state.Tick, ColonyEventType.Pregnant, $"{mother.Name} is expecting a child with {father.Name}.", mother.Id));
        }
    }

    private static void Birth(ColonyState state, GameData data, DeterministicRng rng, Survivor mother)
    {
        var pregnancy = mother.Pregnancy!;
        mother.Pregnancy = null;
        var sex = rng.Next(0, 2) == 0 ? Sex.Male : Sex.Female;
        var baby = new Survivor
        {
            Id = state.NextSurvivorId++,
            Name = PickName(state, rng, sex == Sex.Male ? data.Rules.Family.MaleNames : data.Rules.Family.FemaleNames),
            Sex = sex,
            LifeStage = LifeStage.Infant,
            MotherId = mother.Id,
            FatherId = pregnancy.FatherId,
            Aptitude = new(pregnancy.Aptitude),
        };
        foreach (var (attribute, value) in pregnancy.Aptitude) baby.Set(attribute, value * data.Rules.Life.BirthAttributeFraction);
        state.Survivors.Add(baby);
        var father = pregnancy.FatherId is { } f ? state.FindSurvivor(f)?.Name : null;
        var parents = father is null ? mother.Name : $"{mother.Name} and {father}";
        state.Log(new(state.Tick, ColonyEventType.Born, $"{baby.Name} was born to {parents}.", baby.Id));
    }

    /// <summary>A name nobody in the colony has yet; otherwise a numbered one.</summary>
    private static string PickName(ColonyState state, DeterministicRng rng, List<string> names)
    {
        var used = state.Survivors.Select(s => s.Name).ToHashSet();
        var free = names.Where(n => !used.Contains(n)).ToList();
        if (free.Count > 0) return rng.Pick(free);
        var name = rng.Pick(names);
        for (var n = 2; ; n++)
            if (!used.Contains($"{name} {n}")) return $"{name} {n}";
    }
}

/// <summary>
/// Adults age; infants and children grow. A fed minor's attributes move towards their aptitude; a hungry one
/// does not grow stronger that tick (and it is counted). Stages change at the <see cref="LifeRules"/> thresholds.
/// </summary>
internal static class GrowthSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        var rules = data.Rules.Life;
        foreach (var s in state.Survivors)
        {
            if (!s.IsChild)
            {
                s.AgeTicks++;
                continue;
            }

            s.GrowthTicks++;
            if (s.StarvingTicks > 0) s.MalnourishedTicks++;
            else
                foreach (var (attribute, target) in s.Aptitude)
                    s.Set(attribute, s.Get(attribute) + (target - s.Get(attribute)) * rules.AttributeGrowthPerTick);
            s.AgeTicks = Bonds.AgeTicksAt(s.GrowthTicks, data.Rules);

            var stage = Bonds.StageAt(s.GrowthTicks, rules);
            if (stage == s.LifeStage) continue;
            s.LifeStage = stage;
            if (stage == LifeStage.Child)
                state.Log(new(state.Tick, ColonyEventType.BecameChild, $"{s.Name} is no longer a baby.", s.Id));
            else
            {
                s.Aptitude.Clear();
                var hungry = (double)s.MalnourishedTicks / rules.GrowthTicks >= rules.MalnourishedNoteShare;
                state.Log(new(state.Tick, ColonyEventType.BecameAdult,
                    hungry ? $"{s.Name} came of age, weakened by hunger." : $"{s.Name} came of age.", s.Id));
            }
        }
    }
}

internal static class LifeHandlers
{
    public static CommandResult Matchmake(ColonySim sim, Matchmake c)
    {
        var state = sim.State;
        if (state.FindSurvivor(c.SurvivorId) is not { } a) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (state.FindSurvivor(c.OtherId) is not { } b) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.OtherId}.");
        if (a.Id == b.Id) return CommandResult.Fail(CommandError.SamePerson, "Pick two different people.");
        if (a.IsChild || b.IsChild) return CommandResult.Fail(CommandError.NotAnAdult, $"{(a.IsChild ? a : b).Name} is not an adult.");
        var partners = a.PartnerId == b.Id;
        if (c.Stance == PairStance.Encourage && !partners && (a.PartnerId is not null || b.PartnerId is not null))
            return CommandResult.Fail(CommandError.AlreadyPartnered, $"{(a.PartnerId is not null ? a : b).Name} already has a partner.");

        var r = state.FindRelationship(a.Id, b.Id);
        if (r is null) state.Relationships.Add(r = new Relationship { A = Math.Min(a.Id, b.Id), B = Math.Max(a.Id, b.Id) });
        r.Stance = c.Stance;
        if (c.Stance == PairStance.Discourage && partners)
        {
            Bonds.BreakUp(state, sim.Data, a, b);
            return CommandResult.Success($"{a.Name} and {b.Name} split up.");
        }
        return CommandResult.Success(c.Stance switch
        {
            PairStance.Encourage => $"Encouraging {a.Name} and {b.Name}.",
            PairStance.Discourage => $"Keeping {a.Name} and {b.Name} apart.",
            _ => $"Leaving {a.Name} and {b.Name} be.",
        });
    }
}
