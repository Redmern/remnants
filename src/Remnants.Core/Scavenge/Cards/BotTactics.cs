using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Simple heuristics for scripted players (the playtest bot, the server's bot): which card to play now, stepping off a
/// tile a seen enemy plan aims at, and declaring a plan to close in and strike when nothing is in reach. They read only
/// what a player's view holds (their own deck, seen units and plans) and return ordinary commands, which Core checks.
/// </summary>
public static class BotTactics
{
    /// <summary>Seen hostiles whose telegraphed attack is aimed at <paramref name="unit"/>'s tile.</summary>
    public static List<Unit> Threats(ScavengeSim sim, Unit unit) =>
        sim.State.Units.Where(u => u.IsActive && unit.HostileTo(u) && u.Plan is { AttackTile: { } t } && t == unit.Pos
                                   && Detection.SideSees(sim.Data, sim.State, unit.Side, u))
            .OrderBy(u => u.Id).ToList();

    /// <summary>
    /// The card to play on <paramref name="unit"/> now, or null. By priority: a reaction against a seen plan aimed at it;
    /// an aim card when it can attack a hostile now; a defense card when a hostile sees it; a mend when it is hurt; a
    /// draw when the hand runs low; else a dead hard card nobody can use is discarded. Only cards it may play, that the
    /// points cover and that cost no AP or leave AP for a blow.
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
        PlayCard Play(CardDef c, int? target = null) => new(playerId, unit.Id, c.Id, target);

        if (Threats(sim, unit).FirstOrDefault() is { } threat && playable.FirstOrDefault(c => c.Reaction) is { } reaction) return Play(reaction, threat.Id);
        if (!current) return null;
        var hostiles = state.Units.Where(u => u.IsActive && unit.HostileTo(u) && Detection.SideSees(data, state, unit.Side, u)).ToList();
        var inReach = hostiles.Any(h => profile.Actions.Any(a => a.ApCost <= unit.Ap && Combat.CheckReach(data, state.Map, unit.Pos, a, h.Pos) == ScavengeError.None));
        var seen = hostiles.Any(h => Detection.Spots(data, state, h, unit));
        var nonReactions = playable.Where(c => !c.Reaction).ToList();
        if (inReach && unit.CardHit <= 0 && nonReactions.FirstOrDefault(c => c.Effect.Hit > 0) is { } aim) return Play(aim);
        if (seen && unit.CardCover <= 0 && nonReactions.FirstOrDefault(c => c.Effect.Cover > 0) is { } cover) return Play(cover);
        if (seen && unit.Guard == 0 && nonReactions.FirstOrDefault(c => c.Effect.Guard > 0) is { } guard) return Play(guard);
        if (Treatment.WorstInjury(unit.Body) >= 0.2 && nonReactions.FirstOrDefault(c => c.Effect.Mend > 0) is { } mend) return Play(mend);
        if (deck.Hand.Count <= 2 && nonReactions.FirstOrDefault(c => c.Effect.Draw > 0) is { } draw) return Play(draw);
        var squad = state.Units.Where(u => u.PlayerId == playerId && u.PlayerControlled && u.IsActive).ToList();
        if (deck.DeadDiscardRound != state.Round
            && hand.FirstOrDefault(c => c.Requirement.Kind == CardKind.Hard && !squad.Any(c.Requirement.Met)) is { } dead)
            return new DiscardDeadCard(playerId, unit.Id, dead.Id);
        return null;
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
