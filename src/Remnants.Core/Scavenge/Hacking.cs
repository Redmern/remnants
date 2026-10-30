using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Random;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Hackable devices and the Hack action. Tuned in scavenge.json <c>hacking</c> and <c>devices</c>.
/// <list type="bullet">
/// <item>A hack needs a hacking tool carried (equipped or in the pack: <see cref="ItemDef.HackTool"/>), costs
/// <see cref="HackingRules.ApCost"/> less <c>hack_ap_discount</c>, and reaches the tool's range (beyond 1 tile with line of
/// sight). Each action puts the tool's progress into the side's hack; the action that completes the device's
/// <see cref="DeviceKindDef.Work"/> rolls <see cref="Chance"/>.</item>
/// <item>Success hands the device to the hacker's side: a terminal gives up its data (into the pack) and the zone's map and
/// enemy contacts; a camera sees for the side; a turret fires for it; a door lets the side through and keeps everyone else
/// out; a locker spills its loot as a cache; a relay node jams the Network around it and stops reinforcements.</item>
/// <item>Failure: an alarm (noise in <see cref="HackingRules.AlarmRadius"/>, less <c>hack_alarm_reduction</c>; for a device on
/// the Network every Linked is told where the hacker is), a feedback shock to the hacker's arm (halved by
/// <c>field_repair</c>), the progress is lost and the side is locked out for <see cref="HackingRules.LockoutRounds"/>.</item>
/// <item>Override (<c>can_hack_linked</c>): a Linked unit in range turns for <see cref="HackingRules.OverrideRounds"/> rounds,
/// fighting for the hacker's side off the Network. Husks are disconnected: nothing to hack.</item>
/// <item>A Network device is harder (<see cref="HackingRules.NetworkPenalty"/>) unless a jammer hostile to the Network covers
/// it, which also keeps a failure's alarm off the Network.</item>
/// </list>
/// Hacking trains the Hacking skill and earns field XP (<see cref="Unit.FieldXp"/>), which the survivor brings home.
/// </summary>
public static class Hacking
{
    /// <summary>The unit's best hacking tool (pack or equipped, or built into a working cybernetic arm), or null.</summary>
    public static ItemDef? Tool(ScavengeData data, Unit unit) =>
        unit.AllItems.Select(id => data.Items[id]).Where(i => i.Prosthetic is null)
            .Concat(data.FittedParts(unit.Body).Select(f => f.Item))
            .Where(i => i.HackTool is not null)
            .OrderByDescending(i => i.HackTool!.ChanceBonus).ThenByDescending(i => i.HackTool!.Range).ThenBy(i => i.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>AP one hack action costs this unit (perks take some off).</summary>
    public static int ApCost(ScavengeData data, Unit unit)
    {
        var rules = data.Rules.Hacking;
        return Math.Max(rules.MinApCost, rules.ApCost - (int)Math.Round(unit.Hook(PerkHooks.HackApDiscount)));
    }

    /// <summary>The side that controls a device now (a unit device's side is its unit's).</summary>
    public static string ControllerOf(ScavengeState state, ZoneDevice device) =>
        device.UnitId is { } id && state.FindUnit(id) is { } u ? u.Side : device.Controller;

    /// <summary>Whether a device still works (not destroyed; a unit device's unit is still up).</summary>
    public static bool Working(ScavengeState state, ZoneDevice device) =>
        !device.Disabled && (device.UnitId is not { } id || state.FindUnit(id) is { IsActive: true });

    /// <summary>
    /// The chance the hack succeeds when it completes: base + Hacking skill + Intellect + tool + perks − the device's
    /// difficulty − the Network penalty (unless a hostile jammer covers the device). <paramref name="target"/> instead of a
    /// device: Override on a Linked unit.
    /// </summary>
    public static double Chance(ScavengeSim sim, Unit hacker, ZoneDevice? device, Unit? target = null)
    {
        var rules = sim.Data.Rules.Hacking;
        var tool = Tool(sim.Data, hacker)?.HackTool;
        var chance = rules.BaseChance
            + hacker.Get(Skill.Hacking) * rules.PerSkill
            + (hacker.Get(AttributeKind.Intellect) - 50) * rules.PerIntellect
            + (tool?.ChanceBonus ?? 0)
            + hacker.Hook(PerkHooks.HackChanceBonus);
        if (device is not null)
        {
            chance -= sim.Data.Device(device.Kind).Difficulty;
            if (device.Networked && !Devices.JamsTile(sim, device.Pos, Unit.EnemySide)) chance -= rules.NetworkPenalty;
        }
        else if (target is not null)
        {
            chance -= rules.OverrideDifficulty;
            if (!Devices.IsJammed(sim, target)) chance -= rules.NetworkPenalty;
        }
        return Math.Clamp(chance, rules.MinChance, rules.MaxChance);
    }

    /// <summary>The side's hack in progress on the device (null when none).</summary>
    public static HackProgress? ProgressOf(ZoneDevice device, string side) => device.Progress.Find(p => p.Side == side);

    public static bool LockedOut(ScavengeState state, ZoneDevice device, string side) =>
        device.Lockouts.Any(l => l.Side == side && l.UntilRound >= state.Round);

    /// <summary>
    /// Whether <paramref name="mover"/> may not enter the tile because of a device: every device blocks its tile except a door,
    /// which lets its controller's side through. A camera in a wall is on an impassable tile anyway.
    /// </summary>
    public static bool Blocks(ScavengeState state, Unit mover, Pos p)
    {
        if (state.Devices.Count == 0) return false;
        var device = state.DeviceAt(p);
        if (device is null || device.UnitId is not null) return false;
        return device.Kind != DeviceKind.Door || device.Controller != mover.Side;
    }

    /// <summary>Checks and runs one hack action (see the class summary).</summary>
    internal static ScavengeResult Hack(ScavengeSim sim, Unit unit, Hack c)
    {
        var state = sim.State;
        var data = sim.Data;
        var device = c.DeviceId is { } did ? state.FindDevice(did) : null;
        Unit? target = null;
        if (c.TargetId is { } tid)
        {
            target = state.FindUnit(tid);
            if (target is not { OnMap: true } || !Detection.SideSees(data, state, unit.Side, target))
                return ScavengeResult.Fail(ScavengeError.NotVisible, $"{unit.Name}'s side sees no unit {tid}.");
            if (target.DeviceId is { } unitDevice) (device, target) = (state.FindDevice(unitDevice), null);
        }
        if (device is null && target is null)
            return ScavengeResult.Fail(ScavengeError.UnknownDevice, c.DeviceId is { } id ? $"No device {id}." : "Hack what?");
        // A device is only hackable where the side sees it now; an unseen one answers like a missing one.
        if (device is not null && !Visibility.SideVisible(data, state, unit.Side).Contains(state.Map.Index(device.Pos)))
            return ScavengeResult.Fail(ScavengeError.UnknownDevice, $"{unit.Name}'s side sees no device {device.Id}.");

        var name = device is not null ? DeviceName(device) : target!.Name;
        if (device is not null)
        {
            if (!Working(state, device) || device.Used || ControllerOf(state, device) == unit.Side)
                return ScavengeResult.Fail(ScavengeError.AlreadyHacked, $"There is nothing more to hack on the {name}.");
            if (LockedOut(state, device, unit.Side))
                return ScavengeResult.Fail(ScavengeError.LockedOut, $"The {name} has locked your side out for now.");
        }
        else
        {
            if (!unit.HostileTo(target!) || !Network.IsLinked(data, target!) || target!.Kind != UnitKind.Enemy)
                return ScavengeResult.Fail(ScavengeError.NotHackable, $"{name} has nothing to hack into (Husks are disconnected).");
            if (unit.Hook(PerkHooks.CanHackLinked) <= 0)
                return ScavengeResult.Fail(ScavengeError.NotHackable, $"Overriding a Linked needs the Override perk.");
        }
        var tool = Tool(data, unit);
        if (tool?.HackTool is not { } kit) return ScavengeResult.Fail(ScavengeError.NoHackingTool, $"{unit.Name} carries no hacking tool.");
        var at = device?.Pos ?? target!.Pos;
        if (unit.Pos.Chebyshev(at) > kit.Range || (unit.Pos.Chebyshev(at) > 1 && !state.Map.HasLineOfSight(data, unit.Pos, at)))
            return ScavengeResult.Fail(ScavengeError.TooFar, $"The {name} is out of reach of {tool.Name} ({kit.Range} tiles).");
        var cost = ApCost(data, unit);
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
        unit.Ap -= cost;

        if (device is null) return Override(sim, unit, target!, kit);

        var needed = data.Device(device.Kind).Work;
        var done = (ProgressOf(device, unit.Side)?.Done ?? 0) + kit.Progress;
        device.Progress.RemoveAll(p => p.Side == unit.Side);
        if (done < needed)
        {
            device.Progress.Add(new HackProgress(unit.Side, unit.Id, done, needed));
            state.Log(new(state.Round, ScavengeEventType.HackProgress, $"{unit.Name} works on the {name} ({done}/{needed}).",
                unit.Id, Pos: device.Pos, Side: unit.Side));
            return ScavengeResult.Success($"{unit.Name} is hacking the {name} ({done}/{needed}).", device.Id);
        }

        var chance = Chance(sim, unit, device);
        if (sim.Rng.NextDouble() < chance)
        {
            Succeed(sim, unit, device, chance);
            return ScavengeResult.Success($"{unit.Name} hacked the {name}.", device.Id);
        }
        Fail(sim, unit, device.Pos, device.Networked && !Devices.JamsTile(sim, device.Pos, Unit.EnemySide), $"the {name}", chance);
        // A glimpse of what it held: schematics on it become "heard of" for the side.
        foreach (var id in device.Items.Where(id => data.Items.GetValueOrDefault(id)?.Schematic is not null))
        {
            if (!state.HeardSchematics.TryGetValue(unit.Side, out var heard)) state.HeardSchematics[unit.Side] = heard = new(StringComparer.Ordinal);
            heard.Add(id);
        }
        device.Lockouts.RemoveAll(l => l.Side == unit.Side);
        device.Lockouts.Add(new Lockout(unit.Side, state.Round + data.Rules.Hacking.LockoutRounds));
        return ScavengeResult.Success($"{unit.Name} failed to hack the {name}.", device.Id);
    }

    /// <summary>Override: one action, one roll. A turned Linked fights for the side until its time is up.</summary>
    private static ScavengeResult Override(ScavengeSim sim, Unit unit, Unit target, HackToolDef kit)
    {
        _ = kit;
        var state = sim.State;
        var rules = sim.Data.Rules.Hacking;
        var chance = Chance(sim, unit, null, target);
        if (sim.Rng.NextDouble() >= chance)
        {
            Fail(sim, unit, target.Pos, !Devices.IsJammed(sim, target), target.Name, chance);
            target.Ai.Mode = AiMode.Hunting;
            target.Ai.TargetId = unit.Id;
            target.Ai.LastKnown = unit.Pos;
            return ScavengeResult.Success($"{unit.Name} failed to override {target.Name}.", target.Id);
        }
        target.Side = unit.Side;
        target.OverrideUntilRound = state.Round + rules.OverrideRounds - 1;
        target.Ai = new AiMemory();
        target.Stance = Stance.None;
        target.Reactions = 0;
        Trained(unit, rules.SkillOnSuccess, rules.XpOnSuccess);
        state.Log(new(state.Round, ScavengeEventType.Overridden,
            $"{unit.Name} overrides {target.Name} ({chance:P0}): it fights for them for {rules.OverrideRounds} rounds.", unit.Id, target.Id, target.Pos));
        return ScavengeResult.Success($"{unit.Name} overrode {target.Name}.", target.Id);
    }

    private static void Succeed(ScavengeSim sim, Unit unit, ZoneDevice device, double chance)
    {
        var state = sim.State;
        var data = sim.Data;
        var rules = data.Rules.Hacking;
        var name = DeviceName(device);
        device.Controller = unit.Side;
        device.ControllerPlayerId = unit.PlayerId;
        device.Lockouts.Clear();
        device.Progress.Clear();
        Trained(unit, rules.SkillOnSuccess, rules.XpOnSuccess);
        state.Log(new(state.Round, ScavengeEventType.Hacked, $"{unit.Name} hacked the {name} ({chance:P0}).", unit.Id, Pos: device.Pos));

        if (device.UnitId is { } uid && state.FindUnit(uid) is { } machine)
        {
            machine.Side = unit.Side;
            machine.PlayerId = unit.PlayerId;
            machine.Ai = new AiMemory();
            machine.Stance = Stance.None;
        }
        switch (device.Kind)
        {
            case DeviceKind.Terminal:
                device.Used = true;
                unit.Pack.AddRange(device.Items);
                var got = device.Items.Select(id => data.Items[id].Name).ToList();
                device.Items.Clear();
                if (!state.Explored.TryGetValue(unit.Side, out var explored)) state.Explored[unit.Side] = explored = new SortedSet<int>();
                explored.UnionWith(Enumerable.Range(0, state.Map.Width * state.Map.Height));
                foreach (var e in state.Units.Where(e => e.IsActive && e.Kind == UnitKind.Enemy && unit.HostileTo(e)).OrderBy(e => e.Id))
                    if (!Detection.SideSees(data, state, unit.Side, e))
                        Detection.AddContact(data, state, unit.Side, e.Pos, ContactKind.Sensor, e.Id);
                state.Log(new(state.Round, ScavengeEventType.Hacked,
                    $"The terminal gives up the zone map{(got.Count > 0 ? $" and {string.Join(", ", got)}" : "")}.", unit.Id, Pos: device.Pos, Side: unit.Side));
                break;
            case DeviceKind.Locker:
                device.Used = true;
                if (device.Items.Count > 0)
                {
                    var cache = state.Caches.Find(c => c.Pos == device.Pos);
                    if (cache is null) state.Caches.Add(cache = new LootCache { Id = state.NextCacheId++, Pos = device.Pos });
                    cache.Items.AddRange(device.Items);
                    device.Items.Clear();
                }
                break;
        }
    }

    /// <summary>
    /// A failed hack: alarm (noise, contacts for parties in earshot, and over the Network when <paramref name="network"/>),
    /// feedback shock, a little practice.
    /// </summary>
    private static void Fail(ScavengeSim sim, Unit unit, Pos at, bool network, string what, double chance)
    {
        var state = sim.State;
        var data = sim.Data;
        var rules = data.Rules.Hacking;
        var reduction = Math.Clamp(unit.Hook(PerkHooks.HackAlarmReduction), 0, 1);
        var radius = (int)Math.Round(rules.AlarmRadius * (1 - reduction));
        Trained(unit, rules.SkillOnFailure, rules.XpOnFailure);
        state.Log(new(state.Round, ScavengeEventType.HackFailed, $"{unit.Name} failed to hack {what} ({chance:P0}): an alarm goes off!",
            unit.Id, Pos: at, Anonymous: $"An alarm goes off at {at}!"));

        if (radius > 0)
        {
            sim.MakeNoise(unit, radius);
            foreach (var side in Detection.PartySides(state).Where(s => s != unit.Side))
                if (state.Units.Any(u => u.Side == side && u.IsActive && u.Pos.Chebyshev(at) <= radius) && !Detection.SideSees(data, state, side, unit))
                    Detection.AddContact(data, state, side, at, ContactKind.Noise, unit.Id);
        }
        if (network && reduction < 1)
        {
            var linked = state.Units.Where(u => u.IsActive && u.Side == Unit.EnemySide && u.Kind == UnitKind.Enemy
                                                && Network.IsLinked(data, u) && !Devices.IsJammed(sim, u)).ToList();
            foreach (var u in linked)
            {
                u.Ai.Mode = AiMode.Hunting;
                u.Ai.TargetId = unit.Id;
                u.Ai.LastKnown = unit.Pos;
            }
            state.AlarmRounds++;
            state.Log(new(state.Round, ScavengeEventType.Alarm, "The Network is alerted to the intrusion.", Pos: at));
        }

        var shock = rules.ShockDamage[0] + (rules.ShockDamage[1] - rules.ShockDamage[0]) * sim.Rng.NextDouble();
        shock *= 1 - 0.5 * Math.Clamp(unit.Hook(PerkHooks.FieldRepair), 0, 1);
        if (shock >= 1)
        {
            state.Log(new(state.Round, ScavengeEventType.Attacked, $"Feedback shock: {unit.Name} takes {shock:0}.", unit.Id, unit.Id, unit.Pos));
            Combat.ApplyDamage(sim, unit, BodyPart.RightArm, shock);
        }
    }

    private static void Trained(Unit unit, double skill, double xp)
    {
        unit.GainSkill(Skill.Hacking, skill);
        unit.FieldXp += xp;
    }

    /// <summary>Round start: Overrides run out (the Linked goes back to the Network, hunting its last hacker's side).</summary>
    internal static void RoundStarted(ScavengeSim sim)
    {
        var state = sim.State;
        foreach (var d in state.Devices) d.Lockouts.RemoveAll(l => l.UntilRound < state.Round);
        foreach (var u in state.Units.Where(u => u.OverrideUntilRound is { } until && until < state.Round).OrderBy(u => u.Id))
        {
            u.OverrideUntilRound = null;
            if (!u.OnMap) continue;
            u.Side = Unit.EnemySide;
            u.Ai = new AiMemory();
            u.Stance = Stance.None;
            state.Log(new(state.Round, ScavengeEventType.OverrideEnded, $"{u.Name} reconnects to the Network.", u.Id, Pos: u.Pos));
        }
    }

    /// <summary>A device's unit died (shot down): the device is out for good.</summary>
    internal static void UnitKilled(ScavengeSim sim, Unit unit)
    {
        if (unit.DeviceId is not { } id || sim.State.FindDevice(id) is not { } device) return;
        device.Disabled = true;
        device.Progress.Clear();
        sim.State.Log(new(sim.State.Round, ScavengeEventType.DeviceDisabled, $"The {DeviceName(device)} is destroyed.", Pos: device.Pos));
    }

    /// <summary>
    /// After a hostile moved: an enemy camera that spots it for the first time calls it out and points every Linked within
    /// its radius at it (unless the camera is jammed).
    /// </summary>
    internal static void CameraCheck(ScavengeSim sim, Unit mover)
    {
        var state = sim.State;
        if (state.Devices.Count == 0 || !mover.IsActive) return;
        foreach (var device in state.Devices.Where(d => d.Kind == DeviceKind.Camera && d.UnitId is not null).OrderBy(d => d.Id))
        {
            if (state.FindUnit(device.UnitId!.Value) is not { IsActive: true } camera || camera.Side != Unit.EnemySide) continue;
            if (!camera.HostileTo(mover) || Devices.IsJammed(sim, camera) || camera.Ai.TargetId == mover.Id) continue;
            if (!Detection.Spots(sim.Data, state, camera, mover)) continue;
            camera.Ai.Mode = AiMode.Hunting;
            camera.Ai.TargetId = mover.Id;
            camera.Ai.LastKnown = mover.Pos;
            state.Log(new(state.Round, ScavengeEventType.Spotted, $"{camera.Name} spots {mover.Name}.", camera.Id, mover.Id, mover.Pos));
            var radius = sim.Data.Device(DeviceKind.Camera).Radius;
            foreach (var u in state.Units.Where(u => u.IsActive && u.Side == Unit.EnemySide && u.Kind == UnitKind.Enemy
                                                     && Network.IsLinked(sim.Data, u) && u.Pos.Chebyshev(camera.Pos) <= radius
                                                     && !Devices.IsJammed(sim, u)))
            {
                if (u.Ai.Mode == AiMode.Hunting && u.Ai.TargetId is { } t && state.FindUnit(t) is { IsActive: true }) continue;
                u.Ai.Mode = AiMode.Hunting;
                u.Ai.TargetId = mover.Id;
                u.Ai.LastKnown = mover.Pos;
            }
        }
    }

    /// <summary>Whether a hacked relay node controlled by a side other than <paramref name="side"/> jams the tile.</summary>
    public static bool RelayJams(ScavengeState state, ScavengeData data, Pos tile, string side) =>
        state.Devices.Any(d => d.Kind == DeviceKind.Relay && !d.Disabled && d.Hacked && d.Controller != side
                               && d.Pos.Chebyshev(tile) <= data.Device(DeviceKind.Relay).Radius);

    /// <summary>Whether any relay node is hacked: the zone's uplink is down, so no reinforcements come.</summary>
    public static bool UplinkDown(ScavengeState state) => state.Devices.Any(d => d.Kind == DeviceKind.Relay && d.Hacked && !d.Disabled);

    public static string DeviceName(ZoneDevice device) => device.Kind switch
    {
        DeviceKind.Relay => "relay node",
        DeviceKind.Door => "blast door",
        _ => device.Kind.ToString().ToLowerInvariant(),
    };

    // ---- Placement -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Puts a device on a tile for the Network (tests and <see cref="PlaceZoneDevices"/>): a camera or turret gets its unit
    /// (from the kind's enemy definition), a terminal or locker its loot.
    /// </summary>
    public static ZoneDevice Place(ScavengeData data, ScavengeState state, DeviceKind kind, Pos pos, IEnumerable<string>? items = null)
    {
        var def = data.Device(kind);
        var device = new ZoneDevice { Id = state.NextDeviceId++, Kind = kind, Pos = pos, Networked = def.Networked, Items = items?.ToList() ?? new() };
        if (def.Enemy is { } enemyId)
        {
            var unit = NewScavenge.EnemyUnit(data, state, data.Enemies[enemyId], pos);
            unit.Kind = UnitKind.Device;
            unit.DeviceId = device.Id;
            state.Units.Add(unit);
            device.UnitId = unit.Id;
        }
        state.Devices.Add(device);
        return device;
    }

    /// <summary>
    /// Places the zone's devices with their own RNG (so the rest of the generation is unchanged): doors in room doorways,
    /// cameras in walls next to open floor, the rest on floor tiles; nothing that blocks a tile may cut off part of the
    /// map, and nothing goes near the spawn edge.
    /// </summary>
    internal static void PlaceZoneDevices(ScavengeData data, ScavengeState state, ZoneDef zone, ulong seed, IReadOnlyList<Pos> anchors,
        IReadOnlyCollection<Pos> reachable)
    {
        if (zone.Devices.Count == 0) return;
        var rng = new DeterministicRng(seed ^ 0x5EED_DE71_CE00_0001UL);
        var map = state.Map;
        var open = reachable.ToHashSet();
        var start = anchors.Count > 0 ? anchors[0] : open.OrderBy(map.Index).First();
        bool Taken(Pos p) => state.UnitAt(p) is not null || state.Caches.Any(c => c.Pos == p) || state.DeviceAt(p) is not null || map.IsExit(p);
        bool Deep(Pos p) => MapGenerator.Depth(map, zone.SpawnEdge, p) >= 4 && anchors.All(a => a.Chebyshev(p) >= 5);
        bool Wall(Pos p) => map.InBounds(p) && map[p] == Terrain.Wall;
        bool Doorway(Pos p) => map[p] == Terrain.Floor && ((Wall(p + new Pos(-1, 0)) && Wall(p + new Pos(1, 0))) || (Wall(p + new Pos(0, -1)) && Wall(p + new Pos(0, 1))));

        var blocked = new HashSet<Pos>();
        bool KeepsMapWhole(Pos p)
        {
            blocked.Add(p);
            var seen = new HashSet<Pos> { start };
            var queue = new Queue<Pos>([start]);
            while (queue.TryDequeue(out var q))
                foreach (var d in Pos.Orthogonal)
                {
                    var n = q + d;
                    if (open.Contains(n) && !blocked.Contains(n) && seen.Add(n)) queue.Enqueue(n);
                }
            var whole = seen.Count == open.Count(t => !blocked.Contains(t));
            if (!whole) blocked.Remove(p);
            return whole;
        }

        var lootTable = data.Items.Values.Where(i => i.LootWeight > 0 && i.Tier <= zone.LootTier).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        foreach (var kind in Enum.GetValues<DeviceKind>())
        {
            if (!zone.Devices.TryGetValue(kind, out var range)) continue;
            var count = rng.NextInclusive(range[0], range[1]);
            var candidates = kind switch
            {
                DeviceKind.Camera => map.AllPositions.Where(p => Wall(p) && Pos.Orthogonal.Any(d => open.Contains(p + d) && map[p + d] == Terrain.Floor)),
                DeviceKind.Door => open.Where(Doorway),
                _ => open.Where(p => map[p] == Terrain.Floor && !Doorway(p)),
            };
            var list = candidates.Where(p => Deep(p) && !Taken(p)).OrderBy(map.Index).ToList();
            for (var placed = 0; placed < count && list.Count > 0;)
            {
                var p = list[rng.Next(0, list.Count)];
                list.Remove(p);
                if (Taken(p) || state.Devices.Any(d => d.Pos.Chebyshev(p) < 3)) continue;
                if (kind != DeviceKind.Camera && kind != DeviceKind.Turret && !KeepsMapWhole(p)) continue;
                if (kind == DeviceKind.Turret && !KeepsMapWhole(p)) continue;
                var def = data.Device(kind);
                var items = new List<string>();
                var loot = def.Loot.Where(l => data.Items[l.Id].Tier <= zone.LootTier).OrderBy(l => l.Id, StringComparer.Ordinal).ToList();
                var n = rng.NextInclusive(def.LootCount[0], def.LootCount[1]);
                for (var i = 0; i < n; i++)
                {
                    if (def.ZoneLoot && lootTable.Count > 0 && (loot.Count == 0 || rng.NextDouble() < 0.5))
                        items.Add(NewScavenge.PickWeighted(rng, lootTable, it => it.LootWeight).Id);
                    else if (loot.Count > 0)
                        items.Add(NewScavenge.PickWeighted(rng, loot, l => l.Weight).Id);
                }
                Place(data, state, kind, p, items);
                placed++;
            }
        }
    }
}
