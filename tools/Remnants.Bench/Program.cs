using System.Diagnostics;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Raids;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Playtest;

// Usage: dotnet run -c Release --project tools/Remnants.Bench -- [colony] [scavenge] [save] [--seed=N] [--days=N] [--runs=N] [--write-save=PATH] [--loop=DAYS]
// Prints baselines for docs/PERFORMANCE.md. With no section named, runs all three.
var options = args.Where(a => a.StartsWith("--")).Select(a => a[2..].Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
var sections = args.Where(a => !a.StartsWith("--")).ToHashSet();
bool Run(string s) => sections.Count == 0 || sections.Contains(s);
var seed = ulong.Parse(options.GetValueOrDefault("seed", "2"));
var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var data = GameDataLoader.LoadFromDirectory(dataDir);
var scavenge = ScavengeDataLoader.LoadFromDirectory(dataDir);
var raids = RaidDataLoader.LoadFromDirectory(dataDir);

// A colony grown by the playtest bot, saved once so every measurement starts from the same state.
string Grow(int days)
{
    var sw = Stopwatch.StartNew();
    var run = new Playthrough(data, scavenge, seed, new BotOptions(), raids: raids);
    run.Run(days);
    var s = run.Sim.State;
    Console.WriteLine($"grown day {days} (seed {seed}) in {sw.Elapsed.TotalSeconds:F1}s: {s.Survivors.Count} survivors, {s.Buildings.Count} buildings, " +
        $"{s.Food.Lots.Values.Sum(l => l.Count)} pantry lots, {s.Buildings.Sum(b => b.Queue.Count)} queued orders");
    return SaveGame.Save(s);
}

ColonySim Load(string json)
{
    var sim = SaveGame.Load(data, json, scavenge);
    sim.Raids = raids;
    return sim;
}

static string Stats(List<double> ms) =>
    ms.Count == 0 ? "n/a"
    : $"mean {ms.Average():F3} ms, p95 {ms.OrderBy(x => x).ElementAt((int)(ms.Count * 0.95)):F3} ms, max {ms.Max():F3} ms (n={ms.Count})";

if (Run("colony"))
{
    var days = int.Parse(options.GetValueOrDefault("days", "120"));
    var json = Grow(days);
    var tpd = data.Rules.TicksPerDay;
    // Warm-up (JIT), then measure 30 in-game days of pure ticks from the same save.
    var warm = Load(json);
    warm.Advance(tpd * 2);
    var sim = Load(json);
    var ticks = new List<double>(tpd * 30);
    var gen0 = GC.CollectionCount(0);
    var alloc = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < tpd * 30; i++)
    {
        var t = Stopwatch.GetTimestamp();
        sim.Tick();
        ticks.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
    }
    var allocated = GC.GetAllocatedBytesForCurrentThread() - alloc;
    // --write-save=PATH: the grown colony for the game's --load-save (Godot frame-time runs).
    if (options.TryGetValue("write-save", out var savePath)) File.WriteAllText(savePath, json);
    // --loop=N: keep ticking N more days afterwards (for a profiler to sample the tick).
    if (options.TryGetValue("loop", out var loop)) Load(json).Advance(tpd * int.Parse(loop));
    var perDay = ticks.Sum() / 30;
    Console.WriteLine($"colony tick: {Stats(ticks)}");
    Console.WriteLine($"colony per in-game day: {perDay:F2} ms; alloc {allocated / 1024.0 / ticks.Count:F1} KB/tick; gen0 GCs {GC.CollectionCount(0) - gen0}");
    // The game runs one tick per 0.5 s at 1x: 2 / 4 / 8 ticks per second at 1x / 2x / 4x.
    foreach (var speed in new[] { 1, 2, 4 })
        Console.WriteLine($"  {speed}x: {2 * speed} ticks/s -> {ticks.Average() * 2 * speed:F2} ms of sim per real second ({ticks.Average() * 2 * speed / 10:F3}% of a core)");
}

if (Run("save"))
{
    var json = Grow(int.Parse(options.GetValueOrDefault("save-days", "90")));
    var sim = Load(json);
    // Warm-up, then the best of 5 (the game saves on a frame).
    SaveGame.Save(sim.State);
    var saves = new List<double>();
    var loads = new List<double>();
    for (var i = 0; i < 5; i++)
    {
        var t = Stopwatch.GetTimestamp();
        var text = SaveGame.Save(sim.State);
        saves.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        t = Stopwatch.GetTimestamp();
        Load(text);
        loads.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
    }
    Console.WriteLine($"save day 90: {json.Length / 1024.0:F0} KB ({System.Text.Encoding.UTF8.GetByteCount(json) / 1024.0:F0} KB utf8); " +
        $"save {Stats(saves)}; load {Stats(loads)}");
}

if (Run("scavenge"))
{
    var runs = int.Parse(options.GetValueOrDefault("runs", "60"));
    // Biggest zones; tech kits bring drones and hacking; two co-op parties add units.
    foreach (var (zone, kit, players) in new[] { ("network_relay", "tech", 1), ("core_spire", "tech", 1), ("core_spire", "drone", 2) })
    {
        var ends = new List<double>();
        var cards = new List<double>();
        var all = new List<double>();
        PartyBot.Timed = (command, ms) =>
        {
            all.Add(ms);
            if (command is EndTurn or SetStance) ends.Add(ms);
            else if (command is PlayCard or DiscardDeadCard) cards.Add(ms);
        };
        ZoneTrial.Run(data, scavenge, zone, 2, 3, kit: kit, players: players); // warm-up
        ends.Clear();
        cards.Clear();
        all.Clear();
        var sw = Stopwatch.StartNew();
        ZoneTrial.Run(data, scavenge, zone, runs, 3, kit: kit, players: players);
        PartyBot.Timed = null;
        Console.WriteLine($"scavenge {zone} kit={kit} players={players}: {runs} runs in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  end turn or plan (AI turns until the party is up again): {Stats(ends)}");
        Console.WriteLine($"  card plays: {Stats(cards)}");
        Console.WriteLine($"  any command: {Stats(all)}");
    }
}
