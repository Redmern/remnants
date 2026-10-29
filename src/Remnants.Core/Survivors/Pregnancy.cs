namespace Remnants.Core.Survivors;

/// <summary>
/// A child on the way. The baby's inherited <see cref="Aptitude"/> is rolled at conception so the father's
/// attributes count even if he is gone by the birth.
/// </summary>
public sealed class Pregnancy
{
    public int? FatherId { get; set; }
    public long ConceivedTick { get; set; }
    public long DueTick { get; set; }
    public Dictionary<AttributeKind, double> Aptitude { get; set; } = new();
}
