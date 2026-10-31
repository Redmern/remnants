using System.Text;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Story;

namespace Remnants.Playtest;

/// <summary>
/// Many runs of one story mission with a fresh colony's first party (two healthiest adults, starting gear, as
/// <see cref="ZoneTrial"/>), played by <see cref="StoryBot"/>: how often it closes, fails, and kills, by hour and weather.
/// The map is the mission's own (the perimeter variant for <c>townHall</c>); colony state (towers, retries) is left out.
/// </summary>
public static class MissionTrial
{
    public static string Run(GameData data, ScavengeData scavenge, StoryData story, string missionId, int runs, int? hour, string? weather,
        int townHall = 1, Action<string>? trace = null)
    {
        var def = story.Mission(missionId) ?? throw new ArgumentException($"Unknown mission '{missionId}'.");
        var h = hour ?? 12;
        var environment = ScavengeEnvironment.At(h, DayNight.PhaseAtHour(h, data.Rules.DayNight), weather);
        if (weather is not null && weather != WeatherRules.Clear) environment.Intensity = ZoneTrial.TrialIntensity;
        var conditions = new ZoneConditions(Environment: environment);
        var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int deaths = 0, deathRuns = 0, wipes = 0, rounds = 0;
        for (var seed = 1; seed <= runs; seed++)
        {
            var colony = NewGame.Create(data, (ulong)seed, scavenge);
            var adults = colony.State.Survivors.Where(s => !s.IsChild).Take(2).ToList();
            var weapons = new Queue<string>(new[] { "spear", "pipe", "pipe" }.Where(colony.State.Inventory.Has));
            var bodies = new Queue<string>(["padded_vest", "rag_coat"]);
            var packs = new List<List<string>> { new() { "med_kit", "bandage" }, new() { "bandage", "bandage" } };
            var party = adults.Select((s, i) => new PartyMember(s,
                new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = weapons.Dequeue(), [EquipSlot.Body] = bodies.Dequeue() }, packs[i])).ToList();
            var sim = NewScavenge.Create(scavenge, def.Zone!, (ulong)(seed * 7919), [new PartySetup(ColonySim.ExpeditionPlayerId, party)], conditions,
                Campaign.CoopSetup(story, missionId, townHall));
            var (result, r) = StoryBot.Play(sim, ColonySim.ExpeditionPlayerId, trace: trace);
            var outcome = result.Mission?.Outcome.ToString() ?? "?";
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            deaths += result.Deaths.Count;
            if (result.Deaths.Count > 0) deathRuns++;
            if (result.Returned.Count == 0) wipes++;
            rounds += r;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"{missionId} ({def.Name}) × {runs}, hour {h} ({environment.Phase}), weather {weather ?? "none"}: " +
                      string.Join(", ", outcomes.Select(kv => $"{kv.Key} {kv.Value}")) +
                      $"; deaths {deaths} ({deaths / (double)runs:0.00}/run), runs with a death {deathRuns}, wipes {wipes}, rounds {rounds / (double)runs:0.0}");
        return sb.ToString();
    }
}
