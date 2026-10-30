using Remnants.Core.Scavenge;
using Remnants.Core.Story;

namespace Remnants.Net.Tests;

/// <summary>
/// Co-op story v1 over the wire (STORY.md "Co-op"): the host's mission with one guest. The first seat is the host;
/// the guest's party starts at the map's second spawn point, sees the objectives and chatter as an ally but cannot
/// answer the host's choices, carries no campaign progress home, and copies the mission's schematics when it is done.
/// A rival who is not on the host's team never learns the mission.
/// </summary>
public class StoryCoopNetTests
{
    private static readonly Lazy<StoryData> Story =
        new(() => StoryDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "story")));

    private static HostOptions StoryHost(string missionId, MissionMode mode = MissionMode.Coop, int players = 2) => new()
    {
        ZoneId = Story.Value.Mission(missionId)!.Zone!, Mode = mode, MinPlayers = players, TurnSeconds = 30, ReconnectSeconds = 60,
        Mission = Campaign.CoopSetup(Story.Value, missionId),
    };

    [Fact]
    public void The_host_plays_their_mission_with_a_guest_who_sees_it_as_an_ally_and_copies_its_schematics()
    {
        var f = new NetFixture(StoryHost("m7"));
        var (a, b) = f.StartTwo();
        var run = f.Sim.State.Mission!;
        Assert.Equal("m7_drowned_pharmacy", run.MapId);
        Assert.Equal(a.Id, MissionRules.HostPlayerId(f.Sim.State));
        // The guest starts at the second spawn point, next to the host's party (one team).
        var spawns = run.Marker(HandmadeMaps.SpawnMarker);
        Assert.Contains(f.Sim.State.Units, u => u.PlayerId == b.Id && u.Pos.Chebyshev(spawns[^1]) <= 2);
        Assert.True(f.Sim.State.Allied(a.Id, b.Id));

        // Both see the script; neither sees the host's campaign flags.
        Assert.NotNull(a.View.Mission);
        Assert.NotNull(b.View.Mission);
        Assert.Equal(run.Def.Objectives.Count, b.View.Mission!.Objectives.Count);
        Assert.Empty(b.View.Mission.Campaign);

        // The host's choice is the host's.
        run.Pending = new PromptDef { Id = "p", Speaker = "Doc", Text = "?", Options = [new PromptOption("hold", "Hold")] };
        var guestUnit = f.Sim.State.Units.First(u => u.PlayerId == b.Id && u.PlayerControlled);
        Assert.False(f.Sim.Execute(new ChooseOption(b.Id, guestUnit.Id, "p", "hold")).Ok);
        Assert.NotNull(run.Pending);
        run.Pending = null;

        // The mission is done and everyone is out: the guest copies the schematics and nothing else.
        foreach (var o in run.Objectives) o.Status = ObjectiveStatus.Done;
        run.Flags["doc_state"] = 1;
        foreach (var u in f.Sim.State.Units.Where(u => u.PlayerId is { } p && (p == a.Id || p == b.Id))) u.Status = UnitStatus.Extracted;
        f.Sim.UpdateParties();
        var host = f.Sim.ResultFor(a.Id).Mission!;
        var guest = f.Sim.ResultFor(b.Id).Mission!;
        Assert.Equal(MissionOutcome.Done, host.Outcome);
        Assert.Equal(1, host.Flags["doc_state"]);
        Assert.Equal(MissionOutcome.Incomplete, guest.Outcome);
        Assert.Empty(guest.Flags);
        Assert.Equal(["sch_mech_limbs", "sch_powered_limbs"], guest.GuestSchematics);
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_rival_never_learns_the_hosts_mission()
    {
        var f = new NetFixture(StoryHost("m9", MissionMode.Versus));
        var (a, b) = f.StartTwo();
        Assert.NotNull(a.View.Mission);
        Assert.Null(b.View.Mission);
        Assert.Equal(MissionOutcome.Incomplete, f.Sim.ResultFor(b.Id).Mission!.Outcome);
        f.AssertNoLeaks();
    }
}
