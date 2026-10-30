using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Survivors;
using Remnants.Game.Colony;

namespace Remnants.Game.Anim;

/// <summary>
/// Debug: <c>godot-mono --headless --path game -- --anim-selftest</c> checks the rigs headless and quits with 0 (pass)
/// or 1 (fail): every rigged model in the manifest gets a <see cref="CharacterRig"/> with its clips and loop flags;
/// a one-shot fires its manifest event once at the right time, then returns to the base loop; looping work clips fire
/// every stroke; walks get footfall steps; <c>downed</c> hands over to <c>downed_loop</c> and <c>death</c> holds;
/// prosthetics (peg, hook, cyborg; adult and child; one data-named mesh per tier, untinted) move under the body's skeleton and follow its clips;
/// an eye implant rides the neck under the head; a blade arm is open only around an attack's impact; gear hangs on
/// <see cref="BoneAttachment3D"/> sockets and moves with the hand; a portrait pose is idle; a static model gets no rig.
/// </summary>
public partial class AnimSelfTest : Node3D
{
    private readonly List<string> _failures = [];
    private ModelLibrary _library = null!;

    public override void _Ready() => CallDeferred(MethodName.Start);

    private void Start() => _ = Run();

    private async Task Run()
    {
        _library = ModelLibrary.Load();
        var rigged = _library.Tagged().Where(id => _library.Clips(id).Count > 0).ToList();
        GD.Print($"[anim-selftest] {_library.ModelCount} models, {rigged.Count} rigged");
        if (rigged.Count == 0) Fail("no rigged models in the manifest (run tools/sync-assets.sh)");

        foreach (var id in rigged)
        {
            var model = _library.TryInstance(id);
            if (model is null)
            {
                Fail($"{id}: does not load");
                continue;
            }
            AddChild(model);
            var rig = CharacterRig.Attach(_library, model, id);
            if (rig is null) Fail($"{id}: no AnimationPlayer");
            else
            {
                foreach (var clip in _library.Clips(id).Keys.Where(c => !rig.Has(c))) Fail($"{id}: clip {clip} in the manifest but not in the glb");
                if (rig.Current is not ("idle" or "hover")) Fail($"{id}: starts in {rig.Current}, expected idle or hover");
            }
            model.QueueFree();
        }

        await OneShot();
        await Loops();
        await Prosthetics();
        await Gear();
        await Portrait();
        StaticModel();

        foreach (var f in _failures) GD.PrintErr($"[anim-selftest] FAIL {f}");
        GD.Print(_failures.Count == 0 ? "[anim-selftest] PASS" : $"[anim-selftest] FAILED ({_failures.Count})");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private (Node3D Model, CharacterRig Rig, List<(string Clip, string Event, float At)> Events)? Make(string id, Body? body = null, bool child = false)
    {
        if (_library.TryInstance(id) is not { } model) return null;
        if (body is not null) SurvivorDressing.FitLimbs(_library, model, body, child);
        AddChild(model);
        if (CharacterRig.Attach(_library, model, id) is not { } rig) return null;
        var events = new List<(string, string, float)>();
        var clock = Time.GetTicksMsec();
        rig.ClipEvent += (clip, name) => events.Add((clip, name, (Time.GetTicksMsec() - clock) / 1000f));
        return (model, rig, events);
    }

    private async Task Frames(float seconds)
    {
        var end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (Time.GetTicksMsec() < end) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>attack_swing: one impact at 0.38 s, back to idle; downed → downed_loop; death holds.</summary>
    private async Task OneShot()
    {
        if (Make("survivor_adult_male") is not var (model, rig, events)) { Fail("survivor_adult_male: no rig"); return; }
        var finished = new List<string>();
        rig.ClipFinished += clip => finished.Add(clip);
        var info = rig.Info("attack_swing");
        rig.Once("attack_swing");
        await Frames((info?.Length ?? 1) + 0.3f);
        var impacts = events.Where(e => e is ("attack_swing", "impact", _)).ToList();
        if (impacts.Count != 1) Fail($"attack_swing fired {impacts.Count} impacts, expected 1");
        if (!finished.Contains("attack_swing")) Fail("attack_swing never finished");
        if (rig.Current != "idle") Fail($"after attack_swing the rig is in {rig.Current}, expected idle");

        rig.Once("downed", then: "downed_loop");
        await Frames((rig.Info("downed")?.Length ?? 1) + 0.2f);
        if (rig.Current != "downed_loop" || rig.Base != "downed_loop") Fail($"downed went to {rig.Current}/{rig.Base}, expected downed_loop");
        if (!events.Any(e => e is ("downed", "ground", _))) Fail("downed never hit the ground");

        rig.Once("death", hold: true);
        await Frames((rig.Info("death")?.Length ?? 1) + 0.3f);
        if (rig.Current != "death") Fail($"death did not hold (now {rig.Current})");
        model.QueueFree();
    }

    /// <summary>work_craft has two strokes per cycle; walks step twice per cycle.</summary>
    private async Task Loops()
    {
        if (Make("survivor_adult_female") is not var (model, rig, events)) { Fail("survivor_adult_female: no rig"); return; }
        var craft = rig.Info("work_craft");
        rig.Loop("work_craft");
        await Frames((craft?.Length ?? 1.6f) * 2 + 0.05f);
        var strokes = events.Count(e => e is ("work_craft", "impact", _));
        if (strokes < 3) Fail($"work_craft fired {strokes} impacts over two cycles, expected 4");
        events.Clear();
        rig.Loop("walk");
        await Frames((rig.Info("walk")?.Length ?? 1f) * 2 + 0.05f);
        var steps = events.Count(e => e is ("walk", "step", _));
        if (steps < 3) Fail($"walk fired {steps} footfalls over two cycles, expected 4");
        events.Clear();
        rig.SpeedScale = 2f;
        await Frames(rig.Info("walk")?.Length ?? 1f);
        if (events.Count(e => e is ("walk", "step", _)) < 3) Fail("walk at 2x did not step twice as often");
        model.QueueFree();
    }

    /// <summary>Peg leg (adult), hook arm and cyborg leg (child): hidden limb, the prosthetic under the skeleton, moving with walk.</summary>
    private async Task Prosthetics()
    {
        var cases = new (string Body, bool Child, BodyPart Part, double Quality, string Node)[]
        {
            ("survivor_adult_male", false, BodyPart.LeftLeg, 0.6, "leg_l"),
            ("survivor_adult_female", false, BodyPart.RightArm, 0.5, "arm_r"),
            ("survivor_child", true, BodyPart.RightLeg, 1.3, "leg_r"),
            ("survivor_adult_male", false, BodyPart.LeftArm, 1.3, "arm_l"),
        };
        foreach (var (bodyId, child, part, quality, node) in cases)
        {
            var body = new Body();
            body.Lose(part);
            body.FitProsthetic(part, quality);
            if (Make(bodyId, body, child) is not var (model, rig, _)) { Fail($"{bodyId}: no rig"); continue; }
            var skeleton = SurvivorDressing.Skeleton(model)!;
            var fitted = skeleton.GetNodeOrNull<MeshInstance3D>(node + SurvivorDressing.ProstheticSuffix);
            if (fitted is null) { Fail($"{bodyId} {part} q{quality}: no prosthetic under the skeleton"); model.QueueFree(); continue; }
            if (model.FindChild(node, true, false) is Node3D { Visible: true }) Fail($"{bodyId} {part}: the body's {node} still shows");
            if (fitted.GetNodeOrNull(fitted.Skeleton) != skeleton) Fail($"{bodyId} {part}: the prosthetic is not bound to the body skeleton");
            var bone = skeleton.FindBone(_library.LimbBones(bodyId).GetValueOrDefault(node)?.Last() ?? "");
            var rest = skeleton.GetBoneGlobalPose(bone).Origin;
            rig.Loop(node.StartsWith("leg") ? "walk" : "attack_swing");
            await Frames(0.35f);
            if (skeleton.GetBoneGlobalPose(bone).Origin.DistanceTo(rest) < 0.01f) Fail($"{bodyId} {part}: the limb bone does not move under the clip");
            model.QueueFree();
        }

        // The cybernetics data names the mesh, one per tier, worn as exported (the tier tint is only a stand-in).
        var tiers = new (string Item, BodyPart Part, string Node, string Mesh)[]
        {
            ("hook_arm", BodyPart.LeftArm, "arm_l", "prosthetic_arm_hook_l"),
            ("mech_leg", BodyPart.RightLeg, "leg_r", "prosthetic_leg_mech_r"),
            ("servo_arm", BodyPart.LeftArm, "arm_l", "prosthetic_arm_servo_l"),
            ("labor_arm_mk2", BodyPart.RightArm, "arm_r", "prosthetic_arm_labor_mk2_r"),
            ("cyber_leg", BodyPart.LeftLeg, "leg_l", "prosthetic_leg_cyborg_l"),
            ("network_arm", BodyPart.RightArm, "arm_r", "prosthetic_arm_network_r"),
        };
        foreach (var (item, part, node, meshId) in tiers)
        {
            var body = new Body();
            body.Lose(part);
            body.FitProsthetic(part, 1.2, item);
            if (Make("survivor_adult_male", body) is not var (m, _, _)) continue;
            var mesh = SurvivorDressing.Skeleton(m)!.GetNodeOrNull<MeshInstance3D>(node + SurvivorDressing.ProstheticSuffix);
            if (mesh?.Mesh is null || mesh.Mesh.GetFaces().Length != Faces(meshId)) Fail($"{item} did not fit {meshId}");
            else if (mesh.GetSurfaceOverrideMaterial(0) is not null) Fail($"{item} wears a tier tint over its own mesh");
            m.QueueFree();
        }

        // An eye implant: the eye part under the skeleton on neck, the head still shown, moving with the head.
        var eyed = new Body();
        eyed.Fit(BodyPart.Eyes, 1.2, "thermal_eye");
        if (Make("survivor_adult_female", eyed) is var (m3, rig3, _))
        {
            var skeleton = SurvivorDressing.Skeleton(m3)!;
            var eye = skeleton.GetNodeOrNull<MeshInstance3D>("eye" + SurvivorDressing.ProstheticSuffix);
            if (eye?.Mesh is null || eye.Mesh.GetFaces().Length != Faces("prosthetic_eye_thermal")) Fail("thermal_eye did not fit prosthetic_eye_thermal on the head");
            else if (eye.GetNodeOrNull(eye.Skeleton) != skeleton) Fail("the eye implant is not bound to the body skeleton");
            if (m3.FindChild("head", true, false) is Node3D { Visible: false }) Fail("the eye implant hid the head");
            var neck = skeleton.FindBone("neck");
            var rest = skeleton.GetBoneGlobalPose(neck);
            rig3.Loop("walk");
            await Frames(0.35f);
            if (skeleton.GetBoneGlobalPose(neck).IsEqualApprox(rest)) Fail("the neck (and the eye on it) does not move under walk");
            m3.QueueFree();
        }

        // A blade arm: closed at rest, open around attack_swing's impact, closed again after.
        var bladed = new Body();
        bladed.Lose(BodyPart.RightArm);
        bladed.FitProsthetic(BodyPart.RightArm, 1.2, "blade_arm_mk2");
        if (Make("survivor_adult_male", bladed) is var (m4, rig4, _))
        {
            var skeleton = SurvivorDressing.Skeleton(m4)!;
            var closed = skeleton.GetNodeOrNull<Node3D>("arm_r" + SurvivorDressing.ProstheticSuffix);
            var open = skeleton.GetNodeOrNull<Node3D>("arm_r" + SurvivorDressing.ProstheticSuffix + BladeArm.OpenSuffix);
            if (closed is null || open is null) Fail("blade_arm_mk2 did not fit both blade meshes");
            else
            {
                await Frames(0.1f);
                if (!closed.Visible || open.Visible) Fail("the blade is out at rest");
                var impact = rig4.EventDelay("attack_swing", ClipMap.Impact);
                rig4.Once("attack_swing");
                await Frames(impact + 0.05f);
                if (closed.Visible || !open.Visible) Fail("the blade is not out at attack_swing's impact");
                await Frames((rig4.Info("attack_swing")?.Length ?? 1f) + 0.3f - impact);
                if (!closed.Visible || open.Visible) Fail("the blade did not fold after attack_swing");
            }
            m4.QueueFree();
        }
    }

    private int Faces(string id) =>
        _library.TryInstance(id)?.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().First().Mesh.GetFaces().Length ?? -1;

    /// <summary>A weapon in the right hand rides a BoneAttachment3D on socket_hand_r and moves with the swing.</summary>
    private async Task Gear()
    {
        var weapon = _library.Tagged("gear").FirstOrDefault(id => _library.AttachPoints(id).ContainsKey("hand_r"));
        if (weapon is null) { Fail("no gear with a hand_r attach point"); return; }
        if (_library.TryInstance("survivor_adult_male") is not { } model) return;
        SurvivorDressing.AttachGear(_library, model, "survivor_adult_male", new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = weapon["item_".Length..] });
        AddChild(model);
        var socket = model.FindChildren("*", nameof(BoneAttachment3D), true, false).OfType<BoneAttachment3D>().FirstOrDefault(b => b.BoneName == "socket_hand_r");
        if (socket?.GetNodeOrNull<Node3D>("Gear_Weapon") is not { } held) { Fail($"{weapon} is not on a socket_hand_r BoneAttachment3D"); model.QueueFree(); return; }
        var rig = CharacterRig.Attach(_library, model, "survivor_adult_male")!;
        await Frames(0.05f);
        var rest = held.GlobalPosition;
        var expected = _library.Sockets("survivor_adult_male")["hand_r"].Point;
        if (socket.GlobalPosition.DistanceTo(expected) > 0.15f) Fail($"socket_hand_r sits at {socket.GlobalPosition}, manifest says {expected}");
        rig.Once("attack_swing");
        await Frames(0.35f);
        if (held.GlobalPosition.DistanceTo(rest) < 0.05f) Fail("the held weapon does not move with attack_swing");

        // One-armed (right arm lost): the weapon goes to the left hand.
        var body = new Body();
        body.Lose(BodyPart.RightArm);
        var lefty = _library.TryInstance("survivor_adult_male")!;
        SurvivorDressing.FitLimbs(_library, lefty, body, false);
        SurvivorDressing.AttachGear(_library, lefty, "survivor_adult_male", new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = weapon["item_".Length..] });
        var left = lefty.FindChildren("*", nameof(BoneAttachment3D), true, false).OfType<BoneAttachment3D>().FirstOrDefault(b => b.GetNodeOrNull("Gear_Weapon") is not null);
        if (left?.BoneName != "socket_hand_l") Fail($"a one-armed survivor holds the weapon on {left?.BoneName ?? "nothing"}, expected socket_hand_l");
        lefty.Free();
        model.QueueFree();
    }

    /// <summary>PoseStill puts a model in idle's first frame.</summary>
    private async Task Portrait()
    {
        if (_library.TryInstance("survivor_adult_female") is not { } model) return;
        AddChild(model);
        CharacterRig.PoseStill(model, "idle");
        await Frames(0.05f);
        var player = model.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
        if (player?.CurrentAnimation != "idle" && player?.AssignedAnimation != "idle") Fail("the portrait pose is not idle");
        model.QueueFree();
    }

    private void StaticModel()
    {
        var id = _library.Tagged("building").FirstOrDefault();
        if (id is null || _library.TryInstance(id) is not { } building) return;
        if (CharacterRig.Attach(_library, building, id) is not null) Fail($"{id} (static) got a rig");
        building.Free();
    }

    private void Fail(string message) => _failures.Add(message);
}
