using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>Why a perk cannot be chosen (yet). <see cref="Ok"/> when it can.</summary>
public enum PerkBlock { Ok, Owned, Locked, MissingPrerequisites, NoPoints }

/// <summary>A perk's state for one survivor, with the unmet requirements as short lines for a tooltip.</summary>
public sealed record PerkStatus(PerkDef Perk, PerkBlock Block, IReadOnlyList<string> Missing)
{
    public bool Owned => Block == PerkBlock.Owned;

    /// <summary>Every requirement is met (it may still need a point).</summary>
    public bool Eligible => Block is PerkBlock.Ok or PerkBlock.NoPoints;
}

/// <summary>
/// Experience and perks. XP comes from work, crafting, study and missions (<see cref="XpRules"/>); each level reached
/// gives perk points. A perk is chosen with a point (<see cref="ChoosePerk"/>) or learned by studying it with a mentor
/// (<see cref="StudySystem"/>); either way its requirements must hold.
/// </summary>
public static class Progression
{
    /// <summary>Total XP needed to reach <paramref name="level"/> from level 1.</summary>
    public static double XpForLevel(int level, XpRules rules)
    {
        var n = Math.Max(0, level - 1);
        return rules.PerLevel * n + rules.PerLevelStep * n * (n - 1) / 2.0;
    }

    public static int LevelFor(double xp, XpRules rules)
    {
        var level = 1;
        while (level < rules.MaxLevel && xp + 1e-9 >= XpForLevel(level + 1, rules)) level++;
        return level;
    }

    /// <summary>Progress from the current level to the next, 0..1 (1 at max level).</summary>
    public static double LevelProgress(Survivor s, XpRules rules)
    {
        if (s.Level >= rules.MaxLevel) return 1;
        var from = XpForLevel(s.Level, rules);
        var to = XpForLevel(s.Level + 1, rules);
        return Math.Clamp((s.Xp - from) / (to - from), 0, 1);
    }

    /// <summary>
    /// Adds XP (children at <c>xp.childMultiplier</c>, plus perk XP bonuses). Each level reached gives
    /// <c>pointsPerLevel</c> perk points and logs a <see cref="ColonyEventType.LevelUp"/>. Returns the levels gained.
    /// Infants earn nothing.
    /// </summary>
    public static int GrantXp(ColonyState state, GameData data, Survivor s, double amount, XpSource source)
    {
        _ = source;
        if (amount <= 0 || s.IsInfant) return 0;
        var rules = data.Progression.Xp;
        var mult = (s.IsChild ? rules.ChildMultiplier : 1) * (1 + PerkEffects.For(s, data).XpBonus);
        s.Xp += amount * mult;
        var level = LevelFor(s.Xp, rules);
        if (level <= s.Level) return 0;
        var gained = level - s.Level;
        s.Level = level;
        s.PerkPoints += gained * rules.PointsPerLevel;
        var points = s.PerkPoints == 1 ? "a perk point" : $"{s.PerkPoints} perk points";
        state.Log(new(state.Tick, ColonyEventType.LevelUp, $"{s.Name} reached level {level} ({points} to spend).", s.Id));
        return gained;
    }

    /// <summary>A perk's state for a survivor: owned, locked behind a datashard, missing requirements, or open.</summary>
    public static PerkStatus Status(ColonySim sim, Survivor s, PerkDef perk)
    {
        if (s.HasPerk(perk.Id)) return new(perk, PerkBlock.Owned, []);
        var missing = Missing(sim, s, perk);
        if (perk.Requires.Blueprint is { } bp && !sim.State.KnownBlueprints.Contains(bp))
            return new(perk, PerkBlock.Locked, missing);
        if (missing.Count > 0) return new(perk, PerkBlock.MissingPrerequisites, missing);
        return new(perk, s.PerkPoints >= perk.Cost ? PerkBlock.Ok : PerkBlock.NoPoints, []);
    }

    /// <summary>The requirements this survivor (or the colony) does not meet yet, as short lines.</summary>
    public static List<string> Missing(ColonySim sim, Survivor s, PerkDef perk)
    {
        var r = perk.Requires;
        var data = sim.Data;
        var missing = new List<string>();
        if (r.AdultsOnly && s.IsChild) missing.Add("Adults only");
        if (s.Level < r.Level) missing.Add($"Level {r.Level} (has {s.Level})");
        foreach (var (skill, need) in r.Skills)
            if (s.Get(skill) + 1e-9 < need) missing.Add($"{data.SkillInfo(skill).Name} {need:0} (has {Math.Floor(s.Get(skill)):0})");
        foreach (var (attribute, need) in r.Attributes)
            if (s.Get(attribute) + 1e-9 < need) missing.Add($"{attribute} {need:0} (has {Math.Floor(s.Get(attribute)):0})");
        foreach (var id in r.Perks)
            if (!s.HasPerk(id)) missing.Add($"Perk: {data.Perks[id].Name}");
        if (r.Building is { } b && !sim.State.Buildings.Any(x => x.DefId == b && x.Level >= r.BuildingLevel))
            missing.Add($"{data.Buildings[b].Name}{(r.BuildingLevel > 1 ? $" level {r.BuildingLevel}" : "")}");
        if (r.Blueprint is { } bp && !sim.State.KnownBlueprints.Contains(bp))
            missing.Add($"Study {sim.Scavenge?.Items.GetValueOrDefault(bp)?.Name ?? bp}");
        return missing;
    }

    /// <summary>Learn a perk (from a point or a finished study) and log it.</summary>
    internal static void Learn(ColonyState state, Survivor s, PerkDef perk, string how)
    {
        s.Perks.Add(perk.Id);
        state.Log(new(state.Tick, ColonyEventType.PerkLearned, $"{s.Name} learned {perk.Name}{how}.", s.Id));
    }

    /// <summary>
    /// Old saves: XP from the skills a survivor already has (so veterans come with levels and points), and every skill
    /// listed, including ones added since.
    /// </summary>
    internal static void Migrate(GameData data, Survivor s)
    {
        foreach (var skill in Enum.GetValues<Skill>()) s.Skills.TryAdd(skill, 0);
        if (s.Xp > 0 || s.IsInfant) return;
        var rules = data.Progression.Xp;
        s.Xp = s.Skills.Values.Sum() * rules.MigrationPerSkillPoint;
        s.Level = LevelFor(s.Xp, rules);
        s.PerkPoints = (s.Level - 1) * rules.PointsPerLevel;
    }
}

internal static class ProgressionHandlers
{
    public static CommandResult ChoosePerk(ColonySim sim, ChoosePerk c)
    {
        var s = sim.State.FindSurvivor(c.SurvivorId);
        if (s is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (!sim.Data.Perks.TryGetValue(c.PerkId, out var perk))
            return CommandResult.Fail(CommandError.UnknownPerk, $"No perk '{c.PerkId}'.");
        var status = Progression.Status(sim, s, perk);
        switch (status.Block)
        {
            case PerkBlock.Owned:
                return CommandResult.Fail(CommandError.AlreadyHasPerk, $"{s.Name} already has {perk.Name}.");
            case PerkBlock.Locked:
                return CommandResult.Fail(CommandError.PerkLocked,
                    $"{perk.Name} is locked: study {sim.Scavenge?.Items.GetValueOrDefault(perk.Requires.Blueprint!)?.Name ?? perk.Requires.Blueprint} first.");
            case PerkBlock.MissingPrerequisites:
                return CommandResult.Fail(CommandError.PerkRequirementsNotMet, $"{s.Name} needs {string.Join(", ", status.Missing)}.");
            case PerkBlock.NoPoints:
                return CommandResult.Fail(CommandError.NoPerkPoints,
                    $"{perk.Name} costs {perk.Cost} perk point{(perk.Cost == 1 ? "" : "s")}; {s.Name} has {s.PerkPoints}.");
        }
        s.PerkPoints -= perk.Cost;
        Progression.Learn(sim.State, s, perk, "");
        return CommandResult.Success($"{s.Name} learned {perk.Name}.", s.Id);
    }
}
