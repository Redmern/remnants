using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Story;

/// <summary>
/// What the story does to the world: timed and lasting world effects on the zones (the Relay burned, Halcyon's truce,
/// the endings' after-the-end changes), the truce and breaking it, the survivors Halcyon took, the endings, the
/// epilogue and the Codex. Effects (docs/story/endings.md "After the end"):
/// <list type="bullet">
/// <item><c>relay_down</c>: Network enemies weigh half in the Suburb Ruins and the Flooded Mall.</item>
/// <item><c>truce</c>: no Network enemies in threat 1–2 zones; going to the Relay or the Spire breaks it.</item>
/// <item><c>silence</c>: no Network enemies anywhere; Husks ×1.5.</item>
/// <item><c>terms</c>: the Network enemies of the Relay and the Spire stand neutral.</item>
/// <item><c>second_voice</c>: Network enemies stand neutral everywhere but the Spire.</item>
/// </list>
/// </summary>
public static class StoryWorld
{
    public const string Truce = "truce";

    /// <summary>The world effects the rules know.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal) { "relay_down", Truce, "silence", "terms", "second_voice" };
    public const string OfferFlag = "halcyon_offer";

    public static bool Active(ColonySim sim, string id) =>
        sim.State.Campaign?.WorldEffects.Any(w => w.Id == id && (w.UntilTick is null || w.UntilTick > sim.State.Tick)) == true;

    /// <summary>Starts (or extends) a world effect for <paramref name="days"/> days, 0 = for good.</summary>
    public static void Start(ColonySim sim, string id, int days)
    {
        var campaign = sim.State.Campaign!;
        campaign.WorldEffects.RemoveAll(w => w.Id == id);
        campaign.WorldEffects.Add(new WorldEffectState
        {
            Id = id, StartTick = sim.State.Tick, UntilTick = days > 0 ? sim.State.Tick + days * sim.Data.Rules.TicksPerDay : null,
        });
    }

    /// <summary>Ends a world effect, or with <paramref name="ending"/> ends the campaign with that ending.</summary>
    public static void End(ColonySim sim, string id, bool ending = false)
    {
        var campaign = sim.State.Campaign!;
        if (!ending)
        {
            campaign.WorldEffects.RemoveAll(w => w.Id == id);
            return;
        }
        campaign.Ending = id;
        campaign.Flags["ending"] = id switch { "silence" => 1, "terms" => 2, "voice" => 3, "calm" => 4, _ => 9 };
        var def = sim.Story?.Endings.GetValueOrDefault(id);
        foreach (var effect in def?.WorldEffects ?? []) Start(sim, effect, 0);
        campaign.Over = def?.Final == true;
        Campaign.Log(sim, $"The story is over: {def?.Title ?? id}.");
    }

    /// <summary>Once per colony tick: effects that ran out are dropped (the truce ends quietly).</summary>
    public static void Tick(ColonySim sim)
    {
        var campaign = sim.State.Campaign!;
        foreach (var w in campaign.WorldEffects.Where(w => w.UntilTick <= sim.State.Tick).ToList())
        {
            campaign.WorldEffects.Remove(w);
            if (w.Id == Truce) Campaign.Log(sim, "Sixty days. The truce is over. Channel nine is singing again.");
        }
    }

    /// <summary>The zone's conditions with the story's world effects on top.</summary>
    public static ZoneConditions Conditions(ColonySim sim, string zoneId, ZoneConditions conditions)
    {
        if (sim.State.Campaign is null) return conditions;
        var threat = sim.Scavenge?.Zones.GetValueOrDefault(zoneId)?.Threat ?? 0;
        var network = 1.0;
        var husks = 1.0;
        var neutral = false;
        if (Active(sim, "relay_down") && zoneId is "suburb_ruins" or "flooded_mall") network *= 0.5;
        if (Active(sim, Truce) && threat <= 2) network = 0;
        if (Active(sim, "silence"))
        {
            network = 0;
            husks *= 1.5;
        }
        if (Active(sim, "terms") && zoneId is "network_relay" or "core_spire") neutral = true;
        if (Active(sim, "second_voice") && zoneId != "core_spire") neutral = true;
        return conditions with { NetworkWeight = network, HuskCountMultiplier = husks, NetworkNeutral = neutral };
    }

    /// <summary>Launching this mission now would break Halcyon's truce: it goes to the Relay or the Spire while the truce holds.</summary>
    public static bool BreaksTruce(ColonySim sim, MissionDef def) =>
        Active(sim, Truce) && def.Zone is "network_relay" or "core_spire" && def.Map != "perimeter";

    /// <summary>The colony broke the truce: it ends, the Offer counts as broken, and Halcyon's answer (m14b) unlocks.</summary>
    public static void BreakTruce(ColonySim sim)
    {
        var campaign = sim.State.Campaign!;
        End(sim, Truce);
        campaign.Flags[OfferFlag] = 3;
        Campaign.Log(sim, "The truce is broken.");
    }

    // ---- The taken ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The party is home from a mission where the Linked carried survivors off: they leave the colony (not the
    /// memorial) and the story keeps them, whole, for the Choir and the endings.
    /// </summary>
    public static void TakeAway(ColonySim sim, IEnumerable<int> survivorIds)
    {
        var state = sim.State;
        if (state.Campaign is not { } campaign) return;
        foreach (var id in survivorIds)
        {
            if (state.FindSurvivor(id) is not { } s) continue;
            state.Survivors.Remove(s);
            Bonds.OnGone(state, sim.Data, s, died: false);
            campaign.Taken.Add(new TakenSurvivor { Tick = state.Tick, Survivor = s });
            campaign.Flags["taken"] = campaign.Flag("taken") + 1;
            state.Log(new(state.Tick, ColonyEventType.StoryMission, $"{s.Name} was taken by the Linked.", s.Id));
        }
    }

    /// <summary>The taken come home, each <paramref name="humanityLost"/> humanity down. Returns their names.</summary>
    public static List<string> ReturnTaken(ColonySim sim, int humanityLost)
    {
        var state = sim.State;
        var campaign = state.Campaign!;
        var names = new List<string>();
        foreach (var t in campaign.Taken)
        {
            var s = t.Survivor;
            // Ids are never reused, so the survivor comes back as who they were (a story character's SurvivorId still holds).
            s.Away = false;
            s.AssignedBuildingId = s.PatientAtBuildingId = s.HomeBuildingId = null;
            s.HasBed = false;
            s.PartnerId = null;
            s.Mood = Math.Min(s.Mood, 40);
            s.Body.HumanityLoss = Math.Clamp(s.Body.HumanityLoss + humanityLost, 0, Body.MaxHumanity);
            state.Survivors.Add(s);
            if (s.UniqueId is { } unique && campaign.Characters.GetValueOrDefault(unique) is { } c)
            {
                c.Joined = true;
                c.Left = false;
                campaign.Flags.Remove($"{unique}_left");
            }
            names.Add(s.Name);
            state.Log(new(state.Tick, ColonyEventType.Recruited, $"{s.Name} came home. They are not quite who they were.", s.Id));
        }
        campaign.Taken.Clear();
        return names;
    }

    // ---- Endings, epilogue, Codex -------------------------------------------------------------------------------

    /// <summary>Every ending with whether it is open now (hidden ones only when open).</summary>
    public static List<(EndingDef Def, bool Open)> Endings(ColonySim sim) =>
        (sim.Story?.Endings.Values ?? [])
            .Select(e => (e, e.Requires.All(c => Campaign.Holds(sim, c))))
            .Where(t => t.Item2 || t.e.ClosedText is not null).ToList();

    /// <summary>The epilogue of the campaign's ending: each slide's first variant that holds, resolved.</summary>
    public static List<(string Still, string Text)> Epilogue(ColonySim sim)
    {
        if (sim.State.Campaign?.Ending is not { } id || sim.Story?.Endings.GetValueOrDefault(id) is not { } ending) return [];
        var slides = new List<(string, string)>();
        foreach (var slide in ending.Slides)
            if (slide.Variants.FirstOrDefault(v => v.When is null || Campaign.Holds(sim, v.When)) is { } variant)
                slides.Add((slide.Still, Dialogue.Resolve(sim, variant.Text)));
        return slides;
    }

    /// <summary>The Codex pages unlocked now, each with its text and the lines that hold.</summary>
    public static List<(CodexEntryDef Def, string Text)> Codex(ColonySim sim) =>
        (sim.Story?.Codex ?? [])
            .Where(e => sim.State.Campaign is not null && (e.When is null || Campaign.Holds(sim, e.When)))
            .Select(e => (e, Dialogue.Resolve(sim, string.Join("\n", e.Lines.Where(l => l.When is null || Campaign.Holds(sim, l.When))
                .Select(l => l.Text).Prepend(e.Text).Where(t => t.Length > 0))))).ToList();
}
