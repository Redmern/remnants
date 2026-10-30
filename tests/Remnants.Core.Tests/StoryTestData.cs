using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Story;

namespace Remnants.Core.Tests;

/// <summary>The real data/story folder, and a fresh Story colony on it.</summary>
public static class StoryTestData
{
    private static readonly Lazy<StoryData> RealData =
        new(() => StoryDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "story")));

    public static StoryData Real => RealData.Value;

    public static ColonySim NewStory(ulong seed = 7, bool nights = false)
    {
        var sim = NewGame.Create(nights ? TestData.RealWithNights : TestData.Real, seed, ScavengeTestData.Real);
        sim.Story = Real;
        Campaign.Start(sim);
        return sim;
    }

    /// <summary>Ticks until a call is waiting (or the limit), then plays it through, picking the first choice each time.</summary>
    public static DialogueDef? TakeNextCall(ColonySim sim, int maxTicks = 200)
    {
        for (var i = 0; i < maxTicks && !Campaign.Waiting(sim).Any(); i++) sim.Tick();
        if (Campaign.Waiting(sim).FirstOrDefault() is not { } call) return null;
        Assert.True(sim.Execute(new TakeCall(call.Id)).Ok);
        PlayOut(sim);
        return Real.Dialogues[call.DialogueId];
    }

    public static void PlayOut(ColonySim sim, int choice = 0)
    {
        for (var steps = 0; Dialogue.View(sim) is { } view && steps < 200; steps++)
        {
            var result = view.Choices.Count > 0
                ? sim.Execute(new ChooseDialogue(view.NodeId, view.Choices[Math.Min(choice, view.Choices.Count - 1)].Index))
                : sim.Execute(new AdvanceDialogue());
            Assert.True(result.Ok, result.Message);
        }
        Assert.Null(Dialogue.View(sim));
    }

    /// <summary>A small handmade field mission for objective and trigger tests: party on S, anything else from the legend.</summary>
    public static ScavengeSim Field(string[] rows, Dictionary<string, string>? markers, List<ObjectiveDef> objectives,
        List<TriggerDef>? triggers = null, Action<HandmadeMapDef>? _ = null, List<MapEnemyDef>? enemies = null,
        List<MapNpcDef>? npcs = null, List<MapStructureDef>? structures = null, List<MapDeviceDef>? devices = null,
        bool atHome = false, int members = 1, string weapon = "pipe", List<MapCacheDef>? caches = null)
    {
        var map = new HandmadeMapDef
        {
            Id = "test", Zone = "outskirts", Rows = rows.ToList(), Markers = markers ?? new(),
            Enemies = enemies ?? [], Npcs = npcs ?? [], Structures = structures ?? [], Devices = devices ?? [], Caches = caches ?? [],
        };
        var mission = new FieldMission { Id = "test", Name = "Test", Objectives = objectives, Triggers = triggers ?? [], AtHome = atHome };
        return NewScavenge.Create(ScavengeTestData.Real, "outskirts", 1, [ScavengeTestData.Party(1, members, weapon)], null,
            new MissionSetup(mission, map));
    }

    public static Unit Mine(this ScavengeSim sim) => sim.State.Units.First(u => u.PlayerId == 1 && u.Kind == UnitKind.Survivor);

    public static ObjectiveStatus Status(this ScavengeSim sim, string id) => sim.State.Mission!.Find(id)!.Status;

    /// <summary>Ends turns until round <paramref name="round"/> starts (or the run finishes).</summary>
    public static void RunTo(this ScavengeSim sim, int round)
    {
        for (var i = 0; i < 500 && !sim.State.Finished && sim.State.Round < round; i++)
        {
            var u = sim.State.CurrentUnit!;
            Assert.True(sim.Execute(new EndTurn(u.PlayerId!.Value, u.Id)).Ok);
        }
    }
}
