using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Colony.Ui;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony;

/// <summary>
/// The power grid drawn over the town (toggled with P or the top bar's grid button): a post and a tag per generator,
/// battery bank and consumer (name and what it gives or draws), and cables laid on the ground — the sources chained to
/// each other, each consumer to its nearest source — coloured by the consumer's state: fed moss, not fed red, switched
/// off dim. Purely visual and rebuilt when the grid's state changes; clicking a building still selects it, and its panel
/// carries the on/off switch.
/// </summary>
public partial class PowerOverlay : Node3D
{
    private ColonySession _session = null!;
    private TownLayout _layout = null!;
    private string _shown = "";

    public void Setup(ColonySession session, TownLayout layout)
    {
        _session = session;
        _layout = layout;
        Visible = false;
    }

    public void Toggle(bool? on = null)
    {
        Visible = on ?? !Visible;
        _shown = "";
        Sync();
    }

    public void Sync()
    {
        if (!Visible) return;
        var sim = _session.Sim;
        var nodes = sim.PowerNodes();
        var signature = string.Join(";", nodes.Select(n => $"{n.BuildingId}{n.Role}{n.On}{n.Fed}{n.Amount:0.#}")) + $"|{sim.State.Power.Stored:0}";
        if (signature == _shown) return;
        _shown = signature;
        foreach (var child in GetChildren()) child.QueueFree();
        if (nodes.Count == 0) return;

        Vector3 At(int buildingId) => sim.State.FindBuilding(buildingId) is { } b ? _layout.Center(b.Plot) : Vector3.Zero;
        // One marker per building, the strongest role first (a consumer with a Floodlight shows as a consumer).
        var byBuilding = nodes.GroupBy(n => n.BuildingId).Select(g => g.OrderBy(n => n.Role).First()).ToList();
        var sources = byBuilding.Where(n => n.Role is PowerRole.Generator or PowerRole.Storage).ToList();
        var consumers = byBuilding.Where(n => n.Role is PowerRole.Consumer or PowerRole.Floodlight).ToList();

        // The sources form a bus: each joins the nearest one already on it.
        var joined = new List<PowerNode>();
        foreach (var s in sources.OrderBy(s => At(s.BuildingId).Length()))
        {
            if (joined.Count > 0)
            {
                var to = joined.MinBy(j => At(j.BuildingId).DistanceTo(At(s.BuildingId)))!;
                Cable(At(s.BuildingId), At(to.BuildingId), s.On && to.On ? Palette.Warn : Palette.TextDim);
            }
            joined.Add(s);
        }
        foreach (var c in consumers)
            if (sources.Count > 0)
            {
                var from = sources.MinBy(s => At(s.BuildingId).DistanceTo(At(c.BuildingId)))!;
                Cable(At(from.BuildingId), At(c.BuildingId), PowerText.Color(sim, c));
            }

        foreach (var n in byBuilding)
        {
            var extra = nodes.Where(x => x.BuildingId == n.BuildingId && x != n).ToList();
            Marker(sim, At(n.BuildingId), n, extra);
        }
    }

    /// <summary>A cable on the ground between two plots: a dark sheath with a bright core, slightly raised so it reads over the lots.</summary>
    private void Cable(Vector3 a, Vector3 b, Color color)
    {
        a += new Vector3(0, 0.18f, 0);
        b += new Vector3(0, 0.18f, 0);
        var length = a.DistanceTo(b);
        if (length < 0.1f) return;
        var mid = (a + b) / 2;
        var basis = Basis.LookingAt((b - a).Normalized(), Vector3.Up);
        foreach (var (width, height, tint, lift) in new[] { (0.42f, 0.06f, Palette.EdgeDark, 0f), (0.2f, 0.08f, color, 0.02f) })
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(width, height, length) },
                MaterialOverride = Glow(tint, tint == Palette.EdgeDark ? 0 : 0.6f),
                Transform = new Transform3D(basis, mid + new Vector3(0, lift, 0)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
    }

    /// <summary>A post with a glowing head over the building and its tag: name, the amount, the state.</summary>
    private void Marker(ColonySim sim, Vector3 at, PowerNode n, List<PowerNode> extra)
    {
        var color = PowerText.Color(sim, n);
        var post = new Vector3(TownLayout.PlotSize * 0.3f, 0, -TownLayout.PlotSize * 0.3f);
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 3f, RadialSegments = 6 },
            MaterialOverride = Placeholders.Material(new Color(0.25f, 0.24f, 0.22f)), Position = at + post + new Vector3(0, 1.5f, 0),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.32f, Height = 0.64f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = Glow(color, 1.6f), Position = at + post + new Vector3(0, 3.1f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        var name = sim.Data.Buildings[n.DefId].Name;
        var state = n.Role switch
        {
            PowerRole.Generator => n.On ? (n.Fed ? n.Note : $"idle · {n.Note}") : "OFF",
            PowerRole.Storage => n.On ? "stored" : "OFF",
            _ => PowerText.State(sim, n).ToUpperInvariant(),
        };
        var text = $"{name}\n{PowerText.Amount(sim, n)}  {state}";
        foreach (var x in extra.Where(x => x.Role == PowerRole.Floodlight))
            text += $"\nfloodlight −{x.Amount:0.#} {(x.Fed ? "lit" : "dark")}";
        AddChild(new Label3D
        {
            Text = text,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = at + post + new Vector3(0, 4.1f, 0),
            FontSize = 64,
            PixelSize = 0.016f,
            OutlineSize = 18,
            OutlineModulate = new Color(Palette.Void, 0.9f),
            Modulate = color.Lightened(0.15f),
            NoDepthTest = true,
            Font = Fonts.Numbers,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
    }

    private static StandardMaterial3D Glow(Color color, float energy) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        EmissionEnabled = energy > 0,
        Emission = color,
        EmissionEnergyMultiplier = energy,
    };
}
