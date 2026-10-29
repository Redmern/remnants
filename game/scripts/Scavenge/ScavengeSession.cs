using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Game.Scavenge;

/// <summary>A mission handed to the scavenge scene: where the game runs (local sim or server) and who the local player is.</summary>
public sealed record ScavengeMission(IScavengeDriver Driver, Action<ExtractionResult>? OnFinished, string ReturnScene);

/// <summary>
/// The single entry point into scavenge mode. A caller (the colony loop, the main menu) hands over a ready
/// <see cref="ScavengeSim"/> with <see cref="Begin"/>; the scavenge scene plays it and, once the party's run is
/// over and the player closed the summary, <see cref="End"/> stores the <see cref="ExtractionResult"/>, raises
/// <see cref="Finished"/> and either calls the caller's callback or returns to the caller's scene.
/// </summary>
public static class ScavengeSession
{
    public const string ScenePath = "res://scenes/scavenge/Scavenge.tscn";
    public const string MenuScene = "res://scenes/MainMenu.tscn";
    public const int LocalPlayerId = 1;

    public const string DebugZone = "suburb_ruins";
    public const ulong DebugSeed = 7;

    /// <summary>The mission the scavenge scene should play (null until <see cref="Begin"/>).</summary>
    public static ScavengeMission? Pending { get; private set; }

    /// <summary>Result of the last finished mission, for callers that poll instead of subscribing.</summary>
    public static ExtractionResult? LastResult { get; private set; }

    /// <summary>Raised when a mission ends, before control goes back to the caller.</summary>
    public static event Action<ExtractionResult>? Finished;

    /// <summary>
    /// Plays <paramref name="sim"/> as <paramref name="playerId"/>. When it ends, <paramref name="onFinished"/> gets the
    /// result and owns what happens next (e.g. changing scene); without one the game returns to <paramref name="returnScene"/>.
    /// </summary>
    public static void Begin(SceneTree tree, ScavengeSim sim, int playerId = LocalPlayerId,
        Action<ExtractionResult>? onFinished = null, string? returnScene = null) =>
        Begin(tree, new LocalDriver(sim, playerId), onFinished, returnScene);

    /// <summary>Plays whatever <paramref name="driver"/> runs: a local sim, or a multiplayer session (<see cref="RemoteDriver"/>).</summary>
    public static void Begin(SceneTree tree, IScavengeDriver driver, Action<ExtractionResult>? onFinished = null, string? returnScene = null)
    {
        Pending = new ScavengeMission(driver, onFinished, returnScene ?? MenuScene);
        LastResult = null;
        tree.ChangeSceneToFile(ScenePath);
    }

    /// <summary>Builds the zone with Core's <see cref="NewScavenge"/> and plays it.</summary>
    public static void Begin(SceneTree tree, string zoneId, ulong seed, IReadOnlyList<PartyMember> party,
        Action<ExtractionResult>? onFinished = null, string? returnScene = null) =>
        Begin(tree, NewScavenge.Create(GameDataPaths.LoadScavenge(), zoneId, seed, [new PartySetup(LocalPlayerId, party.ToList())]),
            LocalPlayerId, onFinished, returnScene);

    /// <summary>A debug mission with <see cref="DebugParty"/>, returning to the main menu. --zone=ID / --seed=N pick another.</summary>
    public static void BeginDebug(SceneTree tree) => Begin(tree, DebugZoneArg, DebugSeedArg, DebugParty());

    private static string DebugZoneArg => DebugCapture.Value("--zone") is { Length: > 0 } z ? z : DebugZone;

    private static ulong DebugSeedArg => ulong.TryParse(DebugCapture.Value("--seed"), out var s) ? s : DebugSeed;

    /// <summary>The pending mission, or a fresh debug one when the scene was opened directly (editor, --scavenge).</summary>
    public static ScavengeMission TakeMission()
    {
        var mission = Pending ?? new ScavengeMission(new LocalDriver(
            NewScavenge.Create(GameDataPaths.LoadScavenge(), DebugZoneArg, DebugSeedArg, [new PartySetup(LocalPlayerId, DebugParty())]),
            LocalPlayerId), null, MenuScene);
        Pending = mission;
        return mission;
    }

    /// <summary>Called by the scavenge scene when the player leaves the summary screen.</summary>
    public static void End(SceneTree tree, ExtractionResult result)
    {
        var mission = Pending;
        Pending = null;
        LastResult = result;
        Finished?.Invoke(result);
        if (mission?.OnFinished is { } callback) callback(result);
        else tree.ChangeSceneToFile(mission?.ReturnScene ?? MenuScene);
    }

    /// <summary>
    /// The three adults of a fresh colony, geared so every movement mode and action shows up: a scout (rook), a brawler
    /// with a med-kit and a hook for a left arm, and a grappler (knight jump) with a cybernetic leg. Children never go
    /// on a run (Core rejects them), so the colony's children stay out of it.
    /// </summary>
    public static List<PartyMember> DebugParty(ulong colonySeed = 1)
    {
        var colony = NewGame.Create(GameDataPaths.Load(), colonySeed).State.Survivors;
        var adults = colony.Where(s => !s.IsChild).ToList();

        Dictionary<EquipSlot, string> Gear(string weapon, string? body = null, string? tool = null)
        {
            var gear = new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = weapon };
            if (body is not null) gear[EquipSlot.Body] = body;
            if (tool is not null) gear[EquipSlot.Tool] = tool;
            return gear;
        }

        var party = new List<PartyMember>();
        if (adults.Count > 0) party.Add(new PartyMember(adults[0], Gear("pistol", "scout_jacket", "binoculars"), ["bandage", "evac_beacon"]));
        if (adults.Count > 1)
        {
            adults[1].Body.Lose(BodyPart.LeftArm);
            adults[1].Body.FitProsthetic(BodyPart.LeftArm, 0.5, "hook_arm");
            party.Add(new PartyMember(adults[1], Gear("pipe", "padded_vest", "backpack"), ["med_kit", "bandage"]));
        }
        if (adults.Count > 2)
        {
            adults[2].Body.Lose(BodyPart.LeftLeg);
            adults[2].Body.FitProsthetic(BodyPart.LeftLeg, 1.1, "cyber_leg");
            party.Add(new PartyMember(adults[2], Gear("spear", "rag_coat", "grappling_hook"), ["bandage"]));
        }
        return party;
    }
}
