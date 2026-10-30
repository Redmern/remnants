using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// A student in class at a study building: learning a skill up to a cap, or a perk from a mentor who has it. Student and
/// mentor do not work meanwhile. Plain data (saved with the colony).
/// </summary>
public sealed class StudyOrder
{
    public int Id { get; set; }
    public int BuildingId { get; set; }
    public int StudentId { get; set; }
    public int? MentorId { get; set; }

    /// <summary>The skill studied, or null for a perk.</summary>
    public Skill? Skill { get; set; }

    /// <summary>The perk studied (with a mentor who has it), or null for a skill.</summary>
    public string? PerkId { get; set; }

    /// <summary>Manual item used up when the study started (speeds it up and raises the self-study cap).</summary>
    public string? ManualItemId { get; set; }

    /// <summary>Perk studies: effort so far (done at <c>study.perkWorkPerTier · tier</c>).</summary>
    public double Progress { get; set; }
}

/// <summary>What a study would do, from <see cref="StudySystem.Plan"/>: why it cannot start, or its cap, rate and ETA.</summary>
public sealed record StudyPlan(CommandResult Check, double Cap, double RatePerTick, int? EtaTicks)
{
    public bool Ok => Check.Ok;
}

/// <summary>
/// Study (see <see cref="StudyRules"/>): each tick every class makes progress, the student earns study XP and the mentor
/// teaching XP. A skill study ends at its cap; a perk study when its effort is done, and the perk is learned without a
/// point. A class whose student is gone, away or admitted ends; a skill class whose mentor is gone goes on alone (capped
/// at the self-study cap), a perk class without its mentor ends.
/// </summary>
public static class StudySystem
{
    internal static void Run(ColonyState state, GameData data, ScavengeData? items)
    {
        var rules = data.Progression.Study;
        foreach (var order in state.Studies.OrderBy(o => o.Id).ToList())
        {
            var student = state.FindSurvivor(order.StudentId);
            if (student is null || student.Away || student.PatientAtBuildingId is not null || state.FindBuilding(order.BuildingId) is null)
            {
                End(state, order, student is null ? null : $"{student.Name}'s class was interrupted.");
                continue;
            }
            if (order.MentorId is { } mid && state.FindSurvivor(mid) is not { Away: false, PatientAtBuildingId: null })
                if (!DropMentor(state, data, order, student.Name)) continue;
            var mentor = order.MentorId is { } m ? state.FindSurvivor(m) : null;
            var rate = Rate(data, student, mentor, order.Skill, order.PerkId, order.ManualItemId, items);
            if (order.Skill is { } skill)
            {
                var cap = Cap(data, items, mentor, skill, order.ManualItemId);
                var before = student.Get(skill);
                // Never below where they started: a class that lost its mentor may now cap lower than they already are.
                if (before < cap)
                {
                    student.GainSkill(skill, rate, XpSource.Study);
                    if (student.Get(skill) > cap) student.Set(skill, cap);
                }
                if (student.Get(skill) + 1e-6 >= cap)
                    End(state, order, $"{student.Name} finished studying {data.SkillInfo(skill).Name} ({student.Get(skill):0}).", ColonyEventType.StudyFinished);
            }
            else if (order.PerkId is { } perkId)
            {
                order.Progress += rate;
                var perk = data.Perks[perkId];
                if (order.Progress + 1e-9 >= rules.PerkWorkPerTier * perk.Tier)
                {
                    state.Studies.Remove(order);
                    if (!student.HasPerk(perkId)) Progression.Learn(state, student, perk, mentor is null ? " by study" : $" from {mentor.Name}");
                }
            }
            Progression.GrantXp(state, data, student, data.Progression.Xp.PerStudyTick, XpSource.Study);
            if (mentor is not null) Progression.GrantXp(state, data, mentor, data.Progression.Xp.PerTeachTick, XpSource.Teaching);
        }
    }

    /// <summary>
    /// The mentor left the class: a skill class carries on alone (logged), a perk class ends. Returns whether the class
    /// is still running.
    /// </summary>
    private static bool DropMentor(ColonyState state, GameData data, StudyOrder order, string studentName)
    {
        order.MentorId = null;
        if (order.PerkId is not null)
        {
            End(state, order, $"{studentName}'s {data.Perks[order.PerkId].Name} lessons stopped: the mentor is not here.");
            return false;
        }
        state.Log(new(state.Tick, ColonyEventType.StudyEnded, $"{studentName}'s mentor left; they study on alone.", order.StudentId, order.BuildingId));
        return true;
    }

    /// <summary>Someone died or left the colony: their classes end (as student) or lose their mentor, right away.</summary>
    internal static void OnGone(ColonyState state, GameData data, Survivor gone)
    {
        foreach (var order in state.Studies.Where(o => o.StudentId == gone.Id || o.MentorId == gone.Id).OrderBy(o => o.Id).ToList())
        {
            if (order.StudentId == gone.Id) state.Studies.Remove(order);
            else DropMentor(state, data, order, state.FindSurvivor(order.StudentId)?.Name ?? "A student");
        }
    }

    /// <summary>Loading: drop classes whose student is gone and mentors who are gone (saves from before the fix).</summary>
    internal static void Repair(ColonyState state)
    {
        state.Studies.RemoveAll(o => state.FindSurvivor(o.StudentId) is null);
        foreach (var o in state.Studies.Where(o => o.MentorId is { } m && state.FindSurvivor(m) is null).ToList())
        {
            o.MentorId = null;
            if (o.PerkId is not null) state.Studies.Remove(o);
        }
    }

    private static void End(ColonyState state, StudyOrder order, string? message, ColonyEventType type = ColonyEventType.StudyEnded)
    {
        state.Studies.Remove(order);
        if (message is not null) state.Log(new(state.Tick, type, message, order.StudentId, order.BuildingId));
    }

    /// <summary>
    /// Progress per tick: skill points before diminishing returns (skill study), or effort (perk study). See
    /// <see cref="StudyRules"/>. A manual only counts when <paramref name="items"/> is given.
    /// </summary>
    public static double Rate(GameData data, Survivor student, Survivor? mentor, Skill? skill, string? perkId, string? manualItemId, ScavengeData? items)
    {
        var rules = data.Progression.Study;
        var attributes = skill is { } sk ? data.SkillInfo(sk).Attributes : new() { [AttributeKind.Intellect] = 1 };
        var total = attributes.Values.Sum();
        var learning = total <= 0 ? 0.5 : attributes.Sum(kv => kv.Value * student.Get(kv.Key)) / total / Survivor.MaxStat;
        var aptitude = rules.AptitudeFloor + (1 - rules.AptitudeFloor) * learning;
        var mentorMult = mentor is null ? 1 : rules.MentorMultiplier;
        var childMult = student.IsChild ? data.Rules.Training.ChildMultiplier : 1;
        var speed = 1 + PerkEffects.For(student, data).StudySpeed + (Manual(items, manualItemId)?.StudyBonus ?? 0);
        var baseRate = skill is null ? 1 : rules.SkillPerTick;
        return baseRate * aptitude * mentorMult * childMult * speed;
    }

    private static ManualDef? Manual(ScavengeData? items, string? id) =>
        id is not null && items is not null && items.Items.TryGetValue(id, out var item) ? item.Manual : null;

    /// <summary>Where a skill study stops: the self-study cap (or the manual's, if higher), or the mentor's level when that is higher.</summary>
    public static double Cap(GameData data, ScavengeData? items, Survivor? mentor, Skill skill, string? manualItemId)
    {
        var alone = Math.Max(data.Progression.Study.SelfStudyCap, Manual(items, manualItemId)?.Cap ?? 0);
        return mentor is null ? alone : Math.Max(alone, mentor.Get(skill));
    }

    /// <summary>Students a study building takes at its level (0 for buildings that teach nothing).</summary>
    public static int Slots(GameData data, Building building)
    {
        if (!data.Progression.Study.Buildings.TryGetValue(building.DefId, out var rules) || rules.Slots.Count == 0) return 0;
        return rules.Slots[Math.Min(building.Level, rules.Slots.Count) - 1];
    }

    /// <summary>The class this survivor is in, as student or mentor (null when not in class).</summary>
    public static StudyOrder? ClassOf(ColonyState state, int survivorId) =>
        state.Studies.Find(o => o.StudentId == survivorId) ?? state.Studies.Find(o => o.MentorId == survivorId);

    /// <summary>Ticks until a running class is done at today's rate (null when it makes no progress).</summary>
    public static int? Eta(ColonySim sim, StudyOrder order)
    {
        var student = sim.State.FindSurvivor(order.StudentId);
        if (student is null) return null;
        var mentor = order.MentorId is { } m ? sim.State.FindSurvivor(m) : null;
        var rate = Rate(sim.Data, student, mentor, order.Skill, order.PerkId, order.ManualItemId, sim.Scavenge);
        if (order.Skill is { } skill) return SkillEta(student.Get(skill), Cap(sim.Data, sim.Scavenge, mentor, skill, order.ManualItemId), rate);
        var work = sim.Data.Progression.Study.PerkWorkPerTier * sim.Data.Perks[order.PerkId!].Tier;
        return rate <= 0 ? null : (int)Math.Ceiling((work - order.Progress) / rate - 1e-9);
    }

    private static int? SkillEta(double current, double cap, double rate)
    {
        if (current + 1e-6 >= cap) return 0;
        if (rate <= 0 || cap >= Survivor.MaxStat) return null;
        var ticks = 0;
        while (current + 1e-6 < cap && ticks < 100_000)
        {
            current += rate * (1 - current / Survivor.MaxStat);
            ticks++;
        }
        return ticks;
    }

    /// <summary>Checks a study without starting it, and says how far and how fast it would go.</summary>
    public static StudyPlan Plan(ColonySim sim, StartStudy c)
    {
        var state = sim.State;
        var data = sim.Data;
        StudyPlan Fail(CommandError e, string m) => new(CommandResult.Fail(e, m), 0, 0, null);

        var student = state.FindSurvivor(c.StudentId);
        if (student is null) return Fail(CommandError.UnknownSurvivor, $"No survivor {c.StudentId}.");
        var building = state.FindBuilding(c.BuildingId);
        if (building is null) return Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = data.Buildings[building.DefId];
        if (!data.Progression.Study.Buildings.TryGetValue(building.DefId, out var teaches))
            return Fail(CommandError.NotAStudyBuilding, $"Nobody studies at the {def.Name}.");
        if ((c.Skill is null) == (c.PerkId is null)) return Fail(CommandError.InvalidStudy, "Study either a skill or a perk.");
        if (student.IsInfant) return Fail(CommandError.TooYoung, $"{student.Name} is a baby.");
        if (student.Away) return Fail(CommandError.SurvivorAway, $"{student.Name} is away on an expedition.");
        if (student.PatientAtBuildingId is not null) return Fail(CommandError.IsPatient, $"{student.Name} is a patient.");
        if (ClassOf(state, student.Id) is not null) return Fail(CommandError.InClass, $"{student.Name} is already in class.");
        if (state.Studies.Count(o => o.BuildingId == building.Id) >= Slots(data, building))
            return Fail(CommandError.StudyFull, $"The {def.Name} has no room for another student.");

        Survivor? mentor = null;
        if (c.MentorId is { } mid)
        {
            mentor = state.FindSurvivor(mid);
            if (mentor is null) return Fail(CommandError.UnknownSurvivor, $"No survivor {mid}.");
            if (mentor.Id == student.Id) return Fail(CommandError.SamePerson, "A student cannot mentor themselves.");
            if (mentor.IsChild) return Fail(CommandError.NotAnAdult, $"{mentor.Name} is too young to teach.");
            if (mentor.Away) return Fail(CommandError.SurvivorAway, $"{mentor.Name} is away on an expedition.");
            if (mentor.PatientAtBuildingId is not null) return Fail(CommandError.IsPatient, $"{mentor.Name} is a patient.");
            if (state.Studies.Any(o => o.StudentId == mentor.Id)) return Fail(CommandError.InClass, $"{mentor.Name} is a student right now.");
            if (state.Studies.Count(o => o.MentorId == mentor.Id) >= data.Progression.Study.MaxStudentsPerMentor)
                return Fail(CommandError.MentorBusy, $"{mentor.Name} already teaches {data.Progression.Study.MaxStudentsPerMentor} students.");
        }

        ManualDef? manual = null;
        if (c.ManualItemId is { } manualId)
        {
            manual = Manual(sim.Scavenge, manualId);
            if (manual is null) return Fail(CommandError.NotAManual, $"'{manualId}' is not a manual.");
            if (state.Inventory.Count(manualId) < 1) return Fail(CommandError.MissingItems, $"The colony has no {sim.Scavenge!.Items[manualId].Name}.");
            if (c.Skill is not { } ms || manual.Skill != ms)
                return Fail(CommandError.WrongManual, $"{sim.Scavenge!.Items[manualId].Name} teaches {data.SkillInfo(manual.Skill).Name}.");
        }

        if (c.Skill is { } skill)
        {
            if (!teaches.Skills.Contains(skill))
                return Fail(CommandError.CannotStudyHere, $"{data.SkillInfo(skill).Name} is not taught at the {def.Name}.");
            if (mentor is not null && mentor.Get(skill) < student.Get(skill) + data.Progression.Study.MentorLead)
                return Fail(CommandError.MentorNotAhead, $"{mentor.Name} does not know enough {data.SkillInfo(skill).Name} to teach {student.Name}.");
            var cap = Cap(data, sim.Scavenge, mentor, skill, c.ManualItemId);
            if (student.Get(skill) + 0.5 >= cap)
                return Fail(CommandError.AlreadyAtCap, mentor is null
                    ? $"{student.Name} cannot learn more {data.SkillInfo(skill).Name} alone; find a mentor or a manual."
                    : $"{student.Name} already knows what {mentor.Name} can teach.");
            var rate = Rate(data, student, mentor, skill, null, c.ManualItemId, sim.Scavenge);
            return new(CommandResult.Success("ok"), cap, rate, SkillEta(student.Get(skill), cap, rate));
        }

        if (!data.Perks.TryGetValue(c.PerkId!, out var perk)) return Fail(CommandError.UnknownPerk, $"No perk '{c.PerkId}'.");
        if (data.Disciplines[perk.Discipline].StudyAt != building.DefId)
            return Fail(CommandError.CannotStudyHere, $"{perk.Name} is taught at the {data.Buildings[data.Disciplines[perk.Discipline].StudyAt].Name}.");
        if (mentor is null) return Fail(CommandError.NeedsMentor, $"{perk.Name} can only be learned from a mentor who has it.");
        if (!mentor.HasPerk(perk.Id)) return Fail(CommandError.MentorNotAhead, $"{mentor.Name} does not have {perk.Name}.");
        var status = Progression.Status(sim, student, perk);
        if (status.Owned) return Fail(CommandError.AlreadyHasPerk, $"{student.Name} already has {perk.Name}.");
        if (!status.Eligible) return Fail(CommandError.PerkRequirementsNotMet, $"{student.Name} needs {string.Join(", ", status.Missing)}.");
        var perkRate = Rate(data, student, mentor, null, perk.Id, null, sim.Scavenge);
        var work = data.Progression.Study.PerkWorkPerTier * perk.Tier;
        return new(CommandResult.Success("ok"), 0, perkRate, perkRate <= 0 ? null : (int)Math.Ceiling(work / perkRate - 1e-9));
    }
}

internal static class StudyHandlers
{
    public static CommandResult Start(ColonySim sim, StartStudy c)
    {
        var plan = StudySystem.Plan(sim, c);
        if (!plan.Ok) return plan.Check;
        var state = sim.State;
        var student = state.FindSurvivor(c.StudentId)!;
        var mentor = c.MentorId is { } m ? state.FindSurvivor(m) : null;
        if (c.ManualItemId is { } manual) state.Inventory.TryTake(manual);
        student.AssignedBuildingId = null;
        if (mentor is not null) mentor.AssignedBuildingId = null;
        var order = new StudyOrder
        {
            Id = state.NextStudyId++, BuildingId = c.BuildingId, StudentId = c.StudentId, MentorId = c.MentorId,
            Skill = c.Skill, PerkId = c.PerkId, ManualItemId = c.ManualItemId,
        };
        state.Studies.Add(order);
        var subject = c.Skill is { } skill ? sim.Data.SkillInfo(skill).Name : sim.Data.Perks[c.PerkId!].Name;
        var message = $"{student.Name} started studying {subject}{(mentor is null ? "" : $" with {mentor.Name}")}.";
        state.Log(new(state.Tick, ColonyEventType.StudyStarted, message, student.Id, c.BuildingId));
        return CommandResult.Success(message, order.Id);
    }

    public static CommandResult Cancel(ColonySim sim, CancelStudy c)
    {
        var order = sim.State.Studies.Find(o => o.Id == c.StudyId);
        if (order is null) return CommandResult.Fail(CommandError.UnknownStudy, $"No class {c.StudyId}.");
        sim.State.Studies.Remove(order);
        var name = sim.State.FindSurvivor(order.StudentId)?.Name ?? "The student";
        var message = $"{name} stopped studying.";
        sim.State.Log(new(sim.State.Tick, ColonyEventType.StudyEnded, message, order.StudentId, order.BuildingId));
        return CommandResult.Success(message);
    }
}
