using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Colony;
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
    private TacticalCamera _camera = null!;

    private TurnBar _turnBar = null!;
    private UnitPanel _unitPanel = null!;
    private ActionPanel _actionPanel = null!;
    private ScavengeLog _log = null!;
    private ResultPanel _result = null!;
    private Label _toast = null!;
    private Tween? _toastTween;

    private int? _selectedId;
    private int? _lastActingId;
    private string? _usingItem;
    private bool _busy;
    private Pos? _hover;

    public override void _Ready()
    {
        _mission = ScavengeSession.TakeMission();
        _presenter = new ScavengePresenter(_mission.Sim, _mission.PlayerId);
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

        BuildHud();
        Wire();
        SelectActing();
        if (_presenter.ActingUnit is { } acting) _camera.Focus(TileGrid.ToWorld(acting.Pos));
        SyncAll();
        foreach (var e in _presenter.State.Events.Where(Shown).TakeLast(8)) _log.Event(e);

        if (DebugCapture.FromCommandLine() is { } capture) AddChild(capture);
        if (float.TryParse(DebugValue("--zoom"), out var zoom)) _camera.Camera.Size = zoom;
        if (int.TryParse(DebugValue("--autoplay"), out var steps)) _ = Autoplay(steps);
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
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        var root = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) root.AddThemeConstantOverride($"margin_{side}", 8);
        layer.AddChild(root);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(column);
        _turnBar = new TurnBar(_presenter);
        column.AddChild(_turnBar);

        var middle = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(middle);
        _unitPanel = new UnitPanel(_presenter);
        middle.AddChild(_unitPanel);
        middle.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var right = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        middle.AddChild(right);
        _actionPanel = new ActionPanel(_presenter);
        right.AddChild(_actionPanel);

        _toast = Colony.Ui.Ui.Label("", 17, Colony.Ui.Ui.Bad);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.AddThemeConstantOverride("outline_size", 6);
        _toast.AddThemeColorOverride("font_outline_color", Colors.Black);
        _toast.MouseFilter = Control.MouseFilterEnum.Ignore;
        column.AddChild(_toast);

        _log = new ScavengeLog();
        column.AddChild(_log);

        _result = new ResultPanel { Visible = false };
        layer.AddChild(_result);
    }

    private void Wire()
    {
        _presenter.Feedback += (message, error) =>
        {
            _log.Feedback(message, error);
            if (error) Toast(message);
        };
        _turnBar.EndTurnPressed += OnEndTurn;
        _turnBar.LeavePressed += () => ScavengeSession.End(GetTree(), _presenter.Sim.ResultFor(_presenter.PlayerId));
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
        _result.ContinuePressed += () => ScavengeSession.End(GetTree(), _presenter.Sim.ResultFor(_presenter.PlayerId));
    }

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
                }
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                if (TileUnder(click.Position) is { } tile) OnTileClicked(tile);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                _usingItem = null;
                SelectActing();
                RefreshHud();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space or Key.Enter }:
                OnEndTurn();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                _usingItem = null;
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

        if (_usingItem is { } item && acting is not null)
        {
            _usingItem = null;
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
        var seenBefore = new HashSet<int>(_presenter.Visible);
        var result = _presenter.Execute(command);
        if (!result.Ok)
        {
            RefreshHud();
            return false;
        }

        var events = _presenter.TakeNewEvents();
        seenBefore.UnionWith(_presenter.Visible);
        _busy = true;
        _usingItem = null;
        _map.ClearHighlights();
        RefreshHud();
        _map.Sync();
        await _units.Replay(events, seenBefore, e =>
        {
            if (Shown(e, seenBefore)) _log.Event(e);
        });
        _busy = false;

        if (_presenter.ActingUnit is { } acting && acting.Id != _lastActingId)
        {
            Select(acting.Id);
            _camera.GlideTo(TileGrid.ToWorld(acting.Pos));
        }
        SyncAll();
        return true;
    }

    /// <summary>Whether the local player gets to hear about an event (no news of enemies moving in the dark).</summary>
    private bool Shown(ScavengeEvent e) => Shown(e, _presenter.Visible);

    private bool Shown(ScavengeEvent e, IReadOnlySet<int> visible)
    {
        if (e.Type is ScavengeEventType.RoundStarted or ScavengeEventType.PartyDone or ScavengeEventType.Finished) return true;
        bool Mine(int? id) => id is { } i && _presenter.State.FindUnit(i) is { } u && u.Side == _presenter.Side;
        if (Mine(e.UnitId) || Mine(e.TargetId)) return true;
        var map = _presenter.State.Map;
        return e.Pos is { } p && map.InBounds(p) && visible.Contains(map.Index(p));
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
        RefreshHud();
        if (_presenter.Done && !_result.Visible)
            _result.Present(_presenter.Sim.Data, _presenter.Sim.ResultFor(_presenter.PlayerId));
    }

    private void RefreshHud()
    {
        var selected = _selectedId is { } id ? _presenter.State.FindUnit(id) : null;
        _turnBar.Refresh(_busy);
        _unitPanel.Present(selected);
        _actionPanel.Refresh(_busy, _usingItem);
        RefreshHighlights(selected);
    }

    /// <summary>Move tiles (Core's Reachable), attack targets with Core's hit chance, or who an item can reach.</summary>
    private void RefreshHighlights(Unit? selected)
    {
        _map.ClearHighlights();
        if (selected is null || !selected.OnMap || !_presenter.CanSee(selected)) return;
        _map.Highlight(selected.Pos, HighlightKind.Selected);
        if (_busy || _presenter.Done) return;

        var acting = _presenter.ActingUnit;
        var isActing = acting is not null && acting.Id == selected.Id;
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
            _map.Highlight(option.Target.Pos, HighlightKind.Attack,
                action.ApCost > selected.Ap ? $"{option.HitChance:P0} · no AP" : $"{option.HitChance:P0}");
    }

    private void Toast(string message)
    {
        _toast.Text = message;
        _toast.Modulate = Colors.White;
        _toastTween?.Kill();
        _toastTween = CreateTween();
        _toastTween.TweenInterval(2.2);
        _toastTween.TweenProperty(_toast, "modulate:a", 0f, 0.6);
    }

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
            if (_presenter.ActingUnit is not { } u) break;
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
