using Remnants.Core.Help;

namespace Remnants.Core.Scavenge;

/// <summary>How a tile is threatened: by a seen enemy's telegraphed blow, or only within reach of a seen enemy now.</summary>
public enum ThreatKind { Reach, Planned }

/// <summary>
/// A seen enemy's telegraphed plan as a side may know it: where it goes, the tile it will strike (and the tiles a splash
/// would reach), never the card behind it. <see cref="AimedAtUs"/>: the blow lands on one of the side's units now.
/// </summary>
public sealed record Telegraph(int UnitId, string Name, Pos From, List<Pos> Path, Pos? AttackTile, string? ActionName, bool Suppress,
    List<Pos> Threatened, bool AimedAtUs);

/// <summary>An attack as the preview shows it: in reach from the tile, the hit chance, and its terms.</summary>
public sealed record HitPreviewInfo(bool InReach, double Chance, Breakdown? Breakdown);

/// <summary>
/// A card in the hand strip (or in a unit's slot, <see cref="SlotOf"/>): its numbers, the units it could be played on now
/// (<see cref="PlayableBy"/>), and whether it is a dead hard card nobody in the party can use.
/// </summary>
public sealed record CardView(string Id, string Name, string Text, CardCategory Category, int Cost, int ApCost, int BoostAp, bool Reaction,
    bool Consumable, bool ArmLock, string Requirement, CardKind Kind, int? SlotOf, List<int> PlayableBy, bool Dead);

/// <summary>The hand strip: the round's card points, the hand and slot cards, and how many cards are in the draw and used piles.</summary>
public sealed record HandView(int Points, int Budget, int HandSize, int DrawCount, int UsedCount, List<CardView> Cards, bool DeadDiscardUsed);

/// <summary>One row of the character picker: a unit of the party, whether the card can go on it now, and why not.</summary>
public sealed record PickerRow(int UnitId, string Name, bool Ok, string Reason, string Effect);

/// <summary>
/// The plain view models the scavenge UI draws (no Godot): enemy telegraphs and the threat overlay, hit previews from a
/// tile, the hand strip and the play-card picker. They read a sim or a fog-filtered client view alike.
/// </summary>
public static class TacticsView
{
    /// <summary>Hostiles the side sees now that have a plan, as telegraphs.</summary>
    public static List<Telegraph> Telegraphs(ScavengeSim sim, string side)
    {
        var state = sim.State;
        var ours = state.Units.Where(u => u.Side == side && u.IsActive).Select(u => u.Pos).ToHashSet();
        var list = new List<Telegraph>();
        foreach (var u in state.Units.Where(u => u.IsActive && u.Side != side && u.Side != Unit.NeutralSide && u.Plan is not null
                                               && Detection.SideSees(sim.Data, state, side, u)).OrderBy(u => u.Id))
        {
            var plan = u.Plan!;
            var action = plan.ActionId is { } id ? sim.Profile(u).Action(id) : null;
            var threatened = new List<Pos>();
            if (plan.AttackTile is { } tile)
            {
                threatened.Add(tile);
                var area = action?.Area ?? 0;
                for (var dy = -area; dy <= area; dy++)
                    for (var dx = -area; dx <= area; dx++)
                        if ((dx, dy) != (0, 0) && new Pos(tile.X + dx, tile.Y + dy) is var p && state.Map.InBounds(p)) threatened.Add(p);
            }
            list.Add(new Telegraph(u.Id, u.Name, u.Pos, new(plan.Path), plan.AttackTile, action?.Name, plan.Suppress, threatened,
                threatened.Any(ours.Contains)));
        }
        return list;
    }

    /// <summary>
    /// The threat overlay: every tile a seen enemy's telegraphed blow will strike (<see cref="ThreatKind.Planned"/>), and every
    /// other tile a seen enemy could hit from where it stands now (<see cref="ThreatKind.Reach"/>).
    /// </summary>
    public static Dictionary<Pos, ThreatKind> Threats(ScavengeSim sim, string side)
    {
        var state = sim.State;
        var overlay = new Dictionary<Pos, ThreatKind>();
        foreach (var u in state.Units.Where(u => u.IsActive && u.Side != side && u.Side != Unit.NeutralSide && Detection.SideSees(sim.Data, state, side, u)))
            foreach (var p in Combat.ThreatZone(sim, u))
                overlay.TryAdd(p, ThreatKind.Reach);
        foreach (var t in Telegraphs(sim, side))
            foreach (var p in t.Threatened)
                overlay[p] = ThreatKind.Planned;
        return overlay;
    }

    /// <summary>The attack previewed from <paramref name="from"/> (default where the attacker stands): reach, hit chance, terms.</summary>
    public static HitPreviewInfo HitPreview(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target, Pos? from = null)
    {
        var at = from ?? attacker.Pos;
        var inReach = Combat.CheckReach(sim.Data, sim.State.Map, at, action, target.Pos) == ScavengeError.None;
        var chance = Combat.ChanceToHit(sim, attacker, action, target, from);
        var breakdown = Breakdowns.For(Breakdowns.HitChance,
            new HelpContext { Scavenge = sim, Attacker = attacker, Action = action, Target = target, From = from });
        return new HitPreviewInfo(inReach, chance, breakdown);
    }

    /// <summary>
    /// For each tile the unit can move to this turn (and where it stands), its best hit chance at <paramref name="target"/>
    /// with the AP left after the move; tiles with no blow in reach are left out.
    /// </summary>
    public static Dictionary<Pos, double> MovePreview(ScavengeSim sim, Unit unit, Unit target)
    {
        var map = sim.State.Map;
        var actions = sim.Profile(unit).Actions;
        var result = new Dictionary<Pos, double>();
        foreach (var (tile, cost) in sim.Reachable(unit).Append(new KeyValuePair<Pos, int>(unit.Pos, 0)))
        {
            var best = actions.Where(a => cost + Combat.ApCost(a, tile, target.Pos) <= unit.Ap && Drones.CanStrike(a, target)
                                          && Combat.CheckReach(sim.Data, map, tile, a, target.Pos) == ScavengeError.None)
                .Select(a => Combat.ChanceToHit(sim, unit, a, target, tile)).DefaultIfEmpty(-1).Max();
            if (best >= 0) result[tile] = best;
        }
        return result;
    }

    /// <summary>Seen hostiles with a plan: what a reaction card can answer.</summary>
    public static List<Unit> ReactionTargets(ScavengeSim sim, int playerId)
    {
        var side = sim.State.FindParty(playerId)?.Side ?? Unit.PlayerSide(playerId);
        return sim.State.Units.Where(u => u.IsActive && u.Plan is not null && u.Side != side && u.Side != Unit.NeutralSide
                                          && Detection.SideSees(sim.Data, sim.State, side, u)).OrderBy(u => u.Id).ToList();
    }

    /// <summary>The player's hand strip (null without a deck).</summary>
    public static HandView? Hand(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        if (SquadDecks.Of(state, playerId) is not { } deck) return null;
        var squad = Squad(sim, playerId);
        var target = ReactionTargets(sim, playerId).FirstOrDefault()?.Id;
        CardView View(CardDef c, int? slotOf)
        {
            var by = squad.Where(u => (slotOf is null || u.Id == slotOf)
                                      && CardPlay.Check(sim, u, new PlayCard(playerId, u.Id, c.Id, c.Reaction ? target : null, FromSlot: slotOf is not null)).Ok)
                .Select(u => u.Id).ToList();
            var dead = slotOf is null && c.Requirement.Kind == CardKind.Hard && !squad.Any(c.Requirement.Met);
            return new CardView(c.Id, c.Name, c.Text, c.Category, c.Cost, c.ApCost, c.BoostAp, c.Reaction, c.Consumable, c.ArmLock,
                c.Requirement.Describe(), c.Requirement.Kind, slotOf, by, dead);
        }
        var cards = deck.Hand.Select(sim.Data.Cards.Find).OfType<CardDef>().Select(c => View(c, null)).ToList();
        foreach (var u in squad)
            cards.AddRange(u.SlotCards.Where(id => !u.SlotSpent.Contains(id)).Select(sim.Data.Cards.Find).OfType<CardDef>().Select(c => View(c, u.Id)));
        return new HandView(deck.Points, deck.Budget, deck.HandSize, deck.Hidden ? deck.DrawCount : deck.DrawPile.Count,
            deck.Hidden ? deck.UsedCount : deck.Used.Count, cards, deck.DeadDiscardRound == state.Round);
    }

    /// <summary>
    /// The character picker for a card: every conscious unit of the party, whether the card can go on it now (with this
    /// boost and reaction target, from that unit's slot if <paramref name="fromSlot"/>) and, if not, why.
    /// </summary>
    public static List<PickerRow> Picker(ScavengeSim sim, int playerId, string cardId, bool boost = false, int? target = null, bool fromSlot = false)
    {
        var rows = new List<PickerRow>();
        foreach (var u in Squad(sim, playerId))
        {
            var check = CardPlay.Check(sim, u, new PlayCard(playerId, u.Id, cardId, target, Boost: boost, FromSlot: fromSlot));
            rows.Add(new PickerRow(u.Id, u.Name, check.Ok, check.Ok ? "" : check.Result.Message, check.Effect is { } e ? Describe(e) : ""));
        }
        return rows;
    }

    /// <summary>A card effect in words: "+1 move, +10% to hit".</summary>
    public static string Describe(CardEffect e)
    {
        var parts = new List<string>();
        if (e.Move > 0) parts.Add($"+{e.Move} move");
        if (e.Hit > 0) parts.Add($"+{e.Hit:P0} to hit");
        if (e.Guard > 0) parts.Add($"next hit {e.Guard} tier{(e.Guard > 1 ? "s" : "")} lighter");
        if (e.Cover > 0) parts.Add($"+{e.Cover:P0} cover");
        if (e.Mend > 0) parts.Add($"mends {e.Mend:P0}");
        if (e.Draw > 0) parts.Add($"draw {e.Draw}");
        if (e.Counter) parts.Add("strikes first when the plan comes");
        return string.Join(", ", parts);
    }

    private static List<Unit> Squad(ScavengeSim sim, int playerId) =>
        sim.State.Units.Where(u => u.PlayerId == playerId && u.PlayerControlled && u.IsActive).OrderBy(u => u.Id).ToList();
}
