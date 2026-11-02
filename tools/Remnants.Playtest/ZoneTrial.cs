using System.Text;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Playtest;

/// <summary>
/// Many runs of one zone with a fresh colony's first party (two healthiest adults, starting gear), to read off how
/// deadly a zone is with basic gear, independent of the colony economy.
/// </summary>
public static class ZoneTrial
{
    /// <summary>Whether parties bring the colony's squad deck (<c>--no-cards</c> turns it off, for before/after comparisons).</summary>
    public static bool Cards { get; set; } = true;

    /// <summary>The intensity of a trial's weather: heavy rain (drones grounded), a strong storm.</summary>
    public const double TrialIntensity = 0.8;

    /// <summary>The kinds <c>--weather=all</c> sweeps: every kind in weather.json, or the usual five without one.</summary>
    public static List<string> WeatherKinds(GameData data) =>
        data.Weather.Kinds.Count > 0 ? data.Weather.Kinds.Keys.ToList() : [WeatherRules.Clear, "rain", "thunderstorm", "fog", "dust_storm"];

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

    private static (BodyPart, string)[] Legs(string item) => [(BodyPart.LeftLeg, item), (BodyPart.RightLeg, item)];

    /// <summary>Cybernetic kits per party member (the last entry repeats): limbs fitted before the run, starting gear.</summary>
    private static readonly Dictionary<string, (BodyPart Part, string Item)[][]> CyberKits = new()
    {
        ["peg"] = [[(BodyPart.LeftLeg, "peg_leg")]],
        ["mech"] = [[(BodyPart.LeftLeg, "mech_leg")]],
        ["powered"] = [[.. Legs("runner_leg"), (BodyPart.RightArm, "blade_arm")], [.. Legs("silent_leg"), (BodyPart.RightArm, "precision_arm")]],
        ["cyborg"] = [[.. Legs("runner_leg_mk2"), (BodyPart.RightArm, "blade_arm_mk2")], [.. Legs("jumper_leg"), (BodyPart.LeftArm, "gun_arm"), (BodyPart.Eyes, "scope_eye")]],
        ["network"] = [[.. Legs("network_leg"), (BodyPart.RightArm, "network_arm")], [.. Legs("network_leg"), (BodyPart.LeftArm, "network_arm"), (BodyPart.Eyes, "network_eye")]],
    };

    /// <param name="defenses">The bot uses Overwatch / Brace / Lookout / flares (off = the old bot, for comparison).</param>
    /// <param name="kit">"fog": each member also carries two flares and the first a motion sensor. "geared": a crafted
    /// mid-game kit instead of starting gear — pistol / rifle, padded vest / camo cloak, binoculars, med-kit, bandages,
    /// flares and a motion sensor — and the bot stands and fights the Linked.</param>
    /// <param name="kit">... "tech": starting gear plus a hacking kit (first member) and a scout drone (second); "hack" / "drone":
    /// only the one. "hacker": the
    /// same with a trained crew: Hacking 35 and the Hacking I perk, Drone Piloting 25 with Drone Operator I and Spotter.</param>
    /// <param name="tech">The bot hacks and flies the drone when it has the gear (off: it carries them and does nothing).</param>
    /// <param name="carbines">With the geared kit: this many members carry a Linked carbine instead of their rifle / pistol.</param>
    /// <param name="carbineQuality">The carbines' quality tier ("fine", "crude"; empty: standard).</param>
    /// <param name="armor">With the geared kit: everyone wears Linked plating instead of the vest / cloak.</param>
    /// <param name="mods">Gear mods on the kit, fitted by <see cref="GearModder"/> for the run (night, fighting the Linked):
    /// "basic" the default set (iron sights, wrapped grip, sharpened edge, padding, webbing, scrap plates) and a scrap
    /// helmet; "full" Mk II where a kit exists and every survivor-made mod; "stealth" the same chosen to stay hidden; "network"
    /// full plus the EMP coil and Network weave.</param>
    /// <param name="players">Co-op: this many parties (each a fresh colony's first party, different seeds) on one team;
    /// the zone scales its enemies to the player count. Per-party rates are per party, "someone dies" per mission.</param>
    /// <param name="hour">Every run is played at this hour of day (its phase by the colony's hours); null: midday.</param>
    /// <param name="weather">Every run is played in this weather (a <c>weather.json</c> kind id) at
    /// <see cref="TrialIntensity"/>, with no change during the run; null: no weather.</param>
    private static readonly string[] BasicMods =
        ["mod_iron_sight", "mod_wrapped_grip", "mod_sharpened_edge", "mod_armor_padding", "mod_armor_webbing", "mod_armor_scrap_plates"];

    /// <summary>Fits a kit's weapon and body with mods for the preset, and a helmet.</summary>
    private static void Mod(ScavengeData data, Dictionary<EquipSlot, string> gear, string preset, ModContext context)
    {
        var all = data.GearMods!.Def.Mods.Where(m => !m.Network || preset == "network").Select(m => m.Id).ToList();
        var pool = preset == "basic" ? BasicMods.ToList() : all;
        foreach (var slot in new[] { EquipSlot.Weapon, EquipSlot.Body })
        {
            var id = gear[slot];
            if (preset != "basic" && data.GearMods.KitFor(data.Items[id].DefinedId, 1) is not null) id = GearModsData.Compose(id, 2, []);
            gear[slot] = GearModder.Fit(data, id, pool, context);
        }
        gear[EquipSlot.Head] = preset == "basic" ? "helmet_scrap" : GearModder.Fit(data, "helmet_scrap", pool, context);
    }

    public static string Run(GameData data, ScavengeData scavenge, string zoneId, int runs, int partySize = 2, Action<string>? trace = null,
        bool defenses = true, string kit = "", int players = 1, bool tech = true, int carbines = 0, bool armor = false, string carbineQuality = "", int? hour = null, string? weather = null, string mods = "", string? vehicle = null)
    {
        // --hour=H plays every run at that hour of day (its phase by the colony's hours); without it, midday as before.
        // --weather=KIND holds that weather for the whole run ("clear" is the same as none).
        ScavengeEnvironment? environment = null;
        if (hour is not null || weather is not null)
        {
            var h = hour ?? 12;
            environment = ScavengeEnvironment.At(h, DayNight.PhaseAtHour(h, data.Rules.DayNight), weather);
            if (weather is not null && weather != WeatherRules.Clear) environment.Intensity = TrialIntensity;
        }
        var conditions = environment is null ? null : new ZoneConditions(Environment: environment);
        int limbLocks = 0;
        // --vehicle=TIER parks that vehicle (new, no upgrades) at each party's spawn point; its noise draws more Husks.
        FieldVehicle? field = null;
        if (vehicle is { Length: > 0 })
        {
            var tier = data.VehicleTiers[vehicle];
            var stats = VehicleRules.Stats(data, tier, Array.Empty<string>());
            field = new FieldVehicle(tier.Id, tier.Name, tier.Tier, stats.MaxCondition, stats.MaxCondition, stats.Armor, stats.CargoKg, stats.Armored, stats.Seats);
            conditions = (conditions ?? new ZoneConditions()) with { HuskMultiplier = 1 + stats.Noise };
            if (stats.Seats is { } seats && partySize > seats) partySize = seats;
        }
        int stashed = 0, wrecked = 0;
        // Parties bring their deck unless --no-cards (before/after comparisons).
        int hacked = 0, hackFailed = 0, flights = 0, dronesLost = 0, marks = 0, fromDevices = 0;
        int emps = 0, decoys = 0, noises = 0, heals = 0, ferried = 0, droneShots = 0;
        int partyRuns = 0, partyWipes = 0, partyDeathRuns = 0;
        int ambushed = 0, reactions = 0, defended = 0;
        int deaths = 0, runsWithDeath = 0, wipes = 0, injuredRuns = 0, limbs = 0, items = 0, schematics = 0, linkedSeen = 0, runsWithLinked = 0, kills = 0, rounds = 0, stuck = 0;
        int salvaged = 0, piles = 0;
        var resources = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var injuries = 0;
        var deadWithLinked = 0;
        var linkedKills = 0;
        var tactics = new PlanCardStats();
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
                weapons = new Queue<string>(weapons.Select((w, i) => i < carbines ? (carbineQuality == "" ? "linked_carbine" : $"linked_carbine@{carbineQuality}") : w));
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
            // "electronics": starting gear plus a radio pair, an EMP and a holo-decoy (first), a noise decoy and a medic
            // drone (second); "electronics-combat" swaps in a combat drone flown by a Drone Operator.
            if (kit is "electronics" or "electronics-combat")
            {
                packs[0].AddRange(["handheld_radio", "emp_grenade", "holo_decoy"]);
                if (packs.Count > 1)
                {
                    packs[1].AddRange(["handheld_radio", "noise_decoy", kit == "electronics" ? "medic_drone" : "combat_drone"]);
                    if (kit == "electronics-combat")
                    {
                        adults[1].Set(Remnants.Core.Survivors.Skill.DronePiloting, 25);
                        adults[1].Perks.Add("drone_operator_1");
                    }
                }
            }
            if (CyberKits.TryGetValue(kit, out var fitted))
                foreach (var (adult, i) in adults.Select((a, i) => (a, i)))
                    foreach (var (part, item) in fitted[Math.Min(i, fitted.Length - 1)])
                        Cybernetics.Fit(colony.State, data.Cybernetics, adult, part, scavenge.Items[item], adult.Body[part].Condition == PartCondition.Healthy);
            if (fogKit)
            {
                foreach (var pack in packs) pack.AddRange(["flare", "flare"]);
                packs[0].Add("motion_sensor");
            }
            var party = adults.Select((s, i) =>
            {
                var gearSet = new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = weapons.Dequeue(), [EquipSlot.Body] = bodies.Dequeue() };
                if (kit == "geared") gearSet[EquipSlot.Tool] = "binoculars";
                if (mods != "") Mod(scavenge, gearSet, mods, new ModContext(conditions?.Environment?.IsDark == true, kit == "geared", mods == "stealth"));
                return new PartyMember(s, gearSet, packs[i]);
            }).ToList();
            // The colony's squad deck (a new colony's starter set), as an expedition would take it.
            setups.Add(new PartySetup(ColonySim.ExpeditionPlayerId + p, party, players > 1 ? 1 : null) { Vehicle = field, Deck = Cards ? ColonyCards.Loadout(colony) : null });
            allAdults.AddRange(adults);
            }
            var sim = NewScavenge.Create(scavenge, zoneId, (ulong)(seed * 7919), setups, conditions);
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
            if (!result.Complete) { stuck++; trace?.Invoke($"seed {seed} stalled"); Console.Error.WriteLine($"{zoneId} seed {seed} stalled at round {stats.Rounds}: " + string.Join(", ", sim.State.Units.Where(u => u.PlayerId == ColonySim.ExpeditionPlayerId).Select(u => $"{u.Name} {u.Kind} {u.Status} at {u.Pos} following {u.FollowingId} load {sim.Profile(u).Load:0.#}/{sim.Profile(u).CarryCapacity:0.#} ap {sim.Profile(u).MaxAp} [{string.Join(",", u.Pack)}]")));
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
            foreach (var (id, count) in result.Loot)
                if (scavenge.Items[id].Salvage is { } s) resources[s.Resource] = resources.GetValueOrDefault(s.Resource) + s.Amount * count;
            salvaged += stats.Salvaged;
            piles += stats.PilesLooted;
            stashed += results.Sum(r => r.Returned.Count > 0 && r.Vehicle is { Wrecked: false } v ? v.Stash.Count : 0);
            wrecked += results.Count(r => r.Vehicle is { Wrecked: true });
            schematics += result.Loot.Where(kv => scavenge.Items[kv.Key].Schematic is not null).Sum(kv => kv.Value);
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
            limbLocks += stats.LimbLocks;
            emps += stats.Emps;
            decoys += stats.Decoys;
            noises += stats.NoiseDecoys;
            heals += stats.DroneHeals;
            ferried += stats.Ferried;
            droneShots += stats.DroneShots;
            tactics.Add(stats.Tactics);
        }
        var n = Math.Max(1, runs - stuck);
        var sb = new StringBuilder();
        sb.AppendLine($"zone {zoneId}: {runs} runs, {(players > 1 ? $"{players} allied parties of" : "party")} {partySize} ({(kit == "geared" ? "geared" + (carbines > 0 ? $", {carbines} {carbineQuality}{(carbineQuality == "" ? "" : " ")}carbine{(carbines > 1 ? "s" : "")}" : "") + (armor ? ", plating" : "") : "starting gear")}{(kit == "fog" ? " + flares, sensor" : "")}{(kit is "tech" or "hacker" or "hack" or "drone" or "electronics" or "electronics-combat" ? $" + {kit} kit{(tech ? "" : " (unused)")}" : "")}{(CyberKits.ContainsKey(kit) ? $" + {kit} cybernetics" : "")}{(mods != "" ? $", {mods} mods" : "")}{(defenses ? "" : ", no defenses")}){(conditions?.Environment is { } env ? $" at {env.Hour:00}:00 ({env.Phase}{(env.Weather is { } w ? $", {w}" : "")})" : "")}{(stuck > 0 ? $", {stuck} stuck" : "")}");
        sb.AppendLine($"  runs with a death {100.0 * runsWithDeath / n:0}%  wipes {100.0 * wipes / n:0}%  deaths/run {deaths / (double)n:0.00}");
        sb.AppendLine($"  runs with new injuries {100.0 * injuredRuns / n:0}%  injuries/run {injuries / (double)n:0.0}  limbs lost/run {limbs / (double)n:0.00}");
        if (field is not null)
            sb.AppendLine($"  vehicle {field.Name}: stashed items/run {stashed / (double)n:0.0}  wrecked {100.0 * wrecked / n:0}%  (noise ×{conditions!.HuskMultiplier:0.00} Husks)");
        sb.AppendLine($"  items/run {items / (double)n:0.0}  schematics/run {schematics / (double)n:0.00}  kills/run {kills / (double)n:0.0}  Linked kills/run {linkedKills / (double)n:0.00}  rounds/run {rounds / (double)n:0}");
        sb.AppendLine($"  carried home/run: {string.Join("  ", resources.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} {kv.Value / (double)n:0.0}"))}  pick-ups/run {piles / (double)n:0.0}  salvage actions/run {salvaged / (double)n:0.0}");
        sb.AppendLine($"  ambushed/run {ambushed / (double)n:0.00}  overwatch shots/run {reactions / (double)n:0.00}  defenses used/run {defended / (double)n:0.0}");
        string Tiers(int[] t) => t.Sum() == 0 ? "-" : $"{100.0 * t[0] / t.Sum():0}/{100.0 * t[1] / t.Sum():0}/{100.0 * t[2] / t.Sum():0}%";
        sb.AppendLine($"  plans/run {tactics.Declared / (double)n:0.0} (enemy {tactics.EnemyDeclared / (double)n:0.0})  resolved {tactics.Resolved / (double)n:0.0}" +
                      $"  fizzled {tactics.Fizzled / (double)n:0.0}  cancelled {tactics.Canceled / (double)n:0.0}  cards played/run {tactics.CardsPlayed / (double)n:0.0}" +
                      (tactics.EnemyCardsPlayed > 0 ? $" (enemy {tactics.EnemyCardsPlayed / (double)n:0.0})" : ""));
        sb.AppendLine($"  enemy attacks/run {tactics.EnemyAttacks / (double)n:0.0}  per enemy-round {tactics.EnemyAttacks / (double)Math.Max(1, tactics.EnemyRounds):0.000}" +
                      $"  damage dealt/run {tactics.DamageDealt / n:0}  taken/run {tactics.DamageTaken / n:0}" +
                      $"  graze/hit/solid dealt {Tiers(tactics.TiersDealt)}  taken {Tiers(tactics.TiersTaken)}");
        var otherRuns = n - runsWithLinked;
        sb.AppendLine($"  runs seeing Linked {100.0 * runsWithLinked / n:0}%  Linked seen/run {linkedSeen / (double)n:0.00}" +
                      (runsWithLinked > 0 ? $"  death rate with Linked {100.0 * deadWithLinked / runsWithLinked:0}% vs without {100.0 * (runsWithDeath - deadWithLinked) / Math.Max(1, otherRuns):0}%" : ""));
        if (kit is "tech" or "hacker" or "hack" or "drone")
            sb.AppendLine($"  hacks/run {hacked / (double)n:0.00}  failed (alarms)/run {hackFailed / (double)n:0.00}  lockers+terminals opened/run {fromDevices / (double)n:0.0}" +
                          $"  drone flights/run {flights / (double)n:0.0}  drones lost {100.0 * dronesLost / n:0}% of runs  marks/run {marks / (double)n:0.00}");
        if (kit is "electronics" or "electronics-combat")
            sb.AppendLine($"  EMPs/run {emps / (double)n:0.00}  holo-decoys/run {decoys / (double)n:0.00}  noise decoys/run {noises / (double)n:0.00}" +
                          $"  drone heals/run {heals / (double)n:0.00}  drone shots/run {droneShots / (double)n:0.00}  drone flights/run {flights / (double)n:0.0}");
        if (CyberKits.ContainsKey(kit))
            sb.AppendLine($"  limb lock-ups (interference)/run {limbLocks / (double)n:0.00}");
        if (players > 1)
            sb.AppendLine($"  per party: a death in {100.0 * partyDeathRuns / Math.Max(1, partyRuns):0}%  wiped {100.0 * partyWipes / Math.Max(1, partyRuns):0}%  (enemies x{NewScavenge.EnemyScale(scavenge, players):0.##})");
        return sb.ToString();
    }
}
