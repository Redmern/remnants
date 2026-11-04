using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Game.Scavenge.Ui;

namespace Remnants.Game.Scavenge;

/// <summary>Debug: the <c>--tactics-demo</c> script (see <see cref="TacticsDemo"/>).</summary>
public partial class ScavengeScreen
{
    /// <summary>
    /// Frames the staged telegraphs; with <c>picker</c> opens the character picker on a card, with <c>reaction</c> on the
    /// reaction card; with <c>enemy</c> selects the Husk (its card as the bestiary allows); with <c>preview</c> aims the first attack at the Husk so the move tiles show hit chances; then
    /// (with <c>--screenshot=PATH</c>) saves the frame.
    /// </summary>
    private async Task TacticsDemoScript()
    {
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_presenter.ActingUnit is { } u)
        {
            Select(u.Id);
            var husk = _presenter.State.Units.FirstOrDefault(t => t.EnemyDefId == "husk" && _presenter.CanSee(t));
            _camera.GlideTo(husk is null ? TileGrid.ToWorld(u.Pos) : (TileGrid.ToWorld(u.Pos) + TileGrid.ToWorld(husk.Pos)) / 2);
            switch (TacticsDemo.Mode)
            {
                case "picker":
                    _hud.Hand.Choose("steady_aim");
                    break;
                case "reaction":
                    _hud.Hand.Choose("sidestep");
                    break;
                case "discard":
                    // The hand's dead card goes for free, as its Discard button would: it sweeps into the used pile.
                    await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
                    if (_presenter.Hand()?.Cards.FirstOrDefault(c => c.Dead) is { } dead)
                    {
                        var ok = await Run(new Core.Scavenge.DiscardDeadCard(_presenter.PlayerId, u.Id, dead.Id));
                        GD.Print($"Tactics demo: discarded {dead.Id}: {ok}");
                    }
                    else GD.Print("Tactics demo: no dead card in hand");
                    break;
                case "enemy" when husk is not null:
                    Select(husk.Id);
                    break;
                case "targeting" or "smoke" when husk is not null:
                    // The Husk right next to the acting unit, so Shove reaches it; the aim hovers it, tooltip and all.
                    u.Ap = _presenter.Sim.Profile(u).MaxAp; u.Mp = _presenter.Sim.Profile(u).MaxMp;
                    if (Core.Scavenge.Pos.Neighbours8.Select(d => u.Pos + d)
                            .FirstOrDefault(p => _presenter.State.Map.InBounds(p) && _presenter.Sim.Data.Terrain(_presenter.State.Map[p]).Passable
                                                 && _presenter.State.UnitAt(p) is null) is var next && next != default)
                        husk.Pos = next;
                    SyncAll();
                    var card = TacticsDemo.Mode == "smoke" ? "smoke_bomb" : "shove";
                    _hud.Hand.Choose(card);
                    BeginCardAim(card, u.Id, false, false);
                    var aimTile = DemoAimTile(husk.Pos);
                    _hover = aimTile;
                    _map.SetHover(aimTile);
                    RefreshHud();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (_cardAim?.At(aimTile) is { } option)
                        _hud.TargetTip.Show(_cardAim.CardName, husk.Name, option, _camera.Camera.UnprojectPosition(TileGrid.ToWorld(aimTile)));
                    GD.Print($"Tactics demo: aiming {card}, {_cardAim?.Options.Count(o => o.Ok)} of {_cardAim?.Options.Count} targets work; " +
                             $"at {aimTile}: {(_cardAim?.At(aimTile) is { } at ? at.Ok ? at.Outcome : at.Reason : "none")}");
                    break;
                case "plan":
                    // A walk and a push in the plan, then Sweep aimed with its pattern up, so the planning HUD shows it all.
                    u.Ap = _presenter.Sim.Profile(u).MaxAp; u.Mp = _presenter.Sim.Profile(u).MaxMp;
                    var walk = _presenter.Reachable(u).Where(kv => kv.Value == 2).OrderBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Y).Select(kv => kv.Key).FirstOrDefault();
                    await Run(new Core.Scavenge.Move(_presenter.PlayerId, u.Id, walk));
                    while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    _hud.Hand.Choose("sweep");
                    BeginCardAim("sweep", u.Id, false, false);
                    var sweepAt = _cardAim?.Options.FirstOrDefault(o => o.Ok)?.Tile;
                    _hover = sweepAt;
                    _map.SetHover(sweepAt);
                    GD.Print($"Tactics demo: planned a walk to {walk}, aiming sweep at {sweepAt}, {_cardAim?.Options.Count(o => o.Ok)} of {_cardAim?.Options.Count} work");
                    break;
                case "outcomes" when husk is not null:
                    await OutcomesDemo(u, husk);
                    GetTree().Quit();
                    return;
                case "resolve" when husk is not null:
                    await ResolveDemo(u, husk);
                    GetTree().Quit();
                    return;
                case "slots-input":
                    await SlotsInputDemo(u);
                    GetTree().Quit();
                    return;
                case "slots" or "slots-push" or "slots-dash" or "slots-defend" or "slots-recover" or "slots-ally" or "slots-change":
                    await SlotsDemo(u);
                    // Headless there is nothing to look at and no screenshot to wait for.
                    if (TacticsDemo.Mode == "slots-defend" && DisplayServer.GetName() == "headless") GetTree().Quit();
                    break;
                case "preview" when husk is not null:
                    // Fresh AP and the Husk two steps off, so the move tiles in reach of a blow show their chances.
                    u.Ap = _presenter.Sim.Profile(u).MaxAp; u.Mp = _presenter.Sim.Profile(u).MaxMp;
                    if (Core.Scavenge.Pos.Neighbours8.Select(d => new Core.Scavenge.Pos(u.Pos.X + 2 * d.X, u.Pos.Y + 2 * d.Y))
                            .FirstOrDefault(p => _presenter.State.Map.InBounds(p) && _presenter.Sim.Data.Terrain(_presenter.State.Map[p]).Passable
                                                 && _presenter.State.UnitAt(p) is null) is var near && near != default)
                        husk.Pos = near;
                    SyncAll();
                    if (_presenter.Sim.Profile(u).Actions.FirstOrDefault() is { } action) _actionPanel.SetAction(action.Id);
                    _hover = husk.Pos;
                    break;
            }
            RefreshHud();
        }
        await ToSignal(GetTree().CreateTimer(1.4), SceneTreeTimer.SignalName.Timeout);
        // --play-aim: click the aimed target (the Husk) as the player would, replay, and check the models match Core.
        if (DebugCapture.HasFlag("--play-aim") && _cardAim is { } aimed
            && _presenter.State.Units.FirstOrDefault(t => t.EnemyDefId == "husk" && _presenter.CanSee(t)) is { } aimAt)
        {
            ClickCardAim(aimed, DemoAimTile(aimAt.Pos));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var off = _presenter.State.Units.Where(t => t.OnMap && _units.WorldOf(t.Id) is { } w && TileGrid.FromWorld(w) != t.Pos).Select(t => t.Name).ToList();
            GD.Print($"Tactics demo: played {aimed.CardId}; Husk at {aimAt.Pos}, {_presenter.State.FieldEffects.Count} field effects; " +
                     (off.Count == 0 ? "every model on its Core tile" : $"models off their Core tile: {string.Join(", ", off)}"));
        }
        // --play-plan: the aimed pattern goes in the plan, the planning unit's plan is taken back and redone, and everyone commits.
        if (DebugCapture.HasFlag("--play-plan") && _cardAim is { } planned && planned.Options.FirstOrDefault(o => o.Ok) is { } first)
        {
            ClickCardAim(planned, first.Tile);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var planner = _presenter.State.FindUnit(planned.UnitId)!;
            var steps = Core.Scavenge.Plans.Of(_presenter.State, planner)?.Steps.Count ?? 0;
            await Run(new Core.Scavenge.UndoPlan(_presenter.PlayerId, planner.Id));
            var after = Core.Scavenge.Plans.Of(_presenter.State, planner)?.Steps.Count ?? 0;
            var round = _presenter.State.Round;
            for (var i = 0; i < 8 && _presenter.State.Round == round && _presenter.ActingUnit is { } next; i++)
            {
                await Run(new Core.Scavenge.EndTurn(_presenter.PlayerId, next.Id));
                while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            GD.Print($"Tactics demo: plan had {steps} steps, {after} after taking one back; round {round} -> {_presenter.State.Round}, {planner.Name} at {planner.Pos}");
        }
        if (DebugCapture.Value("--screenshot") is { Length: > 0 } path)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
            if (DebugCapture.HasFlag("--quit")) GetTree().Quit();
        }
    }

    /// <summary>
    /// The plan panel by real input: mouse and key events pushed into the viewport, through GUI and hotkey handling as a player's
    /// are: a walk clicked on the map, slot rows clicked clear, the Recover, Push and Dash buttons, aiming clicks and the clear key.
    /// </summary>
    private async Task SlotsInputDemo(Core.Scavenge.Unit u)
    {
        async Task Settled()
        {
            for (var i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        void Say(string what) => GD.Print($"Input demo: {what}: " + string.Join(" | ", Core.Scavenge.TacticsView.Slots(_presenter.Sim, u).Select(s => $"{s.Slot} {s.Used}/{s.Limit} {s.Text}")) + $" | MP {u.Mp} AP {u.Ap} aim {_aim}");
        async Task ClickAt(Vector2 at)
        {
            var viewport = GetViewport();
            viewport.PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            viewport.PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            viewport.PushInput(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false }, true);
            await Settled();
        }
        Task ClickRow(string title) => ClickAt(_hud.Stances.FindChildren("*", nameof(Button), true, false).OfType<ActionButton>().First(b => b.Visible && b.Title.StartsWith(title)).GetGlobalRect().GetCenter());
        Task ClickTile(Core.Scavenge.Pos tile)
        {
            _camera.Focus(TileGrid.ToWorld(tile));
            return ClickWhenFramed(tile);
        }
        async Task ClickWhenFramed(Core.Scavenge.Pos tile)
        {
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            await ClickAt(_camera.Camera.UnprojectPosition(TileGrid.ToWorld(tile)));
        }
        var walks = _presenter.Reachable(u).Where(kv => kv.Value == 2).OrderBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Y).Select(kv => kv.Key).ToList();
        Say("start");
        await ClickTile(walks[0]);
        Say($"mouse on tile {walks[0]}");
        await ClickRow("Recover");
        Say("mouse on Recover");
        await ClickRow("Action:");
        Say("mouse on the Action row");
        await ClickRow("Recover");
        Say("mouse on Recover again");
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, ShiftPressed = true, Pressed = true }, true);
        await Settled();
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, ShiftPressed = true, Pressed = false }, true);
        Say("Shift+Z, the latest slot");
        var east = Core.Scavenge.Plans.Anchor(_presenter.State, u) + new Core.Scavenge.Pos(0, -1);
        await ClickRow("Dash");
        Say("mouse on Dash");
        await ClickTile(east);
        Say($"aim clicked at {east}");
        await ClickRow("Push");
        Say("mouse on Push, to change the action");
        await ClickTile(east);
        Say($"aim clicked at {east}");
        await ClickRow("Move:");
        Say("mouse on the Move row");
        if (DebugCapture.Value("--screenshot") is { Length: > 0 } shot) GD.Print($"Input demo: shot {GetViewport().GetTexture().GetImage().SavePng(shot)}");
    }

    /// <summary><c>--outcome-shots=DIR</c>: each outcome's first showing, a moment in, saved as DIR/Kind.png and named in the log.</summary>
    private void ShootOutcome(Core.Scavenge.ScavengeEvent e)
    {
        if (DebugCapture.Value("--outcome-shots") is not { Length: > 0 }) return;
        if (Core.Scavenge.ReplayFeedback.Of(e) is { } outcome)
        {
            GD.Print($"Outcome: {outcome.Kind} \"{outcome.Text}\" sound {outcome.Sound}");
            Shoot(outcome.Kind.ToString());
        }
        else if (e.Type == Core.Scavenge.ScavengeEventType.PatternResolved)
        {
            GD.Print($"Outcome: pattern {(e.Landed ? "landed" : "failed")} on {string.Join(",", e.Tiles ?? [])}");
            Shoot(e.Landed ? "PatternLanded" : "PatternFailed", 0.25);
        }
    }

    private readonly System.Collections.Generic.HashSet<string> _shot = [];

    private async void Shoot(string name, double after = 0.14)
    {
        if (DebugCapture.Value("--outcome-shots") is not { Length: > 0 } dir || !_shot.Add(name)) return;
        await ToSignal(GetTree().CreateTimer(after), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"Outcome shot {name}: {GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}.png")}");
    }

    /// <summary>
    /// Every outcome in turn through the real replay, on made-up events (a real round cannot be made to roll them all), then a unit
    /// run low on stamina. The rules' side of these events is in ReplayFeedbackTests.
    /// </summary>
    private async Task OutcomesDemo(Core.Scavenge.Unit u, Core.Scavenge.Unit husk)
    {
        var state = _presenter.State;
        var map = state.Map;
        bool Free(Core.Scavenge.Pos p) => map.InBounds(p) && _presenter.Sim.Data.Terrain(map[p]).Passable && state.UnitAt(p) is null;
        var way = Core.Scavenge.Pos.Neighbours8.First(d => d.X != 0 && d.Y == 0 || d.X == 0 && d.Y != 0 ? Free(u.Pos + d) && Free(u.Pos + d + d) && Free(u.Pos + d + d + d) : false);
        var (start, dashed, beside) = (u.Pos, u.Pos + way + way, u.Pos + way + way + way);
        husk.Pos = beside;
        SyncAll();
        var shoved = Core.Scavenge.Pos.Neighbours8.First(d => Free(beside + d) && beside + d != dashed && (beside + d).Chebyshev(dashed) == 2) + beside;
        var round = state.Round;
        Core.Scavenge.ScavengeEvent E(Core.Scavenge.ScavengeEventType type, string text, int? unit, int? target = null, Core.Scavenge.Pos? at = null, double? amount = null,
            System.Collections.Generic.List<Core.Scavenge.Pos>? tiles = null, bool landed = false) => new(round, type, text, unit, target, at, Amount: amount, Tiles: tiles, Landed: landed);
        var hit = new System.Collections.Generic.List<Core.Scavenge.Pos> { beside };
        var events = new System.Collections.Generic.List<Core.Scavenge.ScavengeEvent>
        {
            E(Core.Scavenge.ScavengeEventType.Moved, $"{u.Name} dashes to {dashed}.", u.Id, at: dashed),
            E(Core.Scavenge.ScavengeEventType.PatternResolved, "", u.Id, at: dashed, tiles: [start + way, dashed], landed: true),
            E(Core.Scavenge.ScavengeEventType.Attacked, $"{u.Name} hit {husk.Name}'s Torso for 12 (Pipe).", u.Id, husk.Id, beside, 12),
            E(Core.Scavenge.ScavengeEventType.AdrenalineChanged, "", u.Id, at: dashed, amount: 10),
            E(Core.Scavenge.ScavengeEventType.PatternResolved, "", u.Id, at: dashed, tiles: hit, landed: true),
            E(Core.Scavenge.ScavengeEventType.Missed, $"{u.Name} missed {husk.Name}.", u.Id, husk.Id, beside),
            E(Core.Scavenge.ScavengeEventType.PatternResolved, "", u.Id, at: dashed, tiles: hit),
            E(Core.Scavenge.ScavengeEventType.IntentFizzled, $"{u.Name}'s strike falls through: Pipe hits nothing.", u.Id, at: dashed),
            E(Core.Scavenge.ScavengeEventType.Blocked, $"{u.Name}'s block takes 40% off the hit on {husk.Name}.", u.Id, husk.Id, beside),
            E(Core.Scavenge.ScavengeEventType.Blocked, $"{u.Name}'s perfect block takes 70% off the hit on {husk.Name}.", u.Id, husk.Id, beside),
            E(Core.Scavenge.ScavengeEventType.Stunned, $"{husk.Name} is stunned (the block).", u.Id, husk.Id, beside),
            E(Core.Scavenge.ScavengeEventType.Displaced, $"{husk.Name} is shoved.", u.Id, husk.Id, shoved),
            E(Core.Scavenge.ScavengeEventType.Collided, $"{husk.Name} slams into a wall.", u.Id, husk.Id, shoved),
            E(Core.Scavenge.ScavengeEventType.AdrenalineChanged, "", u.Id, at: dashed, amount: -5),
        };
        u.Pos = dashed;
        husk.Pos = shoved;
        u.Adrenaline = 30;
        husk.StunnedRound = round + 1;
        await _units.Replay(events, Enumerable.Range(0, map.Width * map.Height).ToHashSet(), OnReplayStep);
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        u.Fatigue = _presenter.Sim.Profile(u).MaxStamina - _presenter.Sim.Data.Rules.Turn.StaminaPerAp;
        SyncAll();
        Shoot("LowStamina");
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>
    /// A real round through the real resolve: every unit walks, then one strikes an empty tile, one pushes the Husk and one recovers.
    /// Prints each step of the playback and where the camera is, to check the order.
    /// </summary>
    private async Task ResolveDemo(Core.Scavenge.Unit u, Core.Scavenge.Unit husk)
    {
        var state = _presenter.State;
        var pid = _presenter.PlayerId;
        _units.StepStarted += e => GD.Print($"Step: {e.Type} unit {e.UnitId}: {e.Message}");
        var party = state.Units.Where(f => f.PlayerId == pid && f.OnMap).OrderBy(f => f.Id).ToList();
        foreach (var f in party)
        {
            var walk = _presenter.Reachable(f).Where(kv => kv.Value == 2).OrderBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Y).Select(kv => kv.Key).FirstOrDefault(p => state.UnitAt(p) is null);
            if (walk != default) GD.Print($"Plan: {f.Name} walk to {walk}: {(await _presenter.Execute(new Core.Scavenge.Move(pid, f.Id, walk))).Message}");
        }
        var way = new Core.Scavenge.Pos(1, 0);
        var pusher = party[0];
        husk.Pos = Core.Scavenge.Plans.Anchor(state, pusher) + way;
        GD.Print($"Plan: {pusher.Name} push: {(await _presenter.Execute(new Core.Scavenge.Push(pid, pusher.Id, way))).Message}");
        if (party.Count > 1)
        {
            var striker = party[1];
            var action = _presenter.Sim.Profile(striker).Actions[0];
            var aim = Core.Scavenge.TileStrikes.Reach(_presenter.Sim, striker, action).Where(p => state.UnitAt(p) is null).OrderBy(p => p.X).ThenBy(p => p.Y).First();
            GD.Print($"Plan: {striker.Name} strike {aim}: {(await _presenter.Execute(new Core.Scavenge.AttackTile(pid, striker.Id, aim, action.Id))).Message}");
        }
        if (party.Count > 2) GD.Print($"Plan: {party[2].Name} recover: {(await _presenter.Execute(new Core.Scavenge.Recover(pid, party[2].Id))).Message}");
        SyncAll();
        foreach (var f in party) await Run(new Core.Scavenge.EndTurn(pid, f.Id));
        while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"Resolve demo: round now {state.Round}");
    }

    /// <summary>
    /// The Move, Action and Card slots, through the same clicks a player makes: a walk, then per mode an action aimed or planned
    /// and a card, or (slots-change) the move, action and card changed and cleared. Prints each step for the log.
    /// </summary>
    private async Task SlotsDemo(Core.Scavenge.Unit u)
    {
        async Task Settled()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            SelectActing();
        }
        void Say(string what) => GD.Print($"Slots demo: {what}: " + string.Join(" | ", Core.Scavenge.TacticsView.Slots(_presenter.Sim, u).Select(s => $"{s.Slot} {s.Used}/{s.Limit} {s.Text}")) + $" | MP {u.Mp} AP {u.Ap} STA {Core.Scavenge.Economy.Stamina(_presenter.Sim, u)}");
        var pid = _presenter.PlayerId;
        var walks = _presenter.Reachable(u).Where(kv => kv.Value == 2).OrderBy(kv => kv.Key.X).ThenBy(kv => kv.Key.Y).Select(kv => kv.Key).ToList();
        Say("start");
        OnTileClicked(walks[0]);
        await Settled();
        Say($"clicked {walks[0]}");
        var end = Core.Scavenge.Plans.Anchor(_presenter.State, u);
        var east = end + new Core.Scavenge.Pos(1, 0);
        switch (TacticsDemo.Mode)
        {
            case "slots-push" or "slots-dash":
                _aim = TacticsDemo.Mode == "slots-push" ? TechAim.Push : TechAim.Dash;
                // Someone to shove, for the push's preview of where they end up.
                if (TacticsDemo.Mode == "slots-push") DemoFoeAt(east);
                _hover = east;
                _map.SetHover(east);
                RefreshHud();
                Say($"aiming {_aim} at {east}");
                await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
                if (DebugCapture.Value("--screenshot") is { Length: > 0 } aimShot)
                {
                    GD.Print($"Slots demo: aim shot {GetViewport().GetTexture().GetImage().SavePng(aimShot.Replace(".png", "_aim.png"))}");
                    if (DisplayServer.GetName() != "headless") GetTree().Quit();
                }
                OnTileClicked(east);
                await Settled();
                Say("clicked the aim");
                break;
            case "slots-defend":
                // The real hotkey, the real aim, the real click, then the round resolves.
                // Someone with it in reach, for the guard's odds.
                DemoFoeAt(east);
                var pressed = _actionPanel.PressHotkey(new InputEventKey { Keycode = Key.G, ShiftPressed = true, Pressed = true });
                Say($"hotkey pressed {pressed}, aim {_aim}");
                _hover = east;
                _map.SetHover(east);
                RefreshHud();
                GD.Print($"Slots demo: aim tiles {string.Join(",", Core.Scavenge.TacticsView.DefendAim(_presenter.Sim, u, Core.Scavenge.TacticsView.AimDirection(_presenter.Sim, u, east)).Tiles)} from {end}");
                await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
                if (DebugCapture.Value("--screenshot") is { Length: > 0 } defendShot)
                    GD.Print($"Slots demo: aim shot {GetViewport().GetTexture().GetImage().SavePng(defendShot.Replace(".png", "_aim.png"))}");
                OnTileClicked(east);
                await Settled();
                Say("clicked the aim");
                GD.Print($"Slots demo: planned guard tiles {string.Join(",", Core.Scavenge.TacticsView.PlanPreview(_presenter.Sim, u).ActionTiles)}");
                if (DisplayServer.GetName() != "headless")
                {
                    await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
                    if (DebugCapture.Value("--screenshot") is { Length: > 0 } planShot)
                        GD.Print($"Slots demo: plan shot {GetViewport().GetTexture().GetImage().SavePng(planShot.Replace(".png", "_plan.png"))}");
                }
                foreach (var f in _presenter.State.Units.Where(f => f.PlayerId == pid && f.OnMap && f.IsActive).ToList()) await Run(new Core.Scavenge.EndTurn(pid, f.Id));
                while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                foreach (var e in _presenter.State.Events.Where(e => e.Type == Core.Scavenge.ScavengeEventType.PatternResolved && e.UnitId == u.Id))
                    GD.Print($"Slots demo: resolved pattern on {string.Join(",", e.Tiles ?? [])} landed {e.Landed}");
                GD.Print($"Slots demo: round now {_presenter.State.Round}");
                if (DisplayServer.GetName() != "headless" && DebugCapture.Value("--screenshot") is { Length: > 0 } doneShot)
                {
                    GD.Print($"Slots demo: resolved shot {GetViewport().GetTexture().GetImage().SavePng(doneShot.Replace(".png", "_resolved.png"))}");
                    GetTree().Quit();
                }
                break;
            case "slots-recover":
                await Run(new Core.Scavenge.Recover(pid, u.Id));
                await Settled();
                Say("recover");
                break;
            case "slots-ally":
                // A friend beside where the plan ends: Sweep's aim warns of them.
                var friend = _presenter.State.UnitAt(east) ?? _presenter.State.Units.First(f => f.PlayerId == pid && f.Id != u.Id && f.OnMap);
                friend.Pos = east;
                SyncAll();
                _hud.Hand.Choose("sweep");
                BeginCardAim("sweep", u.Id, false, false);
                _hover = east;
                _map.SetHover(east);
                RefreshHud();
                GD.Print($"Slots demo: ally warning tiles {string.Join(",", Core.Scavenge.TacticsView.PatternAim(_presenter.Sim, u, "sweep", null, Core.Scavenge.TacticsView.AimDirection(_presenter.Sim, u, east)).AllyTiles)}");
                break;
            case "slots-change":
                OnTileClicked(walks[^1]);
                await Settled();
                Say($"changed the move to {walks[^1]}");
                await Run(new Core.Scavenge.Recover(pid, u.Id));
                await Settled();
                Say("planned recover");
                await Run(new Core.Scavenge.Push(pid, u.Id, new Core.Scavenge.Pos(1, 0)));
                await Settled();
                Say("changed the action to a push");
                await Run(new Core.Scavenge.PlayCard(pid, u.Id, "war_cry"));
                await Settled();
                Say("played war_cry");
                await Run(new Core.Scavenge.UndoPlan(pid, u.Id, Slot: Core.Scavenge.PlanSlot.Card));
                await Settled();
                Say($"cleared the card (adrenaline {u.Adrenaline})");
                await Run(new Core.Scavenge.UndoPlan(pid, u.Id, Slot: Core.Scavenge.PlanSlot.Move));
                await Settled();
                Say("cleared the move");
                break;
            default:
                await Run(new Core.Scavenge.Push(pid, u.Id, new Core.Scavenge.Pos(1, 0)));
                await Settled();
                await Run(new Core.Scavenge.PlayCard(pid, u.Id, "war_cry"));
                await Settled();
                Say("all three");
                break;
        }
        RefreshHud();
    }

    /// <summary>
    /// The demo's aim: the Husk's tile when a target there works (Shove), else the working target nearest it (a Smoke Bomb
    /// is a screen, never over the player next to it).
    /// </summary>
    private Core.Scavenge.Pos DemoAimTile(Core.Scavenge.Pos husk) =>
        _cardAim?.At(husk) is { Ok: true } || _cardAim is null ? husk
        : _cardAim.Options.Where(o => o.Ok).OrderBy(o => o.Tile.Chebyshev(husk)).ThenBy(o => o.Tile.Y).ThenBy(o => o.Tile.X)
            .Select(o => o.Tile).DefaultIfEmpty(husk).First();

    /// <summary>The demo's first enemy on that tile, anyone there stepping aside first.</summary>
    private void DemoFoeAt(Core.Scavenge.Pos tile)
    {
        var state = _presenter.State;
        if (state.Units.FirstOrDefault(t => t.Kind == Core.Scavenge.UnitKind.Enemy) is not { } foe) return;
        if (state.UnitAt(tile) is { } there && there.Id != foe.Id)
            there.Pos = Core.Scavenge.Pos.Neighbours8.Select(d => tile + d)
                .First(p => state.Map.InBounds(p) && _presenter.Sim.Data.Terrain(state.Map[p]).Passable && state.UnitAt(p) is null);
        foe.Pos = tile;
        SyncAll();
    }
}
