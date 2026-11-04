using Remnants.Core.Colony;

namespace Remnants.Core.Scavenge;

/// <summary>What a queued plan entry is: a move (telegraphed) or one of the hidden actions.</summary>
public enum PlanStepKind { Move, Strike, Card, Recover, Push, Dash }

/// <summary>What a unit fills in for a round: its walk, its action, its card.</summary>
public enum PlanSlot { Move, Action, Card }

/// <summary>A count for each <see cref="PlanSlot"/>: how many a unit may plan, or has.</summary>
public sealed record PlanSlots(int Moves, int Actions, int Cards);

/// <summary>
/// One entry of a unit's plan: a move leg, or an action resolved in the action phase. What it paid (MP, AP, stamina, card
/// points) is kept so taking it back refunds exactly that.
/// </summary>
public sealed class PlanStep
{
    public PlanStepKind Kind { get; set; }

    /// <summary>A move: where it ends, the tiles walked (the landing alone for a jump) and the MP it took.</summary>
    public Pos To { get; set; }
    public List<Pos> Path { get; set; } = new();

    public TileStrike? Strike { get; set; }

    /// <summary>A card: which, aimed at what, from which deck or slot.</summary>
    public string? CardId { get; set; }
    public int? TargetId { get; set; }
    public Pos? Tile { get; set; }
    public bool Boost { get; set; }
    public bool FromSlot { get; set; }
    public int DeckId { get; set; }

    /// <summary>A card's aim: where the plan had the unit when it was queued, and which way its pattern is turned.</summary>
    public Pos From { get; set; }
    public Pos Direction { get; set; }

    /// <summary>A dash's tiles, and where the plan expects it to end: what comes after it is aimed from there.</summary>
    public int Reach { get; set; }
    public Pos? Landing { get; set; }

    public int RefundMp { get; set; }
    public int RefundAp { get; set; }
    public int RefundFatigue { get; set; }
    public int RefundPoints { get; set; }

    /// <summary>
    /// A card that acted when played (a buff, more AP, a priority shift): what it did, so clearing it can undo that. It is in
    /// the plan only to hold the card slot, and leaves when the round resolves.
    /// </summary>
    public CardEffect? Applied { get; set; }
    public double AppliedAdrenaline { get; set; }
    public MendUndo? AppliedMend { get; set; }

    /// <summary>A copy with its own lists, to put back if a change of plan is refused.</summary>
    internal PlanStep Copy()
    {
        var copy = (PlanStep)MemberwiseClone();
        copy.Path = Path.ToList();
        if (Strike is { } strike) copy.Strike = new TileStrike
        {
            UnitId = strike.UnitId, ActionId = strike.ActionId, Mode = strike.Mode, From = strike.From, Tile = strike.Tile, Direction = strike.Direction, Tiles = strike.Tiles.ToList(),
        };
        return copy;
    }

    /// <summary>An action paid with a free action (<see cref="Unit.FreeActions"/>) rather than AP.</summary>
    public bool Free { get; set; }
}

/// <summary>The part a card mended, as it was before.</summary>
public sealed record MendUndo(Survivors.BodyPart Part, double Severity, Survivors.Dressing Dressing, double Penalty, List<Survivors.BodyCondition> Conditions);

/// <summary>A unit's plan for the round: its entries in the order queued, and whether its player has committed it.</summary>
public sealed class UnitPlan
{
    public int UnitId { get; set; }
    public List<PlanStep> Steps { get; set; } = new();
    public bool Committed { get; set; }
}

/// <summary>
/// The planning phase: everyone plans at once and in any order, a plan pays as it is queued and is editable until planning
/// ends; the round resolves (<see cref="RoundResolver"/>) once every player-controlled unit is committed. A unit plans at most
/// one move, one action and one card (<see cref="Limits"/>; data, perks and cards raise them). Actions are aimed from where the
/// unit's planned moves end (<see cref="Anchor"/>), and moving that end brings them along.
/// </summary>
public static class Plans
{
    public static UnitPlan? Of(ScavengeState state, Unit unit) => state.Plans.Find(p => p.UnitId == unit.Id);

    internal static UnitPlan For(ScavengeState state, Unit unit)
    {
        if (Of(state, unit) is { } plan) return plan;
        plan = new UnitPlan { UnitId = unit.Id };
        state.Plans.Add(plan);
        return plan;
    }

    /// <summary>Where the unit stands once its planned moves are done, as far as planning can tell.</summary>
    public static Pos Anchor(ScavengeState state, Unit unit) =>
        Of(state, unit)?.Steps.LastOrDefault(s => s.Kind == PlanStepKind.Move || s.Landing is not null) is { } last ? last.Landing ?? last.To : unit.Pos;

    /// <summary>Where the unit's planned walking ends: the next move starts here, even with a dash queued after it.</summary>
    internal static Pos MoveEnd(ScavengeState state, Unit unit) =>
        Of(state, unit)?.Steps.LastOrDefault(s => s.Kind == PlanStepKind.Move) is { } last ? last.To : unit.Pos;

    /// <summary>The slot a plan entry fills.</summary>
    public static PlanSlot SlotOf(PlanStep step) => step.Kind switch
    {
        PlanStepKind.Move => PlanSlot.Move,
        PlanStepKind.Card => PlanSlot.Card,
        _ => PlanSlot.Action,
    };

    /// <summary>How many moves, actions and cards the unit may plan this round. A free action comes on top, used or not.</summary>
    public static PlanSlots Limits(ScavengeSim sim, Unit unit)
    {
        var turn = sim.Data.Rules.Turn;
        var freeUsed = Of(sim.State, unit)?.Steps.Count(s => s.Free) ?? 0;
        return new PlanSlots(
            turn.PlanMoves + Bonus(unit, PerkHooks.ExtraMove) + unit.ExtraMoves,
            turn.PlanActions + Bonus(unit, PerkHooks.ExtraAction) + unit.ExtraActions + unit.FreeActions + freeUsed,
            turn.PlanCards + Bonus(unit, PerkHooks.ExtraCard) + unit.ExtraCards);
    }

    private static int Bonus(Unit unit, string hook) => (int)Math.Round(unit.Hook(hook));

    /// <summary>How many the unit has planned this round. A card that acted at once counts: it cannot be taken back.</summary>
    public static PlanSlots Used(ScavengeState state, Unit unit)
    {
        var steps = Of(state, unit)?.Steps ?? [];
        return new PlanSlots(steps.Count(s => SlotOf(s) == PlanSlot.Move), steps.Count(s => SlotOf(s) == PlanSlot.Action), unit.CardsPlayed);
    }

    /// <summary>Whether the unit can plan one more of <paramref name="slot"/>.</summary>
    public static bool HasRoom(ScavengeSim sim, Unit unit, PlanSlot slot)
    {
        var (limit, used) = (Limits(sim, unit), Used(sim.State, unit));
        return slot switch
        {
            PlanSlot.Move => used.Moves < limit.Moves,
            PlanSlot.Action => used.Actions < limit.Actions,
            _ => used.Cards < limit.Cards,
        };
    }

    internal static ScavengeResult Full(Unit unit, PlanSlot slot) =>
        ScavengeResult.Fail(ScavengeError.SlotFull, $"{unit.Name} has no {slot.ToString().ToLowerInvariant()} left to plan this round.");

    /// <summary>
    /// Pays for one more action, a free one first: null when paid, else why not. Whatever the action's cost, nobody gets more
    /// actions than <see cref="Limits"/> allows.
    /// </summary>
    internal static ScavengeResult? PayAction(ScavengeSim sim, Unit unit, int cost, string what, out bool free)
    {
        free = false;
        if (!HasRoom(sim, unit, PlanSlot.Action)) return Full(unit, PlanSlot.Action);
        if (unit.FreeActions > 0 && Economy.Stamina(sim, unit) > 0)
        {
            unit.FreeActions--;
            free = true;
            return null;
        }
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP {what} (has {unit.Ap}).");
        unit.Ap -= cost;
        return null;
    }

    /// <summary>The tiles a move can go to right now: with the slot full, the ones it could change to, with the MP it would get back.</summary>
    public static Dictionary<Pos, int> MoveChoices(ScavengeSim sim, Unit unit)
    {
        var moves = Of(sim.State, unit)?.Steps.Where(s => s.Kind == PlanStepKind.Move).ToList() ?? [];
        if (moves.Count == 0 || HasRoom(sim, unit, PlanSlot.Move)) return sim.Reachable(unit);
        var real = unit.Pos;
        unit.Pos = moves.Count > 1 ? moves[^2].To : real;
        try { return Movement.Reachable(sim.State, unit, sim.Profile(unit).Movement, unit.Mp + moves[^1].RefundMp); }
        finally { unit.Pos = real; }
    }

    /// <summary>The slot an order would change rather than add to: it is full and holds something to clear. The screen sends it as a <see cref="ChangePlan"/>.</summary>
    public static PlanSlot? Replaces(ScavengeSim sim, IScavengeCommand command)
    {
        PlanSlot? slot = command switch
        {
            Move => PlanSlot.Move,
            AttackTile or Push or Dash or Recover => PlanSlot.Action,
            _ => null,
        };
        return slot is { } s && sim.State.FindUnit(command.UnitId) is { } unit && !HasRoom(sim, unit, s)
               && Of(sim.State, unit)?.Steps.Exists(x => SlotOf(x) == s) == true ? s : null;
    }

    /// <summary>The AP an action could cost now: what is left, plus what changing the last planned action would give back. A free action pays any cost.</summary>
    public static int ApToSpend(ScavengeState state, Unit unit)
    {
        var last = Of(state, unit)?.Steps.FindLast(s => SlotOf(s) == PlanSlot.Action);
        return unit.FreeActions > 0 || last is { Free: true } ? int.MaxValue : unit.Ap + (last?.RefundAp ?? 0);
    }

    /// <summary>Runs <paramref name="look"/> with the unit standing where its plan ends. Nothing about it may stick.</summary>
    public static T AtAnchor<T>(ScavengeState state, Unit unit, Func<T> look)
    {
        var real = unit.Pos;
        unit.Pos = Anchor(state, unit);
        try { return look(); }
        finally { unit.Pos = real; }
    }

    /// <summary>Whether the unit takes part in rounds: drones fly on their operator's AP and cameras only watch.</summary>
    internal static bool Acts(ScavengeSim sim, Unit unit) =>
        unit.Side != Unit.NeutralSide && unit.Kind is not (UnitKind.Drone or UnitKind.Decoy)
        && (unit.Kind != UnitKind.Device || sim.Profile(unit).Actions.Count > 0);

    /// <summary>Whether no player-controlled unit is still planning.</summary>
    public static bool AllCommitted(ScavengeState state) => state.CurrentUnitId is null;

    /// <summary>Queues a move to <paramref name="to"/> from where the walking ends, paying its MP. Moves go ahead of the actions, which it brings along.</summary>
    internal static void AddMove(ScavengeSim sim, Unit unit, Pos to, int cost)
    {
        var was = MoveEnd(sim.State, unit);
        var real = unit.Pos;
        unit.Pos = was;
        var path = Movement.Path(sim.State, unit, sim.Profile(unit).Movement, to, unit.Mp);
        unit.Pos = real;
        var steps = For(sim.State, unit).Steps;
        var at = steps.FindIndex(s => s.Kind != PlanStepKind.Move);
        steps.Insert(at < 0 ? steps.Count : at, new PlanStep { Kind = PlanStepKind.Move, To = to, Path = path });
        unit.Mp -= cost;
        Rebase(sim, unit, was);
    }

    /// <summary>
    /// The walking now ends somewhere else than <paramref name="was"/>: what is queued keeps its shape round the unit, a dash is
    /// worked out again from its new start, and what follows it moves by the same.
    /// </summary>
    private static void Rebase(ScavengeSim sim, Unit unit, Pos was)
    {
        if (Of(sim.State, unit) is { } plan) Shift(sim, unit, plan.Steps, MoveEnd(sim.State, unit) - was);
    }

    private static void Shift(ScavengeSim sim, Unit unit, IEnumerable<PlanStep> steps, Pos shift)
    {
        if (shift == default) return;
        foreach (var step in steps.Where(s => s.Kind != PlanStepKind.Move))
        {
            step.From += shift;
            if (step.Tile is { } tile) step.Tile = tile + shift;
            if (step.Strike is { } strike)
            {
                strike.From += shift;
                strike.Tile += shift;
                strike.Tiles = strike.Tiles.Select(p => p + shift).ToList();
            }
            if (step.Landing is not { } landed) continue;
            var real = unit.Pos;
            unit.Pos = step.From;
            var landing = CardAbilities.Slide(sim, unit, step.Direction, step.Reach, terrainOnly: true);
            unit.Pos = real;
            step.Landing = landing;
            shift = landing - landed;
        }
    }

    /// <summary>Takes back one slot's entries, or else the last entry of the plan (or all of them), refunding what they paid.</summary>
    internal static ScavengeResult Undo(ScavengeSim sim, Unit unit, UndoPlan c)
    {
        var plan = Of(sim.State, unit);
        var taken = plan is null ? []
            : c.Slot is { } slot ? plan.Steps.Where(s => SlotOf(s) == slot).Reverse().ToList()
            : c.All ? Enumerable.Reverse(plan.Steps).ToList()
            : plan.Steps.TakeLast(1).ToList();
        if (taken.Count == 0) return ScavengeResult.Fail(ScavengeError.NoPlan, $"{unit.Name} has nothing planned.");
        // Taking back a card can take other entries with it (Settle).
        foreach (var step in taken.Where(plan!.Steps.Contains)) TakeOff(sim, unit, plan, step);
        return ScavengeResult.Success($"{unit.Name}'s plan is changed.");
    }

    /// <summary>
    /// The order takes the place of its slot's last entry. Its handler judges it as if that entry were gone; if it refuses, steps
    /// and pools go back as they were, so a refused change costs nothing.
    /// </summary>
    internal static ScavengeResult Change(ScavengeSim sim, Unit unit, ChangePlan c)
    {
        if (c.Order is ChangePlan || c.Order.UnitId != unit.Id || c.Order.PlayerId != c.PlayerId || Replaces(sim, c.Order) is not { } slot)
            return ScavengeResult.Fail(ScavengeError.NoPlan, $"{unit.Name} has nothing there to change.");
        var plan = Of(sim.State, unit)!;
        var steps = plan.Steps.Select(s => s.Copy()).ToList();
        var (mp, ap, fatigue, free) = (unit.Mp, unit.Ap, unit.Fatigue, unit.FreeActions);
        TakeOff(sim, unit, plan, plan.Steps.FindLast(s => SlotOf(s) == slot)!);
        var result = sim.Execute(c.Order);
        if (result.Ok) return result;
        plan.Steps.Clear();
        plan.Steps.AddRange(steps);
        (unit.Mp, unit.Ap, unit.Fatigue, unit.FreeActions) = (mp, ap, fatigue, free);
        return result;
    }

    private static void TakeOff(ScavengeSim sim, Unit unit, UnitPlan plan, PlanStep step)
    {
        var was = MoveEnd(sim.State, unit);
        var index = plan.Steps.IndexOf(step);
        plan.Steps.Remove(step);
        // What came after a dash no longer starts where it landed.
        if (step.Landing is { } landed) Shift(sim, unit, plan.Steps.Skip(index), step.From - landed);
        unit.Mp += step.RefundMp;
        unit.Ap += step.RefundAp;
        unit.Fatigue -= step.RefundFatigue;
        if (step.Free) unit.FreeActions++;
        Rebase(sim, unit, was);
        if (step.Kind == PlanStepKind.Card) CardPlay.TakeBack(sim, unit, step);
    }

    /// <summary>A card that raised the unit's limits or points is gone: what no longer fits is taken back, the last first.</summary>
    internal static void Settle(ScavengeSim sim, Unit unit)
    {
        if (Of(sim.State, unit) is not { } plan) return;
        var had = plan.Steps.Count;
        void Trim(Func<PlanStep, bool> which, Func<bool> over)
        {
            while (over() && plan.Steps.FindLast(s => which(s)) is { } last) TakeOff(sim, unit, plan, last);
        }
        Trim(s => s.Kind == PlanStepKind.Move, () => unit.Mp < 0 || Used(sim.State, unit).Moves > Limits(sim, unit).Moves);
        Trim(s => s.Free, () => unit.FreeActions < 0);
        Trim(s => SlotOf(s) == PlanSlot.Action, () => unit.Ap < 0 || Used(sim.State, unit).Actions > Limits(sim, unit).Actions);
        Trim(s => s.Kind == PlanStepKind.Card, () => Used(sim.State, unit).Cards > Limits(sim, unit).Cards);
        // A plan that lost entries is open again, whoever cleared the card.
        if (plan.Steps.Count != had) plan.Committed = false;
        // What was spent on the spot (a loot, a hack) stays spent.
        unit.Mp = Math.Max(0, unit.Mp);
        unit.Ap = Math.Max(0, unit.Ap);
        unit.FreeActions = Math.Max(0, unit.FreeActions);
    }
}
