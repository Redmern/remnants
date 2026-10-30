using System.Text.Json;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;

namespace Remnants.Core.Story;

/// <summary>
/// The story's definitions (data/story/): characters, missions in campaign order, dialogue graphs and handmade maps.
/// Loaded next to the game and scavenge data; a colony without it plays as Sandbox.
/// </summary>
public sealed class StoryData
{
    public IReadOnlyDictionary<string, CharacterDef> Characters { get; }

    /// <summary>In campaign order (file name order, then position in the file): unlocks are checked in this order.</summary>
    public IReadOnlyList<MissionDef> Missions { get; }
    public IReadOnlyDictionary<string, DialogueDef> Dialogues { get; }
    public IReadOnlyDictionary<string, HandmadeMapDef> Maps { get; }

    /// <summary>Unique survivors' traits (traits.json).</summary>
    public IReadOnlyDictionary<string, TraitDef> Traits { get; }

    /// <summary>Codex pages in display order (codex.json).</summary>
    public IReadOnlyList<CodexEntryDef> Codex { get; }

    /// <summary>The endings, their epilogues and world effects (endings.json).</summary>
    public IReadOnlyDictionary<string, EndingDef> Endings { get; }

    public StoryData(IEnumerable<CharacterDef> characters, IEnumerable<MissionDef> missions, IEnumerable<DialogueDef> dialogues, IEnumerable<HandmadeMapDef> maps,
        IEnumerable<TraitDef>? traits = null, IEnumerable<CodexEntryDef>? codex = null, IEnumerable<EndingDef>? endings = null)
    {
        Characters = Index(characters, c => c.Id, "character");
        Missions = missions.ToList();
        Index(Missions, m => m.Id, "mission");
        Dialogues = Index(dialogues, d => d.Id, "dialogue");
        Maps = Index(maps, m => m.Id, "map");
        Traits = Index(traits ?? [], t => t.Id, "trait");
        Codex = (codex ?? []).ToList();
        Index(Codex, c => c.Id, "codex entry");
        Endings = Index(endings ?? [], e => e.Id, "ending");
    }

    public MissionDef? Mission(string id) => Missions.FirstOrDefault(m => m.Id == id);

    /// <summary>The handmade map a field mission is played on, for this Town Hall level (perimeter variants: perimeter_th1..3).</summary>
    public HandmadeMapDef? MapFor(MissionDef mission, int townHallLevel) =>
        mission.Perimeter ? Maps.GetValueOrDefault($"perimeter_th{Math.Clamp(townHallLevel, 1, 3)}")
        : mission.HandmadeId is { } id ? Maps.GetValueOrDefault(id) : null;

    private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> id, string what)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
            if (!map.TryAdd(id(item), item)) throw new GameDataException($"Duplicate {what} id '{id(item)}'.");
        return map;
    }

    /// <summary>
    /// Every cross-reference problem: dialogue dead ends (a <c>next</c> that does not exist), effects naming unknown
    /// flags' targets (items, resources, zones, dialogues, characters, missions), missions naming unknown maps, rooms,
    /// zones, buildings or dialogues, and invalid maps. Empty = valid. Schematic ids are checked by the caller when a
    /// schematic catalogue exists.
    /// </summary>
    public List<string> Validate(GameData game, ScavengeData scavenge)
    {
        var errors = new List<string>();
        var missionIds = Missions.Select(m => m.Id).ToHashSet();
        void Effects(string where, IEnumerable<EffectDef> effects)
        {
            foreach (var e in effects)
            {
                var bad = e.Kind switch
                {
                    EffectKind.Item => !scavenge.Items.ContainsKey(e.Id),
                    EffectKind.Resource => e.Id != Colony.Pantry.FoodKey && !game.Resources.ContainsKey(e.Id),
                    EffectKind.RevealZone => !scavenge.Zones.ContainsKey(e.Id),
                    EffectKind.Call => !Dialogues.ContainsKey(e.Id),
                    EffectKind.Mood or EffectKind.Humanity or EffectKind.Leaves => e.Id != "key" && !Characters.ContainsKey(e.Id),
                    EffectKind.Character or EffectKind.Dies or EffectKind.Rename => !Characters.ContainsKey(e.Id),
                    EffectKind.Close => !missionIds.Contains(e.Id),
                    EffectKind.Ending => !Endings.ContainsKey(e.Id),
                    EffectKind.WorldEffect or EffectKind.EndWorldEffect => !StoryWorld.Known.Contains(e.Id),
                    EffectKind.Schematic or EffectKind.Fallback => !e.Id.StartsWith("sch_", StringComparison.Ordinal) && e.Kind == EffectKind.Schematic,
                    _ => false,
                };
                if (bad) errors.Add($"{where}: {e.Kind} names unknown '{e.Id}'.");
                if (e.When is { } w) Condition(where, w);
            }
        }
        void Condition(string where, ConditionDef c)
        {
            if (c.Mission is { } m && !missionIds.Contains(m)) errors.Add($"{where}: requires unknown mission '{m}'.");
            if (c.Building is { } b && !game.Buildings.ContainsKey(b)) errors.Add($"{where}: requires unknown building '{b}'.");
            if (c.Item is { } i && !scavenge.Items.ContainsKey(i)) errors.Add($"{where}: requires unknown item '{i}'.");
            foreach (var who in new[] { c.Alive, c.Dead, c.Joined, c.Adult, c.Of }.OfType<string>().Where(w => w != "key" && !Characters.ContainsKey(w)))
                errors.Add($"{where}: requires unknown character '{who}'.");
            if (c.Skill is { } sk && !Enum.TryParse<Survivors.Skill>(sk, out _)) errors.Add($"{where}: requires unknown skill '{sk}'.");
            foreach (var any in c.Any ?? []) Condition(where, any);
            if (c.Not is { } not) Condition(where, not);
        }

        foreach (var d in Dialogues.Values)
        {
            if (d.Nodes.Count == 0) errors.Add($"Dialogue {d.Id}: no lines.");
            if (d.Caller.Length > 0 && !Characters.ContainsKey(d.Caller)) errors.Add($"Dialogue {d.Id}: unknown caller '{d.Caller}'.");
            var ids = d.Nodes.Select(n => n.Id).ToList();
            if (ids.Distinct().Count() != ids.Count) errors.Add($"Dialogue {d.Id}: duplicate line ids.");
            foreach (var n in d.Nodes)
            {
                var where = $"Dialogue {d.Id}/{n.Id}";
                if (!IsSpeaker(n.Speaker)) errors.Add($"{where}: unknown speaker '{n.Speaker}'.");
                if (n.Next is { } next && !ids.Contains(next)) errors.Add($"{where}: next '{next}' does not exist.");
                if (n.Choices.Count > 3) errors.Add($"{where}: more than three choices.");
                if (n.Choices.Count > 0 && n.Next is not null) errors.Add($"{where}: a line with choices has no next of its own.");
                Effects(where, n.Effects);
                if (n.When is { } nw) Condition(where, nw);
                if (n.When is not null && (n.Choices.Count > 0 || d.Nodes[0] == n)) errors.Add($"{where}: only a plain line after the first may be conditional.");
                foreach (var c in n.Choices)
                {
                    if (c.Next is { } cn && !ids.Contains(cn)) errors.Add($"{where}: choice next '{cn}' does not exist.");
                    Effects(where, c.Effects);
                    foreach (var r in c.Requires) Condition(where, r);
                }
                // Every choice line keeps at least one choice open, whatever the flags (no dead ends).
                if (n.Choices.Count > 0 && n.Choices.All(c => c.Requires.Count > 0)) errors.Add($"{where}: every choice is conditional.");
            }
        }

        foreach (var m in Missions)
        {
            var where = $"Mission {m.Id}";
            foreach (var r in m.Requires) Condition(where, r);
            Effects(where, m.Rewards);
            Effects(where, m.Fail.Effects);
            foreach (var id in new[] { m.Briefing, m.Debrief, m.Fail.Call }.OfType<string>())
                if (!Dialogues.ContainsKey(id)) errors.Add($"{where}: unknown dialogue '{id}'.");
            if (m.Type == MissionType.Colony)
            {
                foreach (var o in m.ColonyObjectives)
                {
                    if (o.Building is { } b && !game.Buildings.ContainsKey(b)) errors.Add($"{where}: objective {o.Id} names unknown building '{b}'.");
                    if (o.Dialogue is { } dl && !Dialogues.ContainsKey(dl)) errors.Add($"{where}: objective {o.Id} names unknown dialogue '{dl}'.");
                    if (o.Item is { } it && !scavenge.Items.ContainsKey(it)) errors.Add($"{where}: objective {o.Id} names unknown item '{it}'.");
                    if (o.Resource is { } rs && rs != Colony.Pantry.FoodKey && !game.Resources.ContainsKey(rs)) errors.Add($"{where}: objective {o.Id} names unknown resource '{rs}'.");
                }
                continue;
            }
            if (m.Field is not { } field) { errors.Add($"{where}: a field mission needs a field script."); continue; }
            if (m.Zone is null || !scavenge.Zones.ContainsKey(m.Zone)) errors.Add($"{where}: unknown zone '{m.Zone}'.");
            if (m.Perimeter)
            {
                for (var th = 1; th <= 3; th++)
                    if (!Maps.ContainsKey($"perimeter_th{th}")) errors.Add($"{where}: no perimeter_th{th} map.");
            }
            else if (m.HandmadeId is { } hid && !Maps.ContainsKey(hid)) errors.Add($"{where}: unknown map '{hid}'.");
            if (m.Room is { } room && (!Maps.TryGetValue(room, out var rd) || !rd.Room)) errors.Add($"{where}: unknown room '{room}'.");
            var objectiveIds = field.Objectives.Select(o => o.Id).ToHashSet();
            foreach (var o in field.Objectives.Where(o => o.Item is not null && !scavenge.Items.ContainsKey(o.Item)))
                errors.Add($"{where}: objective {o.Id} names unknown item '{o.Item}'.");
            foreach (var t in field.Triggers)
            {
                if (t.When.Objective is { } to && !objectiveIds.Contains(to)) errors.Add($"{where}: trigger {t.Id} watches unknown objective '{to}'.");
                foreach (var a in t.Actions)
                {
                    if (a.Enemy is { } e && !scavenge.Enemies.ContainsKey(e)) errors.Add($"{where}: trigger {t.Id} spawns unknown enemy '{e}'.");
                    if (a.Objective is { } ao && !objectiveIds.Contains(ao)) errors.Add($"{where}: trigger {t.Id} names unknown objective '{ao}'.");
                }
            }
        }

        foreach (var m in Missions)
        {
            if (m.ChainFrom is { } from && !missionIds.Contains(from)) errors.Add($"Mission {m.Id}: chained from unknown mission '{from}'.");
            foreach (var who in m.MustBring.Where(w => w != "key" && !Characters.ContainsKey(w))) errors.Add($"Mission {m.Id}: must bring unknown character '{who}'.");
            foreach (var t in m.Field?.Triggers ?? [])
                if (t.When.Kind is TriggerKind.After && t.When.Objective is null) errors.Add($"Mission {m.Id}: trigger {t.Id} waits after no objective.");
        }
        foreach (var c in Characters.Values)
        {
            if (c.Successor is { } heir && !Characters.ContainsKey(heir)) errors.Add($"Character {c.Id}: unknown successor '{heir}'.");
            if (c.Survivor is not { } u) continue;
            foreach (var t in u.Traits.Where(t => !Traits.ContainsKey(t))) errors.Add($"Character {c.Id}: unknown trait '{t}'.");
            foreach (var p in u.Perks.Where(p => !game.Perks.ContainsKey(p))) errors.Add($"Character {c.Id}: unknown perk '{p}'.");
            foreach (var a in u.Attributes.Keys.Where(a => !Enum.TryParse<Survivors.AttributeKind>(a, out _))) errors.Add($"Character {c.Id}: unknown attribute '{a}'.");
            foreach (var k in u.Skills.Keys.Where(k => !Enum.TryParse<Survivors.Skill>(k, out _))) errors.Add($"Character {c.Id}: unknown skill '{k}'.");
        }
        foreach (var t in Traits.Values)
            foreach (var h in t.Hooks.Keys.Where(h => !Colony.PerkHooks.All.ContainsKey(h))) errors.Add($"Trait {t.Id}: unknown hook '{h}'.");
        foreach (var e in Codex)
        {
            if (e.When is { } w) Condition($"Codex {e.Id}", w);
            foreach (var l in e.Lines.Where(l => l.When is not null)) Condition($"Codex {e.Id}", l.When!);
        }
        foreach (var e in Endings.Values)
        {
            foreach (var r in e.Requires) Condition($"Ending {e.Id}", r);
            foreach (var w in e.WorldEffects.Where(w => !StoryWorld.Known.Contains(w))) errors.Add($"Ending {e.Id}: unknown world effect '{w}'.");
            foreach (var v in e.Slides.SelectMany(s => s.Variants).Where(v => v.When is not null)) Condition($"Ending {e.Id}", v.When!);
        }

        foreach (var map in Maps.Values) errors.AddRange(HandmadeMaps.Validate(scavenge, map));
        return errors;
    }

    /// <summary>Speakers a line may have: a character, the colony's lead survivor, Pell, or narration.</summary>
    public bool IsSpeaker(string id) => id is "player" or "narration" || Characters.ContainsKey(id);
}

/// <summary>Loads data/story/: characters.json, missions/*.json, dialogue/*.json, maps/*.json (each file sorted by name).</summary>
public static class StoryDataLoader
{
    public static StoryData LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory)) throw new GameDataException($"Missing story data folder '{directory}'.");
        var characters = Parse<List<CharacterDef>>(File.ReadAllText(Path.Combine(directory, "characters.json")), "story/characters.json");
        IEnumerable<T> All<T>(string folder, Func<string, string, IEnumerable<T>> parse)
        {
            var path = Path.Combine(directory, folder);
            if (!Directory.Exists(path)) return [];
            return Directory.GetFiles(path, "*.json").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                .SelectMany(f => parse(File.ReadAllText(f), $"story/{folder}/{Path.GetFileName(f)}")).ToList();
        }
        List<T> Optional<T>(string file) => File.Exists(Path.Combine(directory, file))
            ? Parse<List<T>>(File.ReadAllText(Path.Combine(directory, file)), $"story/{file}") : [];
        return new StoryData(
            characters,
            All("missions", (json, name) => Parse<List<MissionDef>>(json, name)),
            All("dialogue", (json, name) => Parse<List<DialogueDef>>(json, name)),
            All<HandmadeMapDef>("maps", (json, name) => [HandmadeMaps.Parse(json, name)]),
            Optional<TraitDef>("traits.json"), Optional<CodexEntryDef>("codex.json"), Optional<EndingDef>("endings.json"));
    }

    /// <summary>From file contents (an exported game's embedded copy): each list is (file name, json) in load order.</summary>
    /// <param name="traits">traits.json, codex.json, endings.json: optional (null = none).</param>
    public static StoryData LoadFromJson(string characters, IEnumerable<(string Name, string Json)> missions,
        IEnumerable<(string Name, string Json)> dialogues, IEnumerable<(string Name, string Json)> maps,
        string? traits = null, string? codex = null, string? endings = null) =>
        new(Parse<List<CharacterDef>>(characters, "story/characters.json"),
            missions.OrderBy(f => f.Name, StringComparer.Ordinal).SelectMany(f => Parse<List<MissionDef>>(f.Json, f.Name)).ToList(),
            dialogues.OrderBy(f => f.Name, StringComparer.Ordinal).SelectMany(f => Parse<List<DialogueDef>>(f.Json, f.Name)).ToList(),
            maps.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => HandmadeMaps.Parse(f.Json, f.Name)).ToList(),
            traits is null ? null : Parse<List<TraitDef>>(traits, "story/traits.json"),
            codex is null ? null : Parse<List<CodexEntryDef>>(codex, "story/codex.json"),
            endings is null ? null : Parse<List<EndingDef>>(endings, "story/endings.json"));

    private static T Parse<T>(string json, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json.Options) ?? throw new GameDataException($"{name} is empty.");
        }
        catch (JsonException e)
        {
            throw new GameDataException($"{name} is invalid: {e.Message}");
        }
    }
}
