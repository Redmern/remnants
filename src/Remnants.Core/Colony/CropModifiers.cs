using Remnants.Core.Data;

namespace Remnants.Core.Colony;

/// <summary>One term on crop output: where it comes from (weather kind), a reason to show, and its multiplier.</summary>
public sealed record CropModifier(string Source, string Reason, double Multiplier);

/// <summary>
/// Stand-in for the weather branch's crop hook (same names and signatures as its <c>Crops</c> in Colony/Weather.cs):
/// no weather yet, so no terms. When weather merges, delete this file; <see cref="HarvestSystem"/> already applies
/// <see cref="Multiplier"/> per weather-sensitive harvest at outdoor buildings (and weather's own <c>cropJobs</c>
/// application goes away).
/// </summary>
public static class Crops
{
    public static IReadOnlyList<CropModifier> Modifiers(ColonyState state, GameData data) => [];

    public static double Multiplier(IReadOnlyList<CropModifier> modifiers) => modifiers.Aggregate(1.0, (m, c) => m * c.Multiplier);
}
