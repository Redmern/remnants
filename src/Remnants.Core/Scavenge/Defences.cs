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
/// Defence cards. Planned ones are hidden until the action phase opens; from then they hold their tiles, against whoever is
/// hostile to the unit that played them, whoever stands there: a hit on such a tile does less by
/// <see cref="BlockOf"/>. Whether the card has come up by then only decides if the block is perfect. A defender that goes down
/// or has the card broken leaves its tiles open.
/// </summary>
public static class Defences
{
    /// <summary>The action phase opens: every planned defence of a unit still up takes its tiles.</summary>
    internal static void Arm(ScavengeSim sim)
    {
        var state = sim.State;
        foreach (var plan in state.Plans.OrderBy(p => p.UnitId))
        {
            if (state.FindUnit(plan.UnitId) is not { IsActive: true }) continue;
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
