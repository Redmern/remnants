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
    private TacticalCamera _camera = null!;

    private ScavengeHud _hud = null!;
    private TurnBar _turnBar = null!;
    private UnitPanel _unitPanel = null!;
    private ActionPanel _actionPanel = null!;
    private StancePanel _stancePanel = null!;
    private CoopPanel _coopPanel = null!;
    private ScavengeLog _log = null!;
    private ResultPanel _result = null!;

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
        var models = new ScavengeModels(ModelLibrary.Load());
        GD.Print($"Scavenge: zone {_presenter.State.ZoneId}, {_presenter.State.Units.Count} units, {models.ModelCount} generated models.");

        AddEnvironment();
        var map = _presenter.State.Map;
        _camera = new TacticalCamera { Name = "Camera" };
        AddChild(_camera);
        _camera.SetBounds(Vector2.Zero, new Vector2((map.Width - 1) * TileGrid.TileSize, (map.Height - 1) * TileGrid.TileSize));

        _map = new MapView { Name = "Map" };
        AddChild(_map);
        _map.Setup(_presenter, models);
        _units = new UnitViews { Name = "Units" };
        AddChild(_units);
        _units.Setup(_presenter, models);
        _fog = new FogOverlay { Name = "Fog" };
        AddChild(_fog);
        _fog.Setup(_presenter, models);

        BuildHud();
        Wire();
        SelectActing();
        _camera.Focus(PartyCentre());
        SyncAll();
        foreach (var e in FogLog.For(_presenter.Sim, _presenter.Side, _presenter.State.Events).TakeLast(8)) _log.Event(e);

        if (Fog.FogDemo.Requested) _ = FogDemoScript();
        if (float.TryParse(DebugValue("--zoom"), out var zoom)) _camera.Camera.Size = zoom;
        if (int.TryParse(DebugValue("--autoplay"), out var steps)) _ = Autoplay(steps);
        if (int.TryParse(DebugValue("--select-unit"), out var unitId)) CallDeferred(MethodName.SelectForCapture, unitId);
        if (DebugCapture.HasFlag("--pause-menu")) _hud.Pause.CallDeferred(Hud.Overlay.MethodName.Open);
    }

    private void AddEnvironment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.035f, 0.035f, 0.04f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.64f, 0.7f),
            AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55, 35, 0),
            LightColor = new Color(1f, 0.93f, 0.82f),
            LightEnergy = 1.1f,
            ShadowEnabled = true,
        });
    }

    private void BuildHud()
    {
        _hud = new ScavengeHud(_presenter);
        AddChild(_hud);
        _hud.Tags.Setup(_presenter, _units, _camera.Camera);
        (_turnBar, _unitPanel, _actionPanel, _stancePanel, _log, _result) =
            (_hud.TurnBar, _hud.UnitPanel, _hud.Actions, _hud.Stances, _hud.Log, _hud.Result);
        _coopPanel = _hud.Coop;
    }

    private void Wire()
    {
        _presenter.Feedback += (message, error) =>
        {
            _log.Feedback(message, error);
            if (error) Toast(message);
        };
        _hud.EndTurnRequested += OnEndTurn;
        _hud.LeaveRequested += () =>
        {
            _presenter.Driver.Leave();
            Finish();
        };
        _hud.BackRequested += () =>
        {
            if (_usingItem is null && _pinging is null) return false;
            _usingItem = null;
            _pinging = null;
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
        _stancePanel.CommandRequested += command => _ = Run(command);
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
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                if (TileUnder(click.Position) is { } tile) OnTileClicked(tile);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                _usingItem = null;
                _pinging = null;
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
        var result = await _presenter.Execute(command);
        if (result.Ok) _usingItem = null;
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

    private void SyncAll()
    {
        _map.Sync();
        _units.Sync();
        _fog.Sync();
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

    private void RefreshHud()
    {
        var selected = _selectedId is { } id ? _presenter.State.FindUnit(id) : null;
        RefreshFocus();
        _turnBar.Refresh(_busy);
        _unitPanel.Present(selected);
        _actionPanel.Refresh(_busy, _usingItem);
        _stancePanel.Refresh(_busy);
        _coopPanel.Refresh(_busy, selected, _pinging);
        _hud.Banner.Refresh();
        RefreshHighlights(selected);
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
            previews[option.Target.Id] = new WorldTags.Preview(
                (ambush ? "AMBUSH " : "") + $"{option.HitChance * 100:0}% · {action.MinDamage:0}–{action.MaxDamage:0}" + (action.ApCost > selected.Ap ? " · no AP" : ""),
                ambush, action.ApCost > selected.Ap);
        }
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
    }
}
