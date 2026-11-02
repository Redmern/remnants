using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Colony;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Enemy telegraphs on the map (<see cref="TacticsView.Telegraphs"/>): each seen enemy's planned path as a dotted trail of
/// steps from where it stands, and a pulsing strike marker on every tile its blow will land on (red when it is aimed at
/// one of ours). A plan that resolves flashes its marker; a blow that lands on an empty tile (dodged) flashes it pale.
/// Reads Core's view only; nothing here is a rule.
/// </summary>
public partial class TelegraphView : Node3D
{
    private static readonly Color PathColor = new(1f, 0.72f, 0.3f, 0.9f);
    private static readonly Color StrikeColor = new(1f, 0.3f, 0.18f, 0.95f);
    private static readonly Color AimedColor = new(1f, 0.08f, 0.08f, 1f);
    private static readonly Color DodgedColor = new(0.75f, 0.95f, 1f, 1f);

    private ScavengePresenter _presenter = null!;
    private StandardMaterial3D _path = null!, _strike = null!, _aimed = null!;
    private readonly SphereMesh _dot = new() { Radius = 0.12f, Height = 0.24f, RadialSegments = 8, Rings = 4 };
    private readonly TorusMesh _ring = new() { InnerRadius = 0.62f, OuterRadius = 0.8f, Rings = 24, RingSegments = 6 };
    private readonly Dictionary<Pos, Node3D> _markers = new();
    private string _signature = "";

    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        _path = Placeholders.Material(PathColor, unshaded: true);
        _strike = Placeholders.Material(StrikeColor, unshaded: true);
        _aimed = Placeholders.Material(AimedColor, unshaded: true);
        foreach (var m in new[] { _path, _strike, _aimed })
        {
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            m.NoDepthTest = false;
        }
    }

    /// <summary>Rebuilds the telegraphs when what the side knows of the enemies' plans changed.</summary>
    public void Sync()
    {
        var telegraphs = _presenter.Telegraphs();
        var signature = string.Join(";", telegraphs.Select(t => $"{t.UnitId}@{t.From}:{string.Join(",", t.Path)}>{t.AttackTile}{(t.AimedAtUs ? "!" : "")}"));
        if (signature == _signature) return;
        _signature = signature;
        foreach (var child in GetChildren()) child.QueueFree();
        _markers.Clear();
        foreach (var t in telegraphs)
        {
            // The trail: a dot every quarter tile along each leg, from where the enemy stands through its waypoints.
            var from = t.From;
            foreach (var step in t.Path)
            {
                var a = TileGrid.ToWorld(from);
                var b = TileGrid.ToWorld(step);
                var dots = Mathf.Max(2, (int)(a.DistanceTo(b) / (TileGrid.TileSize / 4)));
                for (var i = 1; i <= dots; i++)
                    AddChild(new MeshInstance3D
                    {
                        Mesh = _dot, MaterialOverride = _path, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                        Position = a.Lerp(b, i / (float)dots) + Vector3.Up * 0.2f,
                    });
                from = step;
            }
            foreach (var tile in t.Threatened)
            {
                var ring = new MeshInstance3D
                {
                    Mesh = _ring, MaterialOverride = t.AimedAtUs && tile == t.AttackTile ? _aimed : _strike,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = TileGrid.ToWorld(tile) + Vector3.Up * 0.16f,
                };
                AddChild(ring);
                _markers[tile] = ring;
                Pulse(ring);
            }
        }
    }

    /// <summary>A telegraph resolving: a strike marker flashes; a blow on an empty tile (dodged) flashes pale.</summary>
    public void Incoming(ScavengeEvent e)
    {
        if (e.Type is not (ScavengeEventType.IntentResolved or ScavengeEventType.IntentFizzled or ScavengeEventType.IntentDeclared)) return;
        if (e.UnitId is not { } id || _presenter.State.FindUnit(id) is not { } unit || !_presenter.CanSee(unit)) return;
        var at = e.Type == ScavengeEventType.IntentDeclared ? unit.Plan?.AttackTile : (Pos?)null;
        foreach (var (tile, marker) in _markers.Where(kv => at is null || kv.Key == at))
            Flash(marker, e.Type == ScavengeEventType.IntentFizzled ? DodgedColor : AimedColor);
    }

    /// <summary>A slow breathing pulse on a strike marker (none with reduced motion).</summary>
    private static void Pulse(Node3D marker)
    {
        if (Hud.GameSettings.ReduceMotion) return;
        var tween = marker.CreateTween().SetLoops();
        tween.TweenProperty(marker, "scale", new Vector3(1.12f, 1, 1.12f), 0.55f).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(marker, "scale", Vector3.One, 0.55f).SetTrans(Tween.TransitionType.Sine);
    }

    private static void Flash(Node3D marker, Color color)
    {
        if (marker is not MeshInstance3D mesh || Hud.GameSettings.ReduceMotion) return;
        var flash = Placeholders.Material(color, unshaded: true);
        var original = mesh.MaterialOverride;
        mesh.MaterialOverride = flash;
        var tween = mesh.CreateTween();
        tween.TweenProperty(mesh, "scale", new Vector3(1.5f, 1, 1.5f), 0.12f);
        tween.TweenProperty(mesh, "scale", Vector3.One, 0.25f);
        tween.TweenCallback(Callable.From(() => mesh.MaterialOverride = original));
    }
}
