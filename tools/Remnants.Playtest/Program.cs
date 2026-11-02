using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Playtest;

// Usage: dotnet run --project tools/Remnants.Playtest -- [--story [--prompt=N] [--story-missions=all|main|none]] [--days=N] [--seeds=A..B | --seed=N] [--quiet] [--trace] [--no-expeditions] [--bold] [--no-mods] [--no-raids | --fight] [--data=DIR]
//        [--no-vehicles] [--schematics=sch_a,sch_b|vehicles]   --travel-table   --zone=ID --vehicle=TIER   (vehicles: every vehicle schematic, to sweep the tiers)
//        dotnet run --project tools/Remnants.Playtest -- --mission=ID [--runs=N] [--hour=H[,H...]] [--weather=KIND[,KIND...]] [--th=N] [--party=N] [--kit=armed]
//        dotnet run --project tools/Remnants.Playtest -- --story-acts[=terms|silence|voice|calm|all] [--seed=N] [--trace]
//        dotnet run --project tools/Remnants.Playtest -- --zone=outskirts|all [--runs=N] [--party=N] [--trace] [--no-defenses] [--kit=fog|geared|tech|hack|drone|hacker|peg|mech|powered|cyborg|network] [--carbines=N] [--carbine-quality=fine|crude] [--armor] [--no-tech] [--coop=N] [--hour=H[,H...]] [--weather=KIND[,KIND...]|all] [--mods=basic|full|stealth|network] [--no-salvage] [--no-cards] [--no-dodge]
var options = args.Where(a => a.StartsWith("--")).Select(a => a[2..].Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
var days = int.Parse(options.GetValueOrDefault("days", "40"));
var dataDir = options.GetValueOrDefault("data", Path.Combine(AppContext.BaseDirectory, "data"));
var seeds = options.TryGetValue("seed", out var one) ? [ulong.Parse(one)]
    : options.GetValueOrDefault("seeds", "1..1").Split("..") is [var a, var b] ? Enumerable.Range(int.Parse(a), int.Parse(b) - int.Parse(a) + 1).Select(i => (ulong)i).ToList()
    : [1UL];
var quiet = options.ContainsKey("quiet");
// --no-salvage: the party bot leaves salvage nodes alone (before/after comparisons of field loot).
PartyBot.Salvage = !options.ContainsKey("no-salvage");
// --no-cards: zone-trial parties go without their squad deck (before/after comparisons of the card layer).
ZoneTrial.Cards = !options.ContainsKey("no-cards");
// --no-dodge: the party bot does not step off tiles seen enemy plans aim at (comparisons).
PartyBot.Dodge = !options.ContainsKey("no-dodge");
var schematics = options.GetValueOrDefault("schematics", "") is { Length: > 0 } list
    ? list == "vehicles"
        ? ["sch_bicycle_cart", "sch_garage_2", "sch_biofuel_still", "sch_motorbike", "sch_small_engine", "sch_buggy",
            "sch_garage_3", "sch_truck", "sch_diesel_engine", "sch_armor_plates", "sch_armored_rig"]
        : list.Split(',').ToList()
    : new List<string>();
var bot = new BotOptions
{
    Expeditions = !options.ContainsKey("no-expeditions"), Bold = options.ContainsKey("bold"), Mods = !options.ContainsKey("no-mods"),
    StoryPrompt = int.Parse(options.GetValueOrDefault("prompt", "0")), FightRaids = options.ContainsKey("fight"),
    Vehicles = !options.ContainsKey("no-vehicles"), Schematics = schematics, StoryMissions = options.GetValueOrDefault("story-missions", "all"),
};

var data = GameDataLoader.LoadFromDirectory(dataDir);
var scavenge = ScavengeDataLoader.LoadFromDirectory(dataDir);
foreach (var w in scavenge.LootWarnings) Console.Error.WriteLine($"warning: {w}");
// --story: a Story game; the bot takes story missions as they open (the report lists the day each is done).
var story = options.ContainsKey("story") ? Remnants.Core.Story.StoryDataLoader.LoadFromDirectory(Path.Combine(dataDir, "story")) : null;
var raids = options.ContainsKey("no-raids") ? null : Remnants.Core.Raids.RaidDataLoader.LoadFromDirectory(dataDir);
raids?.Validate(data, scavenge);
if (options.TryGetValue("story-acts", out var variantArg))
{
    // Story bot playthrough of Acts 2-4: --story-acts[=terms|silence|voice|calm|all] [--seed=N] [--trace]
    var storyData = Remnants.Core.Story.StoryDataLoader.LoadFromDirectory(Path.Combine(dataDir, "story"));
    var picked = StoryActs.Variants.Where(v => variantArg is "" or "all" || variantArg.Split(',').Contains(v.Name)).ToList();
    foreach (var seed in seeds)
        foreach (var variant in picked)
            StoryActs.Print(StoryActs.Run(data, scavenge, storyData, seed, variant, options.ContainsKey("trace") ? line => Console.WriteLine($"    {line}") : null),
                Console.WriteLine);
    return;
}
if (options.TryGetValue("mission", out var missionArg))
{
    // Mission trial: --mission=ID [--runs=N] [--hour=H[,H...]] [--weather=KIND[,KIND...]] [--th=N]: a fresh colony's first party plays it.
    var storyData = Remnants.Core.Story.StoryDataLoader.LoadFromDirectory(Path.Combine(dataDir, "story"));
    var hours = options.TryGetValue("hour", out var hourArg) ? hourArg.Split(',').Select(h => (int?)int.Parse(h)).ToList() : [null];
    var weathers = options.TryGetValue("weather", out var weatherArg) ? weatherArg.Split(',').Select(w => (string?)w).ToList() : [null];
    foreach (var hour in hours)
    foreach (var weather in weathers)
        Console.Write(MissionTrial.Run(data, scavenge, storyData, missionArg, int.Parse(options.GetValueOrDefault("runs", "100")), hour, weather,
            int.Parse(options.GetValueOrDefault("th", "1")), options.ContainsKey("trace") ? line => Console.WriteLine($"    {line}") : null,
            int.Parse(options.GetValueOrDefault("party", "2")), options.GetValueOrDefault("kit", "")));
    return;
}
if (options.TryGetValue("zone", out var zoneArg))
{
    // Zone trial: --zone=ID[,ID...] [--runs=N] [--party=N]; --hour and --weather take lists and sweep every combination.
    var hours = options.TryGetValue("hour", out var hourArg) ? hourArg.Split(',').Select(h => (int?)int.Parse(h)).ToList() : [null];
    var weathers = options.TryGetValue("weather", out var weatherArg)
        ? (weatherArg == "all" ? ZoneTrial.WeatherKinds(data) : weatherArg.Split(',').ToList()).Select(w => (string?)w).ToList()
        : [null];
    foreach (var w in weathers.Where(w => w is not null && w != WeatherRules.Clear && !scavenge.Rules.Environment.Weather.ContainsKey(w)))
        Console.Error.WriteLine($"note: scavenge.json has no field effects for weather '{w}'");
    foreach (var z in (zoneArg == "all" ? scavenge.Zones.Keys.ToList() : zoneArg.Split(',').ToList()))
    foreach (var hour in hours)
    foreach (var weather in weathers)
        Console.Write(ZoneTrial.Run(data, scavenge, z, int.Parse(options.GetValueOrDefault("runs", "100")), int.Parse(options.GetValueOrDefault("party", "2")),
            options.ContainsKey("trace") ? line => Console.WriteLine($"    {line}") : null,
            defenses: !options.ContainsKey("no-defenses"), kit: options.GetValueOrDefault("kit", ""),
            players: int.Parse(options.GetValueOrDefault("coop", "1")), tech: !options.ContainsKey("no-tech"),
            carbines: int.Parse(options.GetValueOrDefault("carbines", "0")), armor: options.ContainsKey("armor"),
            carbineQuality: options.GetValueOrDefault("carbine-quality", ""),
            hour: hour, weather: weather, mods: options.GetValueOrDefault("mods", ""), vehicle: options.GetValueOrDefault("vehicle")));
    return;
}
if (options.ContainsKey("travel-table"))
{
    // Days each way, rations per member (round trip) and fuel per trip, per zone and vehicle tier (new, motor vehicles with headlights).
    Console.Write(TravelTable.Render(data, scavenge));
    return;
}

var summaries = new List<PlaythroughSummary>();
foreach (var seed in seeds)
{
    var run = new Playthrough(data, scavenge, seed, bot, story, raids);
    if (options.ContainsKey("trace")) run.Bot.Trace = line => Console.WriteLine($"    {line}");
    summaries.Add(run.Run(days));
    if (!quiet || seeds.Count == 1) Console.WriteLine(run.Describe());
}
Console.WriteLine(Report.Summaries(summaries));
