namespace Remnants.Core.Scavenge;

/// <summary>What the resolve playback shows for an event: each kind has its own animation, sound and floating text.</summary>
public enum OutcomeKind { Hit, Whiff, Block, PerfectBlock, Stun, Push, Collision, Dash, AdrenalineGain, AdrenalineDrain }

/// <summary>An event's outcome: its kind, its floating text and the sound event it fires.</summary>
public sealed record Outcome(OutcomeKind Kind, string Text, string Sound);

/// <summary>
/// The feedback a replayed event gives (a UI view model, like <see cref="ReplayJuice"/>; nothing here is a rule): it reads the
/// event, by <see cref="Combat"/>'s wording where the type does not say, as <see cref="ReplayJuice.ImpactOf"/> does.
/// </summary>
public static class ReplayFeedback
{
    /// <summary>The sound as a unit of the side runs low on stamina.</summary>
    public const string LowStaminaSound = "scavenge.outcome.low_stamina";

    public static Outcome? Of(ScavengeEvent e) => e.Type switch
    {
        ScavengeEventType.Attacked => new(OutcomeKind.Hit, $"-{e.Amount ?? 0:0}", "scavenge.hit"),
        ScavengeEventType.Missed => new(OutcomeKind.Whiff, "Whiff", "scavenge.miss"),
        // With a target it is a move broken, not an action that came to nothing.
        ScavengeEventType.IntentFizzled when e.TargetId is null => new(OutcomeKind.Whiff, "Whiff", "scavenge.intent_fizzled"),
        ScavengeEventType.Blocked when e.Message.Contains("perfect") => new(OutcomeKind.PerfectBlock, "PERFECT BLOCK!", "scavenge.outcome.perfect_block"),
        ScavengeEventType.Blocked => new(OutcomeKind.Block, "Block", "scavenge.outcome.block"),
        ScavengeEventType.Stunned => new(OutcomeKind.Stun, "STUNNED", "scavenge.outcome.stun"),
        ScavengeEventType.Displaced => new(OutcomeKind.Push, "Shoved", "scavenge.displaced"),
        ScavengeEventType.Collided => new(OutcomeKind.Collision, "CRASH!", "scavenge.outcome.collision"),
        ScavengeEventType.Moved when e.Message.Contains(" dashes to ") => new(OutcomeKind.Dash, "Dash!", "scavenge.outcome.dash"),
        ScavengeEventType.AdrenalineChanged when e.Amount > 0 => new(OutcomeKind.AdrenalineGain, $"+{e.Amount:0} ADR", "scavenge.outcome.adrenaline_gain"),
        ScavengeEventType.AdrenalineChanged => new(OutcomeKind.AdrenalineDrain, $"-{-(e.Amount ?? 0):0} ADR", "scavenge.outcome.adrenaline_drain"),
        _ => null,
    };

    /// <summary>The unit an event's outcome shows on: its target, or the unit itself.</summary>
    public static int? VictimOf(ScavengeEvent e) => e.TargetId ?? e.UnitId;

    /// <summary>Stamina for one action or less: the unit shows it.</summary>
    public static bool LowStamina(ScavengeSim sim, Unit unit) => Economy.Stamina(sim, unit) <= sim.Data.Rules.Turn.StaminaPerAp;
}
