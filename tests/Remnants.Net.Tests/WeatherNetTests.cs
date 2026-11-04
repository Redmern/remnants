using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

/// <summary>
/// Weather in multiplayer: the server decides it (set, or rolled from the seed), every client gets the same sky and its
/// changes, thick weather hides what clear weather shows without anything leaking, and lightning shows both sides the
/// same thing for exactly its round.
/// </summary>
public class WeatherNetTests
{
    /// <summary>Alice and Bob on flattened ground with the AI removed, Alice's pair 5 tiles from Bob's, at midday.</summary>
    private static (NetFixture F, TestPlayer A, TestPlayer B, Unit Alice, Unit Bob) Standoff(string weather, List<WeatherShift>? shifts = null)
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0, Hour = 12, Weather = weather, WeatherIntensity = 0.9, WeatherShifts = shifts ?? new() });
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
        alices[0].Pos = new(10, 10);
        alices[1].Pos = new(10, 11);
        bobs[0].Pos = new(3, 10);
        bobs[1].Pos = new(3, 11);
        return (f, a, b, alices[0], bobs[0]);
    }

    /// <summary>Any intent refreshes every view.</summary>
    private static void Refresh(NetFixture f, TestPlayer a)
    {
        var unit = f.Sim.State.CurrentUnit!;
        (unit.Lookout, unit.Ap, unit.Fatigue) = (false, 2, 0);
        Assert.True(a.Do(new Lookout(a.Id, unit.Id)).Ok);
    }

    [Fact]
    public void The_server_decides_the_weather_and_every_client_gets_it()
    {
        var (f, a, b, _, _) = Standoff("fog");
        Assert.Equal("fog", f.Sim.State.Environment.Weather);
        Refresh(f, a);
        foreach (var p in new[] { a, b })
        {
            Assert.Equal("fog", p.View.Environment.Weather);
            Assert.Equal(0.9, p.View.Environment.Intensity);
        }
        f.AssertNoLeaks();
    }

    [Fact]
    public void Without_a_weather_the_server_rolls_one_from_the_seed()
    {
        var data = NetFixture.Data;
        Assert.Equal(FieldEnvironment.RollWeather(data, 45), FieldEnvironment.RollWeather(data, 45));
        Assert.Equal(FieldEnvironment.RollWeather(data, 45).Weather, new HostOptions { Seed = 45 }.MissionEnvironment(data).Weather);
        var rolled = Enumerable.Range(1, 60).Select(s => FieldEnvironment.RollWeather(data, (ulong)s).Weather).ToHashSet();
        Assert.Contains("clear", rolled);
        Assert.Contains("dust_storm", rolled);
        Assert.Equal("rain", new HostOptions { Seed = 45, Weather = "rain" }.MissionEnvironment(data).Weather);
    }

    [Fact]
    public void A_party_seen_in_clear_weather_is_not_sent_in_a_dust_storm()
    {
        var (clear, ca, cb, clearAlice, _) = Standoff("clear");
        Refresh(clear, ca);
        Assert.Contains(cb.View.Units, u => u.Id == clearAlice.Id);
        clear.AssertNoLeaks();

        var (dust, da, db, dustAlice, _) = Standoff("dust_storm");
        Refresh(dust, da);
        Assert.False(Detection.SideSees(dust.Sim.Data, dust.Sim.State, Unit.PlayerSide(db.Id), dustAlice));
        Assert.DoesNotContain(db.View.Units, u => u.PlayerId == da.Id);
        Assert.DoesNotContain(da.View.Units, u => u.PlayerId == db.Id);
        Assert.DoesNotContain(dust.Sim.State.Map.Index(dustAlice.Pos), Visibility.SideVisible(dust.Sim.Data, dust.Sim.State, Unit.PlayerSide(db.Id)));
        dust.AssertNoLeaks();
    }

    [Fact]
    public void Lightning_shows_both_sides_the_same_thing_for_its_round_only()
    {
        var (f, a, b, alice, bob) = Standoff("dust_storm");
        var state = f.Sim.State;
        state.Environment.LightningRound = state.Round;
        Refresh(f, a);
        Assert.Contains(b.View.Units, u => u.Id == alice.Id);
        Assert.Contains(a.View.Units, u => u.Id == bob.Id);
        Assert.Equal(state.Round, a.View.Environment.LightningRound);
        Assert.Equal(state.Round, b.View.Environment.LightningRound);
        f.AssertNoLeaks();

        state.Environment.LightningRound = state.Round - 1;
        Refresh(f, a);
        Assert.DoesNotContain(b.View.Units, u => u.PlayerId == a.Id);
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_change_of_weather_reaches_every_client_with_no_one_named()
    {
        var (f, a, b, _, _) = Standoff("rain", [new WeatherShift(3, "thunderstorm", 1)]);
        f.EndTurnsUntil(() => f.Sim.State.Round >= 3, a, b);
        Refresh(f, f.PlayerUp(a, b)!);
        foreach (var p in new[] { a, b })
        {
            Assert.Equal("thunderstorm", p.View.Environment.Weather);
            Assert.Empty(p.View.Environment.Shifts);
            var turned = p.Link.Inbox.OfType<Snapshot>().SelectMany(s => s.Events).Single(e => e.Type == ScavengeEventType.WeatherChanged);
            Assert.Null(turned.UnitId);
        }
        f.AssertNoLeaks();
    }
}
