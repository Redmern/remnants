using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// The colony's Gear Bench (<see cref="GearBench"/> rules): mods go on and come off gear in the inventory, or a survivor's
/// fitted gun arm, at a bench building; Mk kits are fitted at an upgrade building. Everything stays an item id, so a
/// modded piece goes out on an expedition and comes back as itself.
/// </summary>
internal static class GearBenchHandlers
{
    /// <summary>The built bench building (of these types) with the highest level, or null.</summary>
    public static Building? Bench(ColonySim sim, IEnumerable<string> types)
    {
        var set = types.ToHashSet();
        return sim.State.Buildings.Where(b => set.Contains(b.DefId)).OrderByDescending(b => b.Level).ThenBy(b => b.Id).FirstOrDefault();
    }

    public static CommandResult Install(ColonySim sim, InstallMod c)
    {
        if (sim.Scavenge is not { GearMods: { } mods } data) return ExpeditionHandlers.NoData();
        if (Bench(sim, mods.Def.Rules.Benches) is null) return NoBench(sim, mods.Def.Rules.Benches);
        if (!sim.State.Inventory.Has(c.ModItemId)) return CommandResult.Fail(CommandError.MissingItems, $"The colony has no '{c.ModItemId}'.");
        if (Target(sim, c.ItemId, c.SurvivorId, c.Part, out var part) is { } missing) return missing;
        var outcome = GearBench.Install(data, c.ItemId, c.ModItemId);
        if (!outcome.Ok) return Fail(outcome);
        sim.State.Inventory.TryTake(c.ModItemId);
        Replace(sim, c.ItemId, outcome.ItemId!, part);
        if (outcome.Returned is { } back) sim.State.Inventory.Add(back);
        return Done(sim, outcome, c.SurvivorId);
    }

    public static CommandResult Remove(ColonySim sim, RemoveMod c)
    {
        if (sim.Scavenge is not { GearMods: { } mods } data) return ExpeditionHandlers.NoData();
        if (Bench(sim, mods.Def.Rules.Benches) is null) return NoBench(sim, mods.Def.Rules.Benches);
        if (Target(sim, c.ItemId, c.SurvivorId, c.Part, out var part) is { } missing) return missing;
        var outcome = GearBench.Remove(data, c.ItemId, c.Slot);
        if (!outcome.Ok) return Fail(outcome);
        Replace(sim, c.ItemId, outcome.ItemId!, part);
        sim.State.Inventory.Add(outcome.Returned!);
        return Done(sim, outcome, c.SurvivorId);
    }

    public static CommandResult Upgrade(ColonySim sim, UpgradeGear c)
    {
        if (sim.Scavenge is not { GearMods: { } mods } data) return ExpeditionHandlers.NoData();
        if (Bench(sim, mods.Def.Rules.UpgradeAt) is null) return NoBench(sim, mods.Def.Rules.UpgradeAt);
        if (!sim.State.Inventory.Has(c.ItemId)) return CommandResult.Fail(CommandError.MissingItems, $"The colony has no '{c.ItemId}'.");
        if (!sim.State.Inventory.Has(c.KitItemId)) return CommandResult.Fail(CommandError.MissingItems, $"The colony has no '{c.KitItemId}'.");
        var outcome = GearBench.Upgrade(data, c.ItemId, c.KitItemId);
        if (!outcome.Ok) return Fail(outcome);
        sim.State.Inventory.TryTake(c.KitItemId);
        Replace(sim, c.ItemId, outcome.ItemId!, null);
        return Done(sim, outcome, null);
    }

    /// <summary>The gear must be in the inventory, or be the prosthetic fitted on the survivor's part.</summary>
    private static CommandResult? Target(ColonySim sim, string itemId, int? survivorId, BodyPart? part, out (Survivor Who, BodyPart Part)? fitted)
    {
        fitted = null;
        if (survivorId is null) return sim.State.Inventory.Has(itemId) ? null : CommandResult.Fail(CommandError.MissingItems, $"The colony has no '{itemId}'.");
        var who = sim.State.FindSurvivor(survivorId.Value);
        if (who is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {survivorId}.");
        if (who.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{who.Name} is away.");
        if (part is not { } p || who.Body[p].Condition != PartCondition.Prosthetic || who.Body[p].ProstheticItemId != itemId)
            return CommandResult.Fail(CommandError.NotAProsthetic, $"{who.Name} has no '{itemId}' fitted there.");
        fitted = (who, p);
        return null;
    }

    private static void Replace(ColonySim sim, string oldId, string newId, (Survivor Who, BodyPart Part)? fitted)
    {
        if (fitted is { } f)
        {
            f.Who.Body[f.Part].ProstheticItemId = newId;
            return;
        }
        sim.State.Inventory.TryTake(oldId);
        sim.State.Inventory.Add(newId);
    }

    private static CommandResult Done(ColonySim sim, GearBench.Outcome outcome, int? survivorId)
    {
        sim.State.Log(new(sim.State.Tick, ColonyEventType.GearModded, outcome.Message, SurvivorId: survivorId));
        return CommandResult.Success(outcome.Message);
    }

    private static CommandResult NoBench(ColonySim sim, IEnumerable<string> types) =>
        CommandResult.Fail(CommandError.NoBench,
            $"Needs a {string.Join(" or ", types.Select(t => sim.Data.Buildings.TryGetValue(t, out var b) ? b.Name : t))}.");

    private static CommandResult Fail(GearBench.Outcome o) => CommandResult.Fail(o.Error switch
    {
        GearBench.Error.NoMods => CommandError.NoScavengeData,
        GearBench.Error.NotModdable => CommandError.NotModdable,
        GearBench.Error.NotAMod or GearBench.Error.NotAKit => CommandError.UnknownItem,
        GearBench.Error.DoesNotFit => CommandError.ModDoesNotFit,
        GearBench.Error.NoFreeSlot => CommandError.NoFreeModSlot,
        GearBench.Error.EmptySlot => CommandError.EmptyModSlot,
        GearBench.Error.WrongKit => CommandError.WrongKit,
        GearBench.Error.MaxMk => CommandError.MaxMk,
        _ => CommandError.UnknownCommand,
    }, o.Message);
}
