using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// One tick of work trains the job's skill and its weighted attributes. Gains shrink as a stat
/// approaches 100 (gain · (1 − value/100)); children learn faster. Skills the job <c>trains</c> on the side grow at
/// their share of the main skill's base gain.
/// </summary>
public static class Training
{
    /// <summary>One tick of work in the colony: train, and earn the work XP, both at the survivor's share of effort (<see cref="Activities"/>).</summary>
    internal static void Work(ColonyState state, GameData data, JobDef job, Survivor s)
    {
        var share = Activities.Share(state, s);
        TrainTick(job, s, data.Rules.Training, share);
        Progression.GrantXp(state, data, s, data.Progression.Xp.PerWorkTick * share, XpSource.Work);
    }

    /// <summary>One tick of training at this job; <paramref name="share"/> is the survivor's share of effort (1/N activities).</summary>
    public static void TrainTick(JobDef job, Survivor s, TrainingRules rules, double share = 1)
    {
        var mult = (s.IsChild ? rules.ChildMultiplier : 1.0) * share;

        s.GainSkill(job.Skill, rules.SkillPerTick * mult);
        foreach (var (skill, side) in job.Trains) s.GainSkill(skill, rules.SkillPerTick * mult * side);

        var totalWeight = job.Attributes.Values.Sum();
        if (totalWeight <= 0) return;
        foreach (var (attribute, weight) in job.Attributes)
        {
            var current = s.Get(attribute);
            s.Set(attribute, current + Gain(rules.AttributePerTick * mult * weight / totalWeight, current));
        }
    }

    /// <summary>Ticks of work until this survivor's job skill reaches <paramref name="target"/> (0 if it already does).</summary>
    public static int TicksToReach(JobDef job, Survivor s, double target, TrainingRules rules)
    {
        var rate = rules.SkillPerTick * (s.IsChild ? rules.ChildMultiplier : 1.0);
        var current = s.Get(job.Skill);
        if (current + 1e-9 >= target) return 0;
        if (rate <= 0 || target >= Survivor.MaxStat) return int.MaxValue;
        var ticks = 0;
        while (current + 1e-9 < target && ticks < 1_000_000)
        {
            current += Gain(rate, current);
            ticks++;
        }
        return ticks;
    }

    private static double Gain(double baseGain, double current) => baseGain * (1 - current / Survivor.MaxStat);
}
