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

    /// <summary>How well this part works, as a multiplier (1 = healthy).</summary>
    public double Efficiency => Condition switch
    {
        PartCondition.Healthy => 1.0,
        PartCondition.Injured => Math.Clamp(1.0 - Severity, 0.0, 1.0),
        PartCondition.Missing => 0.0,
        PartCondition.Prosthetic => Math.Max(0.0, ProstheticQuality),
        _ => 1.0,
    };
}

/// <summary>Body-part model: head, torso, two arms, two legs. Data and modifiers only for now.</summary>
public sealed class Body
{
    public Dictionary<BodyPart, BodyPartState> Parts { get; set; } =
        Enum.GetValues<BodyPart>().ToDictionary(p => p, _ => new BodyPartState());

    public BodyPartState this[BodyPart part] => Parts[part];

    public static BodyRegion RegionOf(BodyPart part) => part switch
    {
        BodyPart.Head => BodyRegion.Head,
        BodyPart.Torso => BodyRegion.Torso,
        BodyPart.LeftArm or BodyPart.RightArm => BodyRegion.Arms,
        _ => BodyRegion.Legs,
    };

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
        state.Severity = 0;
        state.ProstheticQuality = 0;
        state.ProstheticItemId = null;
    }

    public void FitProsthetic(BodyPart part, double quality, string? itemId = null)
    {
        var state = Parts[part];
        if (state.Condition != PartCondition.Missing)
            throw new InvalidOperationException($"{part} is not missing; cannot fit a prosthetic.");
        state.Condition = PartCondition.Prosthetic;
        state.ProstheticQuality = quality;
        state.ProstheticItemId = itemId;
    }

    /// <summary>Average efficiency of the parts in a region.</summary>
    public double RegionEfficiency(BodyRegion region)
    {
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
