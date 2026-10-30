namespace Remnants.Core.Scavenge;

/// <summary>Answers a field prompt (<see cref="PromptDef"/>) of a story mission.</summary>
public sealed record ChooseOption(int PlayerId, int UnitId, string PromptId, string OptionId) : IScavengeCommand;

/// <summary>
/// The rules of a story mission in the field (<see cref="ScavengeState.Mission"/>): objectives pass and fail, triggers
/// fire once each, chatter is said, prompts pause for the player, and a mission at home ends the run when it is done or
/// failed. Story missions are played by one party (the host's; see STORY.md "Co-op"): its side is the one that counts.
/// Everything is deterministic: no rolls, and triggers and objectives are checked in definition order.
/// </summary>
public static class MissionRules
{
    private const int MaxPasses = 8;

    /// <summary>The player whose mission it is (the first party).</summary>
    public static int? HostPlayerId(ScavengeState state) => state.Parties.Count > 0 ? state.Parties[0].PlayerId : null;

    public static string PartySide(ScavengeState state) => state.Parties.Count > 0 ? state.Parties[0].Side : "";

    /// <summary>Fresh objective states (hidden ones inactive) for a new run.</summary>
    public static void Init(MissionRun run)
    {
        run.Objectives = run.Def.Objectives.Select(o => new ObjectiveState
        {
            Id = o.Id, Status = o.Hidden ? ObjectiveStatus.Inactive : ObjectiveStatus.Active, StartRound = 1,
        }).ToList();
    }

    internal static void Started(ScavengeSim sim)
    {
        if (sim.State.Mission is not { } run) return;
        foreach (var u in sim.State.Units) u.Raised = run.Raised.Contains(u.Pos);
        Fire(sim, t => t.When.Kind == TriggerKind.Start);
    }

    internal static void RoundStarted(ScavengeSim sim)
    {
        if (sim.State.Mission is not { } run) return;
        var round = sim.State.Round;
        Fire(sim, t => t.When.Kind == TriggerKind.Round && t.When.Round == round);
        foreach (var (o, s) in Pairs(run).Where(p => p.Def.Kind == ObjectiveKind.Survive && p.State.Status == ObjectiveStatus.Active))
        {
            s.Progress = Math.Max(0, round - s.StartRound);
            if (s.Progress >= o.Count) Set(sim, o, s, ObjectiveStatus.Done);
        }
        Update(sim);
    }

    internal static void Moved(ScavengeSim sim, Unit unit)
    {
        if (sim.State.Mission is not { } run) return;
        unit.Raised = run.Raised.Contains(unit.Pos);
    }

    internal static void PickedUp(ScavengeSim sim, Unit unit, string itemId)
    {
        if (sim.State.Mission is null || unit.Side != PartySide(sim.State)) return;
        Fire(sim, t => t.When.Kind == TriggerKind.PickedUp && t.When.Item == itemId);
    }

    internal static void Alarm(ScavengeSim sim)
    {
        if (sim.State.Mission is null) return;
        Fire(sim, t => t.When.Kind == TriggerKind.Alarm);
    }

    /// <summary>A healing item used on a unit by the party (for <see cref="ObjectiveKind.Aid"/>).</summary>
    internal static void Treated(ScavengeSim sim, Unit by, Unit target)
    {
        if (sim.State.Mission is not { } run || by.Side != PartySide(sim.State)) return;
        run.Aided.Add(target.Id);
    }

    /// <summary>While a prompt waits, only its answer goes through.</summary>
    internal static ScavengeResult? Blocked(ScavengeSim sim, IScavengeCommand command) =>
        sim.State.Mission?.Pending is { } p && command is not ChooseOption
            ? ScavengeResult.Fail(ScavengeError.ChoicePending, $"Answer first: {p.Text}")
            : null;

    internal static ScavengeResult Choose(ScavengeSim sim, Unit unit, ChooseOption c)
    {
        if (sim.State.Mission is not { Pending: { } prompt } run || prompt.Id != c.PromptId)
            return ScavengeResult.Fail(ScavengeError.NoPrompt, "Nothing to answer.");
        if (unit.PlayerId != HostPlayerId(sim.State)) return ScavengeResult.Fail(ScavengeError.NoPrompt, "This is not your mission's choice.");
        var option = prompt.Options.Find(o => o.Id == c.OptionId);
        if (option is null) return ScavengeResult.Fail(ScavengeError.NoPrompt, $"No option '{c.OptionId}'.");
        run.Pending = null;
        run.Choices[prompt.Id] = option.Id;
        sim.State.Log(new(sim.State.Round, ScavengeEventType.MissionPrompt, $"{unit.Name}: \"{option.Text}\"", unit.Id, Side: PartySide(sim.State)));
        var key = $"{prompt.Id}:{option.Id}";
        Fire(sim, t => t.When.Kind == TriggerKind.Chosen && t.When.Choice == key);
        Update(sim);
        return ScavengeResult.Success($"Chose {option.Text}.");
    }

    /// <summary>
    /// Re-checks every objective and condition trigger until nothing changes: reach, hack, aid, kills, the defended
    /// and escorted still alive, stealth, carried items, area and sight triggers; then whether the mission is decided.
    /// </summary>
    internal static void Update(ScavengeSim sim)
    {
        if (sim.State.Mission is not { } run || sim.State.Finished) return;
        var state = sim.State;
        var side = PartySide(state);
        var survivors = state.Units.Where(u => u.Side == side && u.IsActive && u.Kind == UnitKind.Survivor).ToList();

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var changed = false;
            foreach (var (o, s) in Pairs(run).Where(p => p.State.Status == ObjectiveStatus.Active).ToList())
            {
                var tagged = state.Units.Where(u => o.Tag is not null && u.Tag == o.Tag).ToList();
                ObjectiveStatus? to = null;
                switch (o.Kind)
                {
                    case ObjectiveKind.Reach:
                        if (run.Marker(o.Marker).Any(m => survivors.Any(u => u.Pos.Chebyshev(m) <= o.Radius))) to = ObjectiveStatus.Done;
                        break;
                    case ObjectiveKind.Hack:
                        var device = state.Devices.Find(d => d.Tag == o.Tag);
                        if (device is { Hacked: true } && device.Controller == side) to = ObjectiveStatus.Done;
                        else if (device is null or { Disabled: true }) to = ObjectiveStatus.Failed;
                        break;
                    case ObjectiveKind.Aid:
                        if (tagged.Any(u => run.Aided.Contains(u.Id))) to = ObjectiveStatus.Done;
                        else if (tagged.Count > 0 && tagged.All(u => u.Status == UnitStatus.Dead)) to = ObjectiveStatus.Failed;
                        break;
                    case ObjectiveKind.Retrieve:
                        s.Progress = state.Units.Any(u => u.Side == side && u.Kind == UnitKind.Survivor && u.Status != UnitStatus.Dead && u.Pack.Contains(o.Item!)) ? 1 : 0;
                        break;
                    case ObjectiveKind.Escort:
                    case ObjectiveKind.Defend:
                        if (tagged.Count > 0 && tagged.All(u => u.Status == UnitStatus.Dead)) to = ObjectiveStatus.Failed;
                        break;
                    case ObjectiveKind.KillTag:
                        s.Progress = tagged.Count(u => u.Status == UnitStatus.Dead);
                        if (s.Progress >= o.Count) to = ObjectiveStatus.Done;
                        break;
                    case ObjectiveKind.Undetected:
                        if (Detected(sim, side)) to = ObjectiveStatus.Failed;
                        break;
                }
                if (to is { } status)
                {
                    Set(sim, o, s, status);
                    changed = true;
                }
            }

            changed |= Fire(sim, t => t.When.Kind switch
            {
                TriggerKind.Enter => run.Marker(t.When.Marker).Any(m => survivors.Any(u => u.Pos.Chebyshev(m) <= t.When.Radius)),
                TriggerKind.Sees => state.Units.Any(u => u.Tag == t.When.Tag && u.OnMap && Detection.SideSees(sim.Data, state, side, u)),
                TriggerKind.ObjectiveDone => run.Find(t.When.Objective ?? "")?.Status == ObjectiveStatus.Done,
                TriggerKind.ObjectiveFailed => run.Find(t.When.Objective ?? "")?.Status == ObjectiveStatus.Failed,
                _ => false,
            });
            if (!changed) break;
        }
        Decide(sim);
    }

    /// <summary>A required objective failed with <see cref="ObjectiveDef.FailsMission"/>: failed. At home, done once every required objective is.</summary>
    private static void Decide(ScavengeSim sim)
    {
        var run = sim.State.Mission!;
        if (run.Outcome != MissionOutcome.Incomplete) return;
        if (Pairs(run).Any(p => p.Def.FailsMission && p.State.Status == ObjectiveStatus.Failed))
        {
            run.Outcome = MissionOutcome.Failed;
            sim.State.Log(new(sim.State.Round, ScavengeEventType.MissionEnded, $"Mission failed: {run.Def.Name}.", Side: PartySide(sim.State)));
        }
        else if (run.Def.AtHome && Pairs(run).Where(p => !p.Def.Optional).All(p => p.State.Status == ObjectiveStatus.Done
                     || p.Def.Kind is ObjectiveKind.Defend or ObjectiveKind.Undetected && p.State.Status == ObjectiveStatus.Active))
        {
            run.Outcome = MissionOutcome.Done;
            foreach (var (o, s) in Pairs(run).Where(p => p.State.Status == ObjectiveStatus.Active && p.Def.Kind is ObjectiveKind.Defend or ObjectiveKind.Undetected))
                Set(sim, o, s, ObjectiveStatus.Done);
            sim.State.Log(new(sim.State.Round, ScavengeEventType.MissionEnded, $"Mission complete: {run.Def.Name}.", Side: PartySide(sim.State)));
        }
        else return;

        if (!run.Def.AtHome) return;
        // At home the fight is over: everyone still on the map is home already, downed and carried included.
        var host = HostPlayerId(sim.State);
        foreach (var u in sim.State.Units.Where(u => u.PlayerId == host && u.OnMap))
        {
            u.Status = UnitStatus.Extracted;
            u.CarriedById = null;
            u.CarryingId = null;
        }
        sim.UpdateParties();
    }

    /// <summary>Any hostile (the Linked, a camera, a Husk) picks out a unit of the side.</summary>
    private static bool Detected(ScavengeSim sim, string side)
    {
        var state = sim.State;
        var ours = state.Units.Where(u => u.Side == side && u.IsActive && u.Kind is UnitKind.Survivor or UnitKind.Npc).ToList();
        return state.Units.Any(e => e.IsActive && e.Kind is UnitKind.Enemy or UnitKind.Device && e.Side != Unit.NeutralSide
                                    && ours.Any(u => e.HostileTo(u) && Detection.Spots(sim.Data, state, e, u)));
    }

    private static IEnumerable<(ObjectiveDef Def, ObjectiveState State)> Pairs(MissionRun run) =>
        run.Def.Objectives.Select(o => (o, run.Find(o.Id)!)).Where(p => p.Item2 is not null);

    private static void Set(ScavengeSim sim, ObjectiveDef o, ObjectiveState s, ObjectiveStatus status)
    {
        if (s.Status == status) return;
        s.Status = status;
        if (status == ObjectiveStatus.Active) s.StartRound = sim.State.Round;
        if (status == ObjectiveStatus.Done && o.Kind == ObjectiveKind.Survive) s.Progress = o.Count;
        var word = status switch { ObjectiveStatus.Done => "done", ObjectiveStatus.Failed => "failed", _ => "new" };
        sim.State.Log(new(sim.State.Round, ScavengeEventType.ObjectiveUpdated, $"Objective {word}: {o.Text}", Side: PartySide(sim.State)));
    }

    /// <summary>Fires every not-yet-fired trigger matching the filter, in order. Returns whether any fired.</summary>
    private static bool Fire(ScavengeSim sim, Func<TriggerDef, bool> match)
    {
        var run = sim.State.Mission!;
        var any = false;
        foreach (var t in run.Def.Triggers)
        {
            if (run.Fired.Contains(t.Id) || !match(t)) continue;
            run.Fired.Add(t.Id);
            any = true;
            foreach (var a in t.Actions) Do(sim, run, a);
        }
        return any;
    }

    private static void Do(ScavengeSim sim, MissionRun run, TriggerAction a)
    {
        var state = sim.State;
        var side = PartySide(state);
        IEnumerable<Unit> Tagged() => state.Units.Where(u => u.Tag == a.Tag && u.OnMap).OrderBy(u => u.Id).ToList();
        switch (a.Kind)
        {
            case TriggerActionKind.Say:
                Say(sim, a.Speaker ?? "", a.Text ?? "");
                break;
            case TriggerActionKind.Activate:
            case TriggerActionKind.Complete:
            case TriggerActionKind.Fail:
                if (run.DefOf(a.Objective ?? "") is { } o && run.Find(o.Id) is { } s && s.Status is ObjectiveStatus.Inactive or ObjectiveStatus.Active)
                    Set(sim, o, s, a.Kind switch
                    {
                        TriggerActionKind.Activate => ObjectiveStatus.Active,
                        TriggerActionKind.Complete => ObjectiveStatus.Done,
                        _ => ObjectiveStatus.Failed,
                    });
                break;
            case TriggerActionKind.Flag:
                run.Flags[a.Flag!] = (a.Add ? run.Flags.GetValueOrDefault(a.Flag!) : 0) + a.Value;
                break;
            case TriggerActionKind.Spawn:
                Spawn(sim, run, a);
                break;
            case TriggerActionKind.SetSide:
                foreach (var u in Tagged())
                {
                    u.Side = SideName(state, a.Side, a.Tag);
                    u.PlayerId = u.Side == side && u.Kind != UnitKind.Enemy ? HostPlayerId(state) : null;
                    u.Ai.Mode = AiMode.Idle;
                }
                break;
            case TriggerActionKind.Follow:
                foreach (var u in Tagged())
                {
                    var leader = state.Units.Where(l => l.IsActive && l.PlayerControlled && l.Side == side)
                        .OrderBy(l => l.Pos.Chebyshev(u.Pos)).ThenBy(l => l.Id).FirstOrDefault();
                    if (leader is null) continue;
                    u.Side = side;
                    u.PlayerId = leader.PlayerId;
                    u.FollowingId = leader.Id;
                    u.NoRecruit = false;
                    state.Log(new(state.Round, ScavengeEventType.Recruited, $"{u.Name} follows {leader.Name}.", leader.Id, u.Id, u.Pos));
                }
                break;
            case TriggerActionKind.Prompt:
                run.Pending = a.Prompt;
                state.Log(new(state.Round, ScavengeEventType.MissionPrompt, $"{a.Prompt!.Speaker}: {a.Prompt.Text}", Side: side));
                break;
            case TriggerActionKind.Split:
                if (state.Devices.Find(d => d.Tag == a.Tag) is { Used: false } dev && dev.Items.Count > 1)
                {
                    var keep = (dev.Items.Count + 1) / 2;
                    dev.Items.RemoveRange(keep, dev.Items.Count - keep);
                }
                break;
            case TriggerActionKind.Wake:
                var goal = run.Marker(a.Goal).Cast<Pos?>().FirstOrDefault();
                foreach (var u in Tagged().Where(u => u.Ai.Mode == AiMode.Idle))
                {
                    u.Ai.Mode = AiMode.Hunting;
                    u.Ai.LastKnown = goal ?? u.Pos;
                }
                break;
        }
    }

    private static string SideName(ScavengeState state, string? name, string? tag) => name switch
    {
        "party" => PartySide(state),
        "neutral" => Unit.NeutralSide,
        "hostile" => $"band:{tag}",
        null or "enemies" => Unit.EnemySide,
        _ => name,
    };

    public static void Say(ScavengeSim sim, string speaker, string text)
    {
        var state = sim.State;
        state.Mission?.Chatter.Add(new(state.Round, speaker, text));
        state.Log(new(state.Round, ScavengeEventType.Chatter, $"{speaker}: {text}", Side: PartySide(state)));
    }

    /// <summary>Puts <see cref="TriggerAction.Count"/> enemies on the marker's tiles (or the nearest free floor), hunting toward the goal if set.</summary>
    private static void Spawn(ScavengeSim sim, MissionRun run, TriggerAction a)
    {
        var state = sim.State;
        if (a.Enemy is null || !sim.Data.Enemies.TryGetValue(a.Enemy, out var def)) return;
        var anchors = run.Marker(a.Marker);
        if (anchors.Count == 0) return;
        var goal = run.Marker(a.Goal).Cast<Pos?>().FirstOrDefault();
        for (var i = 0; i < a.Count; i++)
        {
            var anchor = anchors[i % anchors.Count];
            var spot = state.Map.AllPositions
                .Where(p => p.Chebyshev(anchor) <= 3 && sim.Data.Terrain(state.Map[p]).Passable && !state.Map.IsExit(p)
                            && state.UnitAt(p) is null && state.DeviceAt(p) is null)
                .OrderBy(p => p.Chebyshev(anchor)).ThenBy(state.Map.Index).Cast<Pos?>().FirstOrDefault();
            if (spot is not { } at) continue;
            var unit = NewScavenge.EnemyUnit(sim.Data, state, def, at);
            unit.Tag = a.Tag;
            unit.Side = SideName(state, a.Side, a.Tag);
            if (goal is { } g)
            {
                unit.Ai.Mode = AiMode.Hunting;
                unit.Ai.LastKnown = g;
            }
            state.Units.Add(unit);
            state.Log(new(state.Round, ScavengeEventType.Reinforced, $"{unit.Name} appears.", unit.Id, Pos: at));
        }
    }

    /// <summary>
    /// The mission's final word for the host's result: carried items and escorts count at extraction, stealth and
    /// defense hold if never broken, a party that brings nobody home fails it (<see cref="FieldMission.FailOnWipe"/>).
    /// </summary>
    public static ObjectiveResult? Result(ScavengeState state, int playerId)
    {
        if (state.Mission is not { } run) return null;
        bool Out(Unit u) => u.Status is UnitStatus.Extracted or UnitStatus.Evacuated;
        var home = state.Units.Where(u => u.PlayerId == playerId && Out(u)).ToList();
        var statuses = new SortedDictionary<string, ObjectiveStatus>(StringComparer.Ordinal);
        foreach (var o in run.Def.Objectives)
        {
            var s = run.Find(o.Id)!.Status;
            if (s == ObjectiveStatus.Active)
                s = o.Kind switch
                {
                    ObjectiveKind.Retrieve => home.Any(u => u.Pack.Contains(o.Item!)) ? ObjectiveStatus.Done : s,
                    ObjectiveKind.Escort => state.Units.Any(u => u.Tag == o.Tag && Out(u)) ? ObjectiveStatus.Done : s,
                    ObjectiveKind.Defend or ObjectiveKind.Undetected => home.Any(u => u.Kind == UnitKind.Survivor) ? ObjectiveStatus.Done : s,
                    _ => s,
                };
            statuses[o.Id] = s;
        }
        var wiped = run.Def.FailOnWipe && state.FindParty(playerId) is { Done: true } && !home.Any(u => u.Kind == UnitKind.Survivor);
        var outcome = run.Outcome == MissionOutcome.Failed || wiped ? MissionOutcome.Failed
            : run.Def.Objectives.Where(o => !o.Optional).All(o => statuses[o.Id] == ObjectiveStatus.Done) ? MissionOutcome.Done
            : MissionOutcome.Incomplete;
        var escorted = state.Units.Where(u => u.CharacterId is not null && Out(u)).Select(u => u.CharacterId!).Distinct().ToList();
        return new ObjectiveResult(run.Def.Id, outcome, statuses, new(run.Flags, StringComparer.Ordinal), new(run.Choices, StringComparer.Ordinal), escorted);
    }
}
