using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Random;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// One scavenge instance: definitions + state + seeded RNG. Changes only through <see cref="Execute"/>;
/// AI plans, bleeding out and round changes run inside it, so the same seed and commands give the same game.
/// A round opens with the AI's plans made; players plan at once (<see cref="Plans"/>), and when all are committed the round
/// resolves (<see cref="RoundResolver"/>). Orders outside combat (loot, items, hacks, drones, co-op) are not queued: they take
/// effect as given, from where the unit stands.
/// </summary>
public sealed class ScavengeSim
{
    private readonly DeterministicRng _rng;

    /// <summary>What is left of an action's effect right now: below 1 only while a clash resolves (<see cref="RoundResolver"/>).</summary>
    internal double EffectFactor { get; set; } = 1;

    public ScavengeData Data { get; }
    public ScavengeState State { get; }

    public ScavengeSim(ScavengeData data, ScavengeState state)
    {
        Data = data;
        State = state;
        _rng = new DeterministicRng { State = state.RngState == 0 ? new DeterministicRng(0).State : state.RngState };
        SyncRng();
        state.Stamp = StampSeenBy;
        FieldEffects.Sync(state);
    }

    /// <summary>Sim-owned randomness. Always draw through this so the state stays in the save.</summary>
    public DeterministicRng Rng => _rng;

    /// <summary>A unit's field profile, with its sight as the run's light and weather leave it.</summary>
    public UnitProfile Profile(Unit unit)
    {
        var p = ProfileDeriver.Derive(Data, unit);
        return p with { Sight = FieldEnvironment.Sight(Data, State, unit, p.Sight) };
    }

    /// <summary>Tiles the unit can move to with one more planned move, from where its walking ends, with MP cost (for UI highlights).</summary>
    public Dictionary<Pos, int> Reachable(Unit unit)
    {
        var real = unit.Pos;
        unit.Pos = Plans.MoveEnd(State, unit);
        try { return Movement.Reachable(State, unit, Profile(unit).Movement, unit.Mp); }
        finally { unit.Pos = real; }
    }

    /// <summary>Tile indices the player's side sees right now.</summary>
    public HashSet<int> VisibleTo(int playerId) =>
        State.FindParty(playerId) is { } party ? Visibility.SideVisible(Data, State, party.Side) : [];

    public ExtractionResult ResultFor(int playerId) => ExtractionResult.Build(this, playerId);

    /// <summary>Starts round 1 and has the AI plan it.</summary>
    public void Start()
    {
        if (State.Round == 0)
        {
            Adrenaline.Started(this);
            MissionRules.Started(this);
            StartRound();
        }
        PlanAi();
        Advance();
        Visibility.UpdateExplored(Data, State);
        Bestiary.Observe(this);
        SyncRng();
    }

    public ScavengeResult Execute(IScavengeCommand command)
    {
        var result = ScavengeCommandHandlers.Validate(this, command, out var unit) ?? MissionRules.Blocked(this, command);
        var (mpBefore, apBefore, fatigueBefore) = (unit?.Mp ?? 0, unit?.Ap ?? 0, unit?.Fatigue ?? 0);
        var stepsBefore = unit is null ? [] : Plans.Of(State, unit)?.Steps.ToList() ?? [];
        result ??= command switch
        {
            ChooseOption c => MissionRules.Choose(this, unit!, c),
            Move c => ScavengeCommandHandlers.Move(this, unit!, c),
            Push c => Pushes.Plan(this, unit!, c),
            Dash c => Pushes.Plan(this, unit!, c),
            UseItem c => ScavengeCommandHandlers.UseItem(this, unit!, c),
            PickUp c => ScavengeCommandHandlers.PickUp(this, unit!, c),
            TakeAll c => FieldLoot.TakeAll(this, unit!, c),
            DropItem c => FieldLoot.Drop(this, unit!, c),
            StashItem c => FieldLoot.Stash(this, unit!, c),
            Salvage c => FieldSalvage.Salvage(this, unit!, c),
            Recruit c => ScavengeCommandHandlers.Recruit(this, unit!, c),
            AttackTile c => TileStrikes.Declare(this, unit!, c),
            PlayCard c => CardPlay.Play(this, unit!, c),
            DiscardDeadCard c => CardPlay.DiscardDead(this, unit!, c),
            Lookout c => ScavengeCommandHandlers.Lookout(this, unit!, c),
            FieldMod c => ScavengeCommandHandlers.FieldMod(this, unit!, c),
            Recover => Economy.Recover(this, unit!),
            EndTurn => Commit(unit!),
            UndoPlan c => Plans.Undo(this, unit!, c),
            Extract => ScavengeCommandHandlers.Extract(this, unit!),
            TakeFromStash c => ParkedVehicles.Unstash(this, unit!, c),
            ProposeAlliance c => Alliances.Propose(this, unit!, c),
            RespondAlliance c => Alliances.Respond(this, unit!, c),
            BreakAlliance c => Alliances.Break(this, unit!, c),
            PingTile c => CoopActions.Ping(this, unit!, c),
            GiveItem c => CoopActions.Give(this, unit!, c),
            Carry c => CoopActions.Carry(this, unit!, c),
            PutDown c => CoopActions.PutDown(this, unit!, c),
            Hack c => Hacking.Hack(this, unit!, c),
            DeployDrone c => Drones.Deploy(this, unit!, c),
            FlyDrone c => Drones.Fly(this, unit!, c),
            RecallDrone => Drones.Recall(this, unit!),
            MarkTarget c => Drones.Mark(this, unit!, c),
            DropPayload c => Drones.Drop(this, unit!, c),
            DroneHeal c => DroneRoles.Heal(this, unit!, c),
            DroneLoad c => DroneRoles.Load(this, unit!, c),
            DroneFerry => DroneRoles.Ferry(this, unit!),
            DroneFire c => DroneRoles.Fire(this, unit!, c),
            _ => ScavengeResult.Fail(ScavengeError.UnknownCommand, $"Unknown command {command.GetType().Name}."),
        };

        if (result.Ok)
        {
            if (command is not Recover) Economy.Pay(this, unit!, mpBefore - unit!.Mp, apBefore - unit.Ap);
            AfterCommand(command, unit!, stepsBefore, mpBefore, apBefore, fatigueBefore);
            MissionRules.Update(this);
            UpdateParties();
            Advance();
            Visibility.UpdateExplored(Data, State);
            Bestiary.Observe(this);
        }
        SyncRng();
        return result;
    }

    /// <summary>A new plan entry remembers what it cost; a plan that changes is open again.</summary>
    private void AfterCommand(IScavengeCommand command, Unit unit, List<PlanStep> stepsBefore, int mpBefore, int apBefore, int fatigueBefore)
    {
        if (Plans.Of(State, unit) is not { } plan) return;
        if (plan.Steps.Find(s => !stepsBefore.Contains(s)) is { } step)
        {
            (step.RefundMp, step.RefundAp, step.RefundFatigue) = (mpBefore - unit.Mp, apBefore - unit.Ap, unit.Fatigue - fatigueBefore);
        }
        if (command is Move or AttackTile or PlayCard or Recover or Push or Dash or UndoPlan) plan.Committed = false;
    }

    private ScavengeResult Commit(Unit unit)
    {
        Plans.For(State, unit).Committed = true;
        return ScavengeResult.Success($"{unit.Name} is ready.");
    }

    /// <summary>While nobody is left planning: the round resolves and the next opens, with the AI's plans made.</summary>
    private void Advance()
    {
        for (var guard = 0; guard < 1000 && !State.Finished && Plans.AllCommitted(State); guard++)
        {
            PlanAi();
            RoundResolver.Run(this);
            AfterStep();
            if (State.Finished) return;
            StartRound();
            PlanAi();
        }
    }

    /// <summary>Whatever a resolved step changed for the mission and the parties.</summary>
    internal void AfterStep()
    {
        MissionRules.Update(this);
        UpdateParties();
    }

    /// <summary>
    /// The AI units plan the round, once, by id, when it opens: they see where everyone stands and never anyone's plan, and
    /// players see their moves while they plan in turn.
    /// </summary>
    private void PlanAi()
    {
        if (State.AiPlannedRound >= State.Round) return;
        State.AiPlannedRound = State.Round;
        foreach (var unit in State.Units.Where(u => u.IsActive && !u.PlayerControlled && Plans.Acts(this, u) && Plans.Of(State, u) is null).OrderBy(u => u.Id).ToList())
            ScavengeAi.TakeTurn(this, unit);
    }

    /// <summary>The round opens for a unit: its lookout and the round's card effects end, and it gets fresh points.</summary>
    internal void BeginRound(Unit unit)
    {
        unit.Lookout = false;
        unit.PingsUsed = 0;
        unit.Moved = false;
        unit.CardMove = 0;
        unit.CardCover = 0;
        unit.PriorityBonus = 0;
        unit.CardsPlayed = 0;
        unit.ExtraMoves = 0;
        unit.ExtraActions = 0;
        unit.ExtraCards = 0;
        unit.FreeActions = (int)Math.Round(unit.Hook(PerkHooks.FreeAction));
        Economy.RoundStarted(this, unit);
        if (State.Round == 1 && unit.FirstRoundAp != 0)
        {
            unit.Ap = Math.Max(1, unit.Ap + unit.FirstRoundAp);
            unit.ExtraActions += Math.Max(0, unit.FirstRoundAp);
        }
        if (unit.CrashAp > 0)
        {
            unit.Ap = Math.Max(0, unit.Ap - unit.CrashAp);
            unit.CrashAp = 0;
            State.Log(new(State.Round, ScavengeEventType.Crashed, $"{unit.Name} crashes off the stim ({unit.Ap} AP).", unit.Id, Pos: unit.Pos));
        }
        if (unit.EmpUntilRound >= State.Round)
        {
            unit.Ap = 0;
            unit.Mp = 0;
            State.Log(new(State.Round, ScavengeEventType.EmpBurst, $"{unit.Name} is still down from the EMP.", unit.Id, Pos: unit.Pos));
        }
        Stuns.RoundStarted(this, unit);
        Adrenaline.RoundStarted(this, unit);
        if (Economy.Stamina(this, unit) <= 0) unit.Ap = 0;
    }

    /// <summary>Bleeding ticks down, the zone's systems move on, then every unit that acts gets its points for the round.</summary>
    private void StartRound()
    {
        State.Round++;
        State.Log(new(State.Round, ScavengeEventType.RoundStarted, $"Round {State.Round}."));
        foreach (var u in State.Units.Where(u => u.Status == UnitStatus.Downed && !u.Stabilized).ToList())
        {
            u.BleedOut--;
            if (u.BleedOut <= 0) Kill(u, ScavengeEventType.BledOut, $"{u.Name} bled out.");
        }
        foreach (var u in State.Units.Where(u => u.OnMap && u.Body.Conditions.Count > 0)) Treatment.CountDown(u.Body);
        UpdateParties();
        Detection.Expire(Data, State);
        FieldEffects.RoundStarted(State);
        FieldEnvironment.RoundStarted(this);
        Alliances.RoundStarted(this);
        Hacking.RoundStarted(this);
        Drones.RoundStarted(this);
        Network.RoundStarted(this);
        Interference.RoundStarted(this);
        ElectronicWarfare.RoundStarted(this);
        MissionRules.RoundStarted(this);
        if (State.Finished) return;
        SquadDecks.RoundStarted(this);
        ParkedVehicles.RoundStarted(this);
        foreach (var unit in State.Units.Where(u => u.IsActive && Plans.Acts(this, u)).ToList()) BeginRound(unit);
    }

    /// <summary>
    /// A party with no conscious player-controlled unit left is done: its downed are left behind and die, its recruits
    /// go over to an ally still in the zone or stay behind as strangers. While an ally is still up, a party whose people
    /// are all downed is not done yet: the ally may stabilize them and carry them out (they still bleed). When every
    /// party is done the instance is finished.
    /// </summary>
    public void UpdateParties()
    {
        CoopActions.Sync(State);
        bool changed;
        do
        {
            changed = false;
            foreach (var party in State.Parties.Where(p => !p.Done))
            {
                var mine = State.Units.Where(u => u.PlayerId == party.PlayerId).ToList();
                if (mine.Any(u => u.PlayerControlled && u.IsActive)) continue;
                var allies = State.Units.Where(u => u.Side == party.Side && u.PlayerId != party.PlayerId && u.PlayerControlled && u.IsActive
                                                    && State.FindParty(u.PlayerId!.Value) is { Left: false }).ToList();
                if (allies.Count > 0 && mine.Any(u => u.Status == UnitStatus.Downed && u.Kind == UnitKind.Survivor)) continue;

                party.Done = true;
                changed = true;
                foreach (var u in mine.Where(u => u.Status == UnitStatus.Downed && u.Kind == UnitKind.Survivor))
                    Kill(u, ScavengeEventType.Died, $"{u.Name} was left behind.");
                foreach (var u in mine.Where(u => u.OnMap && u.Kind == UnitKind.Npc))
                {
                    var leader = allies.OrderBy(a => a.Pos.Chebyshev(u.Pos)).ThenBy(a => a.Id).FirstOrDefault();
                    u.Side = leader?.Side ?? Unit.NeutralSide;
                    u.PlayerId = leader?.PlayerId;
                    u.FollowingId = leader?.Id;
                }
                State.Log(new(State.Round, ScavengeEventType.PartyDone, $"{State.PlayerName(party.PlayerId)}'s run is over."));
            }
            CoopActions.Sync(State);
        } while (changed);
        if (!State.Finished && State.Parties.All(p => p.Done))
        {
            State.Finished = true;
            State.Log(new(State.Round, ScavengeEventType.Finished, "Everyone has left the zone."));
        }
    }

    /// <summary>
    /// A player is gone for good (the multiplayer host decides; like a timeout it is outside input, not a rule): their
    /// party holds position, and an all-downed ally waiting for them to carry or revive it is left behind.
    /// </summary>
    public void Abandon(int playerId)
    {
        if (State.FindParty(playerId) is not { Left: false } party) return;
        party.Left = true;
        UpdateParties();
        Advance();
        Visibility.UpdateExplored(Data, State);
        SyncRng();
    }

    /// <summary>
    /// Dead units drop what is worth taking into a pile on their tile: a survivor or stranger everything they carry,
    /// an enemy or a device its loot table and schematic drops (<see cref="FieldLoot.Drops"/>; never its own
    /// equipment). <paramref name="killer"/>'s perks can add to the loot.
    /// </summary>
    internal void Kill(Unit unit, ScavengeEventType type, string message, Unit? killer = null)
    {
        unit.Status = UnitStatus.Dead;
        unit.Hp = Math.Min(unit.Hp, 0);
        unit.Ap = 0;
        TileStrikes.Lose(this, unit, "dead");
        Bestiary.Killed(this, unit, killer);
        State.Log(new(State.Round, type, message, unit.Id, Pos: unit.Pos));
        if (unit.Kind == UnitKind.Drone)
        {
            Drones.Destroyed(State, unit);
            return;
        }
        if (unit.Kind == UnitKind.Decoy) return;
        if (unit.Kind == UnitKind.Device) Hacking.UnitKilled(this, unit);

        if (unit.Kind is UnitKind.Enemy or UnitKind.Device && unit.EnemyDefId is not null)
        {
            FieldLoot.Dropped(this, unit, FieldLoot.Drops(this, unit, killer), unit.Name);
            return;
        }
        var drops = unit.AllItems.Where(id => unit.Kind != UnitKind.Device || Data.Items[id].LootWeight > 0).ToList();
        if (drops.Count == 0) return;
        FieldLoot.Pile(State, unit.Pos, LootPileKind.Remains, unit.Name).Items.AddRange(drops);
    }

    /// <summary>Idle AI enemies within the radius that are hostile to the source start hunting toward it.</summary>
    internal void MakeNoise(Unit source, int radius)
    {
        foreach (var u in State.Units.Where(u => u.IsActive && u.Kind == UnitKind.Enemy && u.Ai.Mode == AiMode.Idle))
        {
            // Husks hear further in the dark, and a Network weave further still (GearEffects.HuskLure); rain drowns noise out
            // for everyone (FieldEnvironment).
            var effects = FieldEnvironment.Effects(Data, State);
            var husk = FieldEnvironment.SenseOf(Data, State, u) == SenseKind.Husk;
            var heard = radius * effects.HearingMultiplier * (husk ? effects.HuskHearingMultiplier : 1)
                        + (husk ? GearEffects.HuskLure(Data, source) : 0);
            if (!u.HostileTo(source) || u.Pos.Chebyshev(source.Pos) > heard + 1e-9) continue;
            u.Ai.Mode = AiMode.Hunting;
            u.Ai.TargetId = null;
            u.Ai.LastKnown = source.Pos;
        }
    }

    /// <summary>Records which party sides see an event's actor right now (see <see cref="FogLog"/>).</summary>
    private ScavengeEvent StampSeenBy(ScavengeEvent e)
    {
        if (e.UnitId is not { } id || State.FindUnit(id) is not { } actor) return e;
        return e with { SeenBy = Detection.PartySides(State).Where(side => Detection.SideSees(Data, State, side, actor)).ToList() };
    }

    /// <summary>
    /// After a unit moved (by its feet or by force): motion sensors it entered reveal it to their side, sides it is unseen by
    /// but close to hear it (movement contact).
    /// </summary>
    internal void Moved(Unit unit, Pos from)
    {
        var fog = Data.Rules.Fog;
        foreach (var sensor in State.Sensors.OrderBy(s => s.Id).ToList())
        {
            if (sensor.Side == unit.Side || unit.Side == Unit.NeutralSide) continue;
            if (unit.Pos.Chebyshev(sensor.Pos) > sensor.Radius || from.Chebyshev(sensor.Pos) <= sensor.Radius) continue;
            Trip(sensor, unit);
        }
        // Thunder drowns out footsteps; rain shortens how far they carry. Plates clank (and a weave draws Husks): idle enemies
        // in earshot of the move come looking, and parties hear it further.
        var clank = GearEffects.MoveNoise(Data, unit);
        if (clank > 0 || GearEffects.HuskLure(Data, unit) > 0) MakeNoise(unit, Math.Max(0, clank));
        var hear = FieldEnvironment.ThunderNow(State) ? -1 : fog.HearMovement * FieldEnvironment.Effects(Data, State).HearingMultiplier + clank;
        foreach (var side in Detection.PartySides(State).Where(s => s != unit.Side && unit.Side != Unit.NeutralSide))
            if (State.Units.Any(u => u.Side == side && u.IsActive && u.Pos.Chebyshev(unit.Pos) <= hear + 1e-9)
                && !Detection.SideSees(Data, State, side, unit))
                Detection.AddContact(Data, State, side, unit.Pos, ContactKind.Movement, unit.Id);
        MissionRules.Moved(this, unit);
        Hacking.CameraCheck(this, unit);
    }

    /// <summary>A motion sensor goes off: the unit is revealed to the sensor's side, which gets an alert and a contact.</summary>
    internal void Trip(Sensor sensor, Unit unit)
    {
        Detection.Reveal(State, unit, sensor.Side, State.Round + Data.Rules.Fog.SensorRevealRounds);
        Detection.AddContact(Data, State, sensor.Side, unit.Pos, ContactKind.Sensor, unit.Id);
        State.Log(new(State.Round, ScavengeEventType.SensorTripped,
            $"{Item(sensor.ItemId).Name} at {sensor.Pos}: {unit.Name} moving at {unit.Pos}!", unit.Id, Pos: unit.Pos,
            Anonymous: $"{Item(sensor.ItemId).Name} at {sensor.Pos}: movement at {unit.Pos}!", Side: sensor.Side));
    }

    internal void Log(ScavengeEventType type, string message, Unit? unit = null, Unit? target = null) =>
        State.Log(new(State.Round, type, message, unit?.Id, target?.Id, (target ?? unit)?.Pos));

    internal ItemDef Item(string id) => Data.Items[id];

    private void SyncRng() => State.RngState = _rng.State;
}
