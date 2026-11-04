using Remnants.Core.Help;

namespace Remnants.Core.Scavenge;

/// <summary>How a tile is threatened: by a seen enemy's readied strike, or only within reach of a seen enemy now.</summary>
public enum ThreatKind { Reach, Strike }

/// <summary>A seen hostile's planned walk: where it stands, where it is going, and every tile on the way.</summary>
public sealed record Telegraph(int UnitId, string Name, Pos From, Pos To, List<Pos> Path);

/// <summary>Something moves in the fog around here: a whole cell of the map, not the tile (<see cref="FogRules.MoveHintCell"/>).</summary>
public sealed record MoveHint(Pos Center, int Radius);


/// <summary>A unit's plan as its own side previews it: the tiles walked, the tiles its actions cover, the allies on those, where it ends.</summary>
public sealed record PlanPreviewView(List<Pos> Path, List<Pos> ActionTiles, List<Pos> AllyTiles, Pos End);

/// <summary>Where an aimed card's pattern falls, and which of those tiles hold an ally (the warning colour).</summary>
public sealed record PatternAimView(List<Pos> Tiles, List<Pos> AllyTiles);

/// <summary>An attack as the preview shows it: in reach from the tile, the hit chance, and its terms.</summary>
public sealed record HitPreviewInfo(bool InReach, double Chance, Breakdown? Breakdown);

/// <summary>
/// A card in the hand strip (or in a unit's slot, <see cref="SlotOf"/>): its numbers, the units it could be played on now
/// (<see cref="PlayableBy"/>), and whether it is a dead hard card nobody in the party can use.
/// </summary>
/// <summary>One of a unit's plan slots: how many are filled and allowed, what is in it, and whether it can be cleared.</summary>
public sealed record PlanSlotView(PlanSlot Slot, int Used, int Limit, string Text, bool Clearable);

public sealed record CardView(string Id, string Name, string Text, CardCategory Category, int Cost, int ApCost, int BoostAp,
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
    private static IEnumerable<(Unit Unit, List<PlanStep> Moves)> HostileMoves(ScavengeSim sim, string side, bool seen)
    {
        var state = sim.State;
        foreach (var plan in state.Plans.OrderBy(p => p.UnitId))
            if (state.FindUnit(plan.UnitId) is { IsActive: true } u && u.Side != side && u.Side != Unit.NeutralSide
                && plan.Steps.Where(s => s.Kind == PlanStepKind.Move).ToList() is { Count: > 0 } moves
                && Detection.SideSees(sim.Data, state, side, u) == seen)
                yield return (u, moves);
    }

    /// <summary>What a side sees of hostile plans: only the walks of those it sees. Their actions stay hidden until they come up.</summary>
    public static List<Telegraph> Telegraphs(ScavengeSim sim, string side) =>
        HostileMoves(sim, side, seen: true).Select(m => new Telegraph(m.Unit.Id, m.Unit.Name, m.Unit.Pos, m.Moves[^1].To, m.Moves.SelectMany(s => s.Path).ToList())).ToList();

    /// <summary>
    /// What a side senses of walks it cannot see: within <see cref="FogRules.MoveHintRange"/> of one of its people, the map cell
    /// a walk ends in. Works from the sim, or from what the server sent a client (<see cref="ScavengeState.MoveHints"/>).
    /// </summary>
    public static List<MoveHint> MoveHints(ScavengeSim sim, string side)
    {
        var state = sim.State;
        var rules = sim.Data.Rules.Fog;
        var ours = state.Units.Where(u => u.Side == side && u.IsActive && u.OnMap).Select(u => u.Pos).ToList();
        var cell = Math.Max(1, rules.MoveHintCell);
        var hints = HostileMoves(sim, side, seen: false)
            .Select(m => m.Moves[^1].To)
            .Where(to => ours.Any(p => p.Chebyshev(to) <= rules.MoveHintRange))
            .Select(to => new MoveHint(new Pos(to.X / cell * cell + cell / 2, to.Y / cell * cell + cell / 2), cell / 2));
        return hints.Concat(state.MoveHints).Distinct().OrderBy(h => h.Center.Y).ThenBy(h => h.Center.X).ToList();
    }

    /// <summary>The aim of a pattern card from where the unit's plan ends: <paramref name="tile"/> for one aimed at a tile, turned to <paramref name="direction"/>.</summary>
    public static PatternAimView PatternAim(ScavengeSim sim, Unit unit, string cardId, Pos? tile, Pos direction)
    {
        var tiles = sim.Data.Cards.Find(cardId)?.Pattern is { } pattern
            ? Patterns.Tiles(sim.State.Map, pattern, tile ?? Plans.Anchor(sim.State, unit), direction) : [];
        return new PatternAimView(tiles, AllyTiles(sim, unit, tiles));
    }

    /// <summary>
    /// The unit a player is giving orders to: the one they went to, if it is theirs and can act (a plan stays open until the
    /// round resolves), else their first without a committed plan. Other players' units never stand in the way.
    /// </summary>
    public static Unit? PlanningUnit(ScavengeState state, int playerId, int? focus = null)
    {
        if (state.Finished) return null;
        bool Mine(Unit u) => u.IsActive && u.PlayerControlled && u.PlayerId == playerId;
        if (focus is { } id && state.FindUnit(id) is { } chosen && Mine(chosen)) return chosen;
        return state.Units.Find(u => Mine(u) && Plans.Of(state, u) is not { Committed: true });
    }

    /// <summary>The eight ways, clockwise: a pattern turns free of cost.</summary>
    private static readonly Pos[] Compass = [new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1)];

    /// <summary>Which way an aim at the tile points from where the unit's plan ends, turned on by <paramref name="turn"/> eighths.</summary>
    public static Pos AimDirection(ScavengeSim sim, Unit unit, Pos tile, int turn = 0)
    {
        var anchor = Plans.Anchor(sim.State, unit);
        var toward = tile != anchor ? tile - anchor : unit.Facing ?? new Pos(1, 0);
        var at = Array.IndexOf(Compass, new Pos(Math.Sign(toward.X), Math.Sign(toward.Y)));
        return Compass[(((Math.Max(at, 0) + turn) % 8) + 8) % 8];
    }

    /// <summary>A side's own units by priority, 1 first. Rivals' ranks are never shown: they are simply not here.</summary>
    public static Dictionary<int, int> Ranks(ScavengeSim sim, string side) =>
        sim.State.Units.Where(u => u.Side == side && u.IsActive && u.OnMap && Plans.Acts(sim, u))
            .OrderByDescending(u => Priority.Movement(sim, u)).ThenBy(u => Priority.TieBreak(sim.State.Round, u.Id))
            .Select((u, i) => (u.Id, Rank: i + 1)).ToDictionary(x => x.Id, x => x.Rank);

    /// <summary>The unit's Move, Action and Card slots, for its own side's eyes.</summary>
    public static List<PlanSlotView> Slots(ScavengeSim sim, Unit unit)
    {
        var (steps, used, limit) = (Plans.Of(sim.State, unit)?.Steps ?? [], Plans.Used(sim.State, unit), Plans.Limits(sim, unit));
        var walks = steps.Where(s => s.Kind == PlanStepKind.Move).ToList();
        var actions = steps.Where(s => Plans.SlotOf(s) == PlanSlot.Action).Select(s => s.Kind switch
        {
            PlanStepKind.Strike => sim.Profile(unit).Action(s.Strike!.ActionId)?.Name ?? s.Strike.ActionId,
            _ => s.Kind.ToString(),
        }).ToList();
        var cards = steps.Where(s => s.Kind == PlanStepKind.Card).Select(s => sim.Data.Cards.Find(s.CardId!)?.Name ?? s.CardId!).ToList();
        return
        [
            new(PlanSlot.Move, used.Moves, limit.Moves, walks.Count == 0 ? "Stay" : $"Walk, {walks.Sum(s => s.RefundMp)} MP", walks.Count > 0),
            new(PlanSlot.Action, used.Actions, limit.Actions, actions.Count == 0 ? "None" : string.Join(", ", actions), actions.Count > 0),
            new(PlanSlot.Card, used.Cards, limit.Cards, cards.Count > 0 ? string.Join(", ", cards) : used.Cards > 0 ? "Played" : "None", cards.Count > 0),
        ];
    }

    /// <summary>What the unit has queued, for its own side's eyes.</summary>
    public static PlanPreviewView PlanPreview(ScavengeSim sim, Unit unit)
    {
        var (path, tiles, guarded) = (new List<Pos>(), new List<Pos>(), new List<Pos>());
        var map = sim.State.Map;
        foreach (var step in Plans.Of(sim.State, unit)?.Steps ?? [])
            switch (step.Kind)
            {
                case PlanStepKind.Move:
                    path.AddRange(step.Path);
                    break;
                case PlanStepKind.Strike:
                    tiles.AddRange(step.Strike!.Tiles);
                    break;
                case PlanStepKind.Push:
                    tiles.AddRange(Patterns.Tiles(map, sim.Data.Rules.Combat.Push.Pattern, step.From, step.Direction));
                    break;
                case PlanStepKind.Dash:
                    path.AddRange(Line(step.From, step.Direction, step.Landing!.Value));
                    break;
                case PlanStepKind.Defend:
                    guarded.AddRange(Defences.Tiles(sim, unit, step.From, step.Direction));
                    break;
                case PlanStepKind.Card when step.CardId is { } id && sim.Data.Cards.Find(id) is { } card:
                    if (step.Landing is { } end)
                    {
                        path.AddRange(Line(step.From, step.Direction, end));
                        if (card.Pattern is { } after) tiles.AddRange(Patterns.Tiles(map, after, end, step.Direction));
                    }
                    else if (card.Pattern is { } pattern) tiles.AddRange(Patterns.Tiles(map, pattern, step.Tile ?? step.From, step.Direction));
                    else if (step.Tile is { } tile) tiles.Add(tile);
                    break;
            }
        var distinct = tiles.Distinct().ToList();
        return new PlanPreviewView(path, distinct.Union(guarded).ToList(), AllyTiles(sim, unit, distinct), Plans.Anchor(sim.State, unit));
    }

    private static IEnumerable<Pos> Line(Pos from, Pos direction, Pos end)
    {
        for (var at = from; at != end;)
            yield return at += direction;
    }

    /// <summary>The tiles a dash that way runs over from where the unit's plan ends, its whole reach, going by the terrain alone.</summary>
    public static List<Pos> DashAim(ScavengeSim sim, Unit unit, Pos direction)
    {
        var dir = new Pos(Math.Sign(direction.X), Math.Sign(direction.Y));
        return dir == default ? [] : Line(Plans.Anchor(sim.State, unit), dir, Pushes.PlannedLanding(sim, unit, dir, sim.Data.Rules.Combat.Dash.Tiles)).ToList();
    }

    /// <summary>The aim of a Defend from where the unit's plan ends. A guard does an ally on it no harm, so nothing warns of them.</summary>
    public static PatternAimView DefendAim(ScavengeSim sim, Unit unit, Pos direction) =>
        new(Defences.Tiles(sim, unit, Plans.Anchor(sim.State, unit), direction), []);

    /// <summary>The aim of a push from where the unit's plan ends.</summary>
    public static PatternAimView PushAim(ScavengeSim sim, Unit unit, Pos direction)
    {
        var tiles = Pushes.Tiles(sim, unit, direction);
        return new PatternAimView(tiles, AllyTiles(sim, unit, tiles));
    }

    /// <summary>The <paramref name="tiles"/> holding one of the unit's side or an ally: a blow through them strikes them too. Only units it knows of.</summary>
    public static List<Pos> AllyTiles(ScavengeSim sim, Unit unit, IEnumerable<Pos> tiles) =>
        tiles.Where(p => sim.State.UnitAt(p) is { } u && u.Id != unit.Id && !unit.HostileTo(u) && u.Side != Unit.NeutralSide).ToList();

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
        foreach (var (tile, cost) in sim.Reachable(unit).Append(new KeyValuePair<Pos, int>(Plans.Anchor(sim.State, unit), 0)))
        {
            var best = actions.Where(a => cost <= unit.Mp && Combat.ApCost(a, tile, target.Pos) <= unit.Ap && Drones.CanStrike(a, target)
                                          && Combat.CheckReach(sim.Data, map, tile, a, target.Pos) == ScavengeError.None)
                .Select(a => Combat.ChanceToHit(sim, unit, a, target, tile)).DefaultIfEmpty(-1).Max();
            if (best >= 0) result[tile] = Bestiary.ShownChance(sim.Data.Rules.Bestiary, best, tier);
        }
        return result;
    }

    /// <summary>The player's hand strip (null without a deck).</summary>
    public static HandView? Hand(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        if (SquadDecks.Of(state, playerId) is not { } deck) return null;
        var squad = Squad(sim, playerId);
        CardView View(CardDef c, int? slotOf)
        {
            // An ability is playable by a unit when some target works for it.
            var aimed = c.Effect.IsAbility;
            var by = squad.Where(u => (slotOf is null || u.Id == slotOf)
                                      && (aimed
                                          ? Targets(sim, playerId, u.Id, c.Id, fromSlot: slotOf is not null).Any(o => o.Ok)
                                          : CardPlay.Check(sim, u, new PlayCard(playerId, u.Id, c.Id, FromSlot: slotOf is not null)).Ok))
                .Select(u => u.Id).ToList();
            var dead = slotOf is null && c.Requirement.Kind == CardKind.Hard && !squad.Any(c.Requirement.Met);
            return new CardView(c.Id, c.Name, c.Text, c.Category, c.Cost, c.ApCost, c.BoostAp, c.Consumable, c.ArmLock,
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
        var from = Plans.Anchor(state, unit);
        CardTargetOption Option(int? target, Pos tile, Pos? direction = null)
        {
            var check = CardPlay.Check(sim, unit, new PlayCard(playerId, unit.Id, card.Id, target, boost, fromSlot, card.Target == CardTarget.Tile ? tile : null, direction));
            if (!check.Ok) return new(tile, target, false, check.Result.Message, null, [], "");
            var (landing, affected, outcome) = Plans.AtAnchor(state, unit, () => Outcome(sim, unit, card, check.Effect!, check.Ability));
            return new(tile, target, true, "", landing, affected, outcome);
        }
        if (card.Target == CardTarget.Tile)
        {
            var visible = card.Range > 0 ? null : Visibility.SideVisible(data, state, side);
            foreach (var p in state.Map.AllPositions.Where(p => card.Range > 0 ? p.Chebyshev(from) <= card.Range : visible!.Contains(state.Map.Index(p))))
                options.Add(Option(null, p));
        }
        // A pattern played from the unit: one option per way it can turn, on the tile that way.
        else if (card.Target == CardTarget.Self && card.Pattern is not null)
        {
            foreach (var dir in Pos.Neighbours8.Where(d => state.Map.InBounds(from + d)))
                options.Add(Option(null, from + dir, dir));
        }
        else if (card.Target != CardTarget.Self)
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
        CardAbilities.Outcome? ability)
    {
        var state = sim.State;
        var target = ability?.Target;
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
                && state.FindUnit(b.UnitId) is { } s && s.HostileTo(target)) is { Name: { } by } coming)
                said.Add($"in {by}'s blow: it will be hit");
        }
        if (e.Spin && target is not null) said.Add($"{target.Name} turns its back to you");
        if (ability?.Strike is { } blow && target is not null)
        {
            var chance = Combat.ChanceToHit(sim, unit, blow, target);
            said.Add($"{blow.Name}: {chance * 100:0}% to hit, {blow.MinDamage:0}-{blow.MaxDamage:0} damage, {Combat.ArcOf(target, unit.Pos).ToString().ToLowerInvariant()} arc");
        }
        if (e.GrantAp > 0 && target is not null) said.Add($"{target.Name} +{e.GrantAp} AP");
        if (e.AdvanceInitiative && target is not null) said.Add($"{target.Name} comes up sooner");
        if (e.DelayInitiative && target is not null) said.Add($"{target.Name} comes up later");
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

    /// <summary>Blows the side knows are coming: its own side's readied strikes.</summary>
    private static IEnumerable<(int UnitId, string Name, List<Pos> Tiles)> Blows(ScavengeSim sim, string side)
    {
        foreach (var plan in sim.State.Plans)
            if (sim.State.FindUnit(plan.UnitId) is { IsActive: true } u && u.Side == side)
                foreach (var s in plan.Steps.Where(s => s.Strike is not null)) yield return (u.Id, u.Name, s.Strike!.Tiles);
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
        if (e.Push > 0) parts.Add($"push {e.Push}");
        if (e.Pull > 0) parts.Add($"pull {e.Pull}");
        if (e.Swap) parts.Add("swap places");
        if (e.Dash > 0) parts.Add($"dashes {e.Dash}");
        if (e.PushAll > 0) parts.Add($"pushes the pattern {e.PushAll}");
        if (e.Attack) parts.Add("swings through the pattern");
        if (e.Block > 0) parts.Add($"blocks {e.Block:P0} on the pattern");
        if (e.Step > 0) parts.Add($"step {e.Step}");
        if (e.Spin) parts.Add("spin round");
        if (e.GrantAp > 0) parts.Add($"+{e.GrantAp} AP");
        if (e.ExtraMove > 0) parts.Add($"+{e.ExtraMove} move");
        if (e.ExtraAction > 0) parts.Add($"+{e.ExtraAction} action");
        if (e.FreeAction > 0) parts.Add($"+{e.FreeAction} free action");
        if (e.ExtraCard > 0) parts.Add($"+{e.ExtraCard} card");
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
