using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>The colony's standing orders for the Med Bay: which treatments it gives on its own, in which order.</summary>
public sealed class MedicalCare
{
    /// <summary>Enabled priorities, first = treated first. Null = the data's default order (all on).</summary>
    public List<TreatPriority>? Priorities { get; set; }
}

/// <summary>
/// The colony side of medicine (data/medicine.json): conditions run their course, dirty wounds get infected, painkiller
/// doses wear off, and each Med Bay treats its patients from the stocked supplies by the colony's priorities. Queries for
/// the UI live here too. See <see cref="Treatment"/> for what an item does to a body.
/// </summary>
public static class Medicine
{
    /// <summary>The kinds of condition a patient is admitted for (dependencies and passing effects are not).</summary>
    public static bool NeedsCare(Survivor s, GameData data) =>
        s.IsInjured || data.Medicine is { } med && s.Body.Conditions.Any(c => med.Condition(c.Id)?.Kind is ConditionKind.Infection or ConditionKind.Fracture or ConditionKind.Sickness);

    /// <summary>The colony's treatment order (enabled priorities, first first).</summary>
    public static IReadOnlyList<TreatPriority> Priorities(ColonyState state, GameData data) =>
        state.MedicalCare.Priorities ?? data.Medicine?.Def.MedBay.DefaultPriorities ?? [];

    /// <summary>What a priority would do for this survivor now: the condition or wound it is about, or null when nothing.</summary>
    public static bool Wants(MedicineData med, Survivor s, TreatPriority priority) => priority switch
    {
        TreatPriority.Bleeding => Treatment.Undressed(s.Body, Dressing.Bandaged).Any(p => s.Body[p].Severity + 1e-9 >= med.Infection.MinSeverity),
        TreatPriority.Infection => s.Body.Conditions.Any(c => med.Condition(c.Id)?.Kind == ConditionKind.Infection),
        TreatPriority.Fracture => Treatment.UnsplintedFracture(med, s.Body) is not null,
        TreatPriority.Sickness => s.Body.Conditions.Any(c => med.Condition(c.Id)?.Kind == ConditionKind.Sickness),
        TreatPriority.Pain => Treatment.InPain(med, s.Body) && s.Body.Doses + 1 + 1e-9 < med.Painkiller.DependencyDoses
                              && !s.Body.Has(med.Painkiller.DependencyCondition),
        _ => false,
    };

    /// <summary>Whether an item serves a priority (dressings for bleeding, cures for infections and sicknesses, splints, painkillers).</summary>
    public static bool Serves(MedicineData med, UseEffect use, TreatPriority priority) => priority switch
    {
        TreatPriority.Bleeding => use.Dress != Dressing.None,
        TreatPriority.Infection => use.Cures.Any(id => med.Condition(id)?.Kind == ConditionKind.Infection),
        TreatPriority.Fracture => use.Splint,
        TreatPriority.Sickness => use.Cures.Any(id => med.Condition(id)?.Kind == ConditionKind.Sickness),
        TreatPriority.Pain => use.PainRelief > 0,
        _ => false,
    };

    /// <summary>
    /// Stocked items that would treat this survivor for a priority, best for the job first: for bleeding a clean dressing
    /// before a bandage, otherwise the plainest item that does it (no stims, no injectors, no blood bags), worst quality first.
    /// </summary>
    public static IEnumerable<string> SuppliesFor(ColonyState state, MedicineData med, ScavengeData items, Survivor s, TreatPriority priority)
    {
        int Rank(ItemDef def) => items.Rules.Quality.FindIndex(q => q == items.QualityOf(def));
        return state.Inventory.Counts.Keys
            .Where(id => items.Items.TryGetValue(id, out var def) && def.Use is { } use && use.StimAp == 0 && use.HumanityCost == 0
                         && use.ReviveHp == 0 && Serves(med, use, priority) && Treatment.Helps(med, s.Body, use))
            .Select(id => items.Items[id])
            .OrderByDescending(def => priority == TreatPriority.Bleeding ? (int)def.Use!.Dress : 0)
            .ThenBy(Rank).ThenBy(def => def.Id, StringComparer.Ordinal)
            .Select(def => def.Id);
    }

    /// <summary>The Body tab's line for a condition: its name, severity and what helps.</summary>
    public static string Suggestion(MedicineData med, BodyCondition c) =>
        med.Condition(c.Id) is { } def ? (c.Treated && def.Kind == ConditionKind.Fracture ? "Splinted: it knits faster." : def.Treatment) : "";

    /// <summary>The kit a surgery of this tier would use now, and the success chance it adds (times its quality).</summary>
    public static (string ItemId, double Bonus)? SurgeryKit(ColonyState state, GameData data, ScavengeData items, int tier)
    {
        if (data.Medicine is not { } med) return null;
        foreach (var kit in med.Def.Surgery.Kits.Where(k => tier >= k.FromTier && tier <= k.ToTier))
        {
            var id = state.Inventory.Counts.Keys
                .Where(i => items.Items.TryGetValue(i, out var def) && def.DefinedId == kit.Item)
                .OrderByDescending(i => items.QualityOf(items.Items[i])?.StatMultiplier ?? 1).ThenBy(i => i, StringComparer.Ordinal)
                .FirstOrDefault();
            if (id is not null) return (id, kit.SuccessBonus * (items.QualityOf(items.Items[id])?.StatMultiplier ?? 1));
        }
        return null;
    }

    /// <summary>Who would give blood for a blood bag now: the healthiest adult at home, not a patient, above the minimum (then lowest id).</summary>
    public static Survivor? Donor(ColonyState state, GameData data, double cost) =>
        data.Medicine is not { } med ? null
            : state.Present.Where(s => !s.IsChild && s.PatientAtBuildingId is null && s.Health - cost + 1e-9 >= med.Def.Donor.MinHealth)
                .OrderByDescending(s => s.Health).ThenBy(s => s.Id).FirstOrDefault();
}

/// <summary>
/// The one entry point for getting sick (food-cooking calls it for bad or spoiled food and Husk meat). <paramref name="kind"/>
/// is a sickness kind from medicine.json <c>sicknessKinds</c> (food, spoiled, husk) or a condition id.
/// </summary>
public static class Sickness
{
    public static BodyCondition? Apply(ColonyState state, GameData data, Survivor survivor, string kind, double severity)
    {
        if (data.Medicine is not { } med || severity <= 0) return null;
        var id = med.Def.SicknessKinds.GetValueOrDefault(kind, kind);
        if (med.Condition(id) is not { } def) return null;
        var had = survivor.Body.Has(id);
        var c = Treatment.Afflict(survivor.Body, id, severity);
        if (!had) state.Log(new(state.Tick, ColonyEventType.FellSick, $"{survivor.Name} is down with {def.Name.ToLowerInvariant()}.", survivor.Id));
        return c;
    }
}

internal static class MedicineSystem
{
    public static void Run(ColonySim sim, DeterministicRng rng)
    {
        var state = sim.State;
        var data = sim.Data;
        if (data.Medicine is not { } med) return;
        var medicineId = data.Rules.Medical.MedicineResourceId;
        var dead = new List<(Survivor S, string Cause)>();

        foreach (var s in state.Survivors.Where(s => !s.Away).OrderBy(s => s.Id))
        {
            var body = s.Body;
            var onMedicine = s.PatientAtBuildingId is not null && state.Stockpile.Has(medicineId, 1e-9);
            Infect(state, med, s, rng);
            foreach (var c in body.Conditions.ToList())
            {
                if (med.Condition(c.Id) is not { } def) continue;
                switch (def.Kind)
                {
                    case ConditionKind.Effect:
                        continue;
                    case ConditionKind.Fracture:
                        continue;
                    case ConditionKind.Infection:
                        c.Severity += onMedicine
                            ? -def.TreatedRecoverPerTick * med.Def.MedBay.MedicineEasesInfection
                            : def.GrowPerTick - def.RecoverPerTick;
                        break;
                    default:
                        c.Severity -= onMedicine ? Math.Max(def.RecoverPerTick, def.TreatedRecoverPerTick) : def.RecoverPerTick;
                        break;
                }
                c.Severity = Math.Min(1, c.Severity);
                if (def.MoodPerTick != 0) s.Mood = Math.Clamp(s.Mood + def.MoodPerTick * c.Severity, 0, Survivor.MaxMood);
                if (c.Severity + 1e-9 >= def.HealthDrainFrom) s.Health -= def.HealthDrainPerTick;
                if (c.Severity <= 1e-9)
                {
                    body.Conditions.Remove(c);
                    state.Log(new(state.Tick, ColonyEventType.Recovered, $"{s.Name} is over the {def.Name.ToLowerInvariant()}.", s.Id));
                }
                if (s.Health <= 0 && dead.All(d => d.S != s)) dead.Add((s, def.Name));
            }
            Treatment.CountDown(body);
            body.Doses = Math.Max(0, body.Doses - med.Painkiller.DoseDecayPerTick);
        }
        foreach (var (s, cause) in dead) ColonyDeaths.Remove(state, data, s, $"died of {cause.ToLowerInvariant()}", cause);

        if (sim.Scavenge is { } items) AutoTreat(sim, med, items);
    }

    /// <summary>Each dirty wound of a survivor at home may get infected (patients less often, clean wounds never).</summary>
    private static void Infect(ColonyState state, MedicineData med, Survivor s, DeterministicRng rng)
    {
        var rules = med.Infection;
        foreach (var (part, p) in s.Body.Parts.Where(kv => kv.Value.Condition == PartCondition.Injured).OrderBy(kv => kv.Key))
        {
            if (p.Severity + 1e-9 < rules.MinSeverity || s.Body.Find(rules.Condition, part) is not null) continue;
            var chance = rules.ChancePerTick * p.Dressing switch
            {
                Dressing.Clean => rules.CleanMultiplier,
                Dressing.Bandaged => rules.BandagedMultiplier,
                _ => 1,
            } * (s.PatientAtBuildingId is null ? 1 : rules.PatientMultiplier);
            if (chance <= 0 || SideRoll.Next(rng, state.Tick, s.Id, (long)part) >= chance) continue;
            Treatment.Afflict(s.Body, rules.Condition, rules.StartSeverity + p.Severity * rules.StartSeverity, part);
            state.Log(new(state.Tick, ColonyEventType.Infected, $"{s.Name}'s {MedicalSystem.Describe(part)} wound is infected.", s.Id));
        }
    }

    /// <summary>
    /// Each Med Bay gives up to <c>treatmentsPerTick</c> treatments a tick: for each priority in order, each patient (by id)
    /// who needs it and has a stocked item that does it.
    /// </summary>
    private static void AutoTreat(ColonySim sim, MedicineData med, ScavengeData items)
    {
        var state = sim.State;
        var priorities = Medicine.Priorities(state, sim.Data);
        foreach (var bay in sim.MedBays)
        {
            var left = med.Def.MedBay.TreatmentsPerTick;
            var patients = state.PatientsAt(bay.Id).Where(p => !p.Away).OrderBy(p => p.Id).ToList();
            foreach (var priority in priorities)
            {
                if (left <= 0) break;
                foreach (var patient in patients.Where(p => Medicine.Wants(med, p, priority)))
                {
                    if (left <= 0) break;
                    if (Medicine.SuppliesFor(state, med, items, patient, priority).FirstOrDefault() is not { } itemId) continue;
                    Use(state, sim.Data, med, items, patient, itemId, auto: true);
                    left--;
                }
            }
        }
    }

    /// <summary>Uses one item from the inventory on a survivor at home. Returns what it did, or null when it did nothing.</summary>
    internal static string? Use(ColonyState state, GameData data, MedicineData? med, ScavengeData items, Survivor s, string itemId, bool auto)
    {
        var item = items.Items[itemId];
        var use = item.Use!;
        var done = Treatment.Apply(med, s.Body, use, field: false);
        if (use.Heal > 0 && s.Health < Survivor.MaxHealth)
        {
            s.Health = Math.Min(Survivor.MaxHealth, s.Health + use.Heal);
            done.Add("patched them up");
        }
        if (done.Count == 0) return null;
        if (use.HumanityCost > 0) s.Body.HumanityLoss += use.HumanityCost;
        state.Inventory.TryTake(itemId);
        var message = $"{(auto ? "The Med Bay" : "Someone")} used {item.Name} on {s.Name}: {string.Join(", ", done)}.";
        state.Log(new(state.Tick, ColonyEventType.Treated, message, s.Id));
        return message;
    }
}

internal static class MedicineHandlers
{
    /// <summary>Use a medical item from the inventory on a survivor at home (a player override of the Med Bay's choice).</summary>
    public static CommandResult Treat(ColonySim sim, TreatSurvivor c)
    {
        var state = sim.State;
        if (sim.Scavenge is not { } items) return ExpeditionHandlers.NoData();
        var s = state.FindSurvivor(c.SurvivorId);
        if (s is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (s.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{s.Name} is away on an expedition.");
        if (!items.Items.TryGetValue(c.ItemId, out var item)) return CommandResult.Fail(CommandError.UnknownItem, $"No item '{c.ItemId}'.");
        if (item.Use is not { } use || !use.Treats || use.StimAp > 0 || use.ReviveHp > 0)
            return CommandResult.Fail(CommandError.NotATreatment, $"{item.Name} is not used at home.");
        if (!state.Inventory.Has(item.Id)) return CommandResult.Fail(CommandError.MissingItems, $"No {item.Name} in the inventory.");
        return MedicineSystem.Use(state, sim.Data, sim.Data.Medicine, items, s, item.Id, auto: false) is { } message
            ? CommandResult.Success(message)
            : CommandResult.Fail(CommandError.NothingToTreat, $"{item.Name} would do nothing for {s.Name} now.");
    }

    /// <summary>Set the Med Bay's treatment order; priorities left out are not treated on its own. Null = the default.</summary>
    public static CommandResult SetPriorities(ColonySim sim, SetCarePriorities c)
    {
        if (c.Priorities is { } list && list.Distinct().Count() != list.Count)
            return CommandResult.Fail(CommandError.InvalidCount, "A priority is listed twice.");
        sim.State.MedicalCare.Priorities = c.Priorities is null ? null : [.. c.Priorities];
        var order = Medicine.Priorities(sim.State, sim.Data);
        var message = order.Count == 0 ? "The Med Bay will not treat anyone on its own." : $"The Med Bay treats: {string.Join(" → ", order)}.";
        return CommandResult.Success(message);
    }
}
