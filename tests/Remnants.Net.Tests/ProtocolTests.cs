using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

public class ProtocolTests
{
    private static void RoundTrips(Message message)
    {
        var json = ProtocolJson.Serialize(message);
        var back = ProtocolJson.Deserialize(json);
        Assert.Equal(message.GetType(), back.GetType());
        Assert.Equal(json, ProtocolJson.Serialize(back));
    }

    [Fact]
    public void Every_command_kind_round_trips_inside_a_command_message()
    {
        IScavengeCommand[] intents =
        [
            new Move(1, 2, new Pos(3, 4)), new Attack(1, 2, 9, "pistol_shot"), new UseItem(1, 2, "med_kit", 5), new UseItem(1, 2, "bandage"),
            new PickUp(1, 2, 3, "scrap"), new Recruit(1, 2, 7), new EndTurn(1, 2), new Extract(1, 2),
            new UseItem(1, 2, "flare", At: new Pos(6, 1)), new SetStance(1, 2, Stance.Overwatch), new Lookout(1, 2),
            new ProposeAlliance(1, 2, 3), new RespondAlliance(1, 2, 3, true), new BreakAlliance(1, 2),
            new PingTile(1, 2, new Pos(1, 1), PingKind.Loot), new GiveItem(1, 2, 4, "scrap"), new Carry(1, 2, 4), new PutDown(1, 2, new Pos(2, 2)),
        ];
        foreach (var intent in intents)
        {
            var json = ProtocolJson.Serialize(new Command(42, intent));
            var back = Assert.IsType<Command>(ProtocolJson.Deserialize(json));
            Assert.Equal(42, back.RequestId);
            Assert.Equal(intent, back.Intent);
        }
    }

    [Fact]
    public void Messages_carry_a_type_discriminator_and_the_protocol_version()
    {
        var json = ProtocolJson.Serialize(new Join(Protocol.Version, "Ana", NetFixture.Party("A")));
        Assert.StartsWith("{\"type\":\"join\"", json);
        Assert.Contains($"\"version\":{Protocol.Version}", json);
        RoundTrips(new Join(Protocol.Version, "Ana", NetFixture.Party("A"), "token"));
    }

    [Fact]
    public void Lobby_turn_and_notice_messages_round_trip()
    {
        RoundTrips(new Ready(true));
        RoundTrips(new Leave());
        RoundTrips(new Welcome(1, 2, "abc"));
        RoundTrips(new JoinRefused("full"));
        RoundTrips(new LobbyState("outskirts", 2, 4, [new LobbyPlayer(1, "Ana", 2, true, true), new LobbyPlayer(2, "Bo", 3, false, false)]));
        RoundTrips(new CommandAccepted(3, "ok"));
        RoundTrips(new CommandRejected(3, ScavengeError.NotYourTurn, "wait"));
        RoundTrips(new TurnChanged(4, null, 2, 12.5));
        RoundTrips(new PlayerLeft(2, "Bo", 60));
        RoundTrips(new Notice("hi"));
    }

    [Fact]
    public void Co_op_lobby_and_alliance_messages_round_trip()
    {
        RoundTrips(new SetTeam(2));
        RoundTrips(new SetTeam(null));
        RoundTrips(new LobbyState("outskirts", 2, 4, [new LobbyPlayer(1, "Ana", 2, true, true, 1)], MissionMode.Coop));
        RoundTrips(new AllianceProposed(1, 2, 5));
        RoundTrips(new AllianceAccepted([1, 2]));
        RoundTrips(new AllianceDeclined(1, 2, Lapsed: true));
        RoundTrips(new AllianceBreakWarning(2, 7));
        RoundTrips(new AllianceBroken(2, [1]));
        Assert.Contains("\"type\":\"allianceProposed\"", ProtocolJson.Serialize(new AllianceProposed(1, 2, 5)));
        Assert.Contains("\"kind\":\"ping\"", ProtocolJson.Serialize(new Command(1, new PingTile(1, 2, new Pos(1, 1), PingKind.Danger))));
    }

    [Fact]
    public void A_co_op_view_with_pings_and_alliances_round_trips()
    {
        var sim = NewScavenge.Create(NetFixture.Data, "outskirts", 3,
            [new PartySetup(1, NetFixture.Party("A"), Team: 1, Name: "Ana"), new PartySetup(2, NetFixture.Party("B"), Team: 1, Name: "Bo")]);
        var up = sim.State.CurrentUnit!;
        Assert.True(sim.Execute(new PingTile(up.PlayerId!.Value, up.Id, new Pos(2, 2), PingKind.Danger)).Ok);
        Assert.True(sim.Execute(new BreakAlliance(up.PlayerId!.Value, up.Id)).Ok);
        var fog = new SideFog(1, Unit.PlayerSide(1));
        fog.Update(sim);
        Assert.StartsWith("team:", fog.Side);
        var view = fog.View(sim);
        Assert.Single(view.Pings);
        Assert.Single(view.Breaks);
        Assert.Equal(["Ana", "Bo"], view.Parties.Select(p => p.Name)); // log lines and labels use them
        RoundTrips(new Snapshot(view, fog.Visible.Order().ToList(), []));
        RoundTrips(new MissionStarted(sim.State.ZoneId, sim.State.Map, [new PlayerInfo(1, "A", 1), new PlayerInfo(2, "B", 1)], 60, 120, MissionMode.Coop));
    }

    [Fact]
    public void A_whole_mission_start_snapshot_and_result_round_trip()
    {
        var sim = NewScavenge.Create(NetFixture.Data, "suburb_ruins", 7,
            [new PartySetup(1, NetFixture.Party("A")), new PartySetup(2, NetFixture.Party("B"))]);
        RoundTrips(new MissionStarted(sim.State.ZoneId, sim.State.Map, [new PlayerInfo(1, "A"), new PlayerInfo(2, "B")], 60, 120));

        var fog = new SideFog(1, Unit.PlayerSide(1));
        fog.Update(sim);
        RoundTrips(new Snapshot(fog.View(sim), fog.Visible.Order().ToList(), sim.State.Events.ToList()));
        RoundTrips(new MissionEnded(sim.ResultFor(1), "done"));
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ProtocolJson.Deserialize("{\"type\":\"nope\"}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ProtocolJson.Deserialize("null"));
    }
}
