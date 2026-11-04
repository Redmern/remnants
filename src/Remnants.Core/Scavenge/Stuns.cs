namespace Remnants.Core.Scavenge;

/// <summary>
/// Stun: it never touches what the unit queued this round. It costs AP, MP and priority in the next round, and is over when that
/// round ends. A block may stun the attacker (<see cref="Blocked"/>); resistance and repeats scale the chance down.
/// </summary>
public static class Stuns
{
    /// <summary>Stunned now, or about to be next round.</summary>
    public static bool Active(ScavengeState state, Unit unit) => unit.StunnedRound >= state.Round;

    /// <summary>The share of any stun chance that gets past the unit's resistance and its stuns so far this mission.</summary>
    public static double Scale(ScavengeSim sim, Unit victim) =>
        (1 - Math.Clamp(sim.Profile(victim).StunResist, 0, 1)) * Math.Pow(sim.Data.Rules.Stun.RepeatFactor, victim.StunCount);

    /// <summary>The chance a block of <paramref name="share"/> stuns the attacker: base + (block - power) * k, amplified by the defender's adrenaline and clamped, then scaled for the attacker.</summary>
    public static double Chance(ScavengeSim sim, Unit defender, Unit attacker, ActionProfile action, double share, bool perfect)
    {
        var rules = sim.Data.Rules.Stun;
        var power = (action.MinDamage + action.MaxDamage) / 2 * rules.PowerPerDamage;
        var chance = rules.Base + (share - power) * rules.PerBlock + (perfect ? rules.PerfectBonus : 0);
        return Math.Clamp(chance * (1 + Adrenaline.Bonus(sim, defender)), rules.Min, rules.Max) * Scale(sim, attacker);
    }

    /// <summary>A hit was blocked: what it costs both sides, and the roll for the stun.</summary>
    internal static void Blocked(ScavengeSim sim, Unit attacker, ActionProfile action, Unit defender, double share, bool perfect)
    {
        var rules = sim.Data.Rules.Stun;
        var chance = Chance(sim, defender, attacker, action, share, perfect);
        Economy.Drain(sim, attacker, rules.BlockedStamina);
        Economy.Drain(sim, defender, rules.DefenderStamina);
        Adrenaline.Failed(sim, attacker);
        Adrenaline.Gain(sim, defender, sim.Data.Rules.Adrenaline.GainBlock);
        if (!Try(sim, attacker, chance, defender, "the block")) return;
        Economy.Drain(sim, attacker, rules.StunnedStamina);
        Adrenaline.Gain(sim, defender, sim.Data.Rules.Adrenaline.GainStun);
    }

    /// <summary>Rolls a stun that has its chance worked out already. Structures, the calm and a unit stunned now or for next round are not stunned again.</summary>
    internal static bool Try(ScavengeSim sim, Unit victim, double chance, Unit by, string how)
    {
        var state = sim.State;
        if (!victim.IsActive || victim.Structure || victim.CalmUntilRound >= state.Round || victim.StunnedRound >= state.Round) return false;
        if (sim.Rng.NextDouble() >= chance) return false;
        victim.StunnedRound = state.Round + 1;
        victim.StunCount++;
        Adrenaline.Drain(sim, victim, sim.Data.Rules.Adrenaline.DrainStunned);
        state.Log(new(state.Round, ScavengeEventType.Stunned, $"{victim.Name} is stunned ({how}).", by.Id, victim.Id, victim.Pos,
            Anonymous: $"{victim.Name} is stunned."));
        return true;
    }

    /// <summary>The round a stun was for opens.</summary>
    internal static void RoundStarted(ScavengeSim sim, Unit unit)
    {
        if (unit.StunnedRound != sim.State.Round) return;
        var rules = sim.Data.Rules.Stun;
        unit.Ap = Math.Max(0, unit.Ap - rules.ApLoss);
        unit.Mp = Math.Max(0, unit.Mp - rules.MpLoss);
        unit.PriorityBonus -= rules.PriorityPenalty;
        sim.State.Log(new(sim.State.Round, ScavengeEventType.Stunned, $"{unit.Name} is reeling ({unit.Ap} AP, {unit.Mp} MP).", unit.Id, Pos: unit.Pos));
    }
}

/// <summary>
/// Per-unit adrenaline, 0 to <see cref="AdrenalineRules.Max"/>, in the open for everyone who sees the unit. It builds on what goes right and
/// drains each round and on what goes wrong; each tier amplifies damage, priority, block, stun chance and stamina regen. Reset per mission.
/// </summary>
public static class Adrenaline
{
    /// <summary>The tier reached: 0, or 1 up to the number of thresholds.</summary>
    public static int Tier(ScavengeSim sim, Unit unit) => sim.Data.Rules.Adrenaline.Tiers.Count(t => unit.Adrenaline >= t);

    /// <summary>The share the unit's tier adds.</summary>
    public static double Bonus(ScavengeSim sim, Unit unit) => Tier(sim, unit) is var tier and > 0 ? sim.Data.Rules.Adrenaline.TierBonus[tier - 1] : 0;

    internal static void Gain(ScavengeSim sim, Unit unit, double amount) =>
        Change(sim, unit, unit.Adrenaline + amount * (1 + unit.Hook(Colony.PerkHooks.AdrenalineGain)));

    internal static void Drain(ScavengeSim sim, Unit unit, double amount) =>
        Change(sim, unit, unit.Adrenaline - amount * Math.Max(0, 1 - unit.Hook(Colony.PerkHooks.AdrenalineDrain)));

    /// <summary>A miss, a blocked attack, a whiffed push.</summary>
    internal static void Failed(ScavengeSim sim, Unit unit) => Drain(sim, unit, sim.Data.Rules.Adrenaline.DrainFailed);

    /// <summary>A change in play, which the meter shows.</summary>
    private static void Change(ScavengeSim sim, Unit unit, double value)
    {
        var was = unit.Adrenaline;
        Set(sim, unit, value);
        if (unit.Adrenaline != was)
            sim.State.Log(new(sim.State.Round, ScavengeEventType.AdrenalineChanged, "", unit.Id, Pos: unit.Pos, Amount: unit.Adrenaline - was));
    }

    private static void Set(ScavengeSim sim, Unit unit, double value)
    {
        if (!unit.Structure) unit.Adrenaline = Math.Clamp(value, 0, sim.Data.Rules.Adrenaline.Max);
    }

    /// <summary>The mission starts: everyone from nothing, or what its perks and enemy type give it.</summary>
    internal static void Started(ScavengeSim sim)
    {
        foreach (var unit in sim.State.Units) unit.Adrenaline = 0;
        foreach (var unit in sim.State.Units) Set(sim, unit, sim.Profile(unit).AdrenalineStart);
    }

    /// <summary>A unit that turns up mid-mission starts as it would have at the start.</summary>
    internal static void Joined(ScavengeSim sim, Unit unit) => Set(sim, unit, sim.Profile(unit).AdrenalineStart);

    internal static void RoundStarted(ScavengeSim sim, Unit unit)
    {
        if (sim.State.Round > 1) Drain(sim, unit, sim.Data.Rules.Adrenaline.DrainPerRound);
    }
}
