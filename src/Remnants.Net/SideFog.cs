using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Net;

/// <summary>
/// Server-side fog of war for one player: decides what that player's client may receive. A view holds
/// <list type="bullet">
/// <item>the units of its side (any status: its own, and its allies' with their colony record cut like anyone else's)
///   and other units only while the side sees them now (<see cref="Detection"/>:
///   in sight and detected, lit by a flare, revealed, or scanned); other units lose their AI memory and colony record
///   (only name, sex and child/adult are kept for the model);</item>
/// <item>the side's own contacts and motion sensors, reveals of units it sees, and every burning flare (they light up
///   for everyone);</item>
/// <item>loot caches as the side last saw them (a cache looted out of sight still shows until seen again);</item>
/// <item>the turn order cut to known units, with <see cref="HiddenUnitId"/> standing in when an unseen unit is up;</item>
/// <item>the run's environment (hour, phase, weather): the same for everyone;</item>
/// <item>the side's own explored tiles, every party's done flag, and no RNG state or Network alarm counters;</item>
/// <item>co-op: the side's own pings, alliance offers to or from the player, and breaks announced on its team;</item>
/// <item>hackable devices as the side last saw them (or learned of them from a terminal's map), current while their tile
///   is in sight or the side controls them; never their contents, nor other sides' hacks in progress or lockouts, and an
///   other team's control shows as its owner's own side. Drone marks of the side on units it is sent. Other sides' drones
///   only while seen, like any unit, and a rival's link to an unseen drone or operator is cut.</item>
/// </list>
/// The terrain is not part of a view; it goes once in <see cref="MissionStarted"/>. The side is the player's party's side
/// at each <see cref="Update"/>: a team side while allied (team fog), their own again the moment an alliance breaks.
/// </summary>
public sealed class SideFog(int playerId, string side)
{
    /// <summary>Stands in the view's turn order for a unit the side cannot see whose turn it is.</summary>
    public const int HiddenUnitId = -1;

    private readonly Dictionary<int, LootCache> _knownCaches = new();
    private readonly Dictionary<int, ZoneDevice> _knownDevices = new();
    private readonly Dictionary<int, SalvageNode> _knownNodes = new();

    public int PlayerId { get; } = playerId;
    public string Side { get; private set; } = side;

    /// <summary>Tiles the side saw at the last <see cref="Update"/>.</summary>
    public HashSet<int> Visible { get; private set; } = [];

    /// <summary>Ids of the units the side saw at the last <see cref="Update"/> (its own included).</summary>
    public HashSet<int> Seen { get; private set; } = [];

    /// <summary>Units the side saw at the update before the last one (for judging events against "before").</summary>
    public HashSet<int> SeenBefore { get; private set; } = [];

    /// <summary>Recomputes what the side sees and refreshes its memory of caches on those tiles. Returns the previous visible set.</summary>
    public HashSet<int> Update(ScavengeSim sim)
    {
        var before = Visible;
        var state = sim.State;
        var side = state.FindParty(PlayerId)?.Side ?? Side;
        var switched = side != Side;
        Side = side;
        Visible = Visibility.SideVisible(sim.Data, state, Side);
        SeenBefore = Seen;
        Seen = Detection.SeenBy(sim.Data, state, Side);
        // The team changed (an alliance formed or broke): what the old team saw no longer counts as "before".
        if (switched)
        {
            SeenBefore = new HashSet<int>(Seen);
            before = new HashSet<int>(Visible);
        }
        foreach (var id in _knownCaches.Where(kv => Visible.Contains(state.Map.Index(kv.Value.Pos))).Select(kv => kv.Key).ToList())
            _knownCaches.Remove(id);
        foreach (var cache in state.Caches.Where(c => Visible.Contains(state.Map.Index(c.Pos))))
            _knownCaches[cache.Id] = CopyPile(cache);
        var explored = state.Explored.GetValueOrDefault(Side);
        // Salvage nodes are known once their tile was seen; how far along one is only while it is in sight.
        foreach (var node in state.SalvageNodes)
        {
            var tile = state.Map.Index(node.Pos);
            if (Visible.Contains(tile) || (!_knownNodes.ContainsKey(node.Id) && explored?.Contains(tile) == true))
                _knownNodes[node.Id] = new SalvageNode { Id = node.Id, Kind = node.Kind, Pos = node.Pos, LotsLeft = node.LotsLeft, Progress = node.Progress };
        }
        foreach (var device in state.Devices)
        {
            var tile = state.Map.Index(device.Pos);
            var current = Visible.Contains(tile) || Hacking.ControllerOf(state, device) == Side;
            if (current || (!_knownDevices.ContainsKey(device.Id) && explored?.Contains(tile) == true))
                _knownDevices[device.Id] = Redacted(state, device);
        }
        return before;
    }

    private static LootCache CopyPile(LootCache c) =>
        new() { Id = c.Id, Pos = c.Pos, Items = new(c.Items), Kind = c.Kind, Source = c.Source, PlayerId = c.PlayerId };

    /// <summary>Whether this side may know of a unit right now.</summary>
    public bool Knows(ScavengeState state, Unit u) => Owns(u) || (u.OnMap && Seen.Contains(u.Id));

    private bool Owns(Unit u) => u.Side == Side || u.PlayerId == PlayerId;

    public ScavengeState View(ScavengeSim sim)
    {
        var state = sim.State;
        var units = state.Units.Where(u => Knows(state, u)).Select(u => u.PlayerId == PlayerId ? Copy(u) : Redacted(u)).ToList();
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
            Environment = state.Environment.Copy(),
            Round = state.Round,
            TurnOrder = order,
            TurnIndex = index,
            Finished = state.Finished,
            Map = new TileMap(),
            // Other teams' alliances are none of this player's business: their parties show as on their own.
            Parties = state.Parties.Select(p => new Party { PlayerId = p.PlayerId, Name = p.Name, Side = Shown(p.Side, p.PlayerId), Done = p.Done, Left = p.Left }).ToList(),
            Units = units,
            Caches = _knownCaches.Values.OrderBy(c => c.Id).Select(CopyPile).ToList(),
            SalvageNodes = _knownNodes.Values.OrderBy(n => n.Id)
                .Select(n => new SalvageNode { Id = n.Id, Kind = n.Kind, Pos = n.Pos, LotsLeft = n.LotsLeft, Progress = n.Progress }).ToList(),
            Explored = state.Explored.TryGetValue(Side, out var explored)
                ? new() { [Side] = new SortedSet<int>(explored) }
                : new(),
            Contacts = state.Contacts.Where(c => c.Side == Side).ToList(),
            Reveals = state.Reveals.Where(r => r.Side == Side && known.Contains(r.UnitId)).ToList(),
            Flares = state.Flares.ToList(),
            Sensors = state.Sensors.Where(x => x.Side == Side).ToList(),
            Pings = state.Pings.Where(p => p.Side == Side).ToList(),
            Proposals = state.Proposals.Where(p => p.FromPlayerId == PlayerId || p.ToPlayerId == PlayerId).ToList(),
            Breaks = state.Breaks.Where(b => b.Side == Side).ToList(),
            Devices = _knownDevices.Values.OrderBy(d => d.Id).Select(d => ProtocolCopy.Clone(d)).ToList(),
            Marks = state.Marks.Where(m => m.Side == Side && known.Contains(m.UnitId)).ToList(),
            // Stripped wrecks show on the map for everyone (the wreck is plainly bare).
            SalvagedWrecks = new SortedSet<int>(state.SalvagedWrecks),
            // Our side's vehicles in full; another party's only where we have seen its tile, and never its stash.
            Vehicles = state.Vehicles
                .Where(x => state.FindParty(x.PlayerId)?.Side == Side || explored?.Contains(state.Map.Index(x.Pos)) == true)
                .Select(x => ProtocolCopy.Clone(x).Also(c => { if (state.FindParty(x.PlayerId)?.Side != Side) c.Stash = new(); }))
                .ToList(),
        }.Also(v =>
        {
            // A device's unit only while that unit is sent; links to drones / operators the side does not know are cut.
            foreach (var d in v.Devices.Where(d => d.UnitId is { } id && !known.Contains(id))) d.UnitId = null;
            foreach (var u in v.Units)
            {
                if (u.DroneId is { } drone && !known.Contains(drone)) u.DroneId = null;
                if (u.OperatorId is { } op && !known.Contains(op)) u.OperatorId = null;
            }
        });
    }

    /// <summary>
    /// A device as this side may know it: no contents, only its own side's hack in progress and lockout, and control by
    /// another team shown as the owner's own side.
    /// </summary>
    private ZoneDevice Redacted(ScavengeState state, ZoneDevice d)
    {
        var copy = ProtocolCopy.Clone(d);
        var controller = Hacking.ControllerOf(state, d);
        copy.Controller = Shown(controller, controller == Side ? null : d.ControllerPlayerId);
        if (controller != Side) copy.ControllerPlayerId = null;
        copy.Items = new();
        copy.Progress = d.Progress.Where(p => p.Side == Side).ToList();
        copy.Lockouts = d.Lockouts.Where(l => l.Side == Side).ToList();
        return copy;
    }

    /// <summary>
    /// Events this side gets to hear about (<see cref="FogLog"/>), judged against what it saw before or after they
    /// happened: an unseen actor is removed (the anonymous text, "ambushed from the fog"), side-only alerts stay with
    /// their side.
    /// </summary>
    public List<ScavengeEvent> Filter(ScavengeState state, IEnumerable<ScavengeEvent> events, IReadOnlySet<int> seenBefore) =>
        FogLog.Filter(events, Side,
            id => state.FindUnit(id) is { } u && Owns(u),
            id => Seen.Contains(id) || SeenBefore.Contains(id),
            p => state.Map.InBounds(p) && (Visible.Contains(state.Map.Index(p)) || seenBefore.Contains(state.Map.Index(p))));

    private static Unit Copy(Unit u) => ProtocolCopy.Clone(u);

    /// <summary>A side as this player may know it: their own team's as is, another team's as the owner's own side.</summary>
    private string Shown(string side, int? owner) =>
        side == Side || side is Unit.EnemySide or Unit.NeutralSide || owner is null ? side : Unit.PlayerSide(owner.Value);

    private Unit Redacted(Unit u)
    {
        var copy = ProtocolCopy.Clone(u);
        copy.Side = Shown(u.Side, u.PlayerId);
        copy.Ai = new AiMemory();
        copy.Hooks = new(StringComparer.Ordinal);
        copy.CyberHooks = new();
        copy.FieldXp = 0;
        copy.DroneBattery = null;
        copy.ChargesUsed = new(StringComparer.Ordinal);
        copy.Origin = u.Origin is { } o ? new Survivor { Name = o.Name, Sex = o.Sex, LifeStage = o.LifeStage } : null;
        // A rival's holo-decoy looks like one of their people until it flickers out.
        if (u.Kind == UnitKind.Decoy && u.Side != Side)
        {
            copy.Kind = UnitKind.Survivor;
            copy.DecoyUntilRound = 0;
        }
        return copy;
    }
}

internal static class ProtocolCopy
{
    public static T Also<T>(this T value, Action<T> act)
    {
        act(value);
        return value;
    }

    public static T Clone<T>(T value) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value, ProtocolJson.Options), ProtocolJson.Options)!;
}
