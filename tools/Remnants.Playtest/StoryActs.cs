using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Story;
using Remnants.Core.Survivors;

namespace Remnants.Playtest;

/// <summary>
/// One way through the story: which dialogue choice and which field prompt option to pick (by dialogue or prompt id,
/// matched against the choice text or option id), what to set up before Act 2, and whether to hold off a mission.
/// </summary>
public sealed record StoryVariant(
    string Name,
    Dictionary<string, string> Choices,
    Dictionary<string, string> Prompts,
    Action<ColonySim>? Setup = null,
    Func<ColonySim, MissionDef, bool>? Wait = null,
    Action<ColonySim>? Daily = null);

/// <summary>A mission run in a story playthrough: day, mission, outcome, back, dead.</summary>
public sealed record StoryRun(int Day, string Mission, string Outcome, int Back, int Dead, int Rounds);

/// <summary>What a story playthrough came to.</summary>
public sealed record StoryActsReport(string Variant, int Days, List<StoryRun> Runs, SortedDictionary<string, string> Missions,
    SortedDictionary<string, int> Flags, string? Ending, List<(string Still, string Text)> Epilogue, List<string> Joined, List<string> Problems,
    int Codex, List<string> Memorial);

/// <summary>
/// A story bot playthrough of Acts 2–4 (Act 1 is taken as done): a colony with what the acts ask for (buildings, a
/// strong party, gear and food), every call taken and played by the variant's choices, every field mission played by
/// <see cref="StoryBot"/> as soon as it opens, until the ending. Deterministic for a seed and a variant.
/// </summary>
public static class StoryActs
{
    public static readonly IReadOnlyList<StoryVariant> Variants =
    [
        // Kind: people first, share with Cole, bring Seven and Iris home, refuse the Offer, Iris carries the key, Terms.
        new("terms",
            new() { ["m14_open"] = "No", ["m16_brief"] = "Iris carries", ["m17_core_scene"] = "Ask them", ["sd_seven_name"] = "Pick one yourself",
                    ["sd_cole_ally_done"] = "Room for everyone" },
            new() { ["m6_split"] = "people", ["m9_door"] = "share", ["m10_store"] = "capture", ["m11_choice"] = "home", ["m11_choice2"] = "home",
                    ["m7_clinic"] = "hold", ["m15_bench"] = "leave", ["m17_taken"] = "wake", ["sd_kitchen"] = "person" }),
        // Hard: cargo, take the armoury, destroy the Linked, leave Iris on the radio, one of ours takes the port, Silence.
        new("silence",
            new() { ["m14_open"] = "No", ["m16_brief"] = "One of us", ["m17_core_scene"] = "turn you off" },
            new() { ["m6_split"] = "cargo", ["m9_door"] = "take", ["m10_store"] = "destroy", ["m11_choice"] = "stay", ["m11_choice2"] = "stay",
                    ["m7_clinic"] = "why", ["m15_bench"] = "take" },
            Setup: sim => sim.State.Campaign!.Flags["cole_respect"] = -1),
        // The Second Voice: Seven captured carries the key and goes in.
        new("voice",
            new() { ["m14_open"] = "No", ["m16_brief"] = "Seven carries", ["m17_core_scene"] = "another way|Seven goes|goes", ["sd_seven_name"] = "Seven. It's yours" },
            new() { ["m6_split"] = "both", ["m9_door"] = "slip", ["m10_store"] = "capture", ["m11_choice"] = "home", ["m11_choice2"] = "home" },
            Setup: sim => sim.State.Campaign!.Flags["voice_route"] = 1),
        // The Calm: take the Offer and keep it; the colony is mostly machine by the end.
        new("calm",
            new() { ["m14_open"] = "Sixty days", ["m16_brief"] = "One of us", ["m17_core_scene"] = "another way|Let it in" },
            new() { ["m6_split"] = "cargo", ["m9_door"] = "take", ["m10_store"] = "free", ["m11_choice"] = "stay", ["m11_choice2"] = "stay" },
            Wait: (sim, def) => StoryWorld.Active(sim, StoryWorld.Truce) && def.Zone is "network_relay" or "core_spire",
            Daily: sim =>
            {
                // A colony that has gone further and further into the Network: humanity well under 30.
                foreach (var s in sim.State.Survivors.Where(s => !s.IsChild)) s.Body.HumanityLoss = Math.Max(s.Body.HumanityLoss, 80);
            }),
    ];

    private const int MaxDays = 260;

    public static StoryActsReport Run(GameData data, ScavengeData scavenge, StoryData story, ulong seed, StoryVariant variant, Action<string>? trace = null)
    {
        var sim = NewGame.Create(data, seed, scavenge);
        sim.Story = story;
        Campaign.Start(sim);
        var problems = new List<string>();
        var runs = new List<StoryRun>();
        SkipAct1(sim);
        Prepare(sim);
        variant.Setup?.Invoke(sim);
        var tpd = data.Rules.TicksPerDay;
        var campaign = sim.State.Campaign!;
        for (var t = 0; t < MaxDays * tpd; t++)
        {
            sim.Tick();
            if (t % tpd == 0) Keep(sim, variant);
            TakeCalls(sim, variant, trace);
            if (campaign.Ending is not null && Campaign.Waiting(sim).All(c => c.DialogueId != "m17_core_scene") && sim.State.Expedition is null) break;
            if (sim.State.Expedition is { } exp)
            {
                if (exp.PhaseAt(sim.State.Tick) == ExpeditionPhase.InZone && exp.PendingResult is null)
                {
                    var run = sim.OpenExpeditionScavenge();
                    var (result, rounds) = StoryBot.Play(run, ColonySim.ExpeditionPlayerId, 0, trace,
                        p => variant.Prompts.TryGetValue(p.Id, out var o) ? o : null);
                    var mid = exp.MissionId ?? "?";
                    runs.Add(new(Campaign.Day(sim), mid, result.Mission?.Outcome.ToString() ?? "?", result.Returned.Count, result.Deaths.Count, rounds));
                    trace?.Invoke($"day {Campaign.Day(sim)}: {mid} → {result.Mission?.Outcome} (back {result.Returned.Count}, dead {result.Deaths.Count}, rounds {rounds})");
                    if (!result.Complete) { problems.Add($"day {Campaign.Day(sim)}: {mid} did not finish"); break; }
                    var applied = sim.Execute(new ApplyExtractionResult(result));
                    if (!applied.Ok) problems.Add($"day {Campaign.Day(sim)}: {mid}: {applied.Message}");
                }
                continue;
            }
            Launch(sim, variant, problems);
        }
        var missions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, p) in campaign.Missions) missions[id] = p.Status == MissionStatus.Done ? $"done:{p.Outcome}" : p.Status.ToString().ToLowerInvariant();
        var joined = campaign.Characters.Where(kv => kv.Value.Joined).Select(kv => kv.Key).ToList();
        return new StoryActsReport(variant.Name, Campaign.Day(sim), runs, missions, new(campaign.Flags, StringComparer.Ordinal), campaign.Ending,
            StoryWorld.Epilogue(sim), joined, problems, StoryWorld.Codex(sim).Count,
            sim.State.Memorial.Select(m => $"{m.Name} (day {m.Tick / tpd + 1}: {m.Cause})").ToList());
    }

    /// <summary>Act 1 as done: the missions and tasks closed, the caravan and Iris's channel open, Cole met.</summary>
    public static void SkipAct1(ColonySim sim)
    {
        var campaign = sim.State.Campaign!;
        foreach (var m in sim.Story!.Missions.Where(m => m.Act == 1))
        {
            var p = campaign.Progress(m.Id);
            p.Status = MissionStatus.Done;
            p.Outcome = "done";
        }
        foreach (var (k, v) in new Dictionary<string, int> { ["iris_channel"] = 1, ["caravan_open"] = 1, ["cole_met"] = 1, ["cole_respect"] = 1, ["radio_range"] = 2, ["marrow_visits"] = 2 })
            campaign.Flags[k] = v;
        foreach (var who in new[] { "iris", "marrow", "juniper", "cole" }) campaign.Character(who).Met = true;
        campaign.Inbox.Clear();
        sim.State.Radio.Calls.Clear();
    }

    /// <summary>What Acts 2–4 need from the colony: the buildings, a party that can fight, gear and food.</summary>
    public static void Prepare(ColonySim sim)
    {
        var state = sim.State;
        void Build(string id, int level)
        {
            if (!sim.Data.Buildings.ContainsKey(id)) return;
            if (state.Buildings.Find(b => b.DefId == id) is { } have) have.Level = Math.Max(have.Level, level);
            else state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = id, Level = level, Plot = state.Buildings.Count });
        }
        foreach (var (id, level) in new[] { ("town_hall", 3), ("med_bay", 2), ("workshop", 2), ("electronics_lab", 2), ("radio_mast", 1), ("watchtower", 1),
                                             ("armory", 2), ("house", 2), ("farm", 2), ("kitchen", 1) })
            Build(id, level);
        foreach (var s in state.Survivors.Where(s => !s.IsChild))
        {
            foreach (var a in Enum.GetValues<AttributeKind>()) s.Set(a, Math.Max(s.Get(a), 70));
            foreach (var k in new[] { Skill.Firearms, Skill.Melee, Skill.Medicine, Skill.Hacking }) s.Set(k, Math.Max(s.Get(k), 55));
        }
        // A few more hands, so a party of four can leave and the colony still runs.
        for (var i = 0; i < 4; i++)
        {
            var r = StoryCharacters.Recruit(sim, $"Volunteer {i + 1}", child: false, humanity: 100);
            foreach (var a in Enum.GetValues<AttributeKind>()) r.Set(a, 75);
            foreach (var k in new[] { Skill.Firearms, Skill.Melee, Skill.Medicine, Skill.Hacking }) r.Set(k, 60);
        }
        Stock(sim);
    }

    private static void Stock(ColonySim sim)
    {
        var state = sim.State;
        foreach (var (id, n) in new[] { ("linked_carbine", 6), ("linked_plating", 6), ("rifle", 6), ("riot_armor", 6), ("med_kit", 12), ("bandage", 20), ("hacking_kit", 3), ("cyberdeck", 1), ("backpack", 4), ("flare", 6) })
            if (sim.Scavenge!.Items.ContainsKey(id) && state.Inventory.Count(id) < n) state.Inventory.Add(id, n - state.Inventory.Count(id));
        foreach (var (res, n) in new[] { ("food", 400.0), ("wood", 200.0), ("scrap", 200.0), ("medicine", 40.0) })
            if (state.Stockpile.Get(res) < n) state.Stockpile.Add(res, n - state.Stockpile.Get(res));
    }

    /// <summary>Once a day: stock topped up, the party patched up (this is a story run, not a survival test).</summary>
    private static void Keep(ColonySim sim, StoryVariant variant)
    {
        Stock(sim);
        foreach (var s in sim.State.Survivors.Where(s => !s.Away))
        {
            s.Health = Math.Max(s.Health, 90);
            foreach (var p in s.Body.Parts.Values.Where(p => p.Condition == PartCondition.Injured)) { p.Condition = PartCondition.Healthy; p.Severity = 0; }
            s.Mood = Math.Max(s.Mood, 60);
            s.PatientAtBuildingId = null;
            s.HomelessTicks = 0;
            s.StarvingTicks = 0;
        }
        // Volunteers keep coming: the run is about the story, so the colony never runs out of a party.
        var hands = sim.State.Survivors.Count(s => !s.IsChild && s.UniqueId is null);
        for (var i = hands; i < 8; i++)
        {
            var r = StoryCharacters.Recruit(sim, $"Volunteer {sim.State.NextSurvivorId}", child: false, humanity: 100);
            foreach (var a in Enum.GetValues<AttributeKind>()) r.Set(a, 80);
            foreach (var k in new[] { Skill.Firearms, Skill.Melee, Skill.Medicine, Skill.Hacking }) r.Set(k, 65);
        }
        variant.Daily?.Invoke(sim);
    }

    private static void TakeCalls(ColonySim sim, StoryVariant variant, Action<string>? trace)
    {
        foreach (var call in Campaign.Waiting(sim).ToList())
        {
            if (!sim.Execute(new TakeCall(call.Id)).Ok) continue;
            for (var i = 0; i < 200 && Dialogue.View(sim) is { } view; i++)
            {
                if (view.Choices.Count == 0) { sim.Execute(new AdvanceDialogue()); continue; }
                // "a|b": the first choice on this line that matches a, else b (a scene can ask twice: the core's two menus).
                var wants = variant.Choices.GetValueOrDefault(view.DialogueId)?.Split('|') ?? [];
                var pick = wants.Select(w => view.Choices.FirstOrDefault(c => c.Text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault(c => c.Text is not null);
                if (pick.Text is null) pick = view.Choices[0];
                trace?.Invoke($"  {view.DialogueId}/{view.NodeId}: \"{pick.Text}\"");
                sim.Execute(new ChooseDialogue(view.NodeId, pick.Index));
            }
        }
    }

    private static void Launch(ColonySim sim, StoryVariant variant, List<string> problems)
    {
        var state = sim.State;
        // Fewest tries first, so a mission the bot keeps leaving (a stealth one) does not hold the story up.
        foreach (var (def, _) in Campaign.Visible(sim).Where(m => m.Def.Type == MissionType.Field && m.Progress.Status == MissionStatus.Available)
                     .OrderBy(m => m.Progress.Attempts).ToList())
        {
            if (variant.Wait?.Invoke(sim, def) == true) continue;
            // The bot cannot sneak: a stealth-only mission that fails when seen is left to players.
            if (def.Field!.Objectives.Any(o => o.Kind == ObjectiveKind.Undetected && o.FailsMission)) continue;
            var party = new List<Survivor>();
            foreach (var who in def.MustBring)
                if ((who == StoryCharacters.Key ? Campaign.KeyCarrierSurvivor(sim) : StoryCharacters.SurvivorOf(sim, who)) is { } s) party.Add(s);
            if (def.ChainFrom is { } from && state.Campaign!.ChainParties.TryGetValue(from, out var chain))
                party.AddRange(chain.Select(state.FindSurvivor).OfType<Survivor>().Where(s => !party.Contains(s)));
            var size = def.MaxParty ?? sim.MaxPartySize;
            party.AddRange(state.Survivors.Where(s => !s.IsChild && !s.Away && !party.Contains(s) && s.TraitHooks.Count == 0 && s.Health >= 50
                                                       && (def.ChainFrom is null || state.Campaign!.ChainParties.GetValueOrDefault(def.ChainFrom)?.Contains(s.Id) != false))
                .OrderByDescending(s => s.Get(AttributeKind.Strength) + s.Get(AttributeKind.Agility) + s.Get(AttributeKind.Endurance)).ThenBy(s => s.Id)
                .Take(Math.Max(0, size - party.Count)));
            if (party.Count == 0) continue;
            var members = party.Select(s => new ExpeditionMember(s.Id, new Dictionary<EquipSlot, string>(), new List<string>())).ToList();
            void Give(int i, EquipSlot slot, string id)
            {
                if (state.Inventory.Count(id) - members.Sum(m => m.AllItems.Count(x => x == id)) <= 0) return;
                members[i].Equipment![slot] = id;
            }
            void Pack(int i, string id)
            {
                if (state.Inventory.Count(id) - members.Sum(m => m.AllItems.Count(x => x == id)) <= 0) return;
                members[i].Pack!.Add(id);
            }
            for (var i = 0; i < members.Count; i++)
            {
                Give(i, EquipSlot.Weapon, "linked_carbine");
                if (!members[i].Equipment!.ContainsKey(EquipSlot.Weapon)) Give(i, EquipSlot.Weapon, "rifle");
                Give(i, EquipSlot.Body, "linked_plating");
                if (!members[i].Equipment!.ContainsKey(EquipSlot.Body)) Give(i, EquipSlot.Body, "riot_armor");
                Give(i, EquipSlot.Tool, "backpack");
                Pack(i, "med_kit");
                Pack(i, "bandage");
            }
            Pack(0, "hacking_kit");
            if (members.Count > 1) Pack(1, "hacking_kit");
            var result = sim.Execute(new LaunchStoryMission(def.Id, members));
            if (result.Ok) return;
            problems.Add($"day {Campaign.Day(sim)}: launch {def.Id}: {result.Message}");
        }
    }

    public static void Print(StoryActsReport r, Action<string> write)
    {
        write($"== variant {r.Variant}: {r.Days} days, ending {r.Ending ?? "-"}, codex pages {r.Codex}");
        foreach (var run in r.Runs) write($"  day {run.Day,3} {run.Mission,-20} {run.Outcome,-10} back {run.Back} dead {run.Dead} rounds {run.Rounds}");
        write("  missions: " + string.Join(", ", r.Missions.Where(kv => kv.Value != "locked").Select(kv => $"{kv.Key}={kv.Value}")));
        write("  joined: " + string.Join(", ", r.Joined));
        write("  flags: " + string.Join(", ", r.Flags.Where(kv => kv.Value != 0 && !kv.Key.Contains("_o_") && !kv.Key.StartsWith("trader")).Select(kv => $"{kv.Key}={kv.Value}")));
        foreach (var (still, text) in r.Epilogue) write($"  [{still}] {text}");
        write("  memorial: " + string.Join("; ", r.Memorial));
        foreach (var p in r.Problems) write($"  ! {p}");
    }
}
