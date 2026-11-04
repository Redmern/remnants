using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Playing a card (<see cref="PlayCard"/>) on a character of the party: from the squad hand, or from that character's
/// personal slots. It fills the card slot of the character's plan, separate from its action (<see cref="Plans.Limits"/>): it
/// costs card points from the squad's round budget and stamina for a card with an AP cost, as an action of that size would, but
/// leaves the AP for the action. Only a boost takes AP. A card that acts on the board is an action like any other (<see cref="Queued"/>): aimed from where the plan ends,
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
        if (!Plans.HasRoom(sim, unit, PlanSlot.Card)) return new PlayCheck(Plans.Full(unit, PlanSlot.Card));
        var qualified = card.Requirement.Met(unit);
        if (card.Requirement.Kind == CardKind.Hard && !qualified)
            return Fail(ScavengeError.NotQualified, $"{unit.Name} cannot use {card.Name}: needs {card.Requirement.Describe()}.");
        if (card.ArmLock && ProfileDeriver.ArmsLocked(data, unit.Body, twoHanded: false))
            return Fail(ScavengeError.ArmsLocked, $"{unit.Name}'s arms are too hurt for {card.Name}.");
        if (card.Cost > deck.Points) return Fail(ScavengeError.NotEnoughPoints, $"{card.Name} needs {card.Cost} card points ({deck.Points} left).");
        var boost = c.Boost && card.BoostAp > 0;
        var ap = boost ? card.BoostAp : 0;
        if (ap > unit.Ap) return Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {ap} AP for {card.Name} (has {unit.Ap}).");
        if (Economy.Stamina(sim, unit) <= 0) return Fail(ScavengeError.NotEnoughAp, $"{unit.Name} has no stamina left for {card.Name}.");

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
        unit.CardsPlayed++;
        Economy.Drain(sim, unit, card.ApCost * sim.Data.Rules.Turn.StaminaPerAp);
        if (c.FromSlot) unit.SlotCards.Remove(c.CardId);
        else deck.Hand.Remove(c.CardId);

        if (Queued(effect))
        {
            Plans.For(state, unit).Steps.Add(new PlanStep
            {
                Kind = PlanStepKind.Card,
                CardId = c.CardId,
                TargetId = c.TargetId,
                Tile = c.Tile,
                Boost = boost,
                FromSlot = c.FromSlot,
                DeckId = deckId ?? c.PlayerId,
                RefundPoints = card.Cost,
                From = Plans.Anchor(state, unit),
                Direction = check.Ability?.Direction ?? default,
                Reach = effect.Dash,
                Landing = effect.Dash > 0 ? Pushes.PlannedLanding(sim, unit, check.Ability!.Direction, effect.Dash) : null,
            });
            return ScavengeResult.Success($"{unit.Name} will play {card.Name}.", check.Ability?.Target?.Id);
        }
        var mended = effect.Mend > 0 && Treatment.WorstPart(unit.Body) is { } part ? Before(unit.Body, part) : null;
        var adrenaline = unit.Adrenaline;
        var text = Take(sim, unit, deck, card, effect, check.Ability, boost, c.FromSlot, deckId ?? c.PlayerId);
        // A draw has shown the cards: nothing to take back. Anything else holds the slot, and can be undone until planning ends.
        if (effect.Draw == 0)
            Plans.For(state, unit).Steps.Add(new PlanStep
            {
                Kind = PlanStepKind.Card,
                CardId = c.CardId,
                TargetId = check.Ability?.Target?.Id,
                Boost = boost,
                FromSlot = c.FromSlot,
                DeckId = deckId ?? c.PlayerId,
                RefundPoints = card.Cost,
                RefundAp = ap,
                Applied = effect,
                AppliedAdrenaline = unit.Adrenaline - adrenaline,
                AppliedMend = mended,
            });
        return ScavengeResult.Success(text, check.Ability?.Target?.Id);
    }

    private static MendUndo Before(Body body, BodyPart part)
    {
        var p = body[part];
        return new MendUndo(part, p.Severity, p.Dressing, p.ConditionPenalty, body.Conditions.Where(c => c.Part == part).ToList());
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
        if (effect.ExtraMove > 0) { unit.ExtraMoves += effect.ExtraMove; done.Add($"+{effect.ExtraMove} move"); }
        if (effect.ExtraAction > 0) { unit.ExtraActions += effect.ExtraAction; unit.Ap += effect.ExtraAction; done.Add($"+{effect.ExtraAction} action"); }
        if (effect.FreeAction > 0) { unit.FreeActions += effect.FreeAction; done.Add($"+{effect.FreeAction} free action"); }
        if (effect.ExtraCard > 0) { unit.ExtraCards += effect.ExtraCard; done.Add($"+{effect.ExtraCard} card"); }
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
        // A block holds its tiles from the start of the phase (Defences), wherever its owner has got to.
        var ability = effect.Block > 0 ? new CardAbilities.Outcome(null, null, step.Tile, null, null, step.Direction)
            : CardAbilities.Check(sim, unit, card, effect, step.TargetId, aim, step.Direction);
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
        if (step.Applied is not null) return;
        if (SquadDecks.Of(sim.State, step.DeckId) is { } deck && sim.Data.Cards.Find(step.CardId!) is { } card) Spent(unit, deck, card, step.FromSlot);
    }

    /// <summary>A card taken back: into the hand or slot it came from, its points returned, and what it did already undone.</summary>
    internal static void TakeBack(ScavengeSim sim, Unit unit, PlanStep step)
    {
        if (SquadDecks.Of(sim.State, step.DeckId) is not { } deck) return;
        deck.Points += step.RefundPoints;
        unit.CardsPlayed--;
        if (step.Applied is { } effect)
        {
            Unspent(unit, deck, step);
            Undo(sim, unit, step, effect);
        }
        if (step.FromSlot) unit.SlotCards.Add(step.CardId!);
        else deck.Hand.Add(step.CardId!);
    }

    /// <summary>Off the pile it went to when played (a reshuffle may have moved it to the draw pile).</summary>
    private static void Unspent(Unit unit, SquadDeck deck, PlanStep step)
    {
        var id = step.CardId!;
        if (step.FromSlot)
        {
            unit.SlotCards.Remove(id);
            unit.SlotSpent.Remove(id);
            deck.Consumed.Remove(id);
        }
        else if (!deck.Used.Remove(id) && !deck.Consumed.Remove(id)) deck.DrawPile.Remove(id);
    }

    /// <summary>What the card did when played comes off again; a buff used up by now is simply gone.</summary>
    private static void Undo(ScavengeSim sim, Unit unit, PlanStep step, CardEffect e)
    {
        unit.CardMove = Math.Max(0, unit.CardMove - e.Move);
        unit.Mp -= e.Move * sim.Data.Rules.Turn.MpPerRange;
        unit.CardHit = Math.Max(0, unit.CardHit - e.Hit);
        unit.Guard = Math.Max(0, unit.Guard - e.Guard);
        unit.CardCover = Math.Max(0, unit.CardCover - e.Cover);
        unit.Adrenaline = Math.Max(0, unit.Adrenaline - step.AppliedAdrenaline);
        unit.ExtraMoves -= e.ExtraMove;
        unit.ExtraActions -= e.ExtraAction;
        unit.Ap -= e.ExtraAction;
        unit.FreeActions -= e.FreeAction;
        unit.ExtraCards -= e.ExtraCard;
        if (step.AppliedMend is { } mend)
        {
            var part = unit.Body[mend.Part];
            (part.Condition, part.Severity, part.Dressing, part.ConditionPenalty) = (PartCondition.Injured, mend.Severity, mend.Dressing, mend.Penalty);
            unit.Body.Conditions.AddRange(mend.Conditions.Where(c => !unit.Body.Conditions.Contains(c)));
        }
        if (step.TargetId is { } id && sim.State.FindUnit(id) is { } target)
        {
            target.Ap -= e.GrantAp;
            target.ExtraActions -= e.GrantAp;
            target.PriorityBonus -= ((e.AdvanceInitiative ? 1 : 0) - (e.DelayInitiative ? 1 : 0)) * sim.Data.Rules.Priority.CardShift;
            if (target.Id != unit.Id) Plans.Settle(sim, target);
        }
        Plans.Settle(sim, unit);
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
