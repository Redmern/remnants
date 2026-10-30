using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Trade;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Player trading in Core: goods leave a colony into escrow and are settled or refunded, deliveries and claims apply
/// once per transaction id, couriers arrive after their plan's days, schematic copies keep the seller's knowledge, field
/// trades swap pack items atomically between allies, and offers are validated against the data.
/// </summary>
public class TradeTests
{
    private static readonly Lazy<TradeRules> RulesLazy = new(() => TradeRules.Load(Path.Combine(AppContext.BaseDirectory, "data")));
    private static TradeRules Rules => RulesLazy.Value;

    private static ColonySim Colony(Dictionary<string, double>? stock = null, params (string Item, int Count)[] items)
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = 1 });
        foreach (var (res, amount) in stock ?? new()) state.Stockpile.Add(res, amount);
        foreach (var (item, count) in items) state.Inventory.Add(item, count);
        return new ColonySim(TestData.Real, state, Real);
    }

    [Fact]
    public void Escrow_takes_goods_out_and_refund_puts_them_back()
    {
        var colony = Colony(new() { ["scrap"] = 30 }, ("bandage", 2));
        var goods = new List<TradeGood> { TradeGood.Resource("scrap", 20), TradeGood.Item("bandage") };

        Assert.True(ColonyTrade.Escrow(colony, "tx1", goods, "test").Ok);
        Assert.Equal(10, colony.State.Stockpile.Get("scrap"), 6);
        Assert.Equal(1, colony.State.Inventory.Count("bandage"));
        Assert.True(ColonyTrade.Escrow(colony, "tx1", goods, "test").Ok); // same id again: nothing more taken
        Assert.Equal(10, colony.State.Stockpile.Get("scrap"), 6);

        Assert.True(ColonyTrade.Refund(colony, "tx1"));
        Assert.False(ColonyTrade.Refund(colony, "tx1"));
        Assert.Equal(30, colony.State.Stockpile.Get("scrap"), 6);
        Assert.Equal(2, colony.State.Inventory.Count("bandage"));
    }

    [Fact]
    public void Escrow_of_goods_the_colony_lacks_takes_nothing()
    {
        var colony = Colony(new() { ["scrap"] = 5 }, ("bandage", 1));
        var result = ColonyTrade.Escrow(colony, "tx", [TradeGood.Item("bandage"), TradeGood.Resource("scrap", 6)], "test");
        Assert.False(result.Ok);
        Assert.Equal(5, colony.State.Stockpile.Get("scrap"), 6);
        Assert.Equal(1, colony.State.Inventory.Count("bandage"));
        Assert.Empty(colony.State.Trade.Escrows);
    }

    [Fact]
    public void Settled_escrow_is_gone_for_good()
    {
        var colony = Colony(new() { ["wood"] = 40 });
        ColonyTrade.Escrow(colony, "tx", [TradeGood.Resource("wood", 40)], "test");
        Assert.True(ColonyTrade.Settle(colony, "tx"));
        Assert.False(ColonyTrade.Refund(colony, "tx"));
        Assert.Equal(0, colony.State.Stockpile.Get("wood"), 6);
    }

    [Fact]
    public void Receive_applies_a_transaction_once()
    {
        var colony = Colony();
        var goods = new List<TradeGood> { TradeGood.Resource("electronics", 4), TradeGood.Item("pistol") };
        Assert.True(ColonyTrade.Receive(colony, "claim-1", goods, "Bob"));
        Assert.False(ColonyTrade.Receive(colony, "claim-1", goods, "Bob"));
        Assert.Equal(4, colony.State.Stockpile.Get("electronics"), 6);
        Assert.Equal(1, colony.State.Inventory.Count("pistol"));
        Assert.Contains(colony.State.Events, e => e.Type == ColonyEventType.TradeReceived);
    }

    [Fact]
    public void Quality_variants_trade_as_their_own_item()
    {
        var fine = Real.QualityVariant("pistol", Real.Rules.Quality.First(q => !q.Base).Id);
        var colony = Colony(null, (fine, 1));
        Assert.True(ColonyTrade.Escrow(colony, "tx", [TradeGood.Item(fine)], "test").Ok);
        var other = Colony();
        ColonyTrade.Receive(other, "tx", [TradeGood.Item(fine)], "seller");
        Assert.Equal(1, other.State.Inventory.Count(fine));
        Assert.Equal(0, other.State.Inventory.Count("pistol"));
    }

    [Fact]
    public void Courier_arrives_after_its_days_and_only_once()
    {
        var colony = Colony();
        var arrived = new List<CourierDelivery>();
        colony.CourierArrived += arrived.Add;
        var plan = Courier.Plan(colony, Rules, legs: 2);
        Assert.True(ColonyTrade.Dispatch(colony, "live-1", [TradeGood.Resource("cloth", 12)], "Bob", plan.Days));
        Assert.False(ColonyTrade.Dispatch(colony, "live-1", [TradeGood.Resource("cloth", 12)], "Bob", plan.Days));

        var ticks = (int)Math.Round(plan.Days * colony.Data.Rules.TicksPerDay);
        colony.Advance(ticks - 1);
        Assert.Empty(arrived);
        colony.Advance(1);
        Assert.Single(arrived);
        Assert.Equal(12, colony.State.Stockpile.Get("cloth") - 0, 0);
        Assert.Empty(colony.State.Trade.Deliveries);
        Assert.False(ColonyTrade.Receive(colony, "live-1", [TradeGood.Resource("cloth", 12)], "Bob"), "delivered id is spent");
    }

    [Fact]
    public void Courier_time_grows_with_distance_and_shrinks_with_modifiers()
    {
        var colony = Colony();
        var near = Courier.Plan(colony, Rules, 1);
        var far = Courier.Plan(colony, Rules, 4);
        Assert.True(far.Days > near.Days);

        Courier.Modifiers.Add(Caravan);
        try
        {
            var faster = Courier.Plan(colony, Rules, 4);
            Assert.Equal(far.Days * 0.5, faster.Days, 2);
            Assert.Contains(faster.Factors, f => f.Label == "Caravan");
        }
        finally
        {
            Courier.Modifiers.Remove(Caravan);
        }

        static CourierFactor? Caravan(ColonySim _) => new("Caravan", 0.5);
    }

    [Fact]
    public void Courier_legs_are_symmetric_and_in_range()
    {
        for (var i = 0; i < 50; i++)
        {
            var legs = Courier.Legs($"Alice{i}", "Bob", 4);
            Assert.InRange(legs, 1, 4);
            Assert.Equal(legs, Courier.Legs("Bob", $"Alice{i}", 4));
        }
    }

    [Fact]
    public void A_schematic_copy_keeps_the_sellers_knowledge_and_the_buyer_learns_it_on_arrival()
    {
        var seller = Colony(new() { ["research_data"] = 10 });
        Schematics.Schematics.Grant(seller, "sch_rifle", Schematics.SchematicSource.Debug);
        Assert.True(seller.Execute(new Schematics.CopySchematic("sch_rifle")).Ok);
        Assert.True(Schematics.Schematics.Has(seller, "sch_rifle"));
        Assert.Equal(1, seller.State.Inventory.Count("sch_rifle"));

        Assert.True(ColonyTrade.Escrow(seller, "tx", [TradeGood.Item("sch_rifle")], "test").Ok);
        ColonyTrade.Settle(seller, "tx");
        var buyer = Colony();
        Assert.False(Schematics.Schematics.Has(buyer, "sch_rifle"));
        ColonyTrade.Receive(buyer, "tx", [TradeGood.Item("sch_rifle")], "seller");
        Assert.True(Schematics.Schematics.Has(buyer, "sch_rifle"), "a plain copy is learned when it arrives");
        Assert.True(Schematics.Schematics.Has(seller, "sch_rifle"));
    }

    [Fact]
    public void A_refunded_schematic_copy_comes_back_as_the_item()
    {
        var colony = Colony(null, ("sch_rifle", 1));
        ColonyTrade.Escrow(colony, "tx", [TradeGood.Item("sch_rifle")], "test");
        ColonyTrade.Refund(colony, "tx");
        Assert.Equal(1, colony.State.Inventory.Count("sch_rifle"));
        Assert.False(Schematics.Schematics.Has(colony, "sch_rifle"), "a refund is not a purchase");
    }

    [Fact]
    public void Food_trades_with_its_lots_and_spoilage()
    {
        var seller = Colony();
        var data = seller.Data;
        Pantry.Add(seller.State, data, "vegetables", 3, quality: "fine");
        seller.Advance(data.Rules.TicksPerDay * 2); // two days older
        Pantry.Add(seller.State, data, "vegetables", 2);
        var offer = FoodGoods.WithLots(seller.State, data, TradeGood.Item("vegetables", 4));
        Assert.Equal(4, offer.Extras!.Lots!.Sum(l => l.Count));
        Assert.Null(TradeGoods.Validate([offer], Rules, data, Real));

        Assert.True(ColonyTrade.Escrow(seller, "tx", [TradeGood.Item("vegetables", 4)], "test").Ok);
        var held = seller.State.Trade.Escrows.Single().Goods.Single();
        Assert.Equal(3, held.Extras!.Lots!.First().Count); // the older lot goes first
        Assert.Equal("fine", held.Extras.Lots!.First().Quality);
        Assert.Equal(1, Pantry.Available(seller.State, data, "vegetables"));
        ColonyTrade.Settle(seller, "tx");

        var buyer = Colony();
        ColonyTrade.Receive(buyer, "tx", [held], "seller");
        var lots = buyer.State.Food.Lots["vegetables"];
        Assert.Equal(4, lots.Sum(l => l.Count));
        var fine = lots.Single(l => l.Quality == "fine");
        Assert.Equal(held.Extras.Lots!.First().TicksLeft!.Value, fine.TicksLeft!.Value, 3); // same time left, not fresh
    }

    [Fact]
    public void Spoiled_food_cannot_be_traded()
    {
        var colony = Colony();
        Pantry.Add(colony.State, colony.Data, "mushrooms", 2);
        colony.Advance(colony.Data.Rules.TicksPerDay * 30);
        Assert.Equal(0, FoodGoods.Have(colony.State, colony.Data, TradeGood.Item("mushrooms")));
        Assert.False(ColonyTrade.Escrow(colony, "tx", [TradeGood.Item("mushrooms")], "test").Ok);
    }

    [Fact]
    public void Offers_are_validated_against_the_data_and_caps()
    {
        string? Check(params TradeGood[] goods) => TradeGoods.Validate(goods, Rules, TestData.Real, Real);
        Assert.Null(Check(TradeGood.Resource("scrap", 10), TradeGood.Item("pistol")));
        Assert.Null(Check());
        Assert.NotNull(Check(TradeGood.Resource("unobtainium", 1)));
        Assert.NotNull(Check(TradeGood.Item("no_such_item")));
        Assert.NotNull(Check(TradeGood.Resource("scrap", 0)));
        Assert.NotNull(Check(TradeGood.Resource("scrap", -5)));
        Assert.NotNull(Check(TradeGood.Resource("scrap", Rules.Limits.MaxResourceAmount + 1)));
        Assert.NotNull(Check(TradeGood.Item("pistol", Rules.Limits.MaxItemCount + 1)));
        Assert.NotNull(Check(TradeGood.Resource("scrap", 1), TradeGood.Resource("scrap", 2)));
        Assert.NotNull(Check(Enumerable.Range(0, Rules.Limits.MaxLines + 1).Select(i => TradeGood.Resource("scrap", i + 1)).ToArray()));
        var natural = Real.Items.Values.First(i => i.Natural).Id;
        Assert.NotNull(Check(TradeGood.Item(natural)));
        Assert.NotNull(TradeGoods.Suspicious([TradeGood.Resource("scrap", Rules.Limits.SuspiciousResourceAmount + 1)], Rules));
        Assert.Null(TradeGoods.Suspicious([TradeGood.Resource("scrap", 10)], Rules));
    }

    [Fact]
    public void Item_extras_survive_the_wire_format_and_keep_modded_items_apart()
    {
        var modded = TradeGood.Item("pistol") with { Extras = new ItemExtras(["scope"], 2) };
        var lines = TradeGoods.Normalize([modded, TradeGood.Item("pistol"), TradeGood.Item("pistol")]);
        Assert.Equal(2, lines.Count);
        var json = System.Text.Json.JsonSerializer.Serialize(lines, Json.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<List<TradeGood>>(json, Json.Options)!;
        var withMods = back.Single(g => g.Extras is { IsEmpty: false });
        Assert.Equal(["scope"], withMods.Extras!.Mods!);
        Assert.Equal(2, withMods.Extras.Mk);
    }

    [Fact]
    public void Reference_values_rank_goods_sensibly()
    {
        double V(TradeGood g) => TradePrices.Of(g, Rules, TestData.Real, Real);
        Assert.Equal(10, V(TradeGood.Resource("scrap", 10)), 6);
        Assert.True(V(TradeGood.Resource("electronics", 1)) > V(TradeGood.Resource("wood", 1)));
        Assert.True(V(TradeGood.Item("sch_rifle")) > 0);
        var best = Real.Rules.Quality.OrderByDescending(q => q.StatMultiplier).First();
        Assert.True(V(TradeGood.Item(Real.QualityVariant("pistol", best.Id))) >= V(TradeGood.Item("pistol")));
    }

    [Fact]
    public void Trade_state_survives_a_save()
    {
        var colony = Colony(new() { ["scrap"] = 20 });
        ColonyTrade.Escrow(colony, "mkt-post-1", [TradeGood.Resource("scrap", 5)], "Trading Post",
            new PendingMarketOp(true, Want: [TradeGood.Resource("wood", 10)], Hours: 24));
        ColonyTrade.Dispatch(colony, "live-9", [TradeGood.Item("pistol")], "Bob", 1.5);
        ColonyTrade.Receive(colony, "claim-7", [TradeGood.Resource("cloth", 1)], "x");

        var loaded = SaveGame.Load(TestData.Real, SaveGame.Save(colony.State), Real);
        var trade = loaded.State.Trade;
        Assert.Equal("mkt-post-1", trade.Escrows.Single().TxId);
        Assert.Equal(10, trade.Escrows.Single().Market!.Want!.Single().Count);
        Assert.Equal("Bob", trade.Deliveries.Single().From);
        Assert.True(trade.WasApplied("claim-7"));
    }

    [Fact]
    public void Live_escrows_are_refunded_on_load_but_market_ones_kept()
    {
        var colony = Colony(new() { ["scrap"] = 20 });
        ColonyTrade.Escrow(colony, "live:abc:1", [TradeGood.Resource("scrap", 5)], "trade");
        ColonyTrade.Escrow(colony, "mkt-post-1", [TradeGood.Resource("scrap", 5)], "post");
        Assert.Equal(1, ColonyTrade.RefundAll(colony, "live:"));
        Assert.Equal(15, colony.State.Stockpile.Get("scrap"), 6);
        Assert.Single(colony.State.Trade.Escrows);
    }

    // ---- field trades ----

    private static ScavengeSim AlliedPair(out Unit a, out Unit b)
    {
        a = SurvivorUnit(1, new(1, 1), weapon: "pipe", pack: ["bandage", "med_kit"]);
        b = SurvivorUnit(2, new(5, 1), weapon: "pipe", pack: ["scrap"]);
        var sim = Arena(10, 6, [a, b]);
        Alliances.Merge(sim.State, 1, 2);
        return sim;
    }

    [Fact]
    public void Allies_swap_pack_items_atomically()
    {
        var b0 = "scrap";
        var sim = AlliedPair(out var a, out var b);
        b.Pack.Clear();
        b.Pack.Add(b0);

        var result = FieldTrade.Swap(sim, 1, [TradeGood.Item("bandage")], 2, [TradeGood.Item(b0)]);
        Assert.True(result.Ok, result.Message);
        Assert.DoesNotContain("bandage", a.Pack);
        Assert.Contains(b0, a.Pack);
        Assert.Contains("bandage", b.Pack);
        Assert.DoesNotContain(b0, b.Pack);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.ItemGiven && e.Sides is { Count: > 0 });
    }

    [Fact]
    public void A_field_trade_fails_whole_when_one_side_lacks_its_items()
    {
        var sim = AlliedPair(out var a, out var b);
        var (aBefore, bBefore) = (a.Pack.ToList(), b.Pack.ToList());
        var result = FieldTrade.Swap(sim, 1, [TradeGood.Item("bandage")], 2, [TradeGood.Item("med_kit")]);
        Assert.False(result.Ok);
        Assert.Equal(aBefore, a.Pack);
        Assert.Equal(bBefore, b.Pack);
    }

    [Fact]
    public void Only_allies_trade_in_the_field_and_only_items()
    {
        var a = SurvivorUnit(1, new(1, 1), pack: ["bandage"]);
        var b = SurvivorUnit(2, new(5, 1), pack: ["bandage"]);
        var sim = Arena(10, 6, [a, b]);
        Assert.NotNull(FieldTrade.CheckPair(sim, 1, 2));
        Assert.False(FieldTrade.Swap(sim, 1, [TradeGood.Item("bandage")], 2, []).Ok);
        Alliances.Merge(sim.State, 1, 2);
        Assert.Null(FieldTrade.CheckPair(sim, 1, 2));
        Assert.NotNull(FieldTrade.CheckOffer(sim, 1, [TradeGood.Resource("scrap", 1)]));
        Assert.NotNull(FieldTrade.CheckOffer(sim, 1, [TradeGood.Item("bandage", 2)]));
    }

    [Fact]
    public void A_field_trade_respects_carry_limits()
    {
        var sim = AlliedPair(out var a, out var b);
        var heavy = Real.Items.Values.Where(i => !i.Natural && i.BaseId is null).OrderByDescending(i => i.Weight).First();
        var cap = sim.Profile(b).CarryCapacity;
        for (var w = 0.0; w <= cap * 2; w += heavy.Weight) a.Pack.Add(heavy.Id);
        var offer = new List<TradeGood> { TradeGood.Item(heavy.Id, a.Pack.Count(id => id == heavy.Id)) };
        var result = FieldTrade.Swap(sim, 1, offer, 2, []);
        Assert.False(result.Ok);
        Assert.Equal(ScavengeError.CarryLimitExceeded, result.Error);
        Assert.Contains(heavy.Id, a.Pack);
    }

    [Fact]
    public void Escrow_checks_the_total_of_lines_sharing_an_item()
    {
        var colony = Colony(null, ("pistol", 1));
        var a = TradeGood.Item("pistol") with { Extras = new ItemExtras(["scope"]) };
        var b = TradeGood.Item("pistol") with { Extras = new ItemExtras(["grip"]) };
        Assert.False(ColonyTrade.Escrow(colony, "tx", [a, b], "test").Ok);
        Assert.Equal(1, colony.State.Inventory.Count("pistol"));
        Assert.Empty(colony.State.Trade.Escrows);
    }
}
