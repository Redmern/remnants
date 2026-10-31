using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>Injury care formulas (see <see cref="MedicalRules"/>). Pure; used by the system and by UI queries.</summary>
public static class Care
{
    /// <summary>Severity removed per tick from each injured part of a patient at this Med Bay (with medicine).</summary>
    public static double HealPerTick(ColonyState state, GameData data, Building medBay)
    {
        var def = data.Buildings[medBay.DefId];
        var rules = data.Rules.Medical;
        var care = rules.UnstaffedCare;
        if (def.Job is { } jobId)
        {
            var job = data.Jobs[jobId];
            foreach (var (medic, shift) in Schedule.Crew(state, data, medBay))
                care += shift * JobOutput.Performance(job, medic, data.Rules.Output)
                    * JobOutput.ToolFactor(job, medic)
                    * JobOutput.HealthFactor(job, medic, data.Rules.Output)
                    * JobOutput.LifeFactor(medic, data.Rules.Output)
                    * JobOutput.MoodFactor(medic, data.Rules.Output)
                    * JobOutput.ConditionFactor(medic, data)
                    * JobOutput.PerkFactor(job, medic, data);
        }
        return rules.HealPerTick * def.Level(medBay.Level).OutputMultiplier * care;
    }

    /// <summary>Ticks until every injury is healed at the given rate, or null if it never heals at that rate.</summary>
    public static int? TicksToHeal(Survivor s, double healPerTick)
    {
        var worst = s.Body.Parts.Values.Where(p => p.Condition == PartCondition.Injured).Select(p => p.Severity).DefaultIfEmpty(0).Max();
        if (worst <= 0) return 0;
        return healPerTick <= 0 ? null : (int)Math.Ceiling(worst / healPerTick - 1e-9);
    }

    /// <summary>True when an untreated injury of this survivor is getting worse.</summary>
    public static bool IsWorsening(Survivor s, MedicalRules rules) =>
        s.PatientAtBuildingId is null && !s.Away
        && s.Body.Parts.Values.Any(p => p.Condition == PartCondition.Injured && p.Severity + 1e-9 >= rules.WorsenThreshold);
}

/// <summary>
/// Patients heal (consuming medicine); everyone else heals small injuries naturally while severe ones worsen,
/// losing limbs or draining health. Away survivors are not affected. Medics train while they treat.
/// </summary>
internal static class MedicalSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        var rules = data.Rules.Medical;
        var treatedAt = new HashSet<int>();
        var dead = new List<Survivor>();

        foreach (var s in state.Survivors.Where(s => !s.Away).OrderBy(s => s.Id).ToList())
        {
            if (s.PatientAtBuildingId is { } id && state.FindBuilding(id) is { } medBay)
            {
                if (Medicine.NeedsCare(s, data) && state.Stockpile.TryTake(rules.MedicineResourceId, rules.MedicinePerPatientTick))
                {
                    HealAll(s, data, Care.HealPerTick(state, data, medBay) * FoodEffects.HealingFactor(s, data));
                    treatedAt.Add(medBay.Id);
                }
                else HealAll(s, data, rules.NaturalHealPerTick * FoodEffects.HealingFactor(s, data));
                s.Health = Math.Min(Survivor.MaxHealth, s.Health + rules.PatientHealthPerTick);
                if (!Medicine.NeedsCare(s, data) && state.Surgeries.All(o => o.SurvivorId != s.Id))
                {
                    s.PatientAtBuildingId = null;
                    state.Log(new(state.Tick, ColonyEventType.Discharged, $"{s.Name} has healed and left the {data.Buildings[medBay.DefId].Name}.", s.Id, medBay.Id));
                }
                continue;
            }

            var healedAny = false;
            foreach (var (part, p) in s.Body.Parts.Where(kv => kv.Value.Condition == PartCondition.Injured).OrderBy(kv => kv.Key).ToList())
            {
                if (p.Severity < rules.NaturalHealMaxSeverity)
                {
                    p.Severity -= rules.NaturalHealPerTick * FoodEffects.HealingFactor(s, data) * Treatment.HealFactor(data.Medicine, s.Body, part);
                    if (p.Severity <= 1e-9)
                    {
                        s.Body.Heal(part);
                        healedAny = true;
                    }
                }
                else if (p.Severity + 1e-9 >= rules.WorsenThreshold)
                {
                    p.Severity = Math.Min(1, p.Severity + rules.WorsenPerTick);
                    var region = Body.RegionOf(part);
                    if (region is BodyRegion.Arms or BodyRegion.Legs)
                    {
                        if (p.Severity >= 1 - 1e-9)
                        {
                            s.Body.Lose(part);
                            state.Log(new(state.Tick, ColonyEventType.LimbLost, $"{s.Name} lost their {Describe(part)} to an untreated wound.", s.Id));
                        }
                    }
                    else s.Health -= rules.SevereHealthDrainPerTick;
                }
            }
            if (healedAny && !s.IsInjured)
                state.Log(new(state.Tick, ColonyEventType.Healed, $"{s.Name}'s injuries have healed.", s.Id));
            if (s.Health <= 0) dead.Add(s);
        }

        foreach (var s in dead) ColonyDeaths.Remove(state, data, s, "died of untreated injuries", "Untreated injuries");

        foreach (var medBay in state.Buildings.Where(b => treatedAt.Contains(b.Id)))
        {
            if (data.Buildings[medBay.DefId].Job is not { } jobId) continue;
            foreach (var (medic, _) in Schedule.Crew(state, data, medBay))
                Training.Work(state, data, data.Jobs[jobId], medic);
        }
    }

    private static void HealAll(Survivor s, GameData data, double amount)
    {
        foreach (var (part, p) in s.Body.Parts.Where(kv => kv.Value.Condition == PartCondition.Injured).ToList())
        {
            p.Severity -= amount * Treatment.HealFactor(data.Medicine, s.Body, part);
            if (p.Severity <= 1e-9) s.Body.Heal(part);
        }
    }

    internal static string Describe(BodyPart part) => part switch
    {
        BodyPart.LeftArm => "left arm",
        BodyPart.RightArm => "right arm",
        BodyPart.LeftLeg => "left leg",
        BodyPart.RightLeg => "right leg",
        _ => part.ToString().ToLowerInvariant(),
    };
}

/// <summary>Permanent removal of a survivor who died at home: event log and memorial.</summary>
internal static class ColonyDeaths
{
    public static void Remove(ColonyState state, GameData data, Survivor s, string how, string cause)
    {
        s.Health = 0;
        state.Survivors.Remove(s);
        state.Memorial.Add(new(s.Id, s.Name, state.Tick, cause));
        state.Log(new(state.Tick, ColonyEventType.Died, $"{s.Name} {how}.", s.Id));
        Bonds.OnGone(state, data, s, died: true);
    }
}

internal static class MedicalHandlers
{
    public static CommandResult Admit(ColonySim sim, AdmitPatient c)
    {
        var state = sim.State;
        var s = state.FindSurvivor(c.SurvivorId);
        if (s is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (s.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{s.Name} is away on an expedition.");
        if (!Medicine.NeedsCare(s, sim.Data)) return CommandResult.Fail(CommandError.NotInjured, $"{s.Name} has no injury or illness to treat.");
        if (s.PatientAtBuildingId is { } already)
            return CommandResult.Success($"{s.Name} is already a patient.", already);

        var medBays = sim.MedBays.ToList();
        if (c.BuildingId is { } wanted)
        {
            var b = state.FindBuilding(wanted);
            if (b is null) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {wanted}.");
            if (b.DefId != sim.Data.Rules.Medical.BuildingId) return CommandResult.Fail(CommandError.NoMedBay, $"{sim.Data.Buildings[b.DefId].Name} does not treat patients.");
            medBays = [b];
        }
        if (medBays.Count == 0) return CommandResult.Fail(CommandError.NoMedBay, "Build a Med Bay first.");
        var bay = medBays.FirstOrDefault(b => sim.FreePatientSlots(b) > 0);
        if (bay is null) return CommandResult.Fail(CommandError.MedBayFull, "The Med Bay has no free bed.");

        s.AssignedBuildingId = null;
        s.PatientAtBuildingId = bay.Id;
        var name = sim.Data.Buildings[bay.DefId].Name;
        state.Log(new(state.Tick, ColonyEventType.Admitted, $"{s.Name} was admitted to the {name}.", s.Id, bay.Id));
        return CommandResult.Success($"{s.Name} was admitted to the {name}.", bay.Id);
    }

    /// <summary>
    /// A party is home: whoever has a wound that is getting worse is carried straight into a Med Bay bed, the weakest
    /// first, before it bleeds them out. When the beds are full, a patient with nothing worsening and their health above
    /// half gives up the bed to them (they mend at the natural rate meanwhile). Nobody else is admitted.
    /// </summary>
    public static void Triage(ColonySim sim, IEnumerable<int> survivorIds)
    {
        var state = sim.State;
        var rules = sim.Data.Rules.Medical;
        if (!rules.TriageOnReturn || !sim.MedBays.Any()) return;
        bool Worsening(Survivor s) => s.Body.Parts.Values.Any(p => p.Condition == PartCondition.Injured && p.Severity + 1e-9 >= rules.WorsenThreshold);
        var hurt = survivorIds.Select(state.FindSurvivor).OfType<Survivor>()
            .Where(s => !s.Away && s.PatientAtBuildingId is null && Worsening(s)).OrderBy(s => s.Health).ThenBy(s => s.Id).ToList();
        foreach (var s in hurt)
        {
            if (!sim.MedBays.Any(b => sim.FreePatientSlots(b) > 0))
            {
                var room = state.Survivors.Where(p => p.PatientAtBuildingId is not null && !Worsening(p) && p.Health > Survivor.MaxHealth / 2
                                                      && state.Surgeries.All(o => o.SurvivorId != p.Id))
                    .OrderByDescending(p => p.Health).ThenBy(p => p.Id).FirstOrDefault();
                if (room is null) return;
                room.PatientAtBuildingId = null;
                state.Log(new(state.Tick, ColonyEventType.Discharged, $"{room.Name} gave up their bed for {s.Name}.", room.Id));
            }
            Admit(sim, new AdmitPatient(s.Id));
        }
    }

    public static CommandResult Discharge(ColonySim sim, Discharge c)
    {
        var s = sim.State.FindSurvivor(c.SurvivorId);
        if (s is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (s.PatientAtBuildingId is null) return CommandResult.Fail(CommandError.NotAPatient, $"{s.Name} is not a patient.");
        s.PatientAtBuildingId = null;
        sim.State.Log(new(sim.State.Tick, ColonyEventType.Discharged, $"{s.Name} was discharged.", s.Id));
        return CommandResult.Success($"{s.Name} was discharged.");
    }

    public static CommandResult FitProsthetic(ColonySim sim, FitProsthetic c)
    {
        var state = sim.State;
        if (sim.Scavenge is not { } data) return ExpeditionHandlers.NoData();
        var s = state.FindSurvivor(c.SurvivorId);
        if (s is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (s.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{s.Name} is away on an expedition.");
        if (!data.Items.TryGetValue(c.ItemId, out var item)) return CommandResult.Fail(CommandError.UnknownItem, $"No item '{c.ItemId}'.");
        if (item.Prosthetic is not { } prosthetic) return CommandResult.Fail(CommandError.NotAProsthetic, $"{item.Name} is not a prosthetic.");
        if (s.Body[c.Part].Condition != PartCondition.Missing)
            return CommandResult.Fail(CommandError.PartNotMissing, $"{s.Name}'s {MedicalSystem.Describe(c.Part)} is not missing.");
        if (c.Part == BodyPart.Head || Body.RegionOf(c.Part) != prosthetic.Region)
            return CommandResult.Fail(CommandError.WrongLimb, $"{item.Name} replaces {prosthetic.Region.ToString().ToLowerInvariant()}, not a {MedicalSystem.Describe(c.Part)}.");
        var level = sim.MedBays.Select(b => b.Level).DefaultIfEmpty(0).Max();
        if (level == 0) return CommandResult.Fail(CommandError.NoMedBay, "Build a Med Bay first.");
        if (level < prosthetic.MinMedBayLevel)
            return CommandResult.Fail(CommandError.MedBayLevelTooLow, $"Fitting a {item.Name} needs Med Bay level {prosthetic.MinMedBayLevel} (have {level}).");
        if (sim.Data.Cybernetics is { } cyber && cyber.Node(item.DefinedId) is { } node && node.Tier >= cyber.Surgery.FromTier)
            return CommandResult.Fail(CommandError.NeedsSurgery, $"A {item.Name} is {cyber.Tier(node.Tier).Name}: book it as surgery.");
        if (!state.Inventory.TryTake(item.Id)) return CommandResult.Fail(CommandError.MissingItems, $"No {item.Name} in the inventory.");

        Cybernetics.Fit(state, sim.Data.Cybernetics, s, c.Part, item, elective: false);
        var message = $"{s.Name} was fitted with a {item.Name} ({MedicalSystem.Describe(c.Part)}).";
        state.Log(new(state.Tick, ColonyEventType.ProstheticFitted, message, s.Id));
        return CommandResult.Success(message);
    }
}
