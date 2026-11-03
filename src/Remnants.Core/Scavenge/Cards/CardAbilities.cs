namespace Remnants.Core.Scavenge;

/// <summary>
/// What an ability card (<see cref="CardEffect.IsAbility"/>) does to the board, and whether it can: its target
/// (<see cref="CardDef.Target"/> within <see cref="CardDef.Range"/>, in sight of the player), then each effect's own
/// rules. <see cref="Check"/> works the outcome out without changing anything (the card preview, the bots, the play
/// itself); <see cref="Apply"/> carries it out. Deterministic: no dice, ties by tile and id.
/// <para>
/// Displacement (push, pull, swap) moves a unit tile by tile along the line and stops before a wall, any other
/// impassable tile, a blocked door, another unit or the map edge; it never ends on an occupied tile. A displaced unit's
/// plan fizzles (it left the tile it planned from) and, if a hostile moved it, it is exposed to that side
/// (<see cref="Combat.Exposed"/>). Until its own next sub-turn it is hit by a declared strike on the tile it was moved to,
/// whoever's side the strike is: friendly fire is allowed for a unit shoved into the line of fire.
/// </para>
/// </summary>
public static class CardAbilities
{
    /// <summary>
    /// What an ability would do, or why not (<see cref="Fail"/>): the unit it acts on and where it lands, the tile aimed at,
    /// the tile the player steps to.
    /// </summary>
    public sealed record Outcome(ScavengeResult? Fail, Unit? Target = null, Pos? Tile = null, Pos? Landing = null)
    {
        public bool Ok => Fail is null;
    }

    /// <summary>The target an ability card aims at and what its effect would do, without changing anything.</summary>
    public static Outcome Check(ScavengeSim sim, Unit unit, CardDef card, CardEffect effect, int? targetId, Pos? tile)
    {
        var state = sim.State;
        var data = sim.Data;
        Outcome Fail(ScavengeError e, string m) => new(ScavengeResult.Fail(e, m));
        Unit? target = null;
        if (card.Target is CardTarget.Ally or CardTarget.Enemy or CardTarget.Unit)
        {
            target = targetId is { } id ? state.FindUnit(id) : null;
            if (target is not { IsActive: true, OnMap: true, CarriedById: null })
                return Fail(ScavengeError.InvalidTarget, $"{card.Name} needs a unit to aim at.");
            if (target.Side != unit.Side && !Detection.SideSees(data, state, unit.Side, target))
                return Fail(ScavengeError.NotVisible, $"{unit.Name}'s side does not see {target.Name}.");
            if (card.Target == CardTarget.Enemy && !unit.HostileTo(target))
                return Fail(ScavengeError.InvalidTarget, $"{card.Name} is played on an enemy.");
            if (card.Target == CardTarget.Ally && (unit.HostileTo(target) || target.Side == Unit.NeutralSide))
                return Fail(ScavengeError.InvalidTarget, $"{card.Name} is played on an ally.");
            if (card.Range > 0 && unit.Pos.Chebyshev(target.Pos) > card.Range)
                return Fail(ScavengeError.OutOfRange, $"{target.Name} is out of {card.Name}'s range ({card.Range}).");
            if (target.Id != unit.Id && !Combat.Clear(data, state.Map, unit.Pos, target.Pos))
                return Fail(ScavengeError.NoLineOfSight, $"{unit.Name} has no clear line to {target.Name}.");
        }

        Pos? landing = null;
        if (effect.Push > 0 || effect.Pull > 0)
        {
            if (target!.Id == unit.Id) return Fail(ScavengeError.InvalidTarget, $"{unit.Name} cannot shove themselves.");
            if (Fixed(target)) return Fail(ScavengeError.InvalidTarget, $"{target.Name} cannot be moved.");
            var dir = effect.Push > 0 ? Combat.Face(unit.Pos, target.Pos) : Combat.Face(target.Pos, unit.Pos);
            landing = Slide(sim, target, dir, Math.Max(effect.Push, effect.Pull));
            if (landing == target.Pos) return Fail(ScavengeError.InvalidTarget, $"{target.Name} has nowhere to go.");
        }
        return new Outcome(null, target, tile, landing);
    }

    /// <summary>Carries out a checked ability; returns what it did, in words, for the card's log line.</summary>
    internal static List<string> Apply(ScavengeSim sim, Unit unit, CardEffect effect, Outcome outcome)
    {
        var done = new List<string>();
        var target = outcome.Target;
        if (outcome.Landing is { } landing && target is not null)
        {
            Displace(sim, target, landing, unit);
            done.Add($"{target.Name} {(effect.Push > 0 ? "shoved" : "dragged")} to {landing}");
        }
        return done;
    }

    /// <summary>Devices and vehicles stay put.</summary>
    private static bool Fixed(Unit u) => u.Kind == UnitKind.Device;

    /// <summary>
    /// Where a unit slid <paramref name="tiles"/> steps in <paramref name="dir"/> stops: before the map edge, impassable
    /// terrain, a door that holds it, another unit, or a diagonal squeeze between two impassable tiles.
    /// </summary>
    public static Pos Slide(ScavengeSim sim, Unit mover, Pos dir, int tiles)
    {
        var state = sim.State;
        var map = state.Map;
        var at = mover.Pos;
        for (var i = 0; i < tiles; i++)
        {
            var next = at + dir;
            if (!Open(sim, mover, next)) break;
            if (dir.X != 0 && dir.Y != 0 && !Passable(sim, new Pos(next.X, at.Y)) && !Passable(sim, new Pos(at.X, next.Y))) break;
            at = next;
        }
        return at;
    }

    private static bool Passable(ScavengeSim sim, Pos p) => sim.State.Map.InBounds(p) && sim.Data.Terrain(sim.State.Map[p]).Passable;

    /// <summary>A tile the mover may be put on: passable, no door holding it, nobody else there.</summary>
    private static bool Open(ScavengeSim sim, Unit mover, Pos p) =>
        Passable(sim, p) && !Hacking.Blocks(sim.State, mover, p) && (sim.State.UnitAt(p) is not { } other || other.Id == mover.Id);

    /// <summary>
    /// Puts a unit on another tile by force: its plan fizzles (exposed to <paramref name="by"/>'s side if hostile), and it
    /// stands in the line of fire of strikes on that tile until its next sub-turn (<see cref="Unit.Displaced"/>).
    /// </summary>
    internal static void Displace(ScavengeSim sim, Unit mover, Pos to, Unit by)
    {
        var state = sim.State;
        var from = mover.Pos;
        mover.Pos = to;
        mover.Displaced = true;
        state.Log(new(state.Round, ScavengeEventType.Displaced, $"{by.Name} moves {mover.Name} from {from} to {to}.", by.Id, mover.Id, to,
            Anonymous: $"{mover.Name} is thrown from {from} to {to} by something in the fog."));
        if (mover.Plan is null) return;
        mover.Plan = null;
        state.Log(new(state.Round, ScavengeEventType.IntentFizzled, $"{mover.Name}'s plan falls through: knocked off the tile it planned from.",
            mover.Id, Pos: to));
        if (mover.HostileTo(by)) Combat.Expose(sim, mover, by.Side);
    }
}
