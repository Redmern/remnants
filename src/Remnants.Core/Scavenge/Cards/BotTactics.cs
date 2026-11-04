using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Simple heuristics for scripted players (the playtest bot, the server's bot): which card to play now, and closing in to
/// strike when nothing is in reach. They read only what a player's view holds (their own deck, seen units, never anyone's
/// plan) and return ordinary commands, which Core checks.
/// </summary>
public static class BotTactics
{
    /// <summary>A bot plays a mend card once a body part is at least this badly hurt (a bot habit, not a game rule).</summary>
    public const double MendInjuryAt = 0.2;

    /// <summary>Where <paramref name="side"/> should expect a unit when the round's blows fall: at the end of its telegraphed walk, or where it stands.</summary>
    public static Pos Expected(ScavengeSim sim, string side, Unit u) => TacticsView.Telegraphs(sim, side).Find(t => t.UnitId == u.Id)?.To ?? u.Pos;

    /// <summary>What <paramref name="side"/> can tell a seen unit may reach this round: going by its telegraphed walk and nothing else of its plan.</summary>
    public static HashSet<Pos> SeenThreat(ScavengeSim sim, string side, Unit u) => Combat.ThreatFrom(sim, u, Expected(sim, side, u));

    /// <summary>Seen hostiles that could strike <paramref name="unit"/>'s tile from where they stand.</summary>
    public static List<Unit> Threats(ScavengeSim sim, Unit unit) =>
        sim.State.Units.Where(u => u.IsActive && unit.HostileTo(u) && Detection.SideSees(sim.Data, sim.State, unit.Side, u) && SeenThreat(sim, unit.Side, u).Contains(Plans.Anchor(sim.State, unit)))
            .OrderBy(u => u.Id).ToList();

    /// <summary>
    /// The card to play on <paramref name="unit"/> now, or null. By priority: holding back a seen enemy with one of the squad
    /// in its reach, hurrying up a threatened ally, breaking what a seen enemy with the unit in reach may be planning (a
    /// shove, a pull, a disrupt), a block when one has it in reach, a free strike, a swing or an aim card when it can attack a hostile now, a flank step round to a seen enemy's side or back when
    /// none is in reach; a smoke screen toward who sees it when hurt, a defense card or a barrier toward the hostiles when seen; a
    /// mend when it is hurt; a draw when the hand runs low; else a dead hard card nobody can use is discarded. Only cards
    /// the rules accept now (<see cref="CardPlay.Check"/>).
    /// </summary>
    public static IScavengeCommand? Card(ScavengeSim sim, Unit unit, int playerId)
    {
        var state = sim.State;
        var data = sim.Data;
        if (SquadDecks.Of(state, playerId) is not { Hidden: false } deck) return null;
        var at = Plans.Anchor(state, unit);
        var profile = sim.Profile(unit);
        bool Playable(CardDef c) =>
            Plans.HasRoom(sim, unit, PlanSlot.Card) && c.Cost <= deck.Points && (c.Requirement.Kind == CardKind.Baseline || c.Requirement.Met(unit))
            && !(c.ArmLock && ProfileDeriver.ArmsLocked(data, unit.Body, twoHanded: false));
        var hand = deck.Hand.Distinct().Select(data.Cards.Find).OfType<CardDef>().OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var playable = hand.Where(Playable).ToList();
        PlayCard Play(CardDef c, int? target = null, Pos? tile = null, Pos? direction = null) => new(playerId, unit.Id, c.Id, target, Tile: tile, Direction: direction);
        PlayCard? Try(IEnumerable<CardDef> cards, IEnumerable<(int? Target, Pos? Tile)> aims)
        {
            var list = aims.ToList();
            foreach (var c in cards)
                foreach (var (target, tile) in list)
                    if (CardPlay.Check(sim, unit, Play(c, target, tile)).Ok) return Play(c, target, tile);
            return null;
        }
        IEnumerable<(int?, Pos?)> At(IEnumerable<Unit> units) => units.Select(u => ((int?)u.Id, (Pos?)null));

        var hostiles = state.Units.Where(u => u.IsActive && unit.HostileTo(u) && Detection.SideSees(data, state, unit.Side, u))
            .OrderBy(h => h.Pos.Chebyshev(at)).ThenBy(h => h.Id).ToList();
        var squad = state.Units.Where(u => u.PlayerId == playerId && u.PlayerControlled && u.IsActive).ToList();
        // An enemy that could strike one of us is held up; a threatened ally hurried along.
        var aimedAtUs = hostiles.Where(h => h.PriorityBonus >= 0 && SeenThreat(sim, unit.Side, h) is var zone && squad.Any(s => zone.Contains(Plans.Anchor(state, s))));
        if (Try(playable.Where(c => c.Effect.DelayInitiative), At(aimedAtUs)) is { } hold) return hold;
        var threatened = squad.Where(s => s.Id != unit.Id && s.PriorityBonus <= 0 && Threats(sim, s).Count > 0);
        if (Try(playable.Where(c => c.Effect.AdvanceInitiative), At(threatened)) is { } hurry) return hurry;
        // A foe seen walking off, or that a friend is about to move, would only be a card wasted.
        var moved = BeingMoved(sim, unit.Side);
        if (Try(playable.Where(Breaks), At(Threats(sim, unit).Where(t => !moved.Contains(t.Id) && Expected(sim, unit.Side, t) == t.Pos))) is { } broken) return broken;
        // In reach of someone: guard where it will stand, turned toward the nearest of them.
        if (!Guarding(state, data, unit) && Threats(sim, unit).FirstOrDefault() is { } threat)
            foreach (var c in playable.Where(c => c.Effect.Block > 0))
                if (Play(c, tile: c.Target == CardTarget.Tile ? at : null, direction: Combat.Face(at, threat.Pos)) is var guarded && CardPlay.Check(sim, unit, guarded).Ok) return guarded;

        var inReach = hostiles.Where(h => profile.Actions.Any(a => a.ApCost <= unit.Ap && Combat.CheckReach(data, state.Map, at, a, Expected(sim, unit.Side, h)) == ScavengeError.None)).ToList();
        if (Try(playable.Where(c => c.Effect.Strike), At(inReach)) is { } strike) return strike;
        // A swing through the most hostiles it can see, never through a friend.
        foreach (var c in hand.Where(c => c.Effect.Attack && c.Pattern is not null && c.Cost <= deck.Points && (c.Requirement.Kind == CardKind.Baseline || c.Requirement.Met(unit))))
        {
            var best = Pos.Neighbours8.Select(d => (Dir: d, Tiles: TacticsView.PatternAim(sim, unit, c.Id, null, d)))
                .Where(o => o.Tiles.AllyTiles.Count == 0)
                .Select(o => (o.Dir, Hits: hostiles.Count(h => o.Tiles.Tiles.Contains(Expected(sim, unit.Side, h)))))
                .Where(o => o.Hits > 0).OrderByDescending(o => o.Hits).ThenBy(o => o.Dir.Y).ThenBy(o => o.Dir.X).FirstOrDefault();
            if (best.Hits > 0 && Play(c, direction: best.Dir) is var swing && CardPlay.Check(sim, unit, swing).Ok) return swing;
        }
        if (inReach.Count > 0 && unit.CardHit <= 0 && playable.FirstOrDefault(c => c.Effect.Hit > 0) is { } aim) return Play(aim);
        if (inReach.Count == 0 && Try(playable.Where(c => c.Effect.Step > 0), FlankTiles(sim, unit, hostiles)) is { } flank) return flank;

        var seenBy = hostiles.Where(h => Detection.Spots(data, state, h, unit)).ToList();
        var hurt = Treatment.WorstInjury(unit.Body);
        if (seenBy.Count > 0 && hurt >= SmokeInjuryAt)
            foreach (var c in playable.Where(c => c.Effect.Smoke > 0))
                if (Try([c], [(null, CardAbilities.ScreenTile(unit, seenBy[0].Pos, c.Radius))]) is { } smoke) return smoke;
        if (seenBy.Count > 0 && unit.CardCover <= 0 && playable.FirstOrDefault(c => c.Effect.Cover > 0) is { } cover) return Play(cover);
        if (seenBy.Count > 0 && unit.Guard == 0 && playable.FirstOrDefault(c => c.Effect.Guard > 0) is { } guard) return Play(guard);
        if (seenBy.FirstOrDefault(h => h.Pos.Chebyshev(at) > 1) is { } shooter
            && Combat.CoverAgainst(data, state.Map, at, shooter.Pos) <= 0
            && !WalkedOnto(sim, unit, at + Combat.Face(at, shooter.Pos))
            && Try(playable.Where(c => c.Effect.PlaceBarrier > 0), [(null, at + Combat.Face(at, shooter.Pos))]) is { } barrier) return barrier;
        if (hurt >= MendInjuryAt && playable.FirstOrDefault(c => c.Effect.Mend > 0) is { } mend) return Play(mend);
        if (deck.Hand.Count <= 2 && playable.FirstOrDefault(c => c.Effect.Draw > 0) is { } draw) return Play(draw);
        if (deck.DeadDiscardRound != state.Round
            && hand.FirstOrDefault(c => c.Requirement.Kind == CardKind.Hard && !squad.Any(c.Requirement.Met)) is { } dead)
            return new DiscardDeadCard(playerId, unit.Id, dead.Id);
        return null;
    }

    /// <summary>Whether the unit has a block queued already this round.</summary>
    internal static bool Guarding(ScavengeState state, ScavengeData data, Unit unit) =>
        Plans.Of(state, unit)?.Steps.Any(s => s.Kind == PlanStepKind.Card && data.Cards.Find(s.CardId!)?.Effect.Block > 0) == true;

    /// <summary>A bot throws a smoke screen once a body part is at least this badly hurt and a hostile sees it (a bot habit).</summary>
    public const double SmokeInjuryAt = 0.3;

    /// <summary>Whether a card upsets what its target is up to: it shoves it about or disrupts it.</summary>
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
                         .OrderByDescending(t => Combat.ArcOf(h, t)).ThenBy(t => t.Chebyshev(Plans.Anchor(sim.State, unit))).ThenBy(t => sim.State.Map.Index(t)))
                yield return (null, t);
    }

    /// <summary>Who the side's own plans are about to shove or pull off their tile.</summary>
    public static HashSet<int> BeingMoved(ScavengeSim sim, string side)
    {
        var (state, moved) = (sim.State, new HashSet<int>());
        foreach (var unit in state.Units.Where(u => u.Side == side))
            foreach (var step in Plans.Of(state, unit)?.Steps ?? [])
                if (step.Kind == PlanStepKind.Card && step.TargetId is { } id && sim.Data.Cards.Find(step.CardId!) is { } card && Breaks(card)) moved.Add(id);
                else if (step.Kind == PlanStepKind.Push)
                    foreach (var tile in Patterns.Tiles(state.Map, sim.Data.Rules.Combat.Push.Pattern, step.From, step.Direction))
                        if (state.UnitAt(tile) is { } on) moved.Add(on.Id);
        return moved;
    }

    /// <summary>A push on someone who is no enemy and stands where the unit must go, or null.</summary>
    public static Push? Unblock(ScavengeSim sim, Unit unit, int playerId, IReadOnlyList<Pos> goals)
    {
        var (state, movement) = (sim.State, sim.Profile(unit).Movement);
        if (!Plans.HasRoom(sim, unit, PlanSlot.Action) || unit.Ap < sim.Data.Rules.Combat.Push.ApCost) return null;
        var at = Plans.Anchor(state, unit);
        // Boxed in: every step that gets it closer is taken, and one of them by someone who is no enemy.
        foreach (var goal in goals)
        {
            var field = Movement.DistanceField(state.Map, movement, goal);
            if (!field.TryGetValue(at, out var here)) continue;
            var closer = Pos.Neighbours8.Where(way => field.GetValueOrDefault(at + way, int.MaxValue) < here && (way.X == 0 || way.Y == 0 || Movement.CornerOpen(state.Map, movement, at, way))).ToList();
            if (closer.Count == 0 || closer.Any(way => state.UnitAt(at + way) is null)) continue;
            foreach (var way in closer)
                if (state.UnitAt(at + way) is { } there && !unit.HostileTo(there))
                    return new Push(playerId, unit.Id, way);
        }
        return null;
    }

    /// <summary>
    /// What to do instead of a plain blow, by what the side can see, or null to fight on: a push when it drives a seen
    /// neighbour into something; hurt or with two on it, a dash clear of every seen reach, else a guard toward the nearest;
    /// winded with nothing in reach, a rest. Never looks at a plan its side cannot see.
    /// </summary>
    public static IScavengeCommand? Counter(ScavengeSim sim, Unit unit, int playerId)
    {
        if (!Plans.HasRoom(sim, unit, PlanSlot.Action)) return null;
        var (state, rules, bot) = (sim.State, sim.Data.Rules.Combat, sim.Data.Rules.Bot);
        var at = Plans.Anchor(state, unit);
        var threats = Threats(sim, unit);
        if (threats.Count == 0)
        {
            var profile = sim.Profile(unit);
            return profile.MaxStamina > 0 && (double)Economy.Stamina(sim, unit) / profile.MaxStamina < sim.Data.Rules.Priority.CliffBelow
                   && state.Units.Any(u => u.IsActive && unit.HostileTo(u) && Detection.SideSees(sim.Data, state, unit.Side, u))
                ? new Recover(playerId, unit.Id) : null;
        }

        var nearest = threats.OrderBy(t => t.Pos.Chebyshev(at)).ThenBy(t => t.Id).First();
        if (unit.Ap >= rules.Push.ApCost)
            foreach (var way in Pos.Neighbours8)
                if (TacticsView.PushPreview(sim, unit, way).Any(o => o.Collides && state.FindUnit(o.UnitId) is { } v && unit.HostileTo(v) && v.LastMove == default && Expected(sim, unit.Side, v) == v.Pos && !BeingMoved(sim, unit.Side).Contains(v.Id)) && !TacticsView.PushPreview(sim, unit, way).Any(o => !unit.HostileTo(state.FindUnit(o.UnitId)!)))
                    return new Push(playerId, unit.Id, way);

        if (unit.Hp >= unit.MaxHp * bot.HurtBelow && threats.Count < 2)
            // A neighbour that stood still will likely swing: every so often take it on the guard, for the block and the stun.
            return (state.Round + unit.Id) % bot.GuardEvery == 0 && nearest.LastMove == default && nearest.Pos.Chebyshev(at) <= 1 && unit.Ap >= rules.Defend.ApCost
                ? new Defend(playerId, unit.Id, Combat.Face(at, nearest.Pos)) : null;
        // Not twice running: dashing back and forth can stall a run for good.
        var dashed = unit.Ai.DashedRound > 0 && unit.Ai.DashedRound == state.Round - 1;
        if (!dashed && unit.Ap >= rules.Dash.ApCost && Clear(sim, unit) is { } clear) return new Dash(playerId, unit.Id, clear.Way, clear.Tiles);
        return unit.Ap >= rules.Defend.ApCost ? new Defend(playerId, unit.Id, Combat.Face(at, Expected(sim, unit.Side, nearest))) : null;
    }

    /// <summary>Whether someone the side can see stands on the tile, or will once the walks are over.</summary>
    private static bool WalkedOnto(ScavengeSim sim, Unit unit, Pos tile) =>
        sim.State.Units.Any(u => u.Id != unit.Id && u.IsActive && u.OnMap && Detection.SideSees(sim.Data, sim.State, unit.Side, u)
                                 && (u.Pos == tile || (u.Side == unit.Side ? Plans.MoveEnd(sim.State, u) : Expected(sim, unit.Side, u)) == tile));

    /// <summary>The shortest dash that lands the unit out of every seen hostile's reach, or null.</summary>
    private static (Pos Way, int Tiles)? Clear(ScavengeSim sim, Unit unit)
    {
        var state = sim.State;
        var zones = state.Units.Where(u => u.IsActive && unit.HostileTo(u) && Detection.SideSees(sim.Data, state, unit.Side, u)).SelectMany(u => SeenThreat(sim, unit.Side, u)).ToHashSet();
        var from = Plans.Anchor(state, unit);
        var best = ((Pos Way, int Tiles)?)null;
        foreach (var way in Pos.Neighbours8)
            for (var tiles = 1; tiles <= sim.Data.Rules.Combat.Dash.Tiles; tiles++)
            {
                // A dash never goes through anyone it can see.
                var landing = Pushes.PlannedLanding(sim, unit, way, tiles);
                if (landing != from + new Pos(way.X * tiles, way.Y * tiles) || WalkedOnto(sim, unit, landing)) break;
                if (zones.Contains(landing)) continue;
                if (best is null || tiles < best.Value.Tiles) best = (way, tiles);
                break;
            }
        return best;
    }

    /// <summary>
    /// The move to the tile the unit's next blow on a seen hostile does the most from: its chance, its damage, the arc it comes
    /// from, less for every seen reach the tile stands in. Null when no walk beats where it is, or it has already planned.
    /// </summary>
    public static Move? Position(ScavengeSim sim, Unit unit, int playerId)
    {
        // One walk to a tile, then the blow: circling a foe that holds for a walker would go on for ever.
        if (unit.LastMove != default || !Plans.HasRoom(sim, unit, PlanSlot.Move) || !Plans.HasRoom(sim, unit, PlanSlot.Action)) return null;
        var (state, data) = (sim.State, sim.Data);
        var seen = state.Units.Where(u => u.IsActive && u.OnMap && unit.HostileTo(u) && !u.Structure && Detection.SideSees(data, state, unit.Side, u)).ToList();
        if (seen.Count == 0) return null;
        var zones = seen.Select(u => SeenThreat(sim, unit.Side, u)).ToList();
        var actions = sim.Profile(unit).Actions;
        double Worth(Pos tile)
        {
            var best = 0.0;
            foreach (var target in seen)
                foreach (var action in actions)
                {
                    var to = Expected(sim, unit.Side, target);
                    if (Combat.ApCost(action, tile, to) > unit.Ap || Combat.CheckReach(data, state.Map, tile, action, to) != ScavengeError.None) continue;
                    var worth = Combat.ChanceToHit(sim, unit, action, target, tile) * (action.MinDamage + action.MaxDamage) / 2 * Combat.ArcDamage(data.Rules.Combat.Facing, Combat.ArcOf(target, tile));
                    best = Math.Max(best, worth);
                }
            return best / (1 + data.Rules.Bot.PositionRisk * zones.Count(z => z.Contains(tile)));
        }
        var here = Worth(Plans.Anchor(state, unit));
        var (top, topWorth) = ((Pos?)null, here * data.Rules.Bot.PositionGain);
        foreach (var (tile, cost) in sim.Reachable(unit).Where(kv => kv.Value <= unit.Mp).OrderBy(kv => kv.Value).ThenBy(kv => state.Map.Index(kv.Key)))
            if (state.UnitAt(tile) is null && Worth(tile) is var worth && worth > topWorth && worth > 0)
                (top, topWorth) = (tile, worth);
        return top is { } to2 ? new Move(playerId, unit.Id, to2) : null;
    }

    /// <summary>
    /// One move that brings the unit closer to the nearest of <paramref name="goals"/> by walking distance (round walls), the
    /// closest such tile first; straight-line distance only where no walk gets there. Null when no move gets closer.
    /// </summary>
    public static Move? StepToward(ScavengeSim sim, Unit unit, int playerId, IReadOnlyList<Pos> goals)
    {
        if (goals.Count == 0 || !Plans.HasRoom(sim, unit, PlanSlot.Move)) return null;
        var map = sim.State.Map;
        var movement = sim.Profile(unit).Movement;
        var fields = goals.Select(g => Movement.DistanceField(sim.State, unit, movement, g)).ToList();
        int Distance(Pos p) => fields.Select(f => f.TryGetValue(p, out var d) ? d : int.MaxValue).Min() is var walk && walk < int.MaxValue
            ? walk : 1000 + goals.Min(g => g.Chebyshev(p));
        var here = Distance(Plans.Anchor(sim.State, unit));
        var step = sim.Reachable(unit).Where(kv => kv.Value <= unit.Mp && Distance(kv.Key) < here)
            .OrderBy(kv => Distance(kv.Key)).ThenBy(kv => kv.Value).ThenBy(kv => map.Index(kv.Key))
            .Select(kv => (Pos?)kv.Key).FirstOrDefault();
        return step is { } to ? new Move(playerId, unit.Id, to) : null;
    }

    /// <summary>
    /// A move for a unit with nothing in reach now: to the tile from which its best attack reaches the nearest of
    /// <paramref name="targets"/>, with the AP for that attack left. Null when no such move exists.
    /// </summary>
    public static Move? Approach(ScavengeSim sim, Unit unit, int playerId, IReadOnlyList<Unit> targets)
    {
        if (!Plans.HasRoom(sim, unit, PlanSlot.Move)) return null;
        var data = sim.Data;
        var map = sim.State.Map;
        var actions = sim.Profile(unit).Actions;
        foreach (var target in targets.OrderBy(t => t.Pos.Chebyshev(unit.Pos)).ThenBy(t => t.Id))
        {
            var to = Expected(sim, unit.Side, target);
            var best = sim.Reachable(unit)
                .SelectMany(kv => actions
                    .Where(a => Drones.CanStrike(a, target) && kv.Value <= unit.Mp && Combat.ApCost(a, kv.Key, to) <= unit.Ap
                                && Combat.CheckReach(data, map, kv.Key, a, to) == ScavengeError.None)
                    .Select(a => (Tile: kv.Key, Cost: kv.Value, Action: a, Value: (a.MinDamage + a.MaxDamage) / 2 * a.Accuracy)))
                .OrderByDescending(o => o.Value).ThenBy(o => o.Cost).ThenBy(o => map.Index(o.Tile)).ThenBy(o => o.Action.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (best.Action is not null) return new Move(playerId, unit.Id, best.Tile);
        }
        return null;
    }
}
