using System.Text;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;

namespace Remnants.Playtest;

/// <summary>The world-map trip per zone and vehicle tier, as the expedition planner computes it (Core's travel plan).</summary>
public static class TravelTable
{
    public static string Render(GameData data, ScavengeData scavenge)
    {
        var sim = new ColonySim(data, new ColonyState { Tick = 10 }, scavenge);
        foreach (var tier in data.Vehicles.Tiers)
        {
            // Motor vehicles with headlights, so the hour of departure does not matter (without: ÷ nightSpeedMultiplier at night).
            var v = new Vehicle { Id = sim.State.NextVehicleId++, TierId = tier.Id, Name = tier.Name, Condition = tier.MaxCondition };
            if (tier.Motorized) v.Upgrades.Add("headlights");
            sim.State.Vehicles.Add(v);
        }
        var sb = new StringBuilder();
        sb.AppendLine($"{"zone",-16} {"walk",5} " + string.Join(" ", data.Vehicles.Tiers.Select(t => $"{t.Id,-22}")));
        sb.AppendLine($"{"",-16} {"",5} " + string.Join(" ", data.Vehicles.Tiers.Select(_ => $"{"days food fuel",-22}")));
        foreach (var zone in scavenge.Zones.Values.OrderBy(z => z.TravelDays).ThenBy(z => z.Id, StringComparer.Ordinal))
        {
            sb.Append($"{zone.Id,-16} {zone.TravelDays,5:0.0} ");
            foreach (var v in sim.State.Vehicles)
            {
                var plan = sim.PlanTravel(zone.Id, v.Id);
                var cell = plan.Blocked is not null ? "out of range"
                    : $"{plan.Days,4:0.00} {WorldMap.RationsPerMember(data, plan.Days),4:0.#} {plan.FuelRoundTrip,4:0}";
                sb.Append($"{cell,-22} ");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
