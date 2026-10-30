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

    /// <summary>How well this part works, as a multiplier (1 = healthy).</summary>
    public double Efficiency => Condition switch
    {
        PartCondition.Healthy => 1.0,
        PartCondition.Injured => Math.Clamp(1.0 - Severity, 0.0, 1.0),
        PartCondition.Missing => 0.0,
        PartCondition.Prosthetic => Interfered ? 0.0 : Math.Max(0.0, ProstheticQuality * Upkeep),
        _ => 1.0,
    };
}

/// <summary>Body-part model: head (with the eyes), torso, two arms, two legs. Data and modifiers only for now.</summary>
public sealed class Body
{
    public const double MaxHumanity = 100;

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
    public double Humanity => Math.Max(0, MaxHumanity - Parts.Values.Where(p => p.Condition == PartCondition.Prosthetic).Sum(p => p.HumanityCost));

    public void Injure(BodyPart part, double severity)
    {
        var state = Parts[part];
        if (state.Condition is PartCondition.Missing or PartCondition.Prosthetic) return;
        state.Condition = PartCondition.Injured;
        state.Severity = Math.Clamp(Math.Max(state.Severity, severity), 0.0, 1.0);
    }

    public void Heal(BodyPart part)
    {
        var state = Parts[part];
        if (state.Condition != PartCondition.Injured) return;
        state.Condition = PartCondition.Healthy;
        state.Severity = 0;
    }

    public void Lose(BodyPart part)
    {
        var state = Parts[part];
        state.Condition = PartCondition.Missing;
        ClearProsthetic(state);
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
    }

    /// <summary>
    /// Average efficiency of the parts in a region. The head is the exception: the eyes work through it, so the head's
    /// efficiency is the head's times the eyes' (healthy eyes change nothing, an eye implant scales sight and aim).
    /// </summary>
    public double RegionEfficiency(BodyRegion region)
    {
        if (region == BodyRegion.Head) return Parts[BodyPart.Head].Efficiency * Parts[BodyPart.Eyes].Efficiency;
        var parts = Parts.Where(kv => RegionOf(kv.Key) == region).Select(kv => kv.Value.Efficiency).ToList();
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
