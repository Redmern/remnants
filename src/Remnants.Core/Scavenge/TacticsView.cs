using Remnants.Core.Help;

namespace Remnants.Core.Scavenge;

/// <summary>How a tile is threatened: by a seen enemy's readied strike, or only within reach of a seen enemy now.</summary>
public enum ThreatKind { Reach, Strike }

/// <summary>
/// A seen enemy's readied strike as a side may know it: the tile it is aimed at and the tiles it covers (with those a splash
/// would reach), never the card behind it. <see cref="AimedAtUs"/>: the blow lands on one of the side's units now.
/// </summary>
public sealed record Telegraph(int UnitId, string Name, Pos From, Pos? AttackTile, string? ActionName, bool Suppress,
    List<Pos> Threatened, bool AimedAtUs);

/// <summary>A unit of the side's on overwatch: where it stands and the tiles it watches.</summary>
public sealed record WatchView(int UnitId, string Name, Pos From, List<Pos> Tiles);

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

/// <summary>
/// One target an ability card could be aimed at (a unit, <see cref="UnitId"/>, standing on <see cref="Tile"/>, or the tile
/// itself): whether it can be now and why not, where the moved unit (or the player, for a step) lands, the units it
/// touches, and what comes of it in words (the hit chance and damage of a strike, a blow a shoved unit lands in).
/// </summary>
public sealed record CardTargetOption(Pos Tile, int? UnitId, bool Ok, string Reason, Pos? Landing, List<int> Affected, string Outcome);

/// <summary>One row of the character picker: a unit of the party, whether the card can go on it now, and why not.</summary>
public sealed record PickerRow(int UnitId, string Name, bool Ok, string Reason, string Effect);

/// <summary>
/// The plain view models the scavenge UI draws (no Godot): enemy telegraphs and the threat overlay, hit previews from a
/// tile, the hand strip and the play-card picker. They read a sim or a fog-filtered client view alike.
/// </summary>
public static class TacticsView
{
    /// <summary>Readied strikes of hostiles the side sees now, as telegraphs, showing only the tiles the side can see (none: no telegraph).</summary>
    public static List<Telegraph> Telegraphs(ScavengeSim sim, string side)
    {
        var state = sim.State;
        var ours = state.Units.Where(u => u.Side == side && u.IsActive).Select(u => u.Pos).ToHashSet();
        var visible = Visibility.SideVisible(sim.Data, state, side);
        var list = new List<Telegraph>();
        foreach (var strike in state.Strikes)
        {
            if (state.FindUnit(strike.UnitId) is not { IsActive: true } u || u.Side == side || u.Side == Unit.NeutralSide
                || !Detection.SideSees(sim.Data, state, side, u)) continue;
            var action = sim.Profile(u).Action(strike.ActionId);
            var threatened = new List<Pos>(strike.Tiles);
            var area = action?.Area ?? 0;
            for (var dy = -area; dy <= area; dy++)
                for (var dx = -area; dx <= area; dx++)
                    if (new Pos(strike.Tile.X + dx, strike.Tile.Y + dy) is var p && state.Map.InBounds(p) && !threatened.Contains(p)) threatened.Add(p);
            threatened.RemoveAll(p => !visible.Contains(state.Map.Index(p)));
            if (threatened.Count == 0) continue;
            list.Add(new Telegraph(u.Id, u.Name, u.Pos, threatened.Contains(strike.Tile) ? strike.Tile : null, action?.Name, strike.Suppress, threatened,
                threatened.Any(ours.Contains)));
        }
        return list;
    }

    /// <summary>The side's own units on overwatch and what they watch.</summary>
    public static List<WatchView> Watches(ScavengeSim sim, string side) =>
        sim.State.Units.Where(u => u.IsActive && u.Side == side && u.Watch is not null).OrderBy(u => u.Id)
            .Select(u => new WatchView(u.Id, u.Name, u.Pos, Overwatches.Watched(sim, u))).ToList();

    /// <summary>
    /// The threat overlay: every tile a seen enemy's telegraphed blow will strike (<see cref="ThreatKind.Strike"/>), and every
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
                overlay[p] = ThreatKind.Strike;
        return overlay;
    }

    /// <summary>
    /// The attack previewed from <paramref name="from"/> (default where the attacker stands): reach, hit chance, terms, as
    /// a side that knows the target's type to <paramref name="tier"/> sees them (<see cref="Bestiary.ShownChance"/>).
    /// </summary>
    public static HitPreviewInfo HitPreview(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target, Pos? from = null,
        BestiaryTier tier = BestiaryTier.Killed)
    {
        var at = from ?? attacker.Pos;
        var inReach = Combat.CheckReach(sim.Data, sim.State.Map, at, action, target.Pos) == ScavengeError.None;
        var chance = Bestiary.ShownChance(sim.Data.Rules.Bestiary, Combat.ChanceToHit(sim, attacker, action, target, from), tier);
        var breakdown = Breakdowns.For(Breakdowns.HitChance,
            new HelpContext { Scavenge = sim, Attacker = attacker, Action = action, Target = target, From = from, TargetKnown = tier });
        return new HitPreviewInfo(inReach, chance, breakdown);
    }

    /// <summary>
    /// For each tile the unit can move to this turn (and where it stands), its best hit chance at <paramref name="target"/>
    /// with the AP left after the move; tiles with no blow in reach are left out. Chances are as shown at <paramref name="tier"/>.
    /// </summary>
    public static Dictionary<Pos, double> MovePreview(ScavengeSim sim, Unit unit, Unit target, BestiaryTier tier = BestiaryTier.Killed)
    {
        var map = sim.State.Map;
        var actions = sim.Profile(unit).Actions;
        var result = new Dictionary<Pos, double>();
        foreach (var (tile, cost) in sim.Reachable(unit).Append(new KeyValuePair<Pos, int>(unit.Pos, 0)))
        {
            var best = actions.Where(a => cost + Combat.ApCost(a, tile, target.Pos) <= unit.Ap && Drones.CanStrike(a, target)
                                          && Combat.CheckReach(sim.Data, map, tile, a, target.Pos) == ScavengeError.None)
                .Select(a => Combat.ChanceToHit(sim, unit, a, target, tile)).DefaultIfEmpty(-1).Max();
            if (best >= 0) result[tile] = Bestiary.ShownChance(sim.Data.Rules.Bestiary, best, tier);
        }
        return result;
    }

    /// <summary>Seen hostiles with a readied strike: what a reaction card can answer.</summary>
    public static List<Unit> ReactionTargets(ScavengeSim sim, int playerId)
    {
        var side = sim.State.FindParty(playerId)?.Side ?? Unit.PlayerSide(playerId);
        return sim.State.Units.Where(u => u.IsActive && TileStrikes.Readied(sim.State, u) && u.Side != side && u.Side != Unit.NeutralSide
                                          && Detection.SideSees(sim.Data, sim.State, side, u)).OrderBy(u => u.Id).ToList();
    }

    /// <summary>The player's hand strip (null without a deck).</summary>
    public static HandView? Hand(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        if (SquadDecks.Of(state, playerId) is not { } deck) return null;
        var squad = Squad(sim, playerId);
        CardView View(CardDef c, int? slotOf)
        {
            // An ability (or a reaction aimed at a unit) is playable by a unit when some target works for it.
            var aimed = c.Effect.IsAbility || c.Reaction;
            var by = squad.Where(u => (slotOf is null || u.Id == slotOf)
                                      && (aimed
                                          ? Targets(sim, playerId, u.Id, c.Id, fromSlot: slotOf is not null).Any(o => o.Ok)
                                          : CardPlay.Check(sim, u, new PlayCard(playerId, u.Id, c.Id, FromSlot: slotOf is not null)).Ok))
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

    /// <summary>
    /// The targets an ability or reaction card played by <paramref name="unitId"/> could be aimed at, as the player's side
    /// knows them: every unit it sees (of the card's kind) or every tile in the card's range, the ones the rules refuse
    /// greyed out with Core's reason (<see cref="CardPlay.Check"/>). Empty for a card played on the character itself.
    /// </summary>
    public static List<CardTargetOption> Targets(ScavengeSim sim, int playerId, int unitId, string cardId, bool boost = false, bool fromSlot = false)
    {
        var state = sim.State;
        var data = sim.Data;
        if (data.Cards.Find(cardId) is not { } card || state.FindUnit(unitId) is not { } unit) return [];
        var side = unit.Side;
        var options = new List<CardTargetOption>();
        CardTargetOption Option(int? target, Pos tile)
        {
            var check = CardPlay.Check(sim, unit, new PlayCard(playerId, unit.Id, card.Id, target, boost, fromSlot, card.Target == CardTarget.Tile ? tile : null));
            if (!check.Ok) return new(tile, target, false, check.Result.Message, null, [], "");
            var (landing, affected, outcome) = Outcome(sim, unit, card, check.Effect!, check.Ability, check.Planner);
            return new(tile, target, true, "", landing, affected, outcome);
        }
        if (card.Target == CardTarget.Tile)
        {
            var visible = card.Range > 0 ? null : Visibility.SideVisible(data, state, side);
            foreach (var p in state.Map.AllPositions.Where(p => card.Range > 0 ? p.Chebyshev(unit.Pos) <= card.Range : visible!.Contains(state.Map.Index(p))))
                options.Add(Option(null, p));
        }
        else if (card.Target != CardTarget.Self || card.Reaction)
        {
            foreach (var u in state.Units.Where(u => u.IsActive && u.OnMap && u.CarriedById is null
                                                     && (u.Side == side || Detection.SideSees(data, state, side, u))
                                                     && card.Target switch
                                                     {
                                                         CardTarget.Enemy or CardTarget.Self => unit.HostileTo(u),
                                                         CardTarget.Ally => !unit.HostileTo(u) && u.Side != Unit.NeutralSide,
                                                         _ => true,
                                                     })
                         .OrderBy(u => u.Pos.Chebyshev(unit.Pos)).ThenBy(u => u.Id))
                options.Add(Option(u.Id, u.Pos));
        }
        return options;
    }

    /// <summary>What a checked play does, for the preview: the landing tile, the units touched, and the outcome in words.</summary>
    private static (Pos? Landing, List<int> Affected, string Outcome) Outcome(ScavengeSim sim, Unit unit, CardDef card, CardEffect e,
        CardAbilities.Outcome? ability, Unit? planner)
    {
        var state = sim.State;
        var target = ability?.Target ?? planner;
        var affected = new List<int>();
        var said = new List<string>();
        Pos? landing = null;
        if (target is not null) affected.Add(target.Id);
        if (e.Step > 0 && ability?.Tile is { } to)
        {
            landing = to;
            affected.Add(unit.Id);
            var near = state.Units.Where(u => u.IsActive && u.OnMap && unit.HostileTo(u) && Detection.SideSees(sim.Data, state, unit.Side, u))
                .OrderBy(u => u.Pos.Chebyshev(to)).ThenBy(u => u.Id).FirstOrDefault();
            said.Add(near is null ? $"steps to {to}" : $"steps to {to}, at {near.Name}'s {Combat.ArcOf(near, to).ToString().ToLowerInvariant()}");
        }
        if (ability?.Landing is { } land && target is not null)
        {
            landing = land;
            if (e.Swap) affected.Add(unit.Id);
            said.Add($"{target.Name} lands on {land}");
            if (Blows(sim, unit.Side).FirstOrDefault(b => b.Tiles.Contains(land) && b.UnitId != target.Id
                && state.FindUnit(b.UnitId) is { } s && (s.PlayerControlled || s.HostileTo(target))) is { Name: { } by } coming)
                // A hostile's blow hits it anyway; a player's own side's only while the shove holds (until its next turn).
                said.Add(state.FindUnit(coming.UnitId) is { } striker && striker.HostileTo(target)
                    ? $"in {by}'s blow: it will be hit"
                    : $"in {by}'s blow: hit unless it acts first");
        }
        if (target is not null && TileStrikes.Readied(state, target)
            && (e.Disrupt || landing is not null && target.Id != unit.Id && e.Step == 0))
            said.Add(target.HostileTo(unit) ? "breaks its strike, exposed" : $"{target.Name}'s strike falls through");
        if (e.Spin && target is not null) said.Add($"{target.Name} turns its back to you");
        if (ability?.Strike is { } blow && target is not null)
        {
            var chance = Combat.ChanceToHit(sim, unit, blow, target);
            said.Add($"{blow.Name}: {chance * 100:0}% to hit, {blow.MinDamage:0}-{blow.MaxDamage:0} damage, {Combat.ArcOf(target, unit.Pos).ToString().ToLowerInvariant()} arc");
        }
        if (e.GrantAp > 0 && target is not null) said.Add($"{target.Name} +{e.GrantAp} AP");
        if (e.AdvanceInitiative && target is not null) said.Add($"{target.Name} goes next");
        if (e.DelayInitiative && target is not null) said.Add($"{target.Name} goes last this round");
        if (e.PlaceBarrier > 0 && ability?.Tile is { } wall) said.Add($"cover on {wall} for {e.PlaceBarrier} rounds");
        if (e.Smoke > 0 && ability?.Tile is { } center)
        {
            var tiles = FieldEffects.SmokeTiles(state.Map, center, card.Radius);
            var inside = state.Units.Where(u => u.IsActive && u.OnMap && tiles.Contains(u.Pos)
                                                && (u.Side == unit.Side || Detection.SideSees(sim.Data, state, unit.Side, u))).OrderBy(u => u.Id).ToList();
            affected.AddRange(inside.Select(u => u.Id));
            said.Add($"smoke over {tiles.Count} tiles for {e.Smoke} rounds" + (inside.Count > 0 ? $", hiding {string.Join(", ", inside.Select(u => u.Name))}" : ""));
        }
        if (!e.IsAbility && Describe(e) is { Length: > 0 } buff) said.Add(buff);
        return (landing, affected.Distinct().ToList(), string.Join("; ", said));
    }

    /// <summary>Blows the side knows are coming: seen enemies' telegraphs and its own side's readied strikes.</summary>
    private static IEnumerable<(int UnitId, string Name, List<Pos> Tiles)> Blows(ScavengeSim sim, string side)
    {
        foreach (var t in Telegraphs(sim, side)) yield return (t.UnitId, t.Name, t.Threatened);
        foreach (var s in sim.State.Strikes)
            if (sim.State.FindUnit(s.UnitId) is { IsActive: true } u && u.Side == side) yield return (u.Id, u.Name, s.Tiles);
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
        if (e.Counter) parts.Add("strikes first when the blow comes");
        if (e.Push > 0) parts.Add($"push {e.Push}");
        if (e.Pull > 0) parts.Add($"pull {e.Pull}");
        if (e.Swap) parts.Add("swap places");
        if (e.Step > 0) parts.Add($"step {e.Step}");
        if (e.Spin) parts.Add("spin round");
        if (e.GrantAp > 0) parts.Add($"+{e.GrantAp} AP");
        if (e.AdvanceInitiative) parts.Add("goes next");
        if (e.DelayInitiative) parts.Add("goes last");
        if (e.PlaceBarrier > 0) parts.Add($"barrier {e.PlaceBarrier} rounds");
        if (e.Smoke > 0) parts.Add($"smoke {e.Smoke} rounds");
        if (e.Strike) parts.Add("strike now");
        if (e.Disrupt) parts.Add("breaks the strike");
        return string.Join(", ", parts);
    }

    private static List<Unit> Squad(ScavengeSim sim, int playerId) =>
        sim.State.Units.Where(u => u.PlayerId == playerId && u.PlayerControlled && u.IsActive).OrderBy(u => u.Id).ToList();
}
