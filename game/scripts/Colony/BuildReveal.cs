using System.Collections.Generic;
using Godot;

namespace Remnants.Game.Colony;

/// <summary>
/// The rising reveal of a building under construction: every surface of the model gets a copy of its material as a
/// small spatial shader that cuts the mesh off above a world height, with a band of fresh-timber colour at the cut. A
/// site at 0% shows nothing of the building, at 100% all of it. Built in code over the existing models (no new art).
/// </summary>
public static class BuildReveal
{
    private const string Code = """
        shader_type spatial;
        render_mode cull_disabled;
        uniform vec4 albedo : source_color = vec4(1.0);
        uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
        uniform bool use_tex = false;
        uniform bool use_vertex_color = false;
        uniform float roughness = 0.85;
        uniform float metallic = 0.0;
        uniform float cut_y = 1000.0;
        uniform float band = 0.35;
        uniform vec4 edge_color : source_color = vec4(0.78, 0.6, 0.38, 1.0);
        varying vec3 world_pos;
        void vertex() { world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
        void fragment() {
            if (world_pos.y > cut_y) discard;
            vec4 c = albedo;
            if (use_tex) c *= texture(albedo_tex, UV);
            if (use_vertex_color) c *= COLOR;
            float e = smoothstep(cut_y - band, cut_y, world_pos.y);
            ALBEDO = mix(c.rgb, edge_color.rgb, e * 0.65);
            EMISSION = edge_color.rgb * e * 0.15;
            ROUGHNESS = roughness;
            METALLIC = metallic;
        }
        """;

    private static Shader? _shader;
    private static Shader Shader => _shader ??= new Shader { Code = Code };

    /// <summary>
    /// Swaps every surface of <paramref name="model"/> for a reveal copy of its material; returns the copies so
    /// <see cref="Set"/> can move the cut.
    /// </summary>
    public static List<ShaderMaterial> Apply(Node model)
    {
        var list = new List<ShaderMaterial>();
        Visit(model, list);
        return list;
    }

    private static void Visit(Node node, List<ShaderMaterial> list)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                var m = new ShaderMaterial { Shader = Shader };
                if (mi.GetActiveMaterial(i) is BaseMaterial3D std)
                {
                    m.SetShaderParameter("albedo", std.AlbedoColor);
                    if (std.AlbedoTexture is { } tex)
                    {
                        m.SetShaderParameter("albedo_tex", tex);
                        m.SetShaderParameter("use_tex", true);
                    }
                    m.SetShaderParameter("use_vertex_color", std.VertexColorUseAsAlbedo);
                    m.SetShaderParameter("roughness", std.Roughness);
                    m.SetShaderParameter("metallic", std.Metallic);
                }
                mi.SetSurfaceOverrideMaterial(i, m);
                list.Add(m);
            }
        foreach (var child in node.GetChildren()) Visit(child, list);
    }

    /// <summary>Cut the model at <paramref name="worldY"/> (world height); everything below is built.</summary>
    public static void Set(List<ShaderMaterial> materials, float worldY)
    {
        foreach (var m in materials) m.SetShaderParameter("cut_y", worldY);
    }
}
