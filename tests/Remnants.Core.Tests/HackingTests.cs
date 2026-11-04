using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Hackable devices: what each kind does for whoever hacks it, the hack action (tool, reach, AP, work over several actions,
/// the roll), what a failure costs (alarm, the Network, shock, lockout), Override on a Linked, and the perks that feed it.
/// </summary>
public class HackingTests
{
    private const string P1 = "player:1";

    /// <summary>Real data with every hack roll forced to succeed (1) or fail (0).</summary>
    private static ScavengeData Rolls(double chance) =>
        With(r => r with { Hacking = r.Hacking with { MinChance = chance, MaxChance = chance } });

    private static Unit Hacker(Pos pos, string tool = "hacking_kit", params string[] perks)
    {
        var u = SurvivorUnit(1, pos, pack: [tool]);
        u.Hooks = Real.HooksFor(perks);
        return u;
    }

    private static ScavengeResult Hack(ScavengeSim sim, Unit unit, ZoneDevice device) => sim.Execute(new Hack(1, unit.Id, device.Id));

    private static void EndTurns(ScavengeSim sim, int rounds)
    {
        var start = sim.State.Round;
        while (sim.State.Round < start + rounds && !sim.State.Finished)
        {
            var u = sim.State.CurrentUnit!;
            Assert.True(sim.Execute(new EndTurn(u.PlayerId!.Value, u.Id)).Ok);
        }
    }

    [Fact]
    public void A_hack_needs_a_tool_in_reach_and_the_ap()
    {
        ZoneDevice terminal = null!;
        var bare = SurvivorUnit(1, new Pos(1, 1));
        var sim = Arena(8, 8, [bare], setup: (d, s) => terminal = Hacking.Place(d, s, DeviceKind.Relay, new Pos(2, 1)));
        Assert.Equal(ScavengeError.NoHackingTool, Hack(sim, bare, terminal).Error);

        bare.Pack.Add("hacking_kit");
        bare.Pos = new Pos(4, 1);
        Assert.Equal(ScavengeError.TooFar, Hack(sim, bare, terminal).Error);
        bare.Pack.Remove("hacking_kit");
        bare.Equipment[Items.EquipSlot.Tool] = "cyberdeck";
        Assert.True(Hack(sim, bare, terminal).Ok, "a cyberdeck reaches 3 tiles");
        Assert.Equal(1, bare.Ap);
        Assert.Equal(ScavengeError.NotEnoughAp, Hack(sim, bare, terminal).Error);
        Assert.Equal(ScavengeError.UnknownDevice, sim.Execute(new Hack(1, bare.Id, 99)).Error);
    }

    [Fact]
    public void A_terminal_takes_two_actions_then_gives_up_its_data_the_map_and_contacts()
    {
        ZoneDevice terminal = null!;
        var hacker = Hacker(new Pos(1, 1));
        var husk = EnemyUnit("husk", new Pos(12, 12));
        var sim = Arena(14, 14, [hacker, husk], data: Rolls(1),
            setup: (d, s) => terminal = Hacking.Place(d, s, DeviceKind.Terminal, new Pos(2, 1), ["sch_override", "manual_intrusion"]));

        Assert.True(Hack(sim, hacker, terminal).Ok);
        Assert.Equal(1, Hacking.ProgressOf(terminal, P1)!.Done);
        Assert.False(terminal.Hacked);
        EndTurns(sim, 1);
        Assert.True(Hack(sim, hacker, terminal).Ok);

        Assert.True(terminal.Hacked && terminal.Used);
        Assert.Contains("sch_override", hacker.Pack);
        Assert.Contains("manual_intrusion", hacker.Pack);
        Assert.Equal(14 * 14, sim.State.Explored[P1].Count);
        Assert.Contains(sim.State.Contacts, c => c.Side == P1 && c.Pos.Chebyshev(husk.Pos) <= 1);
        Assert.True(hacker.Get(Skill.Hacking) > 0, "hacking trains the skill");
        Assert.True(hacker.FieldXp > 0);
        Assert.Equal(ScavengeError.AlreadyHacked, Hack(sim, hacker, terminal).Error);
    }

    [Fact]
    public void A_failure_sounds_the_alarm_shocks_the_hacker_and_locks_the_side_out()
    {
        ZoneDevice terminal = null!;
        var hacker = Hacker(new Pos(1, 1));
        var husk = EnemyUnit("husk", new Pos(7, 1));
        var linked = EnemyUnit("linked", new Pos(18, 18));
        var sim = Arena(20, 20, [hacker, husk, linked], data: Rolls(0),
            rows: Rows(20, 20, (10, 0, 10, 19)),
            setup: (d, s) =>
            {
                terminal = Hacking.Place(d, s, DeviceKind.Terminal, new Pos(2, 1));
                s.Devices[0].Progress.Add(new HackProgress(P1, 1, 1, 2));
            });
        var hp = hacker.Hp;
        Assert.True(Hack(sim, hacker, terminal).Ok);

        Assert.False(terminal.Hacked);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.HackFailed);
        Assert.Equal(AiMode.Hunting, husk.Ai.Mode);
        Assert.Equal(AiMode.Hunting, linked.Ai.Mode);
        Assert.Equal(hacker.Pos, linked.Ai.LastKnown);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Alarm);
        Assert.True(hacker.Hp < hp, "feedback shock");
        Assert.Null(Hacking.ProgressOf(terminal, P1));
        Assert.True(Hacking.LockedOut(sim.State, terminal, P1));
        hacker.Ap = 3;
        Assert.Equal(ScavengeError.LockedOut, Hack(sim, hacker, terminal).Error);
    }

    [Fact]
    public void Network_ghost_quietens_the_alarm_and_keeps_it_off_the_network()
    {
        ZoneDevice terminal = null!;
        var hacker = Hacker(new Pos(1, 1), "hacking_kit", "network_ghost");
        hacker.Hooks[PerkHooks.HackAlarmReduction] = 1;
        var husk = EnemyUnit("husk", new Pos(6, 1));
        var linked = EnemyUnit("linked", new Pos(18, 18));
        var sim = Arena(20, 20, [hacker, husk, linked], data: Rolls(0), rows: Rows(20, 20, (10, 0, 10, 19)),
            setup: (d, s) => terminal = Hacking.Place(d, s, DeviceKind.Terminal, new Pos(2, 1)));
        terminal.Progress.Add(new HackProgress(P1, hacker.Id, 1, 2));
        Assert.True(Hack(sim, hacker, terminal).Ok);
        Assert.Equal(AiMode.Idle, husk.Ai.Mode);
        Assert.Equal(AiMode.Idle, linked.Ai.Mode);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.Alarm);
    }

    [Fact]
    public void A_jammer_takes_the_network_penalty_off_and_keeps_a_failure_off_the_network()
    {
        ZoneDevice door = null!;
        var hacker = Hacker(new Pos(1, 1));
        var linked = EnemyUnit("linked", new Pos(18, 18));
        var sim = Arena(20, 20, [hacker, linked], rows: Rows(20, 20, (10, 0, 10, 19)),
            setup: (d, s) => door = Hacking.Place(d, s, DeviceKind.Door, new Pos(2, 1)));
        var plain = Hacking.Chance(sim, hacker, door);
        hacker.Equipment[Items.EquipSlot.Tool] = "signal_jammer";
        Assert.Equal(plain + Real.Rules.Hacking.NetworkPenalty, Hacking.Chance(sim, hacker, door), 6);

        var failing = Arena(20, 20, [Hacker(new Pos(1, 1)), EnemyUnit("linked", new Pos(18, 18))], data: Rolls(0),
            rows: Rows(20, 20, (10, 0, 10, 19)), setup: (d, s) => door = Hacking.Place(d, s, DeviceKind.Door, new Pos(2, 1)));
        var u = failing.State.Units[0];
        u.Equipment[Items.EquipSlot.Tool] = "signal_jammer";
        door.Progress.Add(new HackProgress(P1, u.Id, 1, 2));
        Assert.True(Hack(failing, u, door).Ok);
        Assert.Equal(AiMode.Idle, failing.State.Units[1].Ai.Mode);
    }

    [Fact]
    public void Skill_intellect_tools_and_perks_raise_the_chance()
    {
        ZoneDevice terminal = null!;
        var novice = Hacker(new Pos(1, 1));
        var sim = Arena(8, 8, [novice], setup: (d, s) => terminal = Hacking.Place(d, s, DeviceKind.Terminal, new Pos(2, 1)));
        var baseline = Hacking.Chance(sim, novice, terminal);

        var skilled = Hacker(new Pos(1, 1));
        skilled.Skills[Skill.Hacking] = 40;
        Assert.True(Hacking.Chance(sim, skilled, terminal) > baseline + 0.15);
        var deck = Hacker(new Pos(1, 1), "cyberdeck");
        Assert.True(Hacking.Chance(sim, deck, terminal) > baseline + 0.1);
        var perked = Hacker(new Pos(1, 1), "hacking_kit", "hacking_1", "hacking_2");
        Assert.Equal(baseline + 0.2, Hacking.Chance(sim, perked, terminal), 6);
        Assert.Equal(1, Hacking.ApCost(Real, perked));
        Assert.Equal(2, Hacking.ApCost(Real, novice));
    }

    [Fact]
    public void An_enemy_camera_spots_a_hostile_and_points_the_linked_at_it()
    {
        var scout = SurvivorUnit(1, new Pos(2, 5));
        var linked = EnemyUnit("linked", new Pos(10, 10));
        var sim = Arena(16, 16, [scout, linked], rows: Rows(16, 16, (8, 8, 15, 8)),
            setup: (d, s) => Hacking.Place(d, s, DeviceKind.Camera, new Pos(2, 1)));
        Assert.True(sim.Execute(new Move(1, scout.Id, new Pos(2, 4))).Ok);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Spotted && e.TargetId == scout.Id);
        Assert.Equal(AiMode.Hunting, linked.Ai.Mode);
        Assert.Equal(scout.Id, linked.Ai.TargetId);
    }

    [Fact]
    public void A_hacked_camera_sees_for_the_side_and_for_its_allies()
    {
        ZoneDevice camera = null!;
        var hacker = Hacker(new Pos(1, 1));
        var ally = SurvivorUnit(2, new Pos(1, 3));
        // A wall between the party and the room the camera watches.
        var husk = EnemyUnit("husk", new Pos(9, 2));
        var sim = Arena(12, 6, [hacker, ally, husk], data: Rolls(1), rows:
        [
            "......#.....",
            "......#.....",
            "......#.....",
            "......#.....",
            "......#.....",
            "............",
        ], setup: (d, s) => camera = Hacking.Place(d, s, DeviceKind.Camera, new Pos(6, 1)));
        Alliances.Merge(sim.State, 1, 2);
        var team = hacker.Side;
        Assert.False(Detection.SideSees(sim.Data, sim.State, team, husk));
        Assert.Equal(ScavengeError.TooFar, Hack(sim, hacker, camera).Error);
        hacker.Pos = new Pos(5, 1);
        Assert.True(Hack(sim, hacker, camera).Ok);

        Assert.Equal(team, sim.State.FindUnit(camera.UnitId!.Value)!.Side);
        Assert.True(Detection.SideSees(sim.Data, sim.State, team, husk), "the camera sees through to the other room for the team");
    }

    /// <summary>
    /// A hacked turret against a husk at (6,5); with <paramref name="penned"/> pylons all round the husk keep it on its tile
    /// (they block movement, not sight). The turret is hacked and has telegraphed its aim at the husk's tile.
    /// </summary>
    private static (ScavengeSim Sim, Unit Gun, Unit Husk, Unit Hacker) HackedTurret(bool penned)
    {
        ZoneDevice turret = null!;
        var hacker = Hacker(new Pos(1, 1));
        var husk = EnemyUnit("husk", new Pos(6, 5));
        string[]? rows = penned ? ["..........", "..........", "..........", "..........", ".....PPP..", ".....P.P..", ".....PPP..", ".........."] : null;
        var sim = Arena(10, 8, [hacker, husk], rows: rows, data: Rolls(1), setup: (d, s) => turret = Hacking.Place(d, s, DeviceKind.Turret, new Pos(2, 2)));
        var gun = sim.State.FindUnit(turret.UnitId!.Value)!;
        Assert.True(gun.HostileTo(hacker));
        Assert.False(gun.HostileTo(husk));

        turret.Progress.Add(new HackProgress(P1, hacker.Id, 2, 3));
        Assert.True(Hack(sim, hacker, turret).Ok);
        Assert.Equal(P1, gun.Side);
        Assert.False(gun.PlayerControlled, "a turret is the AI's to run, for its new side");
        // It telegraphs its aim at the husk's tile (a turret never moves, even with the husk in reach).
        ScavengeAi.TakeTurn(sim, gun);
        Assert.Equal(husk.Pos, Assert.Single(sim.State.Strikes, s => s.UnitId == gun.Id).Tile);
        return (sim, gun, husk, hacker);
    }

    [Fact]
    public void A_hacked_turret_fires_for_its_new_side_at_the_tile_it_aimed_at()
    {
        var (sim, gun, husk, hacker) = HackedTurret(penned: true);
        EndTurns(sim, 1);
        Assert.Equal(new Pos(6, 5), husk.Pos);
        Assert.Contains(sim.State.Events, e => e.UnitId == gun.Id && e.TargetId == husk.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == gun.Id && e.Type == ScavengeEventType.IntentFizzled);
        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == gun.Id && e.TargetId == hacker.Id);
    }

    [Fact]
    public void A_hacked_turrets_shot_falls_through_when_its_target_leaves_the_tile()
    {
        var (sim, gun, husk, hacker) = HackedTurret(penned: false);
        husk.Pos = new Pos(8, 7); // walked off before the turret's turn
        EndTurns(sim, 1);
        Assert.Contains(sim.State.Events, e => e.UnitId == gun.Id && e.Type == ScavengeEventType.IntentFizzled);
        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == gun.Id && e.TargetId == husk.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed
                                                     && e.Round < sim.State.Round);
        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == gun.Id && e.TargetId == hacker.Id);
    }

    [Fact]
    public void Shooting_a_turret_down_disables_the_device()
    {
        ZoneDevice turret = null!;
        var shooter = SurvivorUnit(1, new Pos(1, 1), weapon: "pistol");
        var sim = Arena(10, 8, [shooter], setup: (d, s) => turret = Hacking.Place(d, s, DeviceKind.Turret, new Pos(4, 1)));
        var gun = sim.State.FindUnit(turret.UnitId!.Value)!;
        Combat.ApplyDamage(sim, gun, BodyPart.Torso, 999);
        Assert.Equal(UnitStatus.Dead, gun.Status);
        Assert.True(turret.Disabled);
        // What is left is salvage from its loot table (never its own gun), on its tile.
        Assert.All(sim.State.Caches.SelectMany(c => c.Items), id => Assert.Contains(sim.Data.Loot.Tables["turret"].Entries.Concat(sim.Data.Loot.Tables["turret"].Guaranteed).Select(e => e.Item).Append("motion_sensor"), x => x == id));
        Assert.All(sim.State.Caches, c => Assert.Equal(new Pos(4, 1), c.Pos));
        Assert.Equal(ScavengeError.AlreadyHacked, sim.Execute(new Hack(1, shooter.Id, turret.Id)).Error);
    }

    [Fact]
    public void A_door_lets_only_its_controller_through()
    {
        ZoneDevice door = null!;
        var hacker = Hacker(new Pos(2, 1));
        var husk = EnemyUnit("husk", new Pos(2, 4));
        string[] rows =
        [
            "#####",
            "#...#",
            "##.##",
            "#...#",
            "#...#",
        ];
        var sim = Arena(5, 5, [hacker, husk], rows, data: Rolls(1), setup: (d, s) => door = Hacking.Place(d, s, DeviceKind.Door, new Pos(2, 2)));
        Assert.False(sim.Reachable(hacker).ContainsKey(new Pos(2, 2)));
        Assert.True(sim.Reachable(husk).ContainsKey(new Pos(2, 2)), "the Network's door lets its own through");

        door.Progress.Add(new HackProgress(P1, hacker.Id, 1, 2));
        Assert.True(Hack(sim, hacker, door).Ok);
        Assert.True(sim.Reachable(hacker).ContainsKey(new Pos(2, 3)), "through the door");
        Assert.False(sim.Reachable(husk).ContainsKey(new Pos(2, 2)), "locked out");
    }

    [Fact]
    public void A_hacked_locker_spills_its_loot()
    {
        ZoneDevice locker = null!;
        var hacker = Hacker(new Pos(1, 1));
        var sim = Arena(6, 6, [hacker], data: Rolls(1), setup: (d, s) => locker = Hacking.Place(d, s, DeviceKind.Locker, new Pos(2, 1), ["med_kit", "flare"]));
        Assert.True(Hack(sim, hacker, locker).Ok, "a locker takes one action");
        var cache = Assert.Single(sim.State.Caches);
        Assert.Equal(locker.Pos, cache.Pos);
        Assert.Equal(["med_kit", "flare"], cache.Items);
        Assert.True(sim.Execute(new PickUp(1, hacker.Id, cache.Id, "flare")).Ok);
    }

    [Fact]
    public void A_hacked_relay_cuts_the_linked_off_and_stops_reinforcements()
    {
        ZoneDevice relay = null!;
        var hacker = Hacker(new Pos(1, 1));
        var a = EnemyUnit("linked", new Pos(4, 4));
        var b = EnemyUnit("linked", new Pos(6, 4));
        var sim = Arena(16, 16, [hacker, a, b], data: Rolls(1), zoneId: "rail_depot",
            setup: (d, s) => relay = Hacking.Place(d, s, DeviceKind.Relay, new Pos(2, 1)));
        Assert.NotNull(Network.GroupOf(sim, a));
        relay.Progress.Add(new HackProgress(P1, hacker.Id, 2, 3));
        Assert.True(Hack(sim, hacker, relay).Ok);
        Assert.Null(Network.GroupOf(sim, a));
        Assert.True(Hacking.UplinkDown(sim.State));
    }

    [Fact]
    public void Override_turns_a_linked_for_a_while_but_never_a_husk()
    {
        var hacker = Hacker(new Pos(1, 1));
        var linked = EnemyUnit("linked", new Pos(2, 1));
        var husk = EnemyUnit("husk", new Pos(1, 2));
        var far = EnemyUnit("husk", new Pos(4, 4));
        var sim = Arena(8, 8, [hacker, linked, husk, far], data: Rolls(1));
        Assert.Equal(ScavengeError.NotHackable, sim.Execute(new Hack(1, hacker.Id, TargetId: linked.Id)).Error);
        hacker.Hooks = Real.HooksFor(["override"]);
        Assert.Equal(ScavengeError.NotHackable, sim.Execute(new Hack(1, hacker.Id, TargetId: husk.Id)).Error);

        Assert.True(sim.Execute(new Hack(1, hacker.Id, TargetId: linked.Id)).Ok);
        Assert.Equal(P1, linked.Side);
        Assert.False(linked.HostileTo(hacker));
        Assert.True(linked.HostileTo(husk));
        Assert.Null(Network.GroupOf(sim, linked));

        EndTurns(sim, 2); // it telegraphs a blow at a Husk, then strikes
        Assert.Contains(sim.State.Events, e => e.UnitId == linked.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed
                                               && sim.State.FindUnit(e.TargetId!.Value)!.Kind == UnitKind.Enemy);
        EndTurns(sim, Real.Rules.Hacking.OverrideRounds);
        Assert.Equal(Unit.EnemySide, linked.Side);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.OverrideEnded);
    }

    [Fact]
    public void Field_xp_from_hacks_comes_home_in_the_result()
    {
        ZoneDevice locker = null!;
        var hacker = Hacker(new Pos(1, 1));
        var sim = Arena(6, 6, [hacker], rows: ["E.....", "......", "......", "......", "......", "......"], data: Rolls(1),
            setup: (d, s) => locker = Hacking.Place(d, s, DeviceKind.Locker, new Pos(2, 1)));
        Assert.True(Hack(sim, hacker, locker).Ok);
        hacker.Pos = new Pos(0, 0);
        Assert.True(sim.Execute(new Extract(1, hacker.Id)).Ok);
        var back = Assert.Single(sim.ResultFor(1).Returned);
        Assert.Equal(Real.Rules.Hacking.XpOnSuccess, back.FieldXp);
        Assert.True(back.Survivor.Get(Skill.Hacking) > 0);
    }

    [Fact]
    public void Zones_place_devices_that_never_cut_off_the_map()
    {
        foreach (var zone in Real.Zones.Values)
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var sim = NewScavenge.Create(Real, zone.Id, seed, [Party(1)]);
                var state = sim.State;
                foreach (var (kind, range) in zone.Devices)
                    Assert.True(state.Devices.Count(d => d.Kind == kind) <= range[1]);
                var walker = state.Units.First(u => u.PlayerId == 1);
                var floor = state.Map.AllPositions.Where(p => Real.Terrain(state.Map[p]).Passable && state.DeviceAt(p) is null).ToHashSet();
                var seen = new HashSet<Pos> { walker.Pos };
                var queue = new Queue<Pos>([walker.Pos]);
                while (queue.TryDequeue(out var p))
                    foreach (var d in Pos.Orthogonal)
                        if (floor.Contains(p + d) && seen.Add(p + d)) queue.Enqueue(p + d);
                foreach (var exit in state.Map.Exits) Assert.Contains(exit, seen);
                foreach (var device in state.Devices.Where(d => d.Kind == DeviceKind.Camera))
                    Assert.Equal(Terrain.Wall, state.Map[device.Pos]);
                foreach (var device in state.Devices.Where(d => d.UnitId is not null))
                    Assert.Equal(UnitKind.Device, state.FindUnit(device.UnitId!.Value)!.Kind);
            }
        var placed = Real.Zones.Values.Sum(z => NewScavenge.Create(Real, z.Id, 3, [Party(1)]).State.Devices.Count);
        Assert.True(placed >= 10, $"only {placed} devices across the zones");
    }

    [Fact]
    public void Perks_reach_the_field_with_the_survivor()
    {
        var party = Party(1, 1);
        party.Members[0].Survivor.Perks.Add("steady_aim");
        party.Members[0].Survivor.Perks.Add("light_step");
        var sim = NewScavenge.Create(Real, "outskirts", 1, [party]);
        var unit = sim.State.Units.First(u => u.PlayerId == 1);
        Assert.Equal(0.05, unit.Hook(PerkHooks.AccuracyBonus), 6);
        Assert.Equal(5, unit.Hook(PerkHooks.StealthBonus), 6);
    }

    [Fact]
    public void Combat_and_scouting_perks_change_the_field_profile()
    {
        Unit With(params string[] perks)
        {
            var u = SurvivorUnit(weapon: "pipe", body: "rag_coat");
            u.Hooks = Real.HooksFor(perks);
            return u;
        }
        UnitProfile P(Unit u) => ProfileDeriver.Derive(Real, u);
        var plain = P(With());
        Assert.Equal(plain.Actions[0].Accuracy + 0.05, P(With("steady_aim")).Actions[0].Accuracy, 6);
        Assert.True(P(With("brawler")).Actions[0].MaxDamage > plain.Actions[0].MaxDamage);
        Assert.Equal(plain.Stealth + 5, P(With("light_step")).Stealth, 6);
        Assert.Equal(plain.Detection + 5, P(With("keen_eye")).Detection, 6);
        Assert.Equal(plain.Sight + 1, P(With("keen_eye")).Sight, 6);
        Assert.Equal(plain.CarryCapacity + 10, P(With("pack_mule")).CarryCapacity, 6);
        var braced = With("dug_in");
        braced.Stance = Stance.Brace;
        Assert.Equal(Real.Rules.Fog.Brace.CoverBonus + 0.15, P(braced).CoverBonus, 6);
    }

    [Fact]
    public void Medic_perks_heal_more_and_revive_stronger()
    {
        var medic = SurvivorUnit(1, new Pos(1, 1), pack: ["bandage", "med_kit"]);
        medic.Hooks = Real.HooksFor(["field_medic", "triage"]);
        var hurt = SurvivorUnit(1, new Pos(2, 1));
        var downed = SurvivorUnit(1, new Pos(1, 2));
        var sim = Arena(6, 6, [medic, hurt, downed]);
        hurt.Hp = 50;
        downed.Status = UnitStatus.Downed;
        downed.Hp = 0;
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "bandage", hurt.Id)).Ok);
        var heal = Real.Items["bandage"].Use!.Heal;
        Assert.Equal(50 + heal * 1.25, hurt.Hp, 6);
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "med_kit", downed.Id)).Ok);
        Assert.Equal(Real.Items["med_kit"].Use!.ReviveHp + 10, downed.Hp, 6);
    }

    [Fact]
    public void Sentinel_holds_an_extra_overwatch_reaction()
    {
        var watcher = SurvivorUnit(1, new Pos(1, 1), weapon: "pistol");
        watcher.Hooks = Real.HooksFor(["sentinel"]);
        var sim = Arena(6, 6, [watcher, SurvivorUnit(1, new Pos(1, 2)), EnemyUnit("husk", new Pos(5, 5))]);
        Assert.True(sim.Execute(new SetStance(1, watcher.Id, Stance.Overwatch)).Ok);
        Assert.Equal(3, watcher.Reactions);
    }

    [Fact]
    public void Saves_carry_devices_and_old_saves_load_without_them()
    {
        var sim = NewScavenge.Create(Real, "rail_depot", 4, [Party(1)]);
        Assert.NotEmpty(sim.State.Devices);
        sim.State.Devices[0].Progress.Add(new HackProgress(P1, 1, 1, 2));
        var json = ScavengeSave.Save(sim.State);
        var loaded = ScavengeSave.Load(Real, json);
        Assert.Equal(json, ScavengeSave.Save(loaded.State));

        var old = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        old["version"] = 4;
        var scavenge = old["scavenge"]!.AsObject();
        scavenge.Remove("devices");
        scavenge.Remove("marks");
        foreach (var u in scavenge["units"]!.AsArray()) u!.AsObject().Remove("hooks");
        Assert.Contains("Unsupported scavenge save version", Assert.Throws<System.Text.Json.JsonException>(() => ScavengeSave.Load(Real, old.ToJsonString())).Message); // old runs reset
    }

    /// <summary>Open rows with wall runs (x0, y0, x1, y1), inclusive, each a straight line.</summary>
    internal static string[] Rows(int width, int height, params (int X0, int Y0, int X1, int Y1)[] walls)
    {
        var grid = Enumerable.Range(0, height).Select(_ => Enumerable.Repeat('.', width).ToArray()).ToArray();
        foreach (var (x0, y0, x1, y1) in walls)
            for (var y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                for (var x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                    grid[y][x] = '#';
        return grid.Select(r => new string(r)).ToArray();
    }
}
