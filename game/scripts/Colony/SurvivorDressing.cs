using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony;

/// <summary>
/// Purely visual body dressing shared by the colony and scavenge views: missing limbs hidden, prosthetics swapped in
/// at the joint (crude peg/hook below quality 1, cybernetic from there), and equipped gear hung on the body's sockets
/// (manifest <c>sockets</c> on bodies, <c>attach</c> on <c>item_*</c> models: the item's attach point lands on the
/// socket, parented under the part it rides on).
/// </summary>
public static class SurvivorDressing
{
    private const string ReferenceBody = "survivor_adult_male";

    private static readonly Dictionary<BodyPart, string> LimbNodes = new()
    {
        [BodyPart.LeftArm] = "arm_l", [BodyPart.RightArm] = "arm_r",
        [BodyPart.LeftLeg] = "leg_l", [BodyPart.RightLeg] = "leg_r",
    };

    /// <summary>
    /// Sockets each slot may use, best first. Clothing and armor go on the torso; a weapon in the hand, else slung on
    /// the back; a tool on the back, else hung on the chest (over any garment), else in the hand.
    /// </summary>
    private static readonly (EquipSlot Slot, string[] Sockets)[] GearSockets =
    [
        (EquipSlot.Body, ["torso"]),
        (EquipSlot.Weapon, ["hand_r", "back"]),
        (EquipSlot.Tool, ["back", "torso", "hand_r"]),
    ];

    /// <summary>
    /// Hides missing limbs and swaps prosthetic ones in at the same joint. The prosthetic models are built for the
    /// adult male; they are moved to this body's joint and scaled down for a child.
    /// </summary>
    public static void FitLimbs(ModelLibrary library, Node3D model, Body body, bool child)
    {
        foreach (var (part, nodeName) in LimbNodes)
        {
            var state = body[part];
            if (state.Condition is PartCondition.Healthy or PartCondition.Injured) continue;
            if (model.FindChild(nodeName, true, false) is not Node3D limb) continue;
            limb.Visible = false;
            if (state.Condition != PartCondition.Prosthetic) continue;

            var kind = Body.RegionOf(part) == BodyRegion.Arms ? "arm" : "leg";
            var side = nodeName[^1];
            var crude = state.ProstheticQuality < 1 ? kind == "arm" ? "hook" : "peg" : "cyborg";
            if ((library.TryInstance($"prosthetic_{kind}_{crude}_{side}") ?? library.TryInstance($"prosthetic_{kind}_cyborg_{side}"))
                is not { } prosthetic) continue;
            var joint = prosthetic.FindChild(nodeName, true, false) as Node3D;
            var scale = child && joint is not null && joint.Position.Y > 0 ? limb.Position.Y / joint.Position.Y : 1f;
            prosthetic.Scale = Vector3.One * scale;
            prosthetic.Position = limb.Position - (joint?.Position ?? Vector3.Zero) * scale;
            limb.GetParent().AddChild(prosthetic);
        }
    }

    /// <summary>
    /// Hangs the equipped items (by slot, item ids may carry an <c>@quality</c> suffix) on a body model built from
    /// <paramref name="bodyId"/>. Call after <see cref="FitLimbs"/> so a hand item rides on a prosthetic arm and a
    /// missing arm drops its weapon to the back. Items with no model or no free socket are left off.
    /// </summary>
    public static void AttachGear(ModelLibrary library, Node3D model, string bodyId, IReadOnlyDictionary<EquipSlot, string> equipment)
    {
        var sockets = library.Sockets(bodyId);
        if (sockets.Count == 0) return;
        var scale = GearScale(library, bodyId);
        var taken = new HashSet<string>();
        foreach (var (slot, preferred) in GearSockets)
        {
            if (!equipment.TryGetValue(slot, out var itemId)) continue;
            var modelId = $"item_{itemId.Split('@')[0]}";
            var attach = library.AttachPoints(modelId);
            foreach (var name in preferred)
            {
                if (taken.Contains(name) || !attach.TryGetValue(name, out var at) || !sockets.TryGetValue(name, out var socket)) continue;
                if (VisiblePart(model, socket.Part) is not { } part || library.TryInstance(modelId) is not { } item) continue;
                // Model space: the item's attach point on the socket, turned by its rot_deg, scaled to the body.
                var basis = Basis.FromEuler(at.RotationDegrees * Mathf.Pi / 180f) * Basis.FromScale(Vector3.One * scale);
                var inModel = new Transform3D(basis, socket.Point - basis * at.Point);
                item.Transform = RelativeTo(model, part).AffineInverse() * inModel;
                item.Name = $"Gear_{slot}";
                part.AddChild(item);
                // A garment shares the chest with a hung tool (binoculars over a jacket); everything else is exclusive.
                if (slot != EquipSlot.Body) taken.Add(name);
                break;
            }
        }
    }

    /// <summary>Gear is modelled for the adult male; other bodies scale it by their chest height.</summary>
    private static float GearScale(ModelLibrary library, string bodyId)
    {
        if (bodyId == ReferenceBody) return 1f;
        return library.Sockets(bodyId).TryGetValue("torso", out var mine) && library.Sockets(ReferenceBody).TryGetValue("torso", out var reference)
               && reference.Point.Y > 0
            ? mine.Point.Y / reference.Point.Y
            : 1f;
    }

    /// <summary>The shown node for a body part: the swapped-in prosthetic's when the limb was replaced, null when missing.</summary>
    private static Node3D? VisiblePart(Node3D model, string? partName)
    {
        if (partName is null) return model;
        return model.FindChildren(partName, nameof(Node3D), true, false).OfType<Node3D>().LastOrDefault(n => ShownWithin(model, n));
    }

    private static bool ShownWithin(Node3D root, Node3D node)
    {
        for (Node? n = node; n is not null && n != root; n = n.GetParent())
            if (n is Node3D { Visible: false }) return false;
        return true;
    }

    /// <summary>A node's transform in <paramref name="root"/>'s space (works before the model enters the tree).</summary>
    private static Transform3D RelativeTo(Node3D root, Node3D node)
    {
        var xf = Transform3D.Identity;
        for (Node? n = node; n is not null && n != root; n = n.GetParent())
            if (n is Node3D n3) xf = n3.Transform * xf;
        return xf;
    }
}
