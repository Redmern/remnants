using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// Everything written over the map, as HUD chips with backgrounds in one stack per unit so nothing overlaps. From the
/// head up: name with an HP bar (focused, hurt or downed units), status tags (OVERWATCH / ON WATCH / BRACED / LOOKOUT /
/// REVEALED), the attack preview for the chosen action (hit chance, damage, AMBUSH), and short pop-ups (damage, miss,
/// DOWN, AMBUSH!) that rise from the top of the stack. Contacts ("?") the side heard get a chip on their tile unless a
/// seen unit stands there. Purely visual: reads the presenter, positions follow the unit views every frame.
/// </summary>
public partial class WorldTags : Control
{
    private const float HeadHeight = 2.15f;

    /// <summary>An attack preview: the main line, and optional gear-mod tags under it (cyan when they are Network effects).</summary>
    public sealed record Preview(string Text, bool Ambush, bool Unaffordable, string? Effects = null, bool NetworkEffects = false);

    private sealed class Stack
    {
        public VBoxContainer Box = null!;
        public string Signature = "";
        public Vector3 World;
        public bool Visible = true;
        public Vector2 Position = new(float.NaN, float.NaN);
    }

    /// <summary>A rising pop-up: kept here, not as metadata on the node, so placing them each frame stays in managed code.</summary>
    private sealed class Popup
    {
        public PanelContainer Chip = null!;
        public Label Label = null!;
        public StyleBoxFlat Box = null!;
        public Vector3 World;
        public float Offset;
        public float Rise;
        public float Jitter;
        public float Born;
    }

    private readonly List<Popup> _popups = new();

    /// <summary>Pop-ups that have faded, kept for the next (a fight pops dozens; nothing is rebuilt per pop).</summary>
    private readonly Stack<Popup> _popPool = new();
    private readonly Dictionary<int, string> _unitKeys = new();
    private readonly HashSet<string> _wanted = new();
    private readonly List<string> _gone = new();
    private readonly System.Text.StringBuilder _signature = new();

    private ScavengePresenter _presenter = null!;
    private UnitViews _units = null!;
    private Camera3D _camera = null!;
    private readonly Dictionary<string, Stack> _stacks = new();
    private readonly HashSet<int> _focus = new();
    private Dictionary<int, Preview> _previews = new();

    public void Setup(ScavengePresenter presenter, UnitViews units, Camera3D camera)
    {
        _presenter = presenter;
        _units = units;
        _camera = camera;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>The units whose name and HP show: the selected, the hovered and the acting one.</summary>
    public void SetFocus(params int?[] unitIds)
    {
        _focus.Clear();
        foreach (var id in unitIds)
            if (id is { } i) _focus.Add(i);
    }

    /// <summary>Attack previews by target unit for the chosen action (empty when not targeting).</summary>
    public void SetPreviews(Dictionary<int, Preview> previews) => _previews = previews;

    /// <summary>
    /// A short message rising from a spot over the map (damage, miss, AMBUSH!). A juiced pop (<paramref name="scale"/> above
    /// 1, <see cref="Remnants.Core.Scavenge.ReplayJuice.PopScale"/>) lands oversized and settles to its size, and
    /// <paramref name="jitter"/> (pixels) shakes it while it is fresh.
    /// </summary>
    public void Pop(Vector3 world, string text, Color color, bool big = false, float scale = 1, float jitter = 0)
    {
        var anchor = world + Vector3.Up * (HeadHeight + 0.6f);
        // Pop-ups over the same spot queue upwards instead of covering each other.
        var stacked = _popups.Count(p => p.World.DistanceTo(anchor) < 0.5f);
        var popup = TakePopup(text, color, Mathf.RoundToInt((big ? 22 : 17) * Mathf.Max(1, scale)));
        (popup.World, popup.Offset, popup.Jitter, popup.Born, popup.Rise) = (anchor, stacked * 30f * Mathf.Max(1, scale), jitter, Now, 0);
        var chip = popup.Chip;
        _popups.Add(popup);
        if (scale > 1.05f && !GameSettings.ReduceMotion)
        {
            // Lands big and snaps down to size (pivot at its centre once laid out).
            chip.Scale = Vector2.One * 1.45f;
            chip.CreateTween().TweenProperty(chip, "scale", Vector2.One, 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        var tween = chip.CreateTween();
        tween.TweenMethod(Callable.From<float>(v => popup.Rise = v), 0f, 46f, 1.1);
        tween.Parallel().TweenProperty(chip, "modulate:a", 0f, 0.5).SetDelay(0.7);
        tween.TweenCallback(Callable.From(() =>
        {
            _popups.Remove(popup);
            chip.Visible = false;
            _popPool.Push(popup);
        }));
    }

    /// <summary>A pop-up chip from the pool (or a new one), set to the text, colour and size, shown at full opacity.</summary>
    private Popup TakePopup(string text, Color color, int size)
    {
        if (!_popPool.TryPop(out var popup))
        {
            var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            var box = HudTheme.Box(new Color(Palette.Void, 0.82f), color, 2, radius: 3, padX: 6, padY: 1);
            chip.AddThemeStyleboxOverride("panel", box);
            var label = Widgets.Label("", "Number", color, size);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            chip.AddChild(label);
            chip.Resized += () => chip.PivotOffset = chip.Size / 2;
            AddChild(chip);
            popup = new Popup { Chip = chip, Label = label, Box = box };
        }
        popup.Label.Text = text;
        popup.Label.AddThemeColorOverride("font_color", color);
        popup.Label.AddThemeFontSizeOverride("font_size", size);
        popup.Box.BorderColor = color;
        popup.Chip.Visible = true;
        popup.Chip.Modulate = Colors.White;
        popup.Chip.Scale = Vector2.One;
        // Laid out again for the new text (a pooled chip keeps its last size otherwise).
        popup.Chip.Size = Vector2.Zero;
        return popup;
    }

    private static float Now => Time.GetTicksMsec() / 1000f;

    /// <summary>A fresh juiced pop's shake: strong at first, gone after about half a second.</summary>
    private static Vector2 Shaking(Popup popup)
    {
        if (popup.Jitter <= 0) return Vector2.Zero;
        var age = Now - popup.Born;
        var left = Mathf.Max(0, 1 - age / 0.5f);
        if (left <= 0) return Vector2.Zero;
        return new Vector2(Mathf.Sin(age * 71f), Mathf.Cos(age * 59f)) * popup.Jitter * left;
    }

    public override void _Process(double delta)
    {
        if (_presenter is null || !IsVisibleInTree()) return;
        var wanted = _wanted;
        wanted.Clear();
        foreach (var unit in _presenter.State.Units)
        {
            if (!unit.OnMap || !_presenter.CanSee(unit) || _units.WorldOf(unit.Id) is not { } at) continue;
            if (!_unitKeys.TryGetValue(unit.Id, out var key)) _unitKeys[unit.Id] = key = $"u{unit.Id}";
            wanted.Add(key);
            Place(key, at + Vector3.Up * HeadHeight, UnitContent(unit));
        }
        foreach (var contact in _presenter.Contacts)
        {
            if (_presenter.State.UnitAt(contact.Pos) is { } there && _presenter.CanSee(there)) continue;
            var key = $"c{contact.Pos.X},{contact.Pos.Y}";
            if (!wanted.Add(key)) continue;
            var age = _presenter.State.Round - contact.Round;
            var (text, tip) = contact.Kind switch
            {
                ContactKind.Attack => ("?!", "Shots came from here"),
                ContactKind.Sensor => ("?", "A motion sensor tripped here"),
                _ => ("?", "Something was heard here"),
            };
            var color = contact.Kind == ContactKind.Attack ? Palette.Danger : Palette.Amber;
            Place(key, TileGrid.ToWorld(contact.Pos) + Vector3.Up * 1.1f,
                [new(text + (age > 0 ? $" {age}r" : ""), color, "stat_sight", 16, Fade: Mathf.Clamp(1f - age * 0.3f, 0.4f, 1f), Tip: tip)]);
        }
        _gone.Clear();
        foreach (var k in _stacks.Keys)
            if (!wanted.Contains(k)) _gone.Add(k);
        foreach (var gone in _gone)
        {
            _stacks[gone].Box.QueueFree();
            _stacks.Remove(gone);
        }

        _popups.RemoveAll(p => !IsInstanceValid(p.Chip));
        foreach (var popup in _popups)
        {
            if (_camera.IsPositionBehind(popup.World)) continue;
            var p = _camera.UnprojectPosition(popup.World);
            var chip = popup.Chip;
            chip.Position = p - new Vector2(chip.Size.X / 2, chip.Size.Y + popup.Rise + popup.Offset) + Shaking(popup);
        }
    }

    private sealed record Line(string Text, Color Color, string? Icon, int Size = 14, float Fade = 1f, string Tip = "", bool Strong = false, float? Hp = null);

    /// <summary>The stack for a unit, top line first.</summary>
    private List<Line> UnitContent(Unit unit)
    {
        var lines = new List<Line>();
        var mine = unit.Side == _presenter.Side;
        if (_previews.TryGetValue(unit.Id, out var preview))
            lines.Add(new(preview.Text, preview.Ambush ? Palette.Danger : Palette.Text, "ui_attack", 17,
                Fade: preview.Unaffordable ? 0.6f : 1f, Strong: true));
        if (preview?.Effects is { } effects)
            lines.Add(new(effects, preview.NetworkEffects ? Palette.Network : Palette.Amber, "stat_quality", 12, Fade: preview.Unaffordable ? 0.6f : 1f));

        var tags = new List<Line>();
        if (unit.Lookout && mine) tags.Add(new("LOOKOUT", Palette.Text, "stance_scan", 12));
        if (unit.IsActive && Stuns.Active(_presenter.State, unit)) tags.Add(new("STUNNED", Palette.Warn, "stat_warning", 12));
        if (unit.IsActive && unit.Adrenaline >= 1) tags.Add(new($"ADR {unit.Adrenaline:0}", Palette.Amber, "stat_initiative", 12));
        if (!mine && _presenter.IsRevealed(unit)) tags.Add(new("REVEALED", Palette.Danger, "stat_sight", 12));
        if (unit.EmpUntilRound >= _presenter.State.Round && unit.IsActive) tags.Add(new("EMP · OFF RELAY", Palette.Network, "item_emp", 12));
        if (unit.Kind == UnitKind.Decoy && mine)
            tags.Add(new($"DECOY · {Mathf.Max(0, unit.DecoyUntilRound - _presenter.State.Round + 1)}r", Palette.Network, "item_decoy", 12));
        lines.AddRange(tags);

        var downed = unit.Status == UnitStatus.Downed;
        var health = unit.MaxHp > 0 ? Mathf.Clamp((float)(unit.Hp / unit.MaxHp), 0, 1) : 0;
        if (_focus.Contains(unit.Id) || downed || health < 1)
        {
            var name = downed ? $"{unit.Name} · DOWN {(unit.Stabilized ? "stable" : $"{unit.BleedOut}r")}" : unit.Name;
            lines.Add(new(name, downed ? Palette.Danger : SidePalette.For(_presenter, unit).Lightened(0.25f), null, 13,
                Hp: downed ? null : health));
        }
        return lines;
    }

    private void Place(string key, Vector3 world, List<Line> lines)
    {
        if (!_stacks.TryGetValue(key, out var stack))
        {
            stack = new Stack { Box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore } };
            stack.Box.AddThemeConstantOverride("separation", 2);
            AddChild(stack.Box);
            MoveChild(stack.Box, 0); // pop-ups draw over the stacks
            _stacks[key] = stack;
        }
        stack.World = world;
        var sb = _signature.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (i > 0) sb.Append('|');
            // The colour as its packed int: same distinction as its html code, no string per line.
            sb.Append(l.Text).Append(l.Color.ToRgba32()).Append(l.Fade.ToString("0.0")).Append(l.Hp?.ToString("0.00"));
        }
        var signature = sb.ToString();
        if (signature != stack.Signature)
        {
            // Chips are kept per stack and updated in place (a hover's hit preview, an HP change): hover feedback
            // lands the same frame without freeing and re-making the labels.
            stack.Signature = signature;
            for (var i = 0; i < lines.Count; i++)
            {
                if (i == stack.Box.GetChildCount()) stack.Box.AddChild(new TagChip { SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
                var chip = stack.Box.GetChild<TagChip>(i);
                var line = lines[i];
                chip.Set(line.Text, line.Color, line.Icon, line.Size, line.Strong, line.Hp);
                chip.Modulate = new Color(1, 1, 1, line.Fade);
                chip.Visible = true;
            }
            for (var i = lines.Count; i < stack.Box.GetChildCount(); i++) stack.Box.GetChild<TagChip>(i).Visible = false;
            stack.Box.ResetSize();
        }
        var visible = lines.Count > 0 && !_camera.IsPositionBehind(world);
        if (visible != stack.Visible) stack.Box.Visible = stack.Visible = visible;
        if (!visible) return;
        var p = _camera.UnprojectPosition(world);
        var at = (p - new Vector2(stack.Box.Size.X / 2, stack.Box.Size.Y)).Round();
        if (at != stack.Position) stack.Box.Position = stack.Position = at;
    }

    private static PanelContainer Chip(string text, Color color, string? icon, int size, bool strong = false, float? hp = null)
    {
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var box = HudTheme.Box(new Color(Palette.Void, 0.82f), strong ? color : new Color(color, 0.55f), strong ? 2 : 1, radius: 3, padX: 6, padY: 1);
        chip.AddThemeStyleboxOverride("panel", box);
        var col = Widgets.Column(1);
        col.MouseFilter = MouseFilterEnum.Ignore;
        chip.AddChild(col);
        var row = Widgets.Row(4);
        row.MouseFilter = MouseFilterEnum.Ignore;
        row.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddChild(row);
        if (icon is not null) row.AddChild(Icons.Rect(Icons.Get(icon, size), size, color));
        var label = Widgets.Label(text, strong ? "Number" : "", color, size);
        if (!strong) label.AddThemeFontOverride("font", Fonts.BodyBold);
        row.AddChild(label);
        if (hp is { } fraction)
        {
            var bar = new StatBar(null, 0, false) { CustomMinimumSize = new Vector2(52, 4), MouseFilter = MouseFilterEnum.Ignore };
            bar.Set(fraction, "");
            col.AddChild(bar);
        }
        return chip;
    }
}

/// <summary>A world-tag line (stance, preview, name with its HP bar), kept in its stack and updated in place.</summary>
public partial class TagChip : PanelContainer
{
    private readonly StyleBoxFlat _box = HudTheme.Box(new Color(Palette.Void, 0.82f), Palette.Text, 1, radius: 3, padX: 6, padY: 1);
    private readonly TextureRect _icon = Icons.Rect(Icons.Get("ui_info", 14), 14, Palette.Text);
    private readonly Label _label = Widgets.Label("", "");
    private readonly StatBar _bar = new(null, 0, false) { CustomMinimumSize = new Vector2(52, 4), MouseFilter = MouseFilterEnum.Ignore };
    private string? _iconId;
    private int _size;
    private bool? _strong;
    private Color _color;

    public TagChip()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", _box);
        var col = Widgets.Column(1);
        col.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(col);
        var row = Widgets.Row(4);
        row.MouseFilter = MouseFilterEnum.Ignore;
        row.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddChild(row);
        _icon.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_icon);
        row.AddChild(_label);
        col.AddChild(_bar);
    }

    public void Set(string text, Color color, string? icon, int size, bool strong, float? hp)
    {
        if (strong != _strong || color != _color)
        {
            _box.BorderColor = strong ? color : new Color(color, 0.55f);
            _box.SetBorderWidthAll(strong ? 2 : 1);
            if (strong != _strong)
            {
                _label.ThemeTypeVariation = strong ? "Number" : "";
                if (strong) _label.RemoveThemeFontOverride("font");
                else _label.AddThemeFontOverride("font", Fonts.BodyBold);
            }
            _strong = strong;
        }
        if (color != _color)
        {
            _color = color;
            _label.AddThemeColorOverride("font_color", color);
            _icon.Modulate = color;
        }
        if (size != _size)
        {
            _size = size;
            _label.AddThemeFontSizeOverride("font_size", size);
            _icon.CustomMinimumSize = new Vector2(size, size);
            _iconId = null;
        }
        _icon.Visible = icon is not null;
        if (icon is not null && icon != _iconId)
        {
            _iconId = icon;
            _icon.Texture = Icons.Get(icon, size);
        }
        _label.Text = text;
        _bar.Visible = hp is not null;
        if (hp is { } fraction) _bar.Set(fraction, "");
    }
}
