using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// Job output = f(weights, attributes, skill, tool, health).
/// <code>
/// effectiveness = (Σ wₐ·attrₐ + w_skill·skill) / (Σ wₐ + w_skill) / 100          ∈ [0,1]
/// performance   = floor + (1 − floor) · effectiveness
/// tool          = 1 + toolQuality · job.toolBonus
/// health        = bodyEfficiency(job regions) · lerp(lowHealthMultiplier, 1, hp/100)
/// life          = child ? childOutputMultiplier : 1
/// mood          = lerp(lowMoodMultiplier, 1, mood/100)
/// perks         = 1 + Σ perk jobOutput[job] + Σ working cybernetics jobOutput[job]
/// output/tick   = building.baseOutput · level.outputMultiplier · performance · tool · health · life · mood · perks
/// </code>
/// </summary>
public static class JobOutput
{
    public static double Effectiveness(JobDef job, Survivor s)
    {
        var weighted = job.Attributes.Sum(kv => kv.Value * s.Get(kv.Key)) + job.SkillWeight * s.Get(job.Skill);
        var totalWeight = job.Attributes.Values.Sum() + job.SkillWeight;
        return totalWeight <= 0 ? 0 : weighted / totalWeight / Survivor.MaxStat;
    }

    public static double Performance(JobDef job, Survivor s, OutputRules rules) =>
        rules.PerformanceFloor + (1 - rules.PerformanceFloor) * Effectiveness(job, s);

    public static double ToolFactor(JobDef job, Survivor s) => 1 + Math.Clamp(s.ToolQuality, 0, 1) * job.ToolBonus;

    public static double HealthFactor(JobDef job, Survivor s, OutputRules rules)
    {
        var hp = Math.Clamp(s.Health / Survivor.MaxHealth, 0, 1);
        var vitality = rules.LowHealthMultiplier + (1 - rules.LowHealthMultiplier) * hp;
        return s.Body.WeightedEfficiency(job.BodyParts) * vitality;
    }

    /// <summary>Conditions (fever, sickness) and pain from severe injuries (1 without medicine data).</summary>
    public static double ConditionFactor(Survivor s, GameData? data) => Treatment.OutputFactor(data?.Medicine, s.Body);

    public static double LifeFactor(Survivor s, OutputRules rules) => s.IsChild ? rules.ChildOutputMultiplier : 1;

    public static double MoodFactor(Survivor s, OutputRules rules) =>
        rules.LowMoodMultiplier + (1 - rules.LowMoodMultiplier) * Math.Clamp(s.Mood / Survivor.MaxMood, 0, 1);

    /// <summary>Output bonus from the survivor's perks and cybernetics at this job (1 without perk data).</summary>
    public static double PerkFactor(JobDef job, Survivor s, GameData? data) =>
        data is null || s.Perks.Count == 0 && s.Body.Parts.Values.All(p => p.Condition != PartCondition.Prosthetic)
            ? 1
            : 1 + PerkEffects.For(s, data).JobOutputBonus(job.Id);

    /// <summary>Units of the building's resource this survivor produces in one tick (perks count when <paramref name="data"/> is given).</summary>
    public static double PerTick(BuildingDef building, int level, JobDef job, Survivor s, OutputRules rules, GameData? data = null) =>
        building.BaseOutput
        * building.Level(level).OutputMultiplier
        * Performance(job, s, rules)
        * ToolFactor(job, s)
        * HealthFactor(job, s, rules)
        * LifeFactor(s, rules)
        * MoodFactor(s, rules)
        * ConditionFactor(s, data)
        * PerkFactor(job, s, data);
}
