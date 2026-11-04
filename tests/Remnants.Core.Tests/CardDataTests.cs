using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class CardDataTests
{
    private static CardCatalog Cards => Real.Cards;

    [Fact]
    public void The_catalog_holds_common_or_uncommon_cards_across_every_category()
    {
        Assert.InRange(Cards.All.Count, 20, 30);
        Assert.Equal(Enum.GetValues<CardCategory>().ToHashSet(), Cards.All.Values.Select(c => c.Category).ToHashSet());
        Assert.All(Cards.All.Values, c => Assert.True(c.Rarity is CardRarity.Common or CardRarity.Uncommon, c.Id));
        Assert.NotEmpty(Cards.Rules.Starter);
        Assert.All(Cards.Rules.Starter, kv => Assert.True(Cards.All.ContainsKey(kv.Key) && kv.Value is >= 1 and <= 2, kv.Key));
        Assert.True(Cards.All.Values.Count(c => c.ArmLock) >= 2);
        Assert.Contains(Cards.All.Values, c => c.Consumable);
        Assert.Contains(Cards.All.Values, c => c.ApCost > 0);
        Assert.Contains(Cards.All.Values, c => c.BoostAp > 0);
        Assert.Contains(Cards.All.Values, c => c.Requirement.Kind == CardKind.Hard);
        Assert.Contains(Cards.All.Values, c => c.Scaling is not null);
        Assert.Equal(5, Cards.Rules.HandSize);
    }

    private static List<CardDef> StarterCards => Cards.Rules.Starter.Keys.Select(id => Cards.All[id]).ToList();

    [Fact]
    public void The_starter_deck_is_twelve_cards_mostly_abilities_with_buffs_as_cheap_filler()
    {
        Assert.Equal(12, Cards.Rules.Starter.Count);
        var abilities = StarterCards.Where(c => c.Effect.IsAbility).ToList();
        Assert.True(abilities.Count >= 8, $"{abilities.Count} abilities");
        Assert.All(StarterCards, c => Assert.InRange(c.Cost, 1, 2));
        Assert.All(StarterCards.Where(c => !c.Effect.IsAbility), c => Assert.Equal(1, c.Cost));
        Assert.All(StarterCards, c => Assert.Equal(0, c.ApCost)); // cards cost points, not AP
    }

    [Fact]
    public void The_starter_deck_has_cards_for_flanking_and_for_answering_telegraphs()
    {
        // Facing: a step that turns the player round an enemy, a shove that spins one.
        Assert.True(StarterCards.Count(c => c.Effect.Step > 0 || c.Effect.Spin) >= 2);
        // Telegraphs: reactions to a seen plan that break it (a disrupt, a yank off its tile).
        Assert.True(StarterCards.Count(c => (c.Effect.Disrupt || c.Effect.Push + c.Effect.Pull > 0)) >= 2);
        // Every kind of ability is in the catalog.
        var all = Cards.All.Values.Select(c => c.Effect).ToList();
        Assert.Contains(all, e => e.Push > 0);
        Assert.Contains(all, e => e.Pull > 0);
        Assert.Contains(all, e => e.Swap);
        Assert.Contains(all, e => e.GrantAp > 0);
        Assert.Contains(all, e => e.AdvanceInitiative);
        Assert.Contains(all, e => e.DelayInitiative);
        Assert.Contains(all, e => e.PlaceBarrier > 0);
        Assert.Contains(all, e => e.Smoke > 0);
        Assert.Contains(all, e => e.Strike);
    }

    /// <summary>
    /// What a card can do, as numbers to compare across kinds: every effect number, range and radius, and what follows from
    /// them (it breaks a plan, it answers a readied strike, it is played off-turn, it needs no plan to aim at, how broad
    /// its target is).
    /// </summary>
    private static Dictionary<string, double> Capabilities(CardDef c)
    {
        var e = c.Effect;
        var caps = typeof(CardEffect).GetProperties().Where(p => p.CanWrite && p.PropertyType is var t && (t == typeof(int) || t == typeof(double) || t == typeof(bool)))
            .ToDictionary(p => p.Name, p => Convert.ToDouble(p.GetValue(e)));
        caps["Range"] = c.Range == 0 ? 99 : c.Range;
        caps["Radius"] = c.Radius;
        caps["BreaksPlan"] = e.Disrupt || e.Push > 0 || e.Pull > 0 ? 1 : 0;
        caps["AnswersStrike"] = e.Disrupt ? 1 : 0;
        caps["Targets"] = c.Target switch { CardTarget.Unit => 2, CardTarget.Ally or CardTarget.Enemy => 1, _ => 0 };
        return caps;
    }

    [Fact]
    public void No_starter_card_is_strictly_better_than_another_whatever_its_kind()
    {
        foreach (var a in StarterCards)
            foreach (var b in StarterCards.Where(b => b.Id != a.Id))
            {
                // Same kind of target (a unit card covers ally and enemy cards); then: at least as cheap and as much of everything.
                if (!(a.Target == b.Target || a.Target == CardTarget.Unit && b.Target is CardTarget.Ally or CardTarget.Enemy)) continue;
                var (ca, cb) = (Capabilities(a), Capabilities(b));
                var atLeast = ca.Keys.All(k => ca[k] >= cb[k]);
                Assert.False(atLeast && a.Cost <= b.Cost && (a.Cost < b.Cost || ca.Keys.Any(k => ca[k] > cb[k])), $"{a.Id} beats {b.Id}");
            }
    }

    /// <summary>
    /// A rough value of a card in AP-equivalents (1 AP ~ a move or a cheap blow): the progress notes' card power table. A
    /// reaction is worth a quarter less (it needs a seen plan to aim at) and a quarter AP more (played off-turn).
    /// </summary>
    public static double Power(CardDef c)
    {
        var e = c.Effect;
        var v = (e.Push + e.Pull) * 1.0 + (e.Spin ? 1.0 : 0) + (e.Disrupt || e.Push > 0 || e.Pull > 0 ? 1.0 : 0) + (e.Disrupt ? 0.5 : 0)
                + e.Step * 0.75 + (e.Swap ? 1.0 : 0) + e.GrantAp * 1.0 + (e.AdvanceInitiative || e.DelayInitiative ? 0.75 : 0)
                + e.PlaceBarrier * 0.5 + e.Smoke * (2 * c.Radius + 1) * (2 * c.Radius + 1) / 9.0 + (e.Strike ? 2.0 : 0) + (e.Attack ? 1.5 : 0) + e.Block * 3
                + e.Hit / 0.1 * 0.5 + e.Cover / 0.15 * 0.5 + e.Guard * 0.75 + e.Mend / 0.1 * 0.75 + e.Draw * 0.5 + e.Move * 0.5
                + Math.Clamp(c.Range - 1, 0, 5) * 0.1;
        return v;
    }

    [Fact]
    public void Every_starter_card_is_worth_its_points_none_far_above_the_rest()
    {
        var perPoint = StarterCards.ToDictionary(c => c.Id, c => Power(c) / c.Cost);
        var median = perPoint.Values.Order().ElementAt(perPoint.Count / 2);
        Assert.All(perPoint, kv => Assert.True(kv.Value >= 0.4 * median && kv.Value <= 2.0 * median, $"{kv.Key} {kv.Value:0.00}/pt vs median {median:0.00}"));
    }

    [Fact]
    public void A_guard_card_stays_in_the_starter_deck()
    {
        // Without one, hits land a tier heavier: more injuries, fewer expeditions, a weaker colony (raids).
        Assert.Contains(StarterCards, c => c.Effect.Guard > 0);
    }

    [Fact]
    public void Deck_limits_and_trade_values_come_from_the_data()
    {
        var r = Cards.Rules;
        Assert.Equal((60, 4, 8, 1, true), (r.MaxDeck, r.MaxCopies, r.MaxSlotCards, r.MinHandSize, r.FullOpeningHand));
        Assert.Equal(6, Cards.TradeValue(Cards.All["dash"]));      // common
        Assert.Equal(15, Cards.TradeValue(Cards.All["sprint"]));   // uncommon
        Assert.Null(Cards.CheckDeck(Cards.StarterDeck()));
        Assert.Contains("copies", Cards.CheckDeck(Enumerable.Repeat("dash", r.MaxCopies + 1).ToList()));
    }

    [Fact]
    public void A_requirement_is_met_by_any_one_of_its_conditions()
    {
        var req = new CardRequirement
        {
            Kind = CardKind.Hard,
            AnyOf = [new CardCondition { Skill = Skill.Firearms, Min = 40 }, new CardCondition { Attribute = AttributeKind.Perception, Min = 65 },
                     new CardCondition { Hook = "deadeye" }],
        };
        var u = SurvivorUnit(attributes: 50);
        Assert.False(req.Met(u));
        u.Skills[Skill.Firearms] = 40;
        Assert.True(req.Met(u));
        u.Skills[Skill.Firearms] = 10;
        u.Attributes[AttributeKind.Perception] = 70;
        Assert.True(req.Met(u));
        u.Attributes[AttributeKind.Perception] = 50;
        u.Hooks["deadeye"] = 1;
        Assert.True(req.Met(u));
        Assert.True(new CardRequirement().Met(SurvivorUnit())); // no conditions: anyone
    }

    [Fact]
    public void Scaling_grows_with_the_stat_up_to_its_cap()
    {
        var scaling = new CardScaling { Skill = Skill.Medicine, Per = 0.01, Max = 0.5 };
        var u = SurvivorUnit();
        u.Skills[Skill.Medicine] = 0;
        Assert.Equal(1, scaling.Factor(u), 9);
        u.Skills[Skill.Medicine] = 30;
        Assert.Equal(1.3, scaling.Factor(u), 9);
        u.Skills[Skill.Medicine] = 90;
        Assert.Equal(1.5, scaling.Factor(u), 9);
        var byLevel = new CardScaling { Level = true, Per = 0.1, Max = 1 };
        u.Origin!.Level = 4;
        Assert.Equal(1.4, byLevel.Factor(u), 9);
    }

    [Theory]
    [InlineData("empty condition")]
    [InlineData("two-headed condition")]
    [InlineData("negative minimum")]
    [InlineData("unknown hook")]
    [InlineData("negative effect")]
    [InlineData("negative bonus")]
    [InlineData("scaling with nothing")]
    [InlineData("negative scaling")]
    [InlineData("slot rule without a skill")]
    [InlineData("unknown slot hook")]
    [InlineData("free upgrade")]
    public void Broken_card_data_is_refused(string broken)
    {
        var card = Cards.Def.Cards[0];
        var rules = Cards.Rules;
        CardRequirement Req(CardCondition c) => new() { Kind = CardKind.Hard, AnyOf = [c] };
        var bad = broken switch
        {
            "empty condition" => Cards.Def with { Cards = [card with { Requirement = Req(new CardCondition()) }] },
            "two-headed condition" => Cards.Def with { Cards = [card with { Requirement = Req(new CardCondition { Skill = Skill.Melee, Hook = "card_slots" }) }] },
            "negative minimum" => Cards.Def with { Cards = [card with { Requirement = Req(new CardCondition { Skill = Skill.Melee, Min = -1 }) }] },
            "unknown hook" => Cards.Def with { Cards = [card with { Requirement = Req(new CardCondition { Hook = "no_such_hook" }) }] },
            "negative effect" => Cards.Def with { Cards = [card with { Effect = new CardEffect { Move = 1, Hit = -0.1 } }] },
            "negative bonus" => Cards.Def with { Cards = [card with { Bonus = new CardEffect { Guard = -1 } }] },
            "scaling with nothing" => Cards.Def with { Cards = [card with { Scaling = new CardScaling { Per = 0.1 } }] },
            "negative scaling" => Cards.Def with { Cards = [card with { Scaling = new CardScaling { Skill = Skill.Melee, Per = -0.1 } }] },
            "slot rule without a skill" => Cards.Def with { Rules = rules with { Slots = rules.Slots with { Skills = [new CardCondition { Attribute = AttributeKind.Agility, Min = 60 }] } } },
            "unknown slot hook" => Cards.Def with { Rules = rules with { Slots = rules.Slots with { Hook = "no_such_hook" } } },
            _ => Cards.Def with { Rules = rules with { Upgrades = [rules.Upgrades[0] with { Amount = 0 }] } },
        };
        if (bad.Cards.Count == 1) bad = bad with { Rules = bad.Rules with { Starter = new() { [card.Id] = 1 } } };
        Assert.Throws<GameDataException>(() => new CardCatalog(bad));
    }

    [Fact]
    public void Card_data_with_an_unknown_starter_card_is_refused()
    {
        var bad = Cards.Def with { Rules = Cards.Rules with { Starter = new() { ["no_such_card"] = 1 } } };
        Assert.Throws<GameDataException>(() => new CardCatalog(bad));
        var twice = Cards.Def with { Cards = Cards.Def.Cards.Append(Cards.Def.Cards[0]).ToList() };
        Assert.Throws<GameDataException>(() => new CardCatalog(twice));
    }
}
