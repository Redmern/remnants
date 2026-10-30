using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Remnants.Core.Items;

/// <summary>
/// The item lookup: every defined item and quality variant, plus modded gear resolved on demand from its id
/// (<see cref="GearModsData"/>). Enumerating it lists the defined items and variants only; any valid modded id answers
/// lookups, so inventories, units, saves and the wire treat modded gear as ordinary items. Resolving is pure (the same id
/// always gives the same item), so the cache cannot change an outcome.
/// </summary>
public sealed class ItemCatalog : IReadOnlyDictionary<string, ItemDef>
{
    private readonly Dictionary<string, ItemDef> _defined;
    private readonly Func<string, ItemDef?>? _resolve;
    private readonly ConcurrentDictionary<string, ItemDef?> _resolved = new(StringComparer.Ordinal);

    public ItemCatalog(Dictionary<string, ItemDef> defined, Func<string, ItemDef?>? resolve = null)
    {
        _defined = defined;
        _resolve = resolve;
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out ItemDef value)
    {
        if (_defined.TryGetValue(key, out value)) return true;
        if (_resolve is null || !GearModsData.IsComposite(key)) return false;
        value = _resolved.GetOrAdd(key, _resolve)!;
        return value is not null;
    }

    public ItemDef this[string key] => TryGetValue(key, out var v) ? v : throw new KeyNotFoundException($"No item '{key}'.");
    public bool ContainsKey(string key) => TryGetValue(key, out _);
    public IEnumerable<string> Keys => _defined.Keys;
    public IEnumerable<ItemDef> Values => _defined.Values;
    public int Count => _defined.Count;
    public IEnumerator<KeyValuePair<string, ItemDef>> GetEnumerator() => _defined.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
