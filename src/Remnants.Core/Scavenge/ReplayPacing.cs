namespace Remnants.Core.Scavenge;

/// <summary>
/// How the scavenge screen paces its replay of events (a UI view model, like <see cref="TacticsView"/>; nothing here is a
/// rule and the run plays out the same at any pace). An event of another side's unit the side saw nothing of plays at
/// once; with skip animations every step does.
/// </summary>
public static class ReplayPacing
{
    /// <summary>A run's tile takes this share of a walked one (long paths jog).</summary>
    public const double RunPace = 0.75;

    /// <summary>The longest one move's walk may take: long moves speed up so turns keep their pace.</summary>
    public const double MaxWalkSeconds = 1.4;

    /// <summary>
    /// Whether the side saw anything of an event: it is about one of the side's own units (actor or target), or the
    /// event's tile, the actor's (<paramref name="actorAt"/>) or the target's (<paramref name="targetAt"/>) was in sight
    /// (<paramref name="visible"/>: tile indices seen before or after the command). An event about no unit counts as seen.
    /// </summary>
    public static bool InSight(ScavengeState state, string side, ScavengeEvent e, IReadOnlySet<int> visible, Pos? actorAt, Pos? targetAt)
    {
        if (e.UnitId is null) return true;
        bool Mine(int? id) => id is { } i && state.FindUnit(i) is { } u && u.Side == side;
        bool Seen(Pos? p) => p is { } at && state.Map.InBounds(at) && visible.Contains(state.Map.Index(at));
        return Mine(e.UnitId) || Mine(e.TargetId) || Seen(e.Pos) || Seen(actorAt) || Seen(targetAt);
    }

    /// <summary>How long a step may hold the replay: nothing for an unseen step or while skipping.</summary>
    public static double Hold(double seconds, bool seen, bool skip) => !seen || skip ? 0 : seconds;

    /// <summary>A walk of <paramref name="tiles"/> tiles at <paramref name="perTile"/> seconds each (a run quicker), capped; nothing skipping.</summary>
    public static double WalkSeconds(double perTile, int tiles, bool run, bool skip) =>
        skip ? 0 : Math.Min(perTile * (run ? RunPace : 1) * Math.Max(1, tiles), MaxWalkSeconds);
}
