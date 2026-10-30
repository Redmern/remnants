using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge;

/// <summary>A field unit's portrait (its model with limbs and gear, see <see cref="PortraitStudio"/>), re-rendered when either changes.</summary>
public static class UnitPortrait
{
    public static Texture2D For(SceneTree tree, Unit unit)
    {
        var gear = string.Join(",", unit.Equipment.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        return PortraitStudio.For(tree, $"su{unit.Id}|{PortraitStudio.LookOf(unit.Body)}|{gear}", lib => new ScavengeModels(lib).Unit(unit));
    }
}
