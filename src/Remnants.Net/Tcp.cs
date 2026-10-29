using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Remnants.Net;

/// <summary>Frames on a TCP stream: a 4-byte big-endian length, then that many bytes of UTF-8 JSON (one <see cref="Message"/>).</summary>
public static class Framing
{
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    public static async Task WriteAsync(Stream stream, Message message, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(message));
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>The next message, or null at a clean end of stream.</summary>
    public static async Task<Message?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactly(stream, header, ct)) return null;
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 0 or > MaxFrameBytes) throw new InvalidDataException($"Frame of {length} bytes is out of range.");
        var body = new byte[length];
        if (!await ReadExactly(stream, body, ct)) throw new EndOfStreamException("Connection closed mid-frame.");
        return ProtocolJson.Deserialize(Encoding.UTF8.GetString(body));
    }

    private static async Task<bool> ReadExactly(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0)
            {
                if (read == 0) return false;
                throw new EndOfStreamException("Connection closed mid-frame.");
            }
            read += n;
        }
        return true;
    }
}

/// <summary>
/// Plain TCP listener for a <see cref="IHostSink"/>. Each client gets a reader task and a write queue; every call
/// into the sink (and <see cref="Tick"/>-style work through <see cref="Run"/>) holds one lock, so the host stays
/// single-threaded.
/// </summary>
public sealed class TcpServer : IDisposable
{
    private readonly IHostSink _sink;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private int _nextId = 1;

    public TcpServer(IHostSink sink, IPAddress address, int port)
    {
        _sink = sink;
        _listener = new TcpListener(address, port);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Optional diagnostics (connects, drops, bad frames).</summary>
    public Action<string>? Log { get; set; }

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoop();
    }

    /// <summary>Runs work on the host under the same lock as message handling (timers, shutdown).</summary>
    public void Run(Action work)
    {
        lock (_gate) work();
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            client.NoDelay = true;
            var connection = new TcpConnection(this, client, _nextId++);
            Log?.Invoke($"client {connection.Id} connected from {client.Client.RemoteEndPoint}");
            Run(() => _sink.Connected(connection));
            _ = connection.ReadLoop();
            _ = connection.WriteLoop();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }

    private sealed class TcpConnection(TcpServer server, TcpClient client, int id) : IConnection
    {
        private readonly Channel<Message> _outbox = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true });
        private int _closed;

        public int Id { get; } = id;

        public void Send(Message message) => _outbox.Writer.TryWrite(message);

        /// <summary>Server-initiated close: flush what is queued, then drop the socket.</summary>
        public void Close() => _outbox.Writer.TryComplete();

        public async Task ReadLoop()
        {
            var stream = client.GetStream();
            try
            {
                while (await Framing.ReadAsync(stream, server._cts.Token) is { } message)
                    server.Run(() => server._sink.Received(this, message));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException or ObjectDisposedException)
            {
                server.Log?.Invoke($"client {Id}: {e.GetType().Name}: {e.Message}");
            }
            Drop();
        }

        public async Task WriteLoop()
        {
            var stream = client.GetStream();
            try
            {
                await foreach (var message in _outbox.Reader.ReadAllAsync(server._cts.Token))
                    await Framing.WriteAsync(stream, message, server._cts.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
                server.Log?.Invoke($"client {Id}: write failed: {e.Message}");
            }
            Drop();
        }

        private void Drop()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            _outbox.Writer.TryComplete();
            client.Close();
            server.Log?.Invoke($"client {Id} disconnected");
            server.Run(() => server._sink.Disconnected(this));
        }
    }
}

/// <summary>A client's TCP link. <see cref="Received"/> and <see cref="Closed"/> fire on a background thread.</summary>
public sealed class TcpClientTransport : IClientTransport
{
    private readonly TcpClient _client;
    private readonly Channel<Message> _outbox = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private int _closed;

    private TcpClientTransport(TcpClient client) => _client = client;

    public event Action<Message>? Received;
    public event Action? Closed;

    public static async Task<TcpClientTransport> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(host, port, ct);
        return new TcpClientTransport(client);
    }

    /// <summary>Starts reading and writing. Subscribe to the events first.</summary>
    public void Start()
    {
        _ = ReadLoop();
        _ = WriteLoop();
    }

    public void Send(Message message) => _outbox.Writer.TryWrite(message);

    public void Close()
    {
        _outbox.Writer.TryComplete();
        _cts.CancelAfter(TimeSpan.FromSeconds(2));
    }

    private async Task ReadLoop()
    {
        try
        {
            var stream = _client.GetStream();
            while (await Framing.ReadAsync(stream, _cts.Token) is { } message) Received?.Invoke(message);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
        }
        Drop();
    }

    private async Task WriteLoop()
    {
        try
        {
            var stream = _client.GetStream();
            await foreach (var message in _outbox.Reader.ReadAllAsync(_cts.Token))
                await Framing.WriteAsync(stream, message, _cts.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
        }
        Drop();
    }

    private void Drop()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _outbox.Writer.TryComplete();
        _client.Close();
        Closed?.Invoke();
    }
}
