using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Playtest;

// Usage: dotnet run --project tools/Remnants.Playtest -- [--days=N] [--seeds=A..B | --seed=N] [--quiet] [--trace] [--no-expeditions] [--data=DIR]
//        dotnet run --project tools/Remnants.Playtest -- --zone=outskirts|all [--runs=N] [--party=N] [--trace] [--no-defenses] [--kit=fog|geared|tech|hack|drone|hacker] [--no-tech] [--coop=N]
var options = args.Where(a => a.StartsWith("--")).Select(a => a[2..].Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
var days = int.Parse(options.GetValueOrDefault("days", "40"));
var dataDir = options.GetValueOrDefault("data", Path.Combine(AppContext.BaseDirectory, "data"));
var seeds = options.TryGetValue("seed", out var one) ? [ulong.Parse(one)]
    : options.GetValueOrDefault("seeds", "1..1").Split("..") is [var a, var b] ? Enumerable.Range(int.Parse(a), int.Parse(b) - int.Parse(a) + 1).Select(i => (ulong)i).ToList()
    : [1UL];
var quiet = options.ContainsKey("quiet");
var bot = new BotOptions { Expeditions = !options.ContainsKey("no-expeditions") };

var data = GameDataLoader.LoadFromDirectory(dataDir);
var scavenge = ScavengeDataLoader.LoadFromDirectory(dataDir);
if (options.TryGetValue("zone", out var zoneArg))
{
    // Zone trial: --zone=ID[,ID...] [--runs=N] [--party=N]
    foreach (var z in (zoneArg == "all" ? scavenge.Zones.Keys.ToList() : zoneArg.Split(',').ToList()))
        Console.Write(ZoneTrial.Run(data, scavenge, z, int.Parse(options.GetValueOrDefault("runs", "100")), int.Parse(options.GetValueOrDefault("party", "2")),
            options.ContainsKey("trace") ? line => Console.WriteLine($"    {line}") : null,
            defenses: !options.ContainsKey("no-defenses"), kit: options.GetValueOrDefault("kit", ""),
            players: int.Parse(options.GetValueOrDefault("coop", "1")), tech: !options.ContainsKey("no-tech")));
    return;
}

var summaries = new List<PlaythroughSummary>();
foreach (var seed in seeds)
{
    var run = new Playthrough(data, scavenge, seed, bot);
    if (options.ContainsKey("trace")) run.Bot.Trace = line => Console.WriteLine($"    {line}");
    summaries.Add(run.Run(days));
    if (!quiet || seeds.Count == 1) Console.WriteLine(run.Describe());
}
Console.WriteLine(Report.Summaries(summaries));
