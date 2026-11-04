using Remnants.Core.Data;
using Remnants.Core.Items;

namespace Remnants.Core.Survivors;

/// <summary>
/// What medical items and conditions do to a body, the same at home and in the field (data/medicine.json). Pure: callers
/// pay the item, heal HP and log. A null <see cref="MedicineData"/> (medicine.json not loaded) leaves conditions alone.
/// </summary>
public static class Treatment
{
    /// <summary>
    /// Applies a treatment item's body effects (dressing, cures, splint, painkiller) and returns what it did, in words;
    /// empty when it did nothing for this body. <paramref name="field"/>: the painkiller lasts its rounds, not its ticks.
    /// </summary>
    public static List<string> Apply(MedicineData? med, Body body, UseEffect use, bool field)
    {
        var done = new List<string>();
        if (use.Dress != Dressing.None && Dress(body, use.Dress, use.DressAll) is { } dressed) done.Add(dressed);
        if (med is not null)
        {
            if (use.Cures.Count > 0 && Cure(med, body, use.Cures, use.CureStrength) is { } cured) done.Add(cured);
            if (use.Splint && Splint(med, body) is { } splinted) done.Add(splinted);
            if (use.PainRelief > 0)
            {
                Numb(med, body, use.PainRelief, field ? use.PainRounds : use.PainTicks);
                done.Add("numbed the pain");
            }
        }
        return done;
    }

    /// <summary>Whether the item would do something for this body (for the UI and the Med Bay's choice).</summary>
    public static bool Helps(MedicineData? med, Body body, UseEffect use) =>
        use.Dress != Dressing.None && Undressed(body, use.Dress).Any()
        || med is not null && use.Cures.Any(id => body.Has(id))
        || med is not null && use.Splint && UnsplintedFracture(med, body) is not null
        || med is not null && use.PainRelief > 0 && InPain(med, body) && body.PainRelief + 1e-9 < use.PainRelief;

    /// <summary>Injured parts whose dressing is worse than this one, worst wound first.</summary>
    public static IEnumerable<BodyPart> Undressed(Body body, Dressing dressing) =>
        body.Parts.Where(kv => kv.Value.Condition == PartCondition.Injured && kv.Value.Dressing < dressing)
            .OrderByDescending(kv => kv.Value.Severity).ThenBy(kv => kv.Key).Select(kv => kv.Key);

    private static string? Dress(Body body, Dressing dressing, bool all)
    {
        var parts = Undressed(body, dressing).ToList();
        if (parts.Count == 0) return null;
        foreach (var part in all ? parts : parts.Take(1)) body[part].Dressing = dressing;
        return dressing == Dressing.Clean ? "cleaned and dressed the wounds" : "bandaged the worst wound";
    }

    private static string? Cure(MedicineData med, Body body, List<string> ids, double strength)
    {
        var hit = body.Conditions.Where(c => ids.Contains(c.Id)).ToList();
        if (hit.Count == 0) return null;
        foreach (var c in hit)
        {
            c.Severity -= strength;
            if (c.Severity <= 1e-9) body.Conditions.Remove(c);
        }
        var names = hit.Select(c => med.Condition(c.Id)?.Name.ToLowerInvariant() ?? c.Id).Distinct();
        return hit.All(c => !body.Conditions.Contains(c)) ? $"cured the {string.Join(" and ", names)}" : $"eased the {string.Join(" and ", names)}";
    }

    public static BodyCondition? UnsplintedFracture(MedicineData med, Body body) =>
        body.Conditions.Where(c => c.Id == med.Fracture.Condition && !c.Treated && c.Part is not null)
            .OrderByDescending(c => body[c.Part!.Value].Severity).FirstOrDefault();

    private static string? Splint(MedicineData med, Body body)
    {
        if (UnsplintedFracture(med, body) is not { } fracture) return null;
        fracture.Treated = true;
        body[fracture.Part!.Value].ConditionPenalty = med.Fracture.SplintedPenalty;
        return "splinted the fracture";
    }

    /// <summary>The worst injured part, which a mend goes to.</summary>
    public static BodyPart? WorstPart(Body body) => body.Parts.Where(kv => kv.Value.Condition == PartCondition.Injured)
        .OrderByDescending(kv => kv.Value.Severity).ThenBy(kv => kv.Key).Select(kv => (BodyPart?)kv.Key).FirstOrDefault();

    /// <summary>
    /// Field mending (stitches, a trauma kit): the worst injury's severity drops by <paramref name="amount"/>, healed
    /// outright at 0. Null when nothing is injured.
    /// </summary>
    public static string? Mend(Body body, double amount)
    {
        if (WorstPart(body) is not { } part || amount <= 0) return null;
        var p = body[part];
        p.Severity -= amount;
        if (p.Severity > 1e-9) return $"stitched up the {part}";
        body.Heal(part);
        return $"closed the wound on the {part}";
    }

    /// <summary>
    /// A painkiller dose: the effect (strongest relief, longest duration) and the dose count. Too many doses close together
    /// start a dependency, or deepen one.
    /// </summary>
    public static void Numb(MedicineData med, Body body, double relief, int duration)
    {
        var effect = body.Find(Body.PainkillerConditionId);
        if (effect is null) body.Conditions.Add(effect = new BodyCondition { Id = Body.PainkillerConditionId });
        effect.Severity = Math.Max(effect.Severity, Math.Clamp(relief, 0, 1));
        effect.Remaining = Math.Max(effect.Remaining, duration);
        body.Doses += 1;
        var rules = med.Painkiller;
        if (body.Doses + 1e-9 < rules.DependencyDoses) return;
        var dependency = body.Find(rules.DependencyCondition);
        if (dependency is null) body.Conditions.Add(new BodyCondition { Id = rules.DependencyCondition, Severity = rules.DependencySeverity });
        else dependency.Severity = Math.Min(1, dependency.Severity + rules.DependencySeverity / 2);
    }

    /// <summary>
    /// After a hit on an arm or leg: a fracture (one per part) when the injury is bad enough and <paramref name="roll"/>
    /// (0..1) is under the chance. Returns whether one started. A missing medicine.json means no fractures.
    /// </summary>
    public static bool MaybeFracture(MedicineData? med, Body body, BodyPart part, double roll)
    {
        if (med is null || Body.RegionOf(part) is not (BodyRegion.Arms or BodyRegion.Legs)) return false;
        var p = body[part];
        var rules = med.Fracture;
        if (p.Condition != PartCondition.Injured || p.Severity + 1e-9 < rules.MinSeverity || body.Find(rules.Condition, part) is not null) return false;
        if (roll >= rules.Chance) return false;
        Fracture(med, body, part);
        return true;
    }

    /// <summary>Breaks a limb outright (raids, falls: other features call this).</summary>
    public static void Fracture(MedicineData med, Body body, BodyPart part)
    {
        if (body[part].Condition != PartCondition.Injured || body.Find(med.Fracture.Condition, part) is not null) return;
        body.Conditions.Add(new BodyCondition { Id = med.Fracture.Condition, Part = part, Severity = 1 });
        body[part].ConditionPenalty = med.Fracture.Penalty;
    }

    /// <summary>Adds a condition, or raises the one already there to at least this severity. Returns it.</summary>
    public static BodyCondition Afflict(Body body, string id, double severity, BodyPart? part = null)
    {
        var c = body.Find(id, part);
        if (c is null) body.Conditions.Add(c = new BodyCondition { Id = id, Part = part });
        c.Severity = Math.Clamp(Math.Max(c.Severity, severity), 0, 1);
        return c;
    }

    /// <summary>The worst injury is bad enough to hurt (and wear them out), and no painkiller is dulling it.</summary>
    public static bool InPain(MedicineData med, Body body) =>
        WorstInjury(body) + 1e-9 >= med.Pain.Threshold && body.PainRelief < 0.5;

    public static double WorstInjury(Body body) =>
        body.Parts.Values.Where(p => p.Condition == PartCondition.Injured).Select(p => p.Severity).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Job output multiplier from conditions and pain: 1 − the summed penalties (each condition's at its severity), never
    /// below 0.2. 1 without medicine data.
    /// </summary>
    public static double OutputFactor(MedicineData? med, Body body)
    {
        if (med is null) return 1;
        var penalty = body.Conditions.Sum(c => (med.Condition(c.Id)?.OutputPenalty ?? 0) * Math.Clamp(c.Severity, 0, 1));
        if (InPain(med, body)) penalty += med.Pain.OutputPenalty;
        return Math.Max(0.2, 1 - penalty);
    }

    /// <summary>AP a turn in the field loses to conditions and pain (at most <c>field.maxApPenalty</c>).</summary>
    public static int FieldApPenalty(MedicineData? med, Body body)
    {
        if (med is null) return 0;
        var ap = body.Conditions.Select(c => med.Condition(c.Id) is { FieldApPenalty: > 0 } d && c.Severity + 1e-9 >= d.FieldApFrom ? d.FieldApPenalty : 0).DefaultIfEmpty(0).Max();
        if (InPain(med, body)) ap += med.Pain.FieldApPenalty;
        return Math.Min(ap, med.Def.Field.MaxApPenalty);
    }

    /// <summary>
    /// How fast an injured part heals relative to normal: 0 while an infection blocks it, slower with an unsplinted
    /// fracture, a little faster splinted.
    /// </summary>
    public static double HealFactor(MedicineData? med, Body body, BodyPart part)
    {
        if (med is null) return 1;
        var factor = 1.0;
        foreach (var c in body.Conditions.Where(c => c.Part == part))
        {
            if (med.Condition(c.Id) is { BlocksHealing: true }) return 0;
            if (c.Id == med.Fracture.Condition) factor *= c.Treated ? med.Fracture.SplintedHealMultiplier : med.Fracture.UnsplintedHealMultiplier;
        }
        return factor;
    }

    /// <summary>Passing effects (painkillers) count down one tick or round, and wear off at 0.</summary>
    public static void CountDown(Body body)
    {
        foreach (var c in body.Conditions.Where(c => c.Id == Body.PainkillerConditionId).ToList())
            if (--c.Remaining <= 0) body.Conditions.Remove(c);
    }
}
