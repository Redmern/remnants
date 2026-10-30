using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Game.Colony;

namespace Remnants.Game.Anim;

/// <summary>
/// A blade prosthetic's two meshes on one body: <c>{part}_prosthetic</c> (closed) and <c>{part}_prosthetic_open</c>
/// (<c>prosthetic_arm_blade_open_*</c>). The blade swings open around an attack clip's <c>impact</c> and folds away
/// otherwise. Added by <see cref="SurvivorDressing.FitLimbs"/>; it finds the body's <see cref="CharacterRig"/> on its
/// own, so a model without one keeps the blade closed. Purely visual.
/// </summary>
public partial class BladeArm : Node
{
    public const string OpenSuffix = "_open";

    /// <summary>The blade is out from this long before the impact until this long after it.</summary>
    private const float Lead = 0.15f, Hold = 0.35f;

    private readonly List<(Node3D Closed, Node3D Open)> _pairs = [];
    private CharacterRig? _rig;

    /// <summary>Whether the blade is out now.</summary>
    public bool IsOpen { get; private set; }

    public override void _Ready()
    {
        if (GetParent() is not Node3D model || SurvivorDressing.Skeleton(model) is not { } skeleton) return;
        foreach (var open in skeleton.GetChildren().OfType<Node3D>().Where(n => n.Name.ToString().EndsWith(SurvivorDressing.ProstheticSuffix + OpenSuffix)))
            if (skeleton.GetNodeOrNull<Node3D>(open.Name.ToString()[..^OpenSuffix.Length]) is { } closed)
                _pairs.Add((closed, open));
    }

    public override void _Process(double delta)
    {
        _rig ??= CharacterRig.Of(GetParent() as Node3D);
        Set(_rig is not null && Out(_rig));
    }

    /// <summary>Inside an attack clip's impact window.</summary>
    public static bool Out(CharacterRig rig) =>
        rig.Current.StartsWith("attack") && rig.Info(rig.Current)?.Events.GetValueOrDefault(ClipMap.Impact) is { Length: > 0 } hits
        && hits.Any(at => rig.ClipTime >= at - Lead && rig.ClipTime <= at + Hold);

    private void Set(bool open)
    {
        if (open == IsOpen) return;
        IsOpen = open;
        foreach (var (closed, opened) in _pairs)
        {
            closed.Visible = !open;
            opened.Visible = open;
        }
    }
}
