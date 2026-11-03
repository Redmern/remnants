using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>Workers on shift (see <see cref="Schedule"/>) at gathering buildings produce resources and train. Care and crafting have their own systems.</summary>
internal static class ProductionSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        var tools = WorkTools.Handout(state, data);
        foreach (var building in state.Buildings)
        {
            var def = data.Buildings[building.DefId];
            if (def.Job is null || def.Produces is null) continue;
            var job = data.Jobs[def.Job];
            foreach (var (worker, shift) in Schedule.Crew(state, data, building))
            {
                var tool = tools.GetValueOrDefault(job.Id)?.GetValueOrDefault(worker.Id) ?? 0;
                state.Stockpile.Add(def.Produces, JobOutput.PerTick(def, building.Level, job, worker, data.Rules.Output, data, tool) * shift);
                Training.Work(state, data, job, worker);
            }
        }
    }
}

/// <summary>
/// Beds go first to those who already had one, then by id. Each bed is in a housing building: people keep their
/// home while it has room, and newcomers move in with their partner or parents when they can. The homeless count
/// down a grace timer (paused while they are away) and leave the colony when it runs out. An infant or child whose timer runs out leaves with a
/// parent who is at home (mother first) and that parent's departure ends their couple; with no parent at home the
/// child is taken in by passing travelers.
/// </summary>
internal static class HousingSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        var beds = Capacity(state, data);
        var order = state.Survivors.OrderByDescending(s => s.HasBed).ThenBy(s => s.Id).ToList();
        var leaving = new List<Survivor>();
        for (var i = 0; i < order.Count; i++)
        {
            var s = order[i];
            var housed = i < beds;
            if (housed)
            {
                if (!s.HasBed && s.HomelessTicks > 0)
                    state.Log(new(state.Tick, ColonyEventType.Housed, $"{s.Name} found a bed.", s.Id));
                s.HasBed = true;
                s.HomelessTicks = 0;
                continue;
            }

            if (s.HasBed || s.HomelessTicks == 0)
                state.Log(new(state.Tick, ColonyEventType.Homeless, $"{s.Name} has no bed.", s.Id));
            s.HasBed = false;
            s.HomeBuildingId = null;
            // Someone away on an expedition cannot look for a bed; their grace waits until they are home.
            if (s.Away) continue;
            s.HomelessTicks++;
            if (s.HomelessTicks > data.Rules.Housing.HomelessGraceTicks) leaving.Add(s);
        }

        AssignHomes(state, data, order.Take(beds).ToList());
        foreach (var s in leaving) Leave(state, data, s);
    }

    public static int Capacity(ColonyState state, GameData data) =>
        state.Buildings.Sum(b => data.Buildings[b.DefId].Level(b.Level).Beds);

    private static void AssignHomes(ColonyState state, GameData data, List<Survivor> housed)
    {
        var room = state.Buildings.Where(b => data.Buildings[b.DefId].Level(b.Level).Beds > 0).OrderBy(b => b.Id)
            .ToDictionary(b => b.Id, b => data.Buildings[b.DefId].Level(b.Level).Beds);
        var moving = new List<Survivor>();
        foreach (var s in housed)
        {
            if (s.HomeBuildingId is { } home && room.GetValueOrDefault(home) > 0) room[home]--;
            else
            {
                s.HomeBuildingId = null;
                moving.Add(s);
            }
        }
        foreach (var s in moving)
        {
            var near = new[] { s.PartnerId, s.MotherId, s.FatherId }
                .Select(id => id is { } v ? state.FindSurvivor(v)?.HomeBuildingId : null)
                .FirstOrDefault(h => h is { } v && room.GetValueOrDefault(v) > 0);
            // Otherwise pick a building with room for the whole household that still has to move in.
            var household = moving.Count(o => o.HomeBuildingId is null
                && (o == s || o.Id == s.PartnerId || o.MotherId == s.Id || o.FatherId == s.Id));
            var home = near
                ?? room.Where(kv => kv.Value >= household).Select(kv => (int?)kv.Key).FirstOrDefault()
                ?? room.First(kv => kv.Value > 0).Key;
            s.HomeBuildingId = home;
            room[home]--;
        }
    }

    private static void Leave(ColonyState state, GameData data, Survivor s)
    {
        if (!state.Survivors.Contains(s)) return;  // already left with their child
        if (!s.IsChild)
        {
            Depart(state, data, s, $"{s.Name} left: no place to sleep.");
            return;
        }

        var parent = new[] { s.MotherId, s.FatherId }
            .Select(id => id is { } v ? state.FindSurvivor(v) : null)
            .FirstOrDefault(p => p is { Away: false });
        if (parent is null)
        {
            Depart(state, data, s, $"{s.Name} was taken in by passing travelers: no place to sleep.");
            return;
        }
        Depart(state, data, s, $"{s.Name} left with {parent.Name}: no place for the child to sleep.");
        Depart(state, data, parent, $"{parent.Name} left to look after {s.Name}.");
    }

    private static void Depart(ColonyState state, GameData data, Survivor s, string message)
    {
        state.Survivors.Remove(s);
        s.AssignedBuildingId = null;
        ConstructionSystem.Release(state, s.Id);
        s.PatientAtBuildingId = null;
        state.Log(new(state.Tick, ColonyEventType.Left, message, s.Id));
        Bonds.OnGone(state, data, s, died: false);
    }
}
