using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Schematics;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// The colony side of the scavenge loop. Launch takes adults and their gear out of the colony (they keep their
/// bed but have no job, do not eat colony food and are not treated); apply brings back whoever survived as they
/// are now, removes the dead for good and stores loot and recruits.
/// </summary>
internal static class ExpeditionHandlers
{
    /// <param name="partyCap">A story mission's own party size (the perimeter defense takes up to 4), else the Town Hall's.</param>
    public static CommandResult Launch(ColonySim sim, LaunchExpedition c, int? partyCap = null)
    {
        var state = sim.State;
        if (sim.Scavenge is not { } data) return NoData();
        if (state.Expedition is not null) return CommandResult.Fail(CommandError.ExpeditionActive, "A party is already out.");
        if (!data.Zones.TryGetValue(c.ZoneId, out var zone)) return CommandResult.Fail(CommandError.UnknownZone, $"No zone '{c.ZoneId}'.");
        if (WorldMap.IntelOf(state, zone) == ZoneIntel.Unknown)
            return CommandResult.Fail(CommandError.ZoneUnknown, "Nobody knows the way there yet: scout nearby zones first.");
        if (c.Members.Count == 0) return CommandResult.Fail(CommandError.EmptyParty, "Pick at least one survivor.");
        var maxParty = Math.Max(sim.MaxPartySize, partyCap ?? 0);
        if (c.Members.Count > maxParty)
            return CommandResult.Fail(CommandError.PartyTooLarge, $"The Town Hall allows a party of {maxParty} (picked {c.Members.Count}).");
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
            if (s.Pregnancy is not null) return CommandResult.Fail(CommandError.Pregnant, $"{s.Name} is expecting and stays home.");
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
        var plan = sim.PlanArrival(zone.Id, c.WaitFor);
        if (plan.Blocked)
            return CommandResult.Fail(CommandError.RouteBlocked, $"{plan.Travel!.Modifiers.First(m => m.Blocks).Reason}. Wait for it to pass.");
        var need = (WorldMap.RationsPerMember(sim.Data, zone) + plan.ExtraRationsPerMember) * c.Members.Count;
        var trip = WorldMap.TravelTicks(sim.Data, zone) * 2 + plan.WaitTicks;
        var bill = Rations.Plan(sim, need, c.Members.Count, trip, c.Rations);
        if (!bill.Enough)
            return CommandResult.Fail(CommandError.CannotAfford, $"Not enough food for rations: need {need:0.#}, the pantry has {bill.Covered:0.#}.");

        Rations.Take(sim, need, trip, c.Rations);
        foreach (var id in gear) state.Inventory.TryTake(id);
        foreach (var m in c.Members)
        {
            var s = state.FindSurvivor(m.SurvivorId)!;
            s.AssignedBuildingId = null;
            s.Away = true;
        }

        var travel = plan.Travel?.Ticks ?? WorldMap.TravelTicks(sim.Data, zone);
        var expedition = new Expedition
        {
            Id = state.NextExpeditionId++,
            ZoneId = zone.Id,
            Seed = sim.Rng.NextULong(),
            LaunchedTick = state.Tick,
            TravelTicks = travel,
            WaitTicks = plan.WaitTicks,
            ArrivalTick = plan.ArrivalTick,
            Depletion = state.Zones.GetValueOrDefault(zone.Id)?.Depletion ?? 0,
            Members = c.Members.Select(m => m with { Equipment = new(m.Equipment ?? new()), Pack = new(m.Pack ?? []) }).ToList(),
            Rations = bill.Items,
        };
        Rations.SetOut(sim, bill, c.Members.Select(m => state.FindSurvivor(m.SurvivorId)!), plan.ArrivalTick, sim.Rng);
        state.Expedition = expedition;
        var names = string.Join(", ", c.Members.Select(m => state.FindSurvivor(m.SurvivorId)!.Name));
        var road = travel > 0 ? $" ({travel / (double)sim.Data.Rules.TicksPerDay:0.#} days on the road)" : "";
        if (plan.WaitTicks > 0) road += $", waiting {plan.WaitTicks} h for {plan.Phase.ToString().ToLowerInvariant()}";
        state.Log(new(state.Tick, ColonyEventType.ExpeditionLaunched, $"{names} set out for {zone.Name}{road}."));
        return CommandResult.Success($"{names} set out for {zone.Name}{road}.", expedition.Id);
    }

    /// <summary>The scavenge parties for the active expedition, built from the members' current state.</summary>
    public static PartySetup Party(ColonySim sim, Expedition expedition) =>
        new(ColonySim.ExpeditionPlayerId, expedition.Members.Select(m =>
        {
            var s = sim.State.FindSurvivor(m.SurvivorId) ?? throw new InvalidOperationException($"Expedition member {m.SurvivorId} is gone.");
            return new PartyMember(s, new(m.Equipment ?? new()), new(m.Pack ?? []),
                sim.Data.Food is null ? 0 : FoodEffects.FieldFirstRoundAp(s, sim.Data, sim.State.Tick));
        }).ToList());

    public static CommandResult Apply(ColonySim sim, ApplyExtractionResult c)
    {
        var state = sim.State;
        var result = c.Result;
        if (sim.Scavenge is not { } data) return NoData();
        if (state.Expedition is not { } expedition) return CommandResult.Fail(CommandError.NoExpedition, "No party is out.");
        if (expedition.PendingResult is not null) return CommandResult.Fail(CommandError.PartyReturning, "The party is already heading home.");
        if (!result.Complete) return CommandResult.Fail(CommandError.ResultNotFinal, "The run is not over yet.");
        var memberIds = expedition.Members.Select(m => m.SurvivorId).ToHashSet();
        if (result.PlayerId != ColonySim.ExpeditionPlayerId
            || result.Returned.Concat(result.Recruits).Select(r => r.Survivor).Concat(result.Deaths).Any(s => s is null)
            || result.Returned.Select(r => r.Survivor.Id).Concat(result.Deaths.Select(d => d.Id)).Any(id => !memberIds.Contains(id)))
            return CommandResult.Fail(CommandError.ResultMismatch, "That result is not from this expedition.");
        var allItems = result.Returned.Concat(result.Recruits).SelectMany(r => r.Equipment.Values.Concat(r.Pack)).Concat(result.Secured ?? []);
        if (allItems.FirstOrDefault(id => !data.Items.ContainsKey(id)) is { } unknown)
            return CommandResult.Fail(CommandError.UnknownItem, $"The result carries unknown item '{unknown}'.");

        if (expedition.TravelTicks > 0)
        {
            // The road home has its own weather: TravelTicks becomes the way back.
            if (data.Zones.TryGetValue(expedition.ZoneId, out var zone))
                expedition.TravelTicks = Math.Max(1, WorldMap.Travel(sim, zone, state.Tick, homeward: true).Ticks);
            expedition.PendingResult = result;
            expedition.ReturnTick = state.Tick + expedition.TravelTicks;
            expedition.Scavenge = null;
            var days = expedition.TravelTicks / (double)sim.Data.Rules.TicksPerDay;
            var message = $"The run is over; the party is heading home ({days:0.#} days).";
            state.Log(new(state.Tick, ColonyEventType.ExpeditionHeadingHome, message));
            return CommandResult.Success(message, expedition.Id);
        }
        return ApplyNow(sim, expedition, result);
    }

    /// <summary>The party is home: survivors, the dead, recruits and loot are applied and the zone counts as visited.</summary>
    internal static CommandResult ApplyNow(ColonySim sim, Expedition expedition, ExtractionResult result)
    {
        var state = sim.State;
        var data = sim.Scavenge!;
        var zoneName = data.Zones.TryGetValue(expedition.ZoneId, out var zone) ? zone.Name : expedition.ZoneId;
        // Loot is what came back beyond the packs the survivors set out with.
        var loot = new SortedDictionary<string, int>(result.Loot, StringComparer.Ordinal);
        foreach (var r in result.Returned)
            foreach (var id in expedition.Members.Single(m => m.SurvivorId == r.Survivor.Id).Pack ?? [])
            {
                var left = loot.GetValueOrDefault(id) - 1;
                if (left > 0) loot[id] = left;
                else loot.Remove(id);
            }
        var report = new ExpeditionReport(expedition.Id, expedition.ZoneId, state.Tick, [], [], [], loot, new(StringComparer.Ordinal));

        foreach (var r in result.Returned)
        {
            var s = state.FindSurvivor(r.Survivor.Id)!;
            var injuries = BodyDiff.Describe(s.Body, r.Survivor.Body);
            s.Body = r.Survivor.Body;
            s.Health = Math.Clamp(r.Survivor.Health, 1, Survivor.MaxHealth);
            foreach (var (attribute, value) in r.Survivor.Attributes) s.Set(attribute, value);
            var fieldGain = r.Survivor.Skills.Sum(kv => Math.Max(0, kv.Value - s.Get(kv.Key)));
            foreach (var (skill, value) in r.Survivor.Skills) s.Set(skill, value);
            s.Away = false;
            var xp = sim.Data.Progression.Xp;
            Progression.GrantXp(state, sim.Data, s,
                xp.PerMission + xp.PerMissionThreat * (zone?.Threat ?? 0) + xp.PerFieldSkillPoint * fieldGain + Math.Max(0, r.FieldXp), XpSource.Mission);
            Stow(sim, r, report);
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
            Bonds.OnGone(state, sim.Data, s, died: true);
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
            s.HomeBuildingId = null;
            s.HomelessTicks = 0;
            s.StarvingTicks = 0;
            s.PartnerId = s.MotherId = s.FatherId = null;
            s.Pregnancy = null;
            s.Mood = Survivor.MaxMood;
            s.GrowthTicks = s.MalnourishedTicks = 0;
            s.Aptitude = new();
            state.Survivors.Add(s);
            Stow(sim, r, report);
            state.Log(new(state.Tick, ColonyEventType.Recruited, $"{s.Name} joined the colony.", s.Id));
            report.Recruits.Add(s.Name);
        }

        // What a cargo drone ferried out is home whatever became of the party.
        foreach (var id in result.Secured ?? [])
        {
            if (data.Items[id].Schematic is { } schematic) ReceiveSchematic(sim, schematic, report);
            else if (data.Items[id].Salvage is { } salvage) state.Stockpile.Add(salvage.Resource, salvage.Amount);
            else state.Inventory.Add(id);
            if (!expedition.Members.Any(m => m.AllItems.Contains(id))) report.Loot[id] = report.Loot.GetValueOrDefault(id) + 1;
        }
        // Co-op: copies of what allies carried out; what the party saw but left behind becomes researchable.
        foreach (var id in result.SharedSchematics) ReceiveSchematic(sim, id, report);
        foreach (var (id, where) in result.HeardOf) Schematics.Schematics.HearOf(sim, id, where);
        RadioMast.PartyHome(sim, expedition.ZoneId);
        // A plain run's exit stash stays in the zone for next time (a story map is not the zone's usual ground).
        if (expedition.MissionId is null && result.Complete)
        {
            if (!state.Zones.TryGetValue(expedition.ZoneId, out var status)) state.Zones[expedition.ZoneId] = status = new ZoneStatus();
            status.Stash = result.Stash.ToList();
        }

        state.Expedition = null;
        state.LastReport = report;
        if (expedition.MissionId is { } missionId) global::Remnants.Core.Story.Campaign.Returned(sim, missionId, result.Mission);
        if (zone is not null) WorldMap.Visited(state, data, zone);
        var summary = $"The party is back from {zoneName}: {report.Returned.Count} returned, {report.Dead.Count} dead, {report.Recruits.Count} recruited.";
        state.Log(new(state.Tick, ColonyEventType.ExpeditionReturned, summary));
        return CommandResult.Success(summary, expedition.Id);
    }

    /// <summary>
    /// Worn gear goes back to the inventory; carried items too, except salvage, which becomes stockpile resources,
    /// schematics, which the colony learns (or queues for decrypting, or turns into Research Data when known), and food,
    /// which goes into the pantry as a fresh lot. Devices
    /// with charges spent out there go on the charging bench instead (see <see cref="Power"/>), as many as the spent charges fill.
    /// </summary>
    private static void Stow(ColonySim sim, ReturnedSurvivor r, ExpeditionReport report)
    {
        var data = sim.Scavenge!;
        var spent = new Dictionary<string, int>(r.ChargesUsed ?? [], StringComparer.Ordinal);
        void Put(string id)
        {
            var max = data.Items[id].Use?.Charges ?? 0;
            if (max > 0 && spent.GetValueOrDefault(id) is var used and > 0)
            {
                var drained = Math.Min(max, used);
                spent[id] = used - drained;
                Power.Drained(sim.State, sim.Data, id, drained);
                return;
            }
            sim.State.Inventory.Add(id);
        }
        foreach (var id in r.Equipment.Values) Put(id);
        foreach (var id in r.Pack) Deposit(sim, id, report, Put);
    }

    /// <summary>
    /// One item reaching the colony (a pack, raid spoils): salvage becomes stockpile resources, a schematic is received,
    /// food goes into the pantry, anything else into the inventory (through <paramref name="put"/> when given).
    /// </summary>
    internal static void Deposit(ColonySim sim, string id, ExpeditionReport? report = null, Action<string>? put = null)
    {
        var data = sim.Scavenge!;
        if (data.Items[id].Schematic is { } schematic)
        {
            if (report is not null) ReceiveSchematic(sim, schematic, report);
            else if (sim.Data.Schematics.Find(schematic) is not null)
            {
                sim.State.Schematics.Found++;
                Schematics.Schematics.Receive(sim, schematic, SchematicSource.Kill);
            }
        }
        else if (data.Items[id].Salvage is { } salvage) sim.State.Stockpile.Add(salvage.Resource, salvage.Amount);
        else if (sim.Data.Food?.IsFood(id) == true) Pantry.Add(sim.State, sim.Data, id, 1);
        else if (put is not null) put(id);
        else sim.State.Inventory.Add(id);
    }

    private static void ReceiveSchematic(ColonySim sim, string id, ExpeditionReport report)
    {
        if (sim.Data.Schematics.Find(id) is null) return;
        sim.State.Schematics.Found++;
        var result = Schematics.Schematics.Receive(sim, id, SchematicSource.Loot);
        report.Schematics[id] = result.Outcome;
    }

    private static string DescribeMissing(ItemInventory inventory, List<string> gear, ScavengeData data) =>
        string.Join(", ", gear.GroupBy(id => id)
            .Where(g => !inventory.Has(g.Key, g.Count()))
            .Select(g => $"{g.Count() - inventory.Count(g.Key)} more {data.Items[g.Key].Name}"));

    internal static CommandResult NoData() =>
        CommandResult.Fail(CommandError.NoScavengeData, "Scavenge data (items, zones) is not loaded.");
}
