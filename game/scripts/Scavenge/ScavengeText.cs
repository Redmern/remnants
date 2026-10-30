using System.Collections.Generic;
using System.Linq;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Game.Scavenge;

/// <summary>View-model formatting of scavenge state for labels. Read-only; no rules.</summary>
public static class ScavengeText
{
    /// <summary>"Dust storm" for <c>dust_storm</c>; "Clear" for none; "Heavy rain" from 0.75 up.</summary>
    public static string WeatherName(string? weather, double intensity) => weather switch
    {
        null or "" or "clear" => "Clear",
        "rain" when intensity >= 0.75 => "Heavy rain",
        _ => char.ToUpperInvariant(weather[0]) + weather[1..].Replace('_', ' '),
    };

    /// <summary>What the run's weather does in the field, one line each, from its <c>scavenge.json</c> entry.</summary>
    public static string WeatherTip(ScavengeData data, ScavengeEnvironment env)
    {
        var lines = new List<string> { WeatherName(env.Weather, env.Intensity) + "." };
        if (env.Weather is not { } w || !data.Rules.Environment.Weather.TryGetValue(w, out var e))
            return lines[0] + " Nothing in the air to help or hinder.";
        string Pct(double m) => $"{(m >= 1 ? "+" : "−")}{System.Math.Abs(System.Math.Round((m - 1) * 100)):0}%";
        var sight = e.Sight.Where(kv => System.Math.Abs(kv.Value - 1) > 1e-9).Select(kv => $"{kv.Key} {Pct(kv.Value)}").ToList();
        if (sight.Count > 0) lines.Add("Sight: " + string.Join(", ", sight) + ".");
        if (e.StealthBonus > 0)
            lines.Add($"+{e.StealthBonus:0} stealth for everyone" + (e.StealthIgnoredBy.Count > 0 ? $" (not against {string.Join(", ", e.StealthIgnoredBy)})." : "."));
        if (e.HearingMultiplier < 1) lines.Add($"Noise and footsteps carry {Pct(e.HearingMultiplier)} as far.");
        if (e.FlareRoundsMultiplier < 1) lines.Add("Flares burn out sooner.");
        if (e.RangedAccuracyPenalty > 0) lines.Add($"Ranged attacks −{e.RangedAccuracyPenalty * 100:0} points to hit.");
        if (e.DronesGrounded) lines.Add("Drones are grounded (one in the air can still be recalled).");
        if (e.RelayRangeMultiplier < 1) lines.Add($"Linked relay range {Pct(e.RelayRangeMultiplier)}: their squads lose touch.");
        if (e.ThunderChance > 0) lines.Add("Thunder now and then drowns out every footstep for a round.");
        if (e.LightningChance > 0) lines.Add("Lightning now and then shows everyone on the map for a round.");
        if (env.Shifts.Count > 0) lines.Add("The sky is changing: it may turn during the run.");
        return string.Join("\n", lines);
    }

    /// <summary>"Knight — Grappling Hook" for a mode with a source, "King 2" for the plain walk.</summary>
    public static string Mode(MoveMode mode)
    {
        var name = mode.Pattern == MovePattern.Knight ? "Knight" : $"{mode.Pattern} {mode.Range}";
        return mode.Source is { } source ? $"{name} — {source}" : name;
    }

    /// <summary>Every move mode, primary first, with AP cost when it is not 1.</summary>
    public static string Movement(MovementProfile movement) =>
        string.Join("\n", movement.Modes.Select(m => m.ApCost == 1 ? Mode(m) : $"{Mode(m)} ({m.ApCost} AP)"));

    public static string Item(ScavengeData data, string id) => data.Items.TryGetValue(id, out var item) ? item.Name : id;

    /// <summary>"Bandage x2, Scrap" — counts repeated ids.</summary>
    public static string Items(ScavengeData data, IEnumerable<string> ids)
    {
        var groups = ids.GroupBy(id => id).Select(g => g.Count() > 1 ? $"{Item(data, g.Key)} x{g.Count()}" : Item(data, g.Key)).ToList();
        return groups.Count == 0 ? "nothing" : string.Join(", ", groups);
    }

    public static string Part(BodyPart part) => part switch
    {
        BodyPart.LeftArm => "Left arm",
        BodyPart.RightArm => "Right arm",
        BodyPart.LeftLeg => "Left leg",
        BodyPart.RightLeg => "Right leg",
        _ => part.ToString(),
    };

    /// <summary>One line per body part: condition and how well it works.</summary>
    public static IEnumerable<(string Text, PartCondition Condition)> BodyLines(Body body) =>
        body.Parts.OrderBy(kv => kv.Key).Select(kv => (kv.Value.Condition switch
        {
            PartCondition.Healthy => $"{Part(kv.Key)}: ok",
            PartCondition.Injured => $"{Part(kv.Key)}: injured {kv.Value.Severity:P0}",
            PartCondition.Prosthetic when kv.Value.Interfered => $"{Part(kv.Key)}: cybernetic, LOCKED by interference",
            PartCondition.Prosthetic => $"{Part(kv.Key)}: cybernetic {kv.Value.Efficiency:P0}",
            _ => $"{Part(kv.Key)}: missing",
        }, kv.Value.Condition));

    public static string Status(Unit u) => u.Status switch
    {
        UnitStatus.Downed when u.Stabilized => "Downed (stabilized)",
        UnitStatus.Downed => $"Downed — bleeds out in {u.BleedOut} round{(u.BleedOut == 1 ? "" : "s")}",
        UnitStatus.Extracted => "Extracted",
        UnitStatus.Evacuated => "Evacuated",
        UnitStatus.Dead => "Dead",
        _ => "Active",
    };

    public static string Kind(Unit u) => u.Kind switch
    {
        UnitKind.Enemy => u.EnemyDefId is "linked" ? "Linked (Network)" : "Husk",
        UnitKind.Npc when u.FollowingId is not null => "Recruit",
        UnitKind.Npc => "Stranger",
        UnitKind.Drone => "Scout drone",
        UnitKind.Device => u.EnemyDefId is "turret" ? "Turret (device)" : "Camera (device)",
        _ => u.Origin is { IsChild: true } ? "Survivor (child)" : "Survivor",
    };

    /// <summary>Short, player-facing name for a command error code.</summary>
    public static string Error(ScavengeError error) => error switch
    {
        ScavengeError.Finished => "Run over",
        ScavengeError.UnknownUnit => "No such unit",
        ScavengeError.NotYourUnit => "Not your unit",
        ScavengeError.NotYourTurn => "Not their turn",
        ScavengeError.Incapacitated => "Can't act",
        ScavengeError.NotEnoughAp => "Not enough AP",
        ScavengeError.Unreachable => "Can't move there",
        ScavengeError.UnknownAction => "No such action",
        ScavengeError.InvalidTarget => "Invalid target",
        ScavengeError.OutOfRange => "Out of range",
        ScavengeError.NoLineOfSight => "No line of sight",
        ScavengeError.ItemNotCarried => "Not carried",
        ScavengeError.NotUsable => "Can't use that",
        ScavengeError.UnknownCache => "No such cache",
        ScavengeError.TooFar => "Too far",
        ScavengeError.ItemNotInCache => "Not in cache",
        ScavengeError.CarryLimitExceeded => "Too heavy",
        ScavengeError.NotOnExit => "Not on an exit",
        ScavengeError.NotRecruitable => "Can't recruit",
        ScavengeError.NotVisible => "Can't see that",
        ScavengeError.FriendlyFire => "That's an ally",
        ScavengeError.NotAPlayer => "No such player",
        ScavengeError.AlreadyAllied => "Already allies",
        ScavengeError.NoContact => "Not in contact",
        ScavengeError.NoProposal => "No offer",
        ScavengeError.ProposalPending => "Offer already open",
        ScavengeError.NotAllied => "No alliance",
        ScavengeError.BreakPending => "Already leaving",
        ScavengeError.PingLimit => "No pings left",
        ScavengeError.CannotCarry => "Can't carry",
        ScavengeError.NoHackingTool => "No hacking tool",
        ScavengeError.UnknownDevice => "No such device",
        ScavengeError.AlreadyHacked => "Nothing to hack",
        ScavengeError.LockedOut => "Locked out",
        ScavengeError.NotHackable => "Can't hack that",
        ScavengeError.NoDrone => "No drone",
        ScavengeError.DroneAlreadyOut => "Drone already out",
        ScavengeError.LowBattery => "Battery low",
        ScavengeError.MissingPerk => "Needs a perk",
        _ => error.ToString(),
    };

    public static string Device(DeviceKind kind) => kind switch
    {
        DeviceKind.Terminal => "Terminal",
        DeviceKind.Camera => "Camera",
        DeviceKind.Turret => "Turret",
        DeviceKind.Door => "Blast door",
        DeviceKind.Locker => "Locker",
        _ => "Relay node",
    };

    /// <summary>What hacking a device of this kind does, for its tooltip and hack button.</summary>
    public static string HackEffect(DeviceKind kind) => kind switch
    {
        DeviceKind.Terminal => "its data (datashards, blueprints) into your pack, and the zone's map and enemy positions",
        DeviceKind.Camera => "it watches for your team instead of alerting the Linked",
        DeviceKind.Turret => "it fires at your enemies instead of you",
        DeviceKind.Door => "your team passes, everyone else is locked out",
        DeviceKind.Locker => "it opens: its loot spills out to pick up",
        _ => "the Linked around it lose the Network, and no reinforcements come",
    };

    /// <summary>What a device does now, as the local side knows it.</summary>
    public static string DeviceState(ZoneDevice d, string controller, string localSide) =>
        d.Disabled ? "destroyed"
        : d.Used ? (d.Kind == DeviceKind.Terminal ? "downloaded" : "opened")
        : controller == localSide ? "hacked: works for your team"
        : controller == Unit.EnemySide ? (d.Networked ? "on the Network" : "locked")
        : "hacked by a rival";
}
