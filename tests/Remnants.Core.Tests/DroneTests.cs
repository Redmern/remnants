using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Scout drones: deploy, fly over anything within flight and control range, battery and recharge, landing, recall,
/// marking and payloads, being shot down (and Husks not bothering), jammers, and sharing what they see with allies.
/// </summary>
public class DroneTests
{
    /// <summary>A wall from top to bottom at x = 5 with no gap: only a drone gets across.</summary>
    private static readonly string[] Split = HackingTests.Rows(12, 8, (5, 0, 5, 7));

    private static Unit Operator(Pos pos, params string[] perks)
    {
        var u = SurvivorUnit(1, pos, pack: ["scout_drone"]);
        u.Hooks = Real.HooksFor(perks);
        return u;
    }

    private static Unit Deploy(ScavengeSim sim, Unit op, Pos at)
    {
        var r = sim.Execute(new DeployDrone(1, op.Id, at));
        Assert.True(r.Ok, r.Message);
        return sim.State.FindUnit(op.DroneId!.Value)!;
    }

    private static void NextRound(ScavengeSim sim)
    {
        var round = sim.State.Round;
        while (sim.State.Round == round && !sim.State.Finished)
        {
            var u = sim.State.CurrentUnit!;
            Assert.True(sim.Execute(new EndTurn(u.PlayerId!.Value, u.Id)).Ok);
        }
    }

    [Fact]
    public void Deploying_takes_the_drone_out_of_the_pack_onto_a_tile_next_to_the_operator()
    {
        var op = Operator(new Pos(2, 2));
        var sim = Arena(12, 8, [op], Split);
        Assert.Equal(ScavengeError.InvalidTile, sim.Execute(new DeployDrone(1, op.Id, new Pos(4, 4))).Error);
        var drone = Deploy(sim, op, new Pos(3, 2));

        Assert.Equal(UnitKind.Drone, drone.Kind);
        Assert.Equal(op.Side, drone.Side);
        Assert.DoesNotContain("scout_drone", op.Pack);
        Assert.Equal(Real.Items["scout_drone"].Drone!.Battery, drone.Battery);
        Assert.False(drone.PlayerControlled, "the operator flies it; it takes no turns");
        Assert.DoesNotContain(drone.Id, sim.State.TurnOrder);
        Assert.Equal(ScavengeError.DroneAlreadyOut, sim.Execute(new DeployDrone(1, op.Id, new Pos(1, 2))).Error);

        var none = SurvivorUnit(1, new Pos(8, 2));
        var sim2 = Arena(12, 8, [none]);
        Assert.Equal(ScavengeError.NoDrone, sim2.Execute(new DeployDrone(1, none.Id, new Pos(8, 3))).Error);
    }

    [Fact]
    public void A_drone_flies_over_a_wall_and_shows_the_side_what_is_behind_it()
    {
        var op = Operator(new Pos(3, 3));
        var husk = EnemyUnit("husk", new Pos(9, 3));
        var sim = Arena(12, 8, [op, husk], Split);
        Assert.False(Detection.SideSees(Real, sim.State, op.Side, husk));
        var drone = Deploy(sim, op, new Pos(4, 3));

        Assert.Equal(ScavengeError.InvalidTile, sim.Execute(new FlyDrone(1, op.Id, new Pos(5, 3))).Error);
        Assert.Contains(new Pos(6, 3), Drones.FlightTargets(sim, op));
        Assert.True(sim.Execute(new FlyDrone(1, op.Id, new Pos(6, 3))).Ok, "straight over the wall");
        Assert.Equal(0, op.Ap);
        Assert.True(Detection.SideSees(Real, sim.State, op.Side, husk), "the drone sees it");
        Assert.Contains(sim.State.Map.Index(new Pos(10, 3)), sim.VisibleTo(1));
        Assert.True(op.Get(Skill.DronePiloting) > 0);
        Assert.Equal(ScavengeError.TooFar, sim.Execute(new FlyDrone(1, op.Id, new Pos(11, 3))).Error);
    }

    [Fact]
    public void The_control_range_holds_the_drone_near_its_operator_and_perks_stretch_it()
    {
        var op = Operator(new Pos(0, 0));
        var sim = Arena(20, 4, [op]);
        var drone = Deploy(sim, op, new Pos(1, 0));
        var range = Drones.ControlRange(Real, op, "scout_drone");
        drone.Pos = new Pos(range - 1, 0);
        Assert.True(sim.Execute(new FlyDrone(1, op.Id, new Pos(range, 0))).Ok);
        Assert.Equal(ScavengeError.OutOfRange, sim.Execute(new FlyDrone(1, op.Id, new Pos(range + 1, 0))).Error);

        Assert.Equal(range + 2, Drones.ControlRange(Real, Operator(default, "drone_operator_1"), "scout_drone"));
        var pilot = Operator(default);
        pilot.Skills[Skill.DronePiloting] = 50;
        Assert.Equal(range + 2, Drones.ControlRange(Real, pilot, "scout_drone"));
        Assert.Equal(Real.Items["scout_drone"].Drone!.Battery + 1, Drones.MaxBattery(Real, Operator(default, "drone_operator_1"), "scout_drone"));
    }

    [Fact]
    public void The_battery_runs_down_and_a_flat_drone_lands_as_an_item()
    {
        var op = Operator(new Pos(2, 2));
        var sim = Arena(12, 8, [op]);
        var drone = Deploy(sim, op, new Pos(3, 2));
        var battery = drone.Battery;
        NextRound(sim);
        Assert.Equal(battery - 1, drone.Battery);
        for (var i = 1; i < battery; i++) NextRound(sim);

        Assert.Equal(UnitStatus.Extracted, drone.Status);
        Assert.Null(op.DroneId);
        var cache = Assert.Single(sim.State.Caches);
        Assert.Equal(new Pos(3, 2), cache.Pos);
        Assert.Contains("scout_drone", cache.Items);
        Assert.True(sim.Execute(new PickUp(1, op.Id, cache.Id, "scout_drone")).Ok);
        Assert.Equal(ScavengeError.LowBattery, sim.Execute(new DeployDrone(1, op.Id, new Pos(1, 2))).Error);
        NextRound(sim);
        NextRound(sim);
        Assert.True(sim.Execute(new DeployDrone(1, op.Id, new Pos(1, 2))).Ok, "recharged enough to take off");
    }

    [Fact]
    public void Recalled_it_goes_back_to_the_pack_and_recharges()
    {
        var op = Operator(new Pos(2, 2));
        var sim = Arena(12, 8, [op]);
        var drone = Deploy(sim, op, new Pos(3, 2));
        NextRound(sim);
        NextRound(sim);
        Assert.True(sim.Execute(new RecallDrone(1, op.Id)).Ok);
        Assert.Contains("scout_drone", op.Pack);
        Assert.Equal(UnitStatus.Extracted, drone.Status);
        Assert.Equal(drone.Battery, op.DroneBattery);
        var stored = op.DroneBattery!.Value;
        NextRound(sim);
        Assert.Equal(stored + 1, op.DroneBattery);
        for (var i = 0; i < 6; i++) NextRound(sim);
        Assert.Null(op.DroneBattery);
    }

    [Fact]
    public void A_drone_out_when_its_operator_extracts_comes_home()
    {
        var op = Operator(new Pos(0, 0));
        var sim = Arena(12, 8, [op], ["E..........."]);
        Deploy(sim, op, new Pos(1, 1));
        Assert.True(sim.Execute(new Extract(1, op.Id)).Ok);
        Assert.Contains("scout_drone", Assert.Single(sim.ResultFor(1).Returned).Pack);
    }

    [Fact]
    public void A_shot_down_drone_is_lost()
    {
        var op = Operator(new Pos(2, 2));
        var sim = Arena(12, 8, [op]);
        var drone = Deploy(sim, op, new Pos(3, 2));
        Combat.ApplyDamage(sim, drone, BodyPart.Torso, 100);
        Assert.Equal(UnitStatus.Dead, drone.Status);
        Assert.Empty(sim.State.Caches);
        Assert.Null(op.DroneId);
        Assert.DoesNotContain("scout_drone", op.AllItems);
    }

    [Fact]
    public void The_linked_shoot_at_a_drone_they_see_and_husks_ignore_it()
    {
        var op = Operator(new Pos(1, 1));
        var linked = EnemyUnit("linked", new Pos(9, 6));
        var sim = Arena(12, 8, [op, linked], Split);
        Deploy(sim, op, new Pos(2, 1));
        var drone = sim.State.FindUnit(op.DroneId!.Value)!;
        drone.Pos = new Pos(8, 2);
        ScavengeAi.TakeTurn(sim, linked);
        Assert.Equal(drone.Pos, Assert.Single(sim.State.Strikes, s => s.UnitId == linked.Id).Tile); // it telegraphs a shot at the drone
        Assert.True(sim.Execute(new EndTurn(1, op.Id)).Ok);
        Assert.Contains(sim.State.Events, e => e.UnitId == linked.Id && e.TargetId == drone.Id
                                               && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed or ScavengeEventType.Suppressed);

        var op2 = Operator(new Pos(1, 1));
        var husk = EnemyUnit("husk", new Pos(9, 3));
        var sim2 = Arena(12, 8, [op2, husk], Split);
        var drone2 = Deploy(sim2, op2, new Pos(2, 1));
        drone2.Pos = new Pos(9, 2);
        ScavengeAi.TakeTurn(sim2, husk);
        Assert.DoesNotContain(sim2.State.Strikes, s => s.UnitId == husk.Id && s.Tile == drone2.Pos);
        Assert.True(sim2.Execute(new EndTurn(1, op2.Id)).Ok);
        Assert.True(sim2.Execute(new EndTurn(1, op2.Id)).Ok);
        Assert.DoesNotContain(sim2.State.Events, e => e.UnitId == husk.Id && e.TargetId == drone2.Id);
    }

    [Fact]
    public void Melee_cannot_reach_a_drone()
    {
        var pipe = SurvivorUnit(2, new Pos(4, 2), weapon: "pipe");
        var op = Operator(new Pos(2, 2));
        var sim = Arena(8, 6, [op, pipe]);
        var drone = Deploy(sim, op, new Pos(3, 2));
        Assert.True(sim.Execute(new EndTurn(1, op.Id)).Ok);
        // A swing at the drone's tile comes down under it.
        Assert.True(StrikeNow(sim, pipe, drone, "swing").Ok);
        Assert.Equal(drone.MaxHp, drone.Hp);
        Assert.DoesNotContain(sim.State.Events, e => e.TargetId == drone.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
        Assert.Contains(sim.State.Events, e => e.UnitId == pipe.Id && e.Type == ScavengeEventType.IntentFizzled);
    }

    [Fact]
    public void A_spotter_marks_a_target_for_the_side_through_walls()
    {
        var op = Operator(new Pos(2, 3));
        var linked = EnemyUnit("linked", new Pos(9, 3));
        var sim = Arena(12, 8, [op, linked], Split);
        var drone = Deploy(sim, op, new Pos(3, 3));
        drone.Pos = new Pos(8, 1);
        Assert.Equal(ScavengeError.MissingPerk, sim.Execute(new MarkTarget(1, op.Id, linked.Id)).Error);
        op.Hooks = Real.HooksFor(["drone_operator_1", "spotter"]);
        var shooter = SurvivorUnit(1, new Pos(4, 3), weapon: "pistol");
        Assert.True(sim.Execute(new MarkTarget(1, op.Id, linked.Id)).Ok);

        drone.Pos = new Pos(1, 6);
        Assert.True(Detection.SideSees(Real, sim.State, op.Side, linked), "marked: seen without the drone's eyes on it");
        var action = ProfileDeriver.Derive(Real, shooter).Action("shoot")!;
        sim.State.Units.Add(shooter);
        var marked = Combat.ChanceToHit(sim, shooter, action, linked);
        sim.State.Marks.Clear();
        Assert.True(marked > Combat.ChanceToHit(sim, shooter, action, linked));
    }

    [Fact]
    public void A_payload_drone_drops_a_flare_where_it_hovers()
    {
        var op = Operator(new Pos(2, 3));
        op.Pack.Add("flare");
        var sim = Arena(12, 8, [op], Split);
        var drone = Deploy(sim, op, new Pos(3, 3));
        drone.Pos = new Pos(8, 3);
        Assert.Equal(ScavengeError.MissingPerk, sim.Execute(new DropPayload(1, op.Id, "flare")).Error);
        op.Hooks = Real.HooksFor(["payload"]);
        Assert.True(sim.Execute(new DropPayload(1, op.Id, "flare")).Ok);
        Assert.True(Detection.IsLit(sim.State, new Pos(8, 3)));
        Assert.DoesNotContain("flare", op.Pack);
    }

    [Fact]
    public void A_jammer_drains_the_battery_and_cuts_the_flight_short()
    {
        var op = Operator(new Pos(2, 2));
        op.Equipment[EquipSlot.Tool] = "signal_jammer";
        var sim = Arena(14, 8, [op]);
        var drone = Deploy(sim, op, new Pos(3, 2));
        Assert.Equal(1, Drones.FlyRange(sim, drone));
        var battery = drone.Battery;
        NextRound(sim);
        Assert.Equal(battery - Real.Rules.Drone.JammedDrain, drone.Battery);
    }

    [Fact]
    public void Allies_see_what_the_drone_sees()
    {
        var op = Operator(new Pos(2, 3));
        var ally = SurvivorUnit(2, new Pos(1, 1));
        var husk = EnemyUnit("husk", new Pos(9, 3));
        var sim = Arena(12, 8, [op, ally, husk], Split);
        var team = Alliances.Merge(sim.State, 1, 2);
        var drone = Deploy(sim, op, new Pos(3, 3));
        Assert.Equal(team, drone.Side);
        drone.Pos = new Pos(7, 3);
        Assert.True(Detection.SideSees(Real, sim.State, ally.Side, husk));
    }

    [Fact]
    public void Saves_carry_a_drone_in_the_air()
    {
        var op = Operator(new Pos(2, 2));
        var sim = Arena(12, 8, [op]);
        Deploy(sim, op, new Pos(3, 2));
        var json = ScavengeSave.Save(sim.State);
        var loaded = ScavengeSave.Load(Real, json);
        Assert.Equal(json, ScavengeSave.Save(loaded.State));
        Assert.NotNull(Drones.DroneOf(loaded.State, loaded.State.FindUnit(op.Id)!));
    }
}
