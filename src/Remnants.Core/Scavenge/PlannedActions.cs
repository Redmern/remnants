using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Telegraphed plans (<see cref="DeclareIntent"/>, <see cref="PlannedAction"/>). Declaring is free, makes no noise and ends
/// the turn; the plan must fit a full turn's AP (what it pays from when it resolves), every waypoint one move from the last (a move
/// reach that injured legs shrink, <see cref="ProfileDeriver.LegReach"/>), and the attack tile hittable from the end of
/// the path. It is locked. At the start of the unit's next turn it resolves through the plain move and attack rules,
/// paid from that turn's AP: the path stops where a step no longer works (blocked, legs hurt since, out of AP), and the
/// attack strikes whoever hostile stands on the tile then. Nobody there is a miss and the AP is spent all the same; the
/// one it was aimed at, if they moved off and the blow came down on the tile, dodged it (<see cref="Unit.OutplayedId"/>,
/// good through the next round). Being downed, killed, pinned or
/// stunned (an EMP) cancels the plan.
/// </summary>
public static class PlannedActions
{
    internal static ScavengeResult Declare(ScavengeSim sim, Unit unit, DeclareIntent c)
    {
        var state = sim.State;
        if (unit.Plan is not null) return ScavengeResult.Fail(ScavengeError.PlanLocked, $"{unit.Name} already has a plan.");
        if (c.Path.Count == 0 && c.AttackTile is null || (c.AttackTile is null) != (c.ActionId is null))
            return ScavengeResult.Fail(ScavengeError.InvalidTarget, "A plan needs a path, an attack (a tile and an action), or both.");

        var budget = Budget(sim, unit);
        ScavengeResult NotEnough(int need) => ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {need} AP for that plan (has {budget}).");
        // Every move costs at least 1 AP: a longer path cannot fit, and is refused before any reachability is worked out.
        if (c.Path.Count > budget) return NotEnough(c.Path.Count);

        var start = unit.Pos;
        var ap = 0;
        try
        {
            foreach (var step in c.Path)
            {
                if (!sim.Reachable(unit).TryGetValue(step, out var cost))
                    return ScavengeResult.Fail(ScavengeError.Unreachable, $"{unit.Name} cannot reach {step} from {unit.Pos} in one move.");
                ap += cost;
                if (ap > budget) return NotEnough(ap);
                unit.Pos = step;
            }
        }
        finally
        {
            unit.Pos = start;
        }
        var end = c.Path.Count > 0 ? c.Path[^1] : start;

        int? targetId = null;
        StrikeMode? mode = null;
        Pos? direction = null;
        if (c.AttackTile is { } tile)
        {
            var action = sim.Profile(unit).Action(c.ActionId!);
            if (action is null) return ScavengeResult.Fail(ScavengeError.UnknownAction, $"{unit.Name} cannot use '{c.ActionId}' now.");
            if (c.Suppress && action.Pattern == AttackPattern.Melee)
                return ScavengeResult.Fail(ScavengeError.UnknownAction, $"{action.Name} cannot lay down suppressing fire.");
            if (action.Patterned && !c.Suppress)
            {
                if (TileStrikes.Aim(sim, unit, action, tile, c.Mode, c.Direction, end) is not { } aim)
                    return c.Mode is { } m && action.Modes?.Contains(m) != true
                        ? ScavengeResult.Fail(ScavengeError.InvalidTarget, $"{action.Name} cannot {m.ToString().ToLowerInvariant()}.")
                        : ScavengeResult.Fail(ScavengeError.OutOfRange, $"{tile} is not in reach of {action.Name} from {end}.");
                if (!Combat.Clear(sim.Data, state.Map, end, tile))
                    return ScavengeResult.Fail(ScavengeError.NoLineOfSight, $"{tile} is not in sight of {end}.");
                (mode, direction) = (aim.Mode, aim.Direction);
            }
            else
            {
                if (c.Mode is not null) return ScavengeResult.Fail(ScavengeError.InvalidTarget, $"{action.Name} has no stab or slash.");
                var reach = Combat.CheckReach(sim.Data, state.Map, end, action, tile);
                if (reach != ScavengeError.None) return ScavengeResult.Fail(reach, $"{tile} is not in reach of {action.Name} from {end} ({reach}).");
            }
            ap += Combat.ApCost(action, end, tile);
            targetId = TargetAt(state, unit, tile) is { } t && Detection.SideSees(sim.Data, state, unit.Side, t) ? t.Id : null;
        }
        if (ap > budget) return NotEnough(ap);

        unit.Plan = new PlannedAction
        {
            Path = c.Path.ToList(), AttackTile = c.AttackTile, ActionId = c.ActionId, TargetId = targetId, Round = state.Round,
            CardId = unit.LastCardId, MoveBonus = unit.CardMove, Suppress = c.Suppress, Mode = mode, Direction = direction,
        };
        var what = (c.Path.Count > 0 ? $"a move to {end}" : "") + (c.Path.Count > 0 && c.AttackTile is not null ? " and " : "")
                   + (c.AttackTile is { } at ? $"an attack on {at}" : "");
        state.Log(new(state.Round, ScavengeEventType.IntentDeclared, $"{unit.Name} plans {what}.", unit.Id, Pos: unit.Pos));
        return ScavengeResult.Success($"{unit.Name} plans {what}.");
    }

    /// <summary>
    /// The AP a plan may use: a full turn's (<see cref="UnitProfile.MaxAp"/>), the AP it pays from when it resolves
    /// (whatever the unit has left now; more if a stim left it above its maximum).
    /// </summary>
    public static int Budget(ScavengeSim sim, Unit unit) => Math.Max(unit.Ap, sim.Profile(unit).MaxAp);

    /// <summary>Carries out the unit's plan at the start of its turn (after its AP refill), then clears it.</summary>
    internal static void Resolve(ScavengeSim sim, Unit unit)
    {
        if (unit.Plan is not { } plan) return;
        unit.Plan = null;
        var state = sim.State;
        string? failed = null;
        // A move card played with the plan still counts for its path.
        unit.CardMove = plan.MoveBonus;
        foreach (var step in plan.Path)
        {
            var moved = ScavengeCommandHandlers.Move(sim, unit, new Move(unit.PlayerId ?? 0, unit.Id, step));
            if (!unit.IsActive) return;
            if (!moved.Ok)
            {
                failed = $"cannot reach {step}";
                break;
            }
        }

        unit.CardMove = 0;
        CardPlay.Counters(sim, unit);
        if (!unit.IsActive) return;

        if (plan.AttackTile is { } tile && plan.ActionId is { } id)
        {
            var action = sim.Profile(unit).Action(id);
            var target = TargetAt(state, unit, tile);
            var swung = false;
            List<Unit>? struck = null;
            if (action is null) failed = $"can no longer use {id}";
            // A tile strike from where the path ended: a stab / slash covers its tiles, any other action the one tile.
            else if (TileStrikes.Aim(sim, unit, action, tile, plan.Mode, plan.Direction) is not { } aim || !Combat.Clear(sim.Data, state.Map, unit.Pos, tile))
                failed = $"{tile} is out of reach";
            else if (Combat.ApCost(action, unit.Pos, tile) > unit.Ap) failed = "has no AP left for the attack";
            else if (plan.Suppress && target is not null && Drones.CanStrike(action, target))
            {
                Combat.Suppress(sim, unit, action, target);
                swung = true;
            }
            else
            {
                // The blow comes down on every hostile there in sight; with nobody there the AP goes and the noise carries.
                unit.Ap -= Combat.ApCost(action, unit.Pos, tile);
                struck = TileStrikes.Strike(sim, unit, action, aim.Tiles, tile, aim.Direction);
                if (struck.Count == 0) failed = $"{action.Name} hits nothing at {tile}";
                target = struck.FirstOrDefault();
                swung = true;
            }
            // Only a blow that came down on the tile can be dodged; a plan that fell short dodged nobody.
            if (swung && plan.TargetId is { } aimed && aimed != target?.Id && struck?.Any(u => u.Id == aimed) != true
                && state.FindUnit(aimed) is { IsActive: true } dodger && dodger.Pos != tile)
            {
                dodger.OutplayedId = unit.Id;
                dodger.OutplayedUntilRound = state.Round + 1;
            }
        }

        if (failed is null)
            state.Log(new(state.Round, ScavengeEventType.IntentResolved, $"{unit.Name} carries out their plan.", unit.Id, Pos: unit.Pos));
        else
            state.Log(new(state.Round, ScavengeEventType.IntentFizzled, $"{unit.Name}'s plan falls through: {failed}.", unit.Id, Pos: unit.Pos));
    }

    /// <summary>Drops the unit's plan, if it has one, saying why.</summary>
    internal static void Cancel(ScavengeSim sim, Unit unit, string why)
    {
        if (unit.Plan is null) return;
        unit.Plan = null;
        sim.State.Log(new(sim.State.Round, ScavengeEventType.IntentCanceled, $"{unit.Name}'s plan is off: {why}.", unit.Id, Pos: unit.Pos));
    }

    /// <summary>The unit a plan's attack would strike on a tile now: a conscious hostile standing there (lowest id first).</summary>
    private static Unit? TargetAt(ScavengeState state, Unit attacker, Pos tile) =>
        state.Units.Where(u => u.IsActive && u.OnMap && u.Pos == tile && u.CarriedById is null && attacker.HostileTo(u))
            .OrderBy(u => u.Id).FirstOrDefault();
}
