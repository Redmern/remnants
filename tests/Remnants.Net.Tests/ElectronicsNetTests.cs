using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

/// <summary>Electronics over the wire: a rival's holo-decoy passes for a survivor; spent charges stay with their side.</summary>
public class ElectronicsNetTests
{
    [Fact]
    public void A_rivals_decoy_shows_as_a_survivor_and_charges_stay_private()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        var state = f.Sim.State;
        foreach (var p in state.Map.AllPositions.ToList()) state.Map[p] = Terrain.Floor;
        var alice = state.Units.First(u => u.PlayerId == a.Id);
        var bob = state.Units.First(u => u.PlayerId == b.Id);
        alice.ChargesUsed["motion_sensor_mk2"] = 2;
        bob.Pos = new Pos(alice.Pos.X + 2, alice.Pos.Y);
        var decoy = new Unit
        {
            Id = state.NextUnitId++, Name = "Alice?", Kind = UnitKind.Decoy, Side = alice.Side, PlayerId = a.Id,
            Pos = new Pos(alice.Pos.X + 1, alice.Pos.Y), Hp = 4, MaxHp = 4, CanBeDowned = false, DecoyUntilRound = state.Round + 1,
        };
        state.Units.Add(decoy);

        var bobFog = new SideFog(b.Id, bob.Side);
        bobFog.Update(f.Sim);
        var bobView = bobFog.View(f.Sim);
        var seen = bobView.Units.Single(u => u.Id == decoy.Id);
        Assert.Equal(UnitKind.Survivor, seen.Kind);
        Assert.Equal(0, seen.DecoyUntilRound);
        Assert.Empty(bobView.Units.Single(u => u.Id == alice.Id).ChargesUsed);

        var aliceFog = new SideFog(a.Id, alice.Side);
        aliceFog.Update(f.Sim);
        var aliceView = aliceFog.View(f.Sim);
        Assert.Equal(UnitKind.Decoy, aliceView.Units.Single(u => u.Id == decoy.Id).Kind);
        Assert.Equal(2, aliceView.Units.Single(u => u.Id == alice.Id).ChargesUsed["motion_sensor_mk2"]);
    }

    [Fact]
    public void A_device_given_over_the_wire_arrives_with_its_spent_charges()
    {
        var f = new NetFixture(new HostOptions { Mode = MissionMode.Coop, MinPlayers = 2, TurnSeconds = 30, ReconnectSeconds = 60 });
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        var up = f.PlayerUp(a, b)!;
        var mine = f.Sim.State.CurrentUnit!;
        var other = f.Sim.State.Units.First(u => u.PlayerControlled && u.PlayerId != up.Id);
        other.Pos = f.FreeNeighbour(mine.Pos);
        mine.Pack.Add("motion_sensor_mk2");
        mine.ChargesUsed["motion_sensor_mk2"] = 2;

        Assert.True(up.Do(new GiveItem(up.Id, mine.Id, other.Id, "motion_sensor_mk2")).Ok);
        Assert.False(mine.ChargesUsed.ContainsKey("motion_sensor_mk2"));
        Assert.Equal(2, other.ChargesUsed["motion_sensor_mk2"]);
        var mate = up == a ? b : a;
        Assert.Equal(2, mate.View.FindUnit(other.Id)!.ChargesUsed["motion_sensor_mk2"]); // the ally's own view shows the spent charges
        f.AssertNoLeaks();
    }
}
