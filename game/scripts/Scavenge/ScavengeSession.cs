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
        // The colony's bestiary gates the enemy stats the run shows (empty for a run started without a colony).
        BestiaryKnowledge.Colony = (IReadOnlyDictionary<string, BestiaryEntry>?)Colony.ColonySession.Current?.Sim.State.Bestiary ?? new Dictionary<string, BestiaryEntry>();
        tree.ChangeSceneToFile(ScenePath);
    }

    /// <summary>Builds the zone with Core's <see cref="NewScavenge"/> and plays it.</summary>
    public static void Begin(SceneTree tree, string zoneId, ulong seed, IReadOnlyList<PartyMember> party,
        Action<ExtractionResult>? onFinished = null, string? returnScene = null) =>
        Begin(tree, NewScavenge.Create(GameDataPaths.LoadScavenge(), zoneId, seed, [new PartySetup(LocalPlayerId, party.ToList())]),
            LocalPlayerId, onFinished, returnScene);

    private static FieldVehicle? DebugVehicle()
    {
        var data = GameDataPaths.Load();
        if (DebugCapture.Value("--vehicle") is not { } tierId || !data.VehicleTiers.TryGetValue(tierId, out var tier)) return null;
        var vehicle = new Remnants.Core.Colony.Vehicle { TierId = tier.Id, Name = tier.Name };
        foreach (var u in new[] { "roof_turret", "winch", "cargo_rack", "turbo", "armor_plates" })
            if (data.VehicleUpgrades.TryGetValue(u, out var def) && tier.Tier >= def.MinTier
                && tier.Slots.GetValueOrDefault(def.Slot) > vehicle.Upgrades.Count(f => data.VehicleUpgrades[f.UpgradeId].Slot == def.Slot))
                vehicle.Upgrades.Add(Remnants.Core.Colony.FittedUpgrade.Standard(u));
        vehicle.Condition = Remnants.Core.Colony.VehicleRules.Stats(data, vehicle).MaxCondition * 0.8;
        return Remnants.Core.Colony.VehicleRules.Field(data, vehicle);
    }

    /// <summary>The starter squad deck with the base deck rules (a debug run plays with cards like a colony's first).</summary>
    private static SquadLoadout? DebugDeck()
    {
        var cards = GameDataPaths.LoadScavenge().Cards;
        return cards.All.Count == 0 ? null : new SquadLoadout(cards.StarterDeck(), cards.Rules.HandSize, cards.Rules.Draw, cards.Rules.Budget);
    }

    /// <summary>A debug mission with <see cref="DebugParty"/>, returning to the main menu. --zone=ID / --seed=N pick another.</summary>
    public static void BeginDebug(SceneTree tree) => Begin(tree, DebugSim());

    private static string DebugZoneArg => DebugCapture.Value("--zone") is { Length: > 0 } z ? z : DebugZone;

    private static ulong DebugSeedArg => ulong.TryParse(DebugCapture.Value("--seed"), out var s) ? s : DebugSeed;

    /// <summary>The pending mission, or a fresh debug one when the scene was opened directly (editor, --scavenge).</summary>
    public static ScavengeMission TakeMission()
    {
        var mission = Pending ?? new ScavengeMission(new LocalDriver(DebugSim(), LocalPlayerId), null, MenuScene);
        Pending = mission;
        return mission;
    }

    /// <summary>
    /// The debug mission's sim; <c>--fog-demo</c> stages a hidden Linked near the party (<see cref="Fog.FogDemo"/>),
    /// <c>--hour=H</c> plays it at that hour (its day phase by the colony's hours), <c>--weather=KIND[:INTENSITY]</c> in that weather,
    /// <c>--vehicle=TIER</c> parks a vehicle of that tier (with a roof turret, winch, cargo rack, turbo and plates where they fit)
    /// at the party's spawn point.
    /// </summary>
    private static ScavengeSim DebugSim()
    {
        if (DebugCapture.Value("--story-mission") is { Length: > 0 } missionId) return StoryMissionSim(missionId);
        ZoneConditions? conditions = null;
        if (int.TryParse(DebugCapture.Value("--hour"), out var hour))
            conditions = new ZoneConditions(Environment: ScavengeEnvironment.At(hour, Remnants.Core.Colony.DayNight.PhaseAtHour(hour, GameDataPaths.Load().Rules.DayNight)));
        if (DebugCapture.Value("--weather") is { Length: > 0 } weather)
        {
            var parts = weather.Split(':');
            var env = conditions?.Environment ?? new ScavengeEnvironment();
            env.Weather = parts[0];
            env.Intensity = parts.Length > 1 && double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : 0.85;
            conditions = new ZoneConditions(Environment: env);
        }
        // The late-zone tuning: --late-zones=current|expeditions|everywhere, else the setting.
        var late = Enum.TryParse<LateZoneTuning>(DebugCapture.Value("--late-zones"), ignoreCase: true, out var flag) ? flag : Hud.GameSettings.LateZones;
        conditions = (conditions ?? new ZoneConditions()) with { LateZones = late };
        var sim = NewScavenge.Create(GameDataPaths.LoadScavenge(), DebugZoneArg, DebugSeedArg,
            [new PartySetup(LocalPlayerId, DebugParty()) { Vehicle = DebugVehicle(), Deck = DebugDeck() }], conditions);
        if (Fog.FogDemo.Requested) Fog.FogDemo.Stage(sim, LocalPlayerId);
        if (TechDemo.Requested) TechDemo.Stage(sim, LocalPlayerId);
        if (LootDemo.Requested) LootDemo.Stage(sim, LocalPlayerId);
        if (AnimDemo.Requested) AnimDemo.Stage(sim, LocalPlayerId);
        if (GearDemo.Requested) GearDemo.Stage(sim, LocalPlayerId);
        if (TacticsDemo.Requested) TacticsDemo.Stage(sim, LocalPlayerId);
        return sim;
    }

    /// <summary>
    /// Debug: <c>--story-mission=ID</c> plays a story mission's field script with the debug party (four for the perimeter
    /// defense; <c>--th=N</c> picks the perimeter variant, two watchtowers built).
    /// </summary>
    private static ScavengeSim StoryMissionSim(string missionId)
    {
        var story = GameDataPaths.LoadStory();
        var def = story.Mission(missionId) ?? throw new ArgumentException($"No story mission '{missionId}'.");
        var th = int.TryParse(DebugCapture.Value("--th"), out var level) ? level : 2;
        var setup = new MissionSetup(def.Field!, story.MapFor(def, th), def.Room is { } room ? story.Maps[room] : null, Towers: 2);
        var party = DebugParty();
        if (def.MaxParty is > 3)
        {
            var extra = DebugParty(colonySeed: 2)[0];
            extra.Survivor.Id = 99;
            party.Add(extra);
        }
        var sim = NewScavenge.Create(GameDataPaths.LoadScavenge(), def.Zone!, DebugSeedArg, [new PartySetup(LocalPlayerId, party)], null, setup);
        // --rounds=N: everyone holds until round N (the wave is in sight), for screenshots.
        if (int.TryParse(DebugCapture.Value("--rounds"), out var rounds))
            for (var i = 0; i < 200 && sim.State.Round < rounds && sim.State.CurrentUnit is { } u && !sim.State.Finished; i++)
                sim.Execute(sim.State.Mission?.Pending is { } p ? new ChooseOption(u.PlayerId!.Value, u.Id, p.Id, p.Options[0].Id)
                    : u.Ap >= 1 && i % 2 == 0 ? new SetStance(u.PlayerId!.Value, u.Id, Stance.Overwatch) : new EndTurn(u.PlayerId!.Value, u.Id));
        return sim;
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
