using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Trade;

namespace Remnants.Net.Tests;

/// <summary>
/// Live trades over loopback: the full lobby flow with two real colonies (escrow on confirm, one atomic commit, couriers
/// home), offer changes unlocking both sides, double confirms, drops and timeouts cancelling with refunds, nothing reaching
/// a third player, and field trades between allies executed on the server's sim.
/// </summary>
public class TradeNetTests
{
    private static readonly Lazy<GameData> GameLazy = new(() => GameDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")));
    public static GameData Game => GameLazy.Value;
    public static TradeRules Rules { get; } = TradeRules.Load(Path.Combine(AppContext.BaseDirectory, "data"));

    private static HostOptions Options(MissionMode mode = MissionMode.Versus, int players = 2) =>
        new() { Mode = mode, MinPlayers = players, TurnSeconds = 30, ReconnectSeconds = 60, Trade = Rules, Resources = Game.Resources.Keys.ToList() };

    public static ColonySim Colony(ulong seed)
    {
        var colony = NewGame.Create(Game, seed, NetFixture.Data);
        colony.State.Stockpile.Add("scrap", 50);
        colony.State.Inventory.Add("bp_rifle");
        return colony;
    }

    private sealed record Trader(TestPlayer Player, ColonySim Colony, LiveTradeSync Sync)
    {
        public NetClient Client => Player.Client;
        public TradeView Trade => Client.Trades.Values.Single();
        public int TradeId => Trade.TradeId;
    }

    private static Trader Seat(NetFixture f, string name, ulong seed)
    {
        var p = f.Join(name);
        var colony = Colony(seed);
        return new Trader(p, colony, new LiveTradeSync(p.Client, () => colony, Rules));
    }

    /// <summary>Alice invites Bob, Bob accepts; returns the trade id.</summary>
    private static int Open(Trader a, Trader b)
    {
        a.Client.RequestTrade(b.Player.Id);
        var invite = b.Player.Last<TradeInvite>();
        Assert.Equal(a.Player.Id, invite.FromPlayerId);
        b.Client.RespondTrade(invite.TradeId, true);
        Assert.True(a.Trade.Accepted);
        return invite.TradeId;
    }

    [Fact]
    public void Lobby_trade_commits_atomically_and_couriers_bring_the_goods_home()
    {
        var f = new NetFixture(Options(players: 3));
        var a = Seat(f, "Alice", 1);
        var b = Seat(f, "Bob", 2);
        var carol = f.Join("Carol");
        var id = Open(a, b);

        a.Client.OfferTrade(id, [TradeGood.Resource("scrap", 20)]);
        b.Client.OfferTrade(id, [TradeGood.Item("bp_rifle")]);
        var (me, them) = a.Client.SidesOf(a.Trade);
        Assert.Equal(20, me.Goods.Single().Count);
        Assert.Equal("bp_rifle", them.Goods.Single().Id);

        Assert.False(a.Sync.Confirm(id).Ok, "confirm needs both locked");
        Assert.Empty(a.Colony.State.Trade.Escrows);
        a.Client.LockTrade(id, true);
        b.Client.LockTrade(id, true);
        Assert.True(a.Sync.Confirm(id).Ok);
        Assert.Equal(30, a.Colony.State.Stockpile.Get("scrap"), 6); // escrowed
        Assert.Equal(0, f.Host.Trades.Committed);
        Assert.True(b.Sync.Confirm(id).Ok);

        Assert.Equal(1, f.Host.Trades.Committed);
        var ca = a.Player.Last<TradeCommit>();
        var cb = b.Player.Last<TradeCommit>();
        Assert.Equal(ca.TxId, cb.TxId);
        Assert.Equal(ca.Legs, cb.Legs);
        Assert.Equal("bp_rifle", ca.Get.Single().Id);
        Assert.Empty(a.Colony.State.Trade.Escrows);
        Assert.Empty(b.Colony.State.Trade.Escrows);
        Assert.Equal(0, b.Colony.State.Inventory.Count("bp_rifle"));
        Assert.Empty(a.Client.Trades);

        var delivery = a.Colony.State.Trade.Deliveries.Single();
        a.Colony.Advance((int)(delivery.ArriveTick - a.Colony.State.Tick));
        b.Colony.Advance((int)(b.Colony.State.Trade.Deliveries.Single().ArriveTick - b.Colony.State.Tick));
        Assert.Equal(2, a.Colony.State.Inventory.Count("bp_rifle"));
        Assert.Equal(70, b.Colony.State.Stockpile.Get("scrap"), 6);

        Assert.DoesNotContain(carol.Link.Inbox, m => m is TradeInvite or TradeUpdate or TradeCommit or TradeCancelled);
    }

    [Fact]
    public void Changing_an_offer_unlocks_both_and_refunds_a_confirmed_side()
    {
        var f = new NetFixture(Options());
        var a = Seat(f, "Alice", 1);
        var b = Seat(f, "Bob", 2);
        var id = Open(a, b);
        a.Client.OfferTrade(id, [TradeGood.Resource("scrap", 10)]);
        b.Client.OfferTrade(id, [TradeGood.Resource("wood", 10)]);
        a.Client.LockTrade(id, true);
        b.Client.LockTrade(id, true);
        Assert.True(a.Sync.Confirm(id).Ok);
        Assert.Single(a.Colony.State.Trade.Escrows);

        b.Client.OfferTrade(id, [TradeGood.Resource("wood", 5)]);
        Assert.All(a.Trade.Sides, s => Assert.False(s.Locked || s.Confirmed));
        Assert.Empty(a.Colony.State.Trade.Escrows);
        Assert.Equal(50, a.Colony.State.Stockpile.Get("scrap"), 6);
        Assert.Equal(0, f.Host.Trades.Committed);
    }

    [Fact]
    public void Confirming_twice_commits_once()
    {
        var f = new NetFixture(Options());
        var a = Seat(f, "Alice", 1);
        var b = Seat(f, "Bob", 2);
        var id = Open(a, b);
        a.Client.OfferTrade(id, [TradeGood.Resource("scrap", 10)]);
        a.Client.LockTrade(id, true);
        b.Client.LockTrade(id, true);
        a.Sync.Confirm(id);
        a.Client.ConfirmTrade(id);
        b.Sync.Confirm(id);
        b.Client.ConfirmTrade(id);
        a.Client.ConfirmTrade(id);

        Assert.Equal(1, f.Host.Trades.Committed);
        Assert.Single(a.Player.Link.Inbox.OfType<TradeCommit>());
        Assert.Single(b.Player.Link.Inbox.OfType<TradeCommit>());
        Assert.Single(b.Colony.State.Trade.Deliveries);
        Assert.Equal(40, a.Colony.State.Stockpile.Get("scrap"), 6);
    }

    [Fact]
    public void A_drop_mid_trade_cancels_and_refunds()
    {
        var f = new NetFixture(Options());
        var a = Seat(f, "Alice", 1);
        var b = Seat(f, "Bob", 2);
        var id = Open(a, b);
        a.Client.OfferTrade(id, [TradeGood.Resource("scrap", 25), TradeGood.Item("bp_rifle")]);
        a.Client.LockTrade(id, true);
        b.Client.LockTrade(id, true);
        a.Sync.Confirm(id);
        Assert.Equal(0, a.Colony.State.Inventory.Count("bp_rifle"));

        b.Player.Link.Close();
        Assert.Contains("disconnected", a.Player.Last<TradeCancelled>().Reason);
        Assert.Empty(a.Colony.State.Trade.Escrows);
        Assert.Equal(50, a.Colony.State.Stockpile.Get("scrap"), 6);
        Assert.Equal(1, a.Colony.State.Inventory.Count("bp_rifle"));
        Assert.Equal(0, f.Host.Trades.Open);
        Assert.Equal(0, f.Host.Trades.Committed);
    }

    [Fact]
    public void An_idle_trade_times_out_with_a_refund()
    {
        var f = new NetFixture(Options());
        var a = Seat(f, "Alice", 1);
        var b = Seat(f, "Bob", 2);
        var id = Open(a, b);
        a.Client.OfferTrade(id, [TradeGood.Resource("scrap", 5)]);
        a.Client.LockTrade(id, true);
        b.Client.LockTrade(id, true);
        a.Sync.Confirm(id);

        f.Clock.Advance(Rules.Live.IdleSeconds - 1);
        f.Host.Tick();
        Assert.Single(a.Client.Trades);
        f.Clock.Advance(2);
        f.Host.Tick();
        Assert.Contains("timed out", a.Player.Last<TradeCancelled>().Reason);
        Assert.Contains("timed out", b.Player.Last<TradeCancelled>().Reason);
        Assert.Equal(50, a.Colony.State.Stockpile.Get("scrap"), 6);
    }

    [Fact]
    public void Bad_offers_are_refused_and_the_table_stays_as_it_was()
    {
        var f = new NetFixture(Options());
        var a = Seat(f, "Alice", 1);
        var b = Seat(f, "Bob", 2);
        var id = Open(a, b);
        a.Client.OfferTrade(id, [TradeGood.Resource("scrap", 10)]);
        a.Client.OfferTrade(id, [TradeGood.Item("no_such_thing")]);
        a.Client.OfferTrade(id, [TradeGood.Resource("scrap", Rules.Limits.MaxResourceAmount + 1)]);
        Assert.Contains("Offer refused", a.Player.Last<Notice>().Text);
        Assert.Equal(10, a.Client.SidesOf(a.Trade).Me.Goods.Single().Count);
        Assert.Equal(10, b.Client.SidesOf(b.Trade).Them.Goods.Single().Count);
    }

    [Fact]
    public void A_declined_invite_closes_for_both_and_strangers_cannot_meddle()
    {
        var f = new NetFixture(Options(players: 3));
        var a = Seat(f, "Alice", 1);
        var b = Seat(f, "Bob", 2);
        var carol = f.Join("Carol");
        a.Client.RequestTrade(b.Player.Id);
        var id = b.Player.Last<TradeInvite>().TradeId;
        carol.Client.OfferTrade(id, [TradeGood.Resource("scrap", 1)]);
        carol.Client.CancelTrade(id);
        Assert.Single(a.Client.Trades);
        b.Client.RespondTrade(id, false);
        Assert.Contains("declined", a.Player.Last<TradeCancelled>().Reason);
        Assert.Empty(a.Client.Trades);
        Assert.Empty(carol.Link.Inbox.OfType<TradeUpdate>());
    }

    [Fact]
    public void Lobby_trades_end_when_the_mission_starts()
    {
        var f = new NetFixture(Options());
        var a = Seat(f, "Alice", 1);
        var b = Seat(f, "Bob", 2);
        var id = Open(a, b);
        a.Client.OfferTrade(id, [TradeGood.Resource("scrap", 5)]);
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        Assert.Equal(HostPhase.Playing, f.Host.Phase);
        Assert.Contains("mission started", a.Player.Last<TradeCancelled>().Reason);
    }

    [Fact]
    public void Allies_trade_pack_items_in_the_field_and_rivals_cannot()
    {
        var f = new NetFixture(Options(MissionMode.Coop));
        var (a, b) = f.StartTwo();
        var aItem = f.Sim.State.Units.First(u => u.PlayerId == a.Id && u.Pack.Count > 0).Pack[0];

        a.Client.RequestTrade(b.Id);
        var invite = b.Last<TradeInvite>();
        Assert.Equal(TradeVenue.Field, invite.Venue);
        b.Client.RespondTrade(invite.TradeId, true);
        a.Client.OfferTrade(invite.TradeId, [TradeGood.Resource("scrap", 1)]);
        Assert.Contains("packs", a.Last<Notice>().Text);
        a.Client.OfferTrade(invite.TradeId, [TradeGood.Item(aItem)]);
        a.Client.LockTrade(invite.TradeId, true);
        b.Client.LockTrade(invite.TradeId, true);
        a.Client.ConfirmTrade(invite.TradeId);
        var aCount = f.Sim.State.Units.Where(u => u.PlayerId == a.Id).Sum(u => u.Pack.Count);
        b.Client.ConfirmTrade(invite.TradeId);

        Assert.Equal(TradeVenue.Field, b.Last<TradeCommit>().Venue);
        Assert.Equal(aCount - 1, f.Sim.State.Units.Where(u => u.PlayerId == a.Id).Sum(u => u.Pack.Count));
        Assert.Contains(b.View.Units.Where(u => u.PlayerId == b.Id), u => u.Pack.Contains(aItem));
        f.AssertNoLeaks();

        var v = new NetFixture(Options());
        var (x, y) = v.StartTwo();
        x.Client.RequestTrade(y.Id);
        Assert.Contains("allies", x.Last<TradeCancelled>().Reason);
        Assert.Empty(y.Link.Inbox.OfType<TradeInvite>());
    }

    [Fact]
    public void A_confirm_overtaken_by_the_other_sides_confirm_keeps_its_escrow()
    {
        var f = new NetFixture(Options());
        var a = Seat(f, "Alice", 1);
        var b = Seat(f, "Bob", 2);
        var id = Open(a, b);
        a.Client.OfferTrade(id, [TradeGood.Resource("scrap", 20)]);
        b.Client.OfferTrade(id, [TradeGood.Resource("wood", 10)]);
        a.Client.LockTrade(id, true);
        b.Client.LockTrade(id, true);
        // Alice escrows and her confirm is in flight while Bob's confirm reaches the server first.
        Assert.True(ColonyTrade.Escrow(a.Colony, a.Sync.EscrowId(id), [TradeGood.Resource("scrap", 20)], "trade").Ok);
        Assert.True(b.Sync.Confirm(id).Ok);
        Assert.Single(a.Colony.State.Trade.Escrows); // the update without her confirm must not refund
        a.Client.ConfirmTrade(id);

        Assert.Equal(1, f.Host.Trades.Committed);
        Assert.Empty(a.Colony.State.Trade.Escrows);
        Assert.Equal(30, a.Colony.State.Stockpile.Get("scrap"), 6); // paid exactly once
    }
}
