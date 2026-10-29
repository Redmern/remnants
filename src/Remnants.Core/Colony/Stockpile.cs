namespace Remnants.Core.Colony;

/// <summary>Resource amounts by resource id. Fractional so per-tick output accumulates.</summary>
public sealed class Stockpile
{
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
        if (Get(resourceId) < amount) return false;
        Amounts[resourceId] = Get(resourceId) - amount;
        return true;
    }

    public bool CanAfford(IReadOnlyDictionary<string, double> cost) =>
        cost.All(kv => Get(kv.Key) >= kv.Value);

    /// <summary>What is still needed to afford <paramref name="cost"/>; empty when affordable.</summary>
    public Dictionary<string, double> Shortfall(IReadOnlyDictionary<string, double> cost) =>
        cost.Where(kv => Get(kv.Key) < kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value - Get(kv.Key));

    public void Spend(IReadOnlyDictionary<string, double> cost)
    {
        if (!CanAfford(cost)) throw new InvalidOperationException("Cannot afford cost.");
        foreach (var (res, amount) in cost) Amounts[res] = Get(res) - amount;
    }
}
