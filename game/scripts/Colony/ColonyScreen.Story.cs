using System.Linq;
using Remnants.Core.Colony;
using Remnants.Core.Story;

namespace Remnants.Game.Colony;

/// <summary>
/// Debug stagings of Story mode for screenshots (<c>--story-demo=radio|dialogue|worldmap|briefing|journal</c>): a
/// fresh Story colony plays M1 through Core commands until First Light calls, then stops on the asked-for screen.
/// Acts 2–4 (<c>--story-demo=offer|join|codex|epilogue [--ending=terms|silence|voice|calm] [--slide=N]</c>): Act 1 is
/// taken as done and the campaign is set up for the scene: Halcyon's Offer, Doc joining after M7, the Codex, an ending.
/// </summary>
public partial class ColonyScreen
{
    private void RunStoryDemo(string stage)
    {
        var sim = _session.Sim;
        if (sim.State.Campaign is null) Campaign.Start(sim);
        if (stage is "offer" or "join" or "codex" or "epilogue")
        {
            RunActsDemo(stage);
            return;
        }
        foreach (var (res, amount) in new[] { ("wood", 120.0), ("stone", 60.0), ("scrap", 30.0), ("food", 40.0) }) sim.State.Stockpile.Add(res, amount);
        var plot = 8;
        foreach (var id in new[] { "farm", "woodcutter", "workshop", "radio_mast" })
            _session.Execute(new Construct(id, plot++));
        var adults = sim.State.Survivors.Where(s => !s.IsChild).Select(s => s.Id).ToList();
        var farm = sim.State.Buildings.First(b => b.DefId == "farm").Id;
        foreach (var a in adults.Take(2)) _session.Execute(new AssignJob(a, farm));

        // Play every call but the First Light briefing (first choice each time), until it is in.
        for (var guard = 0; guard < 20; guard++)
        {
            if (Campaign.Waiting(sim).FirstOrDefault(c => c.DialogueId == "m2_first_light") is { } briefing)
            {
                if (stage == "radio") break;
                _session.Execute(new TakeCall(briefing.Id));
                if (stage == "dialogue")
                {
                    while (Dialogue.View(sim) is { Choices.Count: 0 }) _session.Execute(new AdvanceDialogue());
                    break;
                }
                PlayOut();
                break;
            }
            if (Campaign.Waiting(sim).FirstOrDefault() is { } call)
            {
                _session.Execute(new TakeCall(call.Id));
                PlayOut();
                continue;
            }
            _session.FastForward(48, s => Campaign.Waiting(s).Any());
        }
        _session.SetSpeed(0);
        RefreshAll();
        switch (stage)
        {
            case "radio": _hud.Story.Radio.Open(); break;
            case "worldmap": _hud.WorldMap.Open("outskirts"); break;
            case "briefing": _hud.Story.OpenBriefing("m2"); break;
            case "journal": _hud.Story.Journal.Open(); break;
        }
        _hud.Story.Refresh();
    }

    private void RunActsDemo(string stage)
    {
        var sim = _session.Sim;
        var campaign = sim.State.Campaign!;
        // Act 1 done, and every mission before the scene's.
        var upTo = stage switch { "offer" => "m14", "join" => "m7", _ => "m17_core" };
        foreach (var m in sim.Story!.Missions)
        {
            if (m.Id == upTo) break;
            campaign.Progress(m.Id).Status = MissionStatus.Done;
            campaign.Progress(m.Id).Outcome = "done";
        }
        foreach (var (k, v) in new (string, int)[] { ("caravan_open", 1), ("cole_met", 1), ("m6_saved", 1), ("convoy_ready", 1), ("seven_fate", 1),
                     ("iris_home", 1), ("cole_path", 1), ("cole_respect", 2), ("taken", 0), ("iris_logs", 5) })
            campaign.Flags[k] = v;
        for (var i = 1; i <= 5; i++) campaign.Flags[$"log_iris_{i}"] = 1;
        campaign.Flags["log_freight"] = 1;
        campaign.Flags["log_uplink"] = 1;
        foreach (var who in new[] { "iris", "marrow", "dee", "juniper", "cole" }) campaign.Character(who).Met = true;
        campaign.Inbox.Clear();
        sim.State.Radio.Calls.Clear();
        _session.SetSpeed(0);
        switch (stage)
        {
            case "offer":
                StoryCharacters.Join(sim, "iris");
                StoryCharacters.Join(sim, "hale");
                Dialogue.Open(sim, sim.Story.Dialogues["m14_open"]);
                // Through the greeting to Halcyon's offer itself.
                for (var i = 0; i < 60 && Dialogue.View(sim) is { } v; i++)
                {
                    if (v.Choices.Count == 0) { _session.Execute(new AdvanceDialogue()); continue; }
                    if (v.Choices.Any(c => c.Text.Contains("Sixty days"))) break;
                    _session.Execute(new ChooseDialogue(v.NodeId, v.Choices.First(c => c.Text.Contains("What do you want")).Index));
                }
                break;
            case "join":
                campaign.Progress("m7").Status = MissionStatus.Active;
                Campaign.Returned(sim, "m7", new Remnants.Core.Scavenge.ObjectiveResult("m7", Remnants.Core.Scavenge.MissionOutcome.Done,
                    new() { ["o_reach"] = Remnants.Core.Scavenge.ObjectiveStatus.Done, ["o_hold"] = Remnants.Core.Scavenge.ObjectiveStatus.Done,
                            ["o_doc"] = Remnants.Core.Scavenge.ObjectiveStatus.Done }, new(), new(), ["hale"]));
                campaign.Inbox.Clear();
                sim.State.Radio.Calls.Clear();
                break;
            case "codex":
                foreach (var who in new[] { "iris", "hale", "seven" }) StoryCharacters.Join(sim, who);
                break;
            case "epilogue":
                foreach (var who in new[] { "iris", "hale", "seven", "cole" }) StoryCharacters.Join(sim, who);
                var ending = DebugCapture.Value("--ending") is { Length: > 0 } e ? e : "terms";
                campaign.Flags["key_carrier"] = ending == "voice" ? 2 : 1;
                if (ending == "voice") campaign.Flags["uploaded"] = 2;
                if (ending == "calm") campaign.Flags["halcyon_offer"] = 1;
                Campaign.Apply(sim, new EffectDef { Kind = EffectKind.Ending, Id = ending }, "demo");
                break;
        }
        RefreshAll();
        _hud.Story.Refresh();
        switch (stage)
        {
            case "codex": _hud.Story.Codex.Show(DebugCapture.Value("--codex-tab") is { Length: > 0 } tab ? tab : "logs"); break;
            case "epilogue": _hud.Story.Epilogue.Show(int.TryParse(DebugCapture.Value("--slide"), out var n) ? n : 0); break;
        }
    }

    private void PlayOut()
    {
        var sim = _session.Sim;
        for (var i = 0; i < 100 && Dialogue.View(sim) is { } v; i++)
            _session.Execute(v.Choices.Count > 0 ? new ChooseDialogue(v.NodeId, v.Choices[0].Index) : new AdvanceDialogue());
    }
}
