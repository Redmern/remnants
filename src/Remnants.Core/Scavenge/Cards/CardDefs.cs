using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

public enum CardCategory { Movement, Attack, Defense, Utility }

public enum CardRarity { Common, Uncommon, Rare }

/// <summary>
/// What an ability card is aimed at: nothing beyond the character it is played on (<see cref="Self"/>), a unit of its
/// side (<see cref="Ally"/>, itself included), a hostile (<see cref="Enemy"/>), anyone (<see cref="Unit"/>) or a
/// <see cref="Tile"/>.
/// </summary>
public enum CardTarget { Self, Ally, Enemy, Unit, Tile }

/// <summary>
/// <see cref="Hard"/>: only a character who meets the requirement may play it. <see cref="Baseline"/>: anyone may, at the
/// card's base effect; one who meets the requirement adds its <see cref="CardDef.Bonus"/>.
/// </summary>
public enum CardKind { Baseline, Hard }

/// <summary>One way to qualify: a skill or an attribute at <see cref="Min"/> or more, or a perk hook above 0.</summary>
public sealed record CardCondition
{
    public Skill? Skill { get; init; }
    public AttributeKind? Attribute { get; init; }
    public string? Hook { get; init; }
    public double Min { get; init; }

    public string Describe() =>
        Skill is { } s ? $"{s} {Min:0}" : Attribute is { } a ? $"{a} {Min:0}" : $"the {Hook?.Replace('_', ' ')} perk";

    public bool Met(Unit unit) =>
        Skill is { } s ? unit.Get(s) + 1e-9 >= Min
        : Attribute is { } a ? unit.Get(a) + 1e-9 >= Min
        : Hook is { } h && unit.Hook(h) > 0;
}

/// <summary>Who qualifies for a card: any one of <see cref="AnyOf"/> (none listed: everyone).</summary>
public sealed record CardRequirement
{
    public CardKind Kind { get; init; } = CardKind.Baseline;
    public List<CardCondition> AnyOf { get; init; } = new();

    public bool Met(Unit unit) => AnyOf.Count == 0 || AnyOf.Any(c => c.Met(unit));

    /// <summary>The requirement in words: "Firearms 40 or Perception 65" ("anyone" without conditions).</summary>
    public string Describe() => AnyOf.Count == 0 ? "anyone" : string.Join(" or ", AnyOf.Select(c => c.Describe()));
}

/// <summary>
/// How a card grows with its player: × (1 + <see cref="Per"/> · the skill, attribute or survivor level), the extra capped
/// at <see cref="Max"/>.
/// </summary>
public sealed record CardScaling
{
    public Skill? Skill { get; init; }
    public AttributeKind? Attribute { get; init; }
    public bool Level { get; init; }
    public double Per { get; init; }
    public double Max { get; init; } = 1;

    public double Factor(Unit unit)
    {
        var value = Skill is { } s ? unit.Get(s) : Attribute is { } a ? unit.Get(a) : Level ? unit.Origin?.Level ?? 1 : 0;
        return 1 + Math.Clamp(Per * value, 0, Max);
    }
}

/// <summary>
/// What a card does to the character it is played on, in the game's own terms: <see cref="Move"/> tiles more move range
/// (this turn), <see cref="Hit"/> on its next attack, <see cref="Guard"/> tiers off the next
/// hit it takes, <see cref="Cover"/> until its next turn, <see cref="Mend"/> off its worst injury (field mending),
/// <see cref="Draw"/> cards for the squad, and <see cref="Counter"/>: a reaction blow at an enemy whose strike it reads,
/// struck just before that strike comes down.
/// Abilities change the board instead (<see cref="CardAbilities"/>): <see cref="Push"/> / <see cref="Pull"/> the target
/// that many tiles along the line from / to the player, <see cref="Swap"/> places with an ally, <see cref="Step"/> the
/// player up to that many tiles to the target tile, <see cref="Spin"/> the target's back to the player,
/// <see cref="GrantAp"/>, <see cref="AdvanceInitiative"/> / <see cref="DelayInitiative"/> the target within the
/// current round, <see cref="PlaceBarrier"/> (cover on the tile for that many rounds), <see cref="Smoke"/> (sight-blocking
/// smoke for that many rounds), <see cref="Strike"/> the target now with the player's best attack, and
/// <see cref="Disrupt"/> the target's readied strike.
/// </summary>
public sealed record CardEffect
{
    public int Move { get; init; }
    public double Hit { get; init; }
    public int Guard { get; init; }
    public double Cover { get; init; }
    public double Mend { get; init; }
    public int Draw { get; init; }
    public bool Counter { get; init; }
    public int Push { get; init; }
    public int Pull { get; init; }
    public bool Swap { get; init; }
    public int Step { get; init; }
    public bool Spin { get; init; }
    public int GrantAp { get; init; }
    public bool AdvanceInitiative { get; init; }
    public bool DelayInitiative { get; init; }
    public int PlaceBarrier { get; init; }
    public int Smoke { get; init; }
    public bool Strike { get; init; }
    public bool Disrupt { get; init; }

    /// <summary>Whether it changes the board (anything beyond a buff on the character it is played on).</summary>
    public bool IsAbility => Push > 0 || Pull > 0 || Swap || Step > 0 || Spin || GrantAp > 0 || AdvanceInitiative || DelayInitiative
                             || PlaceBarrier > 0 || Smoke > 0 || Strike || Disrupt;

    /// <summary>Whether it acts on a unit other than the player (a target unit is needed).</summary>
    public bool NeedsUnit => Push > 0 || Pull > 0 || Swap || Spin || GrantAp > 0 || AdvanceInitiative || DelayInitiative || Strike || Disrupt;

    /// <summary>Whether it acts on a tile (a target tile is needed).</summary>
    public bool NeedsTile => Step > 0 || PlaceBarrier > 0 || Smoke > 0;

    public bool IsEmpty => Move == 0 && Hit == 0 && Guard == 0 && Cover == 0 && Mend == 0 && Draw == 0 && !Counter && !IsAbility;

    public CardEffect Plus(CardEffect? other) => other is null ? this : new()
    {
        Move = Move + other.Move, Hit = Hit + other.Hit, Guard = Guard + other.Guard, Cover = Cover + other.Cover,
        Mend = Mend + other.Mend, Draw = Draw + other.Draw, Counter = Counter || other.Counter,
        Push = Push + other.Push, Pull = Pull + other.Pull, Swap = Swap || other.Swap, Step = Step + other.Step, Spin = Spin || other.Spin,
        GrantAp = GrantAp + other.GrantAp, AdvanceInitiative = AdvanceInitiative || other.AdvanceInitiative,
        DelayInitiative = DelayInitiative || other.DelayInitiative, PlaceBarrier = PlaceBarrier + other.PlaceBarrier,
        Smoke = Smoke + other.Smoke, Strike = Strike || other.Strike, Disrupt = Disrupt || other.Disrupt,
    };

    /// <summary>Numbers × <paramref name="factor"/> (whole-number effects round down, never below their base).</summary>
    public CardEffect Scaled(double factor) => new()
    {
        Move = Math.Max(Move, (int)Math.Floor(Move * factor + 1e-9)), Hit = Hit * factor, Guard = Math.Max(Guard, (int)Math.Floor(Guard * factor + 1e-9)),
        Cover = Cover * factor, Mend = Mend * factor, Draw = Draw, Counter = Counter,
        // Abilities do not scale: a push is as far, a smoke as long, whoever plays it.
        Push = Push, Pull = Pull, Swap = Swap, Step = Step, Spin = Spin, GrantAp = GrantAp, AdvanceInitiative = AdvanceInitiative,
        DelayInitiative = DelayInitiative, PlaceBarrier = PlaceBarrier, Smoke = Smoke, Strike = Strike, Disrupt = Disrupt,
    };
}

/// <summary>
/// A tactical card (data/cards.json). <see cref="Cost"/> card points from the squad's round budget; <see cref="ApCost"/> AP
/// of the character it is played on (most cost none); <see cref="BoostAp"/> more AP adds <see cref="Boost"/>.
/// <see cref="Consumable"/>: gone for good once played (else it goes to the used pile). <see cref="Reaction"/>: played
/// against a seen enemy's readied strike. <see cref="ArmLock"/>: not while the character's arms are badly hurt.
/// An ability is aimed at its <see cref="Target"/> within <see cref="Range"/> tiles (0: any distance in sight); a
/// smoke covers <see cref="Radius"/> tiles around its tile.
/// </summary>
public sealed record CardDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
    public CardCategory Category { get; init; }
    public CardRarity Rarity { get; init; }
    public int Cost { get; init; } = 1;
    public int ApCost { get; init; }
    public int BoostAp { get; init; }
    public bool Consumable { get; init; }
    public bool Reaction { get; init; }
    public bool ArmLock { get; init; }
    public CardTarget Target { get; init; }
    public int Range { get; init; }
    public int Radius { get; init; }
    public CardRequirement Requirement { get; init; } = new();
    public CardScaling? Scaling { get; init; }
    public CardEffect Effect { get; init; } = new();

    /// <summary>A baseline card's extra for a character who meets the requirement.</summary>
    public CardEffect? Bonus { get; init; }

    /// <summary>What <see cref="BoostAp"/> more AP adds.</summary>
    public CardEffect? Boost { get; init; }

    /// <summary>Reference value in scrap for the trade hint (0: the rules' value for its rarity).</summary>
    public double Value { get; init; }
}

/// <summary>
/// Personal card slots a character has: one at survivor level <see cref="Level"/> (0 = none from level), one per skill
/// rule met, plus gear (<c>cardSlots</c> on items) and the perk hook <see cref="Hook"/>.
/// </summary>
public sealed record CardSlotRules
{
    public int Level { get; init; }
    public List<CardCondition> Skills { get; init; } = new();
    public string Hook { get; init; } = "card_slots";
}

public enum CardUpgradeStat { HandSize, Draw, Budget }

/// <summary>A village upgrade to the squad deck: <see cref="Amount"/> more of a stat, for resources (bought once).</summary>
public sealed record CardUpgradeDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public CardUpgradeStat Stat { get; init; }
    public int Amount { get; init; } = 1;
    public Dictionary<string, int> Cost { get; init; } = new(StringComparer.Ordinal);

    /// <summary>An upgrade that must be bought first (a chain of levels).</summary>
    public string? After { get; init; }
}

/// <summary>
/// The squad deck's numbers: hand size (refilled at each round start, optionally capped by <see cref="Draw"/>) and the
/// card-point budget refilled each round; the starter set (id → copies) new colonies get.
/// </summary>
public sealed record CardRules
{
    public int HandSize { get; init; } = 5;

    /// <summary>At most this many cards drawn at a round start; null (the default): the hand refills to its size.</summary>
    public int? Draw { get; init; }
    public int Budget { get; init; } = 3;

    /// <summary>The smallest hand a deck is dealt, whatever a loadout says.</summary>
    public int MinHandSize { get; init; } = 1;

    /// <summary>Whether a party enters with a full hand (else it draws its first round's cards like any round).</summary>
    public bool FullOpeningHand { get; init; } = true;

    /// <summary>The most cards a squad deck may hold, and copies of one card in it (the colony's deck and a client's Join alike).</summary>
    public int MaxDeck { get; init; } = 60;
    public int MaxCopies { get; init; } = 4;

    /// <summary>The most slot cards one character may bring.</summary>
    public int MaxSlotCards { get; init; } = 8;

    /// <summary>Reference value in scrap per rarity for the trade hint (a card's own <see cref="CardDef.Value"/> wins).</summary>
    public Dictionary<CardRarity, double> TradeValues { get; init; } = new() { [CardRarity.Common] = 6, [CardRarity.Uncommon] = 15, [CardRarity.Rare] = 30 };
    public Dictionary<string, int> Starter { get; init; } = new(StringComparer.Ordinal);
    public CardSlotRules Slots { get; init; } = new();
    public List<CardUpgradeDef> Upgrades { get; init; } = new();
}

public sealed record CardsDef
{
    public CardRules Rules { get; init; } = new();
    public List<CardDef> Cards { get; init; } = new();
}

/// <summary>The card definitions, indexed and checked. <see cref="Empty"/> without cards.json.</summary>
public sealed class CardCatalog
{
    /// <summary>The perk hook ids the game knows (<see cref="Colony.PerkHooks"/>): a card may only ask for one of these.</summary>
    private static readonly HashSet<string> KnownHooks = typeof(Colony.PerkHooks)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToHashSet(StringComparer.Ordinal);

    // After KnownHooks: static fields initialize in order.
    public static readonly CardCatalog Empty = new(new CardsDef());

    public CardsDef Def { get; }
    public CardRules Rules => Def.Rules;
    public IReadOnlyDictionary<string, CardDef> All { get; }

    public CardCatalog(CardsDef def)
    {
        Def = def;
        var all = new Dictionary<string, CardDef>(StringComparer.Ordinal);
        foreach (var c in def.Cards)
        {
            if (string.IsNullOrWhiteSpace(c.Id)) throw new GameDataException("cards.json: a card without an id.");
            if (!all.TryAdd(c.Id, c)) throw new GameDataException($"cards.json: card '{c.Id}' is defined twice.");
            if (c.Cost < 0 || c.ApCost < 0 || c.BoostAp < 0) throw new GameDataException($"Card '{c.Id}': costs cannot be negative.");
            if (c.Effect.IsEmpty) throw new GameDataException($"Card '{c.Id}' does nothing.");
            CheckTarget(c);
            if (c.BoostAp > 0 && c.Boost is null) throw new GameDataException($"Card '{c.Id}': a boost cost needs a boost effect.");
            if (c.Requirement.Kind == CardKind.Hard && c.Requirement.AnyOf.Count == 0)
                throw new GameDataException($"Card '{c.Id}': a hard card needs a requirement.");
            foreach (var cond in c.Requirement.AnyOf) CheckCondition(cond, $"Card '{c.Id}'");
            foreach (var e in new[] { c.Effect, c.Bonus, c.Boost }.OfType<CardEffect>())
                if (e.Move < 0 || e.Hit < 0 || e.Guard < 0 || e.Cover < 0 || e.Mend < 0 || e.Draw < 0 || e.Push < 0 || e.Pull < 0 || e.Step < 0
                    || e.GrantAp < 0 || e.PlaceBarrier < 0 || e.Smoke < 0)
                    throw new GameDataException($"Card '{c.Id}': effects cannot be negative.");
            if (c.Scaling is { } s
                && ((s.Skill is null ? 0 : 1) + (s.Attribute is null ? 0 : 1) + (s.Level ? 1 : 0) != 1 || s.Per < 0 || s.Max < 0))
                throw new GameDataException($"Card '{c.Id}': scaling needs exactly one of skill, attribute or level, and no negative numbers.");
        }
        var slots = def.Rules.Slots;
        if (slots.Level < 0) throw new GameDataException("cards.json: slots.level cannot be negative.");
        foreach (var cond in slots.Skills)
        {
            CheckCondition(cond, "cards.json slots");
            if (cond.Skill is null) throw new GameDataException("cards.json: each slots.skills rule names a skill.");
        }
        if (!KnownHooks.Contains(slots.Hook)) throw new GameDataException($"cards.json: slots.hook '{slots.Hook}' is not a perk hook.");
        foreach (var u in def.Rules.Upgrades.Where(u => u.Amount <= 0 || u.Cost.Values.Any(v => v < 0)))
            throw new GameDataException($"cards.json: upgrade '{u.Id}' needs a positive amount and no negative cost.");
        foreach (var id in def.Rules.Starter.Keys.Where(id => !all.ContainsKey(id)))
            throw new GameDataException($"cards.json: starter card '{id}' is not a card.");
        var upgrades = def.Rules.Upgrades.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var u in def.Rules.Upgrades.Where(u => u.After is { } a && !upgrades.Contains(a)))
            throw new GameDataException($"cards.json: upgrade '{u.Id}' follows unknown upgrade '{u.After}'.");
        if (def.Rules.HandSize < 1 || def.Rules.Draw is < 0 || def.Rules.Budget < 0) throw new GameDataException("cards.json: bad deck rules.");
        All = all;
    }

    public CardDef? Find(string id) => All.GetValueOrDefault(id);

    /// <summary>
    /// An ability's target fits what it does: unit effects need a unit (a swap an ally), tile effects a tile, never both;
    /// a reaction answers an enemy's strike; no negative range or radius.
    /// </summary>
    private static void CheckTarget(CardDef c)
    {
        var e = c.Effect;
        if (c.Range < 0 || c.Radius < 0) throw new GameDataException($"Card '{c.Id}': range and radius cannot be negative.");
        if (e.NeedsUnit && e.NeedsTile) throw new GameDataException($"Card '{c.Id}': an ability acts on a unit or a tile, not both.");
        var unit = c.Target is CardTarget.Ally or CardTarget.Enemy or CardTarget.Unit;
        if (e.NeedsUnit && !unit) throw new GameDataException($"Card '{c.Id}': its effect needs a unit target.");
        if (e.NeedsTile && c.Target != CardTarget.Tile) throw new GameDataException($"Card '{c.Id}': its effect needs a tile target.");
        if (e.Swap && c.Target != CardTarget.Ally) throw new GameDataException($"Card '{c.Id}': a swap is with an ally.");
        if (c.Reaction && c.Target is not (CardTarget.Self or CardTarget.Enemy))
            throw new GameDataException($"Card '{c.Id}': a reaction answers an enemy's strike.");
        if (!e.IsAbility && c.Target is not CardTarget.Self && !c.Reaction)
            throw new GameDataException($"Card '{c.Id}': a buff is played on the character itself.");
    }

    /// <summary>A condition names exactly one of a skill, an attribute or a known perk hook, at no negative minimum.</summary>
    private static void CheckCondition(CardCondition c, string where)
    {
        if ((c.Skill is null ? 0 : 1) + (c.Attribute is null ? 0 : 1) + (c.Hook is null ? 0 : 1) != 1)
            throw new GameDataException($"{where}: a condition names exactly one of a skill, an attribute or a perk hook.");
        if (c.Min < 0) throw new GameDataException($"{where}: a condition's minimum cannot be negative.");
        if (c.Hook is { } hook && !KnownHooks.Contains(hook)) throw new GameDataException($"{where}: unknown perk hook '{hook}'.");
    }

    /// <summary>A card's reference value in scrap for the trade hint.</summary>
    public double TradeValue(CardDef card) => card.Value > 0 ? card.Value : Rules.TradeValues.GetValueOrDefault(card.Rarity);

    /// <summary>Why a deck list breaks the deck rules (unknown card, too many cards or copies), or null.</summary>
    public string? CheckDeck(IReadOnlyCollection<string> deck)
    {
        if (deck.Count > Rules.MaxDeck) return $"A deck holds at most {Rules.MaxDeck} cards.";
        foreach (var g in deck.GroupBy(id => id, StringComparer.Ordinal))
        {
            if (Find(g.Key) is not { } card) return $"Unknown card '{g.Key}'.";
            if (g.Count() > Rules.MaxCopies) return $"At most {Rules.MaxCopies} copies of {card.Name} in a deck.";
        }
        return null;
    }

    /// <summary>The starter set as a deck list (each id as often as its copies, in id order).</summary>
    public List<string> StarterDeck() =>
        Rules.Starter.OrderBy(kv => kv.Key, StringComparer.Ordinal).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
}
