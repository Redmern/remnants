using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// The colony side of the scavenge loop. Launch takes adults and their gear out of the colony (they keep their
/// bed but have no job, do not eat colony food and are not treated); apply brings back whoever survived as they
/// are now, removes the dead for good and stores loot and recruits.
/// </summary>
internal static class ExpeditionHandlers
{
    public static CommandResult Launch(ColonySim sim, LaunchExpedition c)
    {
        var state = sim.State;
        if (sim.Scavenge is not { } data) return NoData();
        if (state.Expedition is not null) return CommandResult.Fail(CommandError.ExpeditionActive, "A party is already out.");
        if (!data.Zones.TryGetValue(c.ZoneId, out var zone)) return CommandResult.Fail(CommandError.UnknownZone, $"No zone '{c.ZoneId}'.");
        if (c.Members.Count == 0) return CommandResult.Fail(CommandError.EmptyParty, "Pick at least one survivor.");
        if (c.Members.Count > sim.MaxPartySize)
            return CommandResult.Fail(CommandError.PartyTooLarge, $"The Town Hall allows a party of {sim.MaxPartySize} (picked {c.Members.Count}).");
        if (c.Members.Select(m => m.SurvivorId).Distinct().Count() != c.Members.Count)
            return CommandResult.Fail(CommandError.DuplicateMember, "A survivor was picked twice.");

        foreach (var m in c.Members)
        {
            var s = state.FindSurvivor(m.SurvivorId);
            if (s is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {m.SurvivorId}.");
            if (s.IsChild) return CommandResult.Fail(CommandError.NotAnAdult, $"{s.Name} is a child and stays home.");
            if (s.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{s.Name} is already away.");
            if (s.PatientAtBuildingId is not null)
                return CommandResult.Fail(CommandError.IsPatient, $"{s.Name} is a patient; discharge them first.");
            foreach (var (slot, id) in m.Equipment ?? new())
            {
                if (!data.Items.TryGetValue(id, out var item)) return CommandResult.Fail(CommandError.UnknownItem, $"No item '{id}'.");
                if (item.Slot != slot) return CommandResult.Fail(CommandError.InvalidGear, $"{item.Name} does not go in the {slot} slot.");
            }
            foreach (var id in m.Pack ?? [])
                if (!data.Items.ContainsKey(id)) return CommandResult.Fail(CommandError.UnknownItem, $"No item '{id}'.");
        }

        var gear = c.Members.SelectMany(m => m.AllItems).ToList();
        if (!state.Inventory.HasAll(gear))
            return CommandResult.Fail(CommandError.MissingItems, $"Not enough gear in the inventory: {DescribeMissing(state.Inventory, gear, data)}.");
        var rations = new Dictionary<string, double> { [sim.Data.Rules.Food.ResourceId] = sim.Data.Rules.Expedition.RationFoodPerMember * c.Members.Count };
        if (!state.Stockpile.CanAfford(rations))
            return CommandResult.Fail(CommandError.CannotAfford, $"Not enough rations: {CommandHandlers.Describe(state.Stockpile.Shortfall(rations))}.");

        state.Stockpile.Spend(rations);
        foreach (var id in gear) state.Inventory.TryTake(id);
        foreach (var m in c.Members)
        {
            var s = state.FindSurvivor(m.SurvivorId)!;
            s.AssignedBuildingId = null;
            s.Away = true;
        }

        var expedition = new Expedition
        {
            Id = state.NextExpeditionId++,
            ZoneId = zone.Id,
            Seed = sim.Rng.NextULong(),
            LaunchedTick = state.Tick,
            Members = c.Members.Select(m => m with { Equipment = new(m.Equipment ?? new()), Pack = new(m.Pack ?? []) }).ToList(),
        };
        state.Expedition = expedition;
        var names = string.Join(", ", c.Members.Select(m => state.FindSurvivor(m.SurvivorId)!.Name));
        state.Log(new(state.Tick, ColonyEventType.ExpeditionLaunched, $"{names} set out for {zone.Name}."));
        return CommandResult.Success($"{names} set out for {zone.Name}.", expedition.Id);
    }

    /// <summary>The scavenge parties for the active expedition, built from the members' current state.</summary>
    public static PartySetup Party(ColonySim sim, Expedition expedition) =>
        new(ColonySim.ExpeditionPlayerId, expedition.Members.Select(m => new PartyMember(
            sim.State.FindSurvivor(m.SurvivorId) ?? throw new InvalidOperationException($"Expedition member {m.SurvivorId} is gone."),
            new(m.Equipment ?? new()),
            new(m.Pack ?? []))).ToList());

    public static CommandResult Apply(ColonySim sim, ApplyExtractionResult c)
    {
        var state = sim.State;
        var result = c.Result;
        if (sim.Scavenge is not { } data) return NoData();
        if (state.Expedition is not { } expedition) return CommandResult.Fail(CommandError.NoExpedition, "No party is out.");
        if (!result.Complete) return CommandResult.Fail(CommandError.ResultNotFinal, "The run is not over yet.");
        var memberIds = expedition.Members.Select(m => m.SurvivorId).ToHashSet();
        if (result.PlayerId != ColonySim.ExpeditionPlayerId
            || result.Returned.Concat(result.Recruits).Select(r => r.Survivor).Concat(result.Deaths).Any(s => s is null)
            || result.Returned.Select(r => r.Survivor.Id).Concat(result.Deaths.Select(d => d.Id)).Any(id => !memberIds.Contains(id)))
            return CommandResult.Fail(CommandError.ResultMismatch, "That result is not from this expedition.");
        var allItems = result.Returned.Concat(result.Recruits).SelectMany(r => r.Equipment.Values.Concat(r.Pack));
        if (allItems.FirstOrDefault(id => !data.Items.ContainsKey(id)) is { } unknown)
            return CommandResult.Fail(CommandError.UnknownItem, $"The result carries unknown item '{unknown}'.");

        var zoneName = data.Zones.TryGetValue(expedition.ZoneId, out var zone) ? zone.Name : expedition.ZoneId;
        var report = new ExpeditionReport(expedition.Id, expedition.ZoneId, state.Tick, [], [], [],
            new(result.Loot, StringComparer.Ordinal), new(StringComparer.Ordinal));

        foreach (var r in result.Returned)
        {
            var s = state.FindSurvivor(r.Survivor.Id)!;
            var injuries = BodyDiff.Describe(s.Body, r.Survivor.Body);
            s.Body = r.Survivor.Body;
            s.Health = Math.Clamp(r.Survivor.Health, 1, Survivor.MaxHealth);
            foreach (var (attribute, value) in r.Survivor.Attributes) s.Set(attribute, value);
            foreach (var (skill, value) in r.Survivor.Skills) s.Set(skill, value);
            s.Away = false;
            Stow(sim, r);
            report.Returned.Add(new(s.Id, s.Name, r.Evacuated, s.Health, injuries));
        }

        // Anyone who neither came back nor is listed dead did not make it either (e.g. an unfinished result's leftovers).
        var returnedIds = result.Returned.Select(r => r.Survivor.Id).ToHashSet();
        foreach (var member in expedition.Members.Where(m => !returnedIds.Contains(m.SurvivorId)))
        {
            if (state.FindSurvivor(member.SurvivorId) is not { } s) continue;
            foreach (var id in member.AllItems) report.LostGear[id] = report.LostGear.GetValueOrDefault(id) + 1;
            state.Survivors.Remove(s);
            state.Memorial.Add(new(s.Id, s.Name, state.Tick, $"Died in {zoneName}"));
            state.Log(new(state.Tick, ColonyEventType.Died, $"{s.Name} died in {zoneName}.", s.Id));
            report.Dead.Add(s.Name);
        }

        var ages = sim.Data.Rules.Expedition.RecruitAgeRange;
        foreach (var r in result.Recruits)
        {
            var s = NewScavenge.Clone(r.Survivor);
            s.Id = state.NextSurvivorId++;
            s.LifeStage = LifeStage.Adult;
            s.AgeTicks = sim.Rng.NextInclusive(ages[0], ages[1]) * sim.Data.Rules.TicksPerYear;
            s.Health = Math.Clamp(s.Health, 1, Survivor.MaxHealth);
            s.AssignedBuildingId = null;
            s.PatientAtBuildingId = null;
            s.Away = false;
            s.HasBed = false;
            s.HomelessTicks = 0;
            s.StarvingTicks = 0;
            state.Survivors.Add(s);
            Stow(sim, r);
            state.Log(new(state.Tick, ColonyEventType.Recruited, $"{s.Name} joined the colony.", s.Id));
            report.Recruits.Add(s.Name);
        }

        state.Expedition = null;
        state.LastReport = report;
        var summary = $"The party is back from {zoneName}: {report.Returned.Count} returned, {report.Dead.Count} dead, {report.Recruits.Count} recruited.";
        state.Log(new(state.Tick, ColonyEventType.ExpeditionReturned, summary));
        return CommandResult.Success(summary, expedition.Id);
    }

    /// <summary>Worn gear goes back to the inventory; carried items too, except salvage, which becomes stockpile resources.</summary>
    private static void Stow(ColonySim sim, ReturnedSurvivor r)
    {
        var data = sim.Scavenge!;
        foreach (var id in r.Equipment.Values) sim.State.Inventory.Add(id);
        foreach (var id in r.Pack)
        {
            if (data.Items[id].Salvage is { } salvage) sim.State.Stockpile.Add(salvage.Resource, salvage.Amount);
            else sim.State.Inventory.Add(id);
        }
    }

    private static string DescribeMissing(ItemInventory inventory, List<string> gear, ScavengeData data) =>
        string.Join(", ", gear.GroupBy(id => id)
            .Where(g => !inventory.Has(g.Key, g.Count()))
            .Select(g => $"{g.Count() - inventory.Count(g.Key)} more {data.Items[g.Key].Name}"));

    internal static CommandResult NoData() =>
        CommandResult.Fail(CommandError.NoScavengeData, "Scavenge data (items, zones) is not loaded.");
}
