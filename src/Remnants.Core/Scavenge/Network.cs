namespace Remnants.Core.Scavenge;

/// <summary>A connected piece of the Network: Linked units and pylons that relay to each other.</summary>
public sealed record NetworkGroup(IReadOnlyList<Unit> Units, IReadOnlyList<Pos> Pylons);

/// <summary>
/// The Linked relay graph. Nodes are active, unjammed Linked units and online pylon tiles. Two units link when they are
/// within either one's <see cref="EnemyDef.RelayRange"/> (Chebyshev); a unit and a pylon, or two pylons, within the
/// pylon range (or the unit's own range if longer). What any unit of a group sees, the whole group knows.
/// A signal jammer cuts a Linked unit out (it fights alone, like a Husk) and takes pylons in its radius offline, so they
/// neither relay nor call reinforcements. Pure queries over the state; nothing is stored but the alarm counter.
/// </summary>
public static class Network
{
    public static bool IsLinked(ScavengeData data, Unit unit) =>
        unit.EnemyDefId is { } id && data.Enemies[id].Ai == AiKind.Linked;

    /// <summary>A pylon relays unless a hostile jammer covers its tile.</summary>
    public static bool PylonOnline(ScavengeSim sim, Pos pylon) => !Devices.JamsTile(sim, pylon, Unit.EnemySide);

    public static IEnumerable<Pos> Pylons(TileMap map) => map.AllPositions.Where(p => map[p] == Terrain.Pylon);

    /// <summary>The connected groups, each listing units by id and pylons by tile index; groups ordered by their first unit.</summary>
    public static List<NetworkGroup> Groups(ScavengeSim sim)
    {
        var data = sim.Data;
        var units = sim.State.Units
            .Where(u => u.IsActive && IsLinked(data, u) && !Devices.IsJammed(sim, u))
            .OrderBy(u => u.Id).ToList();
        var pylons = Pylons(sim.State.Map).Where(p => PylonOnline(sim, p)).ToList();
        var pylonRange = data.Rules.Network.PylonRange;
        int Relay(Unit u) => data.Enemies[u.EnemyDefId!].RelayRange;

        // Union-find over units (0..n-1) then pylons (n..).
        var parent = Enumerable.Range(0, units.Count + pylons.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        void Union(int a, int b) => parent[Math.Max(Find(a), Find(b))] = Math.Min(Find(a), Find(b));

        for (var i = 0; i < units.Count; i++)
        {
            for (var j = i + 1; j < units.Count; j++)
                if (units[i].Pos.Chebyshev(units[j].Pos) <= Math.Max(Relay(units[i]), Relay(units[j]))) Union(i, j);
            for (var k = 0; k < pylons.Count; k++)
                if (units[i].Pos.Chebyshev(pylons[k]) <= Math.Max(Relay(units[i]), pylonRange)) Union(i, units.Count + k);
        }
        for (var a = 0; a < pylons.Count; a++)
            for (var b = a + 1; b < pylons.Count; b++)
                if (pylons[a].Chebyshev(pylons[b]) <= pylonRange) Union(units.Count + a, units.Count + b);

        return Enumerable.Range(0, parent.Length)
            .GroupBy(Find)
            .OrderBy(g => g.Key)
            .Select(g => new NetworkGroup(
                g.Where(i => i < units.Count).Select(i => units[i]).ToList(),
                g.Where(i => i >= units.Count).Select(i => pylons[i - units.Count]).ToList()))
            .Where(g => g.Units.Count > 0 || g.Pylons.Count > 0)
            .ToList();
    }

    /// <summary>The group a Linked unit is relaying in, or null when it is jammed (or not Linked).</summary>
    public static NetworkGroup? GroupOf(ScavengeSim sim, Unit unit) =>
        Groups(sim).FirstOrDefault(g => g.Units.Any(u => u.Id == unit.Id));

    /// <summary>Conscious hostiles any unit of the group can see, by id.</summary>
    public static List<Unit> Known(ScavengeSim sim, NetworkGroup group)
    {
        var map = sim.State.Map;
        var sights = group.Units.Where(u => u.IsActive).Select(u => (Unit: u, Sight: sim.Profile(u).Sight)).ToList();
        return sim.State.Units
            .Where(t => t.IsActive && sights.Any(s => s.Unit.HostileTo(t) && Visibility.CanSee(sim.Data, map, s.Unit.Pos, s.Sight, t.Pos)))
            .OrderBy(t => t.Id)
            .ToList();
    }

    /// <summary>
    /// The squad's focus: the known hostile that is weakest and most exposed, i.e. the lowest HP divided by how open it
    /// is to the group (1 - the least cover it has against any member). Ties break on id.
    /// </summary>
    public static Unit? Focus(ScavengeSim sim, NetworkGroup group, IReadOnlyList<Unit> known)
    {
        var map = sim.State.Map;
        double Exposure(Unit t) =>
            1 - group.Units.Select(u => Combat.CoverAgainst(sim.Data, map, t.Pos, u.Pos)).DefaultIfEmpty(0).Min();
        return known
            .OrderBy(t => t.Hp / Math.Max(0.05, Exposure(t)))
            .ThenBy(t => t.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Round start: the Network is alarmed while a group with an online pylon knows a hostile. After
    /// <see cref="NetworkRules.ReinforceAfterRounds"/> such rounds in a row that pylon calls a wave (zone
    /// <see cref="ZoneDef.Reinforcements"/>), placed on free floor next to it.
    /// </summary>
    internal static void RoundStarted(ScavengeSim sim)
    {
        var state = sim.State;
        if (!sim.Data.Zones.TryGetValue(state.ZoneId, out var zone)) return;
        var alarmed = Groups(sim).FirstOrDefault(g => g.Pylons.Count > 0 && g.Units.Count > 0 && Known(sim, g).Count > 0);
        if (alarmed is null)
        {
            state.AlarmRounds = 0;
            return;
        }
        state.AlarmRounds++;
        var waves = zone.Reinforcements;
        if (state.AlarmRounds < sim.Data.Rules.Network.ReinforceAfterRounds || state.ReinforcementWaves >= waves.Waves || waves.Mix.Count == 0)
            return;

        state.AlarmRounds = 0;
        state.ReinforcementWaves++;
        var pylon = alarmed.Pylons[0];
        var target = Known(sim, alarmed)[0];
        var map = state.Map;
        var spots = map.AllPositions
            .Where(p => map[p] == Terrain.Floor && !map.IsExit(p) && p.Chebyshev(pylon) <= 3 && state.UnitAt(p) is null)
            .OrderBy(p => p.Chebyshev(pylon)).ThenBy(map.Index)
            .ToList();
        var count = sim.Rng.NextInclusive(waves.Count[0], waves.Count[1]);
        var mix = waves.Mix.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        var arrived = new List<string>();
        foreach (var spot in spots.Take(count))
        {
            var def = sim.Data.Enemies[NewScavenge.PickWeighted(sim.Rng, mix, e => e.Weight).Id];
            var unit = NewScavenge.EnemyUnit(sim.Data, state, def, spot);
            unit.Ai.Mode = AiMode.Hunting;
            unit.Ai.TargetId = target.Id;
            unit.Ai.LastKnown = target.Pos;
            state.Units.Add(unit);
            arrived.Add(unit.Name);
        }
        if (arrived.Count > 0)
            state.Log(new(state.Round, ScavengeEventType.Reinforced,
                $"A pylon calls reinforcements: {string.Join(", ", arrived)}.", Pos: pylon));
    }
}
