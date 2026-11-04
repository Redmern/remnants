using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Playing a card (<see cref="PlayCard"/>) on a character of the party: from the squad hand, or from that character's
/// personal slots. It costs card points from the squad's round budget and, for some cards, the character's AP (more for a
/// boost). A card that acts on the board is an action like any other (<see cref="Queued"/>): aimed from where the plan ends,
/// paid now, taken back with the plan, and it comes up in the action phase, judged afresh then; a buff, or a card that only
/// hands out AP or priority, takes effect at once. Hard cards need a qualified character, baseline cards give a qualified one their bonus; the
/// effect then scales (<see cref="CardScaling"/>). Arm-locked cards wait for working arms. A dead hard card nobody in the
/// party qualifies for may be discarded for nothing once a round (<see cref="DiscardDeadCard"/>). Card events go to the
/// player's side only: rivals see what a card does, never which card it was.
/// </summary>
public static class CardPlay
{
    /// <summary>A card play as Core would take it: the deck, the card, the effect it would have and its AP; or why not.</summary>
    public sealed record PlayCheck(ScavengeResult Result, SquadDeck? Deck = null, CardDef? Card = null,
        CardEffect? Effect = null, int Ap = 0, bool Boost = false, CardAbilities.Outcome? Ability = null)
    {
        public bool Ok => Result.Ok;
    }

    /// <summary>
    /// Whether <paramref name="c"/> could be played now, and what it would do, without playing it (the UI's character
    /// picker, the bots). The same checks <see cref="PlayCard"/> runs.
    /// </summary>
    public static PlayCheck Check(ScavengeSim sim, Unit unit, PlayCard c, int? deckId = null) =>
        Plans.AtAnchor(sim.State, unit, () => CheckHere(sim, unit, c, deckId));

    /// <summary>Whether the card's effect waits for the action phase: it moves, strikes or blocks something on the board.</summary>
    public static bool Queued(CardEffect e) => e.IsAbility && e.GrantAp == 0 && !e.AdvanceInitiative && !e.DelayInitiative;

    private static CardEffect EffectOf(CardDef card, Unit unit, bool boost)
    {
        var effect = card.Effect.Plus(card.Requirement.Met(unit) && card.Requirement.Kind == CardKind.Baseline ? card.Bonus : null).Plus(boost ? card.Boost : null);
        return card.Scaling is { } scaling ? effect.Scaled(scaling.Factor(unit)) : effect;
    }

    private static PlayCheck CheckHere(ScavengeSim sim, Unit unit, PlayCard c, int? deckId)
    {
        var state = sim.State;
        var data = sim.Data;
        PlayCheck Fail(ScavengeError e, string m) => new(ScavengeResult.Fail(e, m));
        if (SquadDecks.Of(state, deckId ?? c.PlayerId) is not { } deck) return Fail(ScavengeError.NoCard, "Your party has no cards.");
        var held = c.FromSlot ? unit.SlotCards.Contains(c.CardId) && !unit.SlotSpent.Contains(c.CardId) : deck.Hand.Contains(c.CardId);
        if (!held || data.Cards.Find(c.CardId) is not { } card)
            return Fail(ScavengeError.NoCard, c.FromSlot ? $"{unit.Name} has no '{c.CardId}' ready in a slot." : $"No '{c.CardId}' in hand.");
        if (!unit.IsActive) return Fail(ScavengeError.Incapacitated, $"{unit.Name} cannot act ({unit.Status}).");
        var qualified = card.Requirement.Met(unit);
        if (card.Requirement.Kind == CardKind.Hard && !qualified)
            return Fail(ScavengeError.NotQualified, $"{unit.Name} cannot use {card.Name}: needs {card.Requirement.Describe()}.");
        if (card.ArmLock && ProfileDeriver.ArmsLocked(data, unit.Body, twoHanded: false))
            return Fail(ScavengeError.ArmsLocked, $"{unit.Name}'s arms are too hurt for {card.Name}.");
        if (card.Cost > deck.Points) return Fail(ScavengeError.NotEnoughPoints, $"{card.Name} needs {card.Cost} card points ({deck.Points} left).");
        var boost = c.Boost && card.BoostAp > 0;
        var ap = card.ApCost + (boost ? card.BoostAp : 0);
        if (ap > unit.Ap) return Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {ap} AP for {card.Name} (has {unit.Ap}).");

        var effect = EffectOf(card, unit, boost);
        if (effect.Mend > 0 && Treatment.WorstInjury(unit.Body) <= 0 && effect with { Mend = 0 } is { IsEmpty: true })
            return Fail(ScavengeError.NotUsable, $"{unit.Name} has no injury to mend.");
        CardAbilities.Outcome? ability = null;
        if (effect.IsAbility)
        {
            ability = CardAbilities.Check(sim, unit, card, effect, c.TargetId, c.Tile, c.Direction);
            if (!ability.Ok) return new PlayCheck(ability.Fail!);
        }
        return new PlayCheck(ScavengeResult.Success($"{card.Name} on {unit.Name}."), deck, card, effect, ap, boost, ability);
    }

    /// <param name="deckId">The deck to play from: the player's (default), or the enemy side's for its AI (<see cref="EnemyCards.DeckId"/>).</param>
    internal static ScavengeResult Play(ScavengeSim sim, Unit unit, PlayCard c, int? deckId = null)
    {
        var state = sim.State;
        var check = Check(sim, unit, c, deckId);
        if (!check.Ok) return check.Result;
        var (deck, card, effect, ap, boost) = (check.Deck!, check.Card!, check.Effect!, check.Ap, check.Boost);

        deck.Points -= card.Cost;
        unit.Ap -= ap;
        if (c.FromSlot) unit.SlotCards.Remove(c.CardId);
        else deck.Hand.Remove(c.CardId);

        if (Queued(effect))
        {
            Plans.For(state, unit).Steps.Add(new PlanStep
            {
                Kind = PlanStepKind.Card, CardId = c.CardId, TargetId = c.TargetId, Tile = c.Tile, Boost = boost, FromSlot = c.FromSlot,
                DeckId = deckId ?? c.PlayerId, RefundPoints = card.Cost, From = Plans.Anchor(state, unit), Direction = check.Ability?.Direction ?? default,
                Landing = effect.Dash > 0 ? Pushes.PlannedLanding(sim, unit, check.Ability!.Direction, effect.Dash) : null,
            });
            return ScavengeResult.Success($"{unit.Name} will play {card.Name}.", check.Ability?.Target?.Id);
        }
        var text = Take(sim, unit, deck, card, effect, check.Ability, boost, c.FromSlot, deckId ?? c.PlayerId);
        return ScavengeResult.Success(text, check.Ability?.Target?.Id);
    }

    /// <summary>The card takes effect, goes to its pile and is logged to its side; returns the log text.</summary>
    private static string Take(ScavengeSim sim, Unit unit, SquadDeck deck, CardDef card, CardEffect effect, CardAbilities.Outcome? ability, bool boost, bool fromSlot, int deckId)
    {
        var state = sim.State;
        var done = new List<string>();
        if (effect.Move > 0) { unit.CardMove += effect.Move; unit.Mp += effect.Move * sim.Data.Rules.Turn.MpPerRange; done.Add($"+{effect.Move} move"); }
        if (effect.Hit > 0) { unit.CardHit += effect.Hit; done.Add($"+{effect.Hit:P0} to hit next"); }
        if (effect.Guard > 0) { unit.Guard += effect.Guard; done.Add($"next hit {effect.Guard} tier{(effect.Guard > 1 ? "s" : "")} lighter"); }
        if (effect.Cover > 0) { unit.CardCover += effect.Cover; done.Add($"+{effect.Cover:P0} cover"); }
        if (effect.Mend > 0 && Treatment.Mend(unit.Body, effect.Mend) is { } mended) done.Add(mended);
        if (effect.Adrenaline > 0) { Adrenaline.Gain(sim, unit, effect.Adrenaline); done.Add($"+{effect.Adrenaline} adrenaline"); }
        if (effect.Draw > 0) { SquadDecks.Draw(sim, deck, effect.Draw); done.Add($"draws {effect.Draw}"); }
        if (ability is not null) done.AddRange(CardAbilities.Apply(sim, unit, card, effect, ability));
        // Onto its pile only now, so a draw card never reshuffles itself back into the hand.
        Spent(unit, deck, card, fromSlot);

        var side = state.FindParty(deckId)?.Side ?? unit.Side;
        var text = $"{unit.Name} plays {card.Name}{(boost ? " (boosted)" : "")}: {string.Join(", ", done)}.";
        state.Log(new(state.Round, ScavengeEventType.CardPlayed, text, unit.Id, ability?.Target?.Id, unit.Pos, Side: side));
        return text;
    }

    private static void Spent(Unit unit, SquadDeck deck, CardDef card, bool fromSlot)
    {
        if (!fromSlot) (card.Consumable ? deck.Consumed : deck.Used).Add(card.Id);
        else if (card.Consumable) deck.Consumed.Add(card.Id);
        else { unit.SlotCards.Add(card.Id); unit.SlotSpent.Add(card.Id); }
    }

    /// <summary>A queued card comes up: judged again as things stand now, it takes effect or falls through.</summary>
    internal static void Resolve(ScavengeSim sim, Unit unit, PlanStep step)
    {
        var state = sim.State;
        if (SquadDecks.Of(state, step.DeckId) is not { } deck || sim.Data.Cards.Find(step.CardId!) is not { } card) return;
        var effect = EffectOf(card, unit, step.Boost);
        // An attack's pattern moved with the unit if it did not end where its plan said.
        var aim = effect.Attack && step.Tile is { } t ? t + (unit.Pos - step.From) : step.Tile;
        var ability = CardAbilities.Check(sim, unit, card, effect, step.TargetId, aim, step.Direction);
        if (ability.Fail is { } fail)
        {
            Spent(unit, deck, card, step.FromSlot);
            state.Log(new(state.Round, ScavengeEventType.IntentFizzled, $"{unit.Name}'s {card.Name} falls through: {fail.Message}", unit.Id, Pos: unit.Pos,
                Side: state.FindParty(step.DeckId)?.Side ?? unit.Side));
            return;
        }
        Take(sim, unit, deck, card, effect, ability, step.Boost, step.FromSlot, step.DeckId);
    }

    /// <summary>A queued card that will not come up: it is played and gone all the same.</summary>
    internal static void Lost(ScavengeSim sim, Unit unit, PlanStep step)
    {
        if (SquadDecks.Of(sim.State, step.DeckId) is { } deck && sim.Data.Cards.Find(step.CardId!) is { } card) Spent(unit, deck, card, step.FromSlot);
    }

    /// <summary>A queued card taken back: into the hand or slot it came from, its points returned.</summary>
    internal static void TakeBack(ScavengeSim sim, Unit unit, PlanStep step)
    {
        if (SquadDecks.Of(sim.State, step.DeckId) is not { } deck) return;
        deck.Points += step.RefundPoints;
        if (step.FromSlot) unit.SlotCards.Add(step.CardId!);
        else deck.Hand.Add(step.CardId!);
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
}
