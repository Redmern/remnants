using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Colony.Ui;
using Remnants.Game.Hud;
using Remnants.Game.Story;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// A story mission's objectives in the scavenge HUD: each active one with its progress (rounds held, kills, carried),
/// done ones ticked, failed ones struck in red, and a warning on those that must not fail. Hidden on a plain run.
/// </summary>
public partial class MissionPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly VBoxContainer _box;
    private readonly Label _title = Widgets.Label("", "Heading");
    private readonly Label _outcome = Widgets.Label("", "Small");
    private readonly List<(HBoxContainer Row, TextureRect Icon, Label Text)> _rows = new();
    private readonly Unchanged _set = new();
    private string _shown = "";

    public MissionPanel() : this(null!) { }

    public MissionPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(ScavengeHud.ColumnWidth, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        _box = Remnants.Game.Colony.Ui.Ui.Panel(this, 4);
        var head = Widgets.Row(6);
        head.AddChild(Icons.Rect(Icons.Get("story_main", 16), 16, Palette.Amber));
        _title.ClipText = true;
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        _box.AddChild(head);
        _box.AddChild(_outcome);
        Visible = false;
    }

    /// <summary>Objective rows are kept and updated in place; a new one (an objective unlocking) eases in.</summary>
    public void Refresh()
    {
        var state = _presenter.State;
        Presence.Set(this, state.Mission is not null);
        if (state.Mission is not { } run) return;
        var key = $"{state.Round}|{run.Outcome}|" + string.Join(",", run.Objectives.Select(o => $"{o.Id}{o.Status}{o.Progress}"));
        if (key == _shown) return;
        _shown = key;
        _set.Text(_title, run.Def.Name);
        _set.Visible(_outcome, run.Outcome != MissionOutcome.Incomplete);
        _set.Text(_outcome, run.Outcome == MissionOutcome.Done ? "Mission complete" : "Mission failed — you can still get out");
        _set.FontColor(_outcome, run.Outcome == MissionOutcome.Done ? Palette.Good : Palette.Danger);
        var n = 0;
        foreach (var def in run.Def.Objectives)
        {
            var s = run.Find(def.Id)!;
            if (s.Status == ObjectiveStatus.Inactive) continue;
            var (icon, color) = s.Status switch
            {
                ObjectiveStatus.Done => ("story_done", Palette.Good),
                ObjectiveStatus.Failed => ("story_failed", Palette.Danger),
                _ => ("story_objective", def.FailsMission ? Palette.Warn : Palette.Amber),
            };
            if (n == _rows.Count)
            {
                var made = Widgets.Row(6);
                var mark = Icons.Rect(Icons.Get(icon, 14), 14, color);
                made.AddChild(mark);
                var line = Widgets.Wrapped("", "Small");
                line.MouseFilter = MouseFilterEnum.Pass;
                made.AddChild(line);
                _box.AddChild(made);
                _rows.Add((made, mark, line));
                Motion.Enter(made, n);
            }
            var (row, iconRect, label) = _rows[n++];
            _set.Visible(row, true);
            _set.Texture(iconRect, Icons.Get(icon, 14));
            _set.Modulate(iconRect, color);
            var text = def.Text + Progress(def, s, state);
            if (def.Optional) text += " (optional)";
            _set.Text(label, text);
            _set.FontColor(label, s.Status == ObjectiveStatus.Done ? Palette.TextMuted : Palette.Text);
            _set.Tooltip(label, def.FailsMission && s.Status == ObjectiveStatus.Active ? "If this fails, the mission fails." : "");
        }
        for (var i = n; i < _rows.Count; i++) _set.Visible(_rows[i].Row, false);
    }

    private static string Progress(ObjectiveDef def, ObjectiveState s, ScavengeState state) => def.Kind switch
    {
        ObjectiveKind.Survive when s.Status == ObjectiveStatus.Active => $"  · round {Mathf.Min(s.Progress + 1, def.Count)}/{def.Count}",
        ObjectiveKind.KillTag when def.Count > 1 => $"  · {s.Progress}/{def.Count}",
        ObjectiveKind.Retrieve when s.Status == ObjectiveStatus.Active && s.Progress > 0 => "  · carried: get it out",
        ObjectiveKind.Defend when s.Status == ObjectiveStatus.Active && state.Units.Find(u => u.Tag == def.Tag) is { } held
            => $"  · {Mathf.Max(0, (float)held.Hp):0}/{held.MaxHp:0}",
        _ => "",
    };
}

/// <summary>
/// In-mission chatter above the bottom bar: the last few radio and field lines, newest last, fading with age. Does not
/// pause; the full lines stay in the scavenge log.
/// </summary>
public partial class ChatterStrip : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly VBoxContainer _box;
    private int _shown = -1;

    public ChatterStrip() : this(null!) { }

    public ChatterStrip(ScavengePresenter presenter)
    {
        _presenter = presenter;
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkEnd;
        CustomMinimumSize = new Vector2(520, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(Palette.Void, 0.78f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6, BorderWidthLeft = 3, BorderColor = Palette.Amber });
        _box = Widgets.Column(2);
        AddChild(_box);
        Visible = false;
    }

    public void Refresh()
    {
        var chatter = _presenter.State.Mission?.Chatter;
        if (chatter is null || chatter.Count == 0) { Visible = false; return; }
        Visible = true;
        if (chatter.Count == _shown) return;
        _shown = chatter.Count;
        Widgets.Clear(_box);
        var lines = chatter.TakeLast(3).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var fresh = i == lines.Count - 1;
            var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(500, 0) };
            label.AddThemeFontSizeOverride("normal_font_size", fresh ? 16 : 14);
            label.Text = $"[color={Palette.Hex(Palette.Amber)}]{line.Speaker}[/color]  [color={Palette.Hex(fresh ? Palette.Text : Palette.TextMuted)}]{line.Text.Replace("[", "[lb]")}[/color]";
            _box.AddChild(label);
        }
    }
}

/// <summary>
/// Amber beams on the tiles a story mission points at: where to go, what to hack, who to treat, escort or defend.
/// Purely visual; only for active objectives the local side can know about.
/// </summary>
public partial class ObjectiveMarkers : Node3D
{
    private ScavengePresenter _presenter = null!;
    private readonly List<MeshInstance3D> _beams = new();
    private string _shown = "";

    /// <summary>Watchtower posts on the raised tiles (fixed for the run), and the beams.</summary>
    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        foreach (var p in presenter.State.Mission?.Raised ?? [])
        {
            var tower = models.Instance("watchtower_l1") ?? new Node3D();
            tower.Position = TileGrid.ToWorld(p);
            tower.Scale = Vector3.One * 0.55f;
            AddChild(tower);
        }
    }

    public void Sync()
    {
        var state = _presenter.State;
        var tiles = new List<Pos>();
        if (state.Mission is { } run)
            foreach (var def in run.Def.Objectives)
            {
                if (run.Find(def.Id) is not { Status: ObjectiveStatus.Active }) continue;
                switch (def.Kind)
                {
                    case ObjectiveKind.Reach: tiles.AddRange(run.Marker(def.Marker).Take(1)); break;
                    case ObjectiveKind.Hack when state.Devices.Find(d => d.Tag == def.Tag) is { } d: tiles.Add(d.Pos); break;
                    case ObjectiveKind.Aid or ObjectiveKind.Escort or ObjectiveKind.Defend
                        when state.Units.Find(u => u.Tag == def.Tag && u.OnMap) is { } u && _presenter.CanSee(u):
                        tiles.Add(u.Pos);
                        break;
                    case ObjectiveKind.Retrieve when state.Caches.Find(c => c.Items.Contains(def.Item!)) is { } c: tiles.Add(c.Pos); break;
                }
            }
        var key = string.Join(";", tiles);
        if (key == _shown) return;
        _shown = key;
        foreach (var b in _beams) b.QueueFree();
        _beams.Clear();
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(Palette.Amber, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = Palette.Amber,
        };
        foreach (var p in tiles.Distinct())
        {
            var beam = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.3f, Height = 3.2f },
                MaterialOverride = material,
                Position = TileGrid.ToWorld(p) + Vector3.Up * 1.6f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(beam);
            _beams.Add(beam);
        }
    }
}
