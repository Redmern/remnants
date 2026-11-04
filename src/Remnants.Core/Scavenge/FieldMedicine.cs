using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Medical items on a conscious unit in the field (data/medicine.json, items.json <c>use</c>): heal HP, dress wounds, cure,
/// splint, mend the worst injury, numb the pain, a stim's extra AP (then a crash), the Linked "Calm" injector's immunity to being pinned down (at
/// a humanity cost). Downed units are stabilized and revived in <see cref="ScavengeCommandHandlers.UseItem"/>. The body
/// changes travel home with the survivor.
/// </summary>
public static class FieldMedicine
{
    /// <summary>A medical item's AP cost for this unit (<c>medical_ap_discount</c> takes some off, never below 1).</summary>
    public static int ApCost(Unit unit, UseEffect use) =>
        use.ApCost > 0 && (use.Treats || use.StimAp > 0 || use.ReviveHp > 0)
            ? Math.Max(1, use.ApCost - (int)Math.Round(unit.Hook(PerkHooks.MedicalApDiscount)))
            : use.ApCost;

    /// <summary>Applies what the item does to a conscious target. False when it does nothing for them (nothing is spent).</summary>
    internal static bool Use(ScavengeSim sim, Unit unit, Unit target, ItemDef item, UseEffect use)
    {
        if (use.StimAp > 0 && target.Id != unit.Id) return false;
        var done = target.Kind == UnitKind.Survivor || target.Kind == UnitKind.Npc
            ? Treatment.Apply(sim.Data.Medicine, target.Body, use, field: true)
            : [];
        if (use.Mend > 0 && target.Kind is UnitKind.Survivor or UnitKind.Npc && Treatment.Mend(target.Body, use.Mend) is { } mended) done.Add(mended);
        if (use.Heal > 0 && target.Hp < target.MaxHp)
        {
            target.Hp = Math.Min(target.MaxHp, target.Hp + use.Heal * (1 + unit.Hook(PerkHooks.FieldHealBonus)));
            done.Insert(0, "patched up");
        }
        if (use.StimAp > 0)
        {
            unit.Ap += use.StimAp;
            unit.ExtraActions += use.StimAp;
            unit.CrashAp = Math.Max(unit.CrashAp, use.CrashAp);
            done.Add($"+{use.StimAp} AP now, a crash next turn");
        }
        if (use.CalmRounds > 0)
        {
            target.CalmUntilRound = Math.Max(target.CalmUntilRound, sim.State.Round + use.CalmRounds - 1);
            target.StunnedRound = 0;
            done.Add("calm: cannot be stunned");
        }
        if (done.Count == 0) return false;
        if (use.HumanityCost > 0) target.Body.HumanityLoss += use.HumanityCost;
        MissionRules.Treated(sim, unit, target);

        if (done is ["patched up"])
            sim.Log(ScavengeEventType.Healed, $"{unit.Name} treated {target.Name} with {item.Name}.", unit, target);
        else
            sim.Log(ScavengeEventType.Treated, $"{unit.Name} used {item.Name} on {target.Name}: {string.Join(", ", done)}.", unit, target);
        if (use.Treats && sim.Data.Medicine is { } med) unit.GainSkill(Skill.Medicine, med.Def.Field.SkillPerTreatment);
        return true;
    }
}
