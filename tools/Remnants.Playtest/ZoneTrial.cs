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

    /// <param name="defenses">The bot uses Overwatch / Brace / Lookout / flares (off = the old bot, for comparison).</param>
    /// <param name="kit">"fog": each member also carries two flares and the first a motion sensor. "geared": a crafted
    /// mid-game kit instead of starting gear — pistol / rifle, padded vest / camo cloak, binoculars, med-kit, bandages,
    /// flares and a motion sensor — and the bot stands and fights the Linked.</param>
    /// <param name="kit">... "tech": starting gear plus a hacking kit (first member) and a scout drone (second); "hack" / "drone":
    /// only the one. "hacker": the
    /// same with a trained crew: Hacking 35 and the Hacking I perk, Drone Piloting 25 with Drone Operator I and Spotter.</param>
    /// <param name="tech">The bot hacks and flies the drone when it has the gear (off: it carries them and does nothing).</param>
    /// <param name="carbines">With the geared kit: this many members carry a Linked carbine instead of their rifle / pistol.</param>
    /// <param name="armor">With the geared kit: everyone wears Linked plating instead of the vest / cloak.</param>
    /// <param name="players">Co-op: this many parties (each a fresh colony's first party, different seeds) on one team;
    /// the zone scales its enemies to the player count. Per-party rates are per party, "someone dies" per mission.</param>
    public static string Run(GameData data, ScavengeData scavenge, string zoneId, int runs, int partySize = 2, Action<string>? trace = null,
        bool defenses = true, string kit = "", int players = 1, bool tech = true, int carbines = 0, bool armor = false)
    {
        int hacked = 0, hackFailed = 0, flights = 0, dronesLost = 0, marks = 0, fromDevices = 0;
        int partyRuns = 0, partyWipes = 0, partyDeathRuns = 0;
        int ambushed = 0, reactions = 0, defended = 0;
        int deaths = 0, runsWithDeath = 0, wipes = 0, injuredRuns = 0, limbs = 0, items = 0, linkedSeen = 0, runsWithLinked = 0, kills = 0, rounds = 0, stuck = 0;
        var injuries = 0;
        var deadWithLinked = 0;
        var linkedKills = 0;
        for (var seed = 1; seed <= runs; seed++)
        {
            var setups = new List<PartySetup>();
            var allAdults = new List<Remnants.Core.Survivors.Survivor>();
            for (var p = 0; p < players; p++)
            {
            var colony = NewGame.Create(data, (ulong)(seed + 10007 * p), scavenge);
            var adults = colony.State.Survivors.Where(s => !s.IsChild).Take(partySize).ToList();
            var gear = new Queue<string>(colony.State.Inventory.Counts.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)));
            var weapons = new Queue<string>(new[] { "spear", "pipe", "pipe" }.Where(colony.State.Inventory.Has));
            var bodies = new Queue<string>(new[] { "padded_vest", "rag_coat", "rag_coat" });
            var packs = new List<List<string>> { new() { "med_kit", "bandage" }, new() { "bandage", "bandage" }, new() };
            var fogKit = kit is "fog" or "geared";
            if (kit == "geared")
            {
                weapons = new Queue<string>(["rifle", "pistol", "pistol"]);
                bodies = new Queue<string>(["padded_vest", "camo_cloak", "camo_cloak"]);
                packs = [["med_kit", "bandage"], ["med_kit", "bandage"], ["bandage"]];
                weapons = new Queue<string>(weapons.Select((w, i) => i < carbines ? "linked_carbine" : w));
                if (armor) bodies = new Queue<string>(["linked_plating", "linked_plating", "linked_plating"]);
            }
            if (kit is "tech" or "hacker" or "hack" or "drone")
            {
                if (kit != "drone") packs[0].Add("hacking_kit");
                if (packs.Count > 1 && kit != "hack") packs[1].Add("scout_drone");
                if (kit == "hacker")
                {
                    adults[0].Set(Remnants.Core.Survivors.Skill.Hacking, 35);
                    adults[0].Perks.Add("hacking_1");
                    if (adults.Count > 1)
                    {
                        adults[1].Set(Remnants.Core.Survivors.Skill.DronePiloting, 25);
                        adults[1].Perks.UnionWith(["drone_operator_1", "spotter"]);
                    }
                }
            }
            if (fogKit)
            {
                foreach (var pack in packs) pack.AddRange(["flare", "flare"]);
                packs[0].Add("motion_sensor");
            }
            var party = adults.Select((s, i) =>
            {
                var gearSet = new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = weapons.Dequeue(), [EquipSlot.Body] = bodies.Dequeue() };
                if (kit == "geared") gearSet[EquipSlot.Tool] = "binoculars";
                return new PartyMember(s, gearSet, packs[i]);
            }).ToList();
            setups.Add(new PartySetup(ColonySim.ExpeditionPlayerId + p, party, players > 1 ? 1 : null));
            allAdults.AddRange(adults);
            }
            var sim = NewScavenge.Create(scavenge, zoneId, (ulong)(seed * 7919), setups);
            var (results, stats) = PartyBot.PlayAll(sim, setups.Select(s => s.PlayerId).ToList(), trace, defenses: defenses, fightLinked: kit == "geared", tech: tech);
            var adultsById = allAdults;
            var result = results.Count == 1 ? results[0] : results[0] with
            {
                Complete = results.All(r => r.Complete),
                Returned = results.SelectMany(r => r.Returned).ToList(),
                Recruits = results.SelectMany(r => r.Recruits).ToList(),
                Deaths = results.SelectMany(r => r.Deaths).ToList(),
                Loot = new SortedDictionary<string, int>(results.SelectMany(r => r.Loot).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value))),
            };
            foreach (var r in results.Where(r => r.Complete))
            {
                partyRuns++;
                if (r.Returned.Count == 0) partyWipes++;
                if (r.Deaths.Count > 0) partyDeathRuns++;
            }
            if (!result.Complete) { stuck++; trace?.Invoke($"seed {seed} stalled"); Console.Error.WriteLine($"{zoneId} seed {seed} stalled at round {stats.Rounds}: " + string.Join(", ", sim.State.Units.Where(u => u.PlayerId == ColonySim.ExpeditionPlayerId).Select(u => $"{u.Name} {u.Kind} {u.Status} at {u.Pos} following {u.FollowingId}")));
                Console.Error.WriteLine(Picture(sim));
                continue; }
            deaths += result.Deaths.Count;
            if (result.Deaths.Count > 0) runsWithDeath++;
            if (result.Returned.Count == 0) wipes++;
            // Colonies number survivors alike, so match a returned survivor by name too.
            var hurt = result.Returned.Sum(r => BodyDiff.Describe(adultsById.First(a => a.Id == r.Survivor.Id && a.Name == r.Survivor.Name).Body, r.Survivor.Body).Count);
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
            linkedKills += stats.LinkedKills;
            rounds += stats.Rounds;
            ambushed += stats.AmbushedTimes;
            reactions += stats.OverwatchShots;
            defended += stats.DefensesUsed;
            hacked += stats.Hacked;
            hackFailed += stats.HackFailed;
            flights += stats.DroneFlights;
            dronesLost += stats.DronesLost;
            marks += stats.Marks;
            fromDevices += stats.FromDevices;
        }
        var n = Math.Max(1, runs - stuck);
        var sb = new StringBuilder();
        sb.AppendLine($"zone {zoneId}: {runs} runs, {(players > 1 ? $"{players} allied parties of" : "party")} {partySize} ({(kit == "geared" ? "geared" + (carbines > 0 ? $", {carbines} carbine{(carbines > 1 ? "s" : "")}" : "") + (armor ? ", plating" : "") : "starting gear")}{(kit == "fog" ? " + flares, sensor" : "")}{(kit is "tech" or "hacker" or "hack" or "drone" ? $" + {kit} kit{(tech ? "" : " (unused)")}" : "")}{(defenses ? "" : ", no defenses")}){(stuck > 0 ? $", {stuck} stuck" : "")}");
        sb.AppendLine($"  runs with a death {100.0 * runsWithDeath / n:0}%  wipes {100.0 * wipes / n:0}%  deaths/run {deaths / (double)n:0.00}");
        sb.AppendLine($"  runs with new injuries {100.0 * injuredRuns / n:0}%  injuries/run {injuries / (double)n:0.0}  limbs lost/run {limbs / (double)n:0.00}");
        sb.AppendLine($"  items/run {items / (double)n:0.0}  kills/run {kills / (double)n:0.0}  Linked kills/run {linkedKills / (double)n:0.00}  rounds/run {rounds / (double)n:0}");
        sb.AppendLine($"  ambushed/run {ambushed / (double)n:0.00}  overwatch shots/run {reactions / (double)n:0.00}  defenses used/run {defended / (double)n:0.0}");
        var otherRuns = n - runsWithLinked;
        sb.AppendLine($"  runs seeing Linked {100.0 * runsWithLinked / n:0}%  Linked seen/run {linkedSeen / (double)n:0.00}" +
                      (runsWithLinked > 0 ? $"  death rate with Linked {100.0 * deadWithLinked / runsWithLinked:0}% vs without {100.0 * (runsWithDeath - deadWithLinked) / Math.Max(1, otherRuns):0}%" : ""));
        if (kit is "tech" or "hacker" or "hack" or "drone")
            sb.AppendLine($"  hacks/run {hacked / (double)n:0.00}  failed (alarms)/run {hackFailed / (double)n:0.00}  lockers+terminals opened/run {fromDevices / (double)n:0.0}" +
                          $"  drone flights/run {flights / (double)n:0.0}  drones lost {100.0 * dronesLost / n:0}% of runs  marks/run {marks / (double)n:0.00}");
        if (players > 1)
            sb.AppendLine($"  per party: a death in {100.0 * partyDeathRuns / Math.Max(1, partyRuns):0}%  wiped {100.0 * partyWipes / Math.Max(1, partyRuns):0}%  (enemies x{NewScavenge.EnemyScale(scavenge, players):0.##})");
        return sb.ToString();
    }
}
