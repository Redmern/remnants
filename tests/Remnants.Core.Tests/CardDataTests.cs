using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class CardDataTests
{
    private static CardCatalog Cards => Real.Cards;

    [Fact]
    public void The_starter_set_is_about_twelve_common_or_uncommon_cards_across_every_category()
    {
        Assert.InRange(Cards.All.Count, 10, 16);
        Assert.Equal(Enum.GetValues<CardCategory>().ToHashSet(), Cards.All.Values.Select(c => c.Category).ToHashSet());
        Assert.All(Cards.All.Values, c => Assert.True(c.Rarity is CardRarity.Common or CardRarity.Uncommon, c.Id));
        Assert.NotEmpty(Cards.Rules.Starter);
        Assert.All(Cards.Rules.Starter, kv => Assert.True(Cards.All.ContainsKey(kv.Key) && kv.Value is >= 1 and <= 2, kv.Key));
        Assert.Contains(Cards.All.Values, c => c.Reaction);
        Assert.True(Cards.All.Values.Count(c => c.ArmLock) >= 2);
        Assert.Contains(Cards.All.Values, c => c.Consumable);
        Assert.Contains(Cards.All.Values, c => c.ApCost > 0);
        Assert.Contains(Cards.All.Values, c => c.BoostAp > 0);
        Assert.Contains(Cards.All.Values, c => c.Requirement.Kind == CardKind.Hard);
        Assert.Contains(Cards.All.Values, c => c.Scaling is not null);
        Assert.Equal(5, Cards.Rules.HandSize);
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

    [Fact]
    public void Card_data_with_an_unknown_starter_card_is_refused()
    {
        var bad = Cards.Def with { Rules = Cards.Rules with { Starter = new() { ["no_such_card"] = 1 } } };
        Assert.Throws<GameDataException>(() => new CardCatalog(bad));
        var twice = Cards.Def with { Cards = Cards.Def.Cards.Append(Cards.Def.Cards[0]).ToList() };
        Assert.Throws<GameDataException>(() => new CardCatalog(twice));
    }
}
