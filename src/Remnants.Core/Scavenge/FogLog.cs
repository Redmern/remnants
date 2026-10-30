namespace Remnants.Core.Scavenge;

/// <summary>
/// Which events a side hears about, and how. Shared by the single-player log and the multiplayer server's
/// <c>SideFog</c>, so both say the same thing:
/// <list type="bullet">
/// <item>round and party bookkeeping always;</item>
/// <item>an event limited to one side (<see cref="ScavengeEvent.Side"/>) only there; one addressed to some sides
///   (<see cref="ScavengeEvent.Sides"/>: alliance news) to exactly those;</item>
/// <item>an event about someone else's unit the side does not know (the target unseen before and after, or the actor
///   not seen when the event was logged, <see cref="ScavengeEvent.SeenBy"/>): a target it does not know drops the event;
///   an actor it did not see leaves only its
///   <see cref="ScavengeEvent.Anonymous"/> form, actor removed ("Ana was ambushed from the fog"), and only if it was
///   done to the side's own unit or on a tile it saw; without an anonymous form, not at all;</item>
/// <item>otherwise anything done by or to its own units, or on a tile it saw.</item>
/// </list>
/// </summary>
public static class FogLog
{
    /// <param name="mine">Whether a unit id belongs to the side.</param>
    /// <param name="knownUnit">Whether the side saw the unit before or after the events.</param>
    /// <param name="seenTile">Whether the side saw the tile before or after the events.</param>
    public static List<ScavengeEvent> Filter(
        IEnumerable<ScavengeEvent> events, string side, Func<int, bool> mine, Func<int, bool> knownUnit, Func<Pos, bool> seenTile)
    {
        var result = new List<ScavengeEvent>();
        foreach (var e in events)
        {
            if (e.Type is ScavengeEventType.RoundStarted or ScavengeEventType.PartyDone or ScavengeEventType.Finished
                or ScavengeEventType.WeatherChanged or ScavengeEventType.Thunder or ScavengeEventType.Lightning)
            {
                result.Add(e);
                continue;
            }
            if (e.Sides is { } to)
            {
                if (to.Contains(side)) result.Add(e with { Anonymous = null, SeenBy = null, Sides = null });
                continue;
            }
            if (e.Side is { } only && only != side) continue;

            var targetMine = e.TargetId is { } t && mine(t);
            if (e.TargetId is { } other && !targetMine && !knownUnit(other)) continue;
            var onSeenTile = e.Pos is { } p && seenTile(p);
            var actorSeen = e.UnitId is not { } who || mine(who) || (e.SeenBy is { } seers ? seers.Contains(side) : knownUnit(who));
            if (!actorSeen)
            {
                if (e.Anonymous is { } text && (targetMine || onSeenTile || e.Side == side))
                    result.Add(e with { UnitId = null, Message = text, Anonymous = null, SeenBy = null });
                continue;
            }
            if ((e.UnitId is { } u && mine(u)) || targetMine || onSeenTile || e.Side == side)
                result.Add(e with { Anonymous = null, SeenBy = null });
        }
        return result;
    }

    /// <summary>The single-player view: what <paramref name="side"/> may hear of <paramref name="events"/>, judged against now.</summary>
    public static List<ScavengeEvent> For(ScavengeSim sim, string side, IEnumerable<ScavengeEvent> events,
        IReadOnlySet<int>? seenUnitsBefore = null, IReadOnlySet<int>? seenTilesBefore = null)
    {
        var state = sim.State;
        var tiles = Visibility.SideVisible(sim.Data, state, side);
        var units = Detection.SeenBy(sim.Data, state, side);
        return Filter(events, side,
            id => state.FindUnit(id) is { } u && u.Side == side,
            id => units.Contains(id) || (seenUnitsBefore?.Contains(id) ?? false),
            p => state.Map.InBounds(p) && (tiles.Contains(state.Map.Index(p)) || (seenTilesBefore?.Contains(state.Map.Index(p)) ?? false)));
    }
}
