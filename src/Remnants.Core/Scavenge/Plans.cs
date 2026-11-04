namespace Remnants.Core.Scavenge;

/// <summary>What a queued plan entry is: a move (telegraphed) or one of the hidden actions.</summary>
public enum PlanStepKind { Move, Strike, Card, Recover, Push, Dash }

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
}

/// <summary>A unit's plan for the round: its entries in the order queued, and whether its player has committed it.</summary>
public sealed class UnitPlan
{
    public int UnitId { get; set; }
    public List<PlanStep> Steps { get; set; } = new();
    public bool Committed { get; set; }
}

/// <summary>
/// The planning phase: everyone plans at once and in any order, a plan pays as it is queued and is editable until planning
/// ends; the round resolves (<see cref="RoundResolver"/>) once every player-controlled unit is committed. Actions are aimed
/// from where the unit's planned moves end (<see cref="Anchor"/>).
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

    /// <summary>Whether a dash is queued: moves all go before the actions, so none may follow it.</summary>
    internal static bool HasDash(ScavengeState state, Unit unit) => Of(state, unit)?.Steps.Any(s => s.Landing is not null) == true;

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

    /// <summary>Queues a move to <paramref name="to"/> from where the plan ends, paying its MP.</summary>
    internal static void AddMove(ScavengeSim sim, Unit unit, Pos to, int cost)
    {
        var path = AtAnchor(sim.State, unit, () => Movement.Path(sim.State, unit, sim.Profile(unit).Movement, to, unit.Mp));
        For(sim.State, unit).Steps.Add(new PlanStep { Kind = PlanStepKind.Move, To = to, Path = path });
        unit.Mp -= cost;
    }

    /// <summary>Takes back the last entry of the plan (or all of them), refunding what they paid.</summary>
    internal static ScavengeResult Undo(ScavengeSim sim, Unit unit, UndoPlan c)
    {
        var plan = Of(sim.State, unit);
        if (plan is not { Steps.Count: > 0 }) return ScavengeResult.Fail(ScavengeError.NoPlan, $"{unit.Name} has nothing planned.");
        do
        {
            var step = plan.Steps[^1];
            plan.Steps.RemoveAt(plan.Steps.Count - 1);
            unit.Mp += step.RefundMp;
            unit.Ap += step.RefundAp;
            unit.Fatigue -= step.RefundFatigue;
            if (step.Kind == PlanStepKind.Card) CardPlay.TakeBack(sim, unit, step);
        } while (c.All && plan.Steps.Count > 0);
        return ScavengeResult.Success($"{unit.Name}'s plan is changed.");
    }
}
