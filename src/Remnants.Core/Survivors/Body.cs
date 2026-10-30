namespace Remnants.Core.Survivors;

public sealed class BodyPartState
{
    public PartCondition Condition { get; set; } = PartCondition.Healthy;

    /// <summary>Injury severity 0 (scratch) to 1 (useless). Only meaningful while Injured.</summary>
    public double Severity { get; set; }

    /// <summary>Efficiency of a fitted prosthetic. Can exceed 1: cybernetics may beat the original.</summary>
    public double ProstheticQuality { get; set; }

    /// <summary>Item id of the fitted prosthetic, for display. Null for prosthetics from before items existed.</summary>
    public string? ProstheticItemId { get; set; }

    /// <summary>
    /// Humanity this prosthetic costs its wearer, fixed when it was fitted (more for an elective augmentation of a healthy
    /// part than for a replacement; see <c>cybernetics.json</c> tiers).
    /// </summary>
    public double HumanityCost { get; set; }

    /// <summary>Fitted onto a healthy part by choice (an augmentation), not to replace a lost one.</summary>
    public bool Elective { get; set; }

    /// <summary>
    /// Upkeep of a powered prosthetic, 1 (serviced) down to the tier's floor: multiplies its quality. It drops while
    /// maintenance is overdue and the colony cannot pay for the parts.
    /// </summary>
    public double Upkeep { get; set; } = 1;

    /// <summary>Ticks since the prosthetic was last serviced (only powered tiers need it).</summary>
    public int ServiceTicks { get; set; }

    /// <summary>
    /// Scavenge only: a Network-grade part locked up by interference this round (it works at 0 until the next roll).
    /// Never set in the colony.
    /// </summary>
    public bool Interfered { get; set; }

    /// <summary>How an injured part is dressed (see <c>data/medicine.json</c>): a dirty wound may get infected. A new injury undoes it.</summary>
    public Dressing Dressing { get; set; }

    /// <summary>
    /// Efficiency lost on top of the injury to a condition on this part (a fracture: less once splinted). Kept in step by
    /// the medicine rules, so the body needs no data to know how well the part works.
    /// </summary>
    public double ConditionPenalty { get; set; }

    /// <summary>How well this part works, as a multiplier (1 = healthy).</summary>
    public double Efficiency => Condition switch
    {
        PartCondition.Healthy => 1.0,
        PartCondition.Injured => Math.Clamp(1.0 - Severity - ConditionPenalty, 0.0, 1.0),
        PartCondition.Missing => 0.0,
        PartCondition.Prosthetic => Interfered ? 0.0 : Math.Max(0.0, ProstheticQuality * Upkeep),
        _ => 1.0,
    };
}

/// <summary>How a wound is dressed: a bandage keeps most dirt out, a clean dressing or antiseptic all of it.</summary>
public enum Dressing { None, Bandaged, Clean }

/// <summary>
/// A condition the body carries (data/medicine.json <c>conditions</c>): an infection, a fracture, a sickness, a dependency, or
/// a passing effect (a painkiller). <see cref="Part"/> is set for conditions of one body part. <see cref="Severity"/> is
/// 0..1 (for an effect, its strength); <see cref="Remaining"/> counts down for effects (colony ticks, or rounds in the
/// field); <see cref="Treated"/> marks a fracture splinted or an infection under care.
/// </summary>
public sealed class BodyCondition
{
    public string Id { get; set; } = "";
    public BodyPart? Part { get; set; }
    public double Severity { get; set; }
    public int Remaining { get; set; }
    public bool Treated { get; set; }
}

/// <summary>Body-part model: head (with the eyes), torso, two arms, two legs, plus the conditions it carries.</summary>
public sealed class Body
{
    public const double MaxHumanity = 100;

    /// <summary>Conditions (infections, fractures, sicknesses, effects), in the order they started. See <c>Medicine</c>.</summary>
    public List<BodyCondition> Conditions { get; set; } = new();

    /// <summary>Humanity lost to something other than a prosthetic (a Linked "Calm" injector), kept for good.</summary>
    public double HumanityLoss { get; set; }

    /// <summary>Painkiller doses taken recently; it decays, and too many at once grow a dependency.</summary>
    public double Doses { get; set; }

    /// <summary>Share of injury penalties ignored right now (the strongest painkiller effect; 0 without one).</summary>
    public double PainRelief => Conditions.Where(c => c.Id == PainkillerConditionId).Select(c => c.Severity).DefaultIfEmpty(0).Max();

    /// <summary>The condition id painkillers put on the body (see <c>medicine.json</c> <c>painkiller</c>).</summary>
    public const string PainkillerConditionId = "painkilled";

    public BodyCondition? Find(string id, BodyPart? part = null) => Conditions.Find(c => c.Id == id && (part is null || c.Part == part));

    public bool Has(string id) => Conditions.Any(c => c.Id == id);

    private Dictionary<BodyPart, BodyPartState> _parts = Enum.GetValues<BodyPart>().ToDictionary(p => p, _ => new BodyPartState());

    /// <summary>Every part, always complete: a body from an older save gets the parts it lacks (the eyes) as healthy.</summary>
    public Dictionary<BodyPart, BodyPartState> Parts
    {
        get => _parts;
        set
        {
            _parts = value;
            foreach (var part in Enum.GetValues<BodyPart>()) _parts.TryAdd(part, new BodyPartState());
        }
    }

    public BodyPartState this[BodyPart part] => Parts[part];

    public static BodyRegion RegionOf(BodyPart part) => part switch
    {
        BodyPart.Head or BodyPart.Eyes => BodyRegion.Head,
        BodyPart.Torso => BodyRegion.Torso,
        BodyPart.LeftArm or BodyPart.RightArm => BodyRegion.Arms,
        _ => BodyRegion.Legs,
    };

    /// <summary>
    /// 100 minus what the fitted prosthetics cost (<see cref="BodyPartState.HumanityCost"/>), never below 0. "Keep them
    /// human": low humanity costs mood and relationships in the colony.
    /// </summary>
    public double Humanity => Math.Max(0, MaxHumanity - HumanityLoss - Parts.Values.Where(p => p.Condition == PartCondition.Prosthetic).Sum(p => p.HumanityCost));

    public void Injure(BodyPart part, double severity)
    {
        var state = Parts[part];
        if (state.Condition is PartCondition.Missing or PartCondition.Prosthetic) return;
        state.Condition = PartCondition.Injured;
        state.Severity = Math.Clamp(Math.Max(state.Severity, severity), 0.0, 1.0);
        state.Dressing = Dressing.None;
    }

    /// <summary>The injury is gone; so are the conditions of the part that go with it (a fracture knits, a wound infection clears).</summary>
    public void Heal(BodyPart part)
    {
        var state = Parts[part];
        if (state.Condition != PartCondition.Injured) return;
        state.Condition = PartCondition.Healthy;
        state.Severity = 0;
        state.Dressing = Dressing.None;
        state.ConditionPenalty = 0;
        Conditions.RemoveAll(c => c.Part == part);
    }

    public void Lose(BodyPart part)
    {
        var state = Parts[part];
        state.Condition = PartCondition.Missing;
        ClearProsthetic(state);
        Conditions.RemoveAll(c => c.Part == part);
    }

    /// <summary>
    /// How well a part works with the body's state: the injury (and its condition penalty) counts for less under a
    /// painkiller (<see cref="PainRelief"/>). Other conditions are the same as <see cref="BodyPartState.Efficiency"/>.
    /// </summary>
    public double PartEfficiency(BodyPart part)
    {
        var p = Parts[part];
        if (p.Condition != PartCondition.Injured) return p.Efficiency;
        return Math.Clamp(1.0 - (p.Severity + p.ConditionPenalty) * (1 - Math.Clamp(PainRelief, 0, 1)), 0.0, 1.0);
    }

    public void FitProsthetic(BodyPart part, double quality, string? itemId = null)
    {
        var state = Parts[part];
        if (state.Condition != PartCondition.Missing)
            throw new InvalidOperationException($"{part} is not missing; cannot fit a prosthetic.");
        Fit(part, quality, itemId);
    }

    /// <summary>
    /// Fits a prosthetic whatever the part's condition (surgery: a replacement, an upgrade of an older prosthetic, or an
    /// elective augmentation). Resets upkeep; the caller sets the humanity cost and the elective flag.
    /// </summary>
    public void Fit(BodyPart part, double quality, string? itemId)
    {
        var state = Parts[part];
        ClearProsthetic(state);
        state.Condition = PartCondition.Prosthetic;
        state.ProstheticQuality = quality;
        state.ProstheticItemId = itemId;
    }

    private static void ClearProsthetic(BodyPartState state)
    {
        state.Severity = 0;
        state.ProstheticQuality = 0;
        state.ProstheticItemId = null;
        state.HumanityCost = 0;
        state.Elective = false;
        state.Upkeep = 1;
        state.ServiceTicks = 0;
        state.Interfered = false;
        state.Dressing = Dressing.None;
        state.ConditionPenalty = 0;
    }

    /// <summary>
    /// Average efficiency of the parts in a region. The head is the exception: the eyes work through it, so the head's
    /// efficiency is the head's times the eyes' (healthy eyes change nothing, an eye implant scales sight and aim).
    /// </summary>
    public double RegionEfficiency(BodyRegion region)
    {
        if (region == BodyRegion.Head) return PartEfficiency(BodyPart.Head) * PartEfficiency(BodyPart.Eyes);
        var parts = Parts.Where(kv => RegionOf(kv.Key) == region).Select(kv => PartEfficiency(kv.Key)).ToList();
        return parts.Count == 0 ? 1.0 : parts.Average();
    }

    /// <summary>Weighted region efficiency; empty weights fall back to the whole-body average.</summary>
    public double WeightedEfficiency(IReadOnlyDictionary<BodyRegion, double> weights)
    {
        var total = weights.Values.Sum();
        if (total <= 0)
            return Enum.GetValues<BodyRegion>().Average(RegionEfficiency);
        return weights.Sum(kv => kv.Value * RegionEfficiency(kv.Key)) / total;
    }
}
