using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Colony;
using Remnants.Game.Scavenge.Fog;
using Remnants.Game.Scavenge.Ui;

namespace Remnants.Game.Scavenge;

/// <summary>
/// The scavenge screen: builds the tactical map, units, camera and HUD around one <see cref="ScavengePresenter"/>,
/// turns clicks into Core commands, replays what each command caused, and shows the extraction summary when the
/// local party's run is over. The mission comes from <see cref="ScavengeSession"/>.
/// </summary>
public partial class ScavengeScreen : Node3D
{
    private ScavengeMission _mission = null!;
    private ScavengePresenter _presenter = null!;
    private MapView _map = null!;
    private UnitViews _units = null!;
    private FogOverlay _fog = null!;
    private Ui.ObjectiveMarkers _objectives = null!;
    private DeviceViews _devices = null!;
    private SalvageViews _salvage = null!;
    private TacticalCamera _camera = null!;

    private ScavengeHud _hud = null!;
    private TurnBar _turnBar = null!;
    private UnitPanel _unitPanel = null!;
    private ActionPanel _actionPanel = null!;
    private StancePanel _stancePanel = null!;
    private TechPanel _techPanel = null!;
    private TechAim _aim;
    private CoopPanel _coopPanel = null!;
    private ScavengeLog _log = null!;
    private ResultPanel _result = null!;
    private Audio.ScavengeAudio _audio = null!;

    private int? _selectedId;
    private int? _lastActingId;
    private string? _usingItem;
    private PingKind? _pinging;
    private bool _busy;
    private bool _pushPending;
    private HashSet<int> _seen = [];
    private Pos? _hover;

    public override void _Ready()
    {
        Hud.GameSettings.Boot(GetTree());
        _mission = ScavengeSession.TakeMission();
        _presenter = new ScavengePresenter(_mission.Driver);
        var models = new ScavengeModels(ModelLibrary.Load()) { ColonyMap = ColonyBattleDressing.IsColonyMap(_presenter.State), Data = _presenter.Sim.Data };
        GD.Print($"Scavenge: zone {_presenter.State.ZoneId}, {_presenter.State.Units.Count} units, {models.ModelCount} generated models.");

        AddEnvironment();
        var map = _presenter.State.Map;
        _camera = new TacticalCamera { Name = "Camera" };
        AddChild(_camera);
        _camera.SetBounds(Vector2.Zero, new Vector2((map.Width - 1) * TileGrid.TileSize, (map.Height - 1) * TileGrid.TileSize));
        AddWeather();

        _map = new MapView { Name = "Map" };
        AddChild(_map);
        _map.Setup(_presenter, models);
        _units = new UnitViews { Name = "Units" };
        AddChild(_units);
        _units.Setup(_presenter, models);
        _fog = new FogOverlay { Name = "Fog" };
        AddChild(_fog);
        _fog.Setup(_presenter, models);
        _devices = new DeviceViews { Name = "Devices" };
        AddChild(_devices);
        _devices.Setup(_presenter, models);
        _salvage = new SalvageViews { Name = "Salvage" };
        AddChild(_salvage);
        _salvage.Setup(_presenter, models);
        _objectives = new Ui.ObjectiveMarkers { Name = "Objectives" };
        AddChild(_objectives);
        _objectives.Setup(_presenter, models);

        BuildHud();
        _audio = new Audio.ScavengeAudio { Name = "Audio" };
        AddChild(_audio);
        _audio.Setup(_presenter, _camera, _hud);
        _units.Footstep += _audio.Footstep;
        Wire();
        SelectActing();
        _camera.Focus(PartyCentre());
        SyncAll();
        foreach (var e in FogLog.For(_presenter.Sim, _presenter.Side, _presenter.State.Events).TakeLast(8)) _log.Event(e);

        if (Fog.FogDemo.Requested) _ = FogDemoScript();
        if (TechDemo.Requested) _ = TechDemoScript();
        if (LootDemo.Requested) _ = LootDemoScript();
        if (MedicineDemoRequested) _ = MedicineDemoScript();
        if (AnimDemo.Requested) _ = AnimDemoScript();
        if (GearDemo.Requested) _ = GearDemoScript();
        if (float.TryParse(DebugValue("--zoom"), out var zoom)) _camera.Camera.Size = zoom;
        if (int.TryParse(DebugValue("--autoplay"), out var steps)) _ = Autoplay(steps);
        if (int.TryParse(DebugValue("--select-unit"), out var unitId)) CallDeferred(MethodName.SelectForCapture, unitId);
        if (DebugCapture.HasFlag("--pause-menu")) _hud.Pause.CallDeferred(Hud.Overlay.MethodName.Open);
    }

    /// <summary>
    /// The colony's sky and light at the run's hour (<see cref="ScavengeState.Environment"/>, the server's in
    /// multiplayer): dawn and dusk warm, night dark blue with flares and muzzle flashes lighting the ground. No distance
    /// fog on the tactical map, only weather's ground mist. The weather (<see cref="WeatherFx.WeatherView"/>) grades that light
    /// and draws rain and dust; it follows the run's environment on every sync, and lightning flashes on Core's event.
    /// </summary>
    private void AddEnvironment()
    {
        _lighting = new Colony.DayNightLighting { Name = "Lighting", UseFog = false };
        AddChild(_lighting);
        _lighting.SetupFixed(_presenter.State.Environment.Hour + 0.5);
    }

    private Colony.DayNightLighting _lighting = null!;
    private WeatherFx.WeatherView _weather = null!;

    private void AddWeather()
    {
        _weather = new WeatherFx.WeatherView { Name = "Weather", AmbientLightning = false };
        AddChild(_weather);
        _weather.Setup(_camera.Camera, _lighting, 26f);
        SyncWeather(instant: true);
    }

    private void SyncWeather(bool instant = false)
    {
        var env = _presenter.State.Environment;
        _weather.Set(env.Weather, env.Intensity, instant);
        WeatherFx.WeatherSounds.Set(env.Weather, env.Intensity);
    }

    /// <summary>A replayed weather event: the sky turns, thunder rolls, lightning flashes.</summary>
    private void WeatherEvent(ScavengeEvent e)
    {
        switch (e.Type)
        {
            case ScavengeEventType.WeatherChanged: SyncWeather(); break;
            case ScavengeEventType.Lightning: _weather.Strike(0.15f); break;
            case ScavengeEventType.Thunder: Audio.WeatherAudio.Lightning(0.9f); break;
        }
    }

    private void BuildHud()
    {
        _hud = new ScavengeHud(_presenter);
        Hud.Help.HelpLibrary.Ambient = () =>
        {
            // The attack being previewed: the acting unit, its chosen action, and the selected or hovered hostile.
            var acting = _presenter.ActingUnit;
            var action = acting is not null && _actionPanel.ActionId is { } id ? _presenter.Sim.Profile(acting).Action(id) : null;
            var target = _hover is { } p && _presenter.State.UnitAt(p) is { } u && acting is not null && acting.HostileTo(u) && _presenter.CanSee(u) ? u : null;
            return new Remnants.Core.Help.HelpContext { Scavenge = _presenter.Sim, Attacker = acting, Action = action, Target = target };
        };
        TreeExiting += () => Hud.Help.HelpLibrary.Ambient = null;
        AddChild(_hud);
        _hud.Tags.Setup(_presenter, _units, _camera.Camera);
        (_turnBar, _unitPanel, _actionPanel, _stancePanel, _log, _result) =
            (_hud.TurnBar, _hud.UnitPanel, _hud.Actions, _hud.Stances, _hud.Log, _hud.Result);
        _coopPanel = _hud.Coop;
        _techPanel = _hud.Tech;
        // Multiplayer: trade with allies (items between the parties' packs) at any time, not only on your turn.
        if (_presenter.Driver is RemoteDriver remote)
        {
            var dock = new Trade.TradeDock(remote.Client, _hud.Toasts);
            _hud.AddChild(dock);
            _coopPanel.TradeRequested += dock.Request;
            _coopPanel.CanTrade = true;
        }
    }

    private void Wire()
    {
        _presenter.Feedback += (message, error) =>
        {
            _log.Feedback(message, error);
            if (error) Toast(message);
        };
        _hud.EndTurnRequested += OnEndTurn;
        _hud.NextUnitRequested += SelectNextUnit;
        _hud.CenterRequested += CenterOnActing;
        _hud.Acting.ActingClicked += CenterOnActing;
        _hud.LeaveRequested += () =>
        {
            _presenter.Driver.Leave();
            Finish();
        };
        _hud.BackRequested += () =>
        {
            if (_usingItem is null && _pinging is null && _aim == TechAim.None) return false;
            _usingItem = null;
            _pinging = null;
            _aim = TechAim.None;
            RefreshHud();
            return true;
        };
        _units.Floated += (at, text, color, big) => _hud.Tags.Pop(at, text, color, big);
        _fog.Popped += (at, text, color) => _hud.Tags.Pop(at, text, color, true);
        _presenter.Pushed += OnPushed;
        _turnBar.UnitClicked += id =>
        {
            Select(id);
            if (_presenter.State.FindUnit(id) is { } u && _presenter.CanSee(u)) _camera.GlideTo(TileGrid.ToWorld(u.Pos));
        };
        _actionPanel.ActionChosen += _ => RefreshHud();
        _actionPanel.UseItemChosen += id =>
        {
            _usingItem = _usingItem == id ? null : id;
            RefreshHud();
        };
        _actionPanel.CommandRequested += command => _ = Run(command);
        _hud.Loot.CommandRequested += command => _ = Run(command);
        _stancePanel.CommandRequested += command => _ = Run(command);
        _techPanel.CommandRequested += command => _ = Run(command);
        _unitPanel.CommandRequested += command => _ = Run(command);
        _techPanel.AimChosen += aim =>
        {
            _aim = aim;
            _usingItem = null;
            _pinging = null;
            RefreshHud();
        };
        _coopPanel.CommandRequested += command => _ = Run(command);
        _coopPanel.PingChosen += kind =>
        {
            _pinging = _pinging == kind ? null : kind;
            _usingItem = null;
            RefreshHud();
        };
        _result.ContinuePressed += Finish;
    }

    /// <summary>Back to the caller with the local result (an unfinished one if a remote result never came).</summary>
    private void Finish() =>
        ScavengeSession.End(GetTree(), _presenter.Result() ?? new ExtractionResult(_presenter.PlayerId, false, [], [], [], new()));

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m:
                var hover = TileUnder(m.Position);
                if (hover != _hover)
                {
                    _hover = hover;
                    _map.SetHover(hover);
                    RefreshFocus();
                }
                if (hover is { } over && _presenter.IsExplored(over) && _presenter.State.DeviceAt(over) is { } device
                    && _presenter.KnownDevices.Contains(device))
                    _hud.DeviceTip.Show(_presenter, device, _hud.DeviceTip.GetGlobalMousePosition());
                else _hud.DeviceTip.HideTip();
                ShowLootTip(hover);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                if (TileUnder(click.Position) is { } tile) OnTileClicked(tile);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                _usingItem = null;
                _pinging = null;
                _aim = TechAim.None;
                SelectActing();
                RefreshHud();
                break;
        }
    }

    private Pos? TileUnder(Vector2 screen) =>
        _camera.GroundUnder(screen) is { } ground && TileGrid.FromWorld(ground) is var p && _presenter.State.Map.InBounds(p) ? p : null;

    /// <summary>
    /// Item mode: use it on the clicked unit. Otherwise: a visible hostile is attacked by the acting unit, any other
    /// unit is selected, an empty tile is a move order. Core judges every order; failures come back as feedback.
    /// </summary>
    private void OnTileClicked(Pos tile)
    {
        if (_busy || _presenter.Done) return;
        var state = _presenter.State;
        var clicked = state.UnitAt(tile) is { } u && _presenter.CanSee(u) ? u : null;
        var acting = _presenter.ActingUnit;
        var pid = _presenter.PlayerId;

        if (_aim is TechAim.Heal or TechAim.Fire && acting is not null)
        {
            if (clicked is null)
            {
                Toast(_aim == TechAim.Heal ? "Click a friendly unit next to the drone." : "Click a hostile the drone sees.");
                return;
            }
            var aimed = _aim;
            _aim = TechAim.None;
            _ = Run(aimed == TechAim.Heal ? new DroneHeal(pid, acting.Id, clicked.Id) : new DroneFire(pid, acting.Id, clicked.Id));
            return;
        }

        if (_aim != TechAim.None && acting is not null)
        {
            var aim = _aim;
            _aim = TechAim.None;
            _ = Run(aim == TechAim.Deploy ? new DeployDrone(pid, acting.Id, tile) : new FlyDrone(pid, acting.Id, tile));
            return;
        }

        if (_pinging is { } ping && acting is not null)
        {
            _pinging = null;
            _ = Run(new PingTile(pid, acting.Id, tile, ping));
            return;
        }

        if (_usingItem is { } item && acting is not null)
        {
            _usingItem = null;
            if (_presenter.Sim.Data.Items[item].Use is { TargetsTile: true })
            {
                _ = Run(new UseItem(pid, acting.Id, item, At: tile));
                return;
            }
            if (clicked is null)
            {
                Toast("Pick a unit to use the item on.");
                RefreshHud();
                return;
            }
            _ = Run(new UseItem(pid, acting.Id, item, clicked.Id == acting.Id ? null : clicked.Id));
            return;
        }

        if (clicked is not null)
        {
            if (acting is not null && _selectedId == acting.Id && acting.HostileTo(clicked))
                _ = Run(new Attack(pid, acting.Id, clicked.Id, _actionPanel.ActionId ?? ""));
            else
                Select(clicked.Id);
            return;
        }

        if (acting is null) return;
        if (_selectedId != acting.Id)
        {
            SelectActing();
            return;
        }
        _ = Run(new Move(pid, acting.Id, tile));
    }

    private void OnEndTurn()
    {
        if (_busy || _presenter.ActingUnit is not { } acting) return;
        _ = Run(new EndTurn(_presenter.PlayerId, acting.Id));
    }

    /// <summary>Sends a command; on success replays its events (and any AI turns it set off), then snaps views to state.</summary>
    private async Task<bool> Run(IScavengeCommand command)
    {
        if (_busy) return false;
        _busy = true;
        ScavengeResult result;
        using (PerfMonitor.Measure("execute")) result = await _presenter.Execute(command);
        if (result.Ok) _usingItem = null;
        if (result.Ok) _aim = TechAim.None;
        if (result.Ok && command is not PingTile) _pinging = null;
        await ReplayNew();
        return result.Ok;
    }

    /// <summary>Remote: another player acted, a turn timed out, a result arrived. Replayed now, or after the current replay.</summary>
    private void OnPushed()
    {
        if (!IsInsideTree()) return;
        if (_busy)
        {
            _pushPending = true;
            return;
        }
        _busy = true;
        _ = ReplayNew();
    }

    /// <summary>Replays every event not shown yet against what the side saw before and sees now, then syncs. Expects <see cref="_busy"/> set.</summary>
    private async Task ReplayNew()
    {
        do
        {
            _pushPending = false;
            var events = _presenter.TakeNewEvents();
            var seenBefore = new HashSet<int>(_seen);
            seenBefore.UnionWith(_presenter.Visible);
            if (events.Count > 0)
            {
                _map.ClearHighlights();
                RefreshHud();
                _map.Sync();
                await _units.Replay(events, seenBefore, e =>
                {
                    _log.Event(e);
                    _fog.Incoming(e);
                    _audio.Step(e);
                    WeatherEvent(e);
                    // Follow what others do in sight (enemy turns, recruits) so it never happens off screen.
                    if (e.Type is ScavengeEventType.Moved or ScavengeEventType.Attacked or ScavengeEventType.Missed
                        && e.UnitId is { } actor && _presenter.State.FindUnit(actor) is { } u && u.PlayerId != _presenter.PlayerId
                        && _units.WorldOf(actor) is { } at)
                        _camera.Follow(e.Type == ScavengeEventType.Moved && e.Pos is { } to ? TileGrid.ToWorld(to) : at);
                });
            }
        } while (_pushPending);
        _busy = false;

        if (_presenter.ActingUnit is { } acting && acting.Id != _lastActingId)
        {
            Select(acting.Id);
            _camera.GlideTo(TileGrid.ToWorld(acting.Pos));
        }
        SyncAll();
    }

    /// <summary>Debug (<c>--select-unit=ID</c>, for screenshots): select a unit as if clicked, after any demo staging.</summary>
    private async void SelectForCapture(int unitId)
    {
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        Select(unitId);
    }

    private void SelectActing()
    {
        if (_presenter.ActingUnit is { } acting) Select(acting.Id);
    }

    private void Select(int? unitId)
    {
        _selectedId = unitId;
        _lastActingId = _presenter.ActingUnit?.Id;
        RefreshHud();
    }

    /// <summary>The loot tooltip over a known pile (first) or salvage node on the hovered tile.</summary>
    private void ShowLootTip(Pos? hover)
    {
        var mouse = _hud.LootTip.GetGlobalMousePosition();
        if (hover is { } at && _presenter.KnownPiles.FirstOrDefault(c => c.Pos == at) is { } pile) _hud.LootTip.ShowPile(_presenter, pile, mouse);
        else if (hover is { } on && _presenter.KnownNodes.FirstOrDefault(n => n.Pos == on) is { } node) _hud.LootTip.ShowNode(_presenter, node, mouse);
        else _hud.LootTip.HideTip();
    }

    private void SyncAll()
    {
        using var _ = PerfMonitor.Measure("sync");
        SyncWeather();
        _map.Sync();
        _units.Sync();
        _fog.Sync();
        _devices.Sync();
        _salvage.Sync();
        _objectives.Sync();
        RefreshHud();
        _seen = new HashSet<int>(_presenter.Visible);
        if (_presenter.Done && !_result.Visible && _presenter.Result() is { } result)
            _result.Present(_presenter.Sim.Data, result);
    }

    /// <summary>The middle of the local party, where the camera starts.</summary>
    private Vector3 PartyCentre()
    {
        var mine = _presenter.State.Units.Where(u => u.OnMap && _presenter.IsMine(u)).Select(u => TileGrid.ToWorld(u.Pos)).ToList();
        return mine.Count == 0 ? TileGrid.Center(_presenter.State.Map) : mine.Aggregate(Vector3.Zero, (a, b) => a + b) / mine.Count;
    }

    /// <summary>Names and HP bars show for the selected, hovered and acting units.</summary>
    private void RefreshFocus()
    {
        var hovered = _hover is { } p && _presenter.State.UnitAt(p) is { } u && _presenter.CanSee(u) ? u.Id : (int?)null;
        _hud.Tags.SetFocus(_selectedId, hovered, _presenter.ActingUnit?.Id);
        _fog.SetFocus(_selectedId, hovered);
    }

    private bool _hudQueued;

    /// <summary>
    /// Refreshes the HUD once at the end of this frame, however many events and commands asked for it in the frame
    /// (a replay step, a sync and a selection change often land together).
    /// </summary>
    private void RefreshHud()
    {
        if (_hudQueued) return;
        _hudQueued = true;
        Callable.From(RefreshHudNow).CallDeferred();
    }

    private void RefreshHudNow()
    {
        _hudQueued = false;
        if (!IsInsideTree()) return;
        using var _ = PerfMonitor.Measure("hud");
        var selected = _selectedId is { } id ? _presenter.State.FindUnit(id) : null;
        RefreshFocus();
        _turnBar.Refresh(_busy);
        _unitPanel.Present(selected);
        // While a turn replays the orders stay as they are (dimmed, nothing moves); they catch up when it is over.
        if (!_busy)
        {
            _actionPanel.Refresh(false, _usingItem);
            _hud.Loot.Refresh(false);
            _stancePanel.Refresh(false);
            _techPanel.Refresh(false, _aim);
            _coopPanel.Refresh(false, selected, _pinging);
        }
        RefreshActingHeader(selected);
        _hud.Banner.Refresh();
        _hud.RefreshStory();
        RefreshHighlights(selected);
    }

    private bool _autoplaying;

    /// <summary>
    /// Whose orders the right column shows and what a click does now (the header's reserved line), and the column
    /// dimmed while it is not about what the player looks at (an enemy turn, another unit selected).
    /// </summary>
    private void RefreshActingHeader(Unit? selected)
    {
        var acting = _presenter.ActingUnit;
        var ownDrone = acting is not null && selected is { Kind: UnitKind.Drone } d && d.OperatorId == acting.Id;
        var other = acting is not null && selected is not null && selected.Id != acting.Id && !ownDrone;
        string title, hint;
        var color = Hud.Palette.TextMuted;
        if (acting is null)
        {
            title = _presenter.Done ? "Run over" : _busy ? "Enemy turn" : "Waiting";
            hint = _presenter.Done ? "The run is over." : _busy ? "The others are moving. Watch the map." : _presenter.Driver.TurnStatus() ?? "Waiting for the other units to act…";
        }
        else
        {
            title = $"{acting.Name}'s turn";
            var round = _presenter.State.Round;
            (hint, color) = (_busy, _autoplaying) switch
            {
                (true, _) => ("Playing out the last order…", Hud.Palette.TextMuted),
                (_, true) => ("Autoplay is giving the orders (debug).", Hud.Palette.Warn),
                _ when _pinging is { } ping => (CoopPanel.PingHint(ping), Hud.Palette.Amber),
                _ when _aim != TechAim.None => (TechPanel.AimHint(_aim), Hud.Palette.Amber),
                _ when _usingItem is { } item => (_actionPanel.ItemHint(item), Hud.Palette.Amber),
                _ when ownDrone => ($"The drone flies on {acting.Name}'s AP: its orders are under Tech.", Hud.Palette.TextMuted),
                _ when other => ($"{selected!.Name} is selected; these are {acting.Name}'s orders. {Hud.Hotkeys.KeyName("scav_center")} goes back.", Hud.Palette.Warn),
                _ when round <= 3 => ("Click a lit tile to move, a red target to attack with the chosen action.", Hud.Palette.TextMuted),
                _ => ($"{acting.Ap} AP left · {Hud.Hotkeys.KeyName("end_turn")} ends the turn · {Hud.Hotkeys.KeyName("scav_next_unit")} next unit.", Hud.Palette.TextMuted),
            };
        }
        _hud.Acting.Refresh(_presenter, acting, title, hint, color);
        _hud.DimOrders(_busy || acting is null || other);
    }

    /// <summary>Tab: the next of your units on the map after the selected one (the camera glides to it).</summary>
    private void SelectNextUnit()
    {
        var mine = _presenter.State.Units.Where(u => u.OnMap && u.IsActive && _presenter.IsMine(u) && u.Kind != UnitKind.Device)
            .OrderBy(u => u.Id).ToList();
        if (mine.Count == 0) return;
        var next = mine.FirstOrDefault(u => u.Id > (_selectedId ?? int.MinValue)) ?? mine[0];
        Select(next.Id);
        _camera.GlideTo(TileGrid.ToWorld(next.Pos));
    }

    /// <summary>Home / the header's portrait: back to the acting unit, camera on it.</summary>
    private void CenterOnActing()
    {
        if (_presenter.ActingUnit is not { } acting) return;
        Select(acting.Id);
        _camera.GlideTo(TileGrid.ToWorld(acting.Pos));
    }

    /// <summary>Move tiles (Core's Reachable), attack targets with Core's hit chance, or who an item can reach.</summary>
    private void RefreshHighlights(Unit? selected)
    {
        _map.ClearHighlights();
        var previews = new Dictionary<int, WorldTags.Preview>();
        _hud.Tags.SetPreviews(previews);
        if (selected is null || !selected.OnMap || !_presenter.CanSee(selected)) return;
        _map.Highlight(selected.Pos, HighlightKind.Selected);
        if (_busy || _presenter.Done) return;

        var acting = _presenter.ActingUnit;
        var isActing = acting is not null && acting.Id == selected.Id;
        if (isActing && _aim is TechAim.Heal && Drones.DroneOf(_presenter.State, acting!) is { } medic)
        {
            _map.Highlight(medic.Pos, HighlightKind.Selected);
            foreach (var u in _techPanel.HealTargets(acting!, medic)) _map.Highlight(u.Pos, HighlightKind.Use, u.Name);
            return;
        }
        if (isActing && _aim is TechAim.Fire && Drones.DroneOf(_presenter.State, acting!) is { } gunship)
        {
            _map.Highlight(gunship.Pos, HighlightKind.Selected);
            var gun = _presenter.Sim.Profile(gunship).Actions.FirstOrDefault();
            foreach (var t in DroneRoles.FireTargets(_presenter.Sim, acting!).Where(_presenter.CanSee))
            {
                _map.Highlight(t.Pos, HighlightKind.Attack);
                if (gun is not null)
                    previews[t.Id] = new WorldTags.Preview($"DRONE {Combat.ChanceToHit(_presenter.Sim, gunship, gun, t) * 100:0}% · {gun.MinDamage:0}–{gun.MaxDamage:0}",
                        false, gun.ApCost > acting!.Ap);
            }
            return;
        }
        if (isActing && _aim != TechAim.None)
        {
            var targets = _aim == TechAim.Deploy ? _presenter.DeployTiles(acting!) : Drones.FlightTargets(_presenter.Sim, acting!);
            foreach (var p in targets) _map.Highlight(p, HighlightKind.Use);
            return;
        }
        if (isActing && _usingItem is { } placed && _presenter.Sim.Data.Items[placed].Use is { TargetsTile: true } onTile)
        {
            var map = _presenter.State.Map;
            for (var dy = -onTile.Range; dy <= onTile.Range; dy++)
                for (var dx = -onTile.Range; dx <= onTile.Range; dx++)
                    if (acting!.Pos + new Pos(dx, dy) is var p && map.InBounds(p) && (dx, dy) != (0, 0))
                        _map.Highlight(p, HighlightKind.Use);
            return;
        }
        if (isActing && _usingItem is { } item && _presenter.Sim.Data.Items[item].Use is { } use)
        {
            foreach (var u in _presenter.State.Units.Where(u => u.OnMap && !acting!.HostileTo(u) && u.Pos.Chebyshev(acting.Pos) <= use.Range))
                _map.Highlight(u.Pos, HighlightKind.Use, u.Id == acting!.Id ? "self" : u.Name);
            return;
        }

        // Drones fly on their operator's orders and devices stay put: no walking tiles for them.
        if (selected.Kind is UnitKind.Drone or UnitKind.Device) return;
        foreach (var (tile, cost) in _presenter.Reachable(selected))
        {
            if (isActing && cost > selected.Ap) continue;
            _map.Highlight(tile, HighlightKind.Move, cost > 1 ? $"{cost} AP" : null);
        }

        if (!isActing || _actionPanel.ActionId is not { } actionId || _presenter.Sim.Profile(selected).Action(actionId) is not { } action) return;
        foreach (var option in _presenter.AttackOptions(selected, action))
        {
            _map.Highlight(option.Target.Pos, HighlightKind.Attack);
            var ambush = option.Ambush is { Braced: false };
            // True AP from here (a scope costs more up close) and the mod effects that bite on this target.
            var ap = Combat.ApCost(action, selected.Pos, option.Target.Pos);
            var effects = ModEffects(action, option.Target, ap);
            previews[option.Target.Id] = new WorldTags.Preview(
                (ambush ? "AMBUSH " : "") + $"{option.HitChance * 100:0}% · {action.MinDamage:0}–{action.MaxDamage:0}" + (ap > selected.Ap ? " · no AP" : ""),
                ambush, ap > selected.Ap, effects.Count == 0 ? null : string.Join(" · ", effects.Select(e => e.Text)),
                effects.Count > 0 && effects.All(e => e.Network));
        }
    }

    /// <summary>
    /// Short tags for gear-mod effects on an attack at this target: "+1 AP close" (a scope within its close range), each
    /// extra bane that applies ("EMP", "SHOCK", "INCENDIARY"), and armor pierce. EMP and shock against the Network count
    /// as Network effects (cyan when that is all there is).
    /// </summary>
    private List<(string Text, bool Network)> ModEffects(ActionProfile action, Unit target, int ap)
    {
        var tags = new List<(string, bool)>();
        if (ap > action.ApCost) tags.Add(($"+{ap - action.ApCost} AP close", false));
        var enemyTags = target.EnemyDefId is { } id && _presenter.Sim.Data.Enemies.TryGetValue(id, out var def) ? def.Tags : [];
        foreach (var bane in (action.ExtraBanes ?? []).Where(b => enemyTags.Contains(b.Tag)))
        {
            var label = bane.Stun ? "SHOCK" : bane.Tag is "network" or "device" ? "EMP" : bane.Tag == "husk" ? "INCENDIARY" : bane.Tag.ToUpperInvariant();
            if (tags.All(t => t.Item1 != label)) tags.Add((label, bane.Tag is "network"));
        }
        var pierce = action.ArmorPierce;
        if (pierce > 0) tags.Add(($"PIERCE {pierce:0.#}", false));
        return tags;
    }

    private void Toast(string message) => _hud.Toasts.Show("Can't do that", message, "stat_warning", Hud.Palette.Danger);

    private static string? DebugValue(string name) =>
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith($"{name}="))?[(name.Length + 1)..];

    /// <summary>
    /// Debug: <c>--autoplay=N</c> plays N player orders through the normal command path — attack the best visible
    /// target, else loot, else walk toward the exits, else end the turn — so enemy turns replay for screenshots.
    /// Choices come only from what Core reports as reachable or in range.
    /// </summary>
    private async Task Autoplay(int steps)
    {
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        _autoplaying = true;
        for (var i = 0; i < steps && !_presenter.Done; i++)
        {
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Multiplayer: wait (up to a minute) for another player or the AI to hand the turn back.
            for (var wait = 0; _presenter.ActingUnit is null && !_presenter.Done && _presenter.Driver is RemoteDriver && wait < 3600; wait++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_presenter.ActingUnit is not { } u || _busy) break;
            var pid = _presenter.PlayerId;
            var profile = _presenter.Sim.Profile(u);
            var attack = profile.Actions.Where(a => a.ApCost <= u.Ap)
                .SelectMany(a => _presenter.AttackOptions(u, a)).OrderByDescending(o => o.HitChance).FirstOrDefault();
            var exits = _presenter.State.Map.Exits;
            var loot = _presenter.CachesInReach(u).FirstOrDefault();
            IScavengeCommand command;
            if (attack is not null) command = new Attack(pid, u.Id, attack.Target.Id, attack.Action.Id);
            else if (loot is not null && u.Ap >= _presenter.Sim.Data.Rules.Turn.PickUpApCost) command = new PickUp(pid, u.Id, loot.Id, loot.Items[0]);
            else if (_presenter.State.Map.IsExit(u.Pos) && i > steps / 2) command = new Extract(pid, u.Id);
            else
            {
                var step = _presenter.Reachable(u).Where(kv => kv.Value <= u.Ap)
                    .OrderBy(kv => exits.Min(x => x.Chebyshev(kv.Key))).ThenBy(kv => kv.Value)
                    .Select(kv => (Pos?)kv.Key).FirstOrDefault();
                command = step is { } to && exits.Min(x => x.Chebyshev(to)) < exits.Min(x => x.Chebyshev(u.Pos))
                    ? new Move(pid, u.Id, to)
                    : new EndTurn(pid, u.Id);
            }
            if (!await Run(command) && command is not EndTurn) await Run(new EndTurn(pid, u.Id));
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        }
        _autoplaying = false;
        RefreshHud();
    }
}
