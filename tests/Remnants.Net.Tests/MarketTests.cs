using Remnants.Core.Colony;
using Remnants.Core.Trade;

namespace Remnants.Net.Tests;

/// <summary>
/// The Trading Post over loopback: post / browse / accept / claim with real colonies, retries and lost answers never
/// doing anything twice, expiry and cancel refunds, persistence across a server restart, rate limits and caps, and what
/// a listing reveals about its seller.
/// </summary>
public class MarketTests
{
    private static TradeRules Rules => TradeNetTests.Rules;

    private sealed class Market
    {
        public readonly ManualClock Clock = new();
        public MarketHost Host;
        public LoopbackHub Hub;
        public readonly List<string> Log = new();

        public Market(string? path = null, TradeRules? rules = null)
        {
            Host = new MarketHost(new MarketStore(path), rules ?? Rules, TradeNetTests.Game.Resources.ContainsKey, NetFixture.Data, Clock) { Log = Log.Add };
            Hub = new LoopbackHub(Host);
        }

        public void Restart(string path)
        {
            Host = new MarketHost(new MarketStore(path), Rules, TradeNetTests.Game.Resources.ContainsKey, NetFixture.Data, Clock) { Log = Log.Add };
            Hub = new LoopbackHub(Host);
        }
    }

    private sealed record Player(MarketClient Client, ColonySim Colony, LoopbackClient Link, TradeIdentity Identity)
    {
        public int Saves;
    }

    private static Player Connect(Market m, string name, ColonySim? colony = null, TradeIdentity? identity = null)
    {
        colony ??= TradeNetTests.Colony((ulong)name.Length);
        var link = m.Hub.Connect();
        var client = new MarketClient(link, () => colony);
        var player = new Player(client, colony, link, identity ?? TradeIdentity.Create(name));
        client.ColonyChanged += () => player.Saves++;
        client.Hello(player.Identity);
        Assert.NotNull(client.Welcome);
        return player;
    }

    private static MarketResult Done(Task<MarketResult> task)
    {
        Assert.True(task.IsCompleted, "loopback answers synchronously");
        return task.Result;
    }

    private static List<MarketListing> Browse(Player p, MarketFilter? filter = null) => p.Client.Browse(filter).Result.Listings;

    [Fact]
    public void Post_browse_accept_and_claim()
    {
        var m = new Market();
        var seller = Connect(m, "Sally");
        var buyer = Connect(m, "Bram");

        var posted = Done(seller.Client.Post([TradeGood.Resource("wood", 30)], [TradeGood.Resource("scrap", 12)]));
        Assert.True(posted.Ok, posted.Message);
        Assert.Equal(30, seller.Colony.State.Stockpile.Get("wood"), 6); // 60 at start, 30 held by the Trading Post
        Assert.Empty(seller.Colony.State.Trade.Escrows);

        var listing = Browse(buyer).Single();
        Assert.Equal("Sally", listing.Seller);
        Assert.False(listing.Mine);
        Assert.True(Browse(seller).Single().Mine);
        Assert.Single(Browse(buyer, new MarketFilter(Text: "wood")));
        Assert.Empty(Browse(buyer, new MarketFilter(GiveId: "stone")));
        Assert.Empty(Browse(buyer, new MarketFilter(MineOnly: true)));

        var taken = Done(buyer.Client.Accept(listing));
        Assert.True(taken.Ok, taken.Message);
        Assert.Equal(38, buyer.Colony.State.Stockpile.Get("scrap"), 6);
        Assert.Equal(90, buyer.Colony.State.Stockpile.Get("wood"), 6); // claimed right away
        Assert.Equal(62, seller.Colony.State.Stockpile.Get("scrap"), 6); // the seller was online: claimed too
        Assert.Empty(m.Host.Data.Claims);
        Assert.Empty(m.Host.Data.Listings);
        Assert.Empty(Browse(buyer));
        Assert.Contains(seller.Client.Received, c => c.Reason.Contains("Bram"));
    }

    [Fact]
    public void Listings_never_reveal_the_sellers_key_or_id()
    {
        var m = new Market();
        var seller = Connect(m, "Sally");
        var buyer = Connect(m, "Bram");
        Done(seller.Client.Post([TradeGood.Resource("wood", 5)], [TradeGood.Resource("scrap", 1)]));
        Browse(buyer);
        var json = string.Join("\n", buyer.Link.Inbox.Select(ProtocolJson.Serialize));
        Assert.DoesNotContain(seller.Identity.Key, json);
        Assert.DoesNotContain(MarketHost.PlayerIdOf(seller.Identity.Key), json);
    }

    [Fact]
    public void Claims_wait_for_an_offline_seller_and_apply_once()
    {
        var m = new Market();
        var sellerColony = TradeNetTests.Colony(5);
        var identity = TradeIdentity.Create("Sally");
        var seller = Connect(m, "Sally", sellerColony, identity);
        Done(seller.Client.Post([TradeGood.Resource("wood", 10)], [TradeGood.Resource("scrap", 7)]));
        seller.Link.Close();

        var buyer = Connect(m, "Bram");
        Done(buyer.Client.Accept(Browse(buyer).Single()));
        Assert.Single(m.Host.Data.Claims);

        // The seller's client gets the claim, applies it, but the ack is lost (a crash before it went out).
        var raw = m.Hub.Connect();
        raw.Send(new MarketHello(Protocol.Version, "Sally", identity.Key));
        var claim = raw.Inbox.OfType<MarketClaims>().Last().Claims.Single();
        ColonyTrade.Receive(sellerColony, claim.ClaimId, claim.Goods, "test");
        raw.Close();
        Assert.Equal(57, sellerColony.State.Stockpile.Get("scrap"), 6);

        // Next connect: the claim comes again, is recognised, acked and gone; nothing is added twice.
        var again = Connect(m, "Sally", sellerColony, identity);
        Assert.Equal(57, sellerColony.State.Stockpile.Get("scrap"), 6);
        Assert.Empty(m.Host.Data.Claims);
        Assert.Empty(again.Client.Received);

        // A second request for claims finds nothing.
        again.Client.RequestClaims();
        Assert.Empty(again.Link.Inbox.OfType<MarketClaims>().Last().Claims);
    }

    [Fact]
    public void A_retried_post_or_accept_happens_once()
    {
        var m = new Market();
        var raw = m.Hub.Connect();
        raw.Send(new MarketHello(Protocol.Version, "Sally", TradeIdentity.Create("Sally").Key));
        var post = new MarketPost(1, "mkt-post-1", [TradeGood.Resource("wood", 10)], [TradeGood.Resource("scrap", 3)]);
        raw.Send(post);
        raw.Send(post with { RequestId = 2 });
        Assert.Single(m.Host.Data.Listings);
        Assert.All(raw.Inbox.OfType<MarketResult>(), r => Assert.True(r.Ok));

        var buyer = m.Hub.Connect();
        buyer.Send(new MarketHello(Protocol.Version, "Bram", TradeIdentity.Create("Bram").Key));
        var id = m.Host.Data.Listings.Single().Id;
        buyer.Send(new MarketAccept(1, "mkt-take-1", id));
        buyer.Send(new MarketAccept(2, "mkt-take-1", id));
        Assert.All(buyer.Inbox.OfType<MarketResult>(), r => Assert.True(r.Ok));
        Assert.Equal(2, m.Host.Data.Claims.Count); // one for each side, not four

        // Someone else's transaction id is refused, not replayed.
        buyer.Send(new MarketPost(3, "mkt-post-1", [TradeGood.Resource("wood", 1)], [TradeGood.Resource("scrap", 1)]));
        Assert.False(buyer.Inbox.OfType<MarketResult>().Last().Ok);
        Assert.Contains(m.Log, l => l.Contains("suspicious"));
    }

    [Fact]
    public void A_request_whose_answer_was_lost_is_resent_with_the_same_id_on_reconnect()
    {
        var m = new Market();
        var colony = TradeNetTests.Colony(9);
        var identity = TradeIdentity.Create("Sally");
        // The post was escrowed and reached the server, but the answer never came back (the link died).
        var give = new List<TradeGood> { TradeGood.Resource("stone", 10) };
        var want = new List<TradeGood> { TradeGood.Resource("wood", 5) };
        ColonyTrade.Escrow(colony, "mkt-post-lost", give, "Trading Post listing", new PendingMarketOp(true, Want: want));
        var raw = m.Hub.Connect();
        raw.Send(new MarketHello(Protocol.Version, "Sally", identity.Key));
        raw.Send(new MarketPost(1, "mkt-post-lost", give, want));
        raw.Close();
        Assert.Single(m.Host.Data.Listings);

        // On reconnect the client sends it again under the same id: the server answers "already listed", nothing doubles.
        Connect(m, "Sally", colony, identity);
        Assert.Single(m.Host.Data.Listings);
        Assert.Empty(colony.State.Trade.Escrows);
        Assert.Equal(20, colony.State.Stockpile.Get("stone"), 6);
    }

    [Fact]
    public void A_post_the_server_never_saw_is_sent_on_reconnect()
    {
        var m = new Market();
        var colony = TradeNetTests.Colony(9);
        ColonyTrade.Escrow(colony, "mkt-post-offline", [TradeGood.Resource("stone", 10)], "Trading Post listing",
            new PendingMarketOp(true, Want: [TradeGood.Resource("wood", 5)]));
        Connect(m, "Sally", colony);
        Assert.Equal("mkt-post-offline", m.Host.Data.Listings.Single().TxId);
        Assert.Empty(colony.State.Trade.Escrows);
    }

    [Fact]
    public void Expired_and_cancelled_listings_are_refunded()
    {
        var m = new Market();
        var seller = Connect(m, "Sally");
        Done(seller.Client.Post([TradeGood.Resource("wood", 10)], [TradeGood.Resource("scrap", 3)], hours: 1));
        Done(seller.Client.Post([TradeGood.Resource("stone", 10)], [TradeGood.Resource("scrap", 3)]));
        Assert.Equal(50, seller.Colony.State.Stockpile.Get("wood"), 6);

        m.Clock.Advance(3600);
        m.Host.Tick();
        Assert.Equal(60, seller.Colony.State.Stockpile.Get("wood"), 6);
        Assert.Contains(seller.Client.Received, c => c.Reason.Contains("expired"));

        var stone = Browse(seller).Single();
        var other = Connect(m, "Bram");
        Assert.False(Done(other.Client.Cancel(stone.Id)).Ok);
        Assert.False(Done(seller.Client.Accept(stone)).Ok, "own listing");
        Assert.Equal(50, seller.Colony.State.Stockpile.Get("scrap"), 6); // the refused accept was refunded
        Assert.True(Done(seller.Client.Cancel(stone.Id)).Ok);
        Assert.Equal(30, seller.Colony.State.Stockpile.Get("stone"), 6);
        Assert.Empty(m.Host.Data.Listings);
        Assert.Empty(m.Host.Data.Claims);
    }

    [Fact]
    public void Only_one_buyer_gets_a_listing_and_the_other_is_refunded()
    {
        var m = new Market();
        var seller = Connect(m, "Sally");
        var a = Connect(m, "Ann");
        var b = Connect(m, "Bea");
        Done(seller.Client.Post([TradeGood.Resource("wood", 10)], [TradeGood.Resource("scrap", 5)]));
        var listing = Browse(a).Single();
        Assert.True(Done(a.Client.Accept(listing)).Ok);
        var late = Done(b.Client.Accept(listing));
        Assert.False(late.Ok);
        Assert.Equal("gone", late.Code);
        Assert.Equal(50, b.Colony.State.Stockpile.Get("scrap"), 6);
        Assert.Empty(b.Colony.State.Trade.Escrows);
    }

    [Fact]
    public void The_market_survives_a_server_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"remnants-market-{Guid.NewGuid():N}.json");
        try
        {
            var m = new Market(path);
            var identity = TradeIdentity.Create("Sally");
            var sellerColony = TradeNetTests.Colony(3);
            var seller = Connect(m, "Sally", sellerColony, identity);
            Done(seller.Client.Post([TradeGood.Item("bp_rifle")], [TradeGood.Resource("electronics", 1)]));
            Done(seller.Client.Post([TradeGood.Resource("wood", 10)], [TradeGood.Resource("scrap", 2)]));
            seller.Link.Close();

            m.Restart(path);
            var buyer = Connect(m, "Bram");
            buyer.Colony.State.Stockpile.Add("electronics", 1);
            var listings = Browse(buyer);
            Assert.Equal(2, listings.Count);
            Assert.True(Done(buyer.Client.Accept(listings.Single(l => l.Give[0].Id == "bp_rifle"))).Ok);
            Assert.Equal(2, buyer.Colony.State.Inventory.Count("bp_rifle"));

            m.Restart(path);
            Assert.Single(m.Host.Data.Listings);
            Assert.Single(m.Host.Data.Claims); // Sally's electronics, waiting for her
            var back = Connect(m, "Sally", sellerColony, identity);
            Assert.Equal(1, sellerColony.State.Stockpile.Get("electronics"), 6);
            Assert.True(Browse(back).Single().Mine);

            m.Restart(path);
            Assert.Empty(m.Host.Data.Claims);
            Assert.Contains(m.Host.Data.Transactions.Keys, k => k.StartsWith(MarketClient.AcceptPrefix));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Posting_is_rate_limited_and_capped()
    {
        var m = new Market(rules: Rules with { Market = Rules.Market with { PostsPerMinute = 3, MaxOpenListings = 4 } });
        var p = Connect(m, "Sally");
        p.Colony.State.Stockpile.Add("wood", 100);
        MarketResult Post() => Done(p.Client.Post([TradeGood.Resource("wood", 1)], [TradeGood.Resource("scrap", 1)]));
        for (var i = 0; i < 3; i++) Assert.True(Post().Ok);
        var rate = Post();
        Assert.Equal("rate", rate.Code);
        m.Clock.Advance(61);
        Assert.True(Post().Ok);
        var cap = Post();
        Assert.Equal("cap", cap.Code);
        Assert.Equal(4, m.Host.Data.Listings.Count);
        Assert.Equal(156, p.Colony.State.Stockpile.Get("wood"), 6); // refused posts were refunded
        Assert.Empty(p.Colony.State.Trade.Escrows);
    }

    [Fact]
    public void Bad_or_suspicious_posts()
    {
        var m = new Market();
        var raw = m.Hub.Connect();
        raw.Send(new MarketHello(Protocol.Version, "Mallory", TradeIdentity.Create("M").Key));
        raw.Send(new MarketPost(1, "a", [TradeGood.Item("no_such_item")], [TradeGood.Resource("scrap", 1)]));
        raw.Send(new MarketPost(2, "b", [], [TradeGood.Resource("scrap", 1)]));
        raw.Send(new MarketPost(3, "c", [TradeGood.Resource("scrap", 100000)], [TradeGood.Resource("wood", 1)]));
        Assert.All(raw.Inbox.OfType<MarketResult>(), r => Assert.Equal("invalid", r.Code));
        raw.Send(new MarketPost(4, "d", [TradeGood.Resource("scrap", Rules.Limits.SuspiciousResourceAmount + 1)], [TradeGood.Resource("wood", 1)]));
        Assert.True(raw.Inbox.OfType<MarketResult>().Last().Ok);
        Assert.Contains(m.Log, l => l.Contains("suspicious post by Mallory"));

        var stale = m.Hub.Connect();
        stale.Send(new MarketHello(Protocol.Version - 1, "Old", TradeIdentity.Create("O").Key));
        Assert.Equal("version", stale.Inbox.OfType<MarketResult>().Single().Code);
        Assert.False(stale.IsOpen);
        var anon = m.Hub.Connect();
        anon.Send(new MarketHello(Protocol.Version, "Anon", "short"));
        Assert.False(anon.IsOpen);
    }

    /// <summary>
    /// The smoke script in one go: two players trade live in a lobby (colonies, escrow, courier home), then meet again
    /// on the Trading Post (post, accept, claim).
    /// </summary>
    [Fact]
    public void Loopback_script_two_players_trade_live_then_on_the_market()
    {
        var game = new NetFixture(new HostOptions { MinPlayers = 2, Trade = Rules, Resources = TradeNetTests.Game.Resources.Keys.ToList() });
        var alice = game.Join("Alice");
        var bob = game.Join("Bob");
        var aliceColony = TradeNetTests.Colony(11);
        var bobColony = TradeNetTests.Colony(12);
        using var aliceSync = new LiveTradeSync(alice.Client, () => aliceColony, Rules);
        using var bobSync = new LiveTradeSync(bob.Client, () => bobColony, Rules);

        alice.Client.RequestTrade(bob.Id);
        var id = bob.Last<TradeInvite>().TradeId;
        bob.Client.RespondTrade(id, true);
        alice.Client.OfferTrade(id, [TradeGood.Resource("food", 10)]);
        bob.Client.OfferTrade(id, [TradeGood.Resource("stone", 15)]);
        alice.Client.LockTrade(id, true);
        bob.Client.LockTrade(id, true);
        Assert.True(aliceSync.Confirm(id).Ok);
        Assert.True(bobSync.Confirm(id).Ok);
        aliceColony.Advance(aliceColony.Data.Rules.TicksPerDay * 4);
        bobColony.Advance(bobColony.Data.Rules.TicksPerDay * 4);
        Assert.Equal(45, aliceColony.State.Stockpile.Get("stone"), 6);
        Assert.Empty(aliceColony.State.Trade.Deliveries);

        var market = new Market();
        var a = Connect(market, "Alice", aliceColony);
        var b = Connect(market, "Bob", bobColony);
        Assert.True(Done(a.Client.Post([TradeGood.Item("bp_rifle")], [TradeGood.Resource("scrap", 20)])).Ok);
        Assert.True(Done(b.Client.Accept(Browse(b).Single())).Ok);
        Assert.Equal(2, bobColony.State.Inventory.Count("bp_rifle"));
        Assert.Equal(70, aliceColony.State.Stockpile.Get("scrap"), 6);
        Assert.Empty(market.Host.Data.Claims);
    }

    [Fact]
    public void A_retry_of_a_done_trade_is_answered_even_when_rate_limited()
    {
        var m = new Market(rules: Rules with { Market = Rules.Market with { RequestsPerMinute = 3 } });
        var raw = m.Hub.Connect();
        raw.Send(new MarketHello(Protocol.Version, "Sally", TradeIdentity.Create("Sally").Key));
        var post = new MarketPost(1, "mkt-post-r", [TradeGood.Resource("wood", 10)], [TradeGood.Resource("scrap", 3)]);
        raw.Send(post);
        raw.Send(new MarketList(2));
        raw.Send(new MarketList(3));
        raw.Send(post with { RequestId = 4 });
        var answer = raw.Inbox.OfType<MarketResult>().Last();
        Assert.Equal(4, answer.RequestId);
        Assert.True(answer.Ok);
        Assert.Single(m.Host.Data.Listings);
    }
}
