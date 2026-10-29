using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// One tick of work trains the job's skill and its weighted attributes. Gains shrink as a stat
/// approaches 100 (gain · (1 − value/100)); children learn faster.
/// </summary>
public static class Training
{
    public static void TrainTick(JobDef job, Survivor s, TrainingRules rules)
    {
        var mult = s.IsChild ? rules.ChildMultiplier : 1.0;

        s.Set(job.Skill, s.Get(job.Skill) + Gain(rules.SkillPerTick * mult, s.Get(job.Skill)));

        var totalWeight = job.Attributes.Values.Sum();
        if (totalWeight <= 0) return;
        foreach (var (attribute, weight) in job.Attributes)
        {
            var current = s.Get(attribute);
            s.Set(attribute, current + Gain(rules.AttributePerTick * mult * weight / totalWeight, current));
        }
    }

    private static double Gain(double baseGain, double current) => baseGain * (1 - current / Survivor.MaxStat);
}
