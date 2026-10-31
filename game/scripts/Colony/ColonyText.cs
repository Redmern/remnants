using System.Collections.Generic;
using System.Linq;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony;

/// <summary>View-model formatting of Core state for labels. Read-only; no rules.</summary>
public static class ColonyText
{
    public static string Cost(IReadOnlyDictionary<string, double> cost, GameData data) =>
        cost.Count == 0 ? "free" : string.Join(", ", cost.Select(kv => $"{kv.Value:0} {data.Resources[kv.Key].Name}"));

    public static string Clock(ColonySim sim)
    {
        var rules = sim.Data.Rules;
        var day = sim.State.Tick / rules.TicksPerDay + 1;
        var hour = sim.State.Tick % rules.TicksPerDay;
        return $"Day {day}, {hour:00}:00";
    }

    /// <summary>The top bar's clock tooltip: the phase, the colony's hours and who is working or asleep now.</summary>
    public static string PhaseTip(ColonySim sim)
    {
        var r = sim.Data.Rules.DayNight;
        var phase = sim.Phase;
        var next = (DayPhase)(((int)phase + 1) % 4);
        var lines = new List<string>
        {
            $"{phase} until {DayNight.StartHour(next, r):00}:00.",
            $"Day shift {r.WorkHours[0]:00}–{r.WorkHours[1]:00}. Adults sleep {r.AdultSleep[0]:00}–{r.AdultSleep[1]:00}, children {r.ChildSleep[0]:00}–{r.ChildSleep[1]:00}.",
        };
        if (!r.Enabled) return string.Join("\n", lines);
        var home = sim.State.Present.ToList();
        lines.Add($"Now: {home.Count(sim.OnShift)} working, {home.Count(sim.IsAsleep)} asleep.");
        var shifts = sim.State.Buildings.Where(b => b.NightShift).Select(b => sim.Data.Buildings[b.DefId].Name).ToList();
        if (shifts.Count > 0) lines.Add($"Night shift: {string.Join(", ", shifts)}.");
        if (home.Count(sim.IsTired) is var tired and > 0) lines.Add($"{tired} tired after a night's work.");
        return string.Join("\n", lines);
    }

    /// <summary>A weather kind's name as the colony says it ("Heavy rain" from 0.75 up).</summary>
    public static string WeatherName(ColonySim sim, string kind, double intensity) =>
        kind == "rain" && intensity >= 0.75 ? "Heavy rain" : sim.Data.Weather.Kind(kind).Name;

    /// <summary>What a kind of weather does at home and on the road, one line each.</summary>
    public static List<string> WeatherEffects(ColonySim sim, string kind)
    {
        var def = sim.Data.Weather.Kind(kind);
        var c = def.Colony;
        var lines = new List<string>();
        string Pct(double m) => $"{(m >= 1 ? "+" : "−")}{System.Math.Abs(System.Math.Round((m - 1) * 100)):0}%";
        if (c.MaskedOutput is { } masked)
            lines.Add($"Outdoor work {Pct(c.OutdoorOutput)} without a dust mask ({Pct(masked)} with one); the unmasked lose health.");
        else if (c.OutdoorOutput < 1) lines.Add($"Outdoor work {Pct(c.OutdoorOutput)}.");
        if (System.Math.Abs(c.CropOutput - 1) > 1e-9) lines.Add($"Crops {Pct(c.CropOutput)}.");
        if (c.ChildrenStayIn) lines.Add("Children stay indoors.");
        if (c.MoodPerTick > 0) lines.Add("Spirits sink.");
        if (c.NoCampfire) lines.Add("No campfire in the evening.");
        if (c.LightningChancePerTick > 0) lines.Add("Lightning may strike a building.");
        if (def.Travel.ExtraDays > 0) lines.Add($"Parties setting out take +{def.Travel.ExtraDays:0.#} days.");
        if (def.Travel.BlocksFromDays is { } d) lines.Add($"Routes {d:0.#}+ days out are closed while it rages over them.");
        if (def.GroundsDrones) lines.Add("Scout drones stay grounded.");
        else if (def.GroundsDronesFrom is { } from) lines.Add($"Scout drones stay grounded when it is heavy.");
        return lines;
    }

    /// <summary>The weather chip's tooltip: now, what it does, and the forecast as far as the colony sees.</summary>
    public static string WeatherTip(ColonySim sim)
    {
        var state = sim.State;
        var rules = sim.Data.Rules;
        var now = Weather.Current(state, sim.Data);
        var lines = new List<string> { $"{WeatherName(sim, now.Kind, now.Intensity)} for about {System.Math.Max(1, now.End - state.Tick)} h more." };
        lines.AddRange(WeatherEffects(sim, now.Kind));
        var masks = Weather.Masks(state, sim.Data);
        if (sim.Data.Weather.Kind(now.Kind).Colony.MaskedOutput is not null)
            lines.Add($"Dust masks in the stores: {masks} for {Weather.OutdoorCrew(state, sim.Data).Count()} outdoor workers.");
        var forecast = Weather.Forecast(state, sim.Data);
        if (forecast.Days <= 0) return string.Join("\n", lines);
        lines.Add("");
        lines.Add($"Forecast, {forecast.Days} day{(forecast.Days == 1 ? "" : "s")} ahead (next call {forecast.Accuracy * 100:0}% sure):");
        foreach (var e in forecast.Entries.Where(e => !e.Current))
            lines.Add($"  Day {DayNight.Day(e.Start, rules)} {DayNight.Hour(e.Start, rules):00}:00  {WeatherName(sim, e.Called, e.Intensity)}");
        lines.Add("A bigger Town Hall and an Electronics Lab see further; a scanner in the stores reads the sky better.");
        return string.Join("\n", lines);
    }

    /// <summary>What a Night Shift does here, for the building panel (or why it cannot run).</summary>
    public static string NightShiftTip(ColonySim sim, Building b)
    {
        var r = sim.Data.Rules.DayNight;
        var def = sim.Data.Buildings[b.DefId];
        var cut = (int)System.Math.Round((1 - r.NightShift.OutputMultiplier) * 100);
        var tip = $"Adults keep working after {r.WorkHours[1]:00}:00 until {r.WorkHours[0]:00}:00 at −{cut}% output. " +
                  $"They lose sleep: mood falls while they work and they wake tired (−{(int)System.Math.Round((1 - r.Tired.OutputMultiplier) * 100)}% output until they sleep it off). Children never work nights.";
        if (!sim.CanRunNightShift(b))
            tip += $"\n{def.Name} works outdoors: it needs light — a Floodlight or level {r.LitMinLevel}.";
        return tip;
    }

    /// <summary>A trip's length: hours under a day, else days ("16 h", "1.3 days").</summary>
    public static string Trip(double days) => days <= 0 ? "no time" : days < 1 ? $"{days * 24:0} h" : $"{days:0.#} day{(Math.Abs(days - 1) < 0.05 ? "" : "s")}";

    public static string Travel(double days) => days <= 0 ? "on the doorstep" : days == 1 ? "1 day away" : $"{days:0.#} days away";

    public static Godot.Color ThreatColor(int threat) => threat switch
    {
        <= 1 => Hud.Palette.Good,
        2 => Hud.Palette.Warn,
        3 => Hud.Palette.Danger,
        _ => new Godot.Color(0.85f, 0.35f, 0.75f),
    };

    public static string Presence(LinkedPresence presence) => presence switch
    {
        LinkedPresence.None => "no Linked seen",
        LinkedPresence.Rare => "the odd Linked",
        LinkedPresence.Some => "Linked squads",
        _ => "dense Linked presence",
    };

    /// <summary>
    /// A zone as the colony knows it (BBCode): a rumor gives distance and a hint; a scouted zone everything —
    /// threat, reward tier, Linked presence, enemies, caches, how picked-over it is and the rations a member needs.
    /// </summary>
    public static string ZoneSummary(ColonySim sim, ZoneInfo info)
    {
        var zone = info.Zone;
        var travel = $"{Travel(zone.TravelDays)} · rations {info.RationsPerMember:0.#} food per member";
        if (info.Intel != ZoneIntel.Scouted)
            return $"[color=#aaa]Rumored[/color] — {zone.Region}\n{travel}\n[i]{zone.Description}[/i]\nNot scouted: threat and rewards unknown until a party has been there.";

        var data = sim.Scavenge!;
        var mix = string.Join(", ", zone.EnemyMix.Select(e => data.Enemies[e.Id].Name));
        var threat = ThreatColor(zone.Threat).ToHtml(false);
        var picked = info.Depletion > 0.01 ? $" · [color=#e0a070]picked over {info.Depletion:P0}[/color]" : "";
        var pylonCount = zone.Pylons[0] == zone.Pylons[1] ? $"{zone.Pylons[0]}" : $"{zone.Pylons[0]}–{zone.Pylons[1]}";
        var pylons = zone.Pylons[1] > 0 ? $" · {pylonCount} pylon{(zone.Pylons[1] > 1 ? "s" : "")} (reinforcements)" : "";
        return $"[color=#{threat}]Threat {zone.Threat}[/color] · reward: loot tier {zone.LootTier}, {zone.Caches[0]}–{zone.Caches[1]} caches{picked}\n" +
               $"{Presence(info.Linked)}{pylons}\n" +
               $"{zone.Enemies[0]}–{zone.Enemies[1]} enemies ({mix}) · {zone.Npcs[0]}–{zone.Npcs[1]} strangers\n{travel}";
    }

    public static int AgeYears(Survivor s, RulesDef rules) => (int)(s.AgeTicks / rules.TicksPerYear);

    public static string Job(ColonySim sim, Survivor s) =>
        s.Away ? "Away on expedition"
        : s.IsInfant ? "Baby"
        : s.PatientAtBuildingId is not null ? "Patient"
        : sim.ClassOf(s) is { } study ? (study.StudentId == s.Id ? "In class: " : "Teaching: ")
            + (study.Skill is { } k ? sim.Data.SkillInfo(k).Name : sim.Data.Perks[study.PerkId!].Name)
        : sim.State.Schematics.Research is { } r && r.ResearcherId == s.Id
            ? $"Researching: {sim.Data.Schematics.Find(r.SchematicId)?.Name ?? r.SchematicId}"
        : s.AssignedBuildingId is { } id && sim.State.FindBuilding(id) is { } b ? sim.Data.Buildings[b.DefId].Name
        : sim.State.Construction.SiteOf(s.Id) is { } site
            ? (site.Builders.Contains(s.Id) ? "Building: " : "Helping build: ") + sim.Data.Buildings[site.DefId].Name
        : "Idle";

    public static string BuildingName(ColonySim sim, Building b) => $"{sim.Data.Buildings[b.DefId].Name} L{b.Level}";

    /// <summary>One line per non-healthy body part, or "Healthy". Prosthetics are named when item data is given.</summary>
    public static string Body(Body body, ScavengeData? items = null)
    {
        var hurt = body.Parts
            .Where(kv => kv.Value.Condition != PartCondition.Healthy)
            .Select(kv => kv.Value.Condition switch
            {
                PartCondition.Injured => $"{kv.Key}: injured ({Percent(kv.Value.Severity)})",
                PartCondition.Prosthetic => kv.Value.ProstheticItemId is { } id && items?.Items.GetValueOrDefault(id) is { } item
                    ? $"{kv.Key}: {item.Name} ({Percent(kv.Value.ProstheticQuality)})"
                    : $"{kv.Key}: prosthetic ({Percent(kv.Value.ProstheticQuality)})",
                _ => $"{kv.Key}: {kv.Value.Condition.ToString().ToLowerInvariant()}",
            })
            .ToList();
        return hurt.Count == 0 ? "Healthy" : string.Join("\n", hurt);
    }

    public static string Percent(double fraction) => $"{System.Math.Round(fraction * 100):0}%";

    /// <summary>Hours as "5h" or "2d 3h".</summary>
    public static string Duration(int ticks, RulesDef rules) =>
        ticks < rules.TicksPerDay ? $"{ticks}h" : $"{ticks / rules.TicksPerDay}d {ticks % rules.TicksPerDay}h";

    /// <summary>A field profile in one line: primary move (and extra modes), AP, initiative, sight, load and attacks.</summary>
    public static string Profile(UnitProfile p)
    {
        var moves = string.Join(" / ", p.Movement.Modes.Select(m => $"{m.Pattern} {m.Range}" + (m.ApCost > 1 ? $" ({m.ApCost} AP)" : "")));
        var attacks = string.Join(", ", p.Actions.Select(a => $"{a.Name} {a.MinDamage:0}–{a.MaxDamage:0}"));
        return $"Moves {moves} · AP {p.MaxAp} · init {p.Initiative:0} · sight {p.Sight:0.#} · load {p.Load:0.#}/{p.CarryCapacity:0} · armor {p.Armor:0.#} · {attacks}";
    }

    /// <summary>A living survivor's name, a dead one's from the memorial ("Ada †"), else null (left the colony).</summary>
    public static string? PersonName(ColonyState state, int? id) =>
        id is not { } v ? null
        : state.FindSurvivor(v)?.Name ?? (state.Memorial.Find(m => m.SurvivorId == v) is { } dead ? $"{dead.Name} †" : null);

    /// <summary>"Infant (40% to child)", "Child (75% to adult)" or "Adult": a bare percentage next to a name reads as health.</summary>
    public static string Stage(Survivor s, RulesDef rules) =>
        s.IsChild ? $"{s.LifeStage} ({Percent(Bonds.StageProgress(s, rules.Life))} to {(s.IsInfant ? "child" : "adult")})" : "Adult";

    /// <summary>"child · adult in 2d 4h": the roster's short growth note for a minor.</summary>
    public static string GrowthNote(Survivor s, RulesDef rules) =>
        $"{s.LifeStage.ToString().ToLowerInvariant()} · {(s.IsInfant ? "child" : "adult")} in "
        + (Bonds.TicksToNextStage(s, rules.Life) is { } t ? Duration((int)t, rules) : "—");

    public static string Mood(Survivor s) =>
        s.Mood >= 80 ? "content" : s.Mood >= 55 ? "low" : s.Mood >= 30 ? "grieving" : "despairing";

        public static string Needs(Survivor s, RulesDef rules)
    {
        var parts = new List<string>();
        if (s.StarvingTicks > 0) parts.Add($"starving {s.StarvingTicks}h");
        if (!s.HasBed) parts.Add($"homeless {s.HomelessTicks}/{rules.Housing.HomelessGraceTicks}h");
        return parts.Count == 0 ? "fed, housed" : string.Join(", ", parts);
    }
}
