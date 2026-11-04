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

    /// <summary>At the start of each round: round triggers, the Linked carrying off the downed, heavy work, survive counts.</summary>
    public static void RoundStarted(ScavengeSim sim)
    {
        if (sim.State.Mission is not { } run) return;
        var round = sim.State.Round;
        Fire(sim, t => t.When.Kind == TriggerKind.Round && t.When.Round == round);
        Seize(sim, run);
        Carry(sim, run);
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
    public static void Update(ScavengeSim sim)
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
                        // By a tag, or by a story character (a unique survivor tagged "key" still counts as Iris or Seven).
                        var reachers = o.By is { } by
                            ? state.Units.Where(u => u.Side == side && u.IsActive && (u.Tag == by || u.Origin?.UniqueId == by || u.CharacterId == by)).ToList()
                            : survivors;
                        if (run.Marker(o.Marker).Any(m => reachers.Any(u => u.Pos.Chebyshev(m) <= o.Radius))) to = ObjectiveStatus.Done;
                        break;
                    case ObjectiveKind.Keep:
                        if (run.Taken.Count * 100 > o.Count * Math.Max(1, run.StartSurvivors)) to = ObjectiveStatus.Failed;
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
                TriggerKind.After => run.Find(t.When.Objective ?? "") is { Status: not ObjectiveStatus.Inactive } after && state.Round >= after.StartRound + t.When.Round,
                TriggerKind.Hurt => state.Units.Any(u => u.Tag == t.When.Tag && u.OnMap && u.Hp <= u.MaxHp * t.When.Below),
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
                     || p.Def.Kind is ObjectiveKind.Defend or ObjectiveKind.Undetected or ObjectiveKind.Keep && p.State.Status == ObjectiveStatus.Active))
        {
            run.Outcome = MissionOutcome.Done;
            foreach (var (o, s) in Pairs(run).Where(p => p.State.Status == ObjectiveStatus.Active && p.Def.Kind is ObjectiveKind.Defend or ObjectiveKind.Undetected or ObjectiveKind.Keep))
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
            if (run.Fired.Contains(t.Id) || !match(t) || !run.Holds(t.When.If)) continue;
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
                    state.Log(new(state.Round, ScavengeEventType.Recruited, $"{u.Name} follows {leader.Name}.", leader.Id, u.Id, u.Pos));
                }
                break;
            case TriggerActionKind.Prompt:
                // A prompt whose every option is guarded off is skipped, not left blocking the run.
                var open = a.Prompt!.Options.Where(o => run.Holds(o.If)).ToList();
                if (open.Count == 0) break;
                run.Pending = a.Prompt with { Options = open };
                state.Log(new(state.Round, ScavengeEventType.MissionPrompt, $"{a.Prompt!.Speaker}: {a.Prompt.Text}", Side: side));
                break;
            case TriggerActionKind.Split:
                if (state.Devices.Find(d => d.Tag == a.Tag) is { Used: false } dev && dev.Items.Count > 1)
                {
                    var keep = (dev.Items.Count + 1) / 2;
                    dev.Items.RemoveRange(keep, dev.Items.Count - keep);
                }
                break;
            case TriggerActionKind.Leave:
                foreach (var u in Tagged())
                {
                    u.Status = UnitStatus.Extracted;
                    if (u.CarriedById is { } bearer && state.FindUnit(bearer) is { } b) b.CarryingId = null;
                    if (u.CarryingId is { } carried && state.FindUnit(carried) is { } c) c.CarriedById = null;
                    u.CarriedById = null;
                    u.CarryingId = null;
                    state.Log(new(state.Round, ScavengeEventType.Chatter, $"{u.Name} is gone.", u.Id, Pos: u.Pos, Side: side));
                }
                break;
            case TriggerActionKind.Lock:
                foreach (var d in state.Devices.Where(d => d.Tag == a.Tag))
                {
                    d.Lockouts.RemoveAll(l => l.Side == side);
                    d.Lockouts.Add(new Lockout(side, int.MaxValue));
                }
                break;
            case TriggerActionKind.Open:
                foreach (var d in state.Devices.Where(d => d.Tag == a.Tag))
                {
                    d.Controller = side;
                    d.ControllerPlayerId = HostPlayerId(state);
                    d.Lockouts.Clear();
                    state.Log(new(state.Round, ScavengeEventType.Hacked, $"The {d.Kind.ToString().ToLowerInvariant()} opens.", Pos: d.Pos, Side: side));
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

    /// <summary>
    /// The Linked carry off the downed (<see cref="FieldMission.TakeTag"/>): a downed party survivor nobody carries, with a
    /// taker next to it at the start of <see cref="FieldMission.TakeRounds"/> rounds in a row, is gone from the map —
    /// taken, not dead (the result lists them; the story keeps them).
    /// </summary>
    private static void Seize(ScavengeSim sim, MissionRun run)
    {
        if (run.Def.TakeTag is not { } tag) return;
        var state = sim.State;
        var side = PartySide(state);
        // Only the host's own survivors: a co-op guest's people are not the story's to keep (theirs fall as in any run).
        var host = HostPlayerId(state);
        foreach (var u in state.Units.Where(u => u.Side == side && u.Kind == UnitKind.Survivor && u.PlayerId == host).OrderBy(u => u.Id).ToList())
        {
            var held = u.Status == UnitStatus.Downed && u.CarriedById is null
                       && state.Units.Any(t => t.IsActive && t.Tag == tag && t.HostileTo(u) && t.Pos.Chebyshev(u.Pos) <= 1);
            if (!held)
            {
                run.Seized.Remove(u.Id);
                continue;
            }
            var rounds = run.Seized[u.Id] = run.Seized.GetValueOrDefault(u.Id) + 1;
            if (rounds < run.Def.TakeRounds || u.Origin is null) continue;
            run.Seized.Remove(u.Id);
            u.Status = UnitStatus.Dead;
            u.CarryingId = null;
            run.Taken.Add(u.Origin.Id);
            state.Log(new(state.Round, ScavengeEventType.Chatter, $"The Linked carry {u.Name} away. Gently.", u.Id, Pos: u.Pos, Side: side));
            Fire(sim, t => t.When.Kind == TriggerKind.Taken);
        }
    }

    /// <summary>
    /// Heavy work (<see cref="ObjectiveKind.Carry"/>): each round that starts with enough party survivors next to the
    /// marker counts; when it is done the item goes into the strongest carrier's pack.
    /// </summary>
    private static void Carry(ScavengeSim sim, MissionRun run)
    {
        var state = sim.State;
        var side = PartySide(state);
        foreach (var (o, s) in Pairs(run).Where(p => p.Def.Kind == ObjectiveKind.Carry && p.State.Status == ObjectiveStatus.Active).ToList())
        {
            var at = run.Marker(o.Marker);
            var carriers = state.Units.Where(u => u.Side == side && u.IsActive && u.Kind == UnitKind.Survivor && at.Any(m => u.Pos.Chebyshev(m) <= 1))
                .OrderByDescending(u => u.Attributes.GetValueOrDefault(Survivors.AttributeKind.Strength)).ThenBy(u => u.Id).ToList();
            if (carriers.Count < o.Carriers) continue;
            s.Progress++;
            state.Log(new(state.Round, ScavengeEventType.ObjectiveUpdated, $"{o.Text} ({s.Progress}/{o.Count})", Side: side));
            if (s.Progress < o.Count) continue;
            // The host's carrier takes it (the mission's item goes home with the host); a guest's only if no host is there.
            if (o.Item is { } item) (carriers.FirstOrDefault(c => c.PlayerId == HostPlayerId(state)) ?? carriers[0]).Pack.Add(item);
            Set(sim, o, s, ObjectiveStatus.Done);
            if (o.Item is { } picked) Fire(sim, t => t.When.Kind == TriggerKind.PickedUp && t.When.Item == picked);
        }
    }

    public static void Say(ScavengeSim sim, string speaker, string text)
    {
        var state = sim.State;
        // Stage directions have no speaker.
        if (speaker == "narration") speaker = "";
        state.Mission?.Chatter.Add(new(state.Round, speaker, text));
        state.Log(new(state.Round, ScavengeEventType.Chatter, speaker.Length > 0 ? $"{speaker}: {text}" : text, Side: PartySide(state)));
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
            Adrenaline.Joined(sim, unit);
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
                    ObjectiveKind.Defend or ObjectiveKind.Undetected or ObjectiveKind.Keep => home.Any(u => u.Kind == UnitKind.Survivor) ? ObjectiveStatus.Done : s,
                    _ => s,
                };
            statuses[o.Id] = s;
        }
        var wiped = run.Def.FailOnWipe && state.FindParty(playerId) is { Done: true } && !home.Any(u => u.Kind == UnitKind.Survivor);
        var outcome = run.Outcome == MissionOutcome.Failed || wiped ? MissionOutcome.Failed
            : run.Def.Objectives.Where(o => !o.Optional).All(o => statuses[o.Id] == ObjectiveStatus.Done) ? MissionOutcome.Done
            : MissionOutcome.Incomplete;
        var escorted = state.Units.Where(u => u.CharacterId is not null && Out(u)).Select(u => u.CharacterId!).Distinct().ToList();
        var host = HostPlayerId(state) == playerId;
        // Co-op: a guest's result never advances a campaign; it carries copies of the schematics the host's mission pays.
        if (!host)
            return new ObjectiveResult(run.Def.Id, MissionOutcome.Incomplete, statuses, new(StringComparer.Ordinal), new(StringComparer.Ordinal), [])
            {
                GuestSchematics = outcome == MissionOutcome.Done ? run.GuestSchematics.ToList() : [],
            };
        var taken = state.Units.Where(u => u.PlayerId == playerId && u.Origin is { } o && run.Taken.Contains(o.Id)).Select(u => u.Origin!.Id).ToList();
        return new ObjectiveResult(run.Def.Id, outcome, statuses, new(run.Flags, StringComparer.Ordinal), new(run.Choices, StringComparer.Ordinal), escorted)
        {
            Taken = taken,
        };
    }
}
