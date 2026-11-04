using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

/// <summary>
/// Hacking and drones over the wire: devices reach a client only once its side has seen them, never with their contents
/// or another side's hack, a hacked camera or turret works for the whole team and a rival only learns of it by seeing it,
/// drones stay hidden from rivals until seen, and drone sight is shared with allies. Every message is checked by
/// <see cref="NetFixture"/>.
/// </summary>
public class HackNetTests
{
    private static readonly ScavengeData Sure = new(NetFixture.Data.Items.Values.Where(i => i.BaseId is null), NetFixture.Data.Enemies.Values,
        NetFixture.Data.Zones.Values, NetFixture.Data.Rules with { Hacking = NetFixture.Data.Rules.Hacking with { MinChance = 1, MaxChance = 1 } },
        NetFixture.Data.PerkHooks);

    /// <summary>
    /// Two players on an open 30×30 board (walls flattened, the AI and the zone's own devices gone), Alice up with one unit at
    /// (2, 2) and one at (2, 3); Bob's pair far off at (25, 25) and (25, 26).
    /// </summary>
    private static (NetFixture F, TestPlayer A, TestPlayer B, Unit Alice, Unit Bob) Stage(MissionMode mode)
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0, Mode = mode, ZoneId = "rail_depot" }, Sure);
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a, a, b);
        var state = f.Sim.State;
        foreach (var p in state.Map.AllPositions.ToList()) state.Map[p] = Terrain.Floor;
        foreach (var u in state.Units.Where(u => u.PlayerId is null)) u.Status = UnitStatus.Dead;
        state.Devices.Clear();
        state.Caches.Clear();
        var alice = state.CurrentUnit!;
        var other = state.Units.First(u => u.PlayerId == a.Id && u.Id != alice.Id);
        var bobs = state.Units.Where(u => u.PlayerId == b.Id).ToList();
        alice.Pos = new(2, 2);
        other.Pos = new(2, 3);
        bobs[0].Pos = new(25, 25);
        bobs[1].Pos = new(25, 26);
        alice.Ap = f.Sim.Profile(alice).MaxAp;
        return (f, a, b, alice, bobs[0]);
    }

    private static Unit Husk(NetFixture f, Pos at)
    {
        var husk = NewScavenge.EnemyUnit(f.Sim.Data, f.Sim.State, f.Sim.Data.Enemies["husk"], at);
        f.Sim.State.Units.Add(husk);
        return husk;
    }

    [Fact]
    public void Devices_reach_only_sides_that_saw_them_and_never_with_their_insides()
    {
        var (f, a, b, alice, _) = Stage(MissionMode.Versus);
        alice.Pack.Add("hacking_kit");
        var near = Hacking.Place(f.Sim.Data, f.Sim.State, DeviceKind.Relay, new Pos(3, 2));
        var locker = Hacking.Place(f.Sim.Data, f.Sim.State, DeviceKind.Locker, new Pos(4, 4), ["med_kit"]);
        Assert.True(a.Do(new Hack(a.Id, alice.Id, near.Id)).Ok);

        var mine = a.View.Devices.Single(d => d.Id == near.Id);
        Assert.Equal(1, Hacking.ProgressOf(mine, alice.Side)!.Done);
        Assert.Empty(a.View.Devices.Single(d => d.Id == locker.Id).Items);
        Assert.DoesNotContain(b.View.Devices, d => d.Id == near.Id || d.Id == locker.Id);
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_rival_sees_a_hacked_device_only_as_last_seen_and_as_the_owners_side()
    {
        var (f, a, b, alice, bob) = Stage(MissionMode.Versus);
        alice.Pack.Add("hacking_kit");
        var door = Hacking.Place(f.Sim.Data, f.Sim.State, DeviceKind.Door, new Pos(3, 2));
        // Bob saw the door while it was the Network's...
        bob.Pos = new Pos(6, 2);
        Assert.True(a.Do(new EndTurn(a.Id, alice.Id)).Ok);
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a && f.Sim.State.CurrentUnitId == alice.Id, a, b);
        Assert.Equal(Unit.EnemySide, b.View.Devices.Single(d => d.Id == door.Id).Controller);
        // ...then walks off, and Alice hacks it unseen.
        bob.Pos = new Pos(25, 25);
        door.Progress.Add(new HackProgress(alice.Side, alice.Id, 1, 2));
        Assert.True(a.Do(new Hack(a.Id, alice.Id, door.Id)).Ok);
        Assert.Equal(alice.Side, door.Controller);
        Assert.Equal(alice.Side, a.View.Devices.Single(d => d.Id == door.Id).Controller);
        Assert.Equal(Unit.EnemySide, b.View.Devices.Single(d => d.Id == door.Id).Controller);
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_hacked_camera_sees_for_the_whole_team()
    {
        var (f, a, b, alice, bob) = Stage(MissionMode.Coop);
        alice.Pack.Add("hacking_kit");
        var state = f.Sim.State;
        // A wall between the team and the room the camera watches.
        for (var y = 0; y < 12; y++) state.Map[new Pos(6, y)] = Terrain.Wall;
        var camera = Hacking.Place(f.Sim.Data, state, DeviceKind.Camera, new Pos(6, 2));
        var husk = Husk(f, new Pos(10, 2));
        bob.Pos = new Pos(2, 6);
        alice.Pos = new Pos(5, 2);
        Assert.True(a.Do(new Hack(a.Id, alice.Id, camera.Id)).Ok);

        Assert.Equal(alice.Side, state.FindUnit(camera.UnitId!.Value)!.Side);
        Assert.Contains(b.View.Units, u => u.Id == husk.Id);
        Assert.Contains(b.View.Units, u => u.Id == camera.UnitId);
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_drone_stays_hidden_from_a_rival_until_seen()
    {
        var (f, a, b, alice, bob) = Stage(MissionMode.Versus);
        alice.Pack.Add("scout_drone");
        bob.Pos = new Pos(9, 2);
        f.Sim.State.Units.First(u => u.PlayerId == b.Id && u.Id != bob.Id).Pos = new Pos(29, 29);
        foreach (var u in f.Sim.State.Units.Where(u => u.PlayerId == b.Id)) u.Attributes[Remnants.Core.Survivors.AttributeKind.Perception] = 20;
        alice.Pos = new Pos(2, 20);
        f.Sim.State.Units.First(u => u.PlayerId == a.Id && u.Id != alice.Id).Pos = new Pos(2, 22);
        Assert.True(a.Do(new DeployDrone(a.Id, alice.Id, new Pos(2, 19))).Ok);
        var drone = f.Sim.State.FindUnit(alice.DroneId!.Value)!;
        Assert.DoesNotContain(b.View.Units, u => u.Id == drone.Id);
        Assert.Contains(a.View.Units, u => u.Id == drone.Id && u.Kind == UnitKind.Drone);

        // It flies up to Bob: now he sees it, but not who flies it.
        alice.Ap = 2;
        Assert.True(a.Do(new FlyDrone(a.Id, alice.Id, new Pos(5, 16))).Ok);
        drone.Pos = new Pos(9, 3);
        Assert.True(a.Do(new EndTurn(a.Id, alice.Id)).Ok);
        var seen = b.View.Units.SingleOrDefault(u => u.Id == drone.Id);
        Assert.NotNull(seen);
        Assert.Null(seen!.OperatorId);
        f.AssertNoLeaks();
    }

    [Fact]
    public void Drone_sight_is_shared_with_allies()
    {
        var (f, a, b, alice, bob) = Stage(MissionMode.Coop);
        alice.Pack.Add("scout_drone");
        bob.Pos = new Pos(2, 6);
        var husk = Husk(f, new Pos(16, 2));
        Assert.True(a.Do(new DeployDrone(a.Id, alice.Id, new Pos(3, 2))).Ok);
        f.Sim.State.FindUnit(alice.DroneId!.Value)!.Pos = new Pos(8, 2);
        Assert.True(a.Do(new EndTurn(a.Id, alice.Id)).Ok);
        Assert.Contains(b.View.Units, u => u.Id == husk.Id);
        Assert.Contains(b.View.Units, u => u.Id == alice.DroneId);
        f.AssertNoLeaks();
    }

    [Fact]
    public void Perks_reach_the_server_with_the_party_and_stay_private()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var party = NetFixture.Party("A");
        party[0].Survivor.Perks.UnionWith(["hacking_1", "spotter"]);
        var a = f.Join("Alice", party);
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        var unit = f.Sim.State.Units.First(u => u.PlayerId == a.Id && u.Origin!.Perks.Contains("hacking_1"));
        Assert.Equal(0.1, unit.Hook(Remnants.Core.Colony.PerkHooks.HackChanceBonus), 6);
        Assert.Equal(1, unit.Hook(Remnants.Core.Colony.PerkHooks.DroneCanMark), 6);
        Assert.Equal(0.1, a.View.FindUnit(unit.Id)!.Hook(Remnants.Core.Colony.PerkHooks.HackChanceBonus), 6);
        f.AssertNoLeaks();
    }
}
