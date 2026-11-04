namespace Remnants.Core.Scavenge;

/// <summary>
/// Push and dash: hidden actions paid at plan time, going off in the action phase by priority. A push moves whoever stands
/// on its pattern away from the pusher (<see cref="One"/>); what they queued still goes off, from where they land. A dash runs
/// straight on and never through anyone, and a unit with one queued slips a push (<see cref="RoundResolver.DashAway"/>). Neither is
/// telegraphed: only the movement phase is.
/// </summary>
public static class Pushes
{
    private static Pos? Straight(Pos direction)
    {
        var dir = new Pos(Math.Sign(direction.X), Math.Sign(direction.Y));
        return dir == default ? null : dir;
    }

    /// <summary>Queues a push the way the unit aims it.</summary>
    internal static ScavengeResult Plan(ScavengeSim sim, Unit unit, Push c)
    {
        var rules = sim.Data.Rules.Combat.Push;
        if (Straight(c.Direction) is not { } dir) return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{unit.Name} must push some way.");
        if (Plans.PayAction(sim, unit, rules.ApCost, "to push", out var free) is { } refused) return refused;
        Plans.For(sim.State, unit).Steps.Add(new PlanStep { Kind = PlanStepKind.Push, From = Plans.Anchor(sim.State, unit), Direction = dir, Free = free });
        return ScavengeResult.Success($"{unit.Name} will push.");
    }

    /// <summary>Queues a dash. The plan knows the terrain, never who may be in the way: a hidden unit only shows when the dash runs into it.</summary>
    internal static ScavengeResult Plan(ScavengeSim sim, Unit unit, Dash c)
    {
        var rules = sim.Data.Rules.Combat.Dash;
        if (Straight(c.Direction) is not { } dir) return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{unit.Name} must dash some way.");
        if (c.Tiles < 1 || c.Tiles > rules.Tiles) return ScavengeResult.Fail(ScavengeError.OutOfRange, $"{unit.Name} dashes 1 to {rules.Tiles} tiles.");
        var from = Plans.Anchor(sim.State, unit);
        var landing = PlannedLanding(sim, unit, dir, c.Tiles);
        if (landing == from) return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{unit.Name} has no room to dash that way.");
        if (Plans.PayAction(sim, unit, rules.ApCost, "to dash", out var free) is { } refused) return refused;
        Plans.For(sim.State, unit).Steps.Add(new PlanStep { Kind = PlanStepKind.Dash, From = from, Direction = dir, Reach = c.Tiles, Landing = landing, Free = free });
        return ScavengeResult.Success($"{unit.Name} will dash.");
    }

    /// <summary>Where a dash from the end of the unit's plan stops, going by the terrain alone.</summary>
    internal static Pos PlannedLanding(ScavengeSim sim, Unit unit, Pos dir, int tiles) =>
        Plans.AtAnchor(sim.State, unit, () => CardAbilities.Slide(sim, unit, dir, tiles, terrainOnly: true));

    /// <summary>A queued push comes up.</summary>
    internal static void Resolve(ScavengeSim sim, Unit unit, PlanStep step)
    {
        var rules = sim.Data.Rules.Combat.Push;
        Over(sim, unit, Patterns.Tiles(sim.State.Map, rules.Pattern, unit.Pos, step.Direction), rules.Tiles);
    }

    /// <summary>A queued dash comes up.</summary>
    internal static void ResolveDash(ScavengeSim sim, Unit unit, PlanStep step) => Run(sim, unit, step.Direction, step.Reach);

    /// <summary>The push's tiles from where the unit's plan ends, and the allies on them.</summary>
    public static List<Pos> Tiles(ScavengeSim sim, Unit unit, Pos direction) =>
        Patterns.Tiles(sim.State.Map, sim.Data.Rules.Combat.Push.Pattern, Plans.Anchor(sim.State, unit), direction);

    /// <summary>Pushes everyone on <paramref name="tiles"/> in the unit's line of sight, the farthest first so nobody is in the way of a friend pushed along. Returns who moved.</summary>
    internal static List<Unit> Over(ScavengeSim sim, Unit unit, List<Pos> tiles, int distance)
    {
        var state = sim.State;
        var clear = tiles.Where(p => Combat.Clear(sim.Data, state.Map, unit.Pos, p)).ToHashSet();
        List<Unit> On() => state.Units
            .Where(u => u.Id != unit.Id && u.IsActive && u.OnMap && u.CarriedById is null && !u.Structure && !CardAbilities.Fixed(u) && clear.Contains(u.Pos))
            .OrderByDescending(u => u.Pos.Chebyshev(unit.Pos)).ThenBy(u => u.Id).ToList();
        // A dash beats a push: whoever has one queued is off before the shove arrives.
        var slipped = On().Where(u => unit.HostileTo(u) && RoundResolver.DashAway(sim, u)).ToList();
        var victims = On();
        if (victims.Count == 0)
        {
            state.Log(new(state.Round, ScavengeEventType.IntentFizzled,
                slipped.Count > 0 ? $"{unit.Name}'s push finds nobody: {string.Join(" and ", slipped.Select(u => u.Name))} dashed clear." : $"{unit.Name}'s push meets nobody.",
                unit.Id, Pos: unit.Pos));
            Exposed.Mark(sim, unit);
        }
        var pushed = victims.Where(v => One(sim, unit, v, Combat.Face(unit.Pos, v.Pos), distance)).ToList();
        Scored(sim, unit, pushed.Count > 0);
        return pushed;
    }

    /// <summary>
    /// One unit pushed: its own Defend broken if the push is hostile, a tile less off a tile still defended, and on until a wall or a unit stops it, with collision damage. It is never
    /// passed on: whoever it hits stays put. Returns whether it landed: a push held off entirely did not.
    /// </summary>
    internal static bool One(ScavengeSim sim, Unit by, Unit victim, Pos dir, int distance)
    {
        var state = sim.State;
        Defences.Break(sim, by, victim);
        if (Defences.At(sim, by, victim.Pos) is { } block)
        {
            distance--;
            state.Log(new(state.Round, ScavengeEventType.Blocked, $"{block.Defender.Name}'s block holds {victim.Name} against the push.", block.Defender.Id, victim.Id, victim.Pos,
                Anonymous: $"{victim.Name} holds against a push."));
            if (distance <= 0) return false;
        }
        distance = Math.Max(1, (int)(distance * sim.EffectFactor));
        var from = victim.Pos;
        var landing = CardAbilities.Slide(sim, victim, dir, distance);
        if (landing != from) CardAbilities.Displace(sim, victim, landing, by);
        if (landing.Chebyshev(from) < distance && victim.IsActive) Combat.Collide(sim, by, victim, state.UnitAt(landing + dir));
        return true;
    }

    /// <summary>How a push went for the pusher's adrenaline: a strong gain, or a failed action.</summary>
    internal static void Scored(ScavengeSim sim, Unit unit, bool landed)
    {
        if (landed) Adrenaline.Gain(sim, unit, sim.Data.Rules.Adrenaline.GainPush);
        else Adrenaline.Failed(sim, unit);
    }

    /// <summary>The unit runs straight on until something stops it. Returns whether it got anywhere.</summary>
    internal static bool Run(ScavengeSim sim, Unit unit, Pos dir, int tiles)
    {
        var state = sim.State;
        var from = unit.Pos;
        var to = CardAbilities.Slide(sim, unit, dir, Math.Max(1, (int)(tiles * sim.EffectFactor)));
        unit.Facing = dir;
        if (to == from)
        {
            state.Log(new(state.Round, ScavengeEventType.IntentFizzled, $"{unit.Name}'s dash is blocked.", unit.Id, Pos: from));
            Adrenaline.Failed(sim, unit);
            return false;
        }
        unit.Pos = to;
        if (unit.CarryingId is { } carried && state.FindUnit(carried) is { } load) load.Pos = to;
        unit.Moved = true;
        unit.Ai.DashedRound = state.Round;
        state.Log(new(state.Round, ScavengeEventType.Moved, $"{unit.Name} dashes to {to}.", unit.Id, Pos: to));
        sim.Moved(unit, from);
        Adrenaline.Gain(sim, unit, sim.Data.Rules.Adrenaline.GainDash);
        return true;
    }
}
