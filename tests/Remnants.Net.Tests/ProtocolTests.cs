using Remnants.Core.Scavenge;
using Remnants.Core.Trade;

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
    public void The_version_is_the_highest_keyed_feature_version()
    {
        var keyed = typeof(Protocol).GetFields().Where(f => f.IsLiteral && f.Name.EndsWith("Version") && f.Name != nameof(Protocol.Version))
            .Select(f => (int)f.GetRawConstantValue()!).ToList();
        Assert.NotEmpty(keyed);
        Assert.Equal(Protocol.Version, keyed.Max());
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
            new Hack(1, 2, 7), new Hack(1, 2, TargetId: 5), new DeployDrone(1, 2, new Pos(3, 3)), new FlyDrone(1, 2, new Pos(6, 6)),
            new RecallDrone(1, 2), new MarkTarget(1, 2, 9), new DropPayload(1, 2, "flare"),
            new DroneHeal(1, 2, 4), new DroneLoad(1, 2, 3, "rifle"), new DroneFerry(1, 2), new DroneFire(1, 2, 9),
            new UseItem(1, 2, "emp_grenade", At: new Pos(4, 4)),
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
    public void A_planned_action_round_trips_under_its_wire_name()
    {
        var plan = new DeclareIntent(1, 2, [new Pos(3, 4), new Pos(4, 4)], new Pos(5, 4), "swing");
        var json = ProtocolJson.Serialize(new Command(7, plan));
        Assert.Contains("\"kind\":\"plannedAction\"", json);
        var back = Assert.IsType<DeclareIntent>(Assert.IsType<Command>(ProtocolJson.Deserialize(json)).Intent);
        Assert.Equal(plan.Path, back.Path);
        Assert.Equal((plan.AttackTile, plan.ActionId), (back.AttackTile, back.ActionId));
        Assert.Equal(json, ProtocolJson.Serialize(new Command(7, back)));

        var pin = new DeclareIntent(1, 2, [], new Pos(6, 4), "burst", Suppress: true);
        var pinBack = Assert.IsType<DeclareIntent>(Assert.IsType<Command>(ProtocolJson.Deserialize(ProtocolJson.Serialize(new Command(9, pin)))).Intent);
        Assert.True(pinBack.Suppress);
        var moveOnly = ProtocolJson.Serialize(new Command(8, new DeclareIntent(1, 2, [new Pos(1, 1)])));
        Assert.IsType<DeclareIntent>(Assert.IsType<Command>(ProtocolJson.Deserialize(moveOnly)).Intent);
        Assert.Equal(17, Protocol.TacticsVersion);
        Assert.Equal(Protocol.TacticsVersion, Protocol.Version);
    }

    [Fact]
    public void Card_commands_and_a_join_with_a_deck_round_trip()
    {
        foreach (var (command, kind) in new (IScavengeCommand, string)[]
                 {
                     (new PlayCard(1, 2, "dash"), "playCard"), (new PlayCard(1, 2, "counterstrike", 9, Boost: true, FromSlot: true), "playCard"),
                     (new DiscardDeadCard(1, 2, "sprint"), "discardDeadCard"),
                 })
        {
            var json = ProtocolJson.Serialize(new Command(3, command));
            Assert.Contains($"\"kind\":\"{kind}\"", json);
            Assert.Equal(command, Assert.IsType<Command>(ProtocolJson.Deserialize(json)).Intent);
            Assert.DoesNotContain("\"tile\"", json); // a card goes on a character, never a tile
        }
        RoundTrips(new Join(Protocol.Version, "Ana", NetFixture.Party("A"), Deck: ["dash", "dash", "take_cover"]));
        Assert.Equal(17, Protocol.Version);
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
        RoundTrips(new LobbyState("core_spire", 2, 4, [new LobbyPlayer(1, "Ana", 2, true, true)], LateZones: LateZoneTuning.Everywhere));
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

    [Fact]
    public void Trade_and_market_messages_round_trip()
    {
        List<TradeGood> goods = [TradeGood.Resource("scrap", 20), TradeGood.Item("pistol") with { Extras = new ItemExtras(["scope"], 2) }];
        var view = new TradeView(3, TradeVenue.Lobby, true, [new TradeSideView(1, "Ana", goods, true, false), new TradeSideView(2, "Bo", [], false, false)], 42);
        Message[] messages =
        [
            new TradeRequest(2), new TradeRespond(3, true), new TradeOffer(3, goods), new TradeLock(3, true), new TradeConfirm(3), new TradeCancel(3),
            new TradeInvite(3, 1, "Ana", TradeVenue.Field), new TradeUpdate(view), new TradeCommit(3, "live-x", TradeVenue.Lobby, 2, "Bo", goods, [], 2),
            new TradeCancelled(3, "timed out"),
            new MarketHello(Protocol.Version, "Ana", new string('a', 64)), new MarketPost(1, "tx", goods, [TradeGood.Resource("wood", 5)], 24),
            new MarketList(2, new MarketFilter("rifle", "bp_rifle", null, true)), new MarketAccept(3, "tx2", 7), new MarketCancel(4, 7),
            new MarketClaim(5), new MarketClaimAck(["claim-1"]), new MarketWelcome(Protocol.Version, "abcd", "Ana", 72, 8),
            new MarketListings(2, [new MarketListing(7, "Ana", goods, goods, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), false)], 1),
            new MarketResult(3, false, "gone", null, "gone"), new MarketClaims([new MarketClaimView("claim-1", goods, "sold")]),
        ];
        foreach (var m in messages) RoundTrips(m);
    }
}
