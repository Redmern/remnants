using Godot;

namespace Remnants.Game.Colony;

/// <summary>
/// Isometric-style orbit camera around a ground focus point.
/// Pan: WASD / arrows / middle-drag. Zoom: wheel. Rotate: Q/E / right-drag.
/// </summary>
public partial class OrbitCamera : Node3D
{
    private const float MinDistance = 12f, MaxDistance = 90f;
    private const float PanSpeed = 0.8f;     // fraction of distance per second
    private const float RotateSpeed = 1.6f;  // radians per second
    private const float DragRotate = 0.006f; // radians per pixel

    private float _yaw = Mathf.DegToRad(45);
    private float _pitch = Mathf.DegToRad(50);
    private float _distance = 60f;
    private Vector2 _bounds = new(30, 30);

    public Camera3D Camera { get; private set; } = null!;

    public override void _Ready()
    {
        Camera = new Camera3D { Fov = 40, Far = 400 };
        AddChild(Camera);
        Apply();
    }

    /// <summary>Half-extent the focus point may move within (the town plus a margin).</summary>
    public void SetBounds(Vector2 halfExtent) => _bounds = halfExtent;

    public void Focus(Vector3 point) => Position = new Vector3(point.X, 0, point.Z);

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (move != Vector2.Zero) Pan(move.Normalized() * PanSpeed * _distance * dt);

        if (Input.IsKeyPressed(Key.Q)) _yaw -= RotateSpeed * dt;
        if (Input.IsKeyPressed(Key.E)) _yaw += RotateSpeed * dt;
        Apply();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _distance = Mathf.Max(MinDistance, _distance * 0.9f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _distance = Mathf.Min(MaxDistance, _distance * 1.1f);
                break;
            case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Middle) != 0:
                Pan(-m.Relative * _distance * 0.0016f);
                break;
            case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Right) != 0:
                _yaw -= m.Relative.X * DragRotate;
                _pitch = Mathf.Clamp(_pitch + m.Relative.Y * DragRotate, Mathf.DegToRad(25), Mathf.DegToRad(85));
                break;
            default:
                return;
        }
        Apply();
    }

    /// <summary>Screen-space pan: x is right, y is down (towards the camera).</summary>
    private void Pan(Vector2 screen)
    {
        var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
        var back = new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw));
        var p = Position + right * screen.X + back * screen.Y;
        Position = new Vector3(Mathf.Clamp(p.X, -_bounds.X, _bounds.X), 0, Mathf.Clamp(p.Z, -_bounds.Y, _bounds.Y));
    }

    private void Apply()
    {
        if (Camera is null) return;
        var offset = new Vector3(
            Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            Mathf.Sin(_pitch),
            Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance;
        Camera.Transform = new Transform3D(Basis.LookingAt(-offset, Vector3.Up), offset);
    }
}
