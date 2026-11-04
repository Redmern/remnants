namespace Remnants.Core.Scavenge;

/// <summary>
/// Who resolves first: speed + stamina share × weight + the action's weight + traits, highest first. Below
/// <see cref="PriorityRules.CliffBelow"/> stamina the score falls steeply, so a drained unit acts late. Pure and without
/// dice: ties are handled by <see cref="RoundResolver"/>.
/// </summary>
public static class Priority
{
    /// <summary>The unit's score before what it is doing: this is what orders the movement phase.</summary>
    public static double Movement(ScavengeSim sim, Unit unit)
    {
        var rules = sim.Data.Rules.Priority;
        var p = sim.Profile(unit);
        var share = p.MaxStamina > 0 ? (double)Economy.Stamina(sim, unit) / p.MaxStamina : 0;
        var cliff = share < rules.CliffBelow ? rules.CliffPenalty * (1 - share / rules.CliffBelow) : 0;
        return p.Initiative * rules.SpeedPerInitiative + share * rules.StaminaWeight - cliff + unit.PriorityBonus;
    }

    /// <summary>The score of one queued action of the unit.</summary>
    public static double Action(ScavengeSim sim, Unit unit, PlanStep step) => Movement(sim, unit) + Weight(sim, unit, step);

    private static double Weight(ScavengeSim sim, Unit unit, PlanStep step)
    {
        var rules = sim.Data.Rules.Priority;
        switch (step.Kind)
        {
            case PlanStepKind.Recover:
                return rules.EarlyWeight;
            case PlanStepKind.Strike when sim.Profile(unit).Action(step.Strike!.ActionId) is { } action:
                if (action.ApCost >= 2 || action.Area > 0) return rules.HeavyWeight;
                return action.Pattern == Items.AttackPattern.Melee && action.Reach <= 1 && action.MaxDamage <= rules.LightMaxDamage ? rules.QuickWeight : 0;
            case PlanStepKind.Card when step.CardId is { } id && sim.Data.Cards.Find(id) is { } card:
                return card.Effect.Push > 0 || card.Effect.Pull > 0 || card.Effect.Step > 0 ? rules.QuickWeight : 0;
            default:
                return 0;
        }
    }
}
