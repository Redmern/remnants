using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

public enum CardCategory { Movement, Attack, Defense, Utility }

public enum CardRarity { Common, Uncommon, Rare }

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
/// (this turn, and for a plan declared now), <see cref="Hit"/> on its next attack, <see cref="Guard"/> tiers off the next
/// hit it takes, <see cref="Cover"/> until its next turn, <see cref="Mend"/> off its worst injury (field mending),
/// <see cref="Draw"/> cards for the squad, and <see cref="Counter"/>: a reaction blow at an enemy whose plan it reads,
/// struck when that plan resolves.
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

    public bool IsEmpty => Move == 0 && Hit == 0 && Guard == 0 && Cover == 0 && Mend == 0 && Draw == 0 && !Counter;

    public CardEffect Plus(CardEffect? other) => other is null ? this : new()
    {
        Move = Move + other.Move, Hit = Hit + other.Hit, Guard = Guard + other.Guard, Cover = Cover + other.Cover,
        Mend = Mend + other.Mend, Draw = Draw + other.Draw, Counter = Counter || other.Counter,
    };

    /// <summary>Numbers × <paramref name="factor"/> (whole-number effects round down, never below their base).</summary>
    public CardEffect Scaled(double factor) => new()
    {
        Move = Math.Max(Move, (int)Math.Floor(Move * factor + 1e-9)), Hit = Hit * factor, Guard = Math.Max(Guard, (int)Math.Floor(Guard * factor + 1e-9)),
        Cover = Cover * factor, Mend = Mend * factor, Draw = Draw, Counter = Counter,
    };
}

/// <summary>
/// A tactical card (data/cards.json). <see cref="Cost"/> card points from the squad's round budget; <see cref="ApCost"/> AP
/// of the character it is played on (most cost none); <see cref="BoostAp"/> more AP adds <see cref="Boost"/>.
/// <see cref="Consumable"/>: gone for good once played (else it goes to the used pile). <see cref="Reaction"/>: played
/// against a seen enemy's telegraphed plan. <see cref="ArmLock"/>: not while the character's arms are badly hurt.
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
    public CardRequirement Requirement { get; init; } = new();
    public CardScaling? Scaling { get; init; }
    public CardEffect Effect { get; init; } = new();

    /// <summary>A baseline card's extra for a character who meets the requirement.</summary>
    public CardEffect? Bonus { get; init; }

    /// <summary>What <see cref="BoostAp"/> more AP adds.</summary>
    public CardEffect? Boost { get; init; }

    /// <summary>Reference value in scrap for the trade hint (0: by rarity).</summary>
    public double Value { get; init; }

    public double TradeValue => Value > 0 ? Value : Rarity switch { CardRarity.Common => 6, CardRarity.Uncommon => 15, _ => 30 };
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
/// The squad deck's numbers: hand size, cards drawn at each round start (up to the hand size; the first round deals a full
/// hand) and the card-point budget refilled each round; the starter set (id → copies) new colonies get.
/// </summary>
public sealed record CardRules
{
    public int HandSize { get; init; } = 5;
    public int Draw { get; init; } = 3;
    public int Budget { get; init; } = 3;
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
            if (c.BoostAp > 0 && c.Boost is null) throw new GameDataException($"Card '{c.Id}': a boost cost needs a boost effect.");
            if (c.Requirement.Kind == CardKind.Hard && c.Requirement.AnyOf.Count == 0)
                throw new GameDataException($"Card '{c.Id}': a hard card needs a requirement.");
        }
        foreach (var id in def.Rules.Starter.Keys.Where(id => !all.ContainsKey(id)))
            throw new GameDataException($"cards.json: starter card '{id}' is not a card.");
        var upgrades = def.Rules.Upgrades.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var u in def.Rules.Upgrades.Where(u => u.After is { } a && !upgrades.Contains(a)))
            throw new GameDataException($"cards.json: upgrade '{u.Id}' follows unknown upgrade '{u.After}'.");
        if (def.Rules.HandSize < 1 || def.Rules.Draw < 0 || def.Rules.Budget < 0) throw new GameDataException("cards.json: bad deck rules.");
        All = all;
    }

    public CardDef? Find(string id) => All.GetValueOrDefault(id);

    /// <summary>The starter set as a deck list (each id as often as its copies, in id order).</summary>
    public List<string> StarterDeck() =>
        Rules.Starter.OrderBy(kv => kv.Key, StringComparer.Ordinal).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
}
