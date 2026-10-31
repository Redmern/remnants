using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Story;

/// <summary>One thing the briefing warns about before a field mission: a stable id (for tests and the UI) and the text.</summary>
public sealed record ReadinessWarning(string Id, string Text);

/// <summary>
/// What the briefing and the expedition planner warn about before a story mission (BALANCE.md "Full-game pass 2"): a
/// wounded survivor (with what the wound does to their aim), a party too small for the threat, and weapons below the
/// mission's <see cref="MissionDef.GearTier"/>. Pure: reads the colony, changes nothing. A colony mission only warns about wounds.
/// </summary>
public static class MissionReadiness
{
    /// <summary>The party a mission of this threat wants: two for threat 1, three from threat 2.</summary>
    public static int RecommendedParty(MissionDef def) => def.Threat >= 2 ? 3 : 2;

    /// <summary>
    /// Warnings for <paramref name="party"/> (the members picked in the planner) or, with none picked, for the adults at
    /// home who could go. Weapons and armor are read from the members' equipment, or from the stores without a party.
    /// </summary>
    public static List<ReadinessWarning> Check(ColonySim sim, MissionDef def, IReadOnlyList<ExpeditionMember>? party = null)
    {
        var warnings = new List<ReadinessWarning>();
        var state = sim.State;
        var picked = party?.Select(m => state.FindSurvivor(m.SurvivorId)).OfType<Survivor>().ToList();
        var candidates = picked ?? state.Survivors.Where(s => !s.IsChild && !s.Away).ToList();

        foreach (var s in candidates.Where(s => s.IsInjured || s.Health < 70).OrderBy(s => s.Id))
            warnings.Add(new($"hurt:{s.Id}", Wound(s)));

        if (def.Type != MissionType.Field) return warnings;
        var want = Math.Min(RecommendedParty(def), Math.Max(sim.MaxPartySize, def.MaxParty ?? 0));
        var fit = candidates.Count(s => !s.IsInjured && s.Health >= 70);
        if (picked is not null ? picked.Count < want : fit < want)
            warnings.Add(new("party", $"Threat {def.Threat}: send at least {want}. {(picked is not null ? $"You picked {picked.Count}." : $"Only {fit} adults are fit to go.")} " +
                                      "Someone has to be there to patch up whoever goes down."));

        if (def.GearTier is { } tier && sim.Scavenge is { } scavenge)
        {
            var weapons = party is not null
                ? party.Select(m => m.Equipment?.GetValueOrDefault(EquipSlot.Weapon)).ToList()
                : state.Inventory.Counts.Where(kv => kv.Value > 0 && scavenge.Items.TryGetValue(kv.Key, out var d) && d.Slot == EquipSlot.Weapon)
                    .SelectMany(kv => Enumerable.Repeat((string?)kv.Key, kv.Value)).ToList();
            var tiers = weapons.Select(id => id is not null && scavenge.Items.TryGetValue(id, out var d) ? GearTier(d) : 0)
                .OrderByDescending(t => t).Take(Math.Max(1, picked?.Count ?? want)).ToList();
            if (tiers.Count == 0 || tiers.Average() < tier)
                warnings.Add(new("gear", $"Threat {def.Threat}: a stand-up fight, and the party carries {(tier <= 2 ? "starting-tier weapons" : "weapons a tier below it")}. " +
                                         (tier <= 2 ? "Machetes from the Armory, or a Mk II upgrade at the Gear Bench, even it out."
                                                    : "Rifles and carbines from the Armory, or Mk upgrades at the Gear Bench, even it out.")));
        }
        return warnings;
    }

    /// <summary>A weapon's tier, plus one for a Mk upgrade.</summary>
    private static int GearTier(ItemDef d) => d.Tier + (GearModsData.Parse(d.Id).Mk > 1 ? 1 : 0);

    /// <summary>"Noor: right arm injured (45%), hits about 55% as often; worn out (health 60)."</summary>
    public static string Wound(Survivor s)
    {
        var body = s.Body;
        var parts = body.Parts.Where(kv => kv.Value.Condition == PartCondition.Injured).OrderByDescending(kv => kv.Value.Severity)
            .Select(kv => $"{MedicalSystem.Describe(kv.Key)} {kv.Value.Severity * 100:0}%").ToList();
        var bits = new List<string>();
        if (parts.Count > 0) bits.Add($"injured ({string.Join(", ", parts)})");
        var arms = Math.Max(body.PartEfficiency(BodyPart.LeftArm), body.PartEfficiency(BodyPart.RightArm));
        if (arms < 0.95) bits.Add($"hits about {arms * 100:0}% as often");
        var legs = body.RegionEfficiency(BodyRegion.Legs);
        if (legs < 0.95) bits.Add("slow on their feet");
        if (s.Health < 70) bits.Add($"worn out (health {s.Health:0})");
        return $"{s.Name}: {string.Join("; ", bits)}. Rest or the Med Bay first.";
    }
}
