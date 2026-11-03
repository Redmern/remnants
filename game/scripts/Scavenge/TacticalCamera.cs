using Godot;
using Remnants.Game.Hud;

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

    private Tween? _glide;

    /// <summary>Glide toward a point, e.g. the unit whose turn it is.</summary>
    public void GlideTo(Vector3 point)
    {
        _glide?.Kill();
        _glide = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _glide.TweenProperty(this, "position", Clamp(new Vector3(point.X, 0, point.Z)), 0.4);
    }

    /// <summary>Glide to a point only when it is away from the middle of the view, so following a unit does not jitter.</summary>
    public void Follow(Vector3 point)
    {
        if (new Vector2(point.X - Position.X, point.Z - Position.Z).Length() > Camera.Size * 0.3f) GlideTo(point);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (move != Vector2.Zero) Pan(move.Normalized() * PanSpeed * GameSettings.CameraPanSpeed * Camera.Size * dt);
        _yaw = Mathf.Lerp(_yaw, _targetYaw, Mathf.Min(1, dt * 10));
        Apply();
        Shake(dt);
    }

    private float _trauma;
    private float _shakeTime;

    /// <summary>
    /// Adds screen-shake trauma (0..1, <see cref="Remnants.Core.Scavenge.ReplayJuice.Feel"/>). The view jolts by the square
    /// of it, a share of the view size so it reads the same at any zoom, and settles within about half a second. It moves
    /// the lens (<see cref="Camera3D.HOffset"/>), never the focus point. Purely visual.
    /// </summary>
    public void AddTrauma(float amount) => _trauma = Mathf.Clamp(Mathf.Max(_trauma, amount) + amount * 0.25f, 0, 1);

    private void Shake(float dt)
    {
        if (_trauma <= 0)
        {
            Camera.HOffset = Camera.VOffset = 0;
            return;
        }
        // Real time, so a hit-stop (a slowed engine) does not freeze the jolt.
        var real = dt / (float)Mathf.Max(Engine.TimeScale, 0.01);
        _shakeTime += real;
        _trauma = Mathf.Max(0, _trauma - real * 2.2f);
        var amplitude = _trauma * _trauma * Camera.Size * 0.018f;
        // Two unrelated sines per axis: a jolt that does not look like a wobble.
        Camera.HOffset = amplitude * (Mathf.Sin(_shakeTime * 61f) * 0.6f + Mathf.Sin(_shakeTime * 37f + 1.3f) * 0.4f);
        Camera.VOffset = amplitude * (Mathf.Sin(_shakeTime * 53f + 0.7f) * 0.6f + Mathf.Sin(_shakeTime * 29f + 2.1f) * 0.4f);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Camera.Size = Mathf.Max(MinSize, Camera.Size * Mathf.Pow(0.9f, GameSettings.CameraZoomSpeed));
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Camera.Size = Mathf.Min(MaxSize, Camera.Size * Mathf.Pow(1.1f, GameSettings.CameraZoomSpeed));
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
