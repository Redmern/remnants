namespace Remnants.Core.Scavenge;

/// <summary>
/// Cards on the enemy side: authored enemies (<see cref="EnemyDef.Cards"/>) bring their cards into one enemy deck, dealt
/// and refilled with the same rules as a party's (<see cref="SquadDecks"/>), under <see cref="DeckId"/>. After an
/// authored enemy's turn (its plan declared), it plays what it can by a simple priority: a reaction against a seen
/// hostile whose plan aims at its tile, then an attack card when its plan attacks, a defense card, and a mend or a draw.
/// Units without cards never play.
/// </summary>
public static class EnemyCards
{
    /// <summary>The deck key of the enemy side (no player has it).</summary>
    public const int DeckId = -100;

    /// <summary>The enemy deck from the cards of the authored enemies on the map; none when they bring none.</summary>
    internal static void Deal(ScavengeData data, ScavengeState state)
    {
        var authored = state.Units.Where(u => u.Kind == UnitKind.Enemy && u.EnemyDefId is { } id && data.Enemies.TryGetValue(id, out var def) && def.Cards.Count > 0)
            .OrderBy(u => u.Id).ToList();
        if (authored.Count == 0 || SquadDecks.Of(state, DeckId) is not null) return;
        var rules = data.Cards.Rules;
        var cards = authored.SelectMany(u => data.Enemies[u.EnemyDefId!].Cards).ToList();
        SquadDecks.Create(state, DeckId, new SquadLoadout(cards, rules.HandSize, rules.Draw, rules.Budget), rules);
        SquadDecks.Of(state, DeckId)!.Brought = authored.Select(u => u.Id).ToList();
    }

    internal static void Play(ScavengeSim sim, Unit unit)
    {
        if (!unit.IsActive || unit.EnemyDefId is not { } id || !sim.Data.Enemies.TryGetValue(id, out var def) || def.Cards.Count == 0) return;
        // An authored enemy that arrived later (a story trigger, reinforcements) starts the deck if there is none yet, or
        // shuffles its cards into the draw pile.
        if (SquadDecks.Of(sim.State, DeckId) is null)
        {
            var rules = sim.Data.Cards.Rules;
            SquadDecks.Create(sim.State, DeckId, new SquadLoadout(new(def.Cards), rules.HandSize, rules.Draw, rules.Budget), rules);
            sim.State.Decks[^1].Points = sim.State.Decks[^1].Budget;
            sim.State.Decks[^1].Brought.Add(unit.Id);
        }
        var deck = SquadDecks.Of(sim.State, DeckId)!;
        if (!deck.Brought.Contains(unit.Id))
        {
            deck.Brought.Add(unit.Id);
            deck.DrawPile.AddRange(def.Cards);
            SquadDecks.Shuffle(sim.State, deck.DrawPile);
        }
        for (var tries = 0; tries < deck.HandSize && deck.Points > 0; tries++)
        {
            var pick = Choose(sim, unit, deck);
            if (pick is null) return;
            if (!CardPlay.Play(sim, unit, new PlayCard(0, unit.Id, pick.Value.Card, pick.Value.Target), DeckId).Ok) return;
        }
    }

    /// <summary>The card to play now, by priority, or null.</summary>
    private static (string Card, int? Target)? Choose(ScavengeSim sim, Unit unit, SquadDeck deck)
    {
        var data = sim.Data;
        bool Playable(CardDef c) => c.Cost <= deck.Points && c.ApCost == 0 && (c.Requirement.Kind == CardKind.Baseline || c.Requirement.Met(unit))
                                    && !(c.ArmLock && ProfileDeriver.ArmsLocked(data, unit.Body, twoHanded: false));
        var hand = deck.Hand.Distinct().Select(data.Cards.Find).OfType<CardDef>().Where(Playable).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

        var threat = sim.State.Units.Where(u => u.IsActive && u.Plan is { AttackTile: { } t } && t == unit.Pos && unit.HostileTo(u)
                                                && Detection.SideSees(data, sim.State, unit.Side, u)).OrderBy(u => u.Id).FirstOrDefault();
        if (threat is not null && hand.FirstOrDefault(c => c.Reaction) is { } reaction) return (reaction.Id, threat.Id);
        var attacks = unit.Plan?.AttackTile is not null;
        if (attacks && unit.CardHit <= 0 && hand.FirstOrDefault(c => !c.Reaction && c.Effect.Hit > 0) is { } aim) return (aim.Id, null);
        if (unit.Guard == 0 && hand.FirstOrDefault(c => !c.Reaction && c.Effect.Guard > 0) is { } guard) return (guard.Id, null);
        if (unit.CardCover <= 0 && hand.FirstOrDefault(c => !c.Reaction && c.Effect.Cover > 0) is { } cover) return (cover.Id, null);
        if (Survivors.Treatment.WorstInjury(unit.Body) > 0 && hand.FirstOrDefault(c => !c.Reaction && c.Effect.Mend > 0) is { } mend) return (mend.Id, null);
        if (hand.FirstOrDefault(c => !c.Reaction && c.Effect.Draw > 0) is { } draw) return (draw.Id, null);
        return null;
    }
}
