using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>A booked operation: fit <see cref="ItemId"/> on a survivor's part at a Med Bay (see <see cref="ScheduleSurgery"/>).</summary>
public sealed class Surgery
{
    public int Id { get; set; }
    public int SurvivorId { get; set; }
    public int BuildingId { get; set; }
    public BodyPart Part { get; set; }

    /// <summary>The prosthetic, set aside from the inventory while the surgery is booked.</summary>
    public string ItemId { get; set; } = "";

    /// <summary>On a healthy part (or replacing an elective one): an augmentation.</summary>
    public bool Elective { get; set; }
    public double Progress { get; set; }
}

/// <summary>Where a node of the cybernetics tree stands for the colony.</summary>
public enum CyberNodeState
{
    /// <summary>A requirement is missing (a lower-tier design, the perk, the blueprint or the building level).</summary>
    Locked,
    /// <summary>Every requirement is met: its recipe can be queued.</summary>
    Available,
    /// <summary>Available, and the colony has crafted, fitted or owned one.</summary>
    Known,
}

/// <summary>A node's state, what is missing, the recipe that makes it and how many the colony has.</summary>
public sealed record CyberNodeStatus(CyberNodeDef Node, CyberNodeState State, IReadOnlyList<string> Missing, RecipeDef? Recipe,
    int Owned, int Fitted);

/// <summary>
/// Cybernetics rules for the colony (data in <see cref="CyberneticsData"/>): the tree (which designs can be made), surgery
/// odds and speed, humanity, and the effects of fitted parts. Pure queries; <see cref="CyberneticsSystem"/> runs the
/// ticking parts and <see cref="CyberneticsHandlers"/> the commands. See ARCHITECTURE.md "Cybernetics".
/// </summary>
public static class Cybernetics
{
    /// <summary>
    /// Effects of the body's working prosthetics (efficiency above 0, so not locked by interference) and of every paired
    /// set whose parts all work.
    /// </summary>
    public static IEnumerable<CyberEffectsDef> WorkingEffects(CyberneticsData cyber, Body body)
    {
        foreach (var (_, part) in body.Parts.OrderBy(kv => kv.Key))
            if (part.Condition == PartCondition.Prosthetic && part.Efficiency > 0 && cyber.Node(part.ProstheticItemId) is { } node)
                yield return node.Effects;
        foreach (var (set, parts) in cyber.ActiveSets(body))
            if (parts.All(p => body[p].Efficiency > 0)) yield return set.Effects;
    }

    /// <summary>Tier-0 designs are known from the start; the rest once crafted, fitted or owned.</summary>
    public static bool IsKnown(ColonyState state, CyberNodeDef node) => node.Tier == 0 || state.KnownCybernetics.Contains(node.Id);

    /// <summary>The recipe that makes this design, or null for loot-only parts.</summary>
    public static RecipeDef? RecipeFor(GameData data, CyberNodeDef node) => data.Recipes.Values.FirstOrDefault(r => r.Item == node.Id);

    /// <summary>Someone at home (not away) has this perk.</summary>
    public static bool ColonyHasPerk(ColonyState state, string perkId) => state.Survivors.Any(s => !s.Away && s.HasPerk(perkId));

    /// <summary>A design's state for the tree view, with every missing requirement as a short line.</summary>
    public static CyberNodeStatus Status(ColonySim sim, CyberNodeDef node)
    {
        var state = sim.State;
        var missing = Missing(sim, node);
        var recipe = RecipeFor(sim.Data, node);
        if (recipe is not null)
        {
            var at = sim.Data.Buildings[recipe.Building];
            if (!state.Buildings.Any(b => b.DefId == at.Id && b.Level >= recipe.MinLevel)) missing.Add($"{at.Name} level {recipe.MinLevel}");
            if (recipe.Blueprint is { } bp && !state.KnownBlueprints.Contains(bp))
                missing.Add($"{sim.Scavenge?.Items.GetValueOrDefault(bp)?.Name ?? bp} studied");
        }
        else if (!IsKnown(state, node)) missing.Add("found as loot only");
        var owned = sim.Scavenge is null ? 0 : state.Inventory.Counts.Where(kv => sim.Scavenge.Items.TryGetValue(kv.Key, out var i) && i.DefinedId == node.Id).Sum(kv => kv.Value);
        var fitted = state.Survivors.Sum(s => s.Body.Parts.Values.Count(p => p.Condition == PartCondition.Prosthetic && p.ProstheticItemId == node.Id));
        // Owning a part counts even when it could not be made here (loot, a recipe still locked).
        var nodeState = IsKnown(state, node) && (missing.Count == 0 || recipe is null) ? CyberNodeState.Known
            : missing.Count > 0 ? CyberNodeState.Locked : CyberNodeState.Available;
        return new CyberNodeStatus(node, nodeState, missing, recipe, owned, fitted);
    }

    /// <summary>
    /// The way into a design: one of its lower-tier requirements known, or its blueprint studied (a blueprint skips the
    /// tier below). Tier 0 has no requirement.
    /// </summary>
    public static bool WayIn(ColonyState state, CyberneticsData cyber, CyberNodeDef node) =>
        node.Requires.Count == 0 || node.Requires.Any(r => IsKnown(state, cyber.Nodes[r]))
        || node.Blueprint is { } bp && state.KnownBlueprints.Contains(bp);

    /// <summary>Tree requirements only (a way in, the perk): what crafting checks on top of the recipe.</summary>
    private static List<string> Missing(ColonySim sim, CyberNodeDef node)
    {
        var state = sim.State;
        var cyber = sim.Data.Cybernetics!;
        var missing = new List<string>();
        if (!WayIn(state, cyber, node))
        {
            var know = $"know {string.Join(" or ", node.Requires.Select(r => sim.Scavenge?.Items.GetValueOrDefault(r)?.Name ?? r))}";
            missing.Add(node.Blueprint is { } bp ? $"{know}, or study {sim.Scavenge?.Items.GetValueOrDefault(bp)?.Name ?? bp}" : know);
        }
        if (node.Perk is { } perk && !ColonyHasPerk(state, perk))
            missing.Add($"someone with {sim.Data.Perks.GetValueOrDefault(perk)?.Name ?? perk}");
        return missing;
    }

    /// <summary>For a recipe that makes a cybernetics design: a failure when its tree requirements are not met, else null.</summary>
    public static CommandResult? CraftGate(ColonySim sim, RecipeDef recipe)
    {
        if (sim.Data.Cybernetics?.Node(recipe.Item) is not { } node) return null;
        var missing = Missing(sim, node);
        return missing.Count == 0 ? null
            : CommandResult.Fail(CommandError.RecipeLocked, $"{recipe.Name} needs {string.Join(", ", missing)}.");
    }

    /// <summary>The medic who leads surgery at a bay: the best Medicine skill among its workers at home (then lowest id).</summary>
    public static Survivor? LeadSurgeon(ColonyState state, Building bay) =>
        state.WorkersAt(bay.Id).Where(w => !w.Away).OrderByDescending(w => w.Get(Skill.Medicine)).ThenBy(w => w.Id).FirstOrDefault();

    /// <summary>
    /// Surgery effort per tick at a bay: its medics' care (as for healing, without the unstaffed part) times the level
    /// multiplier and the lead's <c>surgery_speed_bonus</c>. 0 without a medic.
    /// </summary>
    public static double EffortPerTick(ColonySim sim, Building bay)
    {
        var data = sim.Data;
        var def = data.Buildings[bay.DefId];
        if (def.Job is not { } jobId || LeadSurgeon(sim.State, bay) is not { } lead) return 0;
        var job = data.Jobs[jobId];
        var care = sim.State.WorkersAt(bay.Id).Where(w => !w.Away).Sum(medic =>
            JobOutput.Performance(job, medic, data.Rules.Output)
            * JobOutput.ToolFactor(job, medic)
            * JobOutput.HealthFactor(job, medic, data.Rules.Output)
            * JobOutput.LifeFactor(medic, data.Rules.Output)
            * JobOutput.MoodFactor(medic, data.Rules.Output)
            * JobOutput.PerkFactor(job, medic, data));
        return care * def.Level(bay.Level).OutputMultiplier * (1 + PerkEffects.For(lead, data).Hook(PerkHooks.SurgerySpeedBonus));
    }

    /// <summary>Why surgery of this tier cannot go ahead at the bay now (no medic, no Cyber-Surgeon), or null.</summary>
    public static string? Blocked(ColonySim sim, Building bay, int tier)
    {
        var lead = LeadSurgeon(sim.State, bay);
        if (lead is null) return "needs a medic at the Med Bay";
        if (tier >= sim.Data.Cybernetics!.Surgery.CyberSurgeonFromTier && !PerkEffects.For(lead, sim.Data).Has(PerkHooks.CyberSurgery))
            return "needs a Cyber-Surgeon at the Med Bay";
        return null;
    }

    /// <summary>The chance a surgery of this tier succeeds at this bay now (see <see cref="SurgeryRules"/>).</summary>
    public static double SuccessChance(ColonySim sim, Building bay, int tier, bool elective)
    {
        var rules = sim.Data.Cybernetics!.Surgery;
        var lead = LeadSurgeon(sim.State, bay);
        var chance = rules.BaseChance
            + (lead?.Get(Skill.Medicine) ?? 0) * rules.PerMedicine
            + (lead is null ? 0 : PerkEffects.For(lead, sim.Data).Hook(PerkHooks.SurgerySuccessBonus))
            - rules.TierPenalty[tier]
            - (elective ? rules.ElectivePenalty : 0);
        return Math.Clamp(chance, rules.MinChance, rules.MaxChance);
    }

    /// <summary>Highest mood a survivor of this humanity can have (100 from <c>moodCapFrom</c> up).</summary>
    public static double MoodCap(HumanityRules rules, double humanity) =>
        humanity >= rules.MoodCapFrom ? Survivor.MaxMood
            : rules.MoodCapAtZero + (Survivor.MaxMood - rules.MoodCapAtZero) * Math.Max(0, humanity) / rules.MoodCapFrom;

    /// <summary>Effort a surgery of this tier takes.</summary>
    public static double Work(CyberneticsData cyber, int tier) => cyber.Surgery.Work[Math.Clamp(tier, 0, cyber.Surgery.Work.Count - 1)];

    /// <summary>
    /// Puts a prosthetic on a part (whatever it had) with its humanity cost and knowledge; returns the item id of the
    /// prosthetic it replaced, if any.
    /// </summary>
    public static string? Fit(ColonyState state, CyberneticsData? cyber, Survivor s, BodyPart part, ItemDef item, bool elective)
    {
        var old = s.Body[part];
        var replaced = old.Condition == PartCondition.Prosthetic ? old.ProstheticItemId : null;
        s.Body.Fit(part, item.Prosthetic!.Quality, item.Id);
        if (cyber?.Node(item.DefinedId) is { } node)
        {
            s.Body[part].Elective = elective;
            s.Body[part].HumanityCost = cyber.HumanityCost(node.Tier, elective) ?? cyber.Tier(node.Tier).Humanity;
            state.KnownCybernetics.Add(node.Id);
        }
        return replaced;
    }
}

/// <summary>
/// Each tick: the colony learns the designs it owns; each Med Bay works its first booked surgery; powered prosthetics of
/// survivors at home are serviced from the stockpile or wear down; low humanity caps mood, strains couples and children
/// and now and then shows in the log.
/// </summary>
internal static class CyberneticsSystem
{
    public static void Run(ColonySim sim, DeterministicRng rng)
    {
        if (sim.Data.Cybernetics is not { } cyber) return;
        var state = sim.State;
        Learn(state, cyber, sim.Scavenge);
        Operate(sim, cyber, rng);
        Upkeep(state, sim.Data, cyber);
        Humanity(state, sim.Data, cyber, rng);
    }

    private static void Learn(ColonyState state, CyberneticsData cyber, ScavengeData? items)
    {
        foreach (var id in state.Inventory.Counts.Keys)
            if (cyber.Node(items?.Items.GetValueOrDefault(id)?.DefinedId ?? id) is { } node) state.KnownCybernetics.Add(node.Id);
        foreach (var op in state.Surgeries)
            if (cyber.Node(items?.Items.GetValueOrDefault(op.ItemId)?.DefinedId ?? op.ItemId) is { } node) state.KnownCybernetics.Add(node.Id);
        foreach (var part in state.Survivors.SelectMany(s => s.Body.Parts.Values))
            if (part.Condition == PartCondition.Prosthetic && cyber.Node(part.ProstheticItemId) is { } node) state.KnownCybernetics.Add(node.Id);
    }

    private static void Operate(ColonySim sim, CyberneticsData cyber, DeterministicRng rng)
    {
        var state = sim.State;
        foreach (var gone in state.Surgeries.Where(o => state.FindSurvivor(o.SurvivorId) is null).ToList())
        {
            state.Inventory.Add(gone.ItemId);
            state.Surgeries.Remove(gone);
        }
        if (sim.Scavenge is not { } items) return;
        var medicine = sim.Data.Rules.Medical.MedicineResourceId;
        foreach (var bay in sim.MedBays)
        {
            if (state.Surgeries.FirstOrDefault(o => o.BuildingId == bay.Id) is not { } op) continue;
            var s = state.FindSurvivor(op.SurvivorId)!;
            var item = items.Items[op.ItemId];
            var tier = cyber.Node(item.DefinedId)?.Tier ?? 0;
            if (s.Away || Cybernetics.Blocked(sim, bay, tier) is not null) continue;
            var effort = Cybernetics.EffortPerTick(sim, bay);
            if (effort <= 0 || !state.Stockpile.TryTake(medicine, cyber.Surgery.MedicinePerTick)) continue;
            op.Progress += effort;
            foreach (var medic in state.WorkersAt(bay.Id).Where(w => !w.Away).OrderBy(w => w.Id))
                Training.Work(state, sim.Data, sim.Data.Jobs[sim.Data.Buildings[bay.DefId].Job!], medic);
            if (op.Progress + 1e-9 < Cybernetics.Work(cyber, tier)) continue;

            var chance = Cybernetics.SuccessChance(sim, bay, tier, op.Elective);
            var success = rng.NextDouble() < chance;
            state.Surgeries.Remove(op);
            if (Cybernetics.Fit(state, cyber, s, op.Part, item, op.Elective) is { } replaced) state.Inventory.Add(replaced);
            var where = MedicalSystem.Describe(op.Part);
            if (success)
                state.Log(new(state.Tick, ColonyEventType.SurgeryDone, $"{s.Name}'s surgery went well: a {item.Name} ({where}).", s.Id, bay.Id));
            else
            {
                var range = cyber.Surgery.ComplicationSeverity;
                s.Body.Injure(BodyPart.Torso, range[0] + rng.NextDouble() * (range[1] - range[0]));
                state.Log(new(state.Tick, ColonyEventType.SurgeryComplication,
                    $"{s.Name}'s {item.Name} ({where}) is on, but the wound is infected. They stay in bed.", s.Id, bay.Id));
            }
            if (!s.IsInjured && state.Surgeries.All(o => o.SurvivorId != s.Id)) s.PatientAtBuildingId = null;
        }
    }

    private static void Upkeep(ColonyState state, GameData data, CyberneticsData cyber)
    {
        var perDay = data.Rules.TicksPerDay;
        foreach (var s in state.Survivors.Where(s => !s.Away).OrderBy(s => s.Id))
            foreach (var (partId, part) in s.Body.Parts.OrderBy(kv => kv.Key))
            {
                if (part.Condition != PartCondition.Prosthetic) continue;
                var tier = cyber.Tier(cyber.TierOf(part));
                if (tier.ServiceDays <= 0) continue;
                part.ServiceTicks++;
                if (part.ServiceTicks < tier.ServiceDays * perDay) continue;
                var name = MedicalSystem.Describe(partId);
                if (state.Stockpile.CanAfford(tier.ServiceCost))
                {
                    state.Stockpile.Spend(tier.ServiceCost);
                    var was = part.Upkeep;
                    part.ServiceTicks = 0;
                    part.Upkeep = 1;
                    if (was < 1)
                        state.Log(new(state.Tick, ColonyEventType.ProstheticServiced, $"{s.Name}'s {name} has been serviced and works properly again.", s.Id));
                    continue;
                }
                if (part.Upkeep >= 1)
                    state.Log(new(state.Tick, ColonyEventType.ProstheticNeedsService,
                        $"{s.Name}'s {name} needs a service ({Cost(data, tier.ServiceCost)}); it wears down until then.", s.Id));
                part.Upkeep = Math.Max(cyber.Def.Upkeep.MinUpkeep, part.Upkeep - cyber.Def.Upkeep.DecayPerDay / perDay);
            }
    }

    private static string Cost(GameData data, Dictionary<string, double> cost) =>
        string.Join(", ", cost.Select(kv => $"{kv.Value:0.#} {data.Resources.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}"));

    private static void Humanity(ColonyState state, GameData data, CyberneticsData cyber, DeterministicRng rng)
    {
        var rules = cyber.Humanity;
        var home = state.Survivors.Where(s => !s.Away).OrderBy(s => s.Id).ToList();
        foreach (var s in home)
        {
            var humanity = s.Body.Humanity;
            if (humanity < rules.MoodCapFrom)
                s.Mood = Math.Min(s.Mood, Cybernetics.MoodCap(rules, humanity));
            if (s.IsChild && Parents(state, s).Any(p => p.Body.Humanity < rules.Detached))
                s.Mood = Math.Min(s.Mood, rules.ChildMoodCap);
        }

        foreach (var s in home.Where(s => s.Body.Humanity < rules.Detached && s.PartnerId is not null))
        {
            if (state.FindSurvivor(s.PartnerId!.Value) is not { } partner || state.FindRelationship(s.Id, partner.Id) is not { } bond) continue;
            bond.Affinity -= rules.PartnerStrainPerTick * (rules.Detached - s.Body.Humanity) / rules.Detached;
            if (bond.Affinity < rules.BreakupBelow)
            {
                Bonds.BreakUp(state, data, s, partner);
                state.Log(new(state.Tick, ColonyEventType.Humanity, $"{partner.Name} says {s.Name} is not the person they fell for.", s.Id));
            }
        }

        if (rules.LineEveryTicks <= 0 || rules.Lines.Count == 0 || state.Tick % rules.LineEveryTicks != 0) return;
        foreach (var s in home.Where(s => s.Body.Humanity < rules.Detached))
            if (rng.NextDouble() < rules.LineChance)
                state.Log(new(state.Tick, ColonyEventType.Humanity, rng.Pick(rules.Lines).Replace("{name}", s.Name), s.Id));
    }

    private static IEnumerable<Survivor> Parents(ColonyState state, Survivor child) =>
        new[] { child.MotherId, child.FatherId }.OfType<int>().Select(state.FindSurvivor).OfType<Survivor>().Where(p => !p.Away);
}

internal static class CyberneticsHandlers
{
    public static CommandResult Schedule(ColonySim sim, ScheduleSurgery c)
    {
        var state = sim.State;
        if (sim.Scavenge is not { } items) return ExpeditionHandlers.NoData();
        if (sim.Data.Cybernetics is not { } cyber) return CommandResult.Fail(CommandError.NoCybernetics, "No cybernetics data is loaded.");
        var s = state.FindSurvivor(c.SurvivorId);
        if (s is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (s.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{s.Name} is away on an expedition.");
        if (!items.Items.TryGetValue(c.ItemId, out var item)) return CommandResult.Fail(CommandError.UnknownItem, $"No item '{c.ItemId}'.");
        if (item.Prosthetic is not { } prosthetic || cyber.Node(item.DefinedId) is not { } node)
            return CommandResult.Fail(CommandError.NotAProsthetic, $"{item.Name} is not a prosthetic.");
        var where = MedicalSystem.Describe(c.Part);
        if (c.Part == BodyPart.Head || Body.RegionOf(c.Part) != prosthetic.Region)
            return CommandResult.Fail(CommandError.WrongLimb, $"A {item.Name} does not fit the {where}.");
        var part = s.Body[c.Part];
        if (part.Condition == PartCondition.Injured)
            return CommandResult.Fail(CommandError.PartInjured, $"{s.Name}'s {where} is injured: let it heal first.");
        var elective = part.Condition == PartCondition.Healthy || part.Condition == PartCondition.Prosthetic && part.Elective;
        if (part.Condition == PartCondition.Healthy)
        {
            if (s.IsChild) return CommandResult.Fail(CommandError.ElectiveNotAllowed, $"{s.Name} is a child: no augmentation.");
            if (node.Tier < cyber.Surgery.ElectiveFromTier || cyber.Tier(node.Tier).ElectiveHumanity is null)
                return CommandResult.Fail(CommandError.ElectiveNotAllowed,
                    $"Only {cyber.Tier(cyber.Surgery.ElectiveFromTier).Name} parts and up may replace a healthy {where}.");
            if (!c.Elective)
                return CommandResult.Fail(CommandError.ElectiveNotAllowed,
                    $"{s.Name}'s {where} is healthy: replacing it is an augmentation (−{cyber.Tier(node.Tier).ElectiveHumanity:0} humanity). Confirm to go ahead.");
        }
        if (state.Surgeries.Any(o => o.SurvivorId == s.Id && o.Part == c.Part))
            return CommandResult.Fail(CommandError.AlreadyBooked, $"{s.Name}'s {where} is already booked for surgery.");

        var bays = sim.MedBays.Where(b => b.Level >= prosthetic.MinMedBayLevel).ToList();
        if (!sim.MedBays.Any()) return CommandResult.Fail(CommandError.NoMedBay, "Build a Med Bay first.");
        if (c.BuildingId is { } wanted) bays = bays.Where(b => b.Id == wanted).ToList();
        if (bays.Count == 0)
            return CommandResult.Fail(CommandError.MedBayLevelTooLow, $"Fitting a {item.Name} needs Med Bay level {prosthetic.MinMedBayLevel}.");
        var bay = bays.FirstOrDefault(b => s.PatientAtBuildingId == b.Id) ?? bays.FirstOrDefault(b => sim.FreePatientSlots(b) > 0);
        if (bay is null) return CommandResult.Fail(CommandError.MedBayFull, "The Med Bay has no free bed.");
        if (!state.Inventory.TryTake(item.Id)) return CommandResult.Fail(CommandError.MissingItems, $"No {item.Name} in the inventory.");

        s.AssignedBuildingId = null;
        s.PatientAtBuildingId = bay.Id;
        var op = new Surgery { Id = state.NextSurgeryId++, SurvivorId = s.Id, BuildingId = bay.Id, Part = c.Part, ItemId = item.Id, Elective = elective };
        state.Surgeries.Add(op);
        var message = $"{s.Name} is booked for surgery: a {item.Name} ({where}){(elective ? ", an augmentation" : "")}.";
        state.Log(new(state.Tick, ColonyEventType.SurgeryBooked, message, s.Id, bay.Id));
        return CommandResult.Success(message, op.Id);
    }

    public static CommandResult Cancel(ColonySim sim, CancelSurgery c)
    {
        var state = sim.State;
        if (state.Surgeries.Find(o => o.Id == c.SurgeryId) is not { } op)
            return CommandResult.Fail(CommandError.UnknownSurgery, $"No surgery {c.SurgeryId}.");
        state.Surgeries.Remove(op);
        state.Inventory.Add(op.ItemId);
        var s = state.FindSurvivor(op.SurvivorId);
        if (s is not null && !s.IsInjured && state.Surgeries.All(o => o.SurvivorId != s.Id)) s.PatientAtBuildingId = null;
        var message = s is null ? "A surgery was called off." : $"{s.Name}'s surgery was called off.";
        state.Log(new(state.Tick, ColonyEventType.SurgeryCancelled, message, s?.Id));
        return CommandResult.Success(message);
    }
}
