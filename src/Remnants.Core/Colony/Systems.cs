using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>Workers at gathering buildings produce resources and train.</summary>
internal static class ProductionSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        foreach (var building in state.Buildings)
        {
            var def = data.Buildings[building.DefId];
            if (def.Job is null) continue;
            var job = data.Jobs[def.Job];
            foreach (var worker in state.WorkersAt(building.Id).OrderBy(w => w.Id))
            {
                if (def.Produces is not null)
                    state.Stockpile.Add(def.Produces, JobOutput.PerTick(def, building.Level, job, worker, data.Rules.Output));
                Training.TrainTick(job, worker, data.Rules.Training);
            }
        }
    }
}

/// <summary>Everyone eats each tick. Children are fed first; whoever goes hungry takes damage and may die.</summary>
internal static class FoodSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        var rules = data.Rules.Food;
        var dead = new List<Survivor>();
        foreach (var s in state.Survivors.OrderBy(s => s.IsChild ? 0 : 1).ThenBy(s => s.Id))
        {
            var need = s.IsChild ? rules.ChildPerTick : rules.AdultPerTick;
            if (state.Stockpile.TryTake(rules.ResourceId, need))
            {
                s.StarvingTicks = 0;
                s.Health = Math.Min(Survivor.MaxHealth, s.Health + rules.RecoveryPerTick);
                continue;
            }

            if (s.StarvingTicks == 0)
                state.Log(new(state.Tick, ColonyEventType.Starving, $"{s.Name} is starving.", s.Id));
            s.StarvingTicks++;
            s.Health -= rules.StarvationDamagePerTick;
            if (s.Health <= 0) dead.Add(s);
        }

        foreach (var s in dead)
        {
            s.Health = 0;
            state.Survivors.Remove(s);
            state.Log(new(state.Tick, ColonyEventType.Died, $"{s.Name} starved to death.", s.Id));
        }
    }
}

/// <summary>
/// Beds go first to those who already had one, then by id. The homeless count down a grace timer
/// and leave the colony when it runs out.
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
            s.HomelessTicks++;
            if (s.HomelessTicks > data.Rules.Housing.HomelessGraceTicks) leaving.Add(s);
        }

        foreach (var s in leaving)
        {
            state.Survivors.Remove(s);
            state.Log(new(state.Tick, ColonyEventType.Left, $"{s.Name} left: no place to sleep.", s.Id));
        }
    }

    public static int Capacity(ColonyState state, GameData data) =>
        state.Buildings.Sum(b => data.Buildings[b.DefId].Level(b.Level).Beds);
}

/// <summary>Survivors age every tick; children become adults at the configured age.</summary>
internal static class AgingSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        var adultTicks = data.Rules.AdultAge * data.Rules.TicksPerYear;
        foreach (var s in state.Survivors)
        {
            s.AgeTicks++;
            if (s.IsChild && s.AgeTicks >= adultTicks)
            {
                s.LifeStage = LifeStage.Adult;
                state.Log(new(state.Tick, ColonyEventType.BecameAdult, $"{s.Name} is now an adult.", s.Id));
            }
        }
    }
}
