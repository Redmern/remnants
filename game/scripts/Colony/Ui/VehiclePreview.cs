using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The Garage's look at one vehicle: its model (<c>vehicle_{model}</c>, the <c>_l2</c>/<c>_l3</c> variant as it gets more
/// upgrades) with its working upgrades hung on the slot nodes (<see cref="VehicleDressing"/>), slowly turning on a dark
/// floor in its own little world. Purely visual.
/// </summary>
public partial class VehiclePreview : SubViewportContainer
{
    private static ModelLibrary? _library;
    private readonly SubViewport _viewport = new() { OwnWorld3D = true, TransparentBg = false, Msaa3D = Viewport.Msaa.Msaa4X };
    private readonly Node3D _turntable = new();
    private readonly Camera3D _camera = new() { Fov = 32 };
    private string _shown = "";

    public VehiclePreview(Vector2 size)
    {
        Stretch = true;
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
        _viewport.Size = (Vector2I)size;
        AddChild(_viewport);
        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Palette.Inset,
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.5f, 0.45f), AmbientLightEnergy = 0.6f,
            },
        };
        _viewport.AddChild(env);
        var sun = new DirectionalLight3D { LightEnergy = 1.4f, ShadowEnabled = true, RotationDegrees = new Vector3(-50, -35, 0) };
        _viewport.AddChild(sun);
        _viewport.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 6, BottomRadius = 6, Height = 0.05f },
            MaterialOverride = Placeholders.Material(Palette.Raised), Position = new Vector3(0, -0.03f, 0) });
        _viewport.AddChild(_turntable);
        _viewport.AddChild(_camera);
    }

    public void Show(GameData data, Vehicle vehicle)
    {
        var tier = data.VehicleTiers[vehicle.TierId];
        var working = vehicle.Upgrades.Where(u => !VehicleRules.IsBroken(data, u)).Select(u => data.VehicleUpgrades[u.UpgradeId]).ToList();
        var signature = $"{vehicle.TierId}|{string.Join(",", working.Select(u => u.Id))}";
        if (signature == _shown) return;
        _shown = signature;
        foreach (var child in _turntable.GetChildren()) child.QueueFree();
        _library ??= ModelLibrary.Load();
        var id = $"vehicle_{tier.Model}";
        var model = _library.TryInstance(id) ?? new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.6f, 1.2f, 3f) }, MaterialOverride = Placeholders.Material(Palette.Rust), Position = new Vector3(0, 0.6f, 0),
        };
        VehicleDressing.Dress(_library, model, id, tier.Tier, working);
        _turntable.AddChild(model);
        var size = _library.Dimensions(id) ?? new Vector3(1.5f, 1.2f, 3f);
        var reach = Mathf.Max(size.Z, Mathf.Max(size.X, size.Y)) * 1.35f + 1.2f;
        _camera.Position = new Vector3(reach * 0.75f, reach * 0.55f, reach * 0.75f);
        _camera.LookAt(new Vector3(0, size.Y * 0.4f, 0), Vector3.Up);
    }

    public override void _Process(double delta) => _turntable.RotateY((float)delta * 0.35f);
}
