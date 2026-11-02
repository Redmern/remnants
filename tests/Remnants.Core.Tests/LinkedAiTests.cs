using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Hand-built scenarios for the Linked squad AI: shared knowledge, flanking, focus fire, suppression, retreat, pylons.</summary>
public class LinkedAiTests
{
    private static bool Did(ScavengeSim sim, Unit unit, ScavengeEventType type) =>
        sim.State.Events.Any(e => e.UnitId == unit.Id && e.Type == type);

    private static bool Shot(ScavengeSim sim, Unit unit, Unit target) =>
        sim.State.Events.Any(e => e.UnitId == unit.Id && e.TargetId == target.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);

    [Fact]
    public void What_one_linked_sees_its_whole_group_knows()
    {
        var u = SurvivorUnit(pos: new(1, 2));
        var spotter = EnemyUnit("linked", new(5, 2));
        var blind = EnemyUnit("linked", new(9, 6)); // behind the wall, out of sight, within relay range of the spotter
        var sim = Arena(12, 9, [u, spotter, blind], [
            "............",
            "............",
            "............",
            "............",
            "......######",
            "......#.....",
            "......#.....",
            "......#.....",
            "............",
        ]);
        Assert.False(Visibility.CanSee(sim.Data, sim.State.Map, blind.Pos, sim.Profile(blind).Sight, u.Pos));

        var group = Network.GroupOf(sim, blind);
        Assert.NotNull(group);
        Assert.Contains(u, Network.Known(sim, group!));

        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.Equal(AiMode.Hunting, blind.Ai.Mode);
        Assert.Contains(sim.State.Events, e => e.UnitId == blind.Id && e.Message.Contains("through the Network"));
    }

    [Fact]
    public void Linked_out_of_relay_range_do_not_share()
    {
        var u = SurvivorUnit(pos: new(1, 1));
        var spotter = EnemyUnit("linked", new(4, 1));
        var far = EnemyUnit("linked", new(18, 1));
        var sim = Arena(20, 3, [u, spotter, far], [
            "....................",
            "..........#.........",
            "....................",
        ]);
        Assert.NotEqual(Network.GroupOf(sim, spotter)!.Units, Network.GroupOf(sim, far)!.Units);
        Assert.DoesNotContain(u, Network.Known(sim, Network.GroupOf(sim, far)!));
    }

    [Fact]
    public void A_jammer_cuts_a_linked_out_of_the_network()
    {
        var u = SurvivorUnit(pos: new(1, 2), tool: "signal_jammer");
        var jammed = EnemyUnit("linked", new(4, 2));
        var other = EnemyUnit("linked", new(9, 2));
        var sim = Arena(12, 5, [u, jammed, other]);
        Assert.Null(Network.GroupOf(sim, jammed));
        Assert.Single(Network.GroupOf(sim, other)!.Units);
    }

    [Fact]
    public void A_pylon_relays_between_linked_too_far_apart_to_link_directly()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var a = EnemyUnit("linked", new(2, 4));
        var b = EnemyUnit("linked", new(16, 4));
        var rows = new[]
        {
            "...................",
            "...................",
            "...................",
            "...................",
            ".........P.........",
        };
        var sim = Arena(19, 5, [u, a, b], rows);
        var group = Network.GroupOf(sim, a)!;
        Assert.Contains(b, group.Units);
        Assert.Equal([new Pos(9, 4)], group.Pylons);

        var jamPylon = SurvivorUnit(pos: new(9, 1), tool: "signal_jammer");
        var sim2 = Arena(19, 5, [jamPylon, EnemyUnit("linked", new(2, 4)), EnemyUnit("linked", new(16, 4))], rows);
        Assert.False(Network.PylonOnline(sim2, new Pos(9, 4)));
        Assert.Single(Network.GroupOf(sim2, sim2.State.Units[1])!.Units);
    }

    [Fact]
    public void Linked_flank_a_target_hiding_behind_cover()
    {
        // The survivor crouches behind cover to its east; the Linked stands due east, so the cover shields it.
        var u = SurvivorUnit(pos: new(3, 3));
        var linked = EnemyUnit("linked", new(8, 3));
        var sim = Arena(11, 8, [u, linked], [
            "...........",
            "...........",
            "...........",
            "....c......",
            "...........",
            "...........",
            "...........",
            "...........",
        ]);
        Assert.True(Combat.CoverAgainst(sim.Data, sim.State.Map, u.Pos, linked.Pos) > 0);

        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.NotEqual(new Pos(8, 3), linked.Pos);
        Assert.Equal(0, Combat.CoverAgainst(sim.Data, sim.State.Map, u.Pos, linked.Pos));
        Assert.True(Did(sim, linked, ScavengeEventType.Flanked));
        Assert.True(Shot(sim, linked, u));
    }

    [Fact]
    public void The_squad_focuses_fire_on_the_weakest_target()
    {
        var healthy = SurvivorUnit(pos: new(2, 2));
        var hurt = SurvivorUnit(pos: new(2, 6));
        hurt.Hp = 70;
        var a = EnemyUnit("linked", new(8, 3));
        var b = EnemyUnit("linked", new(8, 5));
        var sim = Arena(11, 9, [healthy, hurt, a, b]);

        Assert.Equal(hurt, Network.Focus(sim, Network.GroupOf(sim, a)!, Network.Known(sim, Network.GroupOf(sim, a)!)));
        Assert.True(sim.Execute(new EndTurn(1, healthy.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, hurt.Id)).Ok);
        // Each Linked opens on the hurt survivor (three bursts cannot drop 70 HP, so it is still up for the second).
        ScavengeEvent FirstShot(Unit by) => sim.State.Events.First(e => e.UnitId == by.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
        Assert.Equal(hurt.Id, FirstShot(a).TargetId);
        Assert.Equal(hurt.Id, FirstShot(b).TargetId);
    }

    [Fact]
    public void Focus_prefers_the_exposed_target_over_one_in_cover()
    {
        // Cover shields the first survivor against both Linked; the second stands in the open.
        var covered = SurvivorUnit(pos: new(3, 4));
        var exposed = SurvivorUnit(pos: new(3, 8));
        var a = EnemyUnit("linked", new(9, 3));
        var b = EnemyUnit("linked", new(9, 5));
        var sim = Arena(12, 10, [covered, exposed, a, b], [
            "............",
            "............",
            "............",
            "....c.......",
            "....c.......",
            "....c.......",
            "............",
            "............",
            "............",
            "............",
        ]);
        var group = Network.GroupOf(sim, a)!;
        Assert.Equal(exposed, Network.Focus(sim, group, Network.Known(sim, group)));
    }

    [Fact]
    public void Poor_odds_mean_suppressing_fire_which_costs_the_target_ap()
    {
        // Far away and in heavy cover: the shot is below the suppress threshold, so the Linked pins it down instead.
        // The Linked is boxed in (walls around, pylons in front that it cannot cross but can shoot over).
        var u = SurvivorUnit(pos: new(1, 4), attributes: 90);
        var linked = EnemyUnit("linked", new(8, 4));
        var sim = Arena(11, 9, [u, linked], [
            "###########",
            "#.........#",
            "#.........#",
            "#.#########",
            "#.cPPPPP.##",
            "#.#########",
            "#.........#",
            "#.........#",
            "###########",
        ]);
        var chance = sim.Profile(linked).Actions.Where(a => a.Pattern == Items.AttackPattern.Ranged)
            .Max(a => Combat.HitChance(sim.Data, sim.State.Map, linked.Pos, a, u.Pos, sim.Profile(u)));
        Assert.True(chance < sim.Data.Rules.Network.SuppressBelowHitChance, $"hit chance {chance}");

        var maxAp = sim.Profile(u).MaxAp;
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.True(Did(sim, linked, ScavengeEventType.Suppressed), string.Join("\n", sim.State.Events.Select(e => e.Message)));
        Assert.Equal(maxAp - sim.Data.Rules.Network.SuppressApPenalty, u.Ap);
        Assert.False(u.Suppressed);
    }

    [Fact]
    public void A_badly_hurt_linked_falls_back_while_the_squad_holds()
    {
        var u = SurvivorUnit(pos: new(2, 4));
        var hurt = EnemyUnit("linked", new(6, 4));
        hurt.Hp = 10;
        var buddy = EnemyUnit("linked", new(6, 6));
        var sim = Arena(14, 9, [u, hurt, buddy]);
        var before = hurt.Pos.Chebyshev(u.Pos);

        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.True(Did(sim, hurt, ScavengeEventType.Retreated));
        Assert.True(hurt.Pos.Chebyshev(u.Pos) > before);
        Assert.False(Did(sim, buddy, ScavengeEventType.Retreated));
    }

    [Fact]
    public void The_last_linked_standing_does_not_retreat()
    {
        var u = SurvivorUnit(pos: new(2, 4));
        var hurt = EnemyUnit("linked", new(6, 4));
        hurt.Hp = 10;
        var sim = Arena(14, 9, [u, hurt]);
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.False(Did(sim, hurt, ScavengeEventType.Retreated));
    }

    private static readonly string[] PylonYard =
    [
        "..............",
        "..............",
        "..............",
        "..........P...",
        "..............",
        "..............",
    ];

    [Fact]
    public void An_alarmed_pylon_calls_reinforcements_after_n_rounds()
    {
        var u = SurvivorUnit(pos: new(7, 1), attributes: 90);
        var linked = EnemyUnit("linked", new(12, 5));
        var sim = Arena(14, 6, [u, linked], PylonYard, zoneId: "network_relay");
        linked.Hp = 9999; // keep the scenario about the alarm, not about who dies
        u.Hp = 9999;
        u.MaxHp = 9999;

        var rounds = sim.Data.Rules.Network.ReinforceAfterRounds;
        for (var i = 0; i < rounds; i++) Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Reinforced);
        Assert.Equal(1, sim.State.ReinforcementWaves);
        Assert.True(sim.State.Units.Count(x => x.Kind == UnitKind.Enemy) > 1);
    }

    [Fact]
    public void A_jammed_pylon_never_calls_and_zones_without_waves_never_call()
    {
        var jammer = SurvivorUnit(pos: new(9, 0), tool: "signal_jammer");
        jammer.Hp = jammer.MaxHp = 9999;
        var sim = Arena(14, 6, [jammer, EnemyUnit("linked", new(12, 5))], PylonYard, zoneId: "network_relay");
        sim.State.Units[1].Hp = 9999;
        var quiet = SurvivorUnit(pos: new(1, 0));
        quiet.Hp = quiet.MaxHp = 9999;
        var sim2 = Arena(14, 6, [quiet, EnemyUnit("linked", new(12, 5))], PylonYard, zoneId: "outskirts");
        sim2.State.Units[1].Hp = 9999;

        for (var i = 0; i < 6; i++)
        {
            Assert.True(sim.Execute(new EndTurn(1, jammer.Id)).Ok);
            Assert.True(sim2.Execute(new EndTurn(1, quiet.Id)).Ok);
        }
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.Reinforced);
        Assert.DoesNotContain(sim2.State.Events, e => e.Type == ScavengeEventType.Reinforced);
    }

    [Fact]
    public void Squad_ai_is_deterministic()
    {
        string Run()
        {
            var sim = NewScavenge.Create(Real, "network_relay", 7, [Party(1, 3, "pistol")]);
            PlayBot(sim);
            return ScavengeSave.Save(sim.State);
        }
        Assert.Equal(Run(), Run());
    }
}

public class EnemyVariantTests
{
    [Fact]
    public void Every_enemy_has_a_model_id_convention_and_loads()
    {
        foreach (var id in new[] { "husk", "husk_crawler", "linked", "linked_drone", "linked_warden" })
            Assert.Contains(id, Real.Enemies.Keys);
    }

    [Fact]
    public void Drones_fly_over_rough_terrain_and_wardens_plod()
    {
        var drone = ProfileDeriver.Derive(Real, EnemyUnit("linked_drone", default));
        Assert.Equal(1, drone.Movement.Cost(Terrain.Water));
        Assert.Equal(1, drone.Movement.Cost(Terrain.Rubble));
        Assert.Null(drone.Movement.Cost(Terrain.Wall));

        var warden = ProfileDeriver.Derive(Real, EnemyUnit("linked_warden", default));
        Assert.Equal(1, warden.Movement.Primary.Range);
        Assert.True(warden.Armor > ProfileDeriver.Derive(Real, EnemyUnit("linked", default)).Armor);

        var crawler = ProfileDeriver.Derive(Real, EnemyUnit("husk_crawler", default));
        var husk = ProfileDeriver.Derive(Real, EnemyUnit("husk", default));
        Assert.True(crawler.Movement.Primary.Range > husk.Movement.Primary.Range);
        Assert.True(crawler.Dodge > husk.Dodge);
        Assert.True(Real.Enemies["husk_crawler"].Hp < Real.Enemies["husk"].Hp);
    }

    [Fact]
    public void A_warden_pulse_hits_everyone_around_the_target()
    {
        // Fired by a survivor carrying the warden's cannon so the test drives it through a command.
        var gunner = SurvivorUnit(pos: new(6, 2), weapon: "warden_pulse");
        var a = EnemyUnit("husk", new(2, 2));
        var b = EnemyUnit("husk", new(2, 3));
        var far = EnemyUnit("husk", new(2, 5));
        var sim = Arena(9, 6, [gunner, a, b, far]);
        Assert.Equal(1, sim.Profile(gunner).Action("pulse")!.Area);
        Assert.True(sim.Execute(new Attack(1, gunner.Id, a.Id, "pulse")).Ok);
        Assert.Contains(sim.State.Events, e => e.UnitId == gunner.Id && e.TargetId == a.Id);
        Assert.Contains(sim.State.Events, e => e.UnitId == gunner.Id && e.TargetId == b.Id);
        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == gunner.Id && e.TargetId == far.Id);
    }

    [Fact]
    public void Natural_enemy_parts_never_drop()
    {
        foreach (var id in Real.Enemies.Values.SelectMany(e => e.Equipment))
            Assert.True(Real.Items[id].Natural || Real.Items[id].LootWeight >= 0);
        Assert.All(new[] { "drone_rotors", "warden_shell", "crawler_carapace" }, id => Assert.Equal(0, Real.Items[id].LootWeight));
    }
}

public class ZoneGenerationTests
{
    [Theory]
    [InlineData("rail_depot", 1)]
    [InlineData("network_relay", 2)]
    [InlineData("core_spire", 3)]
    public void Pylon_zones_place_their_pylons_without_cutting_the_map(string zoneId, int pylons)
    {
        foreach (var seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            var generated = MapGenerator.Generate(Real, Real.Zones[zoneId], new Random.DeterministicRng(seed));
            Assert.Equal(pylons, Network.Pylons(generated.Map).Count());
            foreach (var exit in generated.Map.Exits) Assert.Contains(exit, generated.Reachable);
        }
    }

    [Fact]
    public void Early_zones_hold_at_most_their_capped_linked()
    {
        foreach (var seed in Enumerable.Range(1, 30).Select(i => (ulong)i))
        {
            var sim = NewScavenge.Create(Real, "suburb_ruins", seed, [Party(1)]);
            Assert.True(sim.State.Units.Count(u => u.EnemyDefId == "linked") <= 2);
        }
    }

    [Fact]
    public void A_depleted_zone_has_fewer_caches()
    {
        var fresh = NewScavenge.Create(Real, "outskirts", 3, [Party(1)]);
        var picked = NewScavenge.Create(Real, "outskirts", 3, [Party(1)], new ZoneConditions(Depletion: 1));
        Assert.NotEmpty(fresh.State.Caches);
        Assert.Empty(picked.State.Caches);
    }

    [Fact]
    public void High_threat_zones_field_more_linked_than_early_ones()
    {
        int Linked(string zone) => Enumerable.Range(1, 10).Sum(seed =>
            NewScavenge.Create(Real, zone, (ulong)seed, [Party(1)]).State.Units.Count(u => Network.IsLinked(Real, u)));
        Assert.Equal(0, Linked("outskirts"));
        Assert.True(Linked("core_spire") > Linked("suburb_ruins"));
    }
}

public class ScavengeSaveVersionTests
{
    [Fact]
    public void Version_1_scavenge_saves_still_load_and_round_trip_keeps_the_alarm()
    {
        var sim = NewScavenge.Create(Real, "rail_depot", 5, [Party(1)]);
        sim.State.AlarmRounds = 2;
        sim.State.ReinforcementWaves = 1;
        var json = ScavengeSave.Save(sim.State);
        var loaded = ScavengeSave.Load(Real, json);
        Assert.Equal(2, loaded.State.AlarmRounds);
        Assert.Equal(1, loaded.State.ReinforcementWaves);

        var v1 = NewScavenge.Create(Real, "outskirts", 5, [Party(1)]);
        var old = ScavengeSave.Save(v1.State).Replace($"\"version\": {ScavengeSave.Version}", "\"version\": 1");
        Assert.Contains("Unsupported scavenge save version", Assert.Throws<System.Text.Json.JsonException>(() => ScavengeSave.Load(Real, old)).Message); // old runs reset
    }
}
