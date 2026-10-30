using System.Linq;
using Remnants.Core.Colony;
using Remnants.Core.Story;

namespace Remnants.Game.Colony;

/// <summary>
/// Debug stagings of Story mode for screenshots (<c>--story-demo=radio|dialogue|worldmap|briefing|journal</c>): a
/// fresh Story colony plays M1 through Core commands until First Light calls, then stops on the asked-for screen.
/// </summary>
public partial class ColonyScreen
{
    private void RunStoryDemo(string stage)
    {
        var sim = _session.Sim;
        if (sim.State.Campaign is null) Campaign.Start(sim);
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

    private void PlayOut()
    {
        var sim = _session.Sim;
        for (var i = 0; i < 100 && Dialogue.View(sim) is { } v; i++)
            _session.Execute(v.Choices.Count > 0 ? new ChooseDialogue(v.NodeId, v.Choices[0].Index) : new AdvanceDialogue());
    }
}
