namespace Remnants.Core.Colony;

/// <summary>Whole items the colony owns, by item id. Sorted so saves are stable.</summary>
public sealed class ItemInventory
{
    public SortedDictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    public int Count(string itemId) => Counts.GetValueOrDefault(itemId);

    public bool Has(string itemId, int count = 1) => Count(itemId) >= count;

    public void Add(string itemId, int count = 1)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return;
        Counts[itemId] = Count(itemId) + count;
    }

    public bool TryTake(string itemId, int count = 1)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (!Has(itemId, count)) return false;
        var left = Count(itemId) - count;
        if (left == 0) Counts.Remove(itemId);
        else Counts[itemId] = left;
        return true;
    }

    /// <summary>True when every id in the list (repeats count) is in stock.</summary>
    public bool HasAll(IEnumerable<string> itemIds) =>
        itemIds.GroupBy(id => id).All(g => Has(g.Key, g.Count()));
}
