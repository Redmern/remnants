using Remnants.Core.Colony;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class LootTableTests
{
    [Fact]
    public void The_real_tables_load_clean_and_every_enemy_has_one()
    {
        Assert.Empty(Real.LootWarnings);
        foreach (var e in Real.Enemies.Values) Assert.True(Real.Loot.Find(e.Loot) is not null, e.Id);
        foreach (var n in Real.Salvage.Nodes) Assert.NotNull(Real.Loot.Find(n.Table));
    }

    [Fact]
    public void Missing_items_and_tables_are_skipped_with_a_warning_and_extensions_merge()
    {
        const string main = """{ "tables": [ { "id": "t", "rolls": [1, 1], "entries": [ { "item": "scrap" } ] } ] }""";
        const string feature = """
            { "tables": [ { "id": "u", "entries": [ { "item": "not_yet_herb" }, { "item": "cloth" } ] } ],
              "extend": [ { "table": "t", "entries": [ { "item": "cloth" }, { "item": "engine_block" } ] }, { "table": "nope", "entries": [] } ] }
            """;
        var data = ScavengeDataLoader.WithLoot(Real, [main, feature], null);
        Assert.Equal(["scrap", "cloth"], data.Loot.Tables["t"].Entries.Select(e => e.Item));
        Assert.Equal(["cloth"], data.Loot.Tables["u"].Entries.Select(e => e.Item));
        Assert.Contains(data.LootWarnings, w => w.Contains("not_yet_herb"));
        Assert.Contains(data.LootWarnings, w => w.Contains("engine_block"));
        Assert.Contains(data.LootWarnings, w => w.Contains("'nope'"));
        // Enemies naming a table that is not there drop only their schematics, with a warning.
        Assert.Contains(data.LootWarnings, w => w.Contains("Enemy 'husk'"));
    }

    [Fact]
    public void Most_husk_kills_leave_something_and_harder_zones_leave_more()
    {
        var husk = Real.Loot.Tables["husk"];
        var rng = new DeterministicRng(7);
        var empty = 0;
        double items = 0;
        const int kills = 2000;
        for (var i = 0; i < kills; i++)
        {
            var drop = Real.Loot.Roll(Real, husk, new LootContext(1), rng);
            if (drop.Count == 0) empty++;
            items += drop.Count;
        }
        Assert.InRange(empty / (double)kills, 0.15, 0.25);
        Assert.InRange(items / kills, Real.Loot.Expected(husk, new LootContext(1)).Values.Sum() * 0.9, Real.Loot.Expected(husk, new LootContext(1)).Values.Sum() * 1.1);
        Assert.True(Real.Loot.Expected(husk, new LootContext(3)).Values.Sum() > 1.3 * Real.Loot.Expected(husk, new LootContext(1)).Values.Sum());
        // Night and the Scavenger perk add picks.
        Assert.True(Real.Loot.Expected(husk, new LootContext(1, ExtraRolls: 0.3)).Values.Sum() > Real.Loot.Expected(husk, new LootContext(1)).Values.Sum());
    }

    [Fact]
    public void Gear_drops_at_a_quality_and_guaranteed_entries_always_come()
    {
        var rng = new DeterministicRng(3);
        var pistols = new HashSet<string>();
        for (var i = 0; i < 300; i++)
        {
            var drop = Real.Loot.Roll(Real, Real.Loot.Tables["cole"], new LootContext(1), rng);
            Assert.Contains("med_kit", drop);
            Assert.Equal(2, drop.Count(id => id == "canned_food"));
            pistols.UnionWith(drop.Where(id => id.StartsWith("pistol", StringComparison.Ordinal)));
        }
        Assert.Equal(["pistol", "pistol@fine"], pistols.OrderBy(p => p, StringComparer.Ordinal));
        Assert.All(pistols, id => Assert.True(Real.Items.ContainsKey(id)));
    }
}

public class FieldLootTests
{
    private static (ScavengeSim Sim, Unit Killer, Unit Husk) HuskArena(ulong seed = 1, params string[] pack)
    {
        var killer = SurvivorUnit(1, new Pos(0, 0), weapon: "pipe", pack: pack);
        var husk = EnemyUnit("husk", new Pos(1, 0));
        var sim = Arena(6, 4, [killer, husk], seed: seed);
        return (sim, killer, husk);
    }

    [Fact]
    public void A_kill_leaves_a_named_pile_and_a_log_line_on_its_tile()
    {
        for (ulong seed = 1; seed < 50; seed++)
        {
            var (sim, killer, husk) = HuskArena(seed);
            Combat.ApplyDamage(sim, husk, BodyPart.Torso, 999, attacker: killer);
            if (sim.State.Caches.Count == 0) continue;
            var pile = Assert.Single(sim.State.Caches);
            Assert.Equal((LootPileKind.Remains, "Husk", husk.Pos), (pile.Kind, pile.Source, pile.Pos));
            var line = sim.State.Events.Last(e => e.Type == ScavengeEventType.LootDropped);
            Assert.StartsWith("Husk dropped: ", line.Message);
            Assert.Equal(husk.Pos, line.Pos);
            Assert.Null(line.UnitId); // the tile tells who hears of it, not the dead unit
            return;
        }
        Assert.Fail("No husk dropped anything in 49 kills.");
    }

    [Fact]
    public void Loot_rolls_never_shift_the_combat_rng()
    {
        var (withLoot, k1, h1) = HuskArena(5);
        var bare = WithLoot(Real, """{ "tables": [] }""");
        var killer = SurvivorUnit(1, new Pos(0, 0), weapon: "pipe");
        var husk = EnemyUnit("husk", new Pos(1, 0));
        var without = Arena(6, 4, [killer, husk], seed: 5, data: bare);
        Combat.ApplyDamage(withLoot, h1, BodyPart.Torso, 999, attacker: k1);
        Combat.ApplyDamage(without, husk, BodyPart.Torso, 999, attacker: killer);
        Assert.Equal(withLoot.Rng.State, without.Rng.State);

        // Same seed, same loot.
        var (again, k2, h2) = HuskArena(5);
        Combat.ApplyDamage(again, h2, BodyPart.Torso, 999, attacker: k2);
        Assert.Equal(withLoot.State.Caches.SelectMany(c => c.Items), again.State.Caches.SelectMany(c => c.Items));
    }

    [Fact]
    public void Take_all_takes_what_fits_lightest_first_for_one_pickup()
    {
        var unit = SurvivorUnit(1, new Pos(0, 0));
        var sim = Arena(4, 3, [unit]);
        var pile = FieldLoot.Pile(sim.State, new Pos(1, 0), LootPileKind.Remains, "Husk 1");
        var room = sim.Profile(unit).CarryCapacity - sim.Profile(unit).Load;
        pile.Items.AddRange(["bandage", "scrap", "concrete_chunk"]);
        pile.Items.AddRange(Enumerable.Repeat("stone", (int)(room / 3) + 1));
        var ap = unit.Ap;

        var result = sim.Execute(new TakeAll(1, unit.Id, pile.Id));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(ap - Real.Rules.Turn.PickUpApCost, unit.Ap);
        Assert.Contains("bandage", unit.Pack);
        Assert.Contains("scrap", unit.Pack);
        Assert.True(sim.Profile(unit).Load <= sim.Profile(unit).CarryCapacity + 1e-9);
        Assert.NotEmpty(pile.Items); // too heavy stays
        Assert.Contains(sim.State.Caches, c => c.Id == pile.Id);
    }

    [Fact]
    public void Dropped_items_can_be_picked_up_again_and_a_stash_goes_into_the_result()
    {
        var unit = SurvivorUnit(1, new Pos(0, 0), pack: ["stone", "stone", "wood", "bandage"]);
        var sim = Arena(4, 3, [unit], rows: ["E...", "....", "...."]);
        Assert.True(sim.Execute(new DropItem(1, unit.Id, "wood")).Ok);
        var dropped = Assert.Single(sim.State.Caches);
        Assert.Equal(LootPileKind.Dropped, dropped.Kind);

        Assert.True(sim.Execute(new StashItem(1, unit.Id, "stone")).Ok);
        Assert.True(sim.Execute(new StashItem(1, unit.Id, "stone")).Ok);
        var stash = Assert.Single(sim.State.Caches, c => c.Kind == LootPileKind.Stash);
        Assert.Equal(["stone", "stone"], stash.Items);
        Assert.Equal(1, stash.PlayerId);
        Assert.Equal(["stone", "stone"], sim.ResultFor(1).Stash);

        unit.Pos = new Pos(1, 1);
        Assert.Equal(ScavengeError.NotOnExit, sim.Execute(new StashItem(1, unit.Id, "bandage")).Error);
    }

    [Fact]
    public void An_exit_stash_waits_in_the_zone_for_the_next_run()
    {
        var sim = LoopTestData.Colony();
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Ok);
        while (sim.State.Expedition!.PhaseAt(sim.State.Tick) != ExpeditionPhase.InZone) sim.Tick();
        var run = sim.OpenExpeditionScavenge();
        var unit = run.State.Units.Single(u => u.Kind == UnitKind.Survivor);
        unit.Pack.AddRange(["stone", "wood"]);
        unit.Pos = run.State.Map.Exits.OrderBy(run.State.Map.Index).First();
        Assert.True(run.Execute(new StashItem(unit.PlayerId!.Value, unit.Id, "stone")).Ok);
        Assert.True(run.Execute(new Extract(unit.PlayerId!.Value, unit.Id)).Ok);
        var result = run.ResultFor(unit.PlayerId!.Value);
        Assert.Equal(["stone"], result.Stash);
        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);
        Assert.Equal(["stone"], sim.State.Zones["outskirts"].Stash);

        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Ok);
        while (sim.State.Expedition!.PhaseAt(sim.State.Tick) != ExpeditionPhase.InZone) sim.Tick();
        var next = sim.OpenExpeditionScavenge();
        var stash = Assert.Single(next.State.Caches, c => c.Kind == LootPileKind.Stash);
        Assert.Equal(["stone"], stash.Items);
        Assert.True(next.State.Map.IsExit(stash.Pos));
    }

    [Fact]
    public void Raid_spoils_go_into_the_stockpile_and_inventory()
    {
        var sim = LoopTestData.Colony();
        var scrap = sim.State.Stockpile.Get("scrap");
        var spoils = FieldLoot.RaidSpoils(sim, ["tally_scav", "tally_scav", "tally_scav", "cole"]);
        Assert.NotEmpty(spoils);
        Assert.Contains("med_kit", spoils.Keys);
        Assert.True(sim.State.Stockpile.Get("scrap") > scrap);
        Assert.True(sim.State.Inventory.Count("med_kit") >= 1);
    }
}

public class SalvageTests
{
    private static (ScavengeSim Sim, Unit Worker, SalvageNode Node) NodeArena(string kind, params string[] pack)
    {
        var worker = SurvivorUnit(1, new Pos(1, 1), pack: pack);
        SalvageNode node = null!;
        var sim = Arena(6, 4, [worker], rows: ["......", "......", ".r....", "......"],
            setup: (d, s) => node = FieldSalvage.Add(d, s, d.Salvage.Node(kind)!, new Pos(1, 2), new DeterministicRng(1)));
        return (sim, worker, node);
    }

    private static void NextTurn(ScavengeSim sim, Unit unit) => unit.Ap = sim.Profile(unit).MaxAp;

    [Fact]
    public void Salvaging_takes_turns_yields_into_the_pack_and_depletes_the_node()
    {
        var (sim, worker, node) = NodeArena("rubble");
        var def = Real.Salvage.Node("rubble")!;
        node.LotsLeft = 1;
        var mining = worker.Get(Skill.Mining);

        var first = sim.Execute(new Salvage(1, worker.Id, node.Id));
        Assert.True(first.Ok, first.Message);
        Assert.Equal(1, node.Progress);
        Assert.Equal(1, node.LotsLeft);
        Assert.True(worker.Get(Skill.Mining) > mining);
        Assert.True(worker.FieldXp > 0);

        NextTurn(sim, worker);
        var pack = worker.Pack.Count;
        Assert.True(sim.Execute(new Salvage(1, worker.Id, node.Id)).Ok);
        Assert.True(node.Depleted);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Salvaged);
        Assert.True(worker.Pack.Count >= pack || sim.State.Caches.Count > 0);

        NextTurn(sim, worker);
        Assert.Equal(ScavengeError.NodeDepleted, sim.Execute(new Salvage(1, worker.Id, node.Id)).Error);
        Assert.Equal(2, def.Work);
    }

    [Fact]
    public void A_tool_halves_the_work_and_salvage_is_loud()
    {
        var (sim, worker, node) = NodeArena("rubble", "pickaxe");
        var husk = EnemyUnit("husk", new Pos(4, 3));
        husk.Id = sim.State.NextUnitId++;
        sim.State.Units.Add(husk);
        var lots = node.LotsLeft;
        Assert.True(sim.Execute(new Salvage(1, worker.Id, node.Id)).Ok);
        Assert.Equal(lots - 1, node.LotsLeft); // 1 + pickaxe 1 = the 2 work points of a lot
        Assert.Equal(AiMode.Hunting, husk.Ai.Mode); // heard it
    }

    [Fact]
    public void Too_much_to_carry_is_left_on_the_node_and_extras_hooks_add_parts()
    {
        var (sim, worker, node) = NodeArena("wrecked_car", Enumerable.Repeat("stone", 7).ToArray());
        var room = sim.Profile(worker).CarryCapacity - sim.Profile(worker).Load;
        worker.Pack.AddRange(Enumerable.Repeat("scrap", (int)Math.Floor(room)));
        node.Progress = 1;
        var parts = FieldSalvage.Extras["wrecked_car"];
        FieldSalvage.Extras["wrecked_car"] = (_, _, _) => ["electronics"];
        try
        {
            Assert.True(sim.Execute(new Salvage(1, worker.Id, node.Id)).Ok);
        }
        finally
        {
            FieldSalvage.Extras["wrecked_car"] = parts;
        }
        var pile = Assert.Single(sim.State.Caches);
        Assert.Equal(node.Pos, pile.Pos);
        Assert.Contains("electronics", pile.Items.Concat(worker.Pack));
        Assert.True(sim.Profile(worker).Load <= sim.Profile(worker).CarryCapacity + 1e-9);
    }

    [Fact]
    public void Zones_get_their_nodes_without_changing_the_rest_of_the_map()
    {
        var with = NewScavenge.Create(Real, "outskirts", 42, [Party(1)]);
        var bare = WithLoot(Real, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "loot_tables.json")), null);
        var without = NewScavenge.Create(bare, "outskirts", 42, [Party(1)]);
        var zone = Real.Salvage.ZoneOf("outskirts");
        Assert.InRange(with.State.SalvageNodes.Count, 1, zone.Count[1]);
        Assert.Empty(without.State.SalvageNodes);
        Assert.Equal(without.State.Units.Select(u => (u.Name, u.Pos)), with.State.Units.Select(u => (u.Name, u.Pos)));
        Assert.Equal(without.State.Caches.Select(c => (c.Pos, string.Join(",", c.Items))), with.State.Caches.Select(c => (c.Pos, string.Join(",", c.Items))));
        foreach (var n in with.State.SalvageNodes)
        {
            Assert.Equal(Real.Salvage.Node(n.Kind)!.Terrain, with.State.Map[n.Pos]);
            Assert.False(with.State.Map.IsExit(n.Pos));
        }
        // Deterministic.
        var again = NewScavenge.Create(Real, "outskirts", 42, [Party(1)]);
        Assert.Equal(with.State.SalvageNodes.Select(n => (n.Kind, n.Pos, n.LotsLeft)), again.State.SalvageNodes.Select(n => (n.Kind, n.Pos, n.LotsLeft)));
    }

    [Fact]
    public void Nodes_piles_and_the_loot_seed_survive_a_save()
    {
        var sim = NewScavenge.Create(Real, "scrapyard_flats", 9, [Party(1)]);
        FieldLoot.Pile(sim.State, new Pos(1, 1), LootPileKind.Stash, "Player 1's stash", 1).Items.Add("stone");
        var loaded = ScavengeSave.Load(Real, ScavengeSave.Save(sim.State));
        Assert.Equal(ScavengeSave.Save(sim.State), ScavengeSave.Save(loaded.State));
        Assert.NotEmpty(loaded.State.SalvageNodes);
        Assert.NotEqual(0UL, loaded.State.LootSeed);
        Assert.Contains(loaded.State.Caches, c => c is { Kind: LootPileKind.Stash, PlayerId: 1 });
    }

    [Fact]
    public void An_older_run_is_refused_and_a_current_one_keeps_its_loot_seed()
    {
        var sim = NewScavenge.Create(Real, "outskirts", 3, [Party(1)]);
        var node = System.Text.Json.Nodes.JsonNode.Parse(ScavengeSave.Save(sim.State))!;
        node["version"] = ScavengeSave.LootVersion - 1;
        node["scavenge"]!["lootSeed"] = 0;
        Assert.Contains("Unsupported scavenge save version", Assert.Throws<System.Text.Json.JsonException>(() => ScavengeSave.Load(Real, node.ToJsonString())).Message); // old runs reset
        Assert.NotEqual(0UL, ScavengeSave.Load(Real, ScavengeSave.Save(sim.State)).State.LootSeed);
    }
}
