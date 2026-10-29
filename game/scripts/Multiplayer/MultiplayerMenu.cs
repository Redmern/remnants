using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Colony.Ui;
using Remnants.Game.Scavenge;
using Remnants.Net;

namespace Remnants.Game.Multiplayer;

/// <summary>
/// Multiplayer menu: player name, server address, Host (runs a server in this game for LAN play, then joins it) or
/// Join; then the lobby list with a Ready toggle. When the server starts the mission the scavenge screen takes over
/// (<see cref="MultiplayerSession.BeginMission"/>).
/// <para>
/// Debug flags (after <c>--</c>): <c>--mp-host[=PORT]</c> or <c>--mp-join=ADDR</c>, <c>--mp-name=NAME</c>, <c>--mp-ready</c>,
/// <c>--mp-zone=ID</c>, <c>--mp-seed=N</c>, <c>--mp-players=N</c>, <c>--mp-turn-seconds=S</c>; the scavenge screen's
/// <c>--autoplay</c> / <c>--screenshot</c> work as usual.
/// </para>
/// </summary>
public partial class MultiplayerMenu : Control
{
    private LineEdit _name = null!;
    private LineEdit _address = null!;
    private OptionButton _zone = null!;
    private Label _party = null!;
    private Label _status = null!;
    private RichTextLabel _lobby = null!;
    private Button _host = null!, _join = null!, _ready = null!;
    private List<PartyMember> _members = new();
    private List<string> _zoneIds = new();
    private NetClient? _client;
    private bool _started;

    public override void _Ready()
    {
        Theme = HudTheme.Shared;
        NetPump.Ensure(GetTree());
        Build();
        if (DebugCapture.Value("--mp-name") is { Length: > 0 } name) _name.Text = name;
        PickParty();
        _name.TextChanged += _ => PickParty();
        if (DebugCapture.Value("--mp-zone") is { Length: > 0 } zone && _zoneIds.IndexOf(zone) is var i and >= 0) _zone.Select(i);
        if (DebugCapture.HasFlag("--mp-host") || DebugCapture.Value("--mp-host") is not null)
        {
            if (int.TryParse(DebugCapture.Value("--mp-host"), out var port)) _address.Text = $"127.0.0.1:{port}";
            CallDeferred(MethodName.OnHost);
        }
        else if (DebugCapture.Value("--mp-join") is { Length: > 0 } address)
        {
            _address.Text = address;
            CallDeferred(MethodName.OnJoin);
        }
    }

    public override void _ExitTree()
    {
        if (_client is not null) _client.Changed -= Refresh;
    }

    private void Build()
    {
        AddChild(new ColorRect { Color = new Color(0.11f, 0.1f, 0.09f), AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
        center.AddChild(panel);
        var box = Ui.Panel(panel, 10);

        var title = Ui.Label("MULTIPLAYER", 34);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        box.AddChild(grid);
        _name = new LineEdit { Text = System.Environment.UserName is { Length: > 0 } u ? u : "Survivor", MaxLength = 24, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _address = new LineEdit { Text = $"127.0.0.1:{Protocol.DefaultPort}", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _zone = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var zone in GameDataPaths.LoadScavenge().Zones.Values.OrderBy(z => z.Threat).ThenBy(z => z.Id))
        {
            _zone.AddItem($"{zone.Name} (threat {zone.Threat})");
            _zoneIds.Add(zone.Id);
        }
        _zone.Select(System.Math.Max(0, _zoneIds.IndexOf(ScavengeSession.DebugZone)));
        grid.AddChild(Ui.Label("Name"));
        grid.AddChild(_name);
        grid.AddChild(Ui.Label("Server"));
        grid.AddChild(_address);
        grid.AddChild(Ui.Label("Zone (host)"));
        grid.AddChild(_zone);

        _party = Ui.Label("", 13, Ui.Muted);
        _party.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_party);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        box.AddChild(buttons);
        _host = Ui.Button("Host", OnHost, 110);
        _join = Ui.Button("Join", OnJoin, 110);
        buttons.AddChild(_host);
        buttons.AddChild(_join);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        buttons.AddChild(Ui.Button("Back", OnBack, 90));

        box.AddChild(Ui.Heading("Lobby"));
        _lobby = Ui.Rich();
        _lobby.CustomMinimumSize = new Vector2(0, 110);
        box.AddChild(_lobby);
        _ready = Ui.Button("Ready", () => _client?.SetReady(!IsReady()), 110);
        _ready.Disabled = true;
        box.AddChild(_ready);

        _status = Ui.Label("", 14, Ui.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_status);
    }

    private void PickParty()
    {
        var (party, description) = MultiplayerSession.PickParty(_name.Text);
        _members = party;
        _party.Text = "Party: " + description;
    }

    private void OnHost()
    {
        var (_, port) = MultiplayerSession.ParseAddress(_address.Text);
        var options = new HostOptions
        {
            ZoneId = _zoneIds[_zone.Selected],
            Seed = ulong.TryParse(DebugCapture.Value("--mp-seed"), out var seed) ? seed : (ulong)Time.GetTicksUsec(),
            MinPlayers = int.TryParse(DebugCapture.Value("--mp-players"), out var players) ? players : 2,
            MaxPartySize = GameDataPaths.Load().Rules.Expedition.PartySizeByTownHallLevel.Max(),
            TurnSeconds = double.TryParse(DebugCapture.Value("--mp-turn-seconds"), out var turn) ? turn : 60,
        };
        try
        {
            MultiplayerSession.Host(GetTree(), options, port);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            _status.Text = $"Could not host on port {port}: {e.Message}";
            return;
        }
        _address.Text = $"127.0.0.1:{port}";
        _status.Text = $"Hosting on port {port}. Others join with this machine's LAN address.";
        OnJoin();
    }

    private async void OnJoin()
    {
        _host.Disabled = _join.Disabled = true;
        _status.Text = $"Connecting to {_address.Text}…";
        var error = await MultiplayerSession.Join(GetTree(), _address.Text, _name.Text, _members);
        if (error is not null || MultiplayerSession.Client is not { } client)
        {
            _status.Text = error ?? "Not connected.";
            _host.Disabled = _join.Disabled = false;
            return;
        }
        _client = client;
        client.Changed += Refresh;
        if (DebugCapture.HasFlag("--mp-ready")) client.SetReady(true);
        Refresh();
    }

    private void OnBack()
    {
        MultiplayerSession.Disconnect();
        MultiplayerSession.StopServer();
        GetTree().ChangeSceneToFile(ScavengeSession.MenuScene);
    }

    private bool IsReady() => _client?.Lobby?.Players.Find(p => p.PlayerId == _client.PlayerId)?.Ready ?? false;

    private void Refresh()
    {
        if (_client is not { } client || !IsInsideTree()) return;
        if (client.RefusedReason is { } reason)
        {
            _status.Text = $"Refused: {reason}";
            _host.Disabled = _join.Disabled = false;
        }
        else if (client.Phase == ClientPhase.Disconnected)
        {
            _status.Text = "Disconnected.";
            _host.Disabled = _join.Disabled = false;
        }

        if (client.Lobby is { } lobby)
        {
            var zone = GameDataPaths.LoadScavenge().Zones.TryGetValue(lobby.ZoneId, out var z) ? z.Name : lobby.ZoneId;
            var lines = lobby.Players.Select(p =>
                $"{(p.PlayerId == client.PlayerId ? "[b]" : "")}{p.Name}{(p.PlayerId == client.PlayerId ? " (you)[/b]" : "")} — {p.PartySize} survivors — " +
                (p.Ready ? $"[color=#{Ui.Hex(Ui.Good)}]ready[/color]" : "not ready") + (p.Connected ? "" : " (dropped)"));
            _lobby.Text = $"{zone} · {lobby.Players.Count}/{lobby.MaxPlayers} players (starts with {lobby.MinPlayers}+ when all are ready)\n" + string.Join("\n", lines);
            _ready.Disabled = client.Phase != ClientPhase.Lobby;
            _ready.Text = IsReady() ? "Not ready" : "Ready";
        }

        if (!_started && client.Phase == ClientPhase.Playing && client.View is not null)
        {
            _started = true;
            client.Changed -= Refresh;
            MultiplayerSession.BeginMission(GetTree());
        }
    }
}
