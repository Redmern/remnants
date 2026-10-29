using Godot;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Orthographic isometric camera (yaw 45°, pitch 35.264°, the asset convention) around a ground focus point.
/// Pan: WASD / arrows / middle-drag. Zoom: wheel. Rotate in 90° steps: Q / E.
/// </summary>
public partial class TacticalCamera : Node3D
{
    private const float Pitch = 35.264f;
    private const float MinSize = 10f, MaxSize = 70f;
    private const float PanSpeed = 0.9f; // fraction of the view size per second
    private const float Distance = 80f;

    private float _yaw = 45f;
    private float _targetYaw = 45f;
    private Vector2 _min, _max;

    public Camera3D Camera { get; private set; } = null!;

    public override void _Ready()
    {
        Camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 26, Near = 0.1f, Far = 300 };
        AddChild(Camera);
        Apply();
    }

    /// <summary>World-space rectangle the focus point may move within.</summary>
    public void SetBounds(Vector2 min, Vector2 max) => (_min, _max) = (min, max);

    public void Focus(Vector3 point) => Position = Clamp(new Vector3(point.X, 0, point.Z));

    /// <summary>Glide toward a point, e.g. the unit whose turn it is.</summary>
    public void GlideTo(Vector3 point) => CreateTween().TweenProperty(this, "position", Clamp(new Vector3(point.X, 0, point.Z)), 0.35);

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (move != Vector2.Zero) Pan(move.Normalized() * PanSpeed * Camera.Size * dt);
        _yaw = Mathf.Lerp(_yaw, _targetYaw, Mathf.Min(1, dt * 10));
        Apply();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Camera.Size = Mathf.Max(MinSize, Camera.Size * 0.9f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Camera.Size = Mathf.Min(MaxSize, Camera.Size * 1.1f);
                break;
            case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Middle) != 0:
                Pan(-m.Relative * Camera.Size / GetViewport().GetVisibleRect().Size.Y);
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Q }:
                _targetYaw -= 90;
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.E }:
                _targetYaw += 90;
                break;
            default:
                return;
        }
        Apply();
    }

    /// <summary>Screen-space pan: x is right, y is down (towards the camera).</summary>
    private void Pan(Vector2 screen)
    {
        var yaw = Mathf.DegToRad(_yaw);
        var right = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
        // Ortho at this pitch: a screen pixel down covers 1/sin(pitch) metres of ground.
        var back = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) / Mathf.Sin(Mathf.DegToRad(Pitch));
        Position = Clamp(Position + right * screen.X + back * screen.Y);
    }

    private Vector3 Clamp(Vector3 p) => new(Mathf.Clamp(p.X, _min.X, _max.X), 0, Mathf.Clamp(p.Z, _min.Y, _max.Y));

    private void Apply()
    {
        if (Camera is null) return;
        var yaw = Mathf.DegToRad(_yaw);
        var pitch = Mathf.DegToRad(Pitch);
        var offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * Distance;
        Camera.Transform = new Transform3D(Basis.LookingAt(-offset, Vector3.Up), offset);
    }

    /// <summary>The ground point (y = 0) under a screen position, if the ray hits the ground.</summary>
    public Vector3? GroundUnder(Vector2 screen)
    {
        var origin = Camera.ProjectRayOrigin(screen);
        var dir = Camera.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 1e-5f) return null;
        var t = -origin.Y / dir.Y;
        return t < 0 ? null : origin + dir * t;
    }
}
