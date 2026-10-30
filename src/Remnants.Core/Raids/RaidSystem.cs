using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Raids;

/// <summary>
/// The raid tick (after the colony's own systems; nothing happens without <see cref="ColonySim.Raids"/>): upkeep at
/// midnight, guard duty, repairs, then the scheduler. A raid is drawn when a threat's pressure reaches the threshold
/// (after the grace period and the gap since the last), sets out <c>travelHours</c> before it arrives, is seen when the
/// arrival is within the lead time (<see cref="RaidWarning"/>), and auto-resolves on arrival unless the player chose to fight.
/// </summary>
public static class RaidSystem
{
    internal static void Run(ColonySim sim)
    {
        if (sim.Raids is not { } raids) return;
        var state = sim.State;
        if (DayNight.Hour(state.Tick, sim.Data.Rules) == 0) PayUpkeep(sim, raids);
        Guards.Run(sim, raids);
        Repairs(sim, raids);
        WatchReturns(state);
        Schedule(sim, raids);
    }

    // ---- buildings ----

    public static double BuildingHp(RaidData raids, Building b)
    {
        var hp = raids.Def.Consequences.BuildingHp;
        return hp[Math.Clamp(b.Level - 1, 0, hp.Count - 1)];
    }

    /// <summary>HP a building has lost to raids.</summary>
    public static double Damage(ColonyState state, Building b) => state.Defense.BuildingDamage.GetValueOrDefault(b.Id);

    /// <summary>Wrecked by a raid: nobody works or is assigned there until it is repaired.</summary>
    public static bool IsRubble(ColonySim sim, Building b) =>
        sim.Raids is { } raids && Damage(sim.State, b) >= BuildingHp(raids, b) * (1 - raids.Def.Consequences.RubbleAt) - 1e-9 && Damage(sim.State, b) > 0;

    internal static void Wreck(ColonySim sim, Building b)
    {
        var name = sim.Data.Buildings[b.DefId].Name;
        foreach (var w in sim.State.WorkersAt(b.Id).ToList()) w.AssignedBuildingId = null;
        b.NightShift = false;
        sim.State.Log(new(sim.State.Tick, ColonyEventType.BuildingWrecked, $"The {name} was wrecked in the raid; repair it to use it again.", BuildingId: b.Id));
    }

    internal static void RemoveDefense(ColonyState state, PlacedDefense d)
    {
        state.Defense.Defenses.Remove(d);
        foreach (var (id, post) in state.Defense.Guards.ToList())
            if (post == d.Id) state.Defense.Guards.Remove(id);
        state.Defense.Repairs.RemoveAll(r => r.Target == RepairTarget.Defense && r.TargetId == d.Id);
        state.Defense.Unpaid.Remove(d.Id);
    }

    // ---- upkeep ----

    private static void PayUpkeep(ColonySim sim, RaidData raids)
    {
        var state = sim.State;
        state.Defense.Unpaid.Clear();
        foreach (var d in state.Defense.Defenses.OrderBy(d => d.Id))
        {
            if (raids.Defenses.GetValueOrDefault(d.DefId) is not { Upkeep.Count: > 0 } def || d.Hp <= 0) continue;
            if (state.Stockpile.CanAfford(def.Upkeep)) state.Stockpile.Spend(def.Upkeep);
            else state.Defense.Unpaid.Add(d.Id);
        }
        if (state.Defense.Unpaid.Count > 0)
            state.Log(new(state.Tick, ColonyEventType.DefenseUnpaid,
                $"No upkeep for {state.Defense.Unpaid.Count} defense{(state.Defense.Unpaid.Count == 1 ? "" : "s")}: idle today."));
    }

    // ---- repairs ----

    /// <summary>HP missing on a repair target (0 when it is whole or gone).</summary>
    public static double Missing(ColonySim sim, RaidData raids, RepairTarget target, int id) => target switch
    {
        RepairTarget.Building => sim.State.FindBuilding(id) is { } b ? Damage(sim.State, b) : 0,
        _ => sim.State.Defense.Find(id) is { } d && raids.Defenses.TryGetValue(d.DefId, out var def) ? def.Hp - d.Hp : 0,
    };

    /// <summary>What repairing this target costs now (materials, paid when the repair starts).</summary>
    public static Dictionary<string, double> RepairCost(ColonySim sim, RaidData raids, RepairTarget target, int id)
    {
        var rules = raids.Def.Repair;
        Dictionary<string, double> Scale(Dictionary<string, double> cost, double share) =>
            cost.Where(kv => kv.Value * share >= 0.5).ToDictionary(kv => kv.Key, kv => Math.Ceiling(kv.Value * share));
        if (target == RepairTarget.Building && sim.State.FindBuilding(id) is { } b)
            return Scale(sim.Data.Buildings[b.DefId].Level(b.Level).Cost, rules.BuildingCostShare * Damage(sim.State, b) / BuildingHp(raids, b));
        if (target == RepairTarget.Defense && sim.State.Defense.Find(id) is { } d && raids.Defenses.TryGetValue(d.DefId, out var def))
            return Scale(def.Cost, rules.BuildingCostShare * (def.Hp - d.Hp) / def.Hp);
        return new();
    }

    /// <summary>HP repaired per tick right now: the base, plus idle adults on their day shift by Crafting, plus Engineers.</summary>
    public static double RepairRate(ColonySim sim, RaidData raids)
    {
        var rules = raids.Def.Repair;
        var state = sim.State;
        if (sim.Data.Rules.DayNight.Enabled && !DayNight.InWindow(DayNight.Hour(state.Tick, sim.Data.Rules), sim.Data.Rules.DayNight.WorkHours)) return 0;
        var idle = state.Survivors.Where(s => !s.Away && !s.IsChild && s.PatientAtBuildingId is null && s.AssignedBuildingId is null
                                              && !state.Defense.Guards.ContainsKey(s.Id) && StudySystem.ClassOf(state, s.Id) is null);
        var bonus = 1 + state.Survivors.Where(s => !s.Away).Select(s => PerkEffects.For(s, sim.Data).Hook(PerkHooks.RepairSpeedBonus)).DefaultIfEmpty(0).Max();
        return (rules.BaseHpPerTick + idle.Sum(s => rules.HpPerWorkerTick * (0.5 + s.Get(Skill.Crafting) / 100))) * bonus;
    }

    private static void Repairs(ColonySim sim, RaidData raids)
    {
        var state = sim.State;
        var queue = state.Defense.Repairs;
        queue.RemoveAll(r => Missing(sim, raids, r.Target, r.TargetId) <= 1e-9 && !r.Paid);
        var order = queue.FirstOrDefault(r => r.Paid || state.Stockpile.CanAfford(r.Cost));
        if (order is null) return;
        if (!order.Paid)
        {
            state.Stockpile.Spend(order.Cost);
            order.Paid = true;
        }
        var rate = RepairRate(sim, raids);
        if (rate <= 0) return;
        var missing = Missing(sim, raids, order.Target, order.TargetId);
        var step = Math.Min(rate, missing);
        order.Progress += step;
        string name;
        if (order.Target == RepairTarget.Building && state.FindBuilding(order.TargetId) is { } b)
        {
            var left = Damage(state, b) - step;
            if (left <= 1e-9) state.Defense.BuildingDamage.Remove(b.Id);
            else state.Defense.BuildingDamage[b.Id] = left;
            name = sim.Data.Buildings[b.DefId].Name;
        }
        else if (state.Defense.Find(order.TargetId) is { } d)
        {
            var def = raids.Defense(d.DefId);
            d.Hp = Math.Min(def.Hp, d.Hp + step);
            name = def.Name;
        }
        else
        {
            queue.Remove(order);
            return;
        }
        if (Missing(sim, raids, order.Target, order.TargetId) > 1e-9) return;
        queue.Remove(order);
        state.Log(new(state.Tick, ColonyEventType.Repaired, $"Repaired the {name}.",
            BuildingId: order.Target == RepairTarget.Building ? order.TargetId : null));
    }

    // ---- noise from returns ----

    private static void WatchReturns(ColonyState state)
    {
        var d = state.Defense;
        if (d.WatchedExpedition is { } id && state.Expedition?.Id != id) d.Returns.Add(state.Tick);
        d.WatchedExpedition = state.Expedition?.Id;
        d.Returns.RemoveAll(t => state.Tick - t > 72);
    }

    // ---- scheduler ----

    /// <summary>Whether raids can be drawn yet: past the grace days with the basics built, and past the gap since the last.</summary>
    public static bool GraceOver(ColonySim sim, RaidData raids)
    {
        var s = raids.Def.Scheduler;
        var state = sim.State;
        return DayNight.Day(state.Tick, sim.Data.Rules) > s.GraceDays && state.Buildings.Count >= s.GraceBuildings;
    }

    private static void Schedule(ColonySim sim, RaidData raids)
    {
        var state = sim.State;
        var d = state.Defense;
        if (d.Raid is { } raid)
        {
            Advance(sim, raids, raid);
            return;
        }
        foreach (var kind in Enum.GetValues<ThreatKind>())
            d.Pressure[kind] = d.Pressure.GetValueOrDefault(kind) + ThreatModel.PressurePerHour(sim, raids, kind);
        var s = raids.Def.Scheduler;
        if (!GraceOver(sim, raids)) return;
        if (d.LastRaidTick is { } last && state.Tick - last < s.MinGapDays * sim.Data.Rules.TicksPerDay) return;
        var ready = d.Pressure.Where(kv => kv.Value >= s.Threshold).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).ToList();
        if (ready.Count == 0) return;
        d.Raid = Draw(sim, raids, ready[0].Key, ready[0].Value);
        d.Pressure[ready[0].Key] = 0;
        foreach (var k in d.Pressure.Keys.ToList()) d.Pressure[k] *= 0.25;
    }

    /// <summary>Makes a raid of this kind now (size from pressure and escalation, approach and arrival phase by weight).</summary>
    public static Raid Draw(ColonySim sim, RaidData raids, ThreatKind kind, double pressure)
    {
        var state = sim.State;
        var s = raids.Def.Scheduler;
        var t = raids.Threat(kind);
        var rng = sim.Rng;
        var graceEnd = (s.GraceDays + 1) * sim.Data.Rules.TicksPerDay;
        var days = Math.Max(0, (state.Tick - graceEnd) / (double)sim.Data.Rules.TicksPerDay);
        var escalation = 1 + s.EscalationPerRaid * state.Defense.RaidsSurvived + s.EscalationPerDay * days;
        var size = (int)Math.Round((t.MinSize + pressure / 100 * t.SizePerPressure) * escalation);
        size = Math.Clamp(size, t.MinSize, t.MaxSize);
        if (state.Defense.RaidsSurvived == 0) size = Math.Min(size, Math.Max(t.MinSize, s.FirstRaidMaxSize));

        var approach = Weighted(rng, Enum.GetValues<Side>(), side => t.Approach.GetValueOrDefault(side, 1));
        var phase = Weighted(rng, Enum.GetValues<DayPhase>(), p => t.ArrivalPhase.GetValueOrDefault(p, 1));
        var arrival = state.Tick + s.TravelHours;
        for (var i = 0; i < sim.Data.Rules.TicksPerDay && DayNight.PhaseAt(arrival, sim.Data.Rules) != phase; i++) arrival++;
        return new Raid
        {
            Id = state.Defense.NextRaidId++, Kind = kind, Size = size, Approach = approach,
            SetOutTick = state.Tick, ArrivalTick = arrival, WarnTick = arrival, Stage = RaidStage.Approaching,
        };
    }

    private static T Weighted<T>(Random.DeterministicRng rng, T[] options, Func<T, double> weight)
    {
        var total = options.Sum(o => Math.Max(0, weight(o)));
        if (total <= 0) return options[0];
        var roll = rng.NextDouble() * total;
        foreach (var o in options)
        {
            roll -= Math.Max(0, weight(o));
            if (roll < 0) return o;
        }
        return options[^1];
    }

    private static void Advance(ColonySim sim, RaidData raids, Raid raid)
    {
        var state = sim.State;
        if (raid.Stage == RaidStage.Approaching)
        {
            var lead = RaidWarning.LeadHours(sim, raids);
            if (state.Tick >= raid.ArrivalTick - lead) Warn(sim, raids, raid, (int)Math.Max(0, raid.ArrivalTick - state.Tick));
        }
        if (state.Tick < raid.ArrivalTick || raid.Fight) return;
        if (raid.Stage == RaidStage.Approaching) Warn(sim, raids, raid, 0);
        Finish(sim, raids, raid, AutoResolve.Resolve(sim, raids, raid));
    }

    private static void Warn(ColonySim sim, RaidData raids, Raid raid, int lead)
    {
        var threat = raids.Threat(raid.Kind);
        raid.Stage = RaidStage.Warned;
        raid.WarnTick = sim.State.Tick;
        (raid.EstimateMin, raid.EstimateMax) = RaidWarning.Estimate(raids, raid.Size, lead);
        var size = raid.EstimateMin == raid.EstimateMax ? $"{raid.EstimateMin}" : $"{raid.EstimateMin}–{raid.EstimateMax}";
        sim.State.Log(new(sim.State.Tick, ColonyEventType.RaidWarned, lead > 0
            ? $"{threat.Name} spotted to the {raid.Approach.ToString().ToLowerInvariant()}: about {size}, here in {lead} h."
            : $"{threat.Name} at the {raid.Approach.ToString().ToLowerInvariant()} fence: about {size}, no warning!"));
    }

    /// <summary>Ends the raid with its report (auto-resolve, or in stage 2 the fought battle's): gates reopen, escalation counts.</summary>
    public static void Finish(ColonySim sim, RaidData raids, Raid raid, RaidReport report)
    {
        var d = sim.State.Defense;
        foreach (var gate in d.Defenses.Where(g => raids.Defenses.GetValueOrDefault(g.DefId)?.Kind == DefenseKind.Gate)) gate.Closed = false;
        if (d.LastReport is { } old) d.History.Add(old);
        if (d.History.Count > 20) d.History.RemoveAt(0);
        d.LastReport = report;
        d.Raid = null;
        d.LastRaidTick = sim.State.Tick;
        d.RaidsSurvived++;
    }
}
