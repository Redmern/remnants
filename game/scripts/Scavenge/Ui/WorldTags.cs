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

    public sealed record Preview(string Text, bool Ambush, bool Unaffordable);

    private sealed class Stack
    {
        public VBoxContainer Box = null!;
        public string Signature = "";
        public Vector3 World;
    }

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

    /// <summary>A short message rising from a spot over the map (damage, miss, AMBUSH!).</summary>
    public void Pop(Vector3 world, string text, Color color, bool big = false)
    {
        var chip = Chip(text, color, null, big ? 22 : 17, strong: true);
        var anchor = world + Vector3.Up * (HeadHeight + 0.6f);
        // Pop-ups over the same spot queue upwards instead of covering each other.
        var stacked = GetChildren().OfType<Control>().Count(c => c.HasMeta("rise") && c.GetMeta("world").AsVector3().DistanceTo(anchor) < 0.5f);
        chip.SetMeta("world", anchor);
        chip.SetMeta("offset", stacked * 30f);
        chip.SetMeta("rise", 0f);
        AddChild(chip);
        var tween = chip.CreateTween();
        tween.TweenMethod(Callable.From<float>(v => chip.SetMeta("rise", v)), 0f, 46f, 1.1);
        tween.Parallel().TweenProperty(chip, "modulate:a", 0f, 0.5).SetDelay(0.7);
        tween.TweenCallback(Callable.From(chip.QueueFree));
    }

    public override void _Process(double delta)
    {
        if (_presenter is null || !IsVisibleInTree()) return;
        var wanted = new HashSet<string>();
        foreach (var unit in _presenter.State.Units)
        {
            if (!unit.OnMap || !_presenter.CanSee(unit) || _units.WorldOf(unit.Id) is not { } at) continue;
            var key = $"u{unit.Id}";
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
        foreach (var gone in _stacks.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _stacks[gone].Box.QueueFree();
            _stacks.Remove(gone);
        }

        foreach (var child in GetChildren().OfType<Control>().Where(c => c.HasMeta("rise")))
        {
            var world = child.GetMeta("world").AsVector3();
            if (_camera.IsPositionBehind(world)) continue;
            var p = _camera.UnprojectPosition(world);
            child.Position = p - new Vector2(child.Size.X / 2, child.Size.Y + child.GetMeta("rise").AsSingle() + child.GetMeta("offset").AsSingle());
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

        var tags = new List<Line>();
        if (unit.Stance == Stance.Overwatch)
            tags.Add(new(mine ? "OVERWATCH" : "ON WATCH", mine ? Palette.Amber : Palette.Danger, "stance_overwatch", 12));
        else if (unit.Stance == Stance.Brace && mine) tags.Add(new("BRACED", Palette.Text, "stance_brace", 12));
        if (unit.Lookout && mine) tags.Add(new("LOOKOUT", Palette.Text, "stance_scan", 12));
        if (!mine && _presenter.IsRevealed(unit)) tags.Add(new("REVEALED", Palette.Danger, "stat_sight", 12));
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
        var signature = string.Join("|", lines.Select(l => $"{l.Text}{l.Color.ToHtml()}{l.Fade:0.0}{l.Hp:0.00}"));
        if (signature != stack.Signature)
        {
            stack.Signature = signature;
            Widgets.Clear(stack.Box);
            foreach (var line in lines)
            {
                var chip = Chip(line.Text, line.Color, line.Icon, line.Size, line.Strong, line.Hp);
                chip.Modulate = new Color(1, 1, 1, line.Fade);
                chip.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
                stack.Box.AddChild(chip);
            }
            stack.Box.ResetSize();
        }
        stack.Box.Visible = lines.Count > 0 && !_camera.IsPositionBehind(world);
        if (!stack.Box.Visible) return;
        var p = _camera.UnprojectPosition(world);
        stack.Box.Position = (p - new Vector2(stack.Box.Size.X / 2, stack.Box.Size.Y)).Round();
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
