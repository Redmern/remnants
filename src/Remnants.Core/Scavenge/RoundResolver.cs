namespace Remnants.Core.Scavenge;

/// <summary>
/// The resolve phase: every unit's moves in priority order, then every action on the new positions in priority order, each
/// unit's in the order it queued them. Scores are taken afresh before each pick, so what has happened so far counts. Hostiles
/// on the same score resolve together, each at <see cref="PriorityRules.TieEffect"/>: each from where it stood when the
/// clash began, and even if the other strikes it down. Same plans and seed, same events.
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
    }

    /// <summary>
    /// Hostiles on one score: all their actions go off, at reduced effect, from the tiles they stood on when it began. Blows
    /// come first, then shoves, then dashes, so nothing turns on which unit is older. A dash runs from wherever the shoves left it.
    /// </summary>
    private static void Clash(ScavengeSim sim, List<(Unit Unit, UnitPlan Plan, PlanStep Step)> group)
    {
        var state = sim.State;
        var tiles = group.Select(g => g.Unit.Pos).ToList();
        foreach (var g in group) g.Plan.Steps.RemoveAt(0);
        state.Log(new(state.Round, ScavengeEventType.Clashed, $"{string.Join(" and ", group.Select(g => g.Unit.Name))} act in the same instant.",
            group[0].Unit.Id, group[1].Unit.Id, group[0].Unit.Pos));
        sim.EffectFactor = sim.Data.Rules.Priority.TieEffect;
        foreach (var i in Enumerable.Range(0, group.Count).OrderBy(i => ClashOrder(sim, group[i].Step)))
        {
            var unit = group[i].Unit;
            if (ClashOrder(sim, group[i].Step) == DashesLast)
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

    private static int ClashOrder(ScavengeSim sim, PlanStep step)
    {
        if (step.Kind == PlanStepKind.Dash || step.Landing is not null) return DashesLast;
        if (step.Kind == PlanStepKind.Push) return 1;
        return step.CardId is { } id && sim.Data.Cards.Find(id)?.Effect is { } e && (e.Push > 0 || e.Pull > 0 || e.Swap || e.PushAll > 0) ? 1 : 0;
    }

    private static void Resolve(ScavengeSim sim, Unit unit, PlanStep step)
    {
        switch (step.Kind)
        {
            case PlanStepKind.Strike:
                TileStrikes.Resolve(sim, unit, step.Strike!);
                break;
            case PlanStepKind.Card:
                CardPlay.Resolve(sim, unit, step);
                break;
            case PlanStepKind.Recover:
                Economy.Recovered(sim, unit);
                break;
            case PlanStepKind.Push:
                Pushes.Resolve(sim, unit, step);
                break;
            case PlanStepKind.Dash:
                Pushes.ResolveDash(sim, unit, step);
                break;
        }
    }
}
