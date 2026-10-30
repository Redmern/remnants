using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony;

/// <summary>
/// Purely visual body dressing shared by the colony and scavenge views: missing limbs hidden, prosthetics swapped in
/// (the model the part's cybernetics node names, tinted by its tier; see <see cref="ProstheticModel"/>), and equipped
/// gear hung on the body's sockets (manifest <c>sockets</c> on bodies, <c>attach</c> on <c>item_*</c> models: the item's attach point lands on the
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
    /// the back (the left hand when only that arm is left); a tool on the back, else hung on the chest (over any garment),
    /// else in the hand.
    /// </summary>
    private static readonly (EquipSlot Slot, string[] Sockets)[] GearSockets =
    [
        (EquipSlot.Body, ["torso"]),
        (EquipSlot.Weapon, ["hand_r", "hand_l", "back"]),
        (EquipSlot.Tool, ["back", "torso", "hand_r"]),
    ];

    /// <summary>
    /// Hides missing limbs and fits prosthetic ones. On a rigged body (manifest v2) the prosthetic's skinned mesh moves
    /// under the body's <c>Skeleton3D</c> (its skin binds by bone name to the bones in <c>limb_bones</c>), so it follows
    /// every clip. A static body (v1) gets the old swap: the prosthetic model placed at the limb's joint, scaled down for
    /// a child.
    /// </summary>
    public static void FitLimbs(ModelLibrary library, Node3D model, Body body, bool child)
    {
        var skeleton = Skeleton(model);
        foreach (var (part, nodeName) in LimbNodes)
        {
            var state = body[part];
            if (state.Condition is PartCondition.Healthy or PartCondition.Injured) continue;
            if (model.FindChild(nodeName, true, false) is not Node3D limb) continue;
            limb.Visible = false;
            if (state.Condition != PartCondition.Prosthetic) continue;

            var kind = Body.RegionOf(part) == BodyRegion.Arms ? "arm" : "leg";
            var side = nodeName[^1];
            var (variant, tint) = ProstheticModel(state, kind);
            var id = new[] { $"prosthetic_{kind}_{variant}_{side}", variant, $"prosthetic_{kind}_cyborg_{side}" }.FirstOrDefault(library.Has);
            if (id is null || library.TryInstance(id) is not { } prosthetic) continue;
            if (tint is { } t) Tint(prosthetic, t);
            if (skeleton is not null && Skinned(prosthetic, nodeName) is { } mesh)
            {
                var bones = library.LimbBones(id).GetValueOrDefault(nodeName) ?? [];
                if (bones.All(b => skeleton.FindBone(b) >= 0))
                {
                    mesh.GetParent().RemoveChild(mesh);
                    mesh.Owner = null;
                    mesh.Name = nodeName + ProstheticSuffix;
                    mesh.Transform = Transform3D.Identity;
                    skeleton.AddChild(mesh);
                    mesh.Skeleton = mesh.GetPathTo(skeleton);
                }
                prosthetic.Free();
                continue;
            }
            var joint = prosthetic.FindChild(nodeName, true, false) as Node3D;
            var scale = child && joint is not null && joint.Position.Y > 0 ? limb.Position.Y / joint.Position.Y : 1f;
            prosthetic.Scale = Vector3.One * scale;
            prosthetic.Position = limb.Position - (joint?.Position ?? Vector3.Zero) * scale;
            limb.GetParent().AddChild(prosthetic);
        }
    }

    /// <summary>A prosthetic mesh moved onto a rigged body is named <c>{part}_prosthetic</c> (e.g. <c>leg_l_prosthetic</c>).</summary>
    public const string ProstheticSuffix = "_prosthetic";

    /// <summary>
    /// The model variant for a fitted part (<c>prosthetic_{kind}_{variant}_{side}</c>) and the tint it wears: the node's
    /// <c>model</c> id and its tier's tint (cybernetics.json), so per-tier meshes drop in by data. Parts outside the tree
    /// (old saves without data) fall back to hook/peg below quality 1 and cyborg from there.
    /// </summary>
    public static (string Variant, Color? Tint) ProstheticModel(BodyPartState state, string kind)
    {
        var cyber = GameDataPaths.LoadScavenge().Cybernetics;
        if (cyber?.Node(state.ProstheticItemId) is { } node)
            return (node.Model, cyber.Tier(node.Tier).Tint is { } hex ? new Color(hex) : null);
        return (state.ProstheticQuality < 1 ? kind == "arm" ? "hook" : "peg" : "cyborg", null);
    }

    /// <summary>A part's look for model and portrait cache keys: condition plus which prosthetic it wears.</summary>
    public static string LimbKey(BodyPartState p) =>
        p.Condition == PartCondition.Prosthetic ? $"{(int)p.Condition}{p.ProstheticItemId ?? (p.ProstheticQuality < 1 ? "-" : "+")}" : $"{(int)p.Condition}";

    /// <summary>
    /// Stand-in for per-tier meshes: every surface gets a copy of its material with the albedo pulled toward the tier's
    /// tint (the copy is per instance, so the shared model stays as it is).
    /// </summary>
    private static void Tint(Node node, Color tint)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh is { } m)
            for (var i = 0; i < m.GetSurfaceCount(); i++)
                if (mesh.GetActiveMaterial(i) is BaseMaterial3D material && material.Duplicate() is BaseMaterial3D copy)
                {
                    copy.AlbedoColor = material.AlbedoColor * Colors.White.Lerp(tint, 0.7f);
                    if (tint.V > 0.75f && tint.B > tint.R) // Network-grade: a faint cold glow
                    {
                        copy.EmissionEnabled = true;
                        copy.Emission = tint;
                        copy.EmissionEnergyMultiplier = 0.25f;
                    }
                    mesh.SetSurfaceOverrideMaterial(i, copy);
                }
        foreach (var child in node.GetChildren()) Tint(child, tint);
    }

    /// <summary>The body's skeleton on a rigged model; null on a static one.</summary>
    public static Skeleton3D? Skeleton(Node3D model) =>
        model.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().FirstOrDefault();

    /// <summary>A prosthetic model's skinned limb mesh (named after the part it replaces).</summary>
    private static MeshInstance3D? Skinned(Node3D prosthetic, string part) =>
        prosthetic.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
            .Where(m => m.Skin is not null).OrderBy(m => m.Name == part ? 0 : 1).FirstOrDefault();

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
        var skeleton = Skeleton(model);
        var taken = new HashSet<string>();
        foreach (var (slot, preferred) in GearSockets)
        {
            if (!equipment.TryGetValue(slot, out var itemId)) continue;
            var modelId = $"item_{itemId.Split('@')[0]}";
            var attach = library.AttachPoints(modelId);
            foreach (var name in preferred)
            {
                // Gear has no hand_l attach point: the left hand holds it as the right one would.
                var atName = name == "hand_l" && !attach.ContainsKey(name) ? "hand_r" : name;
                if (taken.Contains(name) || !attach.TryGetValue(atName, out var at) || !sockets.TryGetValue(name, out var socket)) continue;
                var basis = Basis.FromEuler(at.RotationDegrees * Mathf.Pi / 180f) * Basis.FromScale(Vector3.One * scale);
                if (skeleton is not null && socket.Bone is { } bone && skeleton.FindBone(bone) >= 0)
                {
                    // Rigged: under a BoneAttachment3D on the socket bone (identity at rest at the socket point).
                    if (!PartShown(model, skeleton, socket.Part) || library.TryInstance(modelId) is not { } held) continue;
                    held.Transform = new Transform3D(basis, -(basis * at.Point));
                    held.Name = $"Gear_{slot}";
                    SocketNode(skeleton, bone).AddChild(held);
                }
                else
                {
                    if (VisiblePart(model, socket.Part) is not { } part || library.TryInstance(modelId) is not { } item) continue;
                    // Model space: the item's attach point on the socket, turned by its rot_deg, scaled to the body.
                    var inModel = new Transform3D(basis, socket.Point - basis * at.Point);
                    item.Transform = RelativeTo(model, part).AffineInverse() * inModel;
                    item.Name = $"Gear_{slot}";
                    part.AddChild(item);
                }
                // A garment shares the chest with a hung tool (binoculars over a jacket); everything else is exclusive.
                if (slot != EquipSlot.Body) taken.Add(name);
                break;
            }
        }
    }

    /// <summary>The body's <see cref="BoneAttachment3D"/> on a socket bone, made on first use.</summary>
    public static BoneAttachment3D SocketNode(Skeleton3D skeleton, string bone)
    {
        var name = $"Socket_{bone}";
        if (skeleton.GetNodeOrNull<BoneAttachment3D>(name) is { } existing) return existing;
        var socket = new BoneAttachment3D { Name = name, BoneName = bone };
        skeleton.AddChild(socket);
        return socket;
    }

    /// <summary>On a rigged body: the part is there (its own mesh shown, or a prosthetic fitted in its place).</summary>
    private static bool PartShown(Node3D model, Skeleton3D skeleton, string? part) =>
        part is null || skeleton.GetNodeOrNull(part + ProstheticSuffix) is not null
                     || model.FindChild(part, true, false) is not Node3D node || ShownWithin(model, node);

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
