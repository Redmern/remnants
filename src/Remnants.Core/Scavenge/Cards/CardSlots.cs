namespace Remnants.Core.Scavenge;

/// <summary>
/// Personal card slots (cards.json <c>rules.slots</c>): one from survivor level <see cref="CardSlotRules.Level"/>, one per
/// skill rule met, what worn gear adds (<see cref="Items.ItemDef.CardSlots"/>) and the perk hook. Most characters have
/// none.
/// </summary>
public static class CardSlots
{
    public static int Count(ScavengeData data, Unit unit)
    {
        var rules = data.Cards.Rules.Slots;
        var level = rules.Level > 0 && (unit.Origin?.Level ?? 0) >= rules.Level ? 1 : 0;
        var gear = unit.Equipment.Values.Sum(id => data.Items.GetValueOrDefault(id)?.CardSlots ?? 0);
        return Math.Max(0, level + rules.Skills.Count(s => s.Met(unit)) + gear + (int)Math.Floor(unit.Hook(rules.Hook) + 1e-9));
    }
}
