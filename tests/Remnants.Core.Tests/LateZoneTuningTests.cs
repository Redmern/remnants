using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// The late-zone re-tune (zones.json <c>retune</c>), selectable to compare: Current (the zone as it is), Expeditions
/// (free expeditions re-tuned, story missions as they are) or Everywhere (story missions too).
/// </summary>
public class LateZoneTuningTests
{
    private static readonly string[] Late = ["rail_depot", "network_relay", "core_spire"];

    [Fact]
    public void Only_the_late_zones_carry_a_retune_and_it_makes_them_denser()
    {
        foreach (var zone in Real.Zones.Values)
            Assert.Equal(Late.Contains(zone.Id), zone.Retune is not null);
        foreach (var id in Late)
        {
            var zone = Real.Zones[id];
            var tuned = zone.Retuned();
            Assert.True(tuned.Enemies[0] > zone.Enemies[0]);
            Assert.True(tuned.Reinforcements.Waves > zone.Reinforcements.Waves);
            Assert.Equal(zone.Reinforcements.Mix, tuned.Reinforcements.Mix); // the same enemies, more of them
            Assert.Equal(zone.EnemyMix, tuned.EnemyMix);
            Assert.True(tuned.Devices[DeviceKind.Turret][0] >= zone.Devices.GetValueOrDefault(DeviceKind.Turret, [0, 0])[0]);
            Assert.Equal(zone.Devices[DeviceKind.Camera], tuned.Devices[DeviceKind.Camera]); // kinds not named keep theirs
            Assert.NotNull(tuned.Reinforcements.AfterRounds);
        }
        Assert.Same(Real.Zones["outskirts"], Real.Zones["outskirts"].Retuned());
    }

    [Theory]
    [InlineData(LateZoneTuning.Current, false, false)]
    [InlineData(LateZoneTuning.Current, true, false)]
    [InlineData(LateZoneTuning.Expeditions, false, true)]
    [InlineData(LateZoneTuning.Expeditions, true, false)]
    [InlineData(LateZoneTuning.Everywhere, false, true)]
    [InlineData(LateZoneTuning.Everywhere, true, true)]
    public void The_mode_picks_who_gets_the_retune(LateZoneTuning mode, bool story, bool retuned) =>
        Assert.Equal(retuned, ZoneTuning.Applies(mode, story));

    [Fact]
    public void A_run_is_created_with_the_retuned_zone_and_remembers_it()
    {
        int Enemies(ScavengeSim s) => s.State.Units.Count(u => u.Kind == UnitKind.Enemy);
        var zone = Real.Zones["core_spire"];
        var plain = NewScavenge.Create(Real, "core_spire", 5, [Party(1)]);
        Assert.False(plain.State.Retuned);
        Assert.InRange(Enemies(plain), zone.Enemies[0], zone.Enemies[1]);

        var tuned = NewScavenge.Create(Real, "core_spire", 5, [Party(1)], new ZoneConditions { LateZones = LateZoneTuning.Expeditions });
        Assert.True(tuned.State.Retuned);
        Assert.InRange(Enemies(tuned), zone.Retuned().Enemies[0], zone.Retuned().Enemies[1]);
        Assert.True(ScavengeSave.Load(Real, ScavengeSave.Save(tuned.State)).State.Retuned);

        var outskirts = NewScavenge.Create(Real, "outskirts", 5, [Party(1)], new ZoneConditions { LateZones = LateZoneTuning.Everywhere });
        Assert.False(outskirts.State.Retuned); // nothing to retune
    }

    [Fact]
    public void A_story_mission_keeps_the_zone_as_it_is_unless_everywhere_is_chosen()
    {
        var mission = new MissionSetup(new FieldMission { Id = "test" });
        var expeditions = NewScavenge.Create(Real, "network_relay", 5, [Party(1)], new ZoneConditions { LateZones = LateZoneTuning.Expeditions }, mission);
        Assert.False(expeditions.State.Retuned);
        var everywhere = NewScavenge.Create(Real, "network_relay", 5, [Party(1)], new ZoneConditions { LateZones = LateZoneTuning.Everywhere }, mission);
        Assert.True(everywhere.State.Retuned);
    }

    [Fact]
    public void A_retuned_run_calls_reinforcements_after_the_retunes_delay()
    {
        string[] yard =
        [
            "..............",
            "..............",
            "..............",
            "..........P...",
            "..............",
            "..............",
        ];
        int RoundsToCall(bool retuned)
        {
            var u = SurvivorUnit(pos: new(7, 1), attributes: 90);
            var linked = EnemyUnit("linked", new(12, 5));
            var sim = Arena(14, 6, [u, linked], yard, zoneId: "network_relay");
            sim.State.Retuned = retuned;
            linked.Hp = 9999;
            u.Hp = u.MaxHp = 9999;
            for (var i = 1; i <= 10; i++)
            {
                Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
                if (sim.State.Events.Any(e => e.Type == ScavengeEventType.Reinforced)) return i;
            }
            return 99;
        }
        var after = Real.Zones["network_relay"].Retuned().Reinforcements.AfterRounds!.Value;
        Assert.True(after < Real.Rules.Network.ReinforceAfterRounds);
        Assert.Equal(Real.Rules.Network.ReinforceAfterRounds, RoundsToCall(false));
        Assert.Equal(after, RoundsToCall(true));
    }
}
