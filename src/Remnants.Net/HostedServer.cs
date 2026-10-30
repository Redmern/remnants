using System.Net;
using Remnants.Core.Scavenge;

namespace Remnants.Net;

/// <summary>
/// A <see cref="GameHost"/> listening on TCP with its timers ticking (every 250 ms, under the transport's lock).
/// The console server runs one; the game's Host button runs one in-process for LAN play.
/// </summary>
public sealed class HostedServer : IDisposable
{
    private readonly Timer _timer;

    private HostedServer(GameHost host, TcpServer tcp)
    {
        Host = host;
        Tcp = tcp;
        _timer = new Timer(_ => Tcp.Run(Host.Tick), null, 250, 250);
    }

    public GameHost Host { get; }
    public TcpServer Tcp { get; }
    public int Port => Tcp.Port;

    public static HostedServer Start(ScavengeData data, HostOptions options, IPAddress bind, int port, Action<string>? log = null)
    {
        var host = new GameHost(data, options) { Log = log };
        var tcp = new TcpServer(host, bind, port) { Log = log };
        tcp.Start();
        return new HostedServer(host, tcp);
    }

    public void Dispose()
    {
        _timer.Dispose();
        Tcp.Dispose();
    }
}

/// <summary>A <see cref="MarketHost"/> (the Trading Post) listening on TCP, expiring listings every few seconds.</summary>
public sealed class HostedMarket : IDisposable
{
    private readonly Timer _timer;

    private HostedMarket(MarketHost host, TcpServer tcp)
    {
        Host = host;
        Tcp = tcp;
        _timer = new Timer(_ => Tcp.Run(Host.Tick), null, 1000, 5000);
    }

    public MarketHost Host { get; }
    public TcpServer Tcp { get; }
    public int Port => Tcp.Port;

    public static HostedMarket Start(MarketHost host, IPAddress bind, int port, Action<string>? log = null)
    {
        host.Log ??= log;
        var tcp = new TcpServer(host, bind, port) { Log = log };
        tcp.Start();
        return new HostedMarket(host, tcp);
    }

    public void Dispose()
    {
        _timer.Dispose();
        Tcp.Dispose();
    }
}
