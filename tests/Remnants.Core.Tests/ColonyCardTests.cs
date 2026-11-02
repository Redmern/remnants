using System.Text.Json;
using System.Text.Json.Nodes;
using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;

namespace Remnants.Core.Tests;

/// <summary>The colony's card collection, squad deck, personal slots and deck upgrades; their save and migration.</summary>
public class ColonyCardTests
{
    private static CardCatalog Cards => ScavengeTestData.Real.Cards;

    private static ColonySim Fresh() => NewGame.Create(TestData.Real, 7, ScavengeTestData.Real);

    [Fact]
    public void A_new_colony_owns_the_starter_set_as_its_deck()
    {
        var sim = Fresh();
        Assert.Equal(Cards.Rules.Starter.OrderBy(kv => kv.Key).ToList(), sim.State.Cards.Owned.ToList());
        Assert.Equal(Cards.StarterDeck(), sim.State.Cards.Deck);
    }

    [Fact]
    public void The_deck_is_built_from_owned_cards_only()
    {
        var sim = Fresh();
        Assert.True(sim.Execute(new SetDeck(["dash", "dash", "take_cover"])).Ok);
        Assert.Equal(["dash", "dash", "take_cover"], sim.State.Cards.Deck);
        Assert.Equal(CommandError.NotEnoughCards, sim.Execute(new SetDeck(["dash", "dash", "dash"])).Error);
        Assert.Equal(CommandError.UnknownCard, sim.Execute(new SetDeck(["no_such_card"])).Error);
    }

    [Fact]
    public void Slot_cards_come_from_the_collection_beside_the_deck()
    {
        var sim = Fresh();
        var s = sim.State.Survivors.First(x => !x.IsChild);
        Assert.True(sim.Execute(new SetDeck(["dash"])).Ok);
        Assert.True(sim.Execute(new SetCardSlots(s.Id, ["dash"])).Ok);
        Assert.Equal(["dash"], sim.State.Cards.Slots[s.Id]);
        // Two dashes owned: one in the deck, one in the slot; a third use is one too many.
        var other = sim.State.Survivors.Where(x => !x.IsChild).Skip(1).First();
        Assert.Equal(CommandError.NotEnoughCards, sim.Execute(new SetCardSlots(other.Id, ["dash"])).Error);
        Assert.Equal(CommandError.UnknownSurvivor, sim.Execute(new SetCardSlots(999, ["take_cover"])).Error);
        Assert.True(sim.Execute(new SetCardSlots(s.Id, [])).Ok);
        Assert.False(sim.State.Cards.Slots.ContainsKey(s.Id));
    }

    [Fact]
    public void Deck_upgrades_cost_resources_once_and_follow_their_chain()
    {
        var sim = Fresh();
        var budget = Cards.Rules.Upgrades.Single(u => u.Id == "budget_1");
        Assert.Equal(CommandError.UnknownCardUpgrade, sim.Execute(new BuyCardUpgrade("budget_2")).Error); // budget_1 first
        foreach (var (res, n) in budget.Cost) sim.State.Stockpile.Add(res, n);
        var before = ColonyCards.Loadout(sim)!.Budget;
        Assert.True(sim.Execute(new BuyCardUpgrade("budget_1")).Ok);
        Assert.Equal(before + 1, ColonyCards.Loadout(sim)!.Budget);
        Assert.All(budget.Cost, kv => Assert.Equal(0, sim.State.Stockpile.Get(kv.Key), 6));
        Assert.Equal(CommandError.AlreadyExists, sim.Execute(new BuyCardUpgrade("budget_1")).Error);
        Assert.Equal(CommandError.CannotAfford, sim.Execute(new BuyCardUpgrade("hand_1")).Error);
    }

    [Fact]
    public void An_expedition_takes_the_deck_and_each_members_slot_cards()
    {
        var sim = Fresh();
        var adults = sim.State.Survivors.Where(x => !x.IsChild).Take(2).ToList();
        adults[0].Level = Cards.Rules.Slots.Level;
        Assert.True(sim.Execute(new SetDeck(["dash", "take_cover", "steady_aim"])).Ok);
        Assert.True(sim.Execute(new SetCardSlots(adults[0].Id, ["sidestep"])).Ok);
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", adults.Select(a => new ExpeditionMember(a.Id)).ToList())).Ok, "launch");

        var party = sim.ExpeditionParty();
        Assert.Equal(["dash", "take_cover", "steady_aim"], party.Deck!.Cards);
        Assert.Equal(Cards.Rules.HandSize, party.Deck.HandSize);
        Assert.Equal(["sidestep"], party.Members.Single(m => m.Survivor.Id == adults[0].Id).SlotCards);
    }

    [Fact]
    public void A_colony_without_cards_sends_its_party_without_a_deck()
    {
        var sim = LoopTestData.Colony();
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Ok);
        Assert.Null(sim.ExpeditionParty().Deck);
    }

    [Fact]
    public void Cards_survive_save_and_load()
    {
        var sim = Fresh();
        Assert.True(sim.Execute(new SetDeck(["dash", "take_cover"])).Ok);
        var json = SaveGame.Save(sim.State);
        Assert.Contains($"\"version\": {SaveGame.Version}", json);
        Assert.Equal(27, SaveGame.Version);
        var loaded = SaveGame.Load(TestData.Real, json, ScavengeTestData.Real);
        Assert.Equal(sim.State.Cards.Owned, loaded.State.Cards.Owned);
        Assert.Equal(["dash", "take_cover"], loaded.State.Cards.Deck);
    }

    /// <summary>A save as version 26 wrote it: no card collection.</summary>
    private static string AsV26(ColonyState state)
    {
        var node = JsonNode.Parse(SaveGame.Save(state))!;
        node["version"] = 26;
        node["colony"]!.AsObject().Remove("cards");
        return node.ToJsonString();
    }

    [Fact]
    public void A_v26_colony_migrates_and_gets_the_starter_set()
    {
        var sim = LoopTestData.Colony();
        sim.AddSurvivor();
        var loaded = SaveGame.Load(TestData.Real, AsV26(sim.State), ScavengeTestData.Real);
        Assert.Equal(Cards.StarterDeck(), loaded.State.Cards.Deck);
        Assert.Equal(Cards.Rules.Starter.Values.Sum(), loaded.State.Cards.Owned.Values.Sum());
    }

    [Fact]
    public void A_v26_colony_with_a_run_in_progress_restarts_that_run_and_keeps_the_party_out()
    {
        var sim = LoopTestData.Colony();
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Ok);
        var run = sim.OpenExpeditionScavenge();
        run.Execute(new EndTurn(ColonySim.ExpeditionPlayerId, run.State.CurrentUnitId!.Value));
        Assert.NotNull(sim.State.Expedition!.Scavenge);

        var loaded = SaveGame.Load(TestData.Real, AsV26(sim.State), ScavengeTestData.Real);
        var expedition = Assert.IsType<Expedition>(loaded.State.Expedition);
        Assert.Null(expedition.Scavenge);
        Assert.True(loaded.State.FindSurvivor(a.Id)!.Away);
        var fresh = loaded.OpenExpeditionScavenge(); // the run starts over from the expedition's seed, with today's rules
        Assert.Equal(1, fresh.State.Round);
        Assert.Single(fresh.State.Decks);
    }

    [Fact]
    public void An_old_scavenge_save_is_refused()
    {
        var sim = NewScavenge.Create(ScavengeTestData.Real, "outskirts", 1, [ScavengeTestData.Party(1)]);
        var node = JsonNode.Parse(ScavengeSave.Save(sim.State))!;
        node["version"] = 14;
        Assert.Throws<JsonException>(() => ScavengeSave.Load(ScavengeTestData.Real, node.ToJsonString()));
    }
}
