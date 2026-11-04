using System.Text.Json;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// A kind of salvage node (data/salvage.json): what it is (<see cref="Model"/> is the prop the game draws, on the
/// <see cref="Terrain"/> it sits on), its loot <see cref="Table"/>, the work one lot takes (<see cref="Work"/> points; a
/// Salvage action does 1 plus the best carried tool's bonus), how many lots it holds (<see cref="Lots"/>), what an
/// action costs and how loud it is. <see cref="Skill"/> adds <see cref="YieldPerSkill"/> × skill to the quantity and
/// gains <see cref="SkillGain"/> per action.
/// </summary>
public sealed record SalvageNodeDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Table { get; init; } = "";
    public string Model { get; init; } = "";
    public Terrain Terrain { get; init; } = Terrain.Floor;
    public int Work { get; init; } = 1;
    public int[] Lots { get; init; } = [1, 1];
    public int ApCost { get; init; } = 1;
    public int Noise { get; init; } = 3;
    public Skill? Skill { get; init; }
    public double YieldPerSkill { get; init; } = 0.005;
    public double SkillGain { get; init; } = 0.4;
    public double Xp { get; init; } = 2;
    public Dictionary<string, int> Tools { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>How many nodes a zone has and of which kinds.</summary>
public sealed record SalvageZoneDef
{
    public int[] Count { get; init; } = [0, 0];
    public List<WeightedId> Mix { get; init; } = new();
}

/// <summary>data/salvage.json: node kinds, zone densities (<see cref="Default"/> for zones not listed), drop and stash costs.</summary>
public sealed record SalvageRules
{
    public List<SalvageNodeDef> Nodes { get; init; } = new();
    public Dictionary<string, SalvageZoneDef> Zones { get; init; } = new(StringComparer.Ordinal);
    public SalvageZoneDef Default { get; init; } = new();
    public int DropApCost { get; init; }
    public int StashApCost { get; init; } = 1;

    public SalvageNodeDef? Node(string id) => Nodes.Find(n => n.Id == id);
    public SalvageZoneDef ZoneOf(string zoneId) => Zones.GetValueOrDefault(zoneId) ?? Default;
}

/// <summary>
/// A salvage node on the map: <see cref="LotsLeft"/> lots, <see cref="Progress"/> work points into the next one. Shared
/// by everyone (an ally can finish what you started; a rival can too). Depleted nodes stay, with nothing left.
/// </summary>
public sealed class SalvageNode
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public Pos Pos { get; set; }
    public int LotsLeft { get; set; }
    public int Progress { get; set; }
    public bool Depleted => LotsLeft <= 0;
}

/// <summary>
/// Field salvage: nodes placed per zone (<see cref="Place"/>), the <see cref="Salvage"/> action, and hooks for other
/// features. Yields go into the pack while they fit, the rest onto a pile on the node's tile, so weight decides what
/// comes home.
/// </summary>
public static class FieldSalvage
{
    public const string FileName = "salvage.json";

    /// <summary>
    /// Extra rolls per node kind from other features, called when a lot comes off, after the node's own table; return item
    /// ids. A <c>wrecked_car</c> gives vehicle parts (<see cref="Salvaging.Roll"/>: big ones with the player's vehicle
    /// parked, more with a winch).
    /// </summary>
    public static readonly Dictionary<string, Func<ScavengeSim, Unit, SalvageNode, IEnumerable<string>>> Extras = new(StringComparer.Ordinal)
    {
        ["wrecked_car"] = (sim, unit, _) => Salvaging.Roll(sim, unit.PlayerId ?? -1),
    };

    /// <summary>Puts the zone's salvage nodes on the map from their own seed, so the rest of the map rolls as before.</summary>
    public static void Place(ScavengeData data, ScavengeState state, ZoneDef zone, ulong seed, IReadOnlyCollection<Pos> reachable, IReadOnlyList<Pos> anchors)
    {
        var rules = data.Salvage;
        var density = rules.ZoneOf(zone.Id);
        if (density.Count[1] <= 0 || density.Mix.Count == 0) return;
        var rng = new DeterministicRng(seed ^ 0x5A1F_A6E5_0D1C_E5UL);
        var map = state.Map;
        var near = new HashSet<Pos>(reachable);
        bool Reachable(Pos p) => near.Contains(p) || Pos.Neighbours8.Any(d => near.Contains(p + d));
        bool Free(Pos p) => !map.IsExit(p) && state.UnitAt(p) is null && state.DeviceAt(p) is null && state.Caches.All(c => c.Pos != p)
                            && state.SalvageNodes.All(n => n.Pos.Chebyshev(p) > 1) && anchors.All(a => a.Chebyshev(p) >= 3);
        var count = rng.NextInclusive(density.Count[0], density.Count[1]);
        var mix = density.Mix.Where(m => rules.Node(m.Id) is not null).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        for (var i = 0; i < count && mix.Count > 0; i++)
        {
            var def = rules.Node(NewScavenge.PickWeighted(rng, mix, m => m.Weight).Id)!;
            var spots = map.AllPositions.Where(p => map[p] == def.Terrain && Reachable(p) && Free(p)
                                                    && (def.Terrain != Terrain.Floor || Pos.Neighbours8.Any(d => map.InBounds(p + d) && map[p + d] == Terrain.Wall)))
                .OrderBy(map.Index).ToList();
            if (spots.Count == 0) continue;
            Add(data, state, def, spots[rng.Next(0, spots.Count)], rng);
        }
    }

    /// <summary>Adds one node (handmade maps place theirs at markers).</summary>
    public static SalvageNode Add(ScavengeData data, ScavengeState state, SalvageNodeDef def, Pos pos, DeterministicRng rng)
    {
        var node = new SalvageNode { Id = state.NextSalvageId++, Kind = def.Id, Pos = pos, LotsLeft = rng.NextInclusive(def.Lots[0], def.Lots[1]) };
        state.SalvageNodes.Add(node);
        return node;
    }

    /// <summary>The best work bonus among the tools the unit wears or carries for this node (0 without one).</summary>
    public static int ToolBonus(SalvageNodeDef def, Unit unit) =>
        unit.AllItems.Select(id => def.Tools.GetValueOrDefault(BaseOf(id))).DefaultIfEmpty(0).Max();

    private static string BaseOf(string id) => id.Split('@')[0];

    /// <summary>Work points one action puts in: 1 + the tool's bonus.</summary>
    public static int WorkPerAction(SalvageNodeDef def, Unit unit) => 1 + ToolBonus(def, unit);

    /// <summary>Actions the unit needs to finish the node's next lot from where it is now.</summary>
    public static int ActionsLeft(SalvageNodeDef def, SalvageNode node, Unit unit) =>
        (int)Math.Ceiling(Math.Max(0, def.Work - node.Progress) / (double)WorkPerAction(def, unit));

    /// <summary>The loot context of a lot: the zone's find tier; the skill, the perk and the difficulty raise quantities.</summary>
    public static LootContext Context(ScavengeSim sim, SalvageNodeDef def, Unit unit)
    {
        var context = FieldLoot.Context(sim, unit);
        var skill = def.Skill is { } s ? unit.Skills.GetValueOrDefault(s) : 0;
        return context with { QuantityMultiplier = context.QuantityMultiplier * (1 + def.YieldPerSkill * skill), ExtraRolls = context.ExtraRolls };
    }

    /// <summary>What one lot should yield on average, for the tooltip ("~3 wood").</summary>
    public static SortedDictionary<string, double> Estimate(ScavengeSim sim, SalvageNode node, Unit? unit)
    {
        var def = sim.Data.Salvage.Node(node.Kind);
        if (def is null || sim.Data.Loot.Find(def.Table) is not { } table) return new(StringComparer.Ordinal);
        var context = unit is null ? new LootContext(FieldLoot.FindTier(sim)) : Context(sim, def, unit);
        return sim.Data.Loot.Expected(table, context);
    }

    /// <summary>The node a click on <paramref name="tile"/> salvages: one with lots left there, next to <paramref name="unit"/>; else null.</summary>
    public static SalvageNode? ClickedNode(ScavengeState state, Unit unit, Pos tile) =>
        state.SalvageNodes.Find(n => n.Pos == tile && !n.Depleted && n.Pos.Chebyshev(unit.Pos) <= 1);

    public static ScavengeResult Salvage(ScavengeSim sim, Unit unit, Salvage c)
    {
        var state = sim.State;
        var node = state.SalvageNodes.Find(n => n.Id == c.NodeId);
        if (node is null || sim.Data.Salvage.Node(node.Kind) is not { } def) return ScavengeResult.Fail(ScavengeError.UnknownNode, $"No salvage node {c.NodeId}.");
        if (node.Depleted) return ScavengeResult.Fail(ScavengeError.NodeDepleted, $"The {def.Name.ToLowerInvariant()} is stripped bare.");
        if (unit.Pos.Chebyshev(node.Pos) > 1) return ScavengeResult.Fail(ScavengeError.TooFar, $"{unit.Name} is not next to the {def.Name.ToLowerInvariant()}.");
        if (def.ApCost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {def.ApCost} AP (has {unit.Ap}).");

        unit.Ap -= def.ApCost;
        node.Progress += WorkPerAction(def, unit);
        if (def.Skill is { } skill) unit.GainSkill(skill, def.SkillGain);
        unit.FieldXp += def.Xp;
        Noise(sim, unit, def.Noise);

        if (node.Progress < def.Work)
        {
            sim.Log(ScavengeEventType.SalvageProgress, $"{unit.Name} works the {def.Name.ToLowerInvariant()} ({node.Progress}/{def.Work}).", unit);
            return ScavengeResult.Success($"{unit.Name} salvages ({node.Progress}/{def.Work}).", node.Id);
        }

        node.Progress = 0;
        node.LotsLeft--;
        var items = sim.Data.Loot.Find(def.Table) is { } table ? sim.Data.Loot.Roll(sim.Data, table, Context(sim, def, unit), FieldLoot.Rng(state, node.Id * 7919 + node.LotsLeft)) : [];
        if (Extras.TryGetValue(def.Id, out var extra)) items.AddRange(extra(sim, unit, node));
        FieldLoot.Found(sim, unit, LootCatalog.TakeCards(items));
        var (kept, left) = FieldLoot.Stow(sim, unit, items);
        if (left.Count > 0) FieldLoot.Pile(state, node.Pos, LootPileKind.Dropped, def.Name).Items.AddRange(left);
        var what = items.Count == 0 ? "nothing worth carrying" : FieldLoot.Describe(sim.Data, items);
        var overflow = left.Count == 0 ? "" : $" ({FieldLoot.Describe(sim.Data, left)} left on the ground, too heavy)";
        sim.Log(ScavengeEventType.Salvaged, $"{unit.Name} salvaged the {def.Name.ToLowerInvariant()}: {what}{overflow}.", unit);
        return ScavengeResult.Success($"{unit.Name} salvaged {what}.", node.Id);
    }

    /// <summary>Salvage is loud: idle enemies in earshot come looking, other parties hear a noise unless they see the worker.</summary>
    private static void Noise(ScavengeSim sim, Unit unit, int radius)
    {
        if (radius <= 0) return;
        sim.MakeNoise(unit, radius);
        var state = sim.State;
        foreach (var side in Detection.PartySides(state).Where(s => s != unit.Side))
            if (state.Units.Any(u => u.Side == side && u.IsActive && u.Pos.Chebyshev(unit.Pos) <= radius) && !Detection.SideSees(sim.Data, state, side, unit))
                Detection.AddContact(sim.Data, state, side, unit.Pos, ContactKind.Noise, unit.Id);
    }

    public static SalvageRules Parse(string? json)
    {
        if (json is null) return new SalvageRules();
        try
        {
            return JsonSerializer.Deserialize<SalvageRules>(json, Json.Options) ?? new SalvageRules();
        }
        catch (JsonException e)
        {
            throw new GameDataException($"salvage.json is invalid: {e.Message}");
        }
    }

    /// <summary>Data rules: known tables, tools and zones; at least 1 work and lot.</summary>
    internal static void Validate(ScavengeData data)
    {
        var rules = data.Salvage;
        foreach (var n in rules.Nodes)
        {
            var where = $"Salvage node '{n.Id}'";
            if (data.Loot.Find(n.Table) is null) throw new GameDataException($"{where}: unknown loot table '{n.Table}'.");
            if (n.Work < 1 || n.ApCost < 0 || n.Noise < 0) throw new GameDataException($"{where}: work >= 1, apCost and noise >= 0.");
            if (n.Lots.Length != 2 || n.Lots[0] < 1 || n.Lots[0] > n.Lots[1]) throw new GameDataException($"{where}: lots must be [min, max] with 1 <= min <= max.");
            foreach (var (tool, bonus) in n.Tools)
                if (!data.Items.ContainsKey(tool) || bonus < 0) throw new GameDataException($"{where}: tool '{tool}' is unknown or has a negative bonus.");
        }
        foreach (var (zone, z) in rules.Zones.Append(new("default", rules.Default)))
        {
            if (zone != "default" && !data.Zones.ContainsKey(zone)) throw new GameDataException($"Salvage: unknown zone '{zone}'.");
            if (z.Count.Length != 2 || z.Count[0] < 0 || z.Count[0] > z.Count[1]) throw new GameDataException($"Salvage zone '{zone}': count must be [min, max].");
            foreach (var m in z.Mix)
                if (rules.Node(m.Id) is null) throw new GameDataException($"Salvage zone '{zone}': unknown node '{m.Id}'.");
        }
    }
}
