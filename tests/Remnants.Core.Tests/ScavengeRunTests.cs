using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class ScavengeDataTests
{
    [Fact]
    public void Real_scavenge_data_loads()
    {
        Assert.True(Real.Items.Count >= 10);
        Assert.Contains("husk", Real.Enemies.Keys);
        Assert.Contains("linked", Real.Enemies.Keys);
        Assert.Contains("outskirts", Real.Zones.Keys);
        Assert.All(Enum.GetValues<Terrain>(), t => Assert.True(Real.Rules.Terrain.ContainsKey(t)));
    }

    private static string File(string name) => System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));

    private static GameDataException Broken(string? items = null, string? enemies = null, string? zones = null, string? rules = null) =>
        Assert.Throws<GameDataException>(() => ScavengeDataLoader.LoadFromJson(
            items ?? File("items.json"), enemies ?? File("enemies.json"), zones ?? File("zones.json"), rules ?? File("scavenge.json")));

    [Fact]
    public void Zone_with_unknown_enemy_is_rejected()
    {
        Assert.Contains("ghoul", Broken(zones: File("zones.json").Replace("\"id\": \"husk\"", "\"id\": \"ghoul\"")).Message);
    }

    [Fact]
    public void Enemy_with_unknown_gear_is_rejected()
    {
        Assert.Contains("husk_claws", Broken(items: File("items.json").Replace("\"husk_claws\"", "\"husk_talons\"")).Message);
    }

    [Fact]
    public void Undefined_terrain_is_rejected()
    {
        Assert.Contains("Water", Broken(rules: System.Text.RegularExpressions.Regex.Replace(File("scavenge.json"), "\"Water\":[^}]*},", "")).Message);
    }

    [Fact]
    public void Weapon_without_actions_is_rejected()
    {
        Assert.Contains("weapon", Broken(items: """[{ "id": "stick", "name": "Stick", "kind": "Weapon" }]""").Message);
    }
}

public class MapGeneratorTests
{
    public static IEnumerable<object[]> ZonesAndSeeds() =>
        from zone in new[] { "outskirts", "suburb_ruins", "network_relay" }
        from seed in new ulong[] { 1, 2, 3, 42, 1234 }
        select new object[] { zone, seed };

    [Theory]
    [MemberData(nameof(ZonesAndSeeds))]
    public void Generated_zone_is_escapable_and_populated_by_its_template(string zoneId, ulong seed)
    {
        var zone = Real.Zones[zoneId];
        var sim = NewScavenge.Create(Real, zoneId, seed, [Party(1, members: 3)]);
        var state = sim.State;
        var map = state.Map;

        Assert.Equal((zone.Width, zone.Height), (map.Width, map.Height));
        Assert.InRange(map.Exits.Count, 1, zone.Exits);
        Assert.All(map.Exits, e => Assert.Equal(0, MapGenerator.Depth(map, zone.ExitEdge, e)));

        var party = state.Units.Where(u => u.PlayerId == 1).ToList();
        Assert.All(party, u => Assert.True(MapGenerator.Depth(map, zone.SpawnEdge, u.Pos) <= 2));
        var reachable = MapGenerator.Flood(Real, map, party[0].Pos);
        Assert.All(map.Exits, e => Assert.Contains(e, reachable));

        var enemies = state.Units.Where(u => u.Kind == UnitKind.Enemy).ToList();
        Assert.InRange(enemies.Count, zone.Enemies[0], zone.Enemies[1]);
        Assert.All(enemies, e => Assert.Contains(e.EnemyDefId, zone.EnemyMix.Select(m => m.Id)));
        Assert.All(state.Caches, c => Assert.All(c.Items, i => Assert.True(Real.Items[i].Tier <= zone.LootTier)));
        Assert.All(state.Caches, c => Assert.Contains(c.Pos, reachable));
        Assert.InRange(state.Units.Count(u => u.Kind == UnitKind.Npc), zone.Npcs[0], zone.Npcs[1]);
        Assert.Equal(state.Units.Count, state.Units.Select(u => u.Pos).Distinct().Count());
    }

    [Fact]
    public void Same_seed_same_map_different_seed_different_map()
    {
        string Map(ulong seed) => string.Join("", MapGenerator.Generate(Real, Real.Zones["suburb_ruins"], new DeterministicRng(seed)).Map.Tiles);
        Assert.Equal(Map(5), Map(5));
        Assert.NotEqual(Map(5), Map(6));
    }

    [Fact]
    public void Setup_is_validated()
    {
        Assert.Throws<ArgumentException>(() => NewScavenge.Create(Real, "moon", 1, [Party(1)]));
        Assert.Throws<ArgumentException>(() => NewScavenge.Create(Real, "outskirts", 1, []));
        Assert.Throws<ArgumentException>(() => NewScavenge.Create(Real, "outskirts", 1, [Party(1), Party(1)]));
        Assert.Throws<ArgumentException>(() => NewScavenge.Create(Real, "outskirts", 1, [Party(1, weapon: "padded_vest")]));
    }

    [Fact]
    public void Survivors_are_copied_in_and_the_colony_records_stay_untouched()
    {
        var setup = Party(1, members: 1);
        var survivor = setup.Members[0].Survivor;
        var sim = NewScavenge.Create(Real, "outskirts", 1, [setup]);
        var unit = sim.State.Units.Single(u => u.PlayerId == 1);
        Combat.ApplyDamage(sim, unit, BodyPart.LeftArm, 10);
        Assert.Equal(PartCondition.Healthy, survivor.Body[BodyPart.LeftArm].Condition);
        Assert.Equal(survivor.Name, unit.Name);
    }
}

public class ScavengeDeterminismAndSaveTests
{
    private static ScavengeSim Played(ulong seed, string zone = "suburb_ruins", int commands = 2000)
    {
        var sim = NewScavenge.Create(Real, zone, seed, [Party(1, members: 3), Party(2, members: 2, weapon: "pistol")]);
        PlayBot(sim, commands);
        return sim;
    }

    [Fact]
    public void Same_seed_and_commands_give_identical_state()
    {
        var a = Played(21);
        var b = Played(21);
        Assert.True(a.State.Finished);
        Assert.Equal(ScavengeSave.Save(a.State), ScavengeSave.Save(b.State));
        Assert.NotEqual(ScavengeSave.Save(a.State), ScavengeSave.Save(Played(22).State));
    }

    [Fact]
    public void Replaying_the_recorded_commands_reproduces_the_game()
    {
        var original = NewScavenge.Create(Real, "network_relay", 8, [Party(1, members: 3)]);
        var commands = PlayBot(original);

        var replay = NewScavenge.Create(Real, "network_relay", 8, [Party(1, members: 3)]);
        foreach (var c in commands) Assert.True(replay.Execute(c).Ok);
        Assert.Equal(ScavengeSave.Save(original.State), ScavengeSave.Save(replay.State));
    }

    [Fact]
    public void Save_load_round_trip_preserves_state()
    {
        var sim = Played(4, commands: 25);
        var json = ScavengeSave.Save(sim.State);
        var loaded = ScavengeSave.Load(Real, json);
        Assert.Equal(json, ScavengeSave.Save(loaded.State));
        Assert.Equal(sim.State.Map.Tiles, loaded.State.Map.Tiles);
        Assert.Equal(sim.State.CurrentUnitId, loaded.State.CurrentUnitId);
    }

    [Fact]
    public void Loaded_game_continues_exactly_like_the_original()
    {
        var original = Played(4, commands: 25);
        var loaded = ScavengeSave.Load(Real, ScavengeSave.Save(original.State));

        PlayBot(original);
        PlayBot(loaded);

        Assert.Equal(ScavengeSave.Save(original.State), ScavengeSave.Save(loaded.State));
        Assert.Equal(original.Rng.NextULong(), loaded.Rng.NextULong());
    }

    [Fact]
    public void Wrong_version_and_unknown_definitions_are_rejected()
    {
        var json = ScavengeSave.Save(Played(4, commands: 5).State);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ScavengeSave.Load(Real, json.Replace("\"version\": 1", "\"version\": 99")));
        var ex = Assert.ThrowsAny<System.Text.Json.JsonException>(() => ScavengeSave.Load(Real, json.Replace("\"padded_vest\"", "\"kevlar\"")));
        Assert.Contains("kevlar", ex.Message);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ScavengeSave.Load(Real, json.Replace("\"zoneId\": \"suburb_ruins\"", "\"zoneId\": \"moon\"")));
    }

    [Fact]
    public void A_full_run_produces_a_result_for_the_colony()
    {
        var sim = Played(21);
        foreach (var playerId in new[] { 1, 2 })
        {
            var result = sim.ResultFor(playerId);
            Assert.True(result.Complete);
            var members = playerId == 1 ? 3 : 2;
            Assert.Equal(members, result.Returned.Count + result.Deaths.Count);
            Assert.All(result.Returned, r => Assert.InRange(r.Survivor.Health, 1, Remnants.Core.Survivors.Survivor.MaxHealth));
        }
    }
}
