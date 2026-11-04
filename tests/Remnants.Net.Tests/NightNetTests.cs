using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Net.Tests;

/// <summary>
/// Night in multiplayer: the server decides the hour, every client gets the same environment, and the fog the server
/// filters by follows the dark (a party seen by day is not sent at night), with nothing leaking.
/// </summary>
public class NightNetTests
{
    /// <summary>Alice and Bob on flattened ground with the AI removed, Alice's pair 5 tiles from Bob's.</summary>
    private static (NetFixture F, TestPlayer A, TestPlayer B, Unit Alice, Unit Bob) Standoff(int hour)
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0, Hour = hour });
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a, a, b);
        var state = f.Sim.State;
        foreach (var p in state.Map.AllPositions.ToList()) state.Map[p] = Terrain.Floor;
        foreach (var e in state.Units.Where(u => u.Kind is UnitKind.Enemy or UnitKind.Npc or UnitKind.Device)) e.Status = UnitStatus.Dead;
        var alices = state.Units.Where(u => u.PlayerId == a.Id).ToList();
        var bobs = state.Units.Where(u => u.PlayerId == b.Id).ToList();
        alices[0].Pos = new(8, 10);
        alices[1].Pos = new(8, 11);
        bobs[0].Pos = new(3, 10);
        bobs[1].Pos = new(3, 11);
        return (f, a, b, alices[0], bobs[0]);
    }

    [Fact]
    public void The_server_decides_the_hour_and_every_client_gets_it()
    {
        var (f, a, b, _, _) = Standoff(23);
        Assert.Equal(DayPhase.Night, f.Sim.State.Environment.Phase);
        Assert.Equal(23, f.Sim.State.Environment.Hour);
        // Any intent refreshes the views.
        Assert.True(a.Do(new EndTurn(a.Id, f.Sim.State.CurrentUnit!.Id)).Ok);
        foreach (var p in new[] { a, b })
        {
            Assert.Equal(DayPhase.Night, p.View.Environment.Phase);
            Assert.Equal(23, p.View.Environment.Hour);
        }
        f.AssertNoLeaks();
    }

    [Fact]
    public void Without_an_hour_the_server_picks_one_from_the_seed()
    {
        var options = new HostOptions { Seed = 45 };
        Assert.Equal(45 % 24, options.MissionHour);
        Assert.Equal(12, new HostOptions { Hour = 12, Seed = 45 }.MissionHour);
    }

    [Fact]
    public void A_party_seen_by_day_is_not_sent_at_night()
    {
        var (day, da, db, dayAlice, _) = Standoff(12);
        Assert.True(da.Do(new EndTurn(da.Id, day.Sim.State.CurrentUnit!.Id)).Ok);
        Assert.True(Detection.SideSees(day.Sim.Data, day.Sim.State, Unit.PlayerSide(db.Id), dayAlice));
        Assert.Contains(db.View.Units, u => u.Id == dayAlice.Id);
        day.AssertNoLeaks();

        var (night, na, nb, nightAlice, nightBob) = Standoff(23);
        Assert.True(na.Do(new EndTurn(na.Id, night.Sim.State.CurrentUnit!.Id)).Ok);
        Assert.False(Detection.SideSees(night.Sim.Data, night.Sim.State, Unit.PlayerSide(nb.Id), nightAlice));
        Assert.DoesNotContain(nb.View.Units, u => u.PlayerId == na.Id);
        Assert.DoesNotContain(na.View.Units, u => u.PlayerId == nb.Id);
        // Nor are the tiles they stand on in Bob's sight.
        Assert.DoesNotContain(night.Sim.State.Map.Index(nightAlice.Pos), Visibility.SideVisible(night.Sim.Data, night.Sim.State, Unit.PlayerSide(nb.Id)));
        Assert.Contains(night.Sim.State.Map.Index(nightBob.Pos), Visibility.SideVisible(night.Sim.Data, night.Sim.State, Unit.PlayerSide(nb.Id)));
        night.AssertNoLeaks();
    }

    [Fact]
    public void A_flare_in_the_dark_shows_both_sides_the_same_lit_ground()
    {
        var (f, a, b, alice, _) = Standoff(23);
        alice.Pack.Add("flare");
        Assert.True(a.Do(new UseItem(a.Id, alice.Id, "flare", At: new Pos(5, 10))).Ok);
        var flare = Assert.Single(f.Sim.State.Flares);
        Assert.Equal(flare.Radius, Assert.Single(a.View.Flares).Radius);
        Assert.Equal(flare.Radius, Assert.Single(b.View.Flares).Radius);
        // The lit ground shows whoever stands on it, to every side.
        Assert.True(Detection.IsLit(f.Sim.State, alice.Pos));
        Assert.Contains(b.View.Units, u => u.Id == alice.Id);
        f.AssertNoLeaks();
    }
}
