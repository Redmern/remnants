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
        Vehicle? vehicle = null;
        if (c.VehicleId is { } vid)
        {
            vehicle = state.Vehicles.Find(v => v.Id == vid);
            if (vehicle is null) return CommandResult.Fail(CommandError.UnknownVehicle, $"No vehicle {vid}.");
            if (vehicle.Away) return CommandResult.Fail(CommandError.VehicleAway, $"{vehicle.Name} is already out.");
            if (VehicleRules.Broken(sim.Data, vehicle))
                return CommandResult.Fail(CommandError.VehicleBroken, $"{vehicle.Name} has broken down; repair it at the Garage first.");
            if (sim.VehicleStats(vehicle).Seats is { } seats && c.Members.Count > seats)
                return CommandResult.Fail(CommandError.NotEnoughSeats, $"The {vehicle.Name} seats {seats} (picked {c.Members.Count}).");
        }
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
        var travel = VehicleRules.Plan(sim, zone, vehicle, state.Tick);
        if (travel.Blocked is { } why) return CommandResult.Fail(CommandError.RoadBlocked, why);
        var plan = sim.PlanArrival(zone.Id, c.WaitFor, vehicle?.Id);
        var food = (WorldMap.RationsPerMember(sim.Data, travel.Days) + plan.WaitRationsPerMember) * c.Members.Count;
        var fuel = new Dictionary<string, double>();
        if (travel.FuelRoundTrip > 0) fuel[sim.Data.Vehicles.FuelResource] = travel.FuelRoundTrip;
        if (Rations.Available(sim) < food - 1e-9)
            return CommandResult.Fail(CommandError.CannotAfford, $"Not enough rations: need {Math.Ceiling(food)} food.");
        if (!state.Stockpile.CanAfford(fuel))
            return CommandResult.Fail(CommandError.CannotAfford, $"Not enough fuel: {CommandHandlers.Describe(state.Stockpile.Shortfall(fuel))}.");

        Rations.Take(sim, food);
        state.Stockpile.Spend(fuel);
        foreach (var id in gear) state.Inventory.TryTake(id);
        foreach (var m in c.Members)
        {
            var s = state.FindSurvivor(m.SurvivorId)!;
            s.AssignedBuildingId = null;
            s.Away = true;
        }

        if (vehicle is not null)
        {
            vehicle.Away = true;
            VehicleRules.WearOut(sim.Data, vehicle, travel.Days);
        }
        var expedition = new Expedition
        {
            Id = state.NextExpeditionId++,
            ZoneId = zone.Id,
            Seed = sim.Rng.NextULong(),
            LaunchedTick = state.Tick,
            TravelTicks = travel.Ticks,
            VehicleId = vehicle?.Id,
            VehicleNoise = vehicle is null ? 0 : VehicleRules.ArrivalNoise(sim.Data, vehicle),
            Fuel = travel.FuelRoundTrip,
            WaitTicks = plan.WaitTicks,
            ArrivalTick = plan.ArrivalTick,
            Depletion = state.Zones.GetValueOrDefault(zone.Id)?.Depletion ?? 0,
            Members = c.Members.Select(m => m with { Equipment = new(m.Equipment ?? new()), Pack = new(m.Pack ?? []) }).ToList(),
        };
        state.Expedition = expedition;
        var names = string.Join(", ", c.Members.Select(m => state.FindSurvivor(m.SurvivorId)!.Name));
        var by = vehicle is null ? "" : $" by {vehicle.Name}";
        var road = travel.Ticks > 0 ? $"{by} ({travel.Days:0.#} days on the road)" : by;
        if (plan.WaitTicks > 0) road += $", waiting {plan.WaitTicks} h for {plan.Phase.ToString().ToLowerInvariant()}";
        state.Log(new(state.Tick, ColonyEventType.ExpeditionLaunched, $"{names} set out for {zone.Name}{road}."));
        return CommandResult.Success($"{names} set out for {zone.Name}{road}.", expedition.Id);
    }

    /// <summary>The scavenge parties for the active expedition, built from the members' current state.</summary>
    public static PartySetup Party(ColonySim sim, Expedition expedition) =>
        new(ColonySim.ExpeditionPlayerId, expedition.Members.Select(m => new PartyMember(
            sim.State.FindSurvivor(m.SurvivorId) ?? throw new InvalidOperationException($"Expedition member {m.SurvivorId} is gone."),
            new(m.Equipment ?? new()),
            new(m.Pack ?? []))).ToList())
        {
            Vehicle = expedition.VehicleId is { } id && sim.FindVehicle(id) is { } v ? VehicleRules.Field(sim.Data, v) : null,
        };

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
        var allItems = result.Returned.Concat(result.Recruits).SelectMany(r => r.Equipment.Values.Concat(r.Pack));
        if (allItems.FirstOrDefault(id => !data.Items.ContainsKey(id)) is { } unknown)
            return CommandResult.Fail(CommandError.UnknownItem, $"The result carries unknown item '{unknown}'.");

        var home = ReturnTrip(sim, expedition, result);
        if (home.Ticks > 0)
        {
            expedition.PendingResult = result;
            expedition.ReturnTick = state.Tick + home.Ticks;
            expedition.ReturnTravelTicks = home.Ticks;
            expedition.Scavenge = null;
            var days = home.Ticks / (double)sim.Data.Rules.TicksPerDay;
            var message = $"The run is over; the party is heading home ({days:0.#} days).";
            state.Log(new(state.Tick, ColonyEventType.ExpeditionHeadingHome, message));
            return CommandResult.Success(message, expedition.Id);
        }
        return ApplyNow(sim, expedition, result);
    }

    /// <summary>
    /// The way home, leaving now: by the vehicle when it survived the run and someone drives it, else on foot. A zone the
    /// party walked to takes as long back as out.
    /// </summary>
    private static TravelPlan ReturnTrip(ColonySim sim, Expedition expedition, ExtractionResult result)
    {
        var zone = sim.Scavenge!.Zones[expedition.ZoneId];
        var vehicle = expedition.VehicleId is { } id ? sim.FindVehicle(id) : null;
        var drives = vehicle is not null && result.Returned.Count > 0 && result.Vehicle is not { Wrecked: true };
        if (!drives) return new TravelPlan(zone.TravelDays, [], zone.TravelDays, expedition.VehicleId is null ? expedition.TravelTicks : WorldMap.TravelTicks(sim.Data, zone), 0, null);
        var plan = VehicleRules.Plan(sim, zone, vehicle, sim.State.Tick);
        // Fuel for the way home was paid at launch and a storm cannot strand them: it only slows them down.
        return plan with { Blocked = null };
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
            Stow(sim, r);
            state.Log(new(state.Tick, ColonyEventType.Recruited, $"{s.Name} joined the colony.", s.Id));
            report.Recruits.Add(s.Name);
        }

        VehicleHome(sim, expedition, result, report);
        state.Expedition = null;
        state.LastReport = report;
        if (expedition.MissionId is { } missionId) global::Remnants.Core.Story.Campaign.Returned(sim, missionId, result.Mission);
        if (zone is not null) WorldMap.Visited(state, data, zone);
        var summary = $"The party is back from {zoneName}: {report.Returned.Count} returned, {report.Dead.Count} dead, {report.Recruits.Count} recruited.";
        state.Log(new(state.Tick, ColonyEventType.ExpeditionReturned, summary));
        return CommandResult.Success(summary, expedition.Id);
    }

    /// <summary>
    /// The party's vehicle after the run: wrecked in the field, or left behind when nobody came back, it is lost; otherwise
    /// it is home with the condition it had at the end of the run less the wear of the road back, and its stash is
    /// unloaded like the packs (counted in <see cref="ExtractionResult.Loot"/>).
    /// </summary>
    private static void VehicleHome(ColonySim sim, Expedition expedition, ExtractionResult result, ExpeditionReport report)
    {
        var state = sim.State;
        if (expedition.VehicleId is not { } id || sim.FindVehicle(id) is not { } vehicle) return;
        var data = sim.Scavenge!;
        if (result.Vehicle is { Wrecked: true } || result.Returned.Count == 0)
        {
            state.Vehicles.Remove(vehicle);
            var how = result.Vehicle is { Wrecked: true } ? "was wrecked" : "was left behind";
            report.LostVehicle = vehicle.Name;
            state.Log(new(state.Tick, ColonyEventType.VehicleLost, $"The {vehicle.Name} {how} in {data.Zones.GetValueOrDefault(expedition.ZoneId)?.Name ?? expedition.ZoneId}."));
            return;
        }
        vehicle.Away = false;
        var stats = sim.VehicleStats(vehicle);
        if (result.Vehicle is { } field)
        {
            if (field.Hp < vehicle.Condition) VehicleRules.CombatWear(sim.Data, vehicle, (vehicle.Condition - field.Hp) / Math.Max(1, field.MaxHp));
            vehicle.Condition = Math.Min(vehicle.Condition, field.Hp);
            foreach (var item in field.Stash)
                if (data.Items[item].Salvage is { } salvage) state.Stockpile.Add(salvage.Resource, salvage.Amount);
                else state.Inventory.Add(item);
            if (field.Hp < field.MaxHp - 1e-9)
                state.Log(new(state.Tick, ColonyEventType.VehicleDamaged, $"The {vehicle.Name} came back damaged ({field.Hp:0}/{field.MaxHp:0})."));
        }
        var backDays = WorldMap.TravelTicks(sim.Data, data.Zones[expedition.ZoneId]) > 0 ? expedition.TravelTicks / (double)sim.Data.Rules.TicksPerDay : 0;
        VehicleRules.WearOut(sim.Data, vehicle, backDays);
        // A field medkit locker patches everyone up before the road.
        if (stats.HealOnReturn > 0)
            foreach (var r in result.Returned)
                if (state.FindSurvivor(r.Survivor.Id) is { } s) s.Health = Math.Min(Survivors.Survivor.MaxHealth, s.Health + stats.HealOnReturn);
        report.Vehicle = vehicle.Name;
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

    /// <summary>
    /// Food a party takes along, in food units. The one place launch spends food: when ingredients become items, these
    /// become <c>Pantry.FoodValue(sim)</c> / <c>Pantry.TakeFoodValue(sim, amount)</c>.
    /// </summary>
    internal static class Rations
    {
        public static double Available(ColonySim sim) => sim.State.Stockpile.Get(sim.Data.Rules.Food.ResourceId);

        public static void Take(ColonySim sim, double amount) =>
            sim.State.Stockpile.Spend(new Dictionary<string, double> { [sim.Data.Rules.Food.ResourceId] = amount });
    }

    internal static CommandResult NoData() =>
        CommandResult.Fail(CommandError.NoScavengeData, "Scavenge data (items, zones) is not loaded.");
}
