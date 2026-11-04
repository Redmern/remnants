using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Personal card slots: how many a character has, and playing from them.</summary>
public class CardSlotTests
{
    [Fact]
    public void Slots_come_from_level_skills_gear_and_perks()
    {
        var u = SurvivorUnit();
        Assert.Equal(0, CardSlots.Count(Real, u));
        u.Origin!.Level = Real.Cards.Rules.Slots.Level;
        Assert.Equal(1, CardSlots.Count(Real, u));
        u.Skills[Skill.Melee] = 60;
        Assert.Equal(2, CardSlots.Count(Real, u));
        u.Equipment[EquipSlot.Body] = "scout_jacket";
        Assert.Equal(3, CardSlots.Count(Real, u));
        u.Hooks[PerkHooks.CardSlots] = 1;
        Assert.Equal(4, CardSlots.Count(Real, u));
    }

    [Fact]
    public void A_party_member_brings_only_as_many_slot_cards_as_slots()
    {
        var s = Survivor("Vet", 55);
        s.Level = Real.Cards.Rules.Slots.Level; // one slot
        var party = new PartySetup(1, [new PartyMember(s, SlotCards: ["dash", "take_cover"])]) { Deck = new(["steady_aim"], 5, 3, 3) };
        var sim = NewScavenge.Create(Real, "outskirts", 3, [party]);
        var unit = sim.State.Units.Single(u => u.PlayerId == 1);
        Assert.Equal(["dash"], unit.SlotCards);
        Assert.Throws<ArgumentException>(() => NewScavenge.Create(Real, "outskirts", 3,
            [new PartySetup(1, [new PartyMember(s, SlotCards: ["nope"])])]));
    }

    [Fact]
    public void A_slot_card_is_always_to_hand_pays_from_the_squad_pool_and_comes_back_next_round()
    {
        var a = SurvivorUnit(1, new(0, 1));
        var b = SurvivorUnit(2, new(5, 1));
        var sim = Arena(10, 3, [a, b]);
        var deck = new SquadDeck { PlayerId = 1, Points = 3, Budget = 3, HandSize = 5 };
        sim.State.Decks.Add(deck);
        a.SlotCards = ["take_cover", "field_dressing"];
        a.ExtraCards = 1;
        a.Body.Injure(BodyPart.Torso, 0.4);

        Assert.True(sim.Execute(new PlayCard(1, a.Id, "take_cover", FromSlot: true)).Ok);
        Assert.Equal(2, deck.Points);
        Assert.Equal(ScavengeError.NoCard, sim.Execute(new PlayCard(1, a.Id, "take_cover", FromSlot: true)).Error); // spent this round
        Assert.Equal(ScavengeError.NoCard, sim.Execute(new PlayCard(1, a.Id, "take_cover")).Error); // not in the squad hand
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "field_dressing", FromSlot: true)).Ok);
        Assert.Equal(["take_cover"], a.SlotCards); // consumable: gone
        Assert.Equal(["field_dressing"], deck.Consumed);

        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Empty(a.SlotSpent);
        Assert.Equal(3, deck.Points);
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "take_cover", FromSlot: true)).Ok);
    }

    [Fact]
    public void Only_the_slot_holder_plays_a_slot_card()
    {
        var a = SurvivorUnit(1, new(0, 1));
        var a2 = SurvivorUnit(1, new(0, 2));
        var sim = Arena(10, 3, [a, a2, SurvivorUnit(2, new(9, 1))]);
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Points = 3, Budget = 3, HandSize = 5 });
        a2.SlotCards = ["take_cover"];
        Assert.Equal(ScavengeError.NoCard, sim.Execute(new PlayCard(1, a.Id, "take_cover", FromSlot: true)).Error);
    }

    [Fact]
    public void Some_weapon_actions_are_arm_locked()
    {
        Assert.True(Real.Items["rifle"].Actions.Single(x => x.Id == "aimed_shot").ArmLock);
        var u = SurvivorUnit(weapon: "rifle");
        Assert.NotNull(ProfileDeriver.Derive(Real, u).Action("aimed_shot"));
        u.Body.Injure(BodyPart.LeftArm, 0.8);
        u.Body.Injure(BodyPart.RightArm, 0.8);
        Assert.Null(ProfileDeriver.Derive(Real, u).Action("aimed_shot"));
    }
}
