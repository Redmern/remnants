using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Schematics;

/// <summary>Research a heard-of schematic at the Town Hall: Research Data now, then the researcher's time. They leave their job.</summary>
public sealed record StartResearch(string SchematicId, int ResearcherId) : IColonyCommand;

/// <summary>Stop the research at the Town Hall; the Research Data is refunded.</summary>
public sealed record CancelResearch : IColonyCommand;

/// <summary>Turn a schematic item in the inventory (an encrypted chip you do not want) into Research Data.</summary>
public sealed record SalvageSchematic(string ItemId) : IColonyCommand;

/// <summary>Buy a schematic from the trader's stock, paying with price option <paramref name="Option"/>.</summary>
public sealed record BuyFromTrader(string SchematicId, int Option = 0) : IColonyCommand;

/// <summary>Sell to the trader: a schematic item from the inventory, or <paramref name="ResearchData"/> units of Research Data.</summary>
public sealed record SellToTrader(string? ItemId = null, int ResearchData = 0) : IColonyCommand;

/// <summary>Make a copy (the item) of a known schematic, to trade or give away; it costs Research Data (<c>rules.copyCost</c>).</summary>
public sealed record CopySchematic(string SchematicId) : IColonyCommand;

/// <summary>Answer a radio call: it leaves the queue and <see cref="ColonySim.RadioCallAnswered"/> is raised.</summary>
public sealed record AnswerCall(int CallId) : IColonyCommand;

/// <summary>Accept the contract on offer.</summary>
public sealed record AcceptContract(int ContractId) : IColonyCommand;

/// <summary>Command handlers of the schematics module (research, salvage, trade, radio).</summary>
internal static class SchematicHandlers
{
    public static CommandResult? Handle(ColonySim sim, IColonyCommand command) => command switch
    {
        StartResearch c => Research.Start(sim, c),
        CancelResearch => Research.Cancel(sim),
        SalvageSchematic c => Salvage(sim, c),
        BuyFromTrader c => Buy(sim, c),
        SellToTrader c => Sell(sim, c),
        AnswerCall c => Answer(sim, c),
        AcceptContract c => Accept(sim, c),
        CopySchematic c => Schematics.MakeCopy(sim, c.SchematicId),
        _ => null,
    };

    private static CommandResult Salvage(ColonySim sim, SalvageSchematic c)
    {
        var state = sim.State;
        if (sim.Data.Schematics.Find(c.ItemId) is not { } def) return Fail(CommandError.NotASchematic, $"'{c.ItemId}' is not a schematic.");
        if (!state.Inventory.TryTake(def.Id)) return Fail(CommandError.MissingItems, $"The colony has no {def.ItemName}.");
        var value = sim.Data.Schematics.DuplicateValue(def);
        state.Stockpile.Add(sim.Data.Schematics.Rules.ResearchDataResource, value);
        var message = $"Salvaged the {def.Name} schematic into {value:0.#} Research Data.";
        state.Log(new(state.Tick, ColonyEventType.SchematicFound, message));
        return CommandResult.Success(message);
    }

    private static CommandResult Buy(ColonySim sim, BuyFromTrader c)
    {
        var state = sim.State;
        if (!state.Radio.TraderHere) return Fail(CommandError.NoTrader, "No trader is here.");
        if (!state.Radio.Stock.Contains(c.SchematicId) || sim.Data.Schematics.Find(c.SchematicId) is not { } def)
            return Fail(CommandError.NotInStock, "The trader does not have that schematic.");
        var prices = RadioMast.Prices(sim, def);
        if (c.Option < 0 || c.Option >= prices.Count) return Fail(CommandError.InvalidCount, "No such price.");
        var cost = prices[c.Option].Cost;
        if (!Payment.CanAfford(sim, cost))
            return Fail(CommandError.CannotAfford, $"Cannot afford the {def.Name} schematic: {Payment.Shortfall(sim, cost)}.");
        Payment.Spend(sim, cost);
        state.Radio.Stock.Remove(def.Id);
        var trader = RadioMast.Trader(sim)?.Name ?? "the trader";
        var message = $"Bought the {def.Name} schematic from {trader} for {RadioMast.Describe(sim, cost)}.";
        state.Log(new(state.Tick, ColonyEventType.Traded, message));
        Schematics.Receive(sim, def.Id, SchematicSource.Trade);
        return CommandResult.Success(message);
    }

    private static CommandResult Sell(ColonySim sim, SellToTrader c)
    {
        var state = sim.State;
        if (!state.Radio.TraderHere) return Fail(CommandError.NoTrader, "No trader is here.");
        var rules = sim.Data.Schematics.Rules;
        Dictionary<string, double> pay;
        string what;
        if (c.ItemId is { } id)
        {
            if (sim.Data.Schematics.Find(id) is not { } def) return Fail(CommandError.NotASchematic, $"'{id}' is not a schematic.");
            if (!state.Inventory.TryTake(def.Id)) return Fail(CommandError.MissingItems, $"The colony has no {def.ItemName}.");
            pay = new(RadioMast.SellPrice(sim, def));
            what = $"the {def.Name} schematic";
        }
        else
        {
            if (c.ResearchData < 1) return Fail(CommandError.InvalidCount, "Sell at least 1 Research Data.");
            if (!state.Stockpile.Has(rules.ResearchDataResource, c.ResearchData))
                return Fail(CommandError.CannotAfford, "Not that much Research Data.");
            state.Stockpile.Spend(new Dictionary<string, double> { [rules.ResearchDataResource] = c.ResearchData });
            pay = rules.Radio.ResearchDataPrice.ToDictionary(kv => kv.Key, kv => kv.Value * c.ResearchData);
            what = $"{c.ResearchData} Research Data";
        }
        Payment.Receive(sim, pay);
        var message = $"Sold {what} for {RadioMast.Describe(sim, pay)}.";
        state.Log(new(state.Tick, ColonyEventType.Traded, message));
        return CommandResult.Success(message);
    }

    private static CommandResult Answer(ColonySim sim, AnswerCall c)
    {
        var call = sim.State.Radio.Calls.Find(x => x.Id == c.CallId);
        if (call is null) return Fail(CommandError.UnknownCall, $"No call {c.CallId}.");
        sim.State.Radio.Calls.Remove(call);
        sim.RaiseRadioCallAnswered(call);
        return CommandResult.Success($"Answered {call.CallerName}.", call.Id);
    }

    private static CommandResult Accept(ColonySim sim, AcceptContract c)
    {
        var state = sim.State;
        if (state.Radio.Contract is not { } contract || contract.Id != c.ContractId) return Fail(CommandError.NoContract, "That contract is not on offer.");
        if (contract.Accepted) return Fail(CommandError.NoContract, "That contract is already taken.");
        contract.Accepted = true;
        state.Radio.Calls.RemoveAll(x => x.Kind == RadioCallKind.Contract && x.Tag == contract.Id.ToString());
        var message = $"Contract taken: bring a party home from {RadioMast.ZoneName(sim, contract.ZoneId)}.";
        state.Log(new(state.Tick, ColonyEventType.ContractAccepted, message));
        return CommandResult.Success(message, contract.Id);
    }

    internal static CommandResult Fail(CommandError error, string message) => CommandResult.Fail(error, message);
}

/// <summary>
/// Research at the Town Hall: a heard-of schematic of tier 1–2 at Town Hall L2, tier 3 at L3 with an Electronics Lab L2.
/// Paid in Research Data at the start; one researcher works it on their day shift, faster with Intellect and Electronics.
/// </summary>
public static class Research
{
    /// <summary>Whether this schematic can be researched now, and why not.</summary>
    public static CommandResult Check(ColonySim sim, string schematicId)
    {
        var state = sim.State;
        var rules = sim.Data.Schematics.Rules.Research;
        if (sim.Data.Schematics.Find(schematicId) is not { } def) return Fail(CommandError.NotASchematic, $"'{schematicId}' is not a schematic.");
        if (Schematics.Has(sim, def.Id)) return Fail(CommandError.AlreadyKnown, $"The {def.Name} schematic is already known.");
        if (!def.Available || !def.Has(SchematicSource.Research) || SchematicCatalog.At(rules.Cost, def.Tier) <= 0)
            return Fail(CommandError.NotResearchable, $"The {def.Name} schematic cannot be researched.");
        if (state.Schematics.Research is not null) return Fail(CommandError.ResearchBusy, "The Town Hall is already researching.");
        if (!state.Schematics.HeardOf.ContainsKey(def.Id))
            return Fail(CommandError.NotHeardOf, $"Nobody has heard of a {def.Name} schematic yet.");
        var hallLevel = state.Buildings.Where(b => b.DefId == rules.Building).Select(b => b.Level).DefaultIfEmpty(0).Max();
        var needHall = SchematicCatalog.At(rules.TownHallLevel, def.Tier);
        if (hallLevel < needHall)
            return Fail(CommandError.TownHallLevelTooLow, $"Tier {def.Tier} research needs {Schematics.BuildingName(sim, rules.Building)} level {needHall}.");
        if (rules.Needs.Count > 0)
            foreach (var (b, level) in SchematicCatalog.At(rules.Needs, def.Tier))
                if (!state.Buildings.Any(x => x.DefId == b && x.Level >= level))
                    return Fail(CommandError.NoStudyBuilding, $"Tier {def.Tier} research needs a {Schematics.BuildingName(sim, b)} of level {level}.");
        var cost = SchematicCatalog.At(rules.Cost, def.Tier);
        if (!state.Stockpile.Has(sim.Data.Schematics.Rules.ResearchDataResource, cost))
            return Fail(CommandError.CannotAfford, $"Researching the {def.Name} schematic takes {cost:0} Research Data.");
        return CommandResult.Success("");
    }

    /// <summary>A researcher's factor: base + Intellect·perIntellect + Electronics·perSkill.</summary>
    public static double Factor(ColonySim sim, Survivor s)
    {
        var r = sim.Data.Schematics.Rules.Research;
        return r.BaseFactor + s.Get(AttributeKind.Intellect) * r.PerIntellect + s.Get(Skill.Electronics) * r.PerSkill;
    }

    /// <summary>Ticks left at the researcher's rate (on day-shift hours only), or null with nobody working it.</summary>
    public static int? Eta(ColonySim sim)
    {
        if (sim.State.Schematics.Research is not { } o || sim.State.FindSurvivor(o.ResearcherId) is not { } s) return null;
        var perHour = Factor(sim, s);
        if (perHour <= 0) return null;
        var hours = (o.Work - o.Progress) / perHour;
        var rules = sim.Data.Rules;
        var shift = rules.DayNight.Enabled ? DayNight.WindowLength(rules.DayNight.WorkHours, rules.TicksPerDay) : rules.TicksPerDay;
        return (int)Math.Ceiling(hours * rules.TicksPerDay / Math.Max(1, shift));
    }

    /// <summary>Whether this survivor is researching (they cannot take a job meanwhile).</summary>
    public static bool IsResearching(ColonyState state, int survivorId) => state.Schematics.Research?.ResearcherId == survivorId;

    internal static CommandResult Start(ColonySim sim, StartResearch c)
    {
        var state = sim.State;
        if (state.FindSurvivor(c.ResearcherId) is not { } s) return Fail(CommandError.UnknownSurvivor, $"No survivor {c.ResearcherId}.");
        if (s.IsChild || s.IsInfant) return Fail(CommandError.TooYoung, $"{s.Name} is too young to research.");
        if (s.Away) return Fail(CommandError.SurvivorAway, $"{s.Name} is away.");
        if (s.PatientAtBuildingId is not null) return Fail(CommandError.IsPatient, $"{s.Name} is a patient.");
        if (StudySystem.ClassOf(state, s.Id) is not null) return Fail(CommandError.InClass, $"{s.Name} is in class.");
        if (Check(sim, c.SchematicId) is { Ok: false } no) return no;
        var rules = sim.Data.Schematics.Rules.Research;
        var def = sim.Data.Schematics.All[c.SchematicId];
        var cost = SchematicCatalog.At(rules.Cost, def.Tier);
        state.Stockpile.Spend(new Dictionary<string, double> { [sim.Data.Schematics.Rules.ResearchDataResource] = cost });
        s.AssignedBuildingId = null;
        Colony.ConstructionSystem.Release(state, s.Id);
        var hall = state.Buildings.Where(b => b.DefId == rules.Building).MaxBy(b => b.Level)!;
        state.Schematics.Research = new ResearchOrder
        {
            SchematicId = def.Id, ResearcherId = s.Id, BuildingId = hall.Id, Work = SchematicCatalog.At(rules.Work, def.Tier), Paid = cost,
        };
        var message = $"{s.Name} started researching the {def.Name} schematic.";
        state.Log(new(state.Tick, ColonyEventType.ResearchStarted, message, s.Id, hall.Id));
        return CommandResult.Success(message, hall.Id);
    }

    internal static CommandResult Cancel(ColonySim sim)
    {
        var state = sim.State;
        if (state.Schematics.Research is not { } o) return Fail(CommandError.NoResearch, "Nothing is being researched.");
        state.Stockpile.Add(sim.Data.Schematics.Rules.ResearchDataResource, o.Paid);
        state.Schematics.Research = null;
        var message = $"Research of the {sim.Data.Schematics.Find(o.SchematicId)?.Name ?? o.SchematicId} schematic stopped; the Research Data is back.";
        state.Log(new(state.Tick, ColonyEventType.ResearchEnded, message));
        return CommandResult.Success(message);
    }

    /// <summary>Each tick: the researcher (home, healthy, on their day shift) adds their factor; done, the colony learns it.</summary>
    internal static void Run(ColonySim sim)
    {
        var state = sim.State;
        if (state.Schematics.Research is not { } o) return;
        var s = state.FindSurvivor(o.ResearcherId);
        if (s is null || state.FindBuilding(o.BuildingId) is null)
        {
            Cancel(sim);
            return;
        }
        if (s.Away || s.PatientAtBuildingId is not null) return;
        var rules = sim.Data.Rules;
        if (rules.DayNight.Enabled && !Schedule.IsDayShiftHour(s, DayNight.Hour(state.Tick, rules), rules.DayNight)) return;
        o.Progress += Factor(sim, s);
        Progression.GrantXp(state, sim.Data, s, sim.Data.Progression.Xp.PerStudyTick, XpSource.Study);
        if (o.Progress + 1e-9 < o.Work) return;
        state.Schematics.Research = null;
        state.Log(new(state.Tick, ColonyEventType.ResearchFinished, $"{s.Name} finished researching the {sim.Data.Schematics.All[o.SchematicId].Name} schematic.", s.Id));
        Schematics.Grant(sim, o.SchematicId, SchematicSource.Research);
    }

    private static CommandResult Fail(CommandError error, string message) => CommandResult.Fail(error, message);
}

/// <summary>
/// Every price the schematics module charges or pays (trader, research, copies, contracts) goes through here, so a change
/// to how goods are kept (e.g. food as items) is one swap.
/// </summary>
public static class Payment
{
    /// <summary>
    /// Prices and rewards may name <see cref="Pantry.FoodKey"/> ("food"): that much food value from the pantry (whole items,
    /// soonest to spoil first), or into it as canned food. Everything else is a stockpile resource.
    /// </summary>
    public static bool CanAfford(ColonySim sim, IReadOnlyDictionary<string, double> cost) =>
        sim.State.Stockpile.CanAfford(Resources(cost)) && Pantry.FoodValue(sim) + 1e-9 >= Food(cost);

    public static void Spend(ColonySim sim, IReadOnlyDictionary<string, double> cost)
    {
        if (!CanAfford(sim, cost)) throw new InvalidOperationException("Cannot afford cost.");
        sim.State.Stockpile.Spend(Resources(cost));
        Pantry.TakeFoodValue(sim, Food(cost));
    }

    public static void Receive(ColonySim sim, IReadOnlyDictionary<string, double> amounts)
    {
        foreach (var (res, amount) in amounts)
            if (res == Pantry.FoodKey) Pantry.AddFoodValue(sim.State, sim.Data, amount);
            else sim.State.Stockpile.Add(res, amount);
    }

    public static string Shortfall(ColonySim sim, IReadOnlyDictionary<string, double> cost)
    {
        var shortfall = sim.State.Stockpile.Shortfall(Resources(cost));
        var food = Food(cost) - Pantry.FoodValue(sim);
        if (food > 1e-9) shortfall[Pantry.FoodKey] = food;
        return CommandHandlers.Describe(shortfall);
    }

    private static double Food(IReadOnlyDictionary<string, double> cost) => cost.GetValueOrDefault(Pantry.FoodKey);

    private static Dictionary<string, double> Resources(IReadOnlyDictionary<string, double> cost) =>
        cost.Where(kv => kv.Key != Pantry.FoodKey).ToDictionary(kv => kv.Key, kv => kv.Value);
}
