using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// In a fight, using an item, hacking and firing a drone take the Action slot like any other action: paid when planned, hidden
/// from rivals, going off in the resolve phase by priority, where a faster blow can stop them. With nobody hostile in sight
/// (<see cref="InFight"/>) they stay instant, as do looting and the other field commands. The rules are the handlers' own: they
/// check once when planned, from where the plan ends, and run when the action comes up; if it no longer works it fizzles.
/// </summary>
public static class PlannedActions
{
    /// <summary>Whether the unit's side sees a hostile that can act: a fight, for what planning is concerned.</summary>
    public static bool InFight(ScavengeSim sim, Unit unit)
    {
        var state = sim.State;
        return state.Units.Any(u => u.IsActive && u.OnMap && u.CarriedById is null && !u.Structure && unit.HostileTo(u) && Plans.Acts(sim, u)
                                    && Detection.SideSees(sim.Data, state, unit.Side, u));
    }

    internal static ScavengeResult UseItem(ScavengeSim sim, Unit unit, UseItem c)
    {
        if (!InFight(sim, unit)) return ScavengeCommandHandlers.UseItem(sim, unit, c);
        var check = Plans.AtAnchor(sim.State, unit, () => ScavengeCommandHandlers.UseItem(sim, unit, c, checkOnly: true));
        if (!check.Ok) return check;
        var use = sim.Item(c.ItemId).Use!;
        return Queue(sim, unit, use.TargetsTile ? use.ApCost : FieldMedicine.ApCost(unit, use), new PlanStep { Kind = PlanStepKind.UseItem, ItemId = c.ItemId, TargetId = c.TargetId, Tile = c.At },
            $"{unit.Name} will use {sim.Item(c.ItemId).Name}.");
    }

    internal static ScavengeResult Hack(ScavengeSim sim, Unit unit, Hack c)
    {
        if (!InFight(sim, unit)) return Hacking.Hack(sim, unit, c);
        var check = Plans.AtAnchor(sim.State, unit, () => Hacking.Hack(sim, unit, c, checkOnly: true));
        if (!check.Ok) return check;
        return Queue(sim, unit, Hacking.ApCost(sim.Data, unit), new PlanStep { Kind = PlanStepKind.Hack, DeviceId = c.DeviceId, TargetId = c.TargetId }, $"{unit.Name} will hack.");
    }

    internal static ScavengeResult DroneFire(ScavengeSim sim, Unit unit, DroneFire c)
    {
        if (!InFight(sim, unit)) return DroneRoles.Fire(sim, unit, c);
        var check = DroneRoles.Fire(sim, unit, c, checkOnly: true);
        if (!check.Ok) return check;
        var drone = Drones.DroneOf(sim.State, unit)!;
        return Queue(sim, unit, sim.Profile(drone).Actions[0].ApCost, new PlanStep { Kind = PlanStepKind.DroneFire, TargetId = c.TargetId, Tile = sim.State.FindUnit(c.TargetId)!.Pos },
            $"{drone.Name} will fire.");
    }

    private static ScavengeResult Queue(ScavengeSim sim, Unit unit, int cost, PlanStep step, string says)
    {
        if (Plans.PayAction(sim, unit, cost, "for that", out var free) is { } refused) return refused;
        step.Free = free;
        step.From = Plans.Anchor(sim.State, unit);
        Plans.For(sim.State, unit).Steps.Add(step);
        return ScavengeResult.Success(says);
    }

    /// <summary>The action comes up. Its points were paid when it was planned, so the handler is given what it asks for.</summary>
    internal static void Resolve(ScavengeSim sim, Unit unit, PlanStep step)
    {
        var (pid, ap) = (unit.PlayerId ?? 0, unit.Ap);
        ScavengeResult result;
        unit.Ap = int.MaxValue / 2;
        try
        {
            result = step.Kind switch
            {
                PlanStepKind.UseItem => ScavengeCommandHandlers.UseItem(sim, unit, new UseItem(pid, unit.Id, step.ItemId!, step.TargetId, step.Tile)),
                PlanStepKind.Hack => Hacking.Hack(sim, unit, new Hack(pid, unit.Id, step.DeviceId, step.TargetId)),
                _ => DroneRoles.FireAt(sim, unit, step.Tile!.Value),
            };
        }
        finally
        {
            unit.Ap = ap;
        }
        if (!result.Ok)
            sim.State.Log(new(sim.State.Round, ScavengeEventType.IntentFizzled, $"{unit.Name}'s plan falls through: {result.Message}", unit.Id, Pos: unit.Pos));
    }
}
