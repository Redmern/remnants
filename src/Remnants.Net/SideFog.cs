using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Net;

/// <summary>
/// Server-side fog of war for one player: decides what that player's client may receive. A view holds
/// <list type="bullet">
/// <item>the player's own units (any status) and other units only while they stand on a tile the side sees now;
///   other units lose their AI memory and colony record (only name, sex and child/adult are kept for the model);</item>
/// <item>loot caches as the side last saw them (a cache looted out of sight still shows until seen again);</item>
/// <item>the turn order cut to known units, with <see cref="HiddenUnitId"/> standing in when an unseen unit is up;</item>
/// <item>the side's own explored tiles, every party's done flag, and no RNG state or Network alarm counters.</item>
/// </list>
/// The terrain is not part of a view; it goes once in <see cref="MissionStarted"/>.
/// </summary>
public sealed class SideFog(int playerId, string side)
{
    /// <summary>Stands in the view's turn order for a unit the side cannot see whose turn it is.</summary>
    public const int HiddenUnitId = -1;

    private readonly Dictionary<int, LootCache> _knownCaches = new();

    public int PlayerId { get; } = playerId;
    public string Side { get; } = side;

    /// <summary>Tiles the side saw at the last <see cref="Update"/>.</summary>
    public HashSet<int> Visible { get; private set; } = [];

    /// <summary>Recomputes what the side sees and refreshes its memory of caches on those tiles. Returns the previous visible set.</summary>
    public HashSet<int> Update(ScavengeSim sim)
    {
        var before = Visible;
        var state = sim.State;
        Visible = Visibility.SideVisible(sim.Data, state, Side);
        foreach (var id in _knownCaches.Where(kv => Visible.Contains(state.Map.Index(kv.Value.Pos))).Select(kv => kv.Key).ToList())
            _knownCaches.Remove(id);
        foreach (var cache in state.Caches.Where(c => Visible.Contains(state.Map.Index(c.Pos))))
            _knownCaches[cache.Id] = new LootCache { Id = cache.Id, Pos = cache.Pos, Items = new(cache.Items) };
        return before;
    }

    /// <summary>Whether this side may know of a unit right now.</summary>
    public bool Knows(ScavengeState state, Unit u) =>
        Owns(u) || (u.OnMap && Visible.Contains(state.Map.Index(u.Pos)));

    private bool Owns(Unit u) => u.Side == Side || u.PlayerId == PlayerId;

    public ScavengeState View(ScavengeSim sim)
    {
        var state = sim.State;
        var units = state.Units.Where(u => Knows(state, u)).Select(u => Owns(u) ? Copy(u) : Redacted(u)).ToList();
        var known = units.Select(u => u.Id).ToHashSet();

        var order = new List<int>();
        var index = -1;
        for (var i = 0; i < state.TurnOrder.Count; i++)
        {
            var id = state.TurnOrder[i];
            if (i == state.TurnIndex) index = order.Count;
            if (known.Contains(id)) order.Add(id);
            else if (i == state.TurnIndex) order.Add(HiddenUnitId);
        }
        if (index < 0) index = order.Count;

        return new ScavengeState
        {
            ZoneId = state.ZoneId,
            Round = state.Round,
            TurnOrder = order,
            TurnIndex = index,
            Finished = state.Finished,
            Map = new TileMap(),
            Parties = state.Parties.Select(p => new Party { PlayerId = p.PlayerId, Side = p.Side, Done = p.Done }).ToList(),
            Units = units,
            Caches = _knownCaches.Values.OrderBy(c => c.Id).Select(c => new LootCache { Id = c.Id, Pos = c.Pos, Items = new(c.Items) }).ToList(),
            Explored = state.Explored.TryGetValue(Side, out var explored)
                ? new() { [Side] = new SortedSet<int>(explored) }
                : new(),
        };
    }

    /// <summary>
    /// Events this side gets to hear about, judged against what it saw before or after they happened: round and
    /// party bookkeeping, anything done by or to its own units, and anything on a tile it saw.
    /// </summary>
    public List<ScavengeEvent> Filter(ScavengeState state, IEnumerable<ScavengeEvent> events, IReadOnlySet<int> seenBefore)
    {
        bool Seen(Pos? p) => p is { } at && state.Map.InBounds(at) && (Visible.Contains(state.Map.Index(at)) || seenBefore.Contains(state.Map.Index(at)));
        bool Mine(int? id) => id is { } i && state.FindUnit(i) is { } u && Owns(u);
        return events.Where(e =>
            e.Type is ScavengeEventType.RoundStarted or ScavengeEventType.PartyDone or ScavengeEventType.Finished
            || Mine(e.UnitId) || Mine(e.TargetId) || Seen(e.Pos)).ToList();
    }

    private static Unit Copy(Unit u) => ProtocolCopy.Clone(u);

    private static Unit Redacted(Unit u)
    {
        var copy = ProtocolCopy.Clone(u);
        copy.Ai = new AiMemory();
        copy.Origin = u.Origin is { } o ? new Survivor { Name = o.Name, Sex = o.Sex, LifeStage = o.LifeStage } : null;
        return copy;
    }
}

internal static class ProtocolCopy
{
    public static T Clone<T>(T value) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value, ProtocolJson.Options), ProtocolJson.Options)!;
}
