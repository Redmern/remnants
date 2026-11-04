namespace Remnants.Core.Scavenge;

/// <summary>
/// What an ability card (<see cref="CardEffect.IsAbility"/>) does to the board, and whether it can: its target
/// (<see cref="CardDef.Target"/> within <see cref="CardDef.Range"/>, in sight of the player), then each effect's own
/// rules. <see cref="Check"/> works the outcome out without changing anything (the card preview, the bots, the play
/// itself); <see cref="Apply"/> carries it out. Deterministic: no dice, ties by tile and id.
/// <para>
/// Displacement (push, pull, swap) moves a unit tile by tile along the line and stops before a wall, any other
/// impassable tile, a blocked door, another unit or the map edge; it never ends on an occupied tile. A displaced unit's
/// queued actions still go off, from where it ends up (<see cref="TileStrikes.Resolve"/>).
/// </para>
/// <para>
/// Initiative cards shift a unit's resolve priority for the round (<see cref="PriorityRules.CardShift"/>).
/// </para>
/// </summary>
public static class CardAbilities
{
    /// <summary>
    /// What an ability would do, or why not (<see cref="Fail"/>): the unit it acts on and where it lands, the tile aimed at,
    /// the tile the player steps to.
    /// </summary>
    public sealed record Outcome(ScavengeResult? Fail, Unit? Target = null, Pos? Tile = null, Pos? Landing = null, ActionProfile? Strike = null)
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
            // A co-op partner's people take their own player's orders: no moving, hurrying or AP for them (hostiles are fair game).
            if (!unit.HostileTo(target) && target.Side != Unit.NeutralSide && target.PlayerId != unit.PlayerId)
                return Fail(ScavengeError.NotYourUnit, $"{target.Name} takes orders from their own player.");
            if (card.Range > 0 && unit.Pos.Chebyshev(target.Pos) > card.Range)
                return Fail(ScavengeError.OutOfRange, $"{target.Name} is out of {card.Name}'s range ({card.Range}).");
            if (target.Id != unit.Id && !Combat.Clear(data, state.Map, unit.Pos, target.Pos))
                return Fail(ScavengeError.NoLineOfSight, $"{unit.Name} has no clear line to {target.Name}.");
        }

        if (card.Target == CardTarget.Tile)
        {
            if (tile is not { } t || !state.Map.InBounds(t)) return Fail(ScavengeError.InvalidTile, $"{card.Name} needs a tile on the map.");
            if (card.Range > 0 && unit.Pos.Chebyshev(t) > card.Range)
                return Fail(ScavengeError.OutOfRange, $"{t} is out of {card.Name}'s range ({card.Range}).");
            if (!Visibility.SideVisible(data, state, unit.Side).Contains(state.Map.Index(t)))
                return Fail(ScavengeError.NotVisible, $"{unit.Name}'s side does not see {t}.");
        }

        Pos? landing = null;
        if (effect.Step > 0)
        {
            var to = tile!.Value;
            if (!Open(sim, unit, to) || to == unit.Pos) return Fail(ScavengeError.InvalidTile, $"{unit.Name} cannot stand on {to}.");
            if (unit.CarryingId is not null) return Fail(ScavengeError.CannotCarry, $"{unit.Name} cannot slip away carrying someone.");
            // Its own legs: hurt legs shorten the step as they do a move.
            var reach = ProfileDeriver.LegReach(effect.Step, unit.Body);
            if (Steps(sim, unit, to, reach) is null) return Fail(ScavengeError.Unreachable, $"{to} is more than {reach} steps away.");
        }
        if (effect.Swap)
        {
            if (target!.Id == unit.Id) return Fail(ScavengeError.InvalidTarget, $"{unit.Name} cannot swap with themselves.");
            if (Fixed(target)) return Fail(ScavengeError.InvalidTarget, $"{target.Name} cannot be moved.");
            if (unit.CarryingId is not null) return Fail(ScavengeError.CannotCarry, $"{unit.Name} cannot swap places carrying someone.");
            if (ProfileDeriver.LegReach(1, unit.Body) < 1) return Fail(ScavengeError.Unreachable, $"{unit.Name}'s legs will not carry them.");
            landing = unit.Pos;
        }
        ActionProfile? strike = null;
        if (effect.Strike)
        {
            strike = BestBlow(sim, unit, target!);
            if (strike is null) return Fail(ScavengeError.OutOfRange, $"{target!.Name} is not in reach of any of {unit.Name}'s attacks.");
        }
        // More AP never wakes a unit that spent its own.
        if (effect.GrantAp > 0 && target!.Ap <= 0) return Fail(ScavengeError.InvalidTarget, $"{target.Name} has spent their AP this round.");
        if (effect.Smoke > 0 && FieldEffects.SmokeTiles(state.Map, tile!.Value, card.Radius).Contains(unit.Pos))
            return Fail(ScavengeError.InvalidTile, $"{card.Name} is a screen: it cannot be thrown over {unit.Name}.");
        if (effect.PlaceBarrier > 0 && !FieldEffects.CanBarrier(state, tile!.Value))
            return Fail(ScavengeError.InvalidTile, $"No barrier can go up on {tile}.");
        if (effect.Push > 0 || effect.Pull > 0)
        {
            if (target!.Id == unit.Id) return Fail(ScavengeError.InvalidTarget, $"{unit.Name} cannot shove themselves.");
            if (Fixed(target)) return Fail(ScavengeError.InvalidTarget, $"{target.Name} cannot be moved.");
            var dir = effect.Push > 0 ? Combat.Face(unit.Pos, target.Pos) : Combat.Face(target.Pos, unit.Pos);
            landing = Slide(sim, target, dir, Math.Max(effect.Push, effect.Pull));
            if (landing == target.Pos) return Fail(ScavengeError.InvalidTarget, $"{target.Name} has nowhere to go.");
        }
        return new Outcome(null, target, tile, landing, strike);
    }

    /// <summary>Carries out a checked ability; returns what it did, in words, for the card's log line.</summary>
    internal static List<string> Apply(ScavengeSim sim, Unit unit, CardEffect effect, Outcome outcome, int radius)
    {
        var state = sim.State;
        var done = new List<string>();
        var target = outcome.Target;
        if (effect.Step > 0 && outcome.Tile is { } stepTo)
        {
            var from = unit.Pos;
            unit.Pos = stepTo;
            sim.Moved(unit, from);
            // It slips round to face the nearest hostile it sees (a flank: its blow then finds that enemy's side or back).
            if (state.Units.Where(u => u.IsActive && u.OnMap && unit.HostileTo(u) && Detection.SideSees(sim.Data, state, unit.Side, u))
                    .OrderBy(u => u.Pos.Chebyshev(stepTo)).ThenBy(u => u.Id).FirstOrDefault() is { } nearest)
                unit.Facing = Combat.Face(stepTo, nearest.Pos);
            state.Log(new(state.Round, ScavengeEventType.Moved, $"{unit.Name} slips from {from} to {stepTo}.", unit.Id, Pos: stepTo));
            done.Add($"steps to {stepTo}");
        }
        if (effect.Swap && target is not null)
        {
            var from = unit.Pos;
            unit.Pos = target.Pos;
            Displace(sim, target, from, unit);
            sim.Moved(unit, from);
            done.Add($"swaps places with {target.Name}");
        }
        else if (outcome.Landing is { } landing && target is not null)
        {
            Displace(sim, target, landing, unit);
            done.Add($"{target.Name} {(effect.Push > 0 ? "shoved" : "dragged")} to {landing}");
        }
        if (outcome.Tile is { } at && (effect.PlaceBarrier > 0 || effect.Smoke > 0))
        {
            var smoke = effect.Smoke > 0;
            var rounds = smoke ? effect.Smoke : effect.PlaceBarrier;
            FieldEffects.Add(state, new FieldEffect
            {
                Kind = smoke ? FieldEffectKind.Smoke : FieldEffectKind.Barrier, Tiles = smoke ? FieldEffects.SmokeTiles(state.Map, at, radius) : [at],
                UntilRound = state.Round + rounds - 1, Side = unit.Side,
            });
            var what = smoke ? $"smoke billows round {at}" : $"a barrier goes up at {at}";
            state.Log(new(state.Round, ScavengeEventType.FieldEffectPlaced, $"{unit.Name}: {what}.", unit.Id, Pos: at,
                Anonymous: $"Out of the fog, {what}."));
            done.Add(what);
        }
        if (effect.Disrupt && target is not null)
        {
            // Whether there was anything to break shows only now: a plan is hidden until it comes up.
            var next = Plans.Of(state, target)?.Steps.FirstOrDefault(s => s.Kind != PlanStepKind.Move);
            if (next is not null)
            {
                Plans.Of(state, target)!.Steps.Remove(next);
                if (next.Kind == PlanStepKind.Card) CardPlay.Lost(sim, target, next);
                state.Log(new(state.Round, ScavengeEventType.IntentFizzled, $"{unit.Name} breaks {target.Name}'s next move.", target.Id, unit.Id, target.Pos));
            }
            done.Add(next is null ? $"{target.Name} had nothing to break" : $"{target.Name}'s next action broken");
        }
        if (outcome.Strike is { } blow && target is { IsActive: true })
        {
            Combat.Resolve(sim, unit, blow, target, free: true);
            done.Add($"strikes {target.Name} ({blow.Name})");
        }
        if (effect.GrantAp > 0 && target is not null)
        {
            target.Ap += effect.GrantAp;
            done.Add($"{target.Name} +{effect.GrantAp} AP");
        }
        if ((effect.AdvanceInitiative || effect.DelayInitiative) && target is not null)
        {
            target.PriorityBonus += (effect.AdvanceInitiative ? 1 : -1) * sim.Data.Rules.Priority.CardShift;
            var when = effect.AdvanceInitiative ? "is hurried along" : "is held up";
            state.Log(new(state.Round, ScavengeEventType.InitiativeChanged, $"{target.Name} {when} ({unit.Name}).", unit.Id, target.Id, target.Pos,
                Anonymous: $"{target.Name} {when}."));
            done.Add($"{target.Name} {when}");
        }
        if (effect.Spin && target is not null)
        {
            target.Facing = Combat.Face(unit.Pos, target.Pos);
            state.Log(new(state.Round, ScavengeEventType.Displaced, $"{unit.Name} spins {target.Name} round: its back is open.", unit.Id, target.Id,
                target.Pos, Anonymous: $"{target.Name} is spun round by something in the fog."));
            done.Add($"{target.Name} turned away");
        }
        return done;
    }

    /// <summary>
    /// The walking steps (8 ways, no squeezing past corners, through allies but not hostiles or held doors) from the
    /// unit to an open tile, if at most <paramref name="max"/>; null if further or cut off. Terrain costs do not count.
    /// </summary>
    public static int? Steps(ScavengeSim sim, Unit unit, Pos to, int max)
    {
        var state = sim.State;
        var seen = new Dictionary<Pos, int> { [unit.Pos] = 0 };
        var queue = new Queue<Pos>([unit.Pos]);
        while (queue.TryDequeue(out var p))
        {
            if (p == to) return seen[p];
            if (seen[p] == max) continue;
            foreach (var d in Pos.Neighbours8)
            {
                var n = p + d;
                if (seen.ContainsKey(n) || !Passable(sim, n) || Hacking.Blocks(state, unit, n)) continue;
                if (d.X != 0 && d.Y != 0 && !Passable(sim, new Pos(n.X, p.Y)) && !Passable(sim, new Pos(p.X, n.Y))) continue;
                if (state.UnitAt(n) is { } other && other.Id != unit.Id && (other.HostileTo(unit) || n == to)) continue;
                seen[n] = seen[p] + 1;
                queue.Enqueue(n);
            }
        }
        return null;
    }

    /// <summary>
    /// Where a smoke of <paramref name="radius"/> screens <paramref name="unit"/> from <paramref name="from"/>: the centre
    /// just far enough toward it that the cloud stops short of the unit.
    /// </summary>
    public static Pos ScreenTile(Unit unit, Pos from, int radius)
    {
        var dir = Combat.Face(unit.Pos, from);
        return new Pos(unit.Pos.X + dir.X * (radius + 1), unit.Pos.Y + dir.Y * (radius + 1));
    }

    /// <summary>The unit's attack in reach of the target that does most on average (by id on a tie), or null.</summary>
    public static ActionProfile? BestBlow(ScavengeSim sim, Unit unit, Unit target) =>
        sim.Profile(unit).Actions
            .Where(a => Drones.CanStrike(a, target) && Combat.CheckReach(sim.Data, sim.State.Map, unit.Pos, a, target.Pos) == ScavengeError.None)
            .OrderByDescending(a => Combat.ChanceToHit(sim, unit, a, target) * (a.MinDamage + a.MaxDamage) / 2)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .FirstOrDefault();

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

    /// <summary>Puts a unit on another tile by force. What it has queued goes off from there.</summary>
    internal static void Displace(ScavengeSim sim, Unit mover, Pos to, Unit by)
    {
        var state = sim.State;
        var from = mover.Pos;
        mover.Pos = to;
        state.Log(new(state.Round, ScavengeEventType.Displaced, $"{by.Name} moves {mover.Name} from {from} to {to}.", by.Id, mover.Id, to,
            Anonymous: $"{mover.Name} is thrown to {to} by something in the fog."));
        sim.Moved(mover, from);
    }
}
