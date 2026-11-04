using Remnants.Core.Colony;
using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// The electronics devices' field effects (data in electronics.json items; see ARCHITECTURE.md "Electronics and power"):
/// <list type="bullet">
/// <item><b>Charges</b> (<see cref="UseEffect.Charges"/>): a reusable device is not used up; each use spends a charge of the
/// unit's pooled charges for that item id (charges × how many it carries). Spent charges go home with the result.</item>
/// <item><b>EMP</b> (<see cref="EmpDef"/>): at a tile, every Linked in the radius (Chebyshev) drops off the relay and loses
/// its turns for the EMP's rounds; drones in the air there (anyone's) fall where they are; cameras and turrets are fried
/// for good; Network-grade implants in the radius, the thrower's side's too, lock until the next round.</item>
/// <item><b>Holo-decoy</b> (<see cref="DecoyDef"/>): a fake survivor of the user's side on the tile. It never acts, is easy to
/// spot (no stealth), calls idle enemies within its lure radius each round, and the Linked's focus (lowest HP, most
/// exposed) lands on it, so it soaks a round of fire. It fades after its rounds.</item>
/// <item><b>Noise decoy</b> (<see cref="UseEffect.NoiseRadius"/>): enemies within the radius go to look at the tile — Husks
/// even off a hunt unless their prey is next to them; the Linked only while they do not see theirs.</item>
/// <item><b>Radios</b> (<see cref="RadioDef"/>): see <see cref="RadioAccuracy"/>, <see cref="ExtraPings"/> and
/// <see cref="SharesOverwatch"/>.</item>
/// <item><b>Network deck trace</b> (<see cref="HackToolDef.TraceChance"/>): see <see cref="TraceCheck"/>.</item>
/// </list>
/// </summary>
public static class ElectronicWarfare
{
    /// <summary>Charges the unit has left for a reusable item (null for a one-shot item).</summary>
    public static int? ChargesLeft(ScavengeData data, Unit unit, string itemId)
    {
        if (data.Items[itemId].Use is not { Charges: > 0 } use) return null;
        var carried = unit.AllItems.Count(id => id == itemId);
        return Math.Max(0, use.Charges * carried - unit.ChargesUsed.GetValueOrDefault(itemId));
    }

    /// <summary>Whether a use is possible (a flat reusable device is not).</summary>
    internal static ScavengeResult? CheckCharges(ScavengeData data, Unit unit, ItemDef item) =>
        ChargesLeft(data, unit, item.Id) is 0
            ? ScavengeResult.Fail(ScavengeError.NotUsable, $"{item.Name} is flat (no charges left until it is recharged at home).")
            : null;

    /// <summary>Pays for a use: a reusable device spends a charge, a one-shot consumable leaves the pack.</summary>
    internal static void Spend(Unit unit, ItemDef item, UseEffect use)
    {
        if (use.Charges > 0) unit.ChargesUsed[item.Id] = unit.ChargesUsed.GetValueOrDefault(item.Id) + 1;
        else if (use.Consumed) unit.Pack.Remove(item.Id);
    }

    /// <summary>Sets off an EMP at a tile (by hand or from a drone).</summary>
    internal static void Emp(ScavengeSim sim, Unit unit, ItemDef item, EmpDef emp, Pos at)
    {
        var state = sim.State;
        state.Log(new(state.Round, ScavengeEventType.EmpBurst, $"{unit.Name}'s {item.Name} goes off at {at}!", unit.Id, Pos: at,
            Anonymous: $"An EMP goes off at {at}!"));
        var hits = new List<string>();
        foreach (var u in state.Units.Where(u => u.IsActive && u.Pos.Chebyshev(at) <= emp.Radius).OrderBy(u => u.Id).ToList())
        {
            if (u.Kind == UnitKind.Drone)
            {
                Drones.Downed(sim, u, "is knocked out of the air by the EMP");
                hits.Add(u.Name);
            }
            else if (u.Kind == UnitKind.Device && state.Devices.FirstOrDefault(d => d.UnitId == u.Id) is { } device)
            {
                sim.Kill(u, ScavengeEventType.DeviceDisabled, $"The EMP fries the {device.Kind.ToString().ToLowerInvariant()} at {u.Pos}.");
                hits.Add(device.Kind.ToString());
            }
            else if (Network.IsLinked(sim.Data, u))
            {
                var until = state.Round + emp.Rounds - 1;
                u.OffRelayUntilRound = Math.Max(u.OffRelayUntilRound, until);
                u.EmpUntilRound = Math.Max(u.EmpUntilRound, until);
                u.Ap = 0;
                u.Mp = 0;
                Overwatches.Drop(sim, u, "stunned");
                u.Stance = Stance.None;
                if (!u.PlayerControlled) TileStrikes.Lose(sim, u, "stunned");
                hits.Add(u.Name);
            }
            else if (u.Kind == UnitKind.Survivor)
                foreach (var part in Interference.Exposed(sim.Data, u).ToList())
                {
                    u.Body[part].Interfered = true;
                    var name = sim.Data.Items.GetValueOrDefault(u.Body[part].ProstheticItemId ?? "")?.Name ?? "implant";
                    sim.Log(ScavengeEventType.LimbLocked, $"{u.Name}'s {name} locks up in the EMP.", u);
                }
        }
        if (hits.Count > 0)
            state.Log(new(state.Round, ScavengeEventType.EmpBurst, $"EMP: {string.Join(", ", hits)} {(hits.Count == 1 ? "is" : "are")} knocked out.", unit.Id, Pos: at,
                Anonymous: $"The EMP at {at} knocks out {hits.Count} machine{(hits.Count == 1 ? "" : "s")}."));
        sim.MakeNoise(unit, 2);
    }

    /// <summary>A holo-decoy needs a free floor tile.</summary>
    internal static bool CanPlaceDecoy(ScavengeSim sim, Pos at) =>
        sim.Data.Terrain(sim.State.Map[at]).Passable && sim.State.UnitAt(at) is null && sim.State.DeviceAt(at) is null;

    /// <summary>Puts a holo-decoy on a (free) tile.</summary>
    internal static void Decoy(ScavengeSim sim, Unit unit, ItemDef item, DecoyDef decoy, Pos at)
    {
        var state = sim.State;
        var fake = new Unit
        {
            Id = state.NextUnitId++,
            Name = $"{unit.Name}?",
            Kind = UnitKind.Decoy,
            Side = unit.Side,
            PlayerId = unit.PlayerId,
            Pos = at,
            Hp = decoy.Hp,
            MaxHp = decoy.Hp,
            CanBeDowned = false,
            Attributes = unit.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value),
            DecoyUntilRound = state.Round + decoy.Rounds - 1,
        };
        state.Units.Add(fake);
        state.Log(new(state.Round, ScavengeEventType.DecoyDeployed, $"{unit.Name} throws a {item.Name}: a figure flickers up at {at}.", fake.Id, Pos: at));
        Lure(sim, fake, decoy.LureRadius);
        sim.Moved(fake, at);
    }

    /// <summary>Idle enemies within the radius that can hear it come to look (they hunt toward the source's tile).</summary>
    internal static void Lure(ScavengeSim sim, Unit source, int radius) => sim.MakeNoise(source, radius);

    /// <summary>A noise decoy at a tile: idle enemies within its radius go there.</summary>
    internal static void Noise(ScavengeSim sim, Unit unit, ItemDef item, int radius, Pos at)
    {
        var state = sim.State;
        foreach (var u in state.Units.Where(u => u.IsActive && u.Kind == UnitKind.Enemy && u.HostileTo(unit) && u.Pos.Chebyshev(at) <= radius))
        {
            // Husks are dumb: they go for the racket unless their prey is right next to them. The Linked are not fooled
            // while they see their target.
            if (u.Ai.TargetId is { } t && state.FindUnit(t) is { IsActive: true } prey
                && (prey.Pos.Chebyshev(u.Pos) <= 1 || Network.IsLinked(sim.Data, u) && Detection.Spots(sim.Data, state, u, prey)))
                continue;
            u.Ai.Mode = AiMode.Hunting;
            u.Ai.TargetId = null;
            u.Ai.LastKnown = at;
        }
        state.Log(new(state.Round, ScavengeEventType.NoiseDecoy, $"{unit.Name}'s {item.Name} starts rattling at {at}.", unit.Id, Pos: at,
            Anonymous: $"Something starts rattling at {at}."));
    }

    /// <summary>Round start: decoys past their time fade; live ones keep calling.</summary>
    internal static void RoundStarted(ScavengeSim sim)
    {
        var state = sim.State;
        foreach (var d in state.Units.Where(u => u.Kind == UnitKind.Decoy && u.IsActive).OrderBy(u => u.Id).ToList())
        {
            if (d.DecoyUntilRound < state.Round)
            {
                d.Status = UnitStatus.Extracted;
                state.Log(new(state.Round, ScavengeEventType.DecoyGone, $"The decoy at {d.Pos} flickers out.", d.Id, Pos: d.Pos));
                continue;
            }
            Lure(sim, d, 4);
        }
    }

    /// <summary>The best radio among the unit's own items (equipped or packed), or null.</summary>
    public static RadioDef? RadioOf(ScavengeData data, Unit unit) =>
        !unit.IsActive ? null
            : unit.AllItems.Select(id => data.Items.GetValueOrDefault(id)?.Radio).Where(r => r is not null)
                .OrderByDescending(r => r!.AccuracyBonus).ThenByDescending(r => r!.Range).FirstOrDefault();

    /// <summary>
    /// Squad coordination: an attacker is within range of a radio carrier on its side (itself included) and some other
    /// unit of the side carries a radio too (a net needs two ends). The best such radio's bonus counts.
    /// </summary>
    public static double RadioAccuracy(ScavengeSim sim, Unit attacker)
    {
        if (attacker.Kind != UnitKind.Survivor) return 0;
        var carriers = sim.State.Units.Where(u => u.Side == attacker.Side && u.Kind == UnitKind.Survivor)
            .Select(u => (Unit: u, Radio: RadioOf(sim.Data, u))).Where(c => c.Radio is not null).ToList();
        if (carriers.Count < 2) return 0;
        return carriers.Where(c => c.Unit.Pos.Chebyshev(attacker.Pos) <= c.Radio!.Range).Select(c => c.Radio!.AccuracyBonus).DefaultIfEmpty(0).Max();
    }

    /// <summary>Team pings a turn a radio adds.</summary>
    public static int ExtraPings(ScavengeData data, Unit unit) => RadioOf(data, unit)?.ExtraPings ?? 0;

    /// <summary>
    /// A watcher with a squad radio reacts to a hostile its side sees (a teammate calls it in), not only to what it sees itself;
    /// it still needs reach and line of sight to shoot.
    /// </summary>
    public static bool SharesOverwatch(ScavengeSim sim, Unit watcher, Unit target) =>
        RadioOf(sim.Data, watcher) is { ShareOverwatch: true } && Detection.SideSees(sim.Data, sim.State, watcher.Side, target)
        && sim.State.Map.HasLineOfSight(sim.Data, watcher.Pos, target.Pos);

    /// <summary>
    /// A hack action with a Network deck may be traced: the Linked hunt the hacker and the Network's alarm rises (as a failed
    /// hack's alarm, without the shock or lockout). <c>hack_alarm_reduction</c> lowers the chance the same share.
    /// </summary>
    internal static void TraceCheck(ScavengeSim sim, Unit unit, HackToolDef tool)
    {
        if (tool.TraceChance <= 0) return;
        var chance = tool.TraceChance * (1 - Math.Clamp(unit.Hook(PerkHooks.HackAlarmReduction), 0, 1));
        if (sim.Rng.NextDouble() >= chance) return;
        var state = sim.State;
        var linked = state.Units.Where(u => u.IsActive && u.Side == Unit.EnemySide && u.Kind == UnitKind.Enemy
                                            && Network.IsLinked(sim.Data, u) && !Devices.IsJammed(sim, u)).ToList();
        foreach (var u in linked)
        {
            u.Ai.Mode = AiMode.Hunting;
            u.Ai.TargetId = unit.Id;
            u.Ai.LastKnown = unit.Pos;
        }
        state.AlarmRounds++;
        state.Log(new(state.Round, ScavengeEventType.Traced, $"The Network traces {unit.Name}'s deck: every Linked knows where they are!", unit.Id,
            Pos: unit.Pos, Anonymous: "The Network has traced an intruder."));
    }
}
