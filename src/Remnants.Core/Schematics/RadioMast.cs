using Remnants.Core.Colony;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Schematics;

/// <summary>
/// The Radio Mast: how the colony reaches the outside world. It keeps a queue of incoming calls (traders, contract offers,
/// and in Story mode the characters, queued with <see cref="Enqueue"/>), schedules the trader's visits and offers radio
/// contracts. The trader is data (<c>schematics.json</c> <c>traders</c>); Story mode swaps its own in with <see cref="SetTrader"/>.
/// </summary>
public static class RadioMast
{
    /// <summary>The colony's Radio Mast with the highest level, if it has one.</summary>
    public static Building? Mast(ColonySim colony) =>
        colony.State.Buildings.Where(b => b.DefId == colony.Data.Schematics.Rules.Radio.Building).MaxBy(b => b.Level);

    public static bool HasMast(ColonySim colony) => Mast(colony) is not null;

    /// <summary>The calls waiting to be answered, oldest first.</summary>
    public static IReadOnlyList<RadioCall> Calls(ColonySim colony) => colony.State.Radio.Calls;

    /// <summary>
    /// Queue a call (Story mode: a character calls). It waits until answered (<see cref="AnswerCall"/>) or, with
    /// <paramref name="expiresInTicks"/>, until it expires. Raises <see cref="ColonySim.RadioCallReceived"/>.
    /// </summary>
    public static RadioCall Enqueue(ColonySim colony, RadioCallKind kind, string callerId, string callerName, string text,
        string? tag = null, long? expiresInTicks = null)
    {
        var state = colony.State;
        var call = new RadioCall
        {
            Id = state.Radio.NextCallId++, Kind = kind, CallerId = callerId, CallerName = callerName, Text = text, Tag = tag,
            Tick = state.Tick, ExpiresTick = expiresInTicks is { } t ? state.Tick + t : null,
        };
        state.Radio.Calls.Add(call);
        state.Log(new(state.Tick, ColonyEventType.RadioCall, $"{callerName} is calling on the radio."));
        colony.RaiseRadioCallReceived(call);
        return call;
    }

    /// <summary>
    /// Swap the trader (Story mode: Marrow's caravan). <paramref name="unlocked"/> opens trading now, whatever the
    /// trader's <c>requiresScouted</c> says. A trader already parked outside keeps its stock until it leaves.
    /// </summary>
    public static void SetTrader(ColonySim colony, string traderId, bool unlocked = false)
    {
        if (!colony.Data.Schematics.Traders.ContainsKey(traderId)) throw new ArgumentException($"Unknown trader '{traderId}'.", nameof(traderId));
        colony.State.Radio.TraderId = traderId;
        colony.State.Radio.TraderUnlocked |= unlocked;
    }

    /// <summary>The trader who visits this colony.</summary>
    public static TraderDef? Trader(ColonySim colony) => colony.Data.Schematics.Trader(colony.State.Radio.TraderId);

    /// <summary>Whether the trader visits yet: a Radio Mast, and its zone scouted (or a story unlocked it).</summary>
    public static bool TraderAvailable(ColonySim colony)
    {
        if (!HasMast(colony) || Trader(colony) is not { } trader) return false;
        if (colony.State.Radio.TraderUnlocked || trader.RequiresScouted is null) return true;
        return colony.Scavenge?.Zones.ContainsKey(trader.RequiresScouted) == true && colony.ZoneInfo(trader.RequiresScouted).Intel == ZoneIntel.Scouted;
    }

    /// <summary>Price options for a schematic (empty when it is not for sale at that tier).</summary>
    public static IReadOnlyList<PriceOption> Prices(ColonySim colony, SchematicDef def)
    {
        var prices = colony.Data.Schematics.Rules.Radio.Prices;
        return def.Tier <= prices.Count ? prices[def.Tier - 1] : [];
    }

    /// <summary>What the trader pays for a schematic item of this tier.</summary>
    public static IReadOnlyDictionary<string, double> SellPrice(ColonySim colony, SchematicDef def)
    {
        var prices = colony.Data.Schematics.Rules.Radio.SellPrices;
        return prices.Count == 0 ? new Dictionary<string, double>() : SchematicCatalog.At(prices, def.Tier);
    }

    /// <summary>Days between visits at the current Radio Mast level.</summary>
    public static double VisitEveryDays(ColonySim colony) =>
        SchematicCatalog.At(colony.Data.Schematics.Rules.Radio.VisitEveryDays, Mast(colony)?.Level ?? 1);

    /// <summary>Once a tick: trader arrivals and departures, contract offers and deadlines, expired calls.</summary>
    internal static void Run(ColonySim colony)
    {
        var state = colony.State;
        var radio = state.Radio;
        var rules = colony.Data.Schematics.Rules.Radio;
        var tpd = colony.Data.Rules.TicksPerDay;
        foreach (var call in radio.Calls.Where(c => c.ExpiresTick is { } t && t <= state.Tick).ToList()) radio.Calls.Remove(call);
        if (colony.Data.Schematics.All.Count == 0) return;

        if (radio.TraderHere && state.Tick >= radio.TraderLeavesTick)
        {
            radio.TraderLeavesTick = null;
            radio.Stock.Clear();
            state.Log(new(state.Tick, ColonyEventType.TraderLeft, $"{Trader(colony)?.Name ?? "The trader"} has moved on."));
        }
        if (!TraderAvailable(colony))
        {
            radio.NextVisitTick = 0;
        }
        else if (radio.NextVisitTick == 0)
        {
            radio.NextVisitTick = state.Tick + tpd;
        }
        else if (!radio.TraderHere && state.Tick >= radio.NextVisitTick)
        {
            Arrive(colony);
            radio.NextVisitTick = state.Tick + (long)Math.Round(VisitEveryDays(colony) * tpd);
        }

        if (radio.Contract is { } contract && state.Tick >= contract.DueTick)
        {
            if (contract.Accepted)
                state.Log(new(state.Tick, ColonyEventType.ContractFailed, $"The contract for {ZoneName(colony, contract.ZoneId)} ran out."));
            radio.Contract = null;
        }
        if (!HasMast(colony) || colony.Scavenge is null) return;
        if (radio.NextContractTick == 0) radio.NextContractTick = state.Tick + (long)Math.Round(rules.ContractEveryDays * tpd);
        else if (radio.Contract is null && state.Tick >= radio.NextContractTick)
        {
            OfferContract(colony);
            radio.NextContractTick = state.Tick + (long)Math.Round(rules.ContractEveryDays * tpd);
        }
    }

    /// <summary>A party came home from <paramref name="zoneId"/>: an accepted contract for it pays out.</summary>
    internal static void PartyHome(ColonySim colony, string zoneId)
    {
        var state = colony.State;
        if (state.Radio.Contract is not { Accepted: true } c || c.ZoneId != zoneId) return;
        foreach (var (res, amount) in c.Reward) state.Stockpile.Add(res, amount);
        state.Radio.Contract = null;
        state.Radio.ContractsDone++;
        state.Log(new(state.Tick, ColonyEventType.ContractDone,
            $"Contract done: the run to {ZoneName(colony, zoneId)} paid {Describe(colony, c.Reward)}."));
    }

    private static void Arrive(ColonySim colony)
    {
        var state = colony.State;
        var radio = state.Radio;
        var trader = Trader(colony)!;
        var rules = colony.Data.Schematics.Rules.Radio;
        var tpd = colony.Data.Rules.TicksPerDay;
        radio.Stock = RollStock(colony, trader);
        radio.TraderLeavesTick = state.Tick + (long)Math.Round(rules.StayDays * tpd);
        radio.Visits++;
        foreach (var id in radio.Stock) Schematics.HearOf(colony, id, $"in {trader.Name}'s stock");
        var offer = radio.Stock.Count == 0 ? "nothing new to sell" : "selling " + string.Join(" and ", radio.Stock.Select(id => colony.Data.Schematics.All[id].Name));
        state.Log(new(state.Tick, ColonyEventType.TraderArrived, $"{trader.Name} is parked outside, {offer}."));
        Enqueue(colony, RadioCallKind.Trader, trader.Id, trader.Name, trader.Greeting.Length > 0 ? trader.Greeting : $"{trader.Name} here. We're outside the gate.",
            expiresInTicks: radio.TraderLeavesTick - state.Tick);
    }

    /// <summary>Schematics the colony does not know: tier ≤ the early max, and one ≤ the late max from <c>lateStockDay</c> on.</summary>
    private static List<string> RollStock(ColonySim colony, TraderDef trader)
    {
        var rules = colony.Data.Schematics.Rules.Radio;
        var day = colony.State.Tick / colony.Data.Rules.TicksPerDay;
        var pool = colony.Data.Schematics.All.Values
            .Where(s => s.Available && s.Has(SchematicSource.Trade) && !Schematics.Has(colony, s.Id) && !colony.State.Inventory.Has(s.Id)
                        && Prices(colony, s).Count > 0)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        var stock = new List<string>();
        for (var i = 0; i < rules.StockSize; i++)
        {
            var late = day >= rules.LateStockDay && i == rules.StockSize - 1;
            var max = Math.Min(trader.MaxTier, late ? rules.LateMaxTier : rules.EarlyMaxTier);
            var choices = pool.Where(s => s.Tier <= max && !stock.Contains(s.Id)).ToList();
            if (late && choices.Any(s => s.Tier == max)) choices = choices.Where(s => s.Tier == max).ToList();
            if (choices.Count == 0) continue;
            stock.Add(choices[colony.Rng.Next(0, choices.Count)].Id);
        }
        return stock;
    }

    private static void OfferContract(ColonySim colony)
    {
        var state = colony.State;
        var radio = state.Radio;
        var rules = colony.Data.Schematics.Rules.Radio;
        var level = Mast(colony)!.Level;
        var maxThreat = SchematicCatalog.At(rules.ContractMaxThreat, level);
        var zones = colony.WorldZones().Where(z => z.Intel != ZoneIntel.Unknown && z.Zone.Threat <= maxThreat).Select(z => z.Zone).ToList();
        if (zones.Count == 0) return;
        var zone = zones[colony.Rng.Next(0, zones.Count)];
        var reward = rules.ContractRewards.Count == 0 ? new Dictionary<string, double>() : new Dictionary<string, double>(SchematicCatalog.At(rules.ContractRewards, zone.Threat));
        var contract = new RadioContract
        {
            Id = radio.NextContractId++, ZoneId = zone.Id, Reward = reward,
            DueTick = state.Tick + (long)Math.Round(rules.ContractDays * colony.Data.Rules.TicksPerDay),
        };
        radio.Contract = contract;
        var caller = Trader(colony);
        Enqueue(colony, RadioCallKind.Contract, caller?.Id ?? "radio", "A voice on the radio",
            $"Need someone to run {zone.Name} in the next {rules.ContractDays:0} days. Pays {Describe(colony, reward)}.",
            tag: contract.Id.ToString(), expiresInTicks: contract.DueTick - state.Tick);
    }

    internal static string ZoneName(ColonySim colony, string zoneId) => colony.Scavenge?.Zones.GetValueOrDefault(zoneId)?.Name ?? zoneId;

    internal static string Describe(ColonySim colony, IReadOnlyDictionary<string, double> amounts) =>
        amounts.Count == 0 ? "nothing" : string.Join(", ", amounts.Select(kv => $"{kv.Value:0.#} {colony.Data.Resources.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}"));
}
