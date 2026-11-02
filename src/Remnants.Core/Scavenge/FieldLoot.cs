using Remnants.Core.Colony;
using Remnants.Core.Random;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Loot in the field (ARCHITECTURE.md "Field loot and salvage"): what a kill leaves (its enemy's loot table, plus the
/// schematic drops of SCHEMATICS.md), piles on tiles, taking everything that fits, dropping and stashing, and the hooks
/// other features call (raids: <see cref="RaidSpoils"/>). Stashing next to the party's parked vehicle loads it
/// (<see cref="ParkedVehicles"/>).
/// Loot rolls use their own RNG per kill or lot (<see cref="Rng"/>), so they never shift combat rolls.
/// </summary>
public static class FieldLoot
{
    /// <summary>The find tier of the run's zone: its threat, 1..4 (as for schematics).</summary>
    public static int FindTier(ScavengeSim sim) =>
        sim.Data.Zones.TryGetValue(sim.State.ZoneId, out var zone) ? Schematics.SchematicDrops.FindTier(zone) : 1;

    /// <summary>
    /// The modifiers of a roll in this run: the find tier; night and weather picks (<see cref="LootRules"/>); the
    /// <paramref name="finder"/>'s <c>loot_bonus</c> perk as extra picks; the difficulty's quantity multiplier.
    /// </summary>
    public static LootContext Context(ScavengeSim sim, Unit? finder)
    {
        var rules = sim.Data.Loot.Rules;
        var env = sim.State.Environment;
        var extra = rules.NightRolls.GetValueOrDefault(env.Phase)
                    + (env.Weather is { } w ? rules.WeatherRolls.GetValueOrDefault(w) : 0)
                    + (finder is null ? 0 : finder.Hook(PerkHooks.LootBonus) * rules.LootBonusRolls);
        var quantity = sim.State.Difficulty is { } d ? rules.Difficulty.GetValueOrDefault(d, 1) : 1;
        return new LootContext(FindTier(sim), extra, quantity);
    }

    /// <summary>A fresh RNG for one loot roll, from the run's loot seed and a salt (the unit or node), never the sim's.</summary>
    public static DeterministicRng Rng(ScavengeState state, long salt)
    {
        var z = state.LootSeed + 0x9E37_79B9_7F4A_7C15UL * (ulong)(salt + 1);
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return new DeterministicRng(z ^ (z >> 31) | 1);
    }

    /// <summary>What an enemy (or a camera, a turret) leaves when killed: its loot table, then its schematic drops.</summary>
    public static List<string> Drops(ScavengeSim sim, Unit unit, Unit? killer)
    {
        if (unit.EnemyDefId is not { } enemyId || !sim.Data.Enemies.TryGetValue(enemyId, out var def)) return [];
        var items = sim.Data.Loot.Find(def.Loot) is { } table ? sim.Data.Loot.Roll(sim.Data, table, Context(sim, killer), Rng(sim.State, unit.Id)) : [];
        Found(sim, killer, LootCatalog.TakeCards(items));
        // Schematics roll as SCHEMATICS.md says (on the sim's RNG, first hit only), after the table.
        items.AddRange(Schematics.SchematicDrops.EnemyDrops(sim.Data, def, sim.Rng, sim.State.KnownSchematics));
        return items;
    }

    /// <summary>
    /// Cards a unit's party found (a kill's or a salvage lot's card rows): into the party's run pouch, told to its side only.
    /// A find with no party to take it (an AI kill) is lost.
    /// </summary>
    public static void Found(ScavengeSim sim, Unit? finder, List<string> cards)
    {
        if (cards.Count == 0 || finder?.PlayerId is not { } pid || sim.State.FindParty(pid) is not { } party) return;
        party.CardPouch.AddRange(cards);
        var names = string.Join(", ", cards.Select(id => sim.Data.Cards.Find(id)?.Name ?? id));
        sim.State.Log(new(sim.State.Round, ScavengeEventType.CardFound, $"{finder.Name} found card{(cards.Count > 1 ? "s" : "")}: {names}.", finder.Id,
            Pos: finder.Pos, Side: party.Side));
    }

    /// <summary>The pile of this kind on a tile (a new one when there is none): remains and drops share a tile's pile.</summary>
    public static LootCache Pile(ScavengeState state, Pos pos, LootPileKind kind, string? source = null, int? playerId = null)
    {
        var pile = state.Caches.Find(c => c.Pos == pos && (kind == LootPileKind.Stash ? c.Kind == kind && c.PlayerId == playerId : c.Kind != LootPileKind.Stash));
        if (pile is not null) return pile;
        pile = new LootCache { Id = state.NextCacheId++, Pos = pos, Kind = kind, Source = source, PlayerId = playerId };
        state.Caches.Add(pile);
        return pile;
    }

    /// <summary>Into the unit's pack, lightest first, while the carry limit allows; returns what fit and what did not.</summary>
    public static (List<string> Kept, List<string> Left) Stow(ScavengeSim sim, Unit unit, IEnumerable<string> items)
    {
        var profile = sim.Profile(unit);
        var load = profile.Load;
        var kept = new List<string>();
        var left = new List<string>();
        foreach (var id in items.OrderBy(id => sim.Item(id).Weight).ThenBy(id => id, StringComparer.Ordinal))
        {
            var w = sim.Item(id).Weight;
            if (load + w <= profile.CarryCapacity + 1e-9)
            {
                load += w;
                unit.Pack.Add(id);
                kept.Add(id);
            }
            else left.Add(id);
        }
        return (kept, left);
    }

    /// <summary>"2 Scrap, Bandage" (in order of first appearance).</summary>
    public static string Describe(ScavengeData data, IEnumerable<string> items) =>
        string.Join(", ", items.GroupBy(id => id).Select(g => g.Count() > 1 ? $"{g.Count()} {Name(data, g.Key)}" : Name(data, g.Key)));

    private static string Name(ScavengeData data, string id) => data.Items.TryGetValue(id, out var item) ? item.Name : id;

    /// <summary>
    /// A kill's loot onto its tile and into the log ("Husk 3 dropped: 2 Scrap, Bandage"). The line carries the tile, not
    /// the unit, so only sides that see the tile hear of it (and of what is in the pile).
    /// </summary>
    internal static void Dropped(ScavengeSim sim, Unit unit, List<string> items, string source)
    {
        if (items.Count == 0) return;
        Pile(sim.State, unit.Pos, LootPileKind.Remains, source).Items.AddRange(items);
        sim.State.Log(new(sim.State.Round, ScavengeEventType.LootDropped, $"{source} dropped: {Describe(sim.Data, items)}.", Pos: unit.Pos));
    }

    /// <summary>Take everything that fits from a pile next to the unit for one pick-up's AP (lightest first).</summary>
    public static ScavengeResult TakeAll(ScavengeSim sim, Unit unit, TakeAll c)
    {
        var state = sim.State;
        var pile = state.FindCache(c.CacheId);
        if (pile is null) return ScavengeResult.Fail(ScavengeError.UnknownCache, $"No cache {c.CacheId}.");
        if (unit.Pos.Chebyshev(pile.Pos) > 1) return ScavengeResult.Fail(ScavengeError.TooFar, $"{unit.Name} is not next to cache {pile.Id}.");
        var cost = sim.Data.Rules.Turn.PickUpApCost;
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
        var (kept, left) = Stow(sim, unit, pile.Items);
        if (kept.Count == 0)
            return ScavengeResult.Fail(ScavengeError.CarryLimitExceeded, $"{unit.Name} cannot carry any more.");
        unit.Ap -= cost;
        pile.Items = left;
        if (left.Count == 0) state.Caches.Remove(pile);
        var heavy = left.Count == 0 ? "" : $"; left {Describe(sim.Data, left)} (too heavy)";
        sim.Log(ScavengeEventType.PickedUp, $"{unit.Name} picked up {Describe(sim.Data, kept)}{heavy}.", unit);
        foreach (var id in kept) MissionRules.PickedUp(sim, unit, id);
        return ScavengeResult.Success($"{unit.Name} picked up {kept.Count} items.", pile.Id);
    }

    /// <summary>Drop a carried (not worn) item onto the unit's tile; anyone can pick it up again.</summary>
    public static ScavengeResult Drop(ScavengeSim sim, Unit unit, DropItem c)
    {
        if (!unit.Pack.Contains(c.ItemId)) return ScavengeResult.Fail(ScavengeError.ItemNotCarried, $"{unit.Name} does not carry '{c.ItemId}'.");
        var cost = sim.Data.Salvage.DropApCost;
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
        unit.Ap -= cost;
        unit.Pack.Remove(c.ItemId);
        Pile(sim.State, unit.Pos, LootPileKind.Dropped, unit.Name).Items.Add(c.ItemId);
        sim.Log(ScavengeEventType.ItemDropped, $"{unit.Name} dropped {sim.Item(c.ItemId).Name}.", unit);
        return ScavengeResult.Success($"{unit.Name} dropped {sim.Item(c.ItemId).Name}.");
    }

    /// <summary>
    /// Stash a carried item for later: into the party's parked vehicle when it is next to the unit (it goes home with the
    /// vehicle, within its cargo: <see cref="ParkedVehicles"/>), else on the exit tile the unit stands on. An exit stash
    /// stays in the zone and is there again on the next run.
    /// </summary>
    public static ScavengeResult Stash(ScavengeSim sim, Unit unit, StashItem c)
    {
        var vehicle = ParkedVehicles.Of(sim.State, unit.PlayerId ?? -1) is { Wrecked: false } v ? v : null;
        if (vehicle is not null && vehicle.Pos.Chebyshev(unit.Pos) <= 1) return ParkedVehicles.Stash(sim, unit, c);
        if (!unit.Pack.Contains(c.ItemId)) return ScavengeResult.Fail(ScavengeError.ItemNotCarried, $"{unit.Name} does not carry '{c.ItemId}'.");
        var name = sim.Item(c.ItemId).Name;
        if (!sim.State.Map.IsExit(unit.Pos))
            return vehicle is not null
                ? ScavengeResult.Fail(ScavengeError.TooFar, $"{unit.Name} is not next to the party's vehicle or on an exit.")
                : ScavengeResult.Fail(ScavengeError.NotOnExit, $"{unit.Name} must stand on an exit tile to stash.");
        var cost = sim.Data.Salvage.StashApCost;
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
        unit.Ap -= cost;
        unit.Pack.Remove(c.ItemId);
        Pile(sim.State, unit.Pos, LootPileKind.Stash, $"{sim.State.PlayerName(unit.PlayerId ?? 0)}'s stash", unit.PlayerId).Items.Add(c.ItemId);
        sim.Log(ScavengeEventType.Stashed, $"{unit.Name} stashed {name} by the exit.", unit);
        return ScavengeResult.Success($"{unit.Name} stashed {name}.");
    }

    /// <summary>A player's stash left in the zone: what is still in their exit stash piles.</summary>
    public static List<string> StashOf(ScavengeState state, int playerId) =>
        state.Caches.Where(c => c.Kind == LootPileKind.Stash && c.PlayerId == playerId).SelectMany(c => c.Items).ToList();

    /// <summary>Puts a stash from an earlier run back on the zone's first exit tile.</summary>
    internal static void PlaceStash(ScavengeState state, int playerId, IReadOnlyCollection<string>? items)
    {
        if (items is not { Count: > 0 } || state.Map.Exits.Count == 0) return;
        var exit = state.Map.Exits.OrderBy(state.Map.Index).First();
        Pile(state, exit, LootPileKind.Stash, $"{state.PlayerName(playerId)}'s stash", playerId).Items.AddRange(items);
    }

    /// <summary>
    /// Raids: the spoils of the raiders a colony killed in a battle at home, rolled from their loot tables at
    /// <paramref name="tier"/> on <paramref name="rng"/> (the colony's by default) and put into the colony (salvage →
    /// resources, schematics → learned or queued, food → the pantry). Returns item id → count, for the battle report.
    /// </summary>
    public static SortedDictionary<string, int> RaidSpoils(ColonySim sim, IEnumerable<string> enemyIds, int tier = 1, DeterministicRng? rng = null)
    {
        var data = sim.Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded.");
        var spoils = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in enemyIds)
        {
            if (!data.Enemies.TryGetValue(id, out var def) || data.Loot.Find(def.Loot) is not { } table) continue;
        {
            var items = data.Loot.Roll(data, table, new LootContext(tier), rng ?? sim.Rng);
            // Cards the raiders carried go straight into the colony's collection.
            foreach (var card in LootCatalog.TakeCards(items)) ColonyCards.Add(sim.State, data.Cards, card);
            foreach (var item in items) spoils[item] = spoils.GetValueOrDefault(item) + 1;
        }
        }
        foreach (var (item, n) in spoils)
            for (var i = 0; i < n; i++) ExpeditionHandlers.Deposit(sim, item);
        return spoils;
    }
}
