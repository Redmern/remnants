using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Simple heuristics for scripted players (the playtest bot, the server's bot): which card to play now, stepping off a
/// tile a seen enemy plan aims at, and declaring a plan to close in and strike when nothing is in reach. They read only
/// what a player's view holds (their own deck, seen units and plans) and return ordinary commands, which Core checks.
/// </summary>
public static class BotTactics
{
    /// <summary>A bot plays a mend card once a body part is at least this badly hurt (a bot habit, not a game rule).</summary>
    public const double MendInjuryAt = 0.2;

    /// <summary>Seen hostiles whose telegraphed attack is aimed at <paramref name="unit"/>'s tile.</summary>
    public static List<Unit> Threats(ScavengeSim sim, Unit unit) =>
        sim.State.Units.Where(u => u.IsActive && unit.HostileTo(u) && u.Plan is { AttackTile: { } t } && t == unit.Pos
                                   && Detection.SideSees(sim.Data, sim.State, unit.Side, u))
            .OrderBy(u => u.Id).ToList();

    /// <summary>
    /// The card to play on <paramref name="unit"/> now, or null. By priority: a reaction against a seen plan aimed at it
    /// (one that breaks the plan first); on its own turn, holding back a seen enemy still to act whose plan aims at one of
    /// the squad, hurrying up a threatened ally still to act, breaking a seen enemy's plan (a shove, a pull, a disrupt), a
    /// free strike or an aim card when it can attack a hostile now, a flank step round to a seen enemy's side or back when
    /// none is in reach; smoke on itself when hurt and seen, a defense card or a barrier toward the hostiles when seen; a
    /// mend when it is hurt; a draw when the hand runs low; else a dead hard card nobody can use is discarded. Only cards
    /// the rules accept now (<see cref="CardPlay.Check"/>) that cost no AP or leave AP for a blow.
    /// </summary>
    public static IScavengeCommand? Card(ScavengeSim sim, Unit unit, int playerId)
    {
        var state = sim.State;
        var data = sim.Data;
        if (SquadDecks.Of(state, playerId) is not { Hidden: false } deck) return null;
        var current = state.CurrentUnitId == unit.Id;
        var profile = sim.Profile(unit);
        var cheapest = profile.Actions.Select(a => a.ApCost).DefaultIfEmpty(1).Min();
        bool Playable(CardDef c) =>
            c.Cost <= deck.Points && (c.Requirement.Kind == CardKind.Baseline || c.Requirement.Met(unit))
            && !(c.ArmLock && ProfileDeriver.ArmsLocked(data, unit.Body, twoHanded: false))
            && (c.ApCost == 0 || unit.Ap >= c.ApCost + cheapest) && (current || c.Reaction);
        var hand = deck.Hand.Distinct().Select(data.Cards.Find).OfType<CardDef>().OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var playable = hand.Where(Playable).ToList();
        PlayCard Play(CardDef c, int? target = null, Pos? tile = null) => new(playerId, unit.Id, c.Id, target, Tile: tile);
        PlayCard? Try(IEnumerable<CardDef> cards, IEnumerable<(int? Target, Pos? Tile)> aims)
        {
            var list = aims.ToList();
            foreach (var c in cards)
                foreach (var (target, tile) in list)
                    if (CardPlay.Check(sim, unit, Play(c, target, tile)).Ok) return Play(c, target, tile);
            return null;
        }
        IEnumerable<(int?, Pos?)> At(IEnumerable<Unit> units) => units.Select(u => ((int?)u.Id, (Pos?)null));

        if (Threats(sim, unit) is { Count: > 0 } threats
            && Try(playable.Where(c => c.Reaction).OrderByDescending(Breaks), At(threats)) is { } answer) return answer;
        if (!current) return null;
        var hostiles = state.Units.Where(u => u.IsActive && unit.HostileTo(u) && Detection.SideSees(data, state, unit.Side, u))
            .OrderBy(h => h.Pos.Chebyshev(unit.Pos)).ThenBy(h => h.Id).ToList();
        var squad = state.Units.Where(u => u.PlayerId == playerId && u.PlayerControlled && u.IsActive).ToList();
        var nonReactions = playable.Where(c => !c.Reaction).ToList();
        bool StillToAct(Unit u) => state.TurnOrder.IndexOf(u.Id) > state.TurnIndex;

        // Turn order: an enemy about to bring a blow down on one of us goes last; a threatened ally goes next, to get away.
        var aimedAtUs = hostiles.Where(h => h.Plan?.AttackTile is { } t && squad.Any(s => s.Pos == t) && StillToAct(h));
        if (Try(nonReactions.Where(c => c.Effect.DelayInitiative), At(aimedAtUs)) is { } hold) return hold;
        var threatened = squad.Where(s => s.Id != unit.Id && StillToAct(s) && Threats(sim, s).Count > 0);
        if (Try(nonReactions.Where(c => c.Effect.AdvanceInitiative), At(threatened)) is { } hurry) return hurry;
        // A seen plan is broken: shoved or pulled off its tile, or disrupted.
        if (Try(nonReactions.Where(Breaks), At(hostiles.Where(h => h.Plan is not null))) is { } broken) return broken;

        var inReach = hostiles.Where(h => profile.Actions.Any(a => a.ApCost <= unit.Ap && Combat.CheckReach(data, state.Map, unit.Pos, a, h.Pos) == ScavengeError.None)).ToList();
        if (Try(nonReactions.Where(c => c.Effect.Strike), At(inReach)) is { } strike) return strike;
        if (inReach.Count > 0 && unit.CardHit <= 0 && nonReactions.FirstOrDefault(c => c.Effect.Hit > 0) is { } aim) return Play(aim);
        if (inReach.Count == 0 && Try(nonReactions.Where(c => c.Effect.Step > 0), FlankTiles(sim, unit, hostiles)) is { } flank) return flank;

        var seenBy = hostiles.Where(h => Detection.Spots(data, state, h, unit)).ToList();
        var hurt = Treatment.WorstInjury(unit.Body);
        if (seenBy.Count > 0 && hurt >= SmokeInjuryAt && Try(nonReactions.Where(c => c.Effect.Smoke > 0), [(null, unit.Pos)]) is { } smoke) return smoke;
        if (seenBy.Count > 0 && unit.CardCover <= 0 && nonReactions.FirstOrDefault(c => c.Effect.Cover > 0) is { } cover) return Play(cover);
        if (seenBy.Count > 0 && unit.Guard == 0 && nonReactions.FirstOrDefault(c => c.Effect.Guard > 0) is { } guard) return Play(guard);
        if (seenBy.FirstOrDefault(h => h.Pos.Chebyshev(unit.Pos) > 1) is { } shooter
            && Combat.CoverAgainst(data, state.Map, unit.Pos, shooter.Pos) <= 0
            && Try(nonReactions.Where(c => c.Effect.PlaceBarrier > 0), [(null, unit.Pos + Combat.Face(unit.Pos, shooter.Pos))]) is { } barrier) return barrier;
        if (hurt >= MendInjuryAt && nonReactions.FirstOrDefault(c => c.Effect.Mend > 0) is { } mend) return Play(mend);
        if (deck.Hand.Count <= 2 && nonReactions.FirstOrDefault(c => c.Effect.Draw > 0) is { } draw) return Play(draw);
        if (deck.DeadDiscardRound != state.Round
            && hand.FirstOrDefault(c => c.Requirement.Kind == CardKind.Hard && !squad.Any(c.Requirement.Met)) is { } dead)
            return new DiscardDeadCard(playerId, unit.Id, dead.Id);
        return null;
    }

    /// <summary>A bot smokes itself in once a body part is at least this badly hurt and a hostile sees it (a bot habit).</summary>
    public const double SmokeInjuryAt = 0.3;

    /// <summary>Whether a card breaks a plan it is aimed at: it moves the planner off its tile or disrupts it.</summary>
    private static bool Breaks(CardDef c) => c.Effect.Disrupt || c.Effect.Push > 0 || c.Effect.Pull > 0;

    /// <summary>
    /// Tiles next to seen hostiles off their front (their back first, then their side), nearest hostile first, from which
    /// the unit's attacks reach it.
    /// </summary>
    private static IEnumerable<(int?, Pos?)> FlankTiles(ScavengeSim sim, Unit unit, List<Unit> hostiles)
    {
        var actions = sim.Profile(unit).Actions;
        foreach (var h in hostiles.Where(h => !h.Structure))
            foreach (var t in Pos.Neighbours8.Select(d => h.Pos + d)
                         .Where(t => sim.State.Map.InBounds(t) && Combat.ArcOf(h, t) != FacingArc.Front
                                     && actions.Any(a => Combat.CheckReach(sim.Data, sim.State.Map, t, a, h.Pos) == ScavengeError.None))
                         .OrderByDescending(t => Combat.ArcOf(h, t)).ThenBy(t => t.Chebyshev(unit.Pos)).ThenBy(t => sim.State.Map.Index(t)))
                yield return (null, t);
    }

    /// <summary>
    /// A move off the tile a seen enemy plan aims at, to the cheapest tile no seen plan aims at (more cover against the
    /// threats first), or null when not threatened or with nowhere to go.
    /// </summary>
    public static Move? Dodge(ScavengeSim sim, Unit unit, int playerId)
    {
        var threats = Threats(sim, unit);
        if (threats.Count == 0) return null;
        var state = sim.State;
        var aimed = state.Units.Where(u => u.IsActive && unit.HostileTo(u) && u.Plan?.AttackTile is not null
                                           && Detection.SideSees(sim.Data, state, unit.Side, u))
            .Select(u => u.Plan!.AttackTile!.Value).ToHashSet();
        double Cover(Pos p) => threats.Select(t => Combat.CoverAgainst(sim.Data, state.Map, p, t.Pos)).Min();
        var to = sim.Reachable(unit).Where(kv => kv.Value <= unit.Ap && !aimed.Contains(kv.Key))
            .OrderByDescending(kv => Cover(kv.Key)).ThenBy(kv => kv.Value).ThenBy(kv => state.Map.Index(kv.Key))
            .Select(kv => (Pos?)kv.Key).FirstOrDefault();
        return to is { } tile ? new Move(playerId, unit.Id, tile) : null;
    }

    /// <summary>
    /// One move that brings the unit closer to the nearest of <paramref name="goals"/> by walking distance (round walls), the
    /// closest such tile first; straight-line distance only where no walk gets there. Null when no move gets closer.
    /// </summary>
    public static Move? StepToward(ScavengeSim sim, Unit unit, int playerId, IReadOnlyList<Pos> goals)
    {
        if (goals.Count == 0) return null;
        var map = sim.State.Map;
        var movement = sim.Profile(unit).Movement;
        var fields = goals.Select(g => Movement.DistanceField(sim.State, unit, movement, g)).ToList();
        int Distance(Pos p) => fields.Select(f => f.TryGetValue(p, out var d) ? d : int.MaxValue).Min() is var walk && walk < int.MaxValue
            ? walk : 1000 + goals.Min(g => g.Chebyshev(p));
        var here = Distance(unit.Pos);
        var step = sim.Reachable(unit).Where(kv => kv.Value <= unit.Ap && Distance(kv.Key) < here)
            .OrderBy(kv => Distance(kv.Key)).ThenBy(kv => kv.Value).ThenBy(kv => map.Index(kv.Key))
            .Select(kv => (Pos?)kv.Key).FirstOrDefault();
        return step is { } to ? new Move(playerId, unit.Id, to) : null;
    }

    /// <summary>
    /// A plan for a unit with nothing in reach now: one move to the tile from which its best attack reaches the nearest of
    /// <paramref name="targets"/> (or none needed), and that attack at the target's tile, within the AP a plan may use.
    /// Null when no such plan exists.
    /// </summary>
    public static DeclareIntent? Plan(ScavengeSim sim, Unit unit, int playerId, IReadOnlyList<Unit> targets)
    {
        if (targets.Count == 0 || unit.Plan is not null) return null;
        var data = sim.Data;
        var map = sim.State.Map;
        var budget = PlannedActions.Budget(sim, unit);
        var actions = sim.Profile(unit).Actions;
        var stay = new KeyValuePair<Pos, int>(unit.Pos, 0);
        foreach (var target in targets.OrderBy(t => t.Pos.Chebyshev(unit.Pos)).ThenBy(t => t.Id))
        {
            var best = sim.Reachable(unit).Append(stay)
                .SelectMany(kv => actions
                    .Where(a => Drones.CanStrike(a, target) && kv.Value + Combat.ApCost(a, kv.Key, target.Pos) <= budget
                                && Combat.CheckReach(data, map, kv.Key, a, target.Pos) == ScavengeError.None)
                    .Select(a => (Tile: kv.Key, Cost: kv.Value, Action: a, Value: (a.MinDamage + a.MaxDamage) / 2 * a.Accuracy)))
                .OrderByDescending(o => o.Value).ThenBy(o => o.Cost).ThenBy(o => map.Index(o.Tile)).ThenBy(o => o.Action.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (best.Action is null) continue;
            return new DeclareIntent(playerId, unit.Id, best.Tile == unit.Pos ? [] : [best.Tile], target.Pos, best.Action.Id);
        }
        return null;
    }
}
