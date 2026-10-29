using System.Text;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Playtest;

/// <summary>
/// Many runs of one zone with a fresh colony's first party (two healthiest adults, starting gear), to read off how
/// deadly a zone is with basic gear, independent of the colony economy.
/// </summary>
public static class ZoneTrial
{
    /// <summary>The map as text: # wall, r rubble, c cover, ~ water, P pylon, E exit, units by first letter, $ caches.</summary>
    public static string Picture(ScavengeSim sim)
    {
        var map = sim.State.Map;
        var sb = new StringBuilder();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var p = new Pos(x, y);
                var u = sim.State.UnitAt(p);
                sb.Append(u is not null ? (u.Kind == UnitKind.Enemy ? 'e' : u.Name[0])
                    : sim.State.Caches.Any(c => c.Pos == p) ? '$'
                    : map.IsExit(p) ? 'E'
                    : map[p] switch { Terrain.Wall => '#', Terrain.Rubble => 'r', Terrain.Cover => 'c', Terrain.Water => '~', Terrain.Pylon => 'P', _ => '.' });
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Run(GameData data, ScavengeData scavenge, string zoneId, int runs, int partySize = 2, Action<string>? trace = null)
    {
        int deaths = 0, runsWithDeath = 0, wipes = 0, injuredRuns = 0, limbs = 0, items = 0, linkedSeen = 0, runsWithLinked = 0, kills = 0, rounds = 0, stuck = 0;
        var injuries = 0;
        var deadWithLinked = 0;
        for (var seed = 1; seed <= runs; seed++)
        {
            var colony = NewGame.Create(data, (ulong)seed, scavenge);
            var adults = colony.State.Survivors.Where(s => !s.IsChild).Take(partySize).ToList();
            var gear = new Queue<string>(colony.State.Inventory.Counts.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)));
            var weapons = new Queue<string>(new[] { "spear", "pipe", "pipe" }.Where(colony.State.Inventory.Has));
            var bodies = new Queue<string>(new[] { "padded_vest", "rag_coat", "rag_coat" });
            var packs = new List<List<string>> { new() { "med_kit", "bandage" }, new() { "bandage", "bandage" }, new() };
            var party = adults.Select((s, i) => new PartyMember(s,
                new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = weapons.Dequeue(), [EquipSlot.Body] = bodies.Dequeue() },
                packs[i])).ToList();
            var sim = NewScavenge.Create(scavenge, zoneId, (ulong)(seed * 7919), [new PartySetup(ColonySim.ExpeditionPlayerId, party)]);
            var (result, stats) = PartyBot.Play(sim, ColonySim.ExpeditionPlayerId, trace);
            if (!result.Complete) { stuck++; trace?.Invoke($"seed {seed} stalled"); Console.Error.WriteLine($"{zoneId} seed {seed} stalled at round {stats.Rounds}: " + string.Join(", ", sim.State.Units.Where(u => u.PlayerId == ColonySim.ExpeditionPlayerId).Select(u => $"{u.Name} {u.Kind} {u.Status} at {u.Pos} following {u.FollowingId}")));
                Console.Error.WriteLine(Picture(sim));
                continue; }
            deaths += result.Deaths.Count;
            if (result.Deaths.Count > 0) runsWithDeath++;
            if (result.Returned.Count == 0) wipes++;
            var hurt = result.Returned.Sum(r => BodyDiff.Describe(adults.First(a => a.Id == r.Survivor.Id).Body, r.Survivor.Body).Count);
            injuries += hurt;
            if (hurt > 0) injuredRuns++;
            limbs += result.Returned.Sum(r => r.Survivor.Body.Parts.Values.Count(p => p.Condition == Remnants.Core.Survivors.PartCondition.Missing));
            items += result.Loot.Values.Sum();
            linkedSeen += stats.LinkedSeen;
            if (stats.LinkedSeen > 0)
            {
                runsWithLinked++;
                if (result.Deaths.Count > 0) deadWithLinked++;
            }
            kills += stats.Kills;
            rounds += stats.Rounds;
        }
        var n = Math.Max(1, runs - stuck);
        var sb = new StringBuilder();
        sb.AppendLine($"zone {zoneId}: {runs} runs, party {partySize} (starting gear){(stuck > 0 ? $", {stuck} stuck" : "")}");
        sb.AppendLine($"  runs with a death {100.0 * runsWithDeath / n:0}%  wipes {100.0 * wipes / n:0}%  deaths/run {deaths / (double)n:0.00}");
        sb.AppendLine($"  runs with new injuries {100.0 * injuredRuns / n:0}%  injuries/run {injuries / (double)n:0.0}  limbs lost/run {limbs / (double)n:0.00}");
        sb.AppendLine($"  items/run {items / (double)n:0.0}  kills/run {kills / (double)n:0.0}  rounds/run {rounds / (double)n:0}");
        var otherRuns = n - runsWithLinked;
        sb.AppendLine($"  runs seeing Linked {100.0 * runsWithLinked / n:0}%  Linked seen/run {linkedSeen / (double)n:0.00}" +
                      (runsWithLinked > 0 ? $"  death rate with Linked {100.0 * deadWithLinked / runsWithLinked:0}% vs without {100.0 * (runsWithDeath - deadWithLinked) / Math.Max(1, otherRuns):0}%" : ""));
        return sb.ToString();
    }
}
