namespace Remnants.Core.Colony;

/// <summary>Resource amounts by resource id. Fractional so per-tick output accumulates.</summary>
public sealed class Stockpile
{
    /// <summary>Slack for float drift from many fractional adds/takes (0.05 food per tick, etc.).</summary>
    private const double Epsilon = 1e-9;

    public Dictionary<string, double> Amounts { get; set; } = new();

    public double Get(string resourceId) => Amounts.GetValueOrDefault(resourceId);

    public void Add(string resourceId, double amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Amounts[resourceId] = Get(resourceId) + amount;
    }

    public bool TryTake(string resourceId, double amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (!Has(resourceId, amount)) return false;
        Amounts[resourceId] = Math.Max(0, Get(resourceId) - amount);
        return true;
    }

    public bool Has(string resourceId, double amount) => Get(resourceId) + Epsilon >= amount;

    public bool CanAfford(IReadOnlyDictionary<string, double> cost) => cost.All(kv => Has(kv.Key, kv.Value));

    /// <summary>What is still needed to afford <paramref name="cost"/>; empty when affordable.</summary>
    public Dictionary<string, double> Shortfall(IReadOnlyDictionary<string, double> cost) =>
        cost.Where(kv => !Has(kv.Key, kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value - Get(kv.Key));

    public void Spend(IReadOnlyDictionary<string, double> cost)
    {
        if (!CanAfford(cost)) throw new InvalidOperationException("Cannot afford cost.");
        foreach (var (res, amount) in cost) Amounts[res] = Math.Max(0, Get(res) - amount);
    }
}
