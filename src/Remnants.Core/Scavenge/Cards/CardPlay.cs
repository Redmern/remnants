using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Playing a card (<see cref="PlayCard"/>) on a character of the party: from the squad hand, or from that character's
/// personal slots. It costs card points from the squad's round budget and, for some cards, the character's AP (more for a
/// boost); it must be the character's turn unless the card is a reaction (played against a seen enemy's plan during any
/// of the player's turns). Hard cards need a qualified character, baseline cards give a qualified one their bonus; the
/// effect then scales (<see cref="CardScaling"/>). Arm-locked cards wait for working arms. A dead hard card nobody in the
/// party qualifies for may be discarded for nothing once a round (<see cref="DiscardDeadCard"/>). Card events go to the
/// player's side only: rivals see what a card does, never which card it was.
/// </summary>
public static class CardPlay
{
    /// <summary>A card play as Core would take it: the deck, the card, the effect it would have and its AP; or why not.</summary>
    public sealed record PlayCheck(ScavengeResult Result, SquadDeck? Deck = null, CardDef? Card = null, Unit? Planner = null,
        CardEffect? Effect = null, int Ap = 0, bool Boost = false, CardAbilities.Outcome? Ability = null)
    {
        public bool Ok => Result.Ok;
    }

    /// <summary>
    /// Whether <paramref name="c"/> could be played now, and what it would do, without playing it (the UI's character
    /// picker, the bots). The same checks <see cref="PlayCard"/> runs.
    /// </summary>
    public static PlayCheck Check(ScavengeSim sim, Unit unit, PlayCard c, int? deckId = null)
    {
        var state = sim.State;
        var data = sim.Data;
        PlayCheck Fail(ScavengeError e, string m) => new(ScavengeResult.Fail(e, m));
        if (SquadDecks.Of(state, deckId ?? c.PlayerId) is not { } deck) return Fail(ScavengeError.NoCard, "Your party has no cards.");
        var held = c.FromSlot ? unit.SlotCards.Contains(c.CardId) && !unit.SlotSpent.Contains(c.CardId) : deck.Hand.Contains(c.CardId);
        if (!held || data.Cards.Find(c.CardId) is not { } card)
            return Fail(ScavengeError.NoCard, c.FromSlot ? $"{unit.Name} has no '{c.CardId}' ready in a slot." : $"No '{c.CardId}' in hand.");
        if (!unit.IsActive) return Fail(ScavengeError.Incapacitated, $"{unit.Name} cannot act ({unit.Status}).");
        if (!card.Reaction && state.CurrentUnitId != unit.Id) return Fail(ScavengeError.NotYourTurn, $"It is not {unit.Name}'s turn.");
        var qualified = card.Requirement.Met(unit);
        if (card.Requirement.Kind == CardKind.Hard && !qualified)
            return Fail(ScavengeError.NotQualified, $"{unit.Name} cannot use {card.Name}: needs {card.Requirement.Describe()}.");
        if (card.ArmLock && ProfileDeriver.ArmsLocked(data, unit.Body, twoHanded: false))
            return Fail(ScavengeError.ArmsLocked, $"{unit.Name}'s arms are too hurt for {card.Name}.");
        if (card.Cost > deck.Points) return Fail(ScavengeError.NotEnoughPoints, $"{card.Name} needs {card.Cost} card points ({deck.Points} left).");
        var boost = c.Boost && card.BoostAp > 0;
        var ap = card.ApCost + (boost ? card.BoostAp : 0);
        if (ap > unit.Ap) return Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {ap} AP for {card.Name} (has {unit.Ap}).");

        Unit? planner = null;
        if (card.Reaction)
        {
            planner = c.TargetId is { } id ? state.FindUnit(id) : null;
            if (planner is not { IsActive: true } || !TileStrikes.Readied(state, planner)
                || !unit.HostileTo(planner) || !Detection.SideSees(data, state, unit.Side, planner))
                return Fail(ScavengeError.NoIntent, $"{card.Name} needs a seen enemy with a readied strike.");
        }

        var effect = card.Effect.Plus(qualified && card.Requirement.Kind == CardKind.Baseline ? card.Bonus : null).Plus(boost ? card.Boost : null);
        if (card.Scaling is { } scaling) effect = effect.Scaled(scaling.Factor(unit));
        if (effect.Mend > 0 && Treatment.WorstInjury(unit.Body) <= 0 && effect with { Mend = 0 } is { IsEmpty: true })
            return Fail(ScavengeError.NotUsable, $"{unit.Name} has no injury to mend.");
        CardAbilities.Outcome? ability = null;
        if (effect.IsAbility)
        {
            ability = CardAbilities.Check(sim, unit, card, effect, c.TargetId, c.Tile);
            if (!ability.Ok) return new PlayCheck(ability.Fail!);
        }
        return new PlayCheck(ScavengeResult.Success($"{card.Name} on {unit.Name}."), deck, card, planner, effect, ap, boost, ability);
    }

    /// <param name="deckId">The deck to play from: the player's (default), or the enemy side's for its AI (<see cref="EnemyCards.DeckId"/>).</param>
    internal static ScavengeResult Play(ScavengeSim sim, Unit unit, PlayCard c, int? deckId = null)
    {
        var state = sim.State;
        var check = Check(sim, unit, c, deckId);
        if (!check.Ok) return check.Result;
        var (deck, card, planner, effect, ap, boost) = (check.Deck!, check.Card!, check.Planner, check.Effect!, check.Ap, check.Boost);
        var target = planner ?? check.Ability?.Target;

        deck.Points -= card.Cost;
        unit.Ap -= ap;
        if (c.FromSlot)
        {
            if (card.Consumable)
            {
                unit.SlotCards.Remove(c.CardId);
                deck.Consumed.Add(c.CardId);
            }
            else unit.SlotSpent.Add(c.CardId);
        }
        else deck.Hand.Remove(c.CardId);

        var done = new List<string>();
        if (effect.Move > 0) { unit.CardMove += effect.Move; done.Add($"+{effect.Move} move"); }
        if (effect.Hit > 0) { unit.CardHit += effect.Hit; done.Add($"+{effect.Hit:P0} to hit next"); }
        if (effect.Guard > 0) { unit.Guard += effect.Guard; done.Add($"next hit {effect.Guard} tier{(effect.Guard > 1 ? "s" : "")} lighter"); }
        if (effect.Cover > 0) { unit.CardCover += effect.Cover; done.Add($"+{effect.Cover:P0} cover"); }
        if (effect.Mend > 0 && Treatment.Mend(unit.Body, effect.Mend) is { } mended) done.Add(mended);
        if (effect.Draw > 0) { SquadDecks.Draw(sim, deck, effect.Draw); done.Add($"draws {effect.Draw}"); }
        if (effect.Counter && planner is not null) { unit.CounterId = planner.Id; done.Add($"ready to strike {planner.Name} first"); }
        if (check.Ability is { } ability) done.AddRange(CardAbilities.Apply(sim, unit, effect, ability, card.Radius));
        // Onto its pile only now, so a draw card never reshuffles itself back into the hand.
        if (!c.FromSlot) (card.Consumable ? deck.Consumed : deck.Used).Add(c.CardId);

        var side = deckId is null ? state.FindParty(c.PlayerId)?.Side ?? unit.Side : unit.Side;
        var text = $"{unit.Name} plays {card.Name}{(boost ? " (boosted)" : "")}: {string.Join(", ", done)}.";
        state.Log(new(state.Round, ScavengeEventType.CardPlayed, text, unit.Id, target?.Id, unit.Pos, Side: side));
        return ScavengeResult.Success(text, target?.Id);
    }

    internal static ScavengeResult DiscardDead(ScavengeSim sim, Unit unit, DiscardDeadCard c)
    {
        var state = sim.State;
        if (SquadDecks.Of(state, c.PlayerId) is not { } deck || !deck.Hand.Contains(c.CardId) || sim.Data.Cards.Find(c.CardId) is not { } card)
            return ScavengeResult.Fail(ScavengeError.NoCard, $"No '{c.CardId}' in hand.");
        if (deck.DeadDiscardRound == state.Round) return ScavengeResult.Fail(ScavengeError.DeadCardSpent, "A dead card was discarded this round already.");
        var squad = state.Units.Where(u => u.PlayerId == c.PlayerId && u.PlayerControlled && u.IsActive);
        if (card.Requirement.Kind != CardKind.Hard || squad.Any(card.Requirement.Met))
            return ScavengeResult.Fail(ScavengeError.CardPlayable, $"Someone can still play {card.Name}.");
        deck.Hand.Remove(c.CardId);
        deck.Used.Add(c.CardId);
        deck.DeadDiscardRound = state.Round;
        state.Log(new(state.Round, ScavengeEventType.CardDiscarded, $"{card.Name} is discarded: nobody can use it.", unit.Id,
            Side: state.FindParty(c.PlayerId)?.Side ?? unit.Side));
        return ScavengeResult.Success($"Discarded {card.Name}.");
    }

    /// <summary>
    /// A readied strike comes down: everyone who played a counter against its unit strikes it first (a reaction, no AP), by id, if
    /// they still see it and reach it.
    /// </summary>
    internal static void Counters(ScavengeSim sim, Unit planner)
    {
        foreach (var u in sim.State.Units.Where(u => u.CounterId == planner.Id).OrderBy(u => u.Id).ToList())
        {
            u.CounterId = null;
            if (!planner.IsActive) return;
            if (!u.IsActive || !Detection.Spots(sim.Data, sim.State, u, planner)) continue;
            var action = CardAbilities.BestBlow(sim, u, planner);
            if (action is null) continue;
            sim.State.Log(new(sim.State.Round, ScavengeEventType.Countered, $"{u.Name} reads {planner.Name}'s move and strikes first ({action.Name})!",
                u.Id, planner.Id, planner.Pos, Anonymous: $"{planner.Name} is struck from the fog as it moves."));
            Combat.Resolve(sim, u, action, planner, reaction: true);
        }
    }
}
