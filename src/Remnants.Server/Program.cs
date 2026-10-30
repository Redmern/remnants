using System.Globalization;
using System.Net;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Net;

namespace Remnants.Server;

/// <summary>
/// Headless authoritative server for one shared scavenge instance (see docs/MULTIPLAYER.md).
/// <code>
/// dotnet run --project src/Remnants.Server -- --port 7777 [--zone ID] [--seed N] [--players 2] [--max-players 4]
///     [--turn-seconds 60] [--reconnect-seconds 120] [--bind 0.0.0.0] [--data DIR] [--coop]
/// dotnet run --project src/Remnants.Server -- --bot 127.0.0.1:7777 [--name Bot] [--rounds 30] [--team N] [--hostile]
/// </code>
/// The server runs one mission, then exits once every player's run is over. <c>--bot</c> instead joins a server
/// as a scripted headless client (for smoke tests).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var a = new Args(args);
            var dataDir = a.Value("--data") ?? FindDataDirectory();
            var scavenge = ScavengeDataLoader.LoadFromDirectory(dataDir);
            var game = GameDataLoader.LoadFromDirectory(dataDir);
            if (a.Value("--bot") is { } target) return await Bot.Run(target, a, scavenge, game);
            return await Serve(a, scavenge, game);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or GameDataException or IOException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static async Task<int> Serve(Args a, ScavengeData scavenge, GameData game)
    {
        var options = new HostOptions
        {
            ZoneId = a.Value("--zone") ?? "suburb_ruins",
            Seed = ulong.Parse(a.Value("--seed") ?? "7", CultureInfo.InvariantCulture),
            MinPlayers = a.Int("--players", 2),
            MaxPlayers = a.Int("--max-players", 4),
            MaxPartySize = a.Int("--max-party", game.Rules.Expedition.PartySizeByTownHallLevel.Max()),
            TurnSeconds = a.Double("--turn-seconds", 60),
            ReconnectSeconds = a.Double("--reconnect-seconds", 120),
            Mode = a.Flag("--coop") ? MissionMode.Coop : MissionMode.Versus,
        };
        using var server = HostedServer.Start(scavenge, options, IPAddress.Parse(a.Value("--bind") ?? "0.0.0.0"), a.Int("--port", Protocol.DefaultPort), Print);
        var done = new TaskCompletionSource();
        server.Host.Ended += () => done.TrySetResult();
        Print($"Remnants server v{Protocol.Version} on port {server.Port}: zone {options.ZoneId}, seed {options.Seed}, " +
              $"{options.MinPlayers}-{options.MaxPlayers} players, {(options.Mode == MissionMode.Coop ? "co-op" : "versus")}, turn timer {options.TurnSeconds}s, reconnect window {options.ReconnectSeconds}s");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            done.TrySetResult();
        };
        await done.Task;
        // Let the last MissionEnded frames go out before the process exits.
        await Task.Delay(500);
        Print("server stopped");
        return 0;
    }

    internal static void Print(string line) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");

    /// <summary>data/ next to the executable (copied at build), else the repo's data/ found by walking up.</summary>
    private static string FindDataDirectory()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "data");
        if (File.Exists(Path.Combine(local, "zones.json"))) return local;
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "data", "zones.json"))) return Path.Combine(dir.FullName, "data");
        throw new IOException("Could not find the data/ folder; pass --data DIR.");
    }
}

/// <summary>Minimal <c>--name value</c> / <c>--name=value</c> argument reader.</summary>
internal sealed class Args(string[] args)
{
    public string? Value(string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) return args[i][(name.Length + 1)..];
            if (args[i] == name && i + 1 < args.Length) return args[i + 1];
        }
        return null;
    }

    public bool Flag(string name) => args.Contains(name);

    public int Int(string name, int fallback) => Value(name) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    public double Double(string name, double fallback) => Value(name) is { } v ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
