using System.Collections.Generic;
using System.Linq;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Anim;

/// <summary>
/// Maps what Core says a unit does to clip names, best first, so <see cref="CharacterRig.Pick"/> takes the first clip
/// the body has (a Husk has <c>attack_claw</c> where a survivor has <c>attack_swing</c>). Attack clips go by the action
/// id (data: <c>items.json</c> actions), then by its pattern; work loops by the building's job. Clip names are the asset
/// contract (remnants-assets <c>rigspec.CLIPS</c>).
/// </summary>
public static class ClipMap
{
    private static readonly Dictionary<string, string[]> Actions = new()
    {
        ["punch"] = ["attack_punch", "attack_swing"],
        ["swing"] = ["attack_swing"],
        ["slash"] = ["attack_swing"],
        ["butt_strike"] = ["attack_swing", "attack_punch"],
        ["slam"] = ["attack_swing", "area_pulse"],
        ["thrust"] = ["attack_thrust", "attack_swing"],
        ["claw"] = ["attack_claw", "attack_swing"],
        ["bite"] = ["attack_claw", "attack_punch"],
        ["shoot"] = ["shoot_pistol", "shoot_burst", "shoot_rifle"],
        ["aimed_shot"] = ["shoot_rifle", "shoot_burst", "shoot_pistol"],
        ["burst"] = ["shoot_carbine", "shoot_burst", "shoot_rifle"],
        ["zap"] = ["shoot_burst", "shoot_pistol", "area_pulse"],
        ["pulse"] = ["area_pulse", "shoot_burst"],
    };

    private static readonly string[] Melee = ["attack_swing", "attack_claw", "attack_punch", "attack_thrust", "area_pulse"];
    private static readonly string[] Ranged = ["shoot_pistol", "shoot_burst", "shoot_rifle", "shoot_carbine", "area_pulse"];

    /// <summary>Work loops by job id (data: <c>jobs.json</c>); an unknown job crafts.</summary>
    private static readonly Dictionary<string, string> Jobs = new()
    {
        ["woodcutting"] = "work_chop",
        ["mining"] = "work_mine",
        ["farming"] = "work_farm",
        ["scavenging"] = "work_hammer",
        ["crafting"] = "work_hammer",
        ["gunsmithing"] = "work_hammer",
        ["medicine"] = "work_craft",
        ["tailoring"] = "work_craft",
        ["cooking"] = "work_craft",
        ["electronics"] = "work_craft",
    };

    /// <summary>The clip for an attack: by action id, then melee vs ranged; the events it carries are <c>impact</c> or <c>fire</c>.</summary>
    public static string[] Attack(string? actionId, AttackPattern? pattern) =>
        (actionId is not null && Actions.TryGetValue(actionId, out var named) ? named : [])
        .Concat(pattern is AttackPattern.Melee ? Melee : pattern is null ? Melee.Concat(Ranged) : Ranged)
        .Concat(pattern is AttackPattern.Melee ? Ranged : Melee)
        .Distinct().ToArray();

    /// <summary>The event in an attack clip at which the blow lands or the shot leaves.</summary>
    public const string Impact = "impact", Fire = "fire", Pulse = "pulse";

    public static string Work(string? jobId) => jobId is not null && Jobs.TryGetValue(jobId, out var clip) ? clip : "work_craft";

    /// <summary>The loop a standing unit holds between turns: its stance, downed, being carried, hacking.</summary>
    public static string[] Hold(Unit unit) =>
        unit.Status == UnitStatus.Downed ? ["downed_loop", "idle"]
        : unit.Kind == UnitKind.Drone || unit.EnemyDefId?.EndsWith("drone") == true ? ["hover", "idle"]
        : unit.Stance == Stance.Overwatch ? ["overwatch_aim", "idle"]
        : unit.Stance == Stance.Brace ? ["crouch_brace", "take_cover", "idle"]
        : ["idle", "hover"];

    /// <summary>Moving: carrying a downed ally, else walking.</summary>
    public static string[] Move(Unit unit) =>
        unit.CarryingId is not null ? ["carry_walk", "walk"] : ["walk", "hover"];
}
