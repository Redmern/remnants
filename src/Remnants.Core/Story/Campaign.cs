using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Story;

/// <summary>Take a waiting radio call: its dialogue opens.</summary>
public sealed record TakeCall(int CallId) : IColonyCommand;

/// <summary>Go on from a line without choices (to the next line, or the end).</summary>
public sealed record AdvanceDialogue : IColonyCommand;

/// <summary>Pick choice <see cref="Index"/> on line <see cref="NodeId"/> of the open dialogue.</summary>
public sealed record ChooseDialogue(string NodeId, int Index) : IColonyCommand;

/// <summary>Send a party on an available field mission (the expedition rules apply; the mission may allow a bigger party).</summary>
public sealed record LaunchStoryMission(string MissionId, List<ExpeditionMember> Members) : IColonyCommand;

/// <summary>
/// The campaign's rules: starting Story mode, unlocking missions from colony state (in definition order, every tick),
/// colony objectives and scenes, the radio inbox, launching a field mission and building its run, paying rewards on
/// return, failing and retrying. Dialogue lives in <see cref="Dialogue"/>.
/// </summary>
public static class Campaign
{
    public const string PellId = "pell";

    /// <summary>
    /// Turns a fresh colony into a Story game: one of the starting children is Pell (he/him), at the start of his childhood, and the first missions
    /// unlock at once.
    /// </summary>
    public static void Start(ColonySim sim, string difficulty = "remnant")
    {
        var state = sim.State;
        state.Campaign = new CampaignState { Difficulty = difficulty };
        if (state.Survivors.Where(s => s.IsChild && !s.IsInfant).OrderBy(s => s.Id).FirstOrDefault() is { } pell)
        {
            pell.Name = "Pell";
            pell.Sex = Sex.Male;
            pell.UniqueId = PellId;
            // At the start of childhood, so he grows up during the campaign, not before it starts.
            pell.GrowthTicks = sim.Data.Rules.Life.InfantTicks;
            pell.LifeStage = Bonds.StageAt(pell.GrowthTicks, sim.Data.Rules.Life);
            pell.AgeTicks = Bonds.AgeTicksAt(pell.GrowthTicks, sim.Data.Rules);
            foreach (var trait in new[] { "radio_kid", "grew_up_here" })
                if (sim.Story?.Traits.GetValueOrDefault(trait) is { } t)
                {
                    pell.Traits.Add(t.Id);
                    foreach (var (k, v) in t.Hooks) pell.TraitHooks[k] = pell.TraitHooks.GetValueOrDefault(k) + v;
                }
        }
        foreach (var m in sim.Story?.Missions ?? []) state.Campaign.Progress(m.Id);
        Tick(sim);
    }

    public static int Day(ColonySim sim) => (int)(sim.State.Tick / sim.Data.Rules.TicksPerDay) + 1;

    /// <summary>Once per colony tick (after the systems): retries, unlocks, colony objectives and scenes.</summary>
    public static void Tick(ColonySim sim)
    {
        if (sim.State.Campaign is not { } campaign || sim.Story is not { } story) return;
        var now = sim.State.Tick;
        foreach (var call in campaign.Inbox.Where(c => !c.Rung && !c.Taken && c.Tick <= now).ToList()) Ring(sim, call);
        StoryCharacters.Tick(sim);
        StoryWorld.Tick(sim);
        // The caravan's visits once Marrow trades with the colony (M6 waits for two).
        var visits = sim.State.Radio.Visits;
        if (visits > campaign.Flag("trader_visits_seen") && campaign.Flag("caravan_open") > 0)
            campaign.Flags["marrow_visits"] = campaign.Flag("marrow_visits") + visits - campaign.Flag("trader_visits_seen");
        campaign.Flags["trader_visits_seen"] = visits;
        if (campaign.Over) return;
        foreach (var def in story.Missions)
        {
            var p = campaign.Progress(def.Id);
            switch (p.Status)
            {
                case MissionStatus.Done when p.RepeatTick <= now:
                    p.Status = MissionStatus.Available;
                    p.RepeatTick = null;
                    p.AvailableTick = now;
                    if (def.Briefing is { } again) QueueCall(sim, again);
                    Log(sim, $"{def.Name} is open again.");
                    break;
                case MissionStatus.Failed when p.RetryTick <= now:
                    p.Status = MissionStatus.Available;
                    p.RetryTick = null;
                    p.AvailableTick = now;
                    Log(sim, $"{def.Name} is back on the map.");
                    break;
                case MissionStatus.Locked when def.Requires.All(c => Holds(sim, c)):
                    p.Status = def.Type == MissionType.Colony ? MissionStatus.Active : MissionStatus.Available;
                    p.AvailableTick = now;
                    if (def.Briefing is { } briefing) QueueCall(sim, briefing);
                    Log(sim, def.Task ? $"New colony task: {def.Name}." : $"New mission: {def.Name}.");
                    break;
            }
            if (p.Status == MissionStatus.Active && def.Type == MissionType.Colony) ColonyObjectives(sim, def, p);
        }
    }

    private static void ColonyObjectives(ColonySim sim, MissionDef def, MissionProgress p)
    {
        var state = sim.State;
        var campaign = state.Campaign!;
        foreach (var o in def.ColonyObjectives.Where(o => !p.Objectives.Contains(o.Id)))
        {
            var done = o.Kind switch
            {
                ColonyObjectiveKind.Build => state.Buildings.Any(b => b.DefId == o.Building && b.Level >= o.Level),
                ColonyObjectiveKind.HaveItem => state.Inventory.Count(o.Item!) >= o.Count,
                ColonyObjectiveKind.HaveResource => (o.Resource == Pantry.FoodKey ? Pantry.FoodValue(state, sim.Data) : state.Stockpile.Get(o.Resource!)) >= o.Count,
                ColonyObjectiveKind.Patient => state.Survivors.Any(s => s.PatientAtBuildingId is not null),
                ColonyObjectiveKind.ChildJob => state.Survivors.Any(s => s.IsChild && s.AssignedBuildingId is { } b
                    && state.FindBuilding(b) is { } at && sim.Data.Buildings[at.DefId].Job == o.Job),
                ColonyObjectiveKind.Scene => campaign.Heard.Contains(o.Dialogue!),
                _ => false,
            };
            if (o.Kind == ColonyObjectiveKind.Scene && !done && !p.SceneQueued
                && def.ColonyObjectives.Where(x => x.Id != o.Id).All(x => p.Objectives.Contains(x.Id))
                && (o.Phase is not { } phase || sim.Phase == phase))
            {
                p.SceneQueued = true;
                QueueCall(sim, o.Dialogue!);
            }
            if (!done) continue;
            p.Objectives.Add(o.Id);
            Log(sim, $"{def.Name}: {o.Text} — done.");
        }
        if (def.ColonyObjectives.All(o => p.Objectives.Contains(o.Id))) Close(sim, def, p, done: true, null);
    }

    /// <summary>Whether a condition holds for this colony now.</summary>
    public static bool Holds(ColonySim sim, ConditionDef c)
    {
        var state = sim.State;
        var campaign = state.Campaign;
        if (c.Mission is { } m)
        {
            if (campaign?.Missions.GetValueOrDefault(m) is not { Status: MissionStatus.Done } p) return false;
            if (c.Outcome is { } outcome && p.Outcome != outcome) return false;
        }
        if (c.Building is { } b && !state.Buildings.Any(x => x.DefId == b && x.Level >= c.Level)) return false;
        if (c.TownHall is { } th && sim.TownHallLevel < th) return false;
        if (c.Day is { } day && Day(sim) < day) return false;
        if (c.Flag is { } f)
        {
            var v = campaign?.Flag(f) ?? 0;
            if (c.Min is { } min && v < min) return false;
            if (c.Max is { } max && v > max) return false;
            if (c.Min is null && c.Max is null && v == 0) return false;
        }
        if (c.Item is { } item && state.Inventory.Count(item) < c.Count) return false;
        if (c.Alive is { } who && (campaign?.Characters.GetValueOrDefault(who)?.Dead == true || who == PellId && StoryCharacters.SurvivorOf(sim, PellId) is null)) return false;
        if (c.Dead is { } dead && campaign?.Characters.GetValueOrDefault(dead)?.Dead != true) return false;
        if (c.Joined is { } joined && (campaign?.Characters.GetValueOrDefault(joined) is not { Joined: true, Dead: false } || StoryCharacters.SurvivorOf(sim, joined) is null)) return false;
        if (c.Adult is { } adult && StoryCharacters.SurvivorOf(sim, adult) is not { IsChild: false }) return false;
        if (c.HumanityMin is not null || c.HumanityMax is not null)
        {
            double? humanity = c.Of is { } of ? StoryCharacters.SurvivorOf(sim, of)?.Body.Humanity : StoryCharacters.ColonyHumanity(sim);
            if (humanity is not { } h) return false;
            if (c.HumanityMin is { } hmin && h < hmin) return false;
            if (c.HumanityMax is { } hmax && h > hmax) return false;
        }
        if (c.Skill is { } skill && !state.Survivors.Any(s => !s.IsChild && !s.Away && s.Get(Enum.Parse<Skill>(skill)) >= c.SkillMin)) return false;
        if (c.Any is { Count: > 0 } any && !any.Any(a => Holds(sim, a))) return false;
        if (c.Not is { } not && Holds(sim, not)) return false;
        return true;
    }

    /// <summary>
    /// A story schematic reward: learned now through the schematics module (<c>Schematics.Grant</c>, source Story); a
    /// known one pays its duplicate value in Research Data. Returns the debrief line.
    /// </summary>
    public static string GrantSchematic(ColonySim sim, string schematicId)
    {
        var name = sim.Data.Schematics.Find(schematicId)?.Name ?? SchematicName(schematicId);
        if (sim.Data.Schematics.Find(schematicId) is null) return $"Schematic: {name}";
        var result = Remnants.Core.Schematics.Schematics.Grant(sim, schematicId, Remnants.Core.Schematics.SchematicSource.Story);
        return result.Learned ? $"Schematic: {name}" : $"{name} (known: +{result.ResearchData:0.#} Research Data)";
    }

    /// <summary>Tag of a story call in the Radio Mast's queue: "story:" + the campaign call id.</summary>
    public const string CallTagPrefix = "story:";

    /// <summary>
    /// Queues a character's call: recorded in the campaign (<see cref="CampaignState.Inbox"/>: which dialogue it opens)
    /// and, once its time comes, put in the Radio Mast's queue (<see cref="Remnants.Core.Schematics.RadioMast.Enqueue"/>,
    /// tag <c>story:&lt;id&gt;</c>), where answering it opens the dialogue like <see cref="TakeCall"/>.
    /// </summary>
    public static RadioCall QueueCall(ColonySim sim, string dialogueId, long delayTicks = 0)
    {
        var campaign = sim.State.Campaign!;
        var call = new RadioCall { Id = campaign.NextCallId++, DialogueId = dialogueId, Tick = sim.State.Tick + delayTicks };
        campaign.Inbox.Add(call);
        if (delayTicks == 0) Ring(sim, call);
        return call;
    }

    /// <summary>Puts a campaign call that has come in on the Radio Mast (once).</summary>
    private static void Ring(ColonySim sim, RadioCall call)
    {
        if (call.Rung || call.Taken) return;
        call.Rung = true;
        var def = sim.Story?.Dialogues.GetValueOrDefault(call.DialogueId);
        var caller = def is null ? "radio" : def.Caller;
        var name = def is null ? "A voice on the radio" : CallerName(sim, def);
        Remnants.Core.Schematics.RadioMast.Enqueue(sim, Remnants.Core.Schematics.RadioCallKind.Story, caller, name,
            def?.Title ?? "", tag: CallTagPrefix + call.Id);
    }

    /// <summary>The player answered a story call on the Radio Mast: its dialogue opens.</summary>
    internal static void Answered(ColonySim sim, Remnants.Core.Schematics.RadioCall radioCall)
    {
        if (sim.State.Campaign is null || radioCall.Tag is not { } tag || !tag.StartsWith(CallTagPrefix)) return;
        if (int.TryParse(tag[CallTagPrefix.Length..], out var id)) Dialogue.Take(sim, id);
    }

    /// <summary>Calls that have come in and not been taken, oldest first.</summary>
    public static IEnumerable<RadioCall> Waiting(ColonySim sim) =>
        (sim.State.Campaign?.Inbox ?? []).Where(c => !c.Taken && c.Tick <= sim.State.Tick).OrderBy(c => c.Tick).ThenBy(c => c.Id);

    public static string CallerName(ColonySim sim, DialogueDef d) =>
        sim.Story?.Characters.GetValueOrDefault(d.Caller) is { } c ? c.CallSign ?? c.Name : d.Title;

    // ---- Field missions ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Why this party cannot go on the mission, or null: a chain floor takes only who came back from the floor before;
    /// a mission that needs a story character (or the key-carrier) needs them in the party; a survivor whose trait keeps
    /// them home (Iris) goes only where the mission needs them.
    /// </summary>
    public static string? CannotTake(ColonySim sim, MissionDef def, IReadOnlyList<int> party)
    {
        var campaign = sim.State.Campaign!;
        if (def.ChainFrom is { } from && campaign.ChainParties.TryGetValue(from, out var chain) && party.Any(id => !chain.Contains(id)))
            return "Only the survivors who came out of the floor below can go on.";
        var needed = new HashSet<int>();
        foreach (var who in def.MustBring)
        {
            var id = who == "key" ? KeyCarrierSurvivor(sim)?.Id : StoryCharacters.SurvivorOf(sim, who)?.Id;
            if (id is null) continue;
            if (!party.Contains(id.Value)) return $"{sim.State.FindSurvivor(id.Value)!.Name} must be in the party.";
            needed.Add(id.Value);
        }
        foreach (var id in party.Where(i => !needed.Contains(i)))
            if (sim.State.FindSurvivor(id) is { } s && s.TraitHooks.GetValueOrDefault(PerkHooks.NoScavenge) > 0)
                return $"{s.Name} does not go out unless the mission needs them.";
        return null;
    }

    /// <summary>The colony survivor carrying the key (M16): Iris, Seven, or the one who took the port.</summary>
    public static Survivor? KeyCarrierSurvivor(ColonySim sim)
    {
        var campaign = sim.State.Campaign!;
        return campaign.Flag("key_carrier") switch
        {
            1 => StoryCharacters.SurvivorOf(sim, "iris"),
            2 => StoryCharacters.SurvivorOf(sim, StoryCharacters.Seven),
            3 => sim.State.FindSurvivor(campaign.Flag("key_survivor")),
            _ => null,
        };
    }

    /// <summary>Why a field mission cannot be launched now, or null.</summary>
    public static string? CannotLaunch(ColonySim sim, string missionId)
    {
        if (sim.State.Campaign is not { } campaign || sim.Story is not { } story) return "This is a Sandbox colony.";
        if (story.Mission(missionId) is not { Type: MissionType.Field }) return "No such mission.";
        if (campaign.Progress(missionId).Status != MissionStatus.Available) return "The mission is not open.";
        return null;
    }

    internal static CommandResult Launch(ColonySim sim, LaunchStoryMission c)
    {
        if (sim.State.Campaign is null || sim.Story is null) return CommandResult.Fail(CommandError.NoCampaign, "This is a Sandbox colony.");
        if (sim.Story.Mission(c.MissionId) is not { Type: MissionType.Field } def) return CommandResult.Fail(CommandError.UnknownMission, $"No mission '{c.MissionId}'.");
        if (CannotLaunch(sim, c.MissionId) is { } why) return CommandResult.Fail(CommandError.MissionNotAvailable, why);
        if (CannotTake(sim, def, c.Members.Select(m => m.SurvivorId).ToList()) is { } party) return CommandResult.Fail(CommandError.MissionNotAvailable, party);
        if (StoryWorld.BreaksTruce(sim, def))
        {
            StoryWorld.BreakTruce(sim);
            return CommandResult.Fail(CommandError.MissionNotAvailable, "Going there breaks the truce. Halcyon is coming: hold the colony first.");
        }
        // A mission in a generated zone goes in by daylight (the party waits for it), since night fills those zones with
        // more Husks; a handmade place keeps its fixed cast, and a defense is fought when it comes.
        var wait = def.HandmadeId is not null || def.Perimeter || sim.Phase == DayPhase.Day ? (DayPhase?)null : DayPhase.Day;
        var result = ExpeditionHandlers.Launch(sim, new LaunchExpedition(def.Zone!, c.Members, wait), def.MaxParty);
        if (!result.Ok) return result;
        sim.State.Expedition!.MissionId = def.Id;
        // One of our own takes the port (M16): the first in the party, unless Iris or Seven carries the key.
        if (def.MustBring.Contains(StoryCharacters.Key) && sim.State.Campaign.Flag("key_carrier") == 3 && KeyCarrierSurvivor(sim) is null)
            sim.State.Campaign.Flags["key_survivor"] = c.Members[0].SurvivorId;
        var p = sim.State.Campaign.Progress(def.Id);
        p.Status = MissionStatus.Active;
        p.Attempts++;
        Log(sim, $"Mission started: {def.Name}.");
        return result;
    }

    /// <summary>The run setup of a field mission: its script, the map (the perimeter variant by Town Hall level) and room, towers, a retry's extra enemies.</summary>
    public static MissionSetup Setup(ColonySim sim, string missionId)
    {
        var story = sim.Story ?? throw new InvalidOperationException("Story data is not loaded.");
        var def = story.Mission(missionId) ?? throw new InvalidOperationException($"Unknown mission '{missionId}'.");
        var p = sim.State.Campaign!.Progress(missionId);
        var towers = sim.State.Buildings.Where(b => b.DefId == "watchtower").Sum(b => b.Level);
        var extra = p.Attempts > 1 ? def.Fail.ExtraEnemies.ToList() : [];
        var party = sim.State.Expedition?.Members.Select(m => sim.State.FindSurvivor(m.SurvivorId)).OfType<Survivor>().ToList() ?? [];
        var map = story.MapFor(def, sim.TownHallLevel);
        // A perimeter defense is fought on the real colony when it has what the script needs; the handmade map is the fallback.
        if (def.Perimeter && sim.Raids is { } raids && Raids.RaidBattle.StoryMap(sim, raids, def.Field!) is { } colony)
        {
            map = colony;
            towers = Raids.RaidBattle.TowerCount(colony);
        }
        // Seven's Network Echo: now and then the Linked notice it, and send a patrol.
        var echo = party.Sum(s => s.TraitHooks.GetValueOrDefault(PerkHooks.NetworkEchoChance));
        if (echo > 0 && new Random.DeterministicRng(sim.State.Expedition?.Seed ?? 0).NextDouble() < echo
            && (map ?? (def.Room is { } r ? story.Maps[r] : null))?.Enemies.FirstOrDefault() is { } near)
            extra.Add(new MapEnemyDef("linked", near.Marker, "echo", 1));
        var tags = new Dictionary<int, string>();
        foreach (var s in party.Where(s => s.UniqueId is not null)) tags[s.Id] = s.UniqueId!;
        if (KeyCarrierSurvivor(sim) is { } key && party.Contains(key)) tags[key.Id] = "key";
        return new MissionSetup(def.Field!, map, def.Room is { } room ? story.Maps[room] : null, towers, extra.Count > 0 ? extra : null)
        {
            CampaignFlags = new Dictionary<string, int>(sim.State.Campaign.Flags),
            Tags = tags,
            GuestSchematics = GuestSchematics(def),
        };
    }

    /// <summary>What a co-op guest copies from a mission: its schematic rewards (STORY.md "Co-op").</summary>
    public static List<string> GuestSchematics(MissionDef def) =>
        def.Rewards.Where(e => e.Kind == EffectKind.Schematic).Select(e => e.Id).Distinct().ToList();

    /// <summary>The party is home from a story mission: rewards on success, the fail branch on failure, else it stays open.</summary>
    public static void Returned(ColonySim sim, string missionId, ObjectiveResult? result, IReadOnlyList<int>? party = null)
    {
        if (sim.State.Campaign is not { } campaign || sim.Story?.Mission(missionId) is not { } def) return;
        var p = campaign.Progress(missionId);
        foreach (var (flag, value) in result?.Flags ?? new()) campaign.Flags[flag] = campaign.Flag(flag) + value;
        // Each objective that came off is a flag "<mission>_<objective>" (rewards and later missions read them).
        foreach (var (objective, status) in result?.Objectives ?? new())
            if (status == ObjectiveStatus.Done) campaign.Flags[$"{missionId}_{objective}"] = 1;
        foreach (var who in result?.Escorted ?? []) campaign.Character(who).Met = true;
        // A chain floor: who came out may go on to the next floor (and only they).
        if (result?.Outcome == MissionOutcome.Done && sim.Story!.Missions.Any(m => m.ChainFrom == missionId))
            campaign.ChainParties[missionId] = (party ?? []).Where(id => sim.State.FindSurvivor(id) is not null).ToList();
        var outcome = result?.Outcome ?? MissionOutcome.Incomplete;
        var objectives = def.Field?.Objectives.Select(o =>
            $"{(result?.Objectives.GetValueOrDefault(o.Id) == ObjectiveStatus.Done ? "✓" : result?.Objectives.GetValueOrDefault(o.Id) == ObjectiveStatus.Failed ? "✗" : "·")} {o.Text}").ToList() ?? [];
        if (outcome == MissionOutcome.Incomplete)
        {
            p.Status = MissionStatus.Available;
            campaign.LastDebrief = new(def.Id, "not done yet", sim.State.Tick, [], objectives);
            Log(sim, $"{def.Name} is not done yet; it stays on the map.");
            return;
        }
        Close(sim, def, p, outcome == MissionOutcome.Done, objectives);
    }

    /// <summary>
    /// A mission ends: done pays its rewards and queues its debrief; failed applies the fail effects and either comes back
    /// after <see cref="FailDef.RetryDays"/> or closes for good as "failed" (no failure ends the campaign).
    /// </summary>
    private static void Close(ColonySim sim, MissionDef def, MissionProgress p, bool done, List<string>? objectives)
    {
        var campaign = sim.State.Campaign!;
        var lines = new List<string>();
        if (done)
        {
            p.Status = MissionStatus.Done;
            p.Outcome = "done";
            p.DoneTick = sim.State.Tick;
            foreach (var e in def.Rewards) Apply(sim, e, $"{def.Name}", lines);
            if (def.Debrief is { } debrief) QueueCall(sim, debrief);
            if (def.RepeatDays is { } every) p.RepeatTick = sim.State.Tick + every * sim.Data.Rules.TicksPerDay;
            Log(sim, $"Mission complete: {def.Name}.");
        }
        else
        {
            foreach (var e in def.Fail.Effects) Apply(sim, e, $"{def.Name}", lines);
            // A failed floor of a chain: the assault starts again from the floor below with whoever is fit (cleared floors stay cleared).
            if (def.ChainFrom is { } below) campaign.ChainParties.Remove(below);
            // A fail effect may close the mission for good (the person it was about is dead): no retry then.
            if (p.Status == MissionStatus.Done) { }
            else if (def.Fail.RetryDays is { } days)
            {
                p.Status = MissionStatus.Failed;
                p.RetryTick = sim.State.Tick + days * sim.Data.Rules.TicksPerDay;
            }
            else
            {
                p.Status = MissionStatus.Done;
                p.Outcome = "failed";
                p.DoneTick = sim.State.Tick;
            }
            if (def.Fail.Call is { } call) QueueCall(sim, call);
            Log(sim, $"Mission failed: {def.Name}.");
        }
        // Colony missions close with a log line and a toast; a field mission gets the debrief panel.
        if (def.Type == MissionType.Field)
            campaign.LastDebrief = new(def.Id, done ? "done" : "failed", sim.State.Tick, lines, objectives ?? []);
    }

    /// <summary>Applies one effect (if its condition holds); <paramref name="lines"/> collects what it paid, for the debrief.</summary>
    public static void Apply(ColonySim sim, EffectDef e, string source, List<string>? lines = null)
    {
        if (e.When is { } when && !Holds(sim, when)) return;
        var state = sim.State;
        var campaign = state.Campaign!;
        switch (e.Kind)
        {
            case EffectKind.SetFlag:
                campaign.Flags[e.Id] = e.Value;
                break;
            case EffectKind.AddFlag:
                campaign.Flags[e.Id] = campaign.Flag(e.Id) + e.Value;
                break;
            case EffectKind.Schematic:
                var granted = GrantSchematic(sim, e.Id);
                lines?.Add(granted);
                break;
            case EffectKind.Item:
                if (e.Value >= 0) state.Inventory.Add(e.Id, e.Value);
                else state.Inventory.TryTake(e.Id, Math.Min(-e.Value, state.Inventory.Count(e.Id)));
                if (e.Value > 0) lines?.Add($"{e.Value} × {ItemName(sim, e.Id)}");
                break;
            case EffectKind.Resource when e.Id == Pantry.FoodKey:
                // "food" rewards are food value: canned food into the pantry.
                Pantry.AddFoodValue(state, sim.Data, e.Value);
                lines?.Add($"{e.Value} food (canned)");
                break;
            case EffectKind.Resource:
                state.Stockpile.Add(e.Id, e.Value);
                lines?.Add($"{e.Value} {sim.Data.Resources[e.Id].Name}");
                break;
            case EffectKind.ResearchData:
                state.Stockpile.Add(sim.Data.Schematics.Rules.ResearchDataResource, e.Value);
                lines?.Add($"{e.Value} Research Data");
                break;
            case EffectKind.RevealZone:
                var intel = e.Value >= 2 ? ZoneIntel.Scouted : ZoneIntel.Rumored;
                if (!state.Zones.TryGetValue(e.Id, out var status)) state.Zones[e.Id] = status = new ZoneStatus();
                if (status.Intel < intel) status.Intel = intel;
                lines?.Add(e.Text ?? $"Radio reaches {ZoneName(sim, e.Id)}");
                break;
            case EffectKind.Call:
                QueueCall(sim, e.Id, e.Value > 1 ? e.Value : 0);
                break;
            case EffectKind.Character:
                var c = campaign.Character(e.Id);
                c.Met = true;
                if (e.Value >= 2 && StoryCharacters.Join(sim, e.Id) is { } joinedSurvivor) lines?.Add($"{joinedSurvivor.Name} joins the colony");
                break;
            case EffectKind.Fallback:
                lines?.Add(e.Text ?? "A reward for later");
                break;
            case EffectKind.Note:
                Log(sim, e.Text ?? "");
                lines?.Add(e.Text ?? "");
                break;
            case EffectKind.Mood:
                if (StoryCharacters.SurvivorOf(sim, e.Id) is { } moody) moody.Mood = Math.Clamp(moody.Mood + e.Value, 0, Survivor.MaxMood);
                break;
            case EffectKind.Humanity:
                if (StoryCharacters.SurvivorOf(sim, e.Id) is { } changed)
                    changed.Body.HumanityLoss = Math.Clamp(changed.Body.HumanityLoss - e.Value, 0, Body.MaxHumanity);
                break;
            case EffectKind.Leaves:
                StoryCharacters.Leave(sim, e.Id, e.Text);
                break;
            case EffectKind.Dies:
                StoryCharacters.Die(sim, e.Id, e.Text);
                break;
            case EffectKind.Close:
                var closing = campaign.Progress(e.Id);
                if (closing.Status is not MissionStatus.Done)
                {
                    closing.Status = MissionStatus.Done;
                    closing.Outcome = e.Text ?? "done";
                    closing.DoneTick = state.Tick;
                    closing.RetryTick = null;
                }
                break;
            case EffectKind.WorldEffect:
                StoryWorld.Start(sim, e.Id, e.Value);
                break;
            case EffectKind.EndWorldEffect:
                StoryWorld.End(sim, e.Id);
                break;
            case EffectKind.Ending:
                StoryWorld.End(sim, e.Id, ending: true);
                break;
            case EffectKind.Recruit:
                var recruit = StoryCharacters.Recruit(sim, e.Text ?? "Stranger", e.Id == "child", e.Value <= 1 ? Body.MaxHumanity : e.Value);
                lines?.Add($"{recruit.Name} joins the colony");
                break;
            case EffectKind.ReturnTaken:
                foreach (var name in StoryWorld.ReturnTaken(sim, e.Value)) lines?.Add($"{name} came home");
                break;
            case EffectKind.Rename:
                if (StoryCharacters.SurvivorOf(sim, e.Id) is { } renamed && e.Text is { Length: > 0 } newName) renamed.Name = newName;
                break;
        }
    }

    public static string SchematicName(string id) =>
        string.Join(' ', id.Replace("sch_", "").Split('_').Select(w => w.Length > 0 && char.IsDigit(w[0]) ? $"L{w}" : char.ToUpperInvariant(w[0]) + w[1..]));

    private static string ItemName(ColonySim sim, string id) => sim.Scavenge?.Items.GetValueOrDefault(id)?.Name ?? id;
    private static string ZoneName(ColonySim sim, string id) => sim.Scavenge?.Zones.GetValueOrDefault(id)?.Name ?? id;

    internal static void Log(ColonySim sim, string message) =>
        sim.State.Log(new(sim.State.Tick, ColonyEventType.StoryMission, message));

    internal static CommandResult Execute(ColonySim sim, IColonyCommand command)
    {
        if (sim.State.Campaign is null || sim.Story is null) return CommandResult.Fail(CommandError.NoCampaign, "This is a Sandbox colony.");
        return command switch
        {
            TakeCall c => Dialogue.Take(sim, c.CallId),
            AdvanceDialogue => Dialogue.Advance(sim),
            ChooseDialogue c => Dialogue.Choose(sim, c.NodeId, c.Index),
            LaunchStoryMission c => Launch(sim, c),
            _ => CommandResult.Fail(CommandError.UnknownCommand, $"Unknown command {command.GetType().Name}."),
        };
    }

    /// <summary>The missions to show (journal, world map): not locked, in campaign order.</summary>
    public static IEnumerable<(MissionDef Def, MissionProgress Progress)> Visible(ColonySim sim) =>
        sim.State.Campaign is not { } campaign || sim.Story is not { } story
            ? []
            : story.Missions.Select(m => (m, campaign.Progress(m.Id))).Where(t => t.Item2.Status != MissionStatus.Locked);
}
