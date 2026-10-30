using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Data;

namespace Remnants.Game.Colony;

/// <summary>
/// Purely visual: hangs a vehicle's fitted upgrades on its model's upgrade slot nodes (the manifest's <c>upgrade_slots</c>):
/// engine and utility upgrades under <c>upg_engine</c> / <c>upg_utility_N</c> scaled by the slot's scale (a roof turret takes
/// utility 1, the roof), armor as <c>{model}_t{tier}</c> under <c>upg_armor</c> unscaled, a trailer on <c>tow_hitch</c>, and
/// tyres swapped onto every <c>wheel_*</c> node (not on parked extraction vehicles, whose wheels are baked). A slot node the
/// model lacks is made at the manifest point; an upgrade with no mesh, or no free utility node, is left off.
/// </summary>
public static class VehicleDressing
{
    public static void Dress(ModelLibrary library, Node3D model, string assetId, int tier, IEnumerable<VehicleUpgradeDef> upgrades, bool swapTyres = true)
    {
        var slots = library.VehicleSlots(assetId);
        Node3D Slot(string name)
        {
            if (Find(model, name) is { } node) return node;
            var made = new Node3D { Name = name, Position = slots.TryGetValue(name, out var s) ? s.Point : Vector3.Zero };
            model.AddChild(made);
            return made;
        }
        float ScaleOf(string name) => slots.TryGetValue(name, out var s) ? s.Scale : 1f;

        var utility = 1;
        foreach (var u in upgrades.OrderByDescending(u => u.TurretDamage > 0))
        {
            if (u.Model is not { } mesh) continue;
            switch (u.Slot)
            {
                case VehicleSlot.Engine:
                    Hang(library, Slot("upg_engine"), mesh, ScaleOf("upg_engine"));
                    break;
                case VehicleSlot.Armor:
                    Hang(library, Slot("upg_armor"), $"{mesh}_t{tier}", 1f);
                    break;
                case VehicleSlot.Tyres when swapTyres:
                    SwapTyres(library, model, mesh, slots.TryGetValue("upg_tyres", out var t) ? t.Wheels : new Dictionary<string, Vector3>());
                    break;
                case VehicleSlot.Utility when mesh == "upg_trailer":
                    Hang(library, Slot("tow_hitch"), mesh, ScaleOf("tow_hitch"));
                    break;
                case VehicleSlot.Utility:
                    var name = $"upg_utility_{utility}";
                    if (!slots.ContainsKey(name) && Find(model, name) is null) break;
                    Hang(library, Slot(name), mesh, ScaleOf(name));
                    utility++;
                    break;
            }
        }
    }

    private static void Hang(ModelLibrary library, Node3D slot, string meshId, float scale)
    {
        if (library.TryInstance(meshId) is not { } part) return;
        part.Scale = Vector3.One * scale;
        slot.AddChild(part);
    }

    /// <summary>Each <c>wheel_*</c> node's own meshes are hidden and the tyre hangs there, scaled to that wheel.</summary>
    private static void SwapTyres(ModelLibrary library, Node3D model, string tyreId, IReadOnlyDictionary<string, Vector3> wheels)
    {
        foreach (var wheel in model.FindChildren("wheel_*", "", true, false).OfType<Node3D>().ToList())
        {
            if (library.TryInstance(tyreId) is not { } tyre) return;
            if (wheel is MeshInstance3D own) own.Mesh = null;
            foreach (var mesh in wheel.GetChildren().OfType<MeshInstance3D>()) mesh.Visible = false;
            tyre.Scale = wheels.TryGetValue(wheel.Name, out var s) ? s : Vector3.One;
            wheel.AddChild(tyre);
        }
    }

    private static Node3D? Find(Node root, string name) => root.FindChild(name, true, false) as Node3D;
}
