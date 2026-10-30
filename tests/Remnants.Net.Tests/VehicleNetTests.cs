using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

/// <summary>Each player brings their own vehicle; the server checks its seats and keeps one party's stash from the others.</summary>
public class VehicleNetTests
{
    private static FieldVehicle Buggy(int? seats = 4, double cargo = 80) => new("buggy", "Buggy", 3, 60, 60, 1, cargo, false, seats);

    [Fact]
    public void Each_player_parks_their_own_vehicle_and_only_their_side_sees_its_stash()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var a = f.Join("Alice", vehicle: Buggy());
        var b = f.Join("Bob", vehicle: Buggy() with { Name = "Bob's Buggy" });
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        Assert.Equal(HostPhase.Playing, f.Host.Phase);
        Assert.Equal(2, f.Sim.State.Vehicles.Count);
        Assert.Equal([a.Id, b.Id], f.Sim.State.Vehicles.Select(v => v.PlayerId).OrderBy(id => id));

        var mine = f.Sim.State.Vehicles.Single(v => v.PlayerId == a.Id);
        var theirs = f.Sim.State.Vehicles.Single(v => v.PlayerId == b.Id);
        mine.Stash.Add("bandage");
        theirs.Stash.Add("bandage");
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a, a, b);
        // Bob has explored Alice's corner: he may know her vehicle is there, never what is in it.
        f.Sim.State.Explored[Unit.PlayerSide(b.Id)].Add(f.Sim.State.Map.Index(mine.Pos));
        Assert.True(a.Do(new EndTurn(a.Id, f.Sim.State.CurrentUnit!.Id)).Ok);

        Assert.Equal(["bandage"], a.View.Vehicles.Single(v => v.PlayerId == a.Id).Stash);
        Assert.Empty(b.View.Vehicles.Single(v => v.PlayerId == a.Id).Stash);
        f.AssertNoLeaks();
    }

    [Fact]
    public void The_server_refuses_more_survivors_than_seats_and_impossible_vehicles()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var a = f.Join("Alice", vehicle: Buggy(seats: 1));
        Assert.Contains("seats", a.Client.RefusedReason);
        var b = f.Join("Bob", vehicle: Buggy(cargo: 5000));
        Assert.NotNull(b.Client.RefusedReason);
    }

    [Fact]
    public void The_stash_command_goes_over_the_wire()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var a = f.Join("Alice", vehicle: Buggy());
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a, a, b);
        var unit = f.Sim.State.CurrentUnit!;
        var parked = f.Sim.State.Vehicles.Single();
        unit.Pos = f.Sim.State.Map.AllPositions.First(p => p.Chebyshev(parked.Pos) == 1 && f.Sim.Data.Terrain(f.Sim.State.Map[p]).Passable
                                                         && f.Sim.State.UnitAt(p) is null);
        var result = a.Do(new StashItem(a.Id, unit.Id, "bandage"));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(["bandage"], parked.Stash);
    }
}
