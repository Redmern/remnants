using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>A suggested next step for the player: a stable id (so the UI shows each tip once) and the text.</summary>
public sealed record Tip(string Id, string Text);

/// <summary>
/// First-time guidance: the most useful next step for a colony in this state, or null when nothing stands out.
/// Pure (reads state, changes nothing), in priority order: food, wood and stone, the homeless, idle hands, untreated
/// wounds (build a Med Bay, then admit), the first expedition, a Workshop, full beds, the Town Hall.
/// </summary>
public static class Advice
{
    public static Tip? Next(ColonySim sim)
    {
        var state = sim.State;
        var data = sim.Data;
        string Name(string id) => data.Buildings.TryGetValue(id, out var d) ? d.Name : id;
        bool Has(string id) => state.Buildings.Any(b => b.DefId == id);
        var home = state.Survivors.Where(s => !s.Away).ToList();
        var days = sim.FoodDays;

        if (!Has("farm"))
            return new("farm", $"Food lasts about {days:0} days. Build a {Name("farm")} first and put people on it; children can farm too.");
        if (!Has("woodcutter") || !Has("quarry"))
            return new("gather", $"Wood and stone pay for every building: build a {Name("woodcutter")} and a {Name("quarry")} and staff them.");
        if (home.FirstOrDefault(s => s.HomelessTicks > 0) is { } homeless)
            return new("homeless", $"{homeless.Name} has no bed and will leave in a few days: upgrade the {Name("shelter")} or build more housing.");

        var idle = home.Where(s => !s.IsInfant && s.AssignedBuildingId is null && s.PatientAtBuildingId is null
                                   && state.Buildings.Any(b => CanWork(sim, s, b))).ToList();
        if (idle.Count > 0)
            return new("idle", $"{string.Join(", ", idle.Select(s => s.Name))} {(idle.Count == 1 ? "is" : "are")} idle: select a building and assign workers.");

        var natural = data.Rules.Medical.NaturalHealMaxSeverity;
        bool Serious(Survivor s) => s.Body.Parts.Values.Any(p => p.Condition == PartCondition.Injured && p.Severity >= natural);
        if (sim.MedBays.Any() && sim.MedBays.Any(b => sim.FreePatientSlots(b) > 0)
            && home.FirstOrDefault(s => s.PatientAtBuildingId is null && Serious(s)) is { } wounded)
            return new("admit", $"{wounded.Name} has a wound that will not heal on its own: select the {Name(data.Rules.Medical.BuildingId)} and Admit them. A medic and Medicine speed it up.");
        if (!sim.MedBays.Any() && home.Any(Serious))
            return new("medbay", $"Wounds of {natural * 100:0}% or more do not heal on their own, and bad ones get worse: build a {Name(data.Rules.Medical.BuildingId)} and admit the wounded.");

        if (state.NextExpeditionId == 1 && state.Expedition is null && sim.Scavenge is { } scavenge && home.Count(s => !s.IsChild) >= 2)
        {
            var zone = scavenge.Zones.Values.Where(z => WorldMap.IntelOf(state, z) != ZoneIntel.Unknown)
                .OrderBy(z => z.Threat).ThenBy(z => z.TravelDays).ThenBy(z => z.Id, StringComparer.Ordinal).FirstOrDefault();
            if (zone is not null)
                return new("expedition", $"Scrap, cloth and medicine come from scavenging: open Expedition and send two armed adults to {zone.Name}. Children stay home.");
        }
        if (!Has("workshop"))
            return new("workshop", $"A {Name("workshop")} makes weapons, Herbal Remedy (medicine) and prosthetics.");
        if (data.Food is { } food)
        {
            var day = data.Rules.TicksPerDay;
            if (!Has(food.Rules.CampfireBuilding) && !Has("kitchen") && state.Tick >= 2 * day)
                return new("campfire", $"Raw food keeps people alive; cooked food goes further and lifts mood. Build a {Name(food.Rules.CampfireBuilding)}, " +
                                       "queue Purified Water and Boiled Potatoes, and give it a cook.");
            if (!Has("smokehouse") && !Has(food.Rules.CellarBuilding)
                && state.Events.Any(e => e.Type == ColonyEventType.FoodRotted && e.Tick >= state.Tick - 2 * day))
                return new("spoilage", $"Food is rotting in the pantry: a {Name("smokehouse")} smokes, salts and dries it; beans and corn keep longer than potatoes.");
        }
        if (state.Survivors.Count >= sim.BedCapacity)
            return new("beds", $"Every bed is taken: no babies are conceived and newcomers leave after a while. Upgrade the {Name("shelter")} or build a House.");
        if (sim.TownHallLevel == 1 && Has("med_bay"))
            return new("townhall", $"Upgrading the {Name(data.Rules.TownHallBuildingId)} raises every building's level cap and the party size, and unlocks the House, Armory and Electronics Lab.");
        return null;
    }

    private static bool CanWork(ColonySim sim, Survivor s, Building b)
    {
        var def = sim.Data.Buildings[b.DefId];
        if (def.Job is not { } job || sim.FreeSlots(b) <= 0) return false;
        // Production buildings with nothing queued have nothing to do.
        if (def.Produces is null && sim.Data.RecipesFor(def.Id).Count > 0 && b.Queue.Count == 0) return false;
        return !s.IsChild || sim.Data.Jobs[job].ChildrenAllowed;
    }
}
