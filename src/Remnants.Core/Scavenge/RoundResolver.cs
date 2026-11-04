namespace Remnants.Core.Scavenge;

/// <summary>
/// The resolve phase: every unit's moves in priority order, then every action on the new positions in priority order, each
/// unit's in the order it queued them. Scores are taken afresh before each pick, so what has happened so far counts. Hostiles
/// on the same score resolve together, each at <see cref="PriorityRules.TieEffect"/>: each from where it stood when the
/// clash began, and even if the other strikes it down. Same plans and seed, same events.
/// Two exceptions to going by score, the counters: a unit with a dash queued is off before a push reaches it, and runs into a
/// blow aimed at where it lands (<see cref="DashAway"/>). A recover pays only once all the actions are done, so anything landing
/// on the unit this round catches it (<see cref="Economy.CaughtRecovering"/>).
/// </summary>
internal static class RoundResolver
{
    private const double Epsilon = 1e-9;

    public static void Run(ScavengeSim sim)
    {
        Movement(sim);
        Actions(sim);
        foreach (var plan in sim.State.Plans)
            if (sim.State.FindUnit(plan.UnitId) is { } unit)
                foreach (var card in plan.Steps.Where(s => s.Kind == PlanStepKind.Card)) CardPlay.Lost(sim, unit, card);
        sim.State.Plans.Clear();
    }

    /// <summary>The movement phase: each unit walks its whole plan on its turn, stopping short of whatever now stands in the way.</summary>
    internal static void Movement(ScavengeSim sim)
    {
        var state = sim.State;
        var order = state.Plans.Select(p => state.FindUnit(p.UnitId)).OfType<Unit>().Where(u => u.IsActive)
            .OrderByDescending(u => Priority.Movement(sim, u)).ThenBy(u => Priority.TieBreak(state.Round, u.Id)).ToList();
        foreach (var unit in order)
        {
            if (state.Finished) return;
            if (!unit.IsActive || Plans.Of(state, unit) is not { } plan) continue;
            foreach (var step in plan.Steps.Where(s => s.Kind == PlanStepKind.Move).ToList())
            {
                plan.Steps.Remove(step);
                if (!Walk(sim, unit, step)) break;
            }
            plan.Steps.RemoveAll(s => s.Kind == PlanStepKind.Move);
            sim.AfterStep();
        }
    }

    /// <summary>One leg of a move; false when it could not be walked to its end.</summary>
    private static bool Walk(ScavengeSim sim, Unit unit, PlanStep step)
    {
        var state = sim.State;
        var from = unit.Pos;
        var stop = from;
        foreach (var p in step.Path)
        {
            if (state.UnitAt(p) is { } other && other.Id != unit.Id)
            {
                if (other.HostileTo(unit)) break;
                continue;
            }
            if (Hacking.Blocks(state, unit, p)) break;
            stop = p;
        }
        if (stop == from) return false;
        unit.Pos = stop;
        unit.Moved = true;
        unit.Facing = Combat.Face(from, stop);
        // The carried unit moves along before anything reacts to the move.
        if (unit.CarryingId is { } carried && state.FindUnit(carried) is { } load) load.Pos = stop;
        state.Log(new(state.Round, ScavengeEventType.Moved, $"{unit.Name} moved {from} -> {stop}.", unit.Id, Pos: stop));
        sim.Moved(unit, from);
        return stop == step.To;
    }

    /// <summary>The action phase.</summary>
    internal static void Actions(ScavengeSim sim)
    {
        var state = sim.State;
        // Cards that acted when played only held their slot until now.
        foreach (var plan in state.Plans) plan.Steps.RemoveAll(s => s.Applied is not null);
        Defences.Arm(sim);
        sim.Recovering.Clear();
        foreach (var plan in state.Plans.OrderBy(p => p.UnitId))
        {
            sim.Recovering.AddRange(plan.Steps.Where(s => s.Kind == PlanStepKind.Recover).Select(_ => plan.UnitId));
            plan.Steps.RemoveAll(s => s.Kind == PlanStepKind.Recover);
        }
        while (!state.Finished)
        {
            var heads = state.Plans.OrderBy(p => p.UnitId)
                .Select(p => (Unit: state.FindUnit(p.UnitId), Plan: p))
                .Where(h => h.Unit is { IsActive: true } && h.Plan.Steps.Count > 0)
                .Select(h => (Unit: h.Unit!, Step: h.Plan.Steps[0], Plan: h.Plan, Score: Priority.Action(sim, h.Unit!, h.Plan.Steps[0])))
                .ToList();
            if (heads.Count == 0) break;
            var best = heads.Max(h => h.Score);
            var top = heads.Where(h => h.Score >= best - Epsilon).OrderBy(h => Priority.TieBreak(state.Round, h.Unit.Id)).ToList();
            var clash = top.Where(h => top.Any(o => o.Unit.HostileTo(h.Unit))).ToList();
            if (clash.Count > 1) Clash(sim, clash.Select(h => (h.Unit, h.Plan, h.Step)).ToList());
            else
            {
                var (unit, step, plan, _) = top[0];
                plan.Steps.RemoveAt(0);
                Resolve(sim, unit, step);
            }
            sim.AfterStep();
        }
        state.Defences.Clear();
        foreach (var id in sim.Recovering.ToList())
            if (!state.Finished && state.FindUnit(id) is { IsActive: true } unit) Economy.Recovered(sim, unit);
        sim.Recovering.Clear();
        sim.AfterStep();
    }

    /// <summary>
    /// Hostiles on one score: all their actions go off, at reduced effect, from the tiles they stood on when it began. Blows
    /// come first, then shoves, then dashes, so nothing turns on which unit is older. A dash runs from wherever the shoves left it,
    /// unless it slips one of those shoves or runs into one of those blows (<see cref="DashAway"/>): then it goes first.
    /// </summary>
    private static void Clash(ScavengeSim sim, List<(Unit Unit, UnitPlan Plan, PlanStep Step)> group)
    {
        var state = sim.State;
        var tiles = group.Select(g => g.Unit.Pos).ToList();
        foreach (var g in group) g.Plan.Steps.RemoveAt(0);
        state.Log(new(state.Round, ScavengeEventType.Clashed, $"{string.Join(" and ", group.Select(g => g.Unit.Name))} act in the same instant.",
            group[0].Unit.Id, group[1].Unit.Id, group[0].Unit.Pos));
        sim.EffectFactor = sim.Data.Rules.Priority.TieEffect;
        var order = Enumerable.Range(0, group.Count)
            .Select(i => (Index: i, Rank: DashGoesFirst(sim, group, i) ? -1 : ClashOrder(sim, group[i].Step))).OrderBy(x => x.Rank).ToList();
        foreach (var (i, rank) in order)
        {
            var unit = group[i].Unit;
            if (rank is -1 or DashesLast)
            {
                Resolve(sim, unit, group[i].Step);
                continue;
            }
            // From its tile at the start, unless something has been put there since. Back afterwards, unless its own tile was
            // taken while it was away: then it stays, so no two units ever share a tile.
            var now = unit.Pos;
            var from = state.UnitAt(tiles[i]) is { } there && there.Id != unit.Id ? now : tiles[i];
            unit.Pos = from;
            Resolve(sim, unit, group[i].Step);
            if (unit.Pos == from && state.UnitAt(now) is null) unit.Pos = now;
        }
        sim.EffectFactor = 1;
    }

    private const int DashesLast = 2;

    /// <summary>A plain dash in a clash that a hostile's shove would catch where it stands, or a hostile's blow where it lands.</summary>
    private static bool DashGoesFirst(ScavengeSim sim, List<(Unit Unit, UnitPlan Plan, PlanStep Step)> group, int i)
    {
        var (dasher, _, dash) = group[i];
        if (dash.Kind != PlanStepKind.Dash) return false;
        foreach (var (other, _, step) in group.Where(g => g.Unit.HostileTo(dasher)))
        {
            if (ClashOrder(sim, step) == DashesLast) continue;
            if (PatternOf(sim, other, step, other.Pos) is not { } tiles) continue;
            if (ClashOrder(sim, step) == 1 ? tiles.Contains(dasher.Pos) : RunsInto(sim, dasher, dash, other, tiles)) return true;
        }
        return false;
    }

    /// <summary>Whether a dash leaves the tiles of a blow and lands on them, in the striker's line of sight.</summary>
    private static bool RunsInto(ScavengeSim sim, Unit dasher, PlanStep dash, Unit striker, List<Pos> tiles)
    {
        if (tiles.Contains(dasher.Pos)) return false;
        var landing = CardAbilities.Slide(sim, dasher, dash.Direction, Math.Max(1, (int)(dash.Reach * sim.EffectFactor)));
        return landing != dasher.Pos && tiles.Contains(landing) && Combat.Clear(sim.Data, sim.State.Map, striker.Pos, landing);
    }

    /// <summary>
    /// The unit's queued dash goes off now, ahead of its score, because something is about to catch it. Returns whether it got
    /// away from where it stood; a dash with nowhere to go is spent all the same.
    /// </summary>
    internal static bool DashAway(ScavengeSim sim, Unit unit)
    {
        if (Plans.Of(sim.State, unit) is not { } plan || plan.Steps.Find(s => s.Kind == PlanStepKind.Dash) is not { } dash) return false;
        var from = unit.Pos;
        plan.Steps.Remove(dash);
        Resolve(sim, unit, dash);
        return unit.Pos != from;
    }

    /// <summary>A blow is about to come down: anyone hostile whose queued dash lands on its tiles runs into it.</summary>
    private static void RunIntoBlow(ScavengeSim sim, Unit striker, PlanStep step)
    {
        var effect = step.CardId is { } id ? sim.Data.Cards.Find(id)?.Effect : null;
        if (step.Kind != PlanStepKind.Strike && effect is not { Attack: true, Dash: 0 }) return;
        if (PatternOf(sim, striker, step, striker.Pos) is not { } tiles) return;
        var state = sim.State;
        foreach (var plan in state.Plans.OrderBy(p => p.UnitId).ToList())
            if (state.FindUnit(plan.UnitId) is { IsActive: true } unit && unit.HostileTo(striker)
                && plan.Steps.Find(s => s.Kind == PlanStepKind.Dash) is { } dash && RunsInto(sim, unit, dash, striker, tiles))
                DashAway(sim, unit);
    }

    private static int ClashOrder(ScavengeSim sim, PlanStep step)
    {
        if (step.Kind == PlanStepKind.Dash || step.Landing is not null) return DashesLast;
        if (step.Kind == PlanStepKind.Push) return 1;
        return step.CardId is { } id && sim.Data.Cards.Find(id)?.Effect is { } e && (e.Push > 0 || e.Pull > 0 || e.Swap || e.PushAll > 0) ? 1 : 0;
    }

    private static void Resolve(ScavengeSim sim, Unit unit, PlanStep step)
    {
        RunIntoBlow(sim, unit, step);
        var (logged, from) = (sim.State.Logged, unit.Pos);
        Act(sim, unit, step);
        if (PatternOf(sim, unit, step, from) is not { } tiles) return;
        var state = sim.State;
        var news = state.Events.TakeLast(state.Logged - logged).Where(e => e.UnitId == unit.Id).ToList();
        bool Did(params ScavengeEventType[] types) => news.Exists(e => types.Contains(e.Type));
        var effect = step.CardId is { } id ? sim.Data.Cards.Find(id)?.Effect : null;
        var landed = step.Kind switch
        {
            PlanStepKind.Strike => Did(ScavengeEventType.Attacked),
            PlanStepKind.Push => Did(ScavengeEventType.Displaced, ScavengeEventType.Collided),
            _ when effect is { Attack: true } => Did(ScavengeEventType.Attacked),
            _ => !Did(ScavengeEventType.IntentFizzled),
        };
        // Names who was struck: a side that does not know them hears nothing of it, so a flash never gives away someone in the fog.
        var struck = news.Find(e => e.TargetId is not null)?.TargetId;
        state.Log(new(state.Round, ScavengeEventType.PatternResolved, "", unit.Id, struck, unit.Pos, Tiles: tiles, Landed: landed));
    }

    /// <summary>The tiles a resolved action covered, as it came up from <paramref name="from"/>: null for one without a pattern.</summary>
    private static List<Pos>? PatternOf(ScavengeSim sim, Unit unit, PlanStep step, Pos from)
    {
        var map = sim.State.Map;
        switch (step.Kind)
        {
            case PlanStepKind.Strike:
                var shift = from - step.Strike!.From;
                return step.Strike.Tiles.Select(p => p + shift).Where(map.InBounds).ToList();
            case PlanStepKind.Push:
                return Patterns.Tiles(map, sim.Data.Rules.Combat.Push.Pattern, from, step.Direction);
            case PlanStepKind.Defend:
                // The guard as it came up, on the unit if the walk stopped short.
                return sim.State.Defences.Find(d => d.UnitId == unit.Id && d.CardId == Defences.ActionId)?.Tiles.ToList() ?? Defences.Tiles(sim, unit, from, step.Direction);
            case PlanStepKind.Dash:
                var ran = unit.Pos == from ? step.Reach : unit.Pos.Chebyshev(from);
                return Enumerable.Range(1, ran).Select(i => from + new Pos(step.Direction.X * i, step.Direction.Y * i)).Where(map.InBounds).ToList();
            case PlanStepKind.Card when sim.Data.Cards.Find(step.CardId!) is { Pattern: { } pattern, Effect: var effect }:
                // A dash strikes from where it lands; a pattern with no tile goes from where the unit stands now.
                var aim = effect.Dash > 0 || effect.PushAll > 0 ? unit.Pos
                    : step.Tile is not { } t ? from
                    : effect.Attack ? t + (from - step.From) : t;
                return Patterns.Tiles(map, pattern, aim, step.Direction);
            default:
                return null;
        }
    }

    private static void Act(ScavengeSim sim, Unit unit, PlanStep step)
    {
        switch (step.Kind)
        {
            case PlanStepKind.Strike:
                TileStrikes.Resolve(sim, unit, step.Strike!);
                break;
            case PlanStepKind.Card:
                CardPlay.Resolve(sim, unit, step);
                break;
            case PlanStepKind.Defend:
                Defences.CameUp(sim.State, unit, Defences.ActionId);
                break;
            case PlanStepKind.Push:
                Pushes.Resolve(sim, unit, step);
                break;
            case PlanStepKind.Dash:
                Pushes.ResolveDash(sim, unit, step);
                break;
            case PlanStepKind.UseItem or PlanStepKind.Hack or PlanStepKind.DroneFire:
                PlannedActions.Resolve(sim, unit, step);
                break;
        }
    }
}
