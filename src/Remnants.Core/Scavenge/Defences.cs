using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>Tiles a planned defence card holds in the action phase (<see cref="Defences"/>).</summary>
public sealed class DefendedTiles
{
    public int UnitId { get; set; }
    public string CardId { get; set; } = "";
    public List<Pos> Tiles { get; set; } = new();
    public double CardBlock { get; set; }

    /// <summary>The card has come up already: a block before the blow lands is a perfect one.</summary>
    public bool Up { get; set; }
}

/// <summary>
/// Defences: block cards and the Defend action. Planned ones are hidden until the action phase opens; from then they hold their
/// tiles, against whoever is hostile to the unit that played them, whoever stands there: a hit on such a tile does less by
/// <see cref="BlockOf"/>. Whether the defence has come up by then only decides if the block is perfect. A defender that goes
/// down or has the card broken leaves its tiles open. A hostile push on a unit breaks its Defend (<see cref="Break"/>), not its card.
/// </summary>
public static class Defences
{
    /// <summary>What marks a defence as the Defend action rather than a card: no card has this id.</summary>
    internal const string ActionId = "action:defend";

    /// <summary>What the unit guards with: the strongest guard among its gear, or what anyone can do with bare hands.</summary>
    public static GuardDef GuardOf(ScavengeSim sim, Unit unit)
    {
        var bare = sim.Data.Rules.Combat.Defend;
        return unit.Equipment.Values.Select(id => sim.Data.Items.GetValueOrDefault(id)?.Defend).OfType<GuardDef>().OrderByDescending(g => g.Block).FirstOrDefault()
               ?? new GuardDef { Block = bare.Block, Pattern = bare.Pattern };
    }

    /// <summary>The tiles a Defend covers from <paramref name="origin"/>.</summary>
    public static List<Pos> Tiles(ScavengeSim sim, Unit unit, Pos origin, Pos direction) =>
        Patterns.Tiles(sim.State.Map, GuardOf(sim, unit).Pattern, origin, direction);

    /// <summary>Queues a Defend the way the unit aims it, from where its plan ends.</summary>
    internal static ScavengeResult Plan(ScavengeSim sim, Unit unit, Defend c)
    {
        var dir = new Pos(Math.Sign(c.Direction.X), Math.Sign(c.Direction.Y));
        if (dir == default) return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{unit.Name} must defend some way.");
        if (Plans.PayAction(sim, unit, sim.Data.Rules.Combat.Defend.ApCost, "to defend", out var free) is { } refused) return refused;
        Plans.For(sim.State, unit).Steps.Add(new PlanStep { Kind = PlanStepKind.Defend, From = Plans.Anchor(sim.State, unit), Direction = dir, Free = free });
        return ScavengeResult.Success($"{unit.Name} will defend.");
    }

    /// <summary>A hostile push reached the unit: its Defend is gone for the round. A block card stands up to a push; a Defend does not.</summary>
    internal static void Break(ScavengeSim sim, Unit by, Unit victim)
    {
        var state = sim.State;
        if (!by.HostileTo(victim) || state.Defences.RemoveAll(d => d.UnitId == victim.Id && d.CardId == ActionId) == 0) return;
        Plans.Of(state, victim)?.Steps.RemoveAll(s => s.Kind == PlanStepKind.Defend);
        state.Log(new(state.Round, ScavengeEventType.GuardBroken, $"{by.Name}'s push breaks {victim.Name}'s guard.", by.Id, victim.Id, victim.Pos,
            Anonymous: $"{victim.Name}'s guard is broken by a push."));
    }

    /// <summary>The action phase opens: every planned defence of a unit still up takes its tiles.</summary>
    internal static void Arm(ScavengeSim sim)
    {
        var state = sim.State;
        foreach (var plan in state.Plans.OrderBy(p => p.UnitId))
        {
            if (state.FindUnit(plan.UnitId) is not { IsActive: true }) continue;
            var unit = state.FindUnit(plan.UnitId)!;
            foreach (var step in plan.Steps.Where(s => s.Kind == PlanStepKind.Defend))
                state.Defences.Add(new DefendedTiles
                {
                    UnitId = unit.Id,
                    CardId = ActionId,
                    CardBlock = GuardOf(sim, unit).Block,
                    // On the unit if its walk stopped short; a dash queued ahead of it was planned from its landing.
                    Tiles = Tiles(sim, unit, plan.Steps.TakeWhile(s => s != step).Any(s => s.Landing is not null) ? step.From : unit.Pos, step.Direction),
                });
            foreach (var step in plan.Steps.Where(s => s.Kind == PlanStepKind.Card))
                if (sim.Data.Cards.Find(step.CardId!) is { Pattern: { } pattern, Effect.Block: > 0 } card)
                    state.Defences.Add(new DefendedTiles
                    {
                        UnitId = plan.UnitId,
                        CardId = card.Id,
                        CardBlock = card.Effect.Block,
                        Tiles = Patterns.Tiles(state.Map, pattern, step.Tile ?? step.From, step.Direction),
                    });
        }
    }

    /// <summary>The unit's defence with this card came up.</summary>
    internal static void CameUp(ScavengeState state, Unit unit, string cardId)
    {
        if (state.Defences.Find(d => d.UnitId == unit.Id && d.CardId == cardId && !d.Up) is { } defence) defence.Up = true;
    }

    /// <summary>The unit's defence with this card is broken.</summary>
    internal static void Drop(ScavengeState state, Unit unit, string cardId)
    {
        if (state.Defences.Find(d => d.UnitId == unit.Id && d.CardId == cardId) is { } defence) state.Defences.Remove(defence);
    }

    /// <summary>All the unit's defences end.</summary>
    internal static void Drop(ScavengeState state, Unit unit) => state.Defences.RemoveAll(d => d.UnitId == unit.Id);

    /// <summary>Block share: the card's, plus the defender's block skill, its armor and perks, amplified by its adrenaline; capped.</summary>
    public static double BlockOf(ScavengeSim sim, Unit defender, double cardBlock)
    {
        var rules = sim.Data.Rules.Combat.Block;
        var share = cardBlock + defender.Get(rules.Skill) * rules.PerSkill + sim.Profile(defender).Armor * rules.PerArmor + defender.Hook(Colony.PerkHooks.BlockBonus);
        return Math.Clamp(share * (1 + Adrenaline.Bonus(sim, defender)), 0, rules.Max);
    }

    /// <summary>The strongest defence against <paramref name="attacker"/> on the tile, or null: the share it blocks, and who holds it.</summary>
    internal static (double Share, Unit Defender, bool Perfect)? At(ScavengeSim sim, Unit attacker, Pos tile)
    {
        (double Share, Unit Defender, bool Perfect)? best = null;
        foreach (var d in sim.State.Defences.Where(d => d.Tiles.Contains(tile)))
            if (sim.State.FindUnit(d.UnitId) is { IsActive: true } unit && attacker.HostileTo(unit) && BlockOf(sim, unit, d.CardBlock) is var share && share > (best?.Share ?? 0))
                best = (share, unit, d.Up);
        return best;
    }
}
