using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Godot;
using FileAccess = Godot.FileAccess;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Game.Colony;
using Remnants.Game.Scavenge;
using Remnants.Net;

namespace Remnants.Game.Multiplayer;

/// <summary>
/// Runs actions posted from network threads on the Godot main thread. Lives under the tree root, so it survives
/// scene changes (menu → scavenge screen → colony).
/// </summary>
public partial class NetPump : Node
{
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static NetPump? _instance;

    public static void Post(Action work) => Queue.Enqueue(work);

    public static void Ensure(SceneTree tree)
    {
        if (_instance is not null && IsInstanceValid(_instance)) return;
        _instance = new NetPump { Name = "NetPump", ProcessMode = ProcessModeEnum.Always };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
    }

    public override void _Process(double delta)
    {
        while (Queue.TryDequeue(out var work)) work();
    }
}

/// <summary>
/// The local player's multiplayer session across scenes: the party they bring (the colony's waiting expedition,
/// else the debug party), the connection, an optional in-process server (Host), and what happens with the result.
/// </summary>
public static class MultiplayerSession
{
    public const string MenuScene = "res://scenes/Multiplayer.tscn";

    public static NetClient? Client { get; private set; }

    /// <summary>The in-process server when this game is the host; it keeps running until every player is done.</summary>
    public static HostedServer? Server { get; private set; }

    /// <summary>The colony the party comes from, when it is a real expedition (its result is applied there).</summary>
    public static ColonySession? Colony { get; private set; }

    /// <summary>
    /// The party to bring: the colony's expedition if one waits in its zone (the colony in memory, else the save),
    /// otherwise the debug party.
    /// </summary>
    public static (List<PartyMember> Party, string Description) PickParty(string playerName)
    {
        Colony = null;
        var session = ColonySession.Current ?? LoadSavedColony();
        if (session?.MultiplayerParty() is { } setup)
        {
            Colony = session;
            ColonySession.Current = session;
            var zone = session.Sim.State.Expedition!.ZoneId;
            return (setup.Members, $"Your colony's expedition ({string.Join(", ", setup.Members.Select(m => m.Survivor.Name))}, out to {zone}).");
        }
        // Seeded by the player name so two debug players on one map do not field the same four people.
        var debug = ScavengeSession.DebugParty(1 + (ulong)playerName.Sum(c => c));
        return (debug, $"Debug party ({string.Join(", ", debug.Select(m => m.Survivor.Name))}) — send an expedition from your colony to bring your own.");
    }

    private static ColonySession? LoadSavedColony()
    {
        if (!FileAccess.FileExists(ColonySession.SavePath)) return null;
        try
        {
            return new ColonySession(SaveGame.Load(GameDataPaths.Load(), FileAccess.GetFileAsString(ColonySession.SavePath), GameDataPaths.LoadScavenge()));
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            GD.PrintErr($"Multiplayer: colony save not usable: {e.Message}");
            return null;
        }
    }

    public static void Host(SceneTree tree, HostOptions options, int port)
    {
        StopServer();
        Server = HostedServer.Start(GameDataPaths.LoadScavenge(), options, IPAddress.Any, port, line => GD.Print($"[server] {line}"));
        Server.Host.Ended += () => NetPump.Post(StopServerLater);
        GD.Print($"Multiplayer: hosting {options.ZoneId} on port {Server.Port}");
        NetPump.Ensure(tree);
    }

    /// <summary>Connects and joins with <paramref name="party"/>. Returns an error message, or null when the join was sent.</summary>
    public static async Task<string?> Join(SceneTree tree, string address, string name, List<PartyMember> party)
    {
        NetPump.Ensure(tree);
        Disconnect();
        var (hostName, port) = ParseAddress(address);
        try
        {
            var transport = await TcpClientTransport.ConnectAsync(hostName, port);
            var client = new NetClient(transport, GameDataPaths.LoadScavenge(), NetPump.Post);
            Client = client;
            client.Changed += () =>
            {
                if (client.CanReconnect && client.RefusedReason is null) _ = Reconnect(client, hostName, port);
            };
            transport.Start();
            client.Join(name, party);
            return null;
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or ArgumentException or OperationCanceledException)
        {
            return $"Could not connect to {hostName}:{port}: {e.Message}";
        }
    }

    private static bool _reconnecting;

    /// <summary>A dropped link during a mission: retry every few seconds within the server's reconnect window.</summary>
    private static async Task Reconnect(NetClient client, string hostName, int port)
    {
        if (_reconnecting) return;
        _reconnecting = true;
        var deadline = DateTime.UtcNow.AddSeconds(client.Mission?.ReconnectSeconds ?? 0);
        while (DateTime.UtcNow < deadline && client.CanReconnect && client.RefusedReason is null && Client == client)
        {
            await Task.Delay(3000);
            try
            {
                var transport = await TcpClientTransport.ConnectAsync(hostName, port);
                NetPump.Post(() =>
                {
                    if (!client.CanReconnect || Client != client)
                    {
                        transport.Close();
                        return;
                    }
                    client.Reconnect(transport);
                    transport.Start();
                    GD.Print("Multiplayer: reconnected");
                });
                break;
            }
            catch (System.Net.Sockets.SocketException e)
            {
                GD.Print($"Multiplayer: reconnect failed ({e.Message}), retrying");
            }
        }
        _reconnecting = false;
    }

    public static (string Host, int Port) ParseAddress(string address)
    {
        address = address.Trim();
        var colon = address.LastIndexOf(':');
        return colon > 0 && int.TryParse(address[(colon + 1)..], out var port)
            ? (address[..colon], port)
            : (address.Length > 0 ? address : "127.0.0.1", Protocol.DefaultPort);
    }

    /// <summary>The scavenge screen plays the server's view; the result goes to the colony (if the party is from one).</summary>
    public static void BeginMission(SceneTree tree)
    {
        var client = Client ?? throw new InvalidOperationException("Not connected.");
        var colony = Colony;
        ScavengeSession.Begin(tree, new RemoteDriver(client), result =>
        {
            Disconnect();
            if (colony is not null)
            {
                colony.ApplyMultiplayerResult(result);
                tree.ChangeSceneToFile(ScavengeHandoff.ColonyScene);
            }
            else tree.ChangeSceneToFile(MenuScene);
        });
    }

    public static void Disconnect()
    {
        if (Client is { Phase: not ClientPhase.Disconnected } c) c.Leave();
        Client = null;
    }

    private static void StopServerLater()
    {
        // Give the last frames (results) a moment to go out.
        var server = Server;
        Server = null;
        _ = Task.Delay(1000).ContinueWith(_ => server?.Dispose());
    }

    public static void StopServer()
    {
        Server?.Dispose();
        Server = null;
    }
}
