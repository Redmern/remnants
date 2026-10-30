using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Colony.Ui;
using Remnants.Game.Hud;
using Remnants.Game.Scavenge;
using Remnants.Net;

namespace Remnants.Game.Multiplayer;

/// <summary>
/// Multiplayer menu: player name, server address, mission mode for the host (Versus, or Co-op: everyone on one team,
/// spawned together), Host (runs a server in this game for LAN play, then joins it) or Join; then the lobby list with
/// each player's team and a Ready toggle, and in a versus lobby a team picker (players on the same team start allied). When the server starts the mission the scavenge screen takes over
/// (<see cref="MultiplayerSession.BeginMission"/>).
/// <para>
/// Debug flags (after <c>--</c>): <c>--mp-host[=PORT]</c> or <c>--mp-join=ADDR</c>, <c>--mp-name=NAME</c>, <c>--mp-ready</c>,
/// <c>--mp-zone=ID</c>, <c>--mp-seed=N</c>, <c>--mp-players=N</c>, <c>--mp-turn-seconds=S</c>, <c>--mp-coop</c> (host a
/// co-op mission), <c>--mp-team=N</c> (versus lobby team); the scavenge screen's
/// <c>--autoplay</c> / <c>--screenshot</c> work as usual.
/// </para>
/// </summary>
public partial class MultiplayerMenu : Control
{
    private LineEdit _name = null!;
    private LineEdit _address = null!;
    private OptionButton _zone = null!;
    private OptionButton _mode = null!;
    private OptionButton _team = null!;
    private HBoxContainer _teamRow = null!;
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
        Hud.GameSettings.Boot(GetTree());
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

    public override void _UnhandledInput(InputEvent e)
    {
        if (Hotkeys.Pressed(e, "pause_menu")) OnBack();
    }

    public override void _ExitTree()
    {
        if (_client is not null) _client.Changed -= Refresh;
    }

    private void Build()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        AddChild(new MenuBackdrop { Name = "Backdrop" });
        AddChild(new ColorRect { Color = new Color(0.03f, 0.028f, 0.025f, 0.7f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(580, 0) };
        panel.AddThemeStyleboxOverride("panel",
            PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true, Strip: Palette.Rust, StripHeight: 3), 22, 18));
        center.AddChild(panel);
        var box = Ui.Panel(panel, 10);

        var title = Widgets.Label("MULTIPLAYER", "Title");
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
        _mode = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Versus: every party for itself (alliances can form in the field). Co-op: everyone on one team, spawned together, shared sight; more enemies per player." };
        _mode.AddIconItem(Icons.Get("ui_multiplayer", 16), "Versus");
        _mode.AddIconItem(Icons.Get("stat_ally", 16), "Co-op");
        if (DebugCapture.HasFlag("--mp-coop")) _mode.Select(1);
        grid.AddChild(Ui.Label("Mode (host)"));
        grid.AddChild(_mode);

        _party = Ui.Label("", 13, Ui.Muted);
        _party.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_party);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        box.AddChild(buttons);
        _host = Widgets.IconButton("ui_multiplayer", "Host", "Run a server in this game for LAN play, then join it", OnHost, variation: "Primary");
        _join = Widgets.IconButton("ui_continue", "Join", "Join the server at the address", OnJoin, variation: "Primary");
        _host.CustomMinimumSize = _join.CustomMinimumSize = new Vector2(120, 38);
        buttons.AddChild(_host);
        buttons.AddChild(_join);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        buttons.AddChild(Widgets.IconButton("ui_chevron_left", "Back", "Back to the main menu", OnBack, Hotkeys.KeyName("pause_menu")));

        box.AddChild(Ui.Heading("Lobby", "stat_pop"));
        _lobby = Ui.Rich();
        _lobby.CustomMinimumSize = new Vector2(0, 110);
        box.AddChild(_lobby);
        _teamRow = Widgets.Row(8);
        _teamRow.Visible = false;
        _teamRow.AddChild(Ui.Label("Start on team"));
        _team = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Players who pick the same team spawn together as allies" };
        _team.AddItem("On my own");
        for (var t = 1; t <= 4; t++) _team.AddIconItem(Icons.Get("stat_ally", 16), $"Team {t}");
        _team.ItemSelected += i => _client?.SetTeam(i == 0 ? null : (int)i);
        _teamRow.AddChild(_team);
        box.AddChild(_teamRow);
        _ready = Widgets.IconButton("stat_check", "Ready", "Toggle ready: the mission starts when everyone is", () => _client?.SetReady(!IsReady()));
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

    private static int? ColonyHour() =>
        Colony.ColonySession.Current is { } session && session.MultiplayerParty() is not null && session.Sim.State.Expedition is { } e
            ? session.Sim.MissionEnvironment(e.ArrivalTick).Hour
            : null;

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
            Mode = _mode.Selected == 1 ? MissionMode.Coop : MissionMode.Versus,
            // Hosting from the colony's waiting party: the mission is at the hour it arrived (and maybe waited for).
            Hour = ColonyHour(),
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
        if (int.TryParse(DebugCapture.Value("--mp-team"), out var team) && team is >= 1 and <= 4)
        {
            _team.Select(team);
            client.SetTeam(team);
        }
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
            var myTeam = lobby.Players.Find(p => p.PlayerId == client.PlayerId)?.Team;
            string Team(LobbyPlayer p) => p.Team is not { } t ? Palette.Paint("on their own", Palette.TextMuted)
                : Palette.Paint($"team {t}{(t == myTeam && p.PlayerId != client.PlayerId ? " · ally" : "")}", t == myTeam ? Palette.Ally : SidePalette.OtherPlayer);
            var lines = lobby.Players.Select(p =>
                $"{(p.PlayerId == client.PlayerId ? "[b]" : "")}{p.Name}{(p.PlayerId == client.PlayerId ? " (you)[/b]" : "")} — {p.PartySize} survivors — {Team(p)} — " +
                (p.Ready ? $"[color=#{Ui.Hex(Ui.Good)}]ready[/color]" : "not ready") + (p.Connected ? "" : " (dropped)"));
            var mode = lobby.Mode == MissionMode.Coop
                ? Palette.Paint("CO-OP", Palette.Ally) + " · everyone on one team, more enemies per player"
                : "VERSUS · every party for itself unless it teams up";
            _lobby.Text = $"{zone} · {mode}\n{lobby.Players.Count}/{lobby.MaxPlayers} players (starts with {lobby.MinPlayers}+ when all are ready)\n" + string.Join("\n", lines);
            _teamRow.Visible = lobby.Mode == MissionMode.Versus && client.Phase == ClientPhase.Lobby;
            // Joined: the picker shows the server's mode and is fixed.
            _mode.Select(lobby.Mode == MissionMode.Coop ? 1 : 0);
            _mode.Disabled = true;
            if (_status.Text.StartsWith("Connecting")) _status.Text = $"In the lobby as player {client.PlayerId}.";
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
