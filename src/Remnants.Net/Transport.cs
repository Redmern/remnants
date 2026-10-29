namespace Remnants.Net;

/// <summary>The server's handle on one connected client.</summary>
public interface IConnection
{
    int Id { get; }
    void Send(Message message);
    void Close();
}

/// <summary>
/// What a server transport feeds: connects, messages and disconnects. <see cref="GameHost"/> implements it.
/// Calls are not concurrent: a transport that reads on several threads serializes them (see <see cref="TcpServer"/>).
/// </summary>
public interface IHostSink
{
    void Connected(IConnection connection);
    void Received(IConnection connection, Message message);
    void Disconnected(IConnection connection);
}

/// <summary>A client's link to the server. Events may fire on a background thread (TCP) or inline (loopback).</summary>
public interface IClientTransport
{
    event Action<Message>? Received;

    /// <summary>The link is gone (server closed it, network dropped, or <see cref="Close"/>).</summary>
    event Action? Closed;

    void Send(Message message);
    void Close();
}

/// <summary>
/// In-process transport for tests: every message goes through <see cref="ProtocolJson"/> both ways (so the
/// protocol round-trips exactly as on the wire) and is delivered synchronously on the caller's thread.
/// </summary>
public sealed class LoopbackHub(IHostSink host)
{
    private int _nextId = 1;

    public IHostSink Host { get; } = host;

    public LoopbackClient Connect()
    {
        var client = new LoopbackClient(this, _nextId++);
        Host.Connected(client.ServerSide);
        return client;
    }
}

public sealed class LoopbackClient : IClientTransport
{
    private readonly LoopbackHub _hub;

    internal LoopbackClient(LoopbackHub hub, int id)
    {
        _hub = hub;
        ServerSide = new Connection(this, id);
    }

    public event Action<Message>? Received;
    public event Action? Closed;

    public bool IsOpen { get; private set; } = true;

    /// <summary>Every message the server sent this client, in order (already round-tripped through JSON).</summary>
    public List<Message> Inbox { get; } = new();

    /// <summary>Total JSON bytes the server sent this client.</summary>
    public long BytesReceived { get; private set; }

    internal Connection ServerSide { get; }

    public void Send(Message message)
    {
        if (!IsOpen) throw new InvalidOperationException("Loopback connection is closed.");
        _hub.Host.Received(ServerSide, ProtocolJson.Deserialize(ProtocolJson.Serialize(message)));
    }

    /// <summary>Drops the link as a network failure would: the server sees a disconnect.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _hub.Host.Disconnected(ServerSide);
        Closed?.Invoke();
    }

    private void Deliver(Message message)
    {
        if (!IsOpen) return;
        var json = ProtocolJson.Serialize(message);
        BytesReceived += System.Text.Encoding.UTF8.GetByteCount(json);
        var copy = ProtocolJson.Deserialize(json);
        Inbox.Add(copy);
        Received?.Invoke(copy);
    }

    internal sealed class Connection(LoopbackClient client, int id) : IConnection
    {
        public int Id { get; } = id;
        public void Send(Message message) => client.Deliver(message);

        public void Close()
        {
            if (!client.IsOpen) return;
            client.IsOpen = false;
            client._hub.Host.Disconnected(this);
            client.Closed?.Invoke();
        }
    }
}
