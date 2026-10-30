using System.Linq;
using Godot;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Hackable devices on the map, as the local side knows them (purely visual, rebuilt on <see cref="Sync"/>):
/// <list type="bullet">
/// <item>terminals, blast doors, lockers and relay nodes as <c>device_{kind}</c> with their <c>_hacked</c> / <c>_open</c> /
///   <c>_hacked_open</c> variants (a door shows open while someone stands in it); cameras and turrets are units
///   (<see cref="UnitViews"/>), shown here only once destroyed (<c>_disabled</c>);</item>
/// <item>the local side's hack in progress: <c>hack_marker</c> tinted amber with a progress arc around the device;</item>
/// <item>drone marks: <c>drone_mark_marker</c> tinted red over hostiles the side's drone marked.</item>
/// </list>
/// Flat white markers are tinted here; cyan only where something works for the Network.
/// </summary>
public partial class DeviceViews : Node3D
{
    private ScavengePresenter _presenter = null!;
    private ScavengeModels _models = null!;
    private Node3D _root = null!;

    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        _models = models;
        _root = new Node3D { Name = "Devices" };
        AddChild(_root);
    }

    public void Sync()
    {
        foreach (var child in _root.GetChildren()) child.QueueFree();
        var state = _presenter.State;
        var map = state.Map;
        foreach (var device in _presenter.KnownDevices)
        {
            var at = TileGrid.ToWorld(device.Pos);
            var hacked = _presenter.ControllerOf(device) != Unit.EnemySide;
            string? id = device.Kind switch
            {
                // Shown here when its unit is not: destroyed, or remembered out of sight.
                DeviceKind.Camera or DeviceKind.Turret => device.Disabled || device.UnitId is not { } uid || state.FindUnit(uid) is not { } u || !_presenter.CanSee(u)
                    ? $"device_{ModelName(device.Kind)}{(device.Disabled ? "_disabled" : hacked ? "_hacked" : "")}"
                    : null,
                DeviceKind.Door => $"device_door{(hacked ? "_hacked" : "")}{(state.UnitAt(device.Pos) is not null ? "_open" : "")}",
                _ => $"device_{ModelName(device.Kind)}{(hacked || device.Used ? "_hacked" : "")}",
            };
            if (id is not null)
            {
                var model = _models.Prop(id) ?? Placeholder(device.Kind);
                model.Position = at;
                // A door spans its doorway: turned when the walls are north and south of it.
                var wallsNorthSouth = map.InBounds(device.Pos + new Pos(0, -1)) && map[device.Pos + new Pos(0, -1)] == Terrain.Wall;
                model.RotationDegrees = new Vector3(0, device.Kind == DeviceKind.Door && wallsNorthSouth ? 90 : 0, 0);
                _root.AddChild(model);
            }
            if (Hacking.ProgressOf(device, _presenter.Side) is { } progress)
            {
                var marker = Tinted("hack_marker", Hud.Palette.Amber, 0.9f);
                marker.Position = at + Vector3.Up * 0.16f;
                _root.AddChild(marker);
                var arc = Arc((float)progress.Done / progress.Needed, Hud.Palette.Amber);
                arc.Position = at + Vector3.Up * 0.18f;
                _root.AddChild(arc);
            }
        }

        foreach (var mark in state.Marks.Where(m => m.Side == _presenter.Side))
        {
            if (state.FindUnit(mark.UnitId) is not { OnMap: true } target || !_presenter.CanSee(target)) continue;
            var marker = Tinted("drone_mark_marker", Hud.Palette.Danger, 0.9f);
            marker.Position = TileGrid.ToWorld(target.Pos) + Vector3.Up * 0.17f;
            _root.AddChild(marker);
        }
    }

    private static string ModelName(DeviceKind kind) => kind == DeviceKind.Relay ? "relay_node" : kind.ToString().ToLowerInvariant();

    private Node3D Tinted(string id, Color color, float scale)
    {
        var model = _models.Prop(id) ?? Arc(1, color);
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Append(model as MeshInstance3D).OfType<MeshInstance3D>())
        {
            mesh.MaterialOverride = material;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        model.Scale = Vector3.One * scale;
        return model;
    }

    /// <summary>A flat ring arc around a tile, filled to <paramref name="fraction"/> clockwise from the top.</summary>
    private static Node3D Arc(float fraction, Color color)
    {
        var mesh = new ImmediateMesh();
        var steps = Mathf.Max(2, (int)(48 * Mathf.Clamp(fraction, 0, 1)));
        mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        for (var i = 0; i <= steps; i++)
        {
            var a = -Mathf.Pi / 2 + Mathf.Tau * fraction * i / steps;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            mesh.SurfaceAddVertex(dir * 0.95f);
            mesh.SurfaceAddVertex(dir * 1.12f);
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D
        {
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
    }

    private static Node3D Placeholder(DeviceKind kind)
    {
        var size = kind == DeviceKind.Door ? new Vector3(2f, 2.2f, 0.4f) : new Vector3(0.9f, 1.3f, 0.6f);
        var root = new Node3D { Name = "Placeholder" };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = Colony.Placeholders.Material(new Color(0.3f, 0.6f, 0.7f)),
            Position = new Vector3(0, size.Y / 2, 0),
        });
        return root;
    }
}
