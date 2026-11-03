namespace Remnants.Core.Scavenge;

/// <summary>
/// Cards on the enemy side: authored enemies (<see cref="EnemyDef.Cards"/>) bring their cards into one enemy deck, dealt
/// and refilled with the same rules as a party's (<see cref="SquadDecks"/>), under <see cref="DeckId"/>. After an
/// authored enemy's turn (its plan declared), it plays what it can by a simple priority: a reaction against a seen
/// hostile whose plan aims at its tile (one that breaks the plan first), breaking a seen hostile's plan (a shove, a pull,
/// a disrupt), an attack card when its plan attacks, smoke on itself when hurt and seen, a defense card, and a mend or
/// a draw. Only what the card rules accept (<see cref="CardPlay.Check"/>). Units without cards never play.
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
            if (!CardPlay.Play(sim, unit, pick, DeckId).Ok) return;
        }
    }

    /// <summary>The card to play now, by priority, or null.</summary>
    private static PlayCard? Choose(ScavengeSim sim, Unit unit, SquadDeck deck)
    {
        var data = sim.Data;
        var state = sim.State;
        bool Playable(CardDef c) => c.Cost <= deck.Points && c.ApCost == 0 && (c.Requirement.Kind == CardKind.Baseline || c.Requirement.Met(unit))
                                    && !(c.ArmLock && ProfileDeriver.ArmsLocked(data, unit.Body, twoHanded: false));
        var hand = deck.Hand.Distinct().Select(data.Cards.Find).OfType<CardDef>().Where(Playable).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        PlayCard? Try(IEnumerable<CardDef> cards, IEnumerable<Unit?> targets, Pos? tile = null)
        {
            var list = targets.ToList();
            foreach (var c in cards)
                foreach (var t in list)
                    if (new PlayCard(0, unit.Id, c.Id, t?.Id, Tile: tile) is var play && CardPlay.Check(sim, unit, play, DeckId).Ok) return play;
            return null;
        }
        static bool Breaks(CardDef c) => c.Effect.Disrupt || c.Effect.Push > 0 || c.Effect.Pull > 0;
        var hostiles = state.Units.Where(u => u.IsActive && unit.HostileTo(u) && Detection.SideSees(data, state, unit.Side, u))
            .OrderBy(u => u.Pos.Chebyshev(unit.Pos)).ThenBy(u => u.Id).ToList();

        var threats = hostiles.Where(u => u.Plan is { AttackTile: { } t } && t == unit.Pos);
        if (Try(hand.Where(c => c.Reaction).OrderByDescending(Breaks), threats) is { } reaction) return reaction;
        var buffs = hand.Where(c => !c.Reaction && !c.Effect.IsAbility).ToList();
        if (Try(hand.Where(c => !c.Reaction && Breaks(c)), hostiles.Where(h => h.Plan is not null)) is { } broken) return broken;
        PlayCard Buff(CardDef c) => new(0, unit.Id, c.Id);
        var attacks = unit.Plan?.AttackTile is not null;
        if (attacks && unit.CardHit <= 0 && buffs.FirstOrDefault(c => c.Effect.Hit > 0) is { } aim) return Buff(aim);
        var seen = hostiles.Any(h => Detection.Spots(data, state, h, unit));
        if (seen && Survivors.Treatment.WorstInjury(unit.Body) >= BotTactics.SmokeInjuryAt
            && Try(hand.Where(c => !c.Reaction && c.Effect.Smoke > 0), [null], unit.Pos) is { } smoke) return smoke;
        if (unit.Guard == 0 && buffs.FirstOrDefault(c => c.Effect.Guard > 0) is { } guard) return Buff(guard);
        if (unit.CardCover <= 0 && buffs.FirstOrDefault(c => c.Effect.Cover > 0) is { } cover) return Buff(cover);
        if (Survivors.Treatment.WorstInjury(unit.Body) > 0 && buffs.FirstOrDefault(c => c.Effect.Mend > 0) is { } mend) return Buff(mend);
        if (buffs.FirstOrDefault(c => c.Effect.Draw > 0) is { } draw) return Buff(draw);
        return null;
    }
}
