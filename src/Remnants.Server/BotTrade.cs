using System.Globalization;
using System.Threading.Channels;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Trade;
using Remnants.Net;

namespace Remnants.Server;

/// <summary>
/// Scripted trading for smoke tests and screenshots.
/// <list type="bullet">
/// <item><c>--bot ADDR --trade [--trade-offer "scrap:20,bandage"]</c>: the mission bot also trades. It accepts every
/// invite, puts its offer on the table (lobby: the given goods, default 20 scrap and a bandage, trusted like any lobby
/// offer; field: one item from its packs), locks once the other side has locked, and confirms once both are locked.</item>
/// <item><c>--market-bot ADDR --name N [--listings 3] [--take]</c>: connects to a Trading Post with a fresh colony,
/// posts a few listings out of its stock, with <c>--take</c> also takes the first listing it can pay for, applies its
/// claims, then stays until Ctrl+C (or <c>--seconds S</c>).</item>
/// </list>
/// </summary>
internal static class BotTrade
{
    public static List<TradeGood> ParseGoods(string spec, GameData game, ScavengeData data) =>
        spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(part =>
        {
            var bits = part.Split(':');
            var count = bits.Length > 1 ? int.Parse(bits[1], CultureInfo.InvariantCulture) : 1;
            return game.Resources.ContainsKey(bits[0]) ? TradeGood.Resource(bits[0], count)
                : data.Items.ContainsKey(bits[0]) ? TradeGood.Item(bits[0], count)
                : throw new ArgumentException($"Unknown good '{bits[0]}'.");
        }).ToList();

    /// <summary>Hooks the trading bot into a mission bot's client (call on its message thread).</summary>
    public static void Attach(NetClient client, string name, List<TradeGood> lobbyOffer)
    {
        var offered = new HashSet<int>();
        client.TradeNews += message =>
        {
            switch (message)
            {
                case TradeInvite i:
                    Program.Print($"{name}: trade invite from {i.FromName}; accepting");
                    client.RespondTrade(i.TradeId, true);
                    break;
                case TradeUpdate { Trade: { Accepted: true } t }:
                    var (me, them) = client.SidesOf(t);
                    if (offered.Add(t.TradeId))
                    {
                        var goods = t.Venue == TradeVenue.Lobby ? lobbyOffer : FieldOffer(client);
                        client.OfferTrade(t.TradeId, goods);
                        Program.Print($"{name}: offering {string.Join(", ", goods.Select(g => $"{g.Count} {g.Id}"))}");
                    }
                    else if (them.Locked && !me.Locked && me.Goods.Count > 0) client.LockTrade(t.TradeId, true);
                    else if (me.Locked && them.Locked && !me.Confirmed) client.ConfirmTrade(t.TradeId);
                    break;
                case TradeCommit c:
                    Program.Print($"{name}: trade with {c.PartnerName} done: gave {c.Give.Count} lines, got {string.Join(", ", c.Get.Select(g => $"{g.Count} {g.Id}"))}");
                    break;
                case TradeCancelled c when c.TradeId != 0:
                    Program.Print($"{name}: trade {c.TradeId} cancelled: {c.Reason}");
                    break;
            }
        };
    }

    private static List<TradeGood> FieldOffer(NetClient client)
    {
        var item = client.View?.State.Units.Where(u => u.PlayerId == client.PlayerId && u.OnMap).SelectMany(u => u.Pack).FirstOrDefault();
        return item is null ? [] : [TradeGood.Item(item)];
    }

    public static async Task<int> RunMarket(string target, Args a, ScavengeData data, GameData game)
    {
        var colon = target.LastIndexOf(':');
        var host = colon > 0 ? target[..colon] : target;
        var port = colon > 0 ? int.Parse(target[(colon + 1)..], CultureInfo.InvariantCulture) : Program.DefaultMarketPort;
        var name = a.Value("--name") ?? "Trader";
        var listings = a.Int("--listings", 3);
        var seconds = a.Double("--seconds", 0);

        var colony = NewGame.Create(game, (ulong)name.Sum(c => c), data);
        colony.State.Stockpile.Add("scrap", 80);
        colony.State.Stockpile.Add("electronics", 12);
        colony.State.Stockpile.Add("cloth", 20);
        colony.State.Inventory.Add("bp_rifle");

        var inbox = Channel.CreateUnbounded<Action>();
        var transport = await TcpClientTransport.ConnectAsync(host, port);
        var market = new MarketClient(transport, () => colony, work => inbox.Writer.TryWrite(work));
        market.Claimed += claims => Program.Print($"{name}: claimed {string.Join("; ", claims.Select(c => c.Reason))}");
        transport.Start();
        market.Hello(TradeIdentity.Create(name));
        Program.Print($"{name}: at the Trading Post {target}");

        var offers = new List<(List<TradeGood> Give, List<TradeGood> Want)>
        {
            ([TradeGood.Resource("wood", 30)], [TradeGood.Resource("scrap", 12)]),
            ([TradeGood.Item("bp_rifle")], [TradeGood.Resource("electronics", 6), TradeGood.Resource("medicine", 2)]),
            ([TradeGood.Resource("scrap", 25)], [TradeGood.Resource("food", 30)]),
            ([TradeGood.Item("bandage", 2)], [TradeGood.Resource("cloth", 6)]),
            ([TradeGood.Resource("stone", 20)], [TradeGood.Resource("wood", 15)]),
        };
        var stage = 0;
        var busy = false;
        var deadline = seconds > 0 ? DateTime.UtcNow.AddSeconds(seconds) : DateTime.MaxValue;
        // Answers arrive through this same loop, so requests are chained with continuations, never awaited inside it.
        void Then<T>(Task<T> task, Action<T> done) => task.ContinueWith(t => inbox.Writer.TryWrite(() =>
        {
            busy = false;
            if (t.IsCompletedSuccessfully) done(t.Result);
        }));
        using var timer = new Timer(_ => inbox.Writer.TryWrite(() => { }), null, 500, 500);
        await foreach (var work in inbox.Reader.ReadAllAsync())
        {
            work();
            if (!market.Connected || market.Error is not null || DateTime.UtcNow >= deadline) break;
            if (market.Welcome is null || busy || stage < 0) continue;
            busy = true;
            if (stage < Math.Min(listings, offers.Count))
            {
                var (give, want) = offers[stage++];
                Then(market.Post(give, want), r => Program.Print($"{name}: post {(r.Ok ? $"#{r.Listing?.Id}" : "refused")}: {r.Message}"));
            }
            else if (a.Flag("--take"))
            {
                stage = -1;
                Then(market.Browse(), page =>
                {
                    if (page.Listings.FirstOrDefault(l => !l.Mine && TradeGoods.Has(colony.State, l.Want)) is not { } listing) return;
                    busy = true;
                    Then(market.Accept(listing), r => Program.Print($"{name}: take #{listing.Id}: {r.Message}"));
                });
            }
            else
            {
                stage = -1;
                busy = false;
            }
        }
        Program.Print($"{name}: {market.Error ?? "leaving the Trading Post"}");
        market.Close();
        return market.Error is null ? 0 : 2;
    }
}
