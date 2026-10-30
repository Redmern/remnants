using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Playtest;

/// <summary>
/// Plays a story mission's party by its objectives: go to the marker, pick up the item, fetch a hacking kit and hack
/// the tagged device, treat and escort the tagged NPC, hold near the defended structure at home, then walk out with
/// everyone. Fights what it sees in reach, patches up the hurt, answers prompts with a fixed option. Deterministic.
/// </summary>
public static class StoryBot
{
    private const int MaxCommands = 20000;
    private const int MaxRounds = 60;

    /// <param name="prompt">Which option to pick on a prompt (index, clamped): 0 = the first (talk), 1 = the second (fight).</param>
    public static (ExtractionResult Result, int Rounds) Play(ScavengeSim sim, int playerId, int prompt = 0, Action<string>? trace = null)
    {
        _leaveAll = false;
        var seen = sim.State.Events.Count;
        void Flush()
        {
            if (trace is null) return;
            var log = sim.State.Events;
            foreach (var e in log.Skip(Math.Max(0, Math.Min(seen, log.Count)))) trace($"r{e.Round,-3} {e.Type,-15} {e.Message}");
            seen = log.Count;
        }
        for (var n = 0; n < MaxCommands && !sim.State.Finished && sim.State.FindParty(playerId) is { Done: false }; n++)
        {
            var u = sim.State.CurrentUnit;
            if (u is null || u.PlayerId != playerId) break;
            if (sim.State.Round > MaxRounds) LeaveNow(sim, u);
            var command = Next(sim, u, prompt);
            var result = sim.Execute(command);
            if (!result.Ok)
            {
                trace?.Invoke($"     ! {command.GetType().Name}: {result.Message}");
                sim.Execute(new EndTurn(playerId, u.Id));
            }
            Flush();
        }
        Flush();
        return (sim.ResultFor(playerId), sim.State.Round);
    }

    private static bool _leaveAll;

    /// <summary>The run has dragged on: everyone heads for the exit (leaving early is "not done yet", not a fail).</summary>
    private static void LeaveNow(ScavengeSim sim, Unit u) => _leaveAll = true;

    private static IScavengeCommand Next(ScavengeSim sim, Unit u, int prompt)
    {
        var state = sim.State;
        var run = state.Mission!;
        var pid = u.PlayerId!.Value;
        var end = new EndTurn(pid, u.Id);
        if (run.Pending is { } p) return new ChooseOption(pid, u.Id, p.Id, p.Options[Math.Clamp(prompt, 0, p.Options.Count - 1)].Id);

        var profile = sim.Profile(u);
        var side = u.Side;
        var ours = state.Units.Where(x => x.Side == side && x.OnMap && x.Kind is UnitKind.Survivor or UnitKind.Npc && x.Tag != "mast").ToList();

        // Patch up: a downed friend in reach, or yourself when badly hurt.
        foreach (var friend in ours.Where(f => f.Status == UnitStatus.Downed && f.Pos.Chebyshev(u.Pos) <= 1).OrderBy(f => f.Id))
            if (Heal(sim, u, friend, revive: true) is { } c) return c;
        if (u.Hp < u.MaxHp * 0.4 && Heal(sim, u, u, revive: false) is { } self) return self;

        // Fight what is in reach.
        var target = state.Units.Where(t => t.IsActive && u.HostileTo(t) && Detection.SideSees(sim.Data, state, side, t))
            .SelectMany(t => profile.Actions.Where(a => a.ApCost <= u.Ap && Combat.CheckReach(sim.Data, state.Map, u.Pos, a, t.Pos) == ScavengeError.None
                                                        && Drones.CanStrike(a, t)).Select(a => (t, a)))
            .OrderByDescending(x => Combat.ChanceToHit(sim, u, x.a, x.t) * (x.a.MinDamage + x.a.MaxDamage) / 2)
            .ThenBy(x => x.t.Id).FirstOrDefault();
        if (target.t is not null) return new Attack(pid, u.Id, target.t.Id, target.a.Id);

        // Objectives, in order.
        Pos? goal = null;
        var leave = _leaveAll || run.Outcome == MissionOutcome.Failed;
        if (!leave)
        {
            foreach (var o in run.Def.Objectives.Where(o => !o.Optional))
            {
                var s = run.Find(o.Id)!;
                if (s.Status != ObjectiveStatus.Active) continue;
                switch (o.Kind)
                {
                    case ObjectiveKind.Reach:
                        goal = run.Marker(o.Marker).FirstOrDefault();
                        break;
                    case ObjectiveKind.Retrieve when s.Progress == 0:
                        var cache = state.Caches.Where(c => c.Items.Contains(o.Item!)).OrderBy(c => c.Pos.Chebyshev(u.Pos)).FirstOrDefault();
                        if (cache is null) continue;
                        if (cache.Pos.Chebyshev(u.Pos) <= 1 && sim.Data.Rules.Turn.PickUpApCost <= u.Ap) return new PickUp(pid, u.Id, cache.Id, o.Item!);
                        goal = cache.Pos;
                        break;
                    case ObjectiveKind.Hack:
                        if (state.Devices.Find(d => d.Tag == o.Tag) is not { } device) continue;
                        if (Hacking.Tool(sim.Data, u) is null)
                        {
                            if (ours.Any(x => x.Kind == UnitKind.Survivor && Hacking.Tool(sim.Data, x) is not null)) { goal = device.Pos; break; }
                            var kit = state.Caches.Where(c => c.Items.Any(i => sim.Data.Items[i].HackTool is not null)).OrderBy(c => c.Pos.Chebyshev(u.Pos)).FirstOrDefault();
                            if (kit is null) continue;
                            if (kit.Pos.Chebyshev(u.Pos) <= 1 && sim.Data.Rules.Turn.PickUpApCost <= u.Ap)
                                return new PickUp(pid, u.Id, kit.Id, kit.Items.First(i => sim.Data.Items[i].HackTool is not null));
                            goal = kit.Pos;
                            break;
                        }
                        if (device.Pos.Chebyshev(u.Pos) <= 1)
                            return Hacking.ApCost(sim.Data, u) <= u.Ap && !Hacking.LockedOut(state, device, side) ? new Hack(pid, u.Id, device.Id) : end;
                        goal = device.Pos;
                        break;
                    case ObjectiveKind.Aid:
                        if (state.Units.Find(x => x.Tag == o.Tag && x.OnMap) is not { } hurt) continue;
                        if (hurt.Pos.Chebyshev(u.Pos) <= 1 && Heal(sim, u, hurt, revive: true) is { } aid) return aid;
                        goal = hurt.Pos;
                        break;
                    case ObjectiveKind.Survive or ObjectiveKind.Defend when run.Def.AtHome:
                        var guard = state.Units.Find(x => x.Tag == run.Def.Objectives.FirstOrDefault(d => d.Kind == ObjectiveKind.Defend)?.Tag) ?? u;
                        var threat = state.Units.Where(t => t.IsActive && u.HostileTo(t) && Detection.SideSees(sim.Data, state, side, t))
                            .OrderBy(t => t.Pos.Chebyshev(guard.Pos)).ThenBy(t => t.Id).FirstOrDefault();
                        // Hold close to what we defend; step out only to meet what comes at it.
                        goal = threat is not null && threat.Pos.Chebyshev(guard.Pos) <= 6 ? threat.Pos
                            : run.Raised.Where(r => state.UnitAt(r) is null || state.UnitAt(r) == u).OrderBy(r => r.Chebyshev(u.Pos)).Cast<Pos?>().FirstOrDefault()
                              ?? guard.Pos;
                        break;
                    default:
                        continue;
                }
                if (goal is not null) break;
            }
            if (goal is null && run.Def.AtHome) return u.Ap >= 1 ? new SetStance(pid, u.Id, Stance.Overwatch) : end;
            if (goal is null) leave = true;
        }

        // Never walk out on a friend in a fight: go to the nearest one with a hostile close by.
        if (leave && !_leaveAll)
        {
            var pressed = ours.Where(f => f.Id != u.Id && f.IsActive && f.Kind == UnitKind.Survivor
                                          && state.Units.Any(t => t.IsActive && f.HostileTo(t) && t.Pos.Chebyshev(f.Pos) <= 2))
                .OrderBy(f => f.Pos.Chebyshev(u.Pos)).FirstOrDefault();
            if (pressed is not null && pressed.Pos.Chebyshev(u.Pos) > 1 && Toward(sim, u, pressed.Pos) is { } help) return help;
        }
        if (leave)
        {
            if (state.Map.IsExit(u.Pos))
            {
                // Wait for followers to catch up before stepping out.
                var lagging = state.Units.Any(f => f.FollowingId == u.Id && f.IsActive && f.Pos.Chebyshev(u.Pos) > sim.Data.Rules.EscortRadius);
                return lagging ? end : new Extract(pid, u.Id);
            }
            // The nearest exit that can be walked to now (a bystander may stand in a doorway).
            foreach (var exit in state.Map.Exits.OrderBy(e => e.Chebyshev(u.Pos)).ThenBy(state.Map.Index))
                if (Toward(sim, u, exit, exact: true) is { } step) return step;
            return end;
        }

        return Toward(sim, u, goal!.Value, exact: leave) ?? (run.Def.AtHome && u.Ap >= 1 ? new SetStance(pid, u.Id, Stance.Overwatch) : end);
    }

    /// <summary>A move to the affordable reachable tile closest (walking) to a tile next to the goal, if that gets closer.</summary>
    private static IScavengeCommand? Toward(ScavengeSim sim, Unit u, Pos goal, bool exact = false)
    {
        var state = sim.State;
        var profile = sim.Profile(u).Movement;
        var targets = (exact ? [] : Pos.Neighbours8.Select(d => goal + d)).Prepend(goal)
            .Where(p => state.Map.InBounds(p) && sim.Data.Terrain(state.Map[p]).Passable && state.DeviceAt(p) is null
                        && (state.UnitAt(p) is null || state.UnitAt(p) == u))
            .OrderBy(p => p == goal ? 0 : 1).ThenBy(p => p.Chebyshev(u.Pos)).ThenBy(state.Map.Index).ToList();
        if (targets.Contains(u.Pos)) return null;
        foreach (var t in targets)
        {
            var field = Movement.DistanceField(state, u, profile, t);
            if (!field.TryGetValue(u.Pos, out var here)) continue;
            var options = sim.Reachable(u).Where(kv => kv.Value <= u.Ap && field.ContainsKey(kv.Key) && field[kv.Key] < here)
                .OrderBy(kv => field[kv.Key]).ThenBy(kv => kv.Value).ThenBy(kv => state.Map.Index(kv.Key)).ToList();
            return options.Count == 0 ? null : new Move(u.PlayerId!.Value, u.Id, options[0].Key);
        }
        return null;
    }

    /// <summary>Treat with what is in the pack: a med-kit first for the downed, a bandage first otherwise.</summary>
    private static IScavengeCommand? Heal(ScavengeSim sim, Unit u, Unit target, bool revive)
    {
        var order = revive ? new[] { "med_kit", "bandage" } : ["bandage", "med_kit"];
        foreach (var id in order)
        {
            var item = u.Pack.FirstOrDefault(p => sim.Data.Items[p].DefinedId == id);
            if (item is null || sim.Data.Items[item].Use is not { } use || use.ApCost > u.Ap) continue;
            if (target.Status == UnitStatus.Downed && !use.Stabilize) continue;
            if (target.Status != UnitStatus.Downed && use.Heal <= 0) continue;
            return new UseItem(u.PlayerId!.Value, u.Id, item, target.Id);
        }
        return null;
    }
}
