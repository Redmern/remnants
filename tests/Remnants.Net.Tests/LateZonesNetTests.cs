using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

/// <summary>The late-zone tuning in multiplayer: the host picks it, the run takes it, and every player sees which in the lobby.</summary>
public class LateZonesNetTests
{
    private static (NetFixture F, TestPlayer A) Start(HostOptions options)
    {
        var f = new NetFixture(options with { TurnSeconds = 0, MinPlayers = 2 });
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        Assert.Equal(options.LateZones, b.Client.Lobby!.LateZones); // a joiner sees the host's choice before the run
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        return (f, a);
    }

    [Fact]
    public void A_host_defaults_to_retuned_expeditions_like_single_player() =>
        Assert.Equal(LateZoneTuning.Expeditions, new HostOptions().LateZones);

    [Theory]
    [InlineData(LateZoneTuning.Current, false)]
    [InlineData(LateZoneTuning.Expeditions, true)]
    [InlineData(LateZoneTuning.Everywhere, true)]
    public void The_hosts_choice_decides_whether_a_late_zone_run_is_retuned(LateZoneTuning mode, bool retuned)
    {
        var (f, _) = Start(new HostOptions { ZoneId = "core_spire", LateZones = mode });
        Assert.NotNull(f.Sim);
        Assert.Equal(retuned, f.Sim!.State.Retuned);
    }

    [Fact]
    public void An_early_zone_is_never_retuned()
    {
        var (f, _) = Start(new HostOptions { ZoneId = "outskirts", LateZones = LateZoneTuning.Everywhere });
        Assert.False(f.Sim!.State.Retuned);
    }
}
