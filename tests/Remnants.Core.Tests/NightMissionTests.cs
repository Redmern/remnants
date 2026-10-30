using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class NightMissionTests
{
    private static ScavengeSim At(DayPhase phase, IEnumerable<Unit> units, int width = 24, int height = 5,
        Action<ScavengeData, ScavengeState>? setup = null, ScavengeData? data = null) =>
        Arena(width, height, units, data: data, setup: (d, s) =>
        {
            s.Environment = ScavengeEnvironment.At(phase == DayPhase.Night ? 23 : 12, phase);
            setup?.Invoke(d, s);
        });

    [Fact]
    public void Night_effects_load_from_data()
    {
        var night = Real.Rules.Environment.Phases[DayPhase.Night];
        Assert.True(night.SightFor(SenseKind.Human) < 1);
        Assert.Equal(1, night.SightFor(SenseKind.Linked));
        Assert.Equal(1, night.SightFor(SenseKind.Camera));
        Assert.True(night.SightFor(SenseKind.Drone) > night.SightFor(SenseKind.Human), "drones lose less sight than people");
        Assert.Contains(SenseKind.Linked, night.StealthIgnoredBy);
        Assert.True(night.HuskCountMultiplier > 1);
        Assert.False(Real.Rules.Environment.Phases.ContainsKey(DayPhase.Day), "day is the baseline");
    }

    [Fact]
    public void People_see_less_at_night_the_Linked_and_cameras_do_not()
    {
        var human = SurvivorUnit(1, new Pos(1, 2));
        var linked = EnemyUnit("linked", new Pos(20, 2));
        var day = At(DayPhase.Day, [human, linked]);
        var dayHuman = day.Profile(human).Sight;
        var dayLinked = day.Profile(linked).Sight;

        var human2 = SurvivorUnit(1, new Pos(1, 2));
        var linked2 = EnemyUnit("linked", new Pos(20, 2));
        var night = At(DayPhase.Night, [human2, linked2]);
        Assert.Equal(dayHuman * Real.Rules.Environment.Phases[DayPhase.Night].SightFor(SenseKind.Human), night.Profile(human2).Sight, 9);
        Assert.Equal(dayLinked, night.Profile(linked2).Sight, 9);
    }

    [Fact]
    public void A_husk_seen_by_day_is_lost_in_the_dark_but_a_flare_shows_it()
    {
        Unit Husk() => EnemyUnit("husk", new Pos(6, 2));
        var dayHuman = SurvivorUnit(1, new Pos(1, 2));
        var dayHusk = Husk();
        var day = At(DayPhase.Day, [dayHuman, dayHusk]);
        Assert.True(Detection.SideSees(Real, day.State, dayHuman.Side, dayHusk));

        var human = SurvivorUnit(1, new Pos(1, 2));
        var husk = Husk();
        var night = At(DayPhase.Night, [human, husk]);
        Assert.False(Detection.SideSees(Real, night.State, human.Side, husk));
        Assert.DoesNotContain(night.State.Map.Index(husk.Pos), Visibility.SideVisible(Real, night.State, human.Side));

        night.State.Flares.Add(new Flare(husk.Pos, 2, night.State.Round + 1));
        Assert.True(Detection.SideSees(Real, night.State, human.Side, husk));
    }

    [Fact]
    public void Night_hides_people_from_husks_but_not_from_the_Linked()
    {
        // Same distance, same survivor: a Husk loses them at night, a Linked does not.
        var p1 = SurvivorUnit(1, new Pos(1, 2));
        var husk = EnemyUnit("husk", new Pos(5, 2));
        var linked = EnemyUnit("linked", new Pos(5, 3));
        var day = At(DayPhase.Day, [p1, husk, linked]);
        Assert.True(Detection.Spots(Real, day.State, husk, p1));
        Assert.True(Detection.Spots(Real, day.State, linked, p1));

        var p2 = SurvivorUnit(1, new Pos(1, 2));
        var husk2 = EnemyUnit("husk", new Pos(5, 2));
        var linked2 = EnemyUnit("linked", new Pos(5, 3));
        var night = At(DayPhase.Night, [p2, husk2, linked2]);
        Assert.False(Detection.Spots(Real, night.State, husk2, p2), "stealth is higher in the dark");
        Assert.True(Detection.Spots(Real, night.State, linked2, p2), "the Linked's sensors see through it");
    }

    [Fact]
    public void A_camera_sees_as_well_at_night()
    {
        ZoneDevice camera = null!;
        var mover = SurvivorUnit(1, new Pos(6, 2));
        var night = At(DayPhase.Night, [mover], setup: (d, s) => camera = Hacking.Place(d, s, DeviceKind.Camera, new Pos(1, 1)));
        var cam = night.State.FindUnit(camera.UnitId!.Value)!;
        Assert.Equal(SenseKind.Camera, FieldEnvironment.SenseOf(Real, night.State, cam));
        Assert.Equal(ProfileDeriver.Derive(Real, cam).Sight, night.Profile(cam).Sight, 9);
        Assert.Equal(0, FieldEnvironment.StealthBonusAgainst(Real, night.State, cam));
    }

    [Fact]
    public void A_muzzle_flash_shows_the_shooter_beyond_night_sight()
    {
        var human = SurvivorUnit(1, new Pos(1, 2));
        var shooter = EnemyUnit("husk", new Pos(7, 2));
        var night = At(DayPhase.Night, [human, shooter]);
        Assert.False(Detection.SideSees(Real, night.State, human.Side, shooter));
        night.State.Reveals.Add(new Reveal(shooter.Id, human.Side, night.State.Round + 1));
        Assert.True(Detection.SideSees(Real, night.State, human.Side, shooter));
    }

    [Fact]
    public void Husks_hear_further_at_night()
    {
        // A pistol shot (noise 8) at a Husk next to the shooter; a second Husk 10 tiles off hears it only at night.
        AiMode Heard(DayPhase phase)
        {
            var shooter = SurvivorUnit(1, new Pos(1, 2), weapon: "pistol");
            var target = EnemyUnit("husk", new Pos(2, 2));
            var listener = EnemyUnit("husk", new Pos(11, 2));
            var sim = At(phase, [shooter, target, listener]);
            var r = sim.Execute(new Attack(1, shooter.Id, target.Id, "shoot"));
            Assert.True(r.Ok, r.Message);
            return listener.Ai.Mode;
        }
        Assert.Equal(AiMode.Idle, Heard(DayPhase.Day));
        Assert.Equal(AiMode.Hunting, Heard(DayPhase.Night));
    }

    [Fact]
    public void Night_brings_more_husks_and_day_runs_are_unchanged()
    {
        int Husks(ScavengeSim s) => s.State.Units.Count(u => u.EnemyDefId is { } id && Real.Enemies[id].Ai == AiKind.Husk);
        int day = 0, plain = 0, night = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var d = NewScavenge.Create(Real, "outskirts", seed, [Party(1)], new ZoneConditions(Environment: ScavengeEnvironment.At(12, DayPhase.Day)));
            var p = NewScavenge.Create(Real, "outskirts", seed, [Party(1)]);
            var n = NewScavenge.Create(Real, "outskirts", seed, [Party(1)], new ZoneConditions(Environment: ScavengeEnvironment.At(23, DayPhase.Night)));
            Assert.Equal(DayPhase.Night, n.State.Environment.Phase);
            day += Husks(d);
            plain += Husks(p);
            night += Husks(n);
        }
        Assert.Equal(plain, day);
        var mult = Real.Rules.Environment.Phases[DayPhase.Night].HuskCountMultiplier;
        Assert.True(night >= day * (1 + (mult - 1) * 0.8), $"night {night} vs day {day} (×{mult})");
    }

    [Fact]
    public void Flares_light_further_at_night()
    {
        var thrower = SurvivorUnit(1, new Pos(1, 2), pack: "flare");
        var night = At(DayPhase.Night, [thrower]);
        var r = night.Execute(new UseItem(1, thrower.Id, "flare", At: new Pos(4, 2)));
        Assert.True(r.Ok, r.Message);
        var use = Real.Items["flare"].Use!;
        Assert.Equal(use.LightRadius * Real.Rules.Environment.Phases[DayPhase.Night].FlareRadiusMultiplier, night.State.Flares.Single().Radius, 9);
    }

    [Fact]
    public void Weather_stacks_on_the_phase()
    {
        var data = With(r => r with
        {
            Environment = r.Environment with
            {
                Weather = new() { ["fog"] = new EnvironmentEffects { Sight = new() { [SenseKind.Human] = 0.5 }, StealthBonus = 5 } },
            },
        });
        var human = SurvivorUnit(1, new Pos(1, 2));
        var sim = Arena(24, 5, [human], data: data, setup: (_, s) => s.Environment = ScavengeEnvironment.At(23, DayPhase.Night, "fog"));
        var effects = FieldEnvironment.Effects(data, sim.State);
        var night = data.Rules.Environment.Phases[DayPhase.Night];
        Assert.Equal(night.SightFor(SenseKind.Human) * 0.5, effects.SightFor(SenseKind.Human), 9);
        Assert.Equal(night.StealthBonus + 5, effects.StealthBonus, 9);
        var bare = ProfileDeriver.Derive(data, human).Sight;
        Assert.Equal(Math.Max(effects.MinSight, bare * effects.SightFor(SenseKind.Human)), sim.Profile(human).Sight, 9);
    }

    [Fact]
    public void The_environment_survives_a_save_and_old_runs_load_at_midday()
    {
        var sim = NewScavenge.Create(Real, "outskirts", 3, [Party(1)], new ZoneConditions(Environment: ScavengeEnvironment.At(22, DayPhase.Night)));
        var loaded = ScavengeSave.Load(Real, ScavengeSave.Save(sim.State));
        Assert.Equal(22, loaded.State.Environment.Hour);
        Assert.Equal(DayPhase.Night, loaded.State.Environment.Phase);

        var node = System.Text.Json.Nodes.JsonNode.Parse(ScavengeSave.Save(sim.State))!;
        node["version"] = 5;
        node["scavenge"]!.AsObject().Remove("environment");
        var old = ScavengeSave.Load(Real, node.ToJsonString());
        Assert.Equal(DayPhase.Day, old.State.Environment.Phase);
        Assert.Equal(12, old.State.Environment.Hour);
    }

    [Fact]
    public void A_party_can_wait_for_dawn_at_the_cost_of_rations()
    {
        var colony = TestData.EmptyColony(stock: new() { ["food"] = 100 });
        var sim = new ColonySim(TestData.RealWithNights, colony.State, Real);
        sim.State.Tick = 22;  // launching at night onto the doorstep
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        var zone = Real.Zones.Values.OrderBy(z => z.TravelDays).First(z => z.TravelDays == 0).Id;

        var now = sim.PlanArrival(zone);
        Assert.Equal(DayPhase.Night, now.Phase);
        Assert.Equal(0, now.WaitTicks);

        var dawn = sim.PlanArrival(zone, DayPhase.Dawn);
        Assert.Equal(DayPhase.Dawn, dawn.Phase);
        Assert.Equal(7, dawn.WaitTicks);  // 22:00 → 05:00
        Assert.True(dawn.WaitRationsPerMember > 0);

        var food = sim.State.Stockpile.Get("food");
        var r = sim.Execute(new LaunchExpedition(zone, [new(a.Id), new(b.Id)], DayPhase.Dawn));
        Assert.True(r.Ok, r.Message);
        var spent = food - sim.State.Stockpile.Get("food");
        Assert.Equal((WorldMap.RationsPerMember(sim.Data, Real.Zones[zone]) + dawn.WaitRationsPerMember) * 2, spent, 9);
        Assert.Equal(ExpeditionPhase.Outbound, sim.CurrentExpeditionPhase);

        sim.Advance(7);
        Assert.Equal(ExpeditionPhase.InZone, sim.CurrentExpeditionPhase);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.ExpeditionArrived);
        var run = sim.OpenExpeditionScavenge();
        Assert.Equal(DayPhase.Dawn, run.State.Environment.Phase);
        Assert.Equal(5, run.State.Environment.Hour);
    }

    [Fact]
    public void A_mission_is_played_at_the_hour_the_party_arrives()
    {
        var colony = TestData.EmptyColony(stock: new() { ["food"] = 100 });
        var sim = new ColonySim(TestData.RealWithNights, colony.State, Real);
        sim.State.Tick = 21;
        var a = sim.AddSurvivor();
        var zone = Real.Zones.Values.First(z => z.TravelDays == 0).Id;
        Assert.True(sim.Execute(new LaunchExpedition(zone, [new(a.Id)])).Ok);
        Assert.Equal(DayPhase.Night, sim.OpenExpeditionScavenge().State.Environment.Phase);
    }

    [Fact]
    public void With_day_and_night_off_missions_are_at_midday_and_nobody_waits()
    {
        var colony = TestData.EmptyColony(stock: new() { ["food"] = 100 });
        var sim = new ColonySim(TestData.Real, colony.State, Real);
        sim.State.Tick = 23;
        var a = sim.AddSurvivor();
        var zone = Real.Zones.Values.First(z => z.TravelDays == 0).Id;
        Assert.Equal(0, sim.PlanArrival(zone, DayPhase.Dawn).WaitTicks);
        Assert.True(sim.Execute(new LaunchExpedition(zone, [new(a.Id)], DayPhase.Dawn)).Ok);
        Assert.Equal(DayPhase.Day, sim.OpenExpeditionScavenge().State.Environment.Phase);
    }
}
