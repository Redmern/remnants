namespace Remnants.Core.Scavenge;

/// <summary>
/// A unit's round economy: separate MP and AP pools, both draining one stamina pool that carries between rounds.
/// Stamina is stored as <see cref="Unit.Fatigue"/> (what is spent of the profile's maximum), so a new unit starts fresh and
/// gear that changes the maximum needs no bookkeeping. At 0 stamina a unit takes no actions and has half its MP left.
/// Numbers: <see cref="TurnRules"/>; enemy types may override the pools (<see cref="EnemyDef"/>).
/// </summary>
public static class Economy
{
    /// <summary>The stamina the unit has now.</summary>
    public static int Stamina(ScavengeSim sim, Unit unit) => Math.Max(0, sim.Profile(unit).MaxStamina - unit.Fatigue);

    /// <summary>Stamina the unit gets back at a round start: a share of its maximum less what its load takes, never under the minimum.</summary>
    public static int Regen(ScavengeSim sim, Unit unit)
    {
        var turn = sim.Data.Rules.Turn;
        var p = sim.Profile(unit);
        var load = p.CarryCapacity > 0 ? Math.Min(1, p.Load / p.CarryCapacity) : 1;
        var regen = (int)Math.Round(p.MaxStamina * turn.StaminaRegen - load * turn.RegenLoadPenalty, MidpointRounding.AwayFromZero);
        return (int)Math.Ceiling(Math.Max(turn.MinStaminaRegen, regen) * (1 + Adrenaline.Bonus(sim, unit)));
    }

    /// <summary>A round starts for the unit: stamina regenerates and both pools refill (an exhausted unit gets half its MP and no AP).</summary>
    public static void RoundStarted(ScavengeSim sim, Unit unit)
    {
        unit.Fatigue = Math.Max(0, unit.Fatigue - Regen(sim, unit));
        var p = sim.Profile(unit);
        unit.Ap = p.MaxAp;
        unit.Mp = p.MaxMp;
        if (Stamina(sim, unit) > 0) return;
        unit.Ap = 0;
        unit.Mp /= 2;
    }

    /// <summary>
    /// Cuts an AI unit's pools to what its stamina pays for, since it pays for its whole plan at once: half the stamina for
    /// actions, the rest for walking. Players meet the same limit one order at a time.
    /// </summary>
    internal static void Afford(ScavengeSim sim, Unit unit)
    {
        var turn = sim.Data.Rules.Turn;
        var stamina = Stamina(sim, unit);
        if (stamina <= 0 || unit.Mp * turn.StaminaPerMp + unit.Ap * turn.StaminaPerAp <= stamina) return;
        unit.Ap = Math.Min(unit.Ap, (stamina / Math.Max(1, turn.StaminaPerAp) + 1) / 2);
        unit.Mp = Math.Min(unit.Mp, (stamina - unit.Ap * turn.StaminaPerAp) / Math.Max(1, turn.StaminaPerMp));
    }

    /// <summary>Charges stamina for spent points. The point that empties the pool also ends the unit's actions and halves its MP left.</summary>
    internal static void Pay(ScavengeSim sim, Unit unit, int mp, int ap)
    {
        if (mp <= 0 && ap <= 0) return;
        var turn = sim.Data.Rules.Turn;
        Drain(sim, unit, Math.Max(0, mp) * turn.StaminaPerMp + Math.Max(0, ap) * turn.StaminaPerAp);
    }

    /// <summary>Takes stamina off the unit, emptying it at worst (see <see cref="Pay"/>).</summary>
    internal static void Drain(ScavengeSim sim, Unit unit, int amount)
    {
        if (amount <= 0) return;
        var max = sim.Profile(unit).MaxStamina;
        var had = unit.Fatigue < max;
        unit.Fatigue = Math.Min(max, unit.Fatigue + amount);
        if (!had || unit.Fatigue < max) return;
        unit.Ap = 0;
        unit.Mp /= 2;
    }

    /// <summary>Queues the recover action: spends AP now (which costs no stamina itself) to get stamina back when it comes up.</summary>
    internal static ScavengeResult Recover(ScavengeSim sim, Unit unit)
    {
        var turn = sim.Data.Rules.Turn;
        if (Plans.PayAction(sim, unit, turn.RecoverApCost, "to recover", out var free) is { } refused) return refused;
        Plans.For(sim.State, unit).Steps.Add(new PlanStep { Kind = PlanStepKind.Recover, Free = free });
        return ScavengeResult.Success($"{unit.Name} will catch their breath.");
    }

    /// <summary>A queued recover comes up.</summary>
    internal static void Recovered(ScavengeSim sim, Unit unit)
    {
        unit.Fatigue = Math.Max(0, unit.Fatigue - (int)Math.Round(sim.Data.Rules.Turn.RecoverStamina * sim.EffectFactor));
        sim.Log(ScavengeEventType.Recovered, $"{unit.Name} catches their breath.", unit);
    }
}
