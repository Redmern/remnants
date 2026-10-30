using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge.Fog;

/// <summary>
/// What the fog of war adds to the map, all of it read from Core state the local side may know (purely visual):
/// <list type="bullet">
/// <item>contacts — a disc (<c>contact_marker</c>, amber) where the side heard something, fading with age;</item>
/// <item>overwatch — an eye ring (<c>overwatch_marker</c>) under units on overwatch, and for the local side's own a
///   soft amber ring at their sight radius, only while that unit is selected or hovered (<see cref="SetFocus"/>);</item>
/// <item>a red "revealed" ring on hostiles revealed to the side;</item>
/// <item>burning flares (<c>item_flare_lit</c> + light + a disc over the lit area) and the side's own motion sensors
///   (<c>item_motion_sensor</c> + its radius);</item>
/// <item>incoming fire — on an ambush of one of the side's units, a red streak from where it came from (the attacker if
///   seen, else the attack contact) to the victim.</item>
/// </list>
/// Rebuilt on every <see cref="Sync"/>. Text (the "?" of contacts, OVERWATCH / BRACED / LOOKOUT / REVEALED, AMBUSH!) is
/// drawn by the HUD (<see cref="Ui.WorldTags"/>, fed by <see cref="Popped"/>) so it stacks with names and previews.
/// Cyan stays reserved for the Network: the player's own overwatch is amber.
/// </summary>
public partial class FogOverlay : Node3D
{
    public static readonly Color ContactColor = Hud.Palette.Amber;
    public static readonly Color OverwatchColor = Hud.Palette.Amber;
    public static readonly Color HostileOverwatchColor = Hud.Palette.Danger;
    public static readonly Color RevealedColor = Hud.Palette.Danger;
    public static readonly Color FlareColor = new(1f, 0.45f, 0.3f);
    public static readonly Color SensorColor = new(0.55f, 1f, 0.55f);

    private ScavengePresenter _presenter = null!;
    private ScavengeModels _models = null!;
    private Node3D _markers = null!;
    private readonly Dictionary<int, Node3D> _watchRanges = new();
    private readonly HashSet<int> _focus = new();

    /// <summary>A pop-up for the HUD over a spot: text, colour.</summary>
    public event System.Action<Vector3, string, Color>? Popped;

    /// <summary>Overwatch ranges show only for these units (selected, hovered).</summary>
    public void SetFocus(params int?[] unitIds)
    {
        _focus.Clear();
        foreach (var id in unitIds)
            if (id is { } i) _focus.Add(i);
        foreach (var (id, range) in _watchRanges) range.Visible = _focus.Contains(id);
    }

    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        _models = models;
        _markers = new Node3D { Name = "Markers" };
        AddChild(_markers);
    }

    public void Sync()
    {
        foreach (var child in _markers.GetChildren()) child.QueueFree();
        _watchRanges.Clear();
        var state = _presenter.State;

        foreach (var contact in _presenter.Contacts)
        {
            var age = state.Round - contact.Round;
            var fade = Mathf.Clamp(1f - age * 0.35f, 0.35f, 1f);
            var color = contact.Kind == ContactKind.Attack ? RevealedColor : ContactColor;
            var marker = Marker("contact_marker", color with { A = 0.8f * fade }, 0.8f);
            marker.Position = TileGrid.ToWorld(contact.Pos) + Vector3.Up * 0.06f;
            _markers.AddChild(marker);
        }

        foreach (var unit in state.Units.Where(u => u.OnMap && _presenter.CanSee(u)))
        {
            var at = TileGrid.ToWorld(unit.Pos);
            var mine = unit.Side == _presenter.Side;
            if (unit.Stance == Stance.Overwatch)
            {
                var marker = Marker("overwatch_marker", (mine ? OverwatchColor : HostileOverwatchColor) with { A = 0.85f }, 0.9f);
                marker.Position = at + Vector3.Up * 0.05f;
                _markers.AddChild(marker);
                if (mine)
                {
                    var range = WatchRange(at, (float)_presenter.Sim.Profile(unit).Sight * TileGrid.TileSize);
                    range.Visible = _focus.Contains(unit.Id);
                    _watchRanges[unit.Id] = range;
                    _markers.AddChild(range);
                }
            }
            if (!mine && _presenter.IsRevealed(unit)) _markers.AddChild(Ring(at, 0.95f, RevealedColor with { A = 0.9f }, 0.08f));
        }

        foreach (var flare in state.Flares.Where(f => f.UntilRound >= state.Round))
        {
            var at = TileGrid.ToWorld(flare.Pos);
            // On a colony map a flare that never burns out is a floodlight: the post is the map's, the light cold white.
            var lamp = _models.ColonyMap && flare.UntilRound == int.MaxValue;
            if (!lamp)
            {
                var model = _models.Prop("item_flare_lit") ?? Placeholder(new Color(1f, 0.3f, 0.2f));
                model.Position = at;
                _markers.AddChild(model);
            }
            _markers.AddChild(new OmniLight3D
            {
                Position = at + Vector3.Up * (lamp ? 3f : 1.5f),
                LightColor = lamp ? new Color(0.95f, 0.95f, 0.85f) : new Color(1f, 0.55f, 0.4f),
                LightEnergy = 2.5f,
                OmniRange = (float)flare.Radius * TileGrid.TileSize + 1.5f,
            });
            _markers.AddChild(Disc(at, (float)flare.Radius * TileGrid.TileSize, FlareColor with { A = 0.12f }));
        }

        // Team pings: a coloured pin over the tile and a ring, fading as they age.
        foreach (var ping in _presenter.Pings)
        {
            var at = TileGrid.ToWorld(ping.Pos);
            var color = Ui.CoopPanel.PingColor(ping.Kind);
            var fade = state.Round > ping.Round ? 0.55f : 1f;
            _markers.AddChild(Ring(at, 0.7f, color with { A = 0.9f * fade }, 0.07f));
            _markers.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 1.4f },
                MaterialOverride = Material(color with { A = 0.8f * fade }),
                Position = at + Vector3.Up * 0.7f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            _markers.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.16f, Height = 0.32f },
                MaterialOverride = Material(color with { A = fade }),
                Position = at + Vector3.Up * 1.5f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        foreach (var sensor in state.Sensors.Where(x => x.Side == _presenter.Side))
        {
            var at = TileGrid.ToWorld(sensor.Pos);
            var model = _models.Prop("item_motion_sensor") ?? Placeholder(SensorColor);
            model.Position = at;
            _markers.AddChild(model);
            // Chebyshev radius: a square of tiles.
            var half = (sensor.Radius + 0.5f) * TileGrid.TileSize;
            _markers.AddChild(Square(at, half, SensorColor with { A = 0.5f }));
        }
    }

    /// <summary>
    /// Shows where an ambush on one of the local side's units came from: a red streak from the attacker (if the side
    /// sees it) or the attack contact pointing at the victim, fading out.
    /// </summary>
    public void Incoming(ScavengeEvent e)
    {
        if (e.Type != ScavengeEventType.Ambushed || e.TargetId is not { } victimId) return;
        var state = _presenter.State;
        if (state.FindUnit(victimId) is not { } victim || victim.Side != _presenter.Side) return;
        Pos? source = e.UnitId is { } a && state.FindUnit(a) is { } attacker && _presenter.CanSee(attacker) ? attacker.Pos
            : _presenter.Contacts.Where(c => c.Kind == ContactKind.Attack && c.Toward == victim.Pos).OrderBy(c => c.Round).LastOrDefault()?.Pos;
        if (source is not { } from || from == victim.Pos) return;

        var start = TileGrid.ToWorld(from) + Vector3.Up * 1.2f;
        var end = TileGrid.ToWorld(victim.Pos) + Vector3.Up * 1.2f;
        var length = start.DistanceTo(end);
        var streak = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, length) },
            MaterialOverride = Material(RevealedColor with { A = 0.9f }),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(streak);
        streak.LookAtFromPosition((start + end) / 2, end, Vector3.Up);
        var head = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.3f, Height = 0.6f },
            MaterialOverride = Material(RevealedColor),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(head);
        head.LookAtFromPosition(end - (end - start).Normalized() * 0.5f, end, Vector3.Up);
        head.RotateObjectLocal(Vector3.Right, -Mathf.Pi / 2);
        Popped?.Invoke(TileGrid.ToWorld(victim.Pos), "AMBUSH!", RevealedColor);

        var tween = CreateTween();
        tween.TweenInterval(1.6);
        tween.TweenCallback(Callable.From(() =>
        {
            streak.QueueFree();
            head.QueueFree();
        }));
    }

    /// <summary>A manifest marker tinted (they ship neutral white), else a flat disc.</summary>
    private Node3D Marker(string id, Color color, float scale)
    {
        var model = _models.Prop(id);
        if (model is null) return Disc(Vector3.Zero, 0.8f, color);
        Tint(model, Material(color));
        model.Scale = Vector3.One * scale;
        return model;
    }

    private static void Tint(Node node, Material material)
    {
        if (node is MeshInstance3D mesh)
        {
            mesh.MaterialOverride = material;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        foreach (var child in node.GetChildren()) Tint(child, material);
    }

    private static StandardMaterial3D Material(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>The area a unit on overwatch covers: a soft amber edge with a fainter halo just inside it.</summary>
    private static Node3D WatchRange(Vector3 at, float radius)
    {
        var root = new Node3D();
        root.AddChild(Ring(at, radius, OverwatchColor with { A = 0.3f }, 0.04f));
        root.AddChild(Ring(at, radius - 0.25f, OverwatchColor with { A = 0.08f }, 0.18f));
        return root;
    }

    private static MeshInstance3D Ring(Vector3 at, float radius, Color color, float width = 0.06f) => new()
    {
        Mesh = new TorusMesh { InnerRadius = radius - width, OuterRadius = radius + width, Rings = 64, RingSegments = 4 },
        MaterialOverride = Material(color),
        Scale = new Vector3(1, 0.1f, 1),
        Position = at + Vector3.Up * 0.08f,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private static MeshInstance3D Disc(Vector3 at, float radius, Color color) => new()
    {
        Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.02f, RadialSegments = 48 },
        MaterialOverride = Material(color),
        Position = at + Vector3.Up * 0.07f,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private static Node3D Square(Vector3 at, float half, Color color)
    {
        var root = new Node3D { Position = at + Vector3.Up * 0.08f };
        var material = Material(color);
        foreach (var (offset, size) in new[]
                 {
                     (new Vector3(0, 0, -half), new Vector3(half * 2, 0.02f, 0.08f)),
                     (new Vector3(0, 0, half), new Vector3(half * 2, 0.02f, 0.08f)),
                     (new Vector3(-half, 0, 0), new Vector3(0.08f, 0.02f, half * 2)),
                     (new Vector3(half, 0, 0), new Vector3(0.08f, 0.02f, half * 2)),
                 })
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = offset,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        return root;
    }

    private static Node3D Placeholder(Color color)
    {
        var root = new Node3D();
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = 0.4f },
            MaterialOverride = Material(color),
            Position = new Vector3(0, 0.2f, 0),
        });
        return root;
    }
}
