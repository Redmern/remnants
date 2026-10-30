using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Raids;

/// <summary>A colony command of the defense layer; <see cref="ColonySim.Execute"/> hands these to <see cref="DefenseHandlers"/>.</summary>
public interface IRaidCommand : IColonyCommand;

/// <summary>Build a defense or trap on a free grid cell (not a built plot), facing a side (walls run across it, turrets aim at it).</summary>
public sealed record PlaceDefense(string DefenseId, int X, int Y, Side Facing = Side.South) : IRaidCommand;

/// <summary>Tear a defense down for <c>repair.removeRefund</c> of its cost; its guards go off duty.</summary>
public sealed record RemoveDefense(int DefenseId) : IRaidCommand;

/// <summary>Turn a placed defense (free).</summary>
public sealed record RotateDefense(int DefenseId, Side Facing) : IRaidCommand;

/// <summary>Upgrade into the next tier or level (<see cref="DefenseDef.UpgradesTo"/>), paying the difference in cost.</summary>
public sealed record UpgradeDefense(int DefenseId) : IRaidCommand;

/// <summary>Re-arm a sprung trap for its re-arm cost.</summary>
public sealed record RearmTrap(int DefenseId) : IRaidCommand;

/// <summary>Put an adult on guard duty at a post (it takes them off their building job), or with null take them off.</summary>
public sealed record AssignGuard(int SurvivorId, int? PostId) : IRaidCommand;

/// <summary>The guards at this post stand the night watch (night work) instead of the day.</summary>
public sealed record SetNightWatch(int PostId, bool On) : IRaidCommand;

/// <summary>Queue the repair of a damaged building or defense; materials are paid when the repair starts.</summary>
public sealed record QueueRepair(RepairTarget Target, int TargetId) : IRaidCommand;

public sealed record CancelRepair(int RepairId) : IRaidCommand;

/// <summary>Prep: who defends the warned raid (empty = every able adult; guards always do).</summary>
public sealed record SetDefenders(List<int> SurvivorIds) : IRaidCommand;

/// <summary>Prep: children to the shelter (no risk to them; a little fear).</summary>
public sealed record ShelterChildren(bool On) : IRaidCommand;

/// <summary>Prep: shut every gate for the raid (they soak like walls; they reopen after it).</summary>
public sealed record CloseGates(bool On) : IRaidCommand;

/// <summary>Prep: call the party back while it is still on the road out and can make it home before the raid.</summary>
public sealed record RecallExpedition : IRaidCommand;

/// <summary>Prep: fight the warned raid on the tactical map when it arrives (<see cref="RaidBattle"/>) instead of auto-resolving it.</summary>
public sealed record ChooseFight(bool Fight) : IRaidCommand;

/// <summary>Defense commands, and the queries the UI needs before sending one (cost, placement, guard slots, recall).</summary>
public static class DefenseHandlers
{
    internal static CommandResult Execute(ColonySim sim, IRaidCommand command)
    {
        if (sim.Raids is not { } raids) return CommandResult.Fail(CommandError.NoRaidData, "Raids are not loaded.");
        return command switch
        {
            PlaceDefense c => Place(sim, raids, c),
            RemoveDefense c => Remove(sim, raids, c),
            RotateDefense c => Rotate(sim, c),
            UpgradeDefense c => Upgrade(sim, raids, c),
            RearmTrap c => Rearm(sim, raids, c),
            AssignGuard c => Guard(sim, raids, c),
            SetNightWatch c => NightWatch(sim, raids, c),
            QueueRepair c => Repair(sim, raids, c),
            CancelRepair c => CancelRepairOrder(sim, c),
            SetDefenders c => Prep(sim, raid => Defenders(sim, raid, c)),
            ShelterChildren c => Prep(sim, raid =>
            {
                raid.ChildrenSheltered = c.On;
                return CommandResult.Success(c.On ? "The children are in the shelter." : "The children stay out.");
            }),
            CloseGates c => Prep(sim, raid =>
            {
                raid.GatesClosed = c.On;
                foreach (var g in sim.State.Defense.Defenses.Where(g => raids.Defenses.GetValueOrDefault(g.DefId)?.Kind == DefenseKind.Gate))
                    g.Closed = c.On;
                return CommandResult.Success(c.On ? "Gates shut." : "Gates open.");
            }),
            RecallExpedition => Prep(sim, raid => Recall(sim, raid)),
            ApplyRaidBattle c => RaidBattle.Apply(sim, raids, c),
            ChooseFight c => Prep(sim, raid =>
            {
                if (c.Fight && RaidBattle.CannotFight(sim, raid) is { } why) return CommandResult.Fail(CommandError.CannotFight, why);
                raid.Fight = c.Fight;
                return CommandResult.Success(c.Fight ? "We fight them at the fence." : "The defenders will handle it.");
            }),
            _ => CommandResult.Fail(CommandError.UnknownCommand, $"Unknown command {command.GetType().Name}."),
        };
    }

    /// <summary>Why a defense cannot go on this cell (null when it can), for the placement ghost.</summary>
    public static CommandResult CanPlace(ColonySim sim, RaidData raids, PlaceDefense c)
    {
        if (!raids.Defenses.TryGetValue(c.DefenseId, out var def))
            return CommandResult.Fail(CommandError.UnknownDefense, $"No defense '{c.DefenseId}'.");
        if (!DefenseSchematics.Unlocked(sim, def.Id))
            return CommandResult.Fail(CommandError.SchematicMissing, $"{def.Name} needs its schematic.");
        if (sim.TownHallLevel < def.MinTownHallLevel)
            return CommandResult.Fail(CommandError.TownHallLevelTooLow, $"{def.Name} needs Town Hall level {def.MinTownHallLevel}.");
        var grid = DefenseGrid.Of(sim, raids);
        if (!grid.Inside(c.X, c.Y)) return CommandResult.Fail(CommandError.InvalidCell, "That is outside the colony grounds.");
        if (grid.PlotAt(c.X, c.Y) is var plot and >= 0 && !sim.IsPlotFree(plot))
            return CommandResult.Fail(CommandError.CellOccupied, "A building stands there.");
        if (sim.State.Defense.Defenses.Any(d => d.X == c.X && d.Y == c.Y))
            return CommandResult.Fail(CommandError.CellOccupied, "Something is already built there.");
        var cost = Cost(sim, def, def.Cost);
        if (!sim.State.Stockpile.CanAfford(cost))
            return CommandResult.Fail(CommandError.CannotAfford, $"Cannot afford {def.Name}: {CommandHandlers.Describe(sim.State.Stockpile.Shortfall(cost))}.");
        return CommandResult.Success("");
    }

    /// <summary>A defense's cost for this colony now: traps are cheaper with an Engineer at home (best one counts, at most half off).</summary>
    public static Dictionary<string, double> Cost(ColonySim sim, DefenseDef def, Dictionary<string, double> cost)
    {
        if (!def.IsTrap) return new(cost);
        var cut = Math.Min(0.5, sim.State.Survivors.Where(s => !s.Away)
            .Select(s => PerkEffects.For(s, sim.Data).Hook(PerkHooks.TrapCostReduction)).DefaultIfEmpty(0).Max());
        return cost.ToDictionary(kv => kv.Key, kv => Math.Ceiling(kv.Value * (1 - cut)));
    }

    private static CommandResult Place(ColonySim sim, RaidData raids, PlaceDefense c)
    {
        if (CanPlace(sim, raids, c) is { Ok: false } fail) return fail;
        var def = raids.Defense(c.DefenseId);
        sim.State.Stockpile.Spend(Cost(sim, def, def.Cost));
        var d = new PlacedDefense { Id = sim.State.Defense.NextDefenseId++, DefId = def.Id, X = c.X, Y = c.Y, Facing = c.Facing, Hp = def.Hp };
        sim.State.Defense.Defenses.Add(d);
        sim.State.Log(new(sim.State.Tick, ColonyEventType.DefenseBuilt, $"Built a {def.Name}."));
        return CommandResult.Success($"Built a {def.Name}.", d.Id);
    }

    private static CommandResult Find(ColonySim sim, int id, out PlacedDefense d)
    {
        d = sim.State.Defense.Find(id)!;
        return d is null ? CommandResult.Fail(CommandError.UnknownDefense, $"No defense {id}.") : CommandResult.Success("");
    }

    private static CommandResult Remove(ColonySim sim, RaidData raids, RemoveDefense c)
    {
        if (Find(sim, c.DefenseId, out var d) is { Ok: false } fail) return fail;
        var def = raids.Defense(d.DefId);
        var share = raids.Def.Repair.RemoveRefund * (d.Hp / def.Hp);
        foreach (var (res, amount) in def.Cost) sim.State.Stockpile.Add(res, Math.Floor(amount * share));
        RaidSystem.RemoveDefense(sim.State, d);
        return CommandResult.Success($"Took down the {def.Name}.");
    }

    private static CommandResult Rotate(ColonySim sim, RotateDefense c)
    {
        if (Find(sim, c.DefenseId, out var d) is { Ok: false } fail) return fail;
        d.Facing = c.Facing;
        return CommandResult.Success($"Facing {c.Facing.ToString().ToLowerInvariant()}.", d.Id);
    }

    /// <summary>What upgrading costs: the next tier's cost minus this one's, per resource, never below 0.</summary>
    public static Dictionary<string, double> UpgradeCost(DefenseDef from, DefenseDef to) =>
        to.Cost.Select(kv => (kv.Key, Value: kv.Value - from.Cost.GetValueOrDefault(kv.Key))).Where(kv => kv.Value > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    private static CommandResult Upgrade(ColonySim sim, RaidData raids, UpgradeDefense c)
    {
        if (Find(sim, c.DefenseId, out var d) is { Ok: false } fail) return fail;
        var def = raids.Defense(d.DefId);
        if (def.UpgradesTo is not { } nextId) return CommandResult.Fail(CommandError.MaxLevel, $"The {def.Name} is as good as it gets.");
        var next = raids.Defense(nextId);
        if (!DefenseSchematics.Unlocked(sim, next.Id)) return CommandResult.Fail(CommandError.SchematicMissing, $"{next.Name} needs its schematic.");
        if (sim.TownHallLevel < next.MinTownHallLevel)
            return CommandResult.Fail(CommandError.TownHallLevelTooLow, $"{next.Name} needs Town Hall level {next.MinTownHallLevel}.");
        var cost = UpgradeCost(def, next);
        if (!sim.State.Stockpile.CanAfford(cost))
            return CommandResult.Fail(CommandError.CannotAfford, $"Cannot afford {next.Name}: {CommandHandlers.Describe(sim.State.Stockpile.Shortfall(cost))}.");
        sim.State.Stockpile.Spend(cost);
        d.Hp = next.Hp * (d.Hp / def.Hp);
        d.DefId = next.Id;
        sim.State.Log(new(sim.State.Tick, ColonyEventType.DefenseBuilt, $"The {def.Name} is now a {next.Name}."));
        return CommandResult.Success($"Upgraded to {next.Name}.", d.Id);
    }

    private static CommandResult Rearm(ColonySim sim, RaidData raids, RearmTrap c)
    {
        if (Find(sim, c.DefenseId, out var d) is { Ok: false } fail) return fail;
        var def = raids.Defense(d.DefId);
        if (!def.IsTrap || !def.Rearmable) return CommandResult.Fail(CommandError.NotATrap, $"The {def.Name} does not re-arm.");
        if (!d.Sprung) return CommandResult.Fail(CommandError.AlreadyArmed, $"The {def.Name} is armed.");
        var cost = Cost(sim, def, def.RearmCost);
        if (!sim.State.Stockpile.CanAfford(cost))
            return CommandResult.Fail(CommandError.CannotAfford, $"Cannot re-arm: {CommandHandlers.Describe(sim.State.Stockpile.Shortfall(cost))}.");
        sim.State.Stockpile.Spend(cost);
        d.Sprung = false;
        return CommandResult.Success($"Re-armed the {def.Name}.", d.Id);
    }

    /// <summary>Free guard slots at a post (0 for defenses that take no guards).</summary>
    public static int FreeGuardSlots(ColonySim sim, RaidData raids, PlacedDefense post) =>
        (raids.Defenses.GetValueOrDefault(post.DefId)?.GuardSlots ?? 0) - sim.State.Defense.Guards.Values.Count(p => p == post.Id);

    private static CommandResult Guard(ColonySim sim, RaidData raids, AssignGuard c)
    {
        var state = sim.State;
        if (state.FindSurvivor(c.SurvivorId) is not { } s) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (c.PostId is not { } postId)
        {
            if (!state.Defense.Guards.Remove(s.Id)) return CommandResult.Fail(CommandError.NotAssigned, $"{s.Name} is not on guard duty.");
            return CommandResult.Success($"{s.Name} is off guard duty.");
        }
        if (Find(sim, postId, out var post) is { Ok: false } fail) return fail;
        if (s.IsChild) return CommandResult.Fail(CommandError.NotAnAdult, $"{s.Name} is too young to stand guard.");
        if (s.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{s.Name} is away.");
        if (s.PatientAtBuildingId is not null) return CommandResult.Fail(CommandError.IsPatient, $"{s.Name} is a patient.");
        if (StudySystem.ClassOf(state, s.Id) is not null) return CommandResult.Fail(CommandError.InClass, $"{s.Name} is in class.");
        if (state.Defense.Guards.GetValueOrDefault(s.Id) == post.Id) return CommandResult.Success($"{s.Name} already guards here.", post.Id);
        if (FreeGuardSlots(sim, raids, post) <= 0) return CommandResult.Fail(CommandError.NoFreeSlot, "No room for another guard there.");
        s.AssignedBuildingId = null;
        state.Defense.Guards[s.Id] = post.Id;
        var name = raids.Defense(post.DefId).Name;
        state.Log(new(state.Tick, ColonyEventType.GuardPosted, $"{s.Name} stands guard at the {name}.", s.Id));
        return CommandResult.Success($"{s.Name} stands guard at the {name}.", post.Id);
    }

    private static CommandResult NightWatch(ColonySim sim, RaidData raids, SetNightWatch c)
    {
        if (Find(sim, c.PostId, out var post) is { Ok: false } fail) return fail;
        if (raids.Defense(post.DefId).GuardSlots == 0) return CommandResult.Fail(CommandError.NoFreeSlot, "Nobody stands guard there.");
        post.NightWatch = c.On;
        return CommandResult.Success(c.On ? "Night watch: they guard from dusk, and sleep in the day." : "Day watch.", post.Id);
    }

    private static CommandResult Repair(ColonySim sim, RaidData raids, QueueRepair c)
    {
        var state = sim.State;
        if (c.Target == RepairTarget.Building && state.FindBuilding(c.TargetId) is null)
            return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.TargetId}.");
        if (c.Target == RepairTarget.Defense && state.Defense.Find(c.TargetId) is null)
            return CommandResult.Fail(CommandError.UnknownDefense, $"No defense {c.TargetId}.");
        if (RaidSystem.Missing(sim, raids, c.Target, c.TargetId) <= 1e-9) return CommandResult.Fail(CommandError.NotDamaged, "It is not damaged.");
        if (state.Defense.Repairs.Any(r => r.Target == c.Target && r.TargetId == c.TargetId))
            return CommandResult.Fail(CommandError.AlreadyBooked, "Its repair is already queued.");
        var order = new RepairOrder
        {
            Id = state.Defense.NextRepairId++, Target = c.Target, TargetId = c.TargetId, Cost = RaidSystem.RepairCost(sim, raids, c.Target, c.TargetId),
        };
        state.Defense.Repairs.Add(order);
        return CommandResult.Success("Repair queued.", order.Id);
    }

    private static CommandResult CancelRepairOrder(ColonySim sim, CancelRepair c)
    {
        var order = sim.State.Defense.Repairs.Find(r => r.Id == c.RepairId);
        if (order is null) return CommandResult.Fail(CommandError.UnknownOrder, $"No repair {c.RepairId}.");
        if (order.Paid) foreach (var (res, amount) in order.Cost) sim.State.Stockpile.Add(res, amount);
        sim.State.Defense.Repairs.Remove(order);
        return CommandResult.Success("Repair cancelled.");
    }

    private static CommandResult Prep(ColonySim sim, Func<Raid, CommandResult> action) =>
        sim.State.Defense.Raid is { Stage: RaidStage.Warned } raid
            ? action(raid)
            : CommandResult.Fail(CommandError.NoRaidWarning, "No raid is coming that we know of.");

    private static CommandResult Defenders(ColonySim sim, Raid raid, SetDefenders c)
    {
        foreach (var id in c.SurvivorIds)
            if (sim.State.FindSurvivor(id) is not { } s) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {id}.");
            else if (s.IsChild) return CommandResult.Fail(CommandError.NotAnAdult, $"{s.Name} is a child.");
        raid.Defenders = new SortedSet<int>(c.SurvivorIds);
        return CommandResult.Success(c.SurvivorIds.Count == 0 ? "Everyone able will defend." : $"{c.SurvivorIds.Count} defenders picked.");
    }

    /// <summary>Whether the party out can be called home before the raid (still walking out, and back in time).</summary>
    public static bool CanRecall(ColonyState state, Raid raid) =>
        state.Expedition is { } e && e.PhaseAt(state.Tick) == ExpeditionPhase.Outbound && e.Scavenge is null
        && state.Tick - e.LaunchedTick <= raid.ArrivalTick - state.Tick;

    private static CommandResult Recall(ColonySim sim, Raid raid)
    {
        var state = sim.State;
        if (state.Expedition is not { } e) return CommandResult.Fail(CommandError.NoExpedition, "No party is out.");
        if (!CanRecall(state, raid)) return CommandResult.Fail(CommandError.CannotRecall, "The party is too far out to make it back in time.");
        foreach (var m in e.Members)
        {
            foreach (var id in m.AllItems) state.Inventory.Add(id);
            if (state.FindSurvivor(m.SurvivorId) is { } s) s.Away = false;
        }
        VehicleRules.Recalled(sim, e);
        state.Expedition = null;
        state.Defense.WatchedExpedition = null;
        state.Log(new(state.Tick, ColonyEventType.ExpeditionRecalled, "The party turned back to defend the colony."));
        return CommandResult.Success("The party is coming home.");
    }
}
