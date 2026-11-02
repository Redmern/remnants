using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

/// <summary>
/// Cards over the wire: a party's deck comes with its Join; a player sees its own hand, draw pile, slot cards and pouch,
/// and of everyone else's only how many cards they hold (never which), nor which card they played.
/// </summary>
public class CardNetTests
{
    private static readonly List<string> Deck = ["dash", "dash", "take_cover", "take_cover", "steady_aim", "steady_aim", "scout_ahead", "roll_with_it"];

    private static List<PartyMember> Veterans(string prefix)
    {
        var party = NetFixture.Party(prefix);
        party[0].Survivor.Level = NetFixture.Data.Cards.Rules.Slots.Level;
        return [party[0] with { SlotCards = ["sidestep"] }, party[1]];
    }

    private static (NetFixture F, TestPlayer A, TestPlayer B) Start()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var a = f.Connect("Alice");
        a.Client.Join("Alice", Veterans("A"), deck: Deck);
        var b = f.Connect("Bob");
        b.Client.Join("Bob", Veterans("B"), deck: Deck);
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        return (f, a, b);
    }

    [Fact]
    public void Each_player_sees_their_own_cards_and_only_counts_of_everyone_elses()
    {
        var (f, a, b) = Start();
        Assert.Equal(2, f.Sim.State.Decks.Count);
        var mine = Assert.Single(a.View.Decks, d => d.PlayerId == a.Id);
        Assert.False(mine.Hidden);
        Assert.Equal(5, mine.Hand.Count);
        Assert.NotEmpty(mine.DrawPile);

        var theirs = Assert.Single(a.View.Decks, d => d.PlayerId == b.Id);
        Assert.True(theirs.Hidden);
        Assert.Empty(theirs.Hand);
        Assert.Empty(theirs.DrawPile);
        Assert.Empty(theirs.Used);
        Assert.Equal(5, theirs.HandCount);
        Assert.Equal(Deck.Count - 5, theirs.DrawCount);

        Assert.Equal(["sidestep"], a.View.Units.Single(u => u.PlayerId == a.Id && u.Name == "A1").SlotCards);
        foreach (var u in a.View.Units.Where(u => u.PlayerId == b.Id))
        {
            Assert.Empty(u.SlotCards);
            Assert.Equal(f.Sim.State.FindUnit(u.Id)!.SlotCards.Count, u.HiddenSlots);
        }
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_card_played_through_the_server_is_told_to_its_own_side_only()
    {
        var (f, a, b) = Start();
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a, a, b);
        var unit = f.Sim.State.CurrentUnit!;
        var deck = SquadDecks.Of(f.Sim.State, a.Id)!;
        var card = deck.Hand.First(id => id is "dash" or "take_cover" or "steady_aim" or "roll_with_it");
        // Bob's people stand where Alice's can be seen, so Bob would see the unit the card was played on.
        foreach (var bob in f.Sim.State.Units.Where(u => u.PlayerId == b.Id)) bob.Pos = f.FreeNeighbour(unit.Pos);

        var played = a.Do(new PlayCard(a.Id, unit.Id, card));
        Assert.True(played.Ok, played.Message);
        Assert.Contains(a.Last<Snapshot>().Events, e => e.Type == ScavengeEventType.CardPlayed);
        Assert.DoesNotContain(b.Last<Snapshot>().Events, e => e.Type == ScavengeEventType.CardPlayed);
        var name = f.Sim.Data.Cards.Find(card)!.Name;
        Assert.DoesNotContain(name, ProtocolJson.Serialize(b.Last<Snapshot>()));
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_deck_naming_an_unknown_card_is_refused_at_the_door()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var a = f.Connect("Alice");
        a.Client.Join("Alice", NetFixture.Party("A"), deck: ["no_such_card"]);
        Assert.Contains("no_such_card", a.Last<JoinRefused>().Reason);
    }
}
