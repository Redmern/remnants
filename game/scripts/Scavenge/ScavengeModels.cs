using System.Collections.Generic;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Colony;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Scavenge model lookup on top of the shared <see cref="ModelLibrary"/>: map props, enemies by definition
/// (<c>enemy_{defId}</c>) and survivor bodies with their limbs swapped for cybernetics or hidden when missing.
/// Every lookup falls back to a primitive so the mode runs with no generated assets.
/// </summary>
public sealed class ScavengeModels(ModelLibrary library)
{
    private static readonly Dictionary<BodyPart, string> LimbNodes = new()
    {
        [BodyPart.LeftArm] = "arm_l", [BodyPart.RightArm] = "arm_r",
        [BodyPart.LeftLeg] = "leg_l", [BodyPart.RightLeg] = "leg_r",
    };

    public int ModelCount => library.ModelCount;

    public Node3D Ground() => library.TryInstance("ground_tile") ?? Box(new Vector3(1.96f, 0.1f, 1.96f), new Color(0.32f, 0.31f, 0.29f));

    public Node3D Wall() => library.TryInstance("wall_segment") ?? Box(new Vector3(2f, 2.2f, 0.3f), new Color(0.5f, 0.48f, 0.44f));

    public Node3D Rubble() => library.TryInstance("rubble") ?? Box(new Vector3(1.6f, 0.8f, 1.4f), new Color(0.45f, 0.42f, 0.38f));

    public Node3D Car() => library.TryInstance("wrecked_car") ?? Box(new Vector3(3.9f, 1.3f, 2f), new Color(0.55f, 0.3f, 0.2f));

    public Node3D Crate() => library.TryInstance("loot_crate") ?? Box(new Vector3(1.1f, 0.65f, 0.65f), new Color(0.6f, 0.5f, 0.25f));

    public Node3D Unit(Unit unit)
    {
        if (unit.Kind == UnitKind.Enemy)
        {
            var linked = unit.EnemyDefId == "linked";
            return library.TryInstance($"enemy_{unit.EnemyDefId}")
                   ?? Capsule(linked ? 2.1f : 1.7f, linked ? new Color(0.3f, 0.75f, 0.85f) : new Color(0.55f, 0.45f, 0.3f));
        }

        var child = unit.Origin?.IsChild ?? false;
        var id = child ? "survivor_child" : unit.Origin?.Sex == Sex.Female ? "survivor_adult_female" : "survivor_adult_male";
        if (library.TryInstance(id) is not { } model) return Placeholders.Survivor(child);
        FitLimbs(model, unit.Body, child);
        return model;
    }

    /// <summary>
    /// Hides missing limbs and swaps cybernetic ones in at the same joint. The prosthetic models are built for
    /// the adult male; they are moved to this body's joint and scaled down for a child.
    /// </summary>
    private void FitLimbs(Node3D model, Body body, bool child)
    {
        foreach (var (part, nodeName) in LimbNodes)
        {
            var condition = body[part].Condition;
            if (condition is PartCondition.Healthy or PartCondition.Injured) continue;
            if (model.FindChild(nodeName, true, false) is not Node3D limb) continue;
            limb.Visible = false;
            if (condition != PartCondition.Prosthetic) continue;

            var kind = Body.RegionOf(part) == BodyRegion.Arms ? "arm" : "leg";
            var side = nodeName[^1];
            if (library.TryInstance($"prosthetic_{kind}_cyborg_{side}") is not { } prosthetic) continue;
            var joint = prosthetic.FindChild(nodeName, true, false) as Node3D;
            var scale = child && joint is not null && joint.Position.Y > 0 ? limb.Position.Y / joint.Position.Y : 1f;
            prosthetic.Scale = Vector3.One * scale;
            prosthetic.Position = limb.Position - (joint?.Position ?? Vector3.Zero) * scale;
            limb.GetParent().AddChild(prosthetic);
        }
    }

    private static Node3D Box(Vector3 size, Color color)
    {
        var root = new Node3D { Name = "Placeholder" };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = Placeholders.Material(color),
            Position = new Vector3(0, size.Y / 2, 0),
        });
        return root;
    }

    private static Node3D Capsule(float height, Color color)
    {
        var root = new Node3D { Name = "Placeholder" };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.32f, Height = height },
            MaterialOverride = Placeholders.Material(color),
            Position = new Vector3(0, height / 2, 0),
        });
        return root;
    }
}
