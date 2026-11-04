using Remnants.Core.Items;
using Remnants.Core.Random;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// One scavenge instance: definitions + state + seeded RNG. Changes only through <see cref="Execute"/>;
/// AI turns, bleeding out and round changes run inside it, so the same seed and commands give the same game.
/// Turns follow initiative across every unit on the map, whichever player or AI owns it.
/// </summary>
public sealed class ScavengeSim
{
    private readonly DeterministicRng _rng;

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

    /// <summary>Tiles the unit can move to with one move action now, with AP cost (for UI highlights).</summary>
    public Dictionary<Pos, int> Reachable(Unit unit) => Movement.Reachable(State, unit, Profile(unit).Movement);

    /// <summary>Tile indices the player's side sees right now.</summary>
    public HashSet<int> VisibleTo(int playerId) =>
        State.FindParty(playerId) is { } party ? Visibility.SideVisible(Data, State, party.Side) : [];

    public ExtractionResult ResultFor(int playerId) => ExtractionResult.Build(this, playerId);

    /// <summary>Starts round 1 and runs AI turns until the first player-controlled unit is up.</summary>
    public void Start()
    {
        if (State.Round == 0)
        {
            MissionRules.Started(this);
            AdvanceTurns();
        }
        Visibility.UpdateExplored(Data, State);
        Bestiary.Observe(this);
        SyncRng();
    }

    public ScavengeResult Execute(IScavengeCommand command)
    {
        var result = ScavengeCommandHandlers.Validate(this, command, out var unit) ?? MissionRules.Blocked(this, command) ?? command switch
        {
            ChooseOption c => MissionRules.Choose(this, unit!, c),
            Move c => ScavengeCommandHandlers.Move(this, unit!, c),
            UseItem c => ScavengeCommandHandlers.UseItem(this, unit!, c),
            PickUp c => ScavengeCommandHandlers.PickUp(this, unit!, c),
            TakeAll c => FieldLoot.TakeAll(this, unit!, c),
            DropItem c => FieldLoot.Drop(this, unit!, c),
            StashItem c => FieldLoot.Stash(this, unit!, c),
            Salvage c => FieldSalvage.Salvage(this, unit!, c),
            Recruit c => ScavengeCommandHandlers.Recruit(this, unit!, c),
            SetStance c => ScavengeCommandHandlers.SetStance(this, unit!, c),
            AttackTile c => TileStrikes.Declare(this, unit!, c),
            PlayCard c => CardPlay.Play(this, unit!, c),
            DiscardDeadCard c => CardPlay.DiscardDead(this, unit!, c),
            Lookout c => ScavengeCommandHandlers.Lookout(this, unit!, c),
            FieldMod c => ScavengeCommandHandlers.FieldMod(this, unit!, c),
            EndTurn c => FinishTurn(unit!, c),
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
            MissionRules.Update(this);
            UpdateParties();
            // The acting unit's turn ends on EndTurn or a stance, or when it left the map or can no longer act; its sub-turn
            // ends on a tile attack.
            if (command is EndTurn or SetStance or AttackTile || unit is not { IsActive: true })
            {
                State.TurnIndex++;
                AdvanceTurns();
            }
            Visibility.UpdateExplored(Data, State);
            Bestiary.Observe(this);
        }
        SyncRng();
        return result;
    }

    /// <summary>
    /// Moves along the turn order: skips units that can no longer act, runs AI turns, starts new rounds,
    /// and stops at the next player-controlled unit (with fresh AP) or when the instance is finished.
    /// </summary>
    private void AdvanceTurns()
    {
        while (!State.Finished)
        {
            if (State.TurnIndex >= State.TurnOrder.Count)
            {
                EndSubRound();
                if (State.Finished) break;
                if (State.Round > 0 && State.SubRound < Data.Rules.Turn.SubRounds)
                {
                    State.SubRound++;
                    State.TurnIndex = 0;
                }
                else StartRound();
                continue;
            }
            var unit = State.CurrentUnit;
            if (unit is not { IsActive: true } || State.SubRound > 1 && SitsOut(unit))
            {
                State.TurnIndex++;
                continue;
            }
            if (State.SubRound > 1)
            {
                // A later sub-round: the turn goes on with the AP left.
                StartSubTurn(unit);
                if (unit.PlayerControlled) return;
                ScavengeAi.TakeTurn(this, unit);
                MissionRules.Update(this);
                UpdateParties();
                State.TurnIndex++;
                continue;
            }
            BeginTurn(unit);
            StartSubTurn(unit);
            if (unit.Suppressed)
            {
                unit.Suppressed = false;
                unit.Ap = Math.Max(1, unit.Ap - Data.Rules.Network.SuppressApPenalty);
                State.Log(new(State.Round, ScavengeEventType.Suppressed, $"{unit.Name} is pinned down ({unit.Ap} AP).", unit.Id, Pos: unit.Pos));
            }
            if (unit.PlayerControlled) return;

            ScavengeAi.TakeTurn(this, unit);
            MissionRules.Update(this);
            UpdateParties();
            State.TurnIndex++;
        }
    }

    /// <summary>A unit's sub-turn begins: it gets the next <see cref="ScavengeState.TurnSeq"/> and is on its feet again (no longer <see cref="Unit.Displaced"/>).</summary>
    private void StartSubTurn(Unit unit)
    {
        unit.TurnSeq = ++State.TurnSeq;
        unit.Displaced = false;
    }

    private ScavengeResult FinishTurn(Unit unit, EndTurn c)
    {
        if (!c.WholeTurn) return ScavengeResult.Success($"{unit.Name} ends their turn.");
        unit.TurnOverRound = State.Round;
        return ScavengeResult.Success($"{unit.Name} is done for the round.");
    }

    /// <summary>
    /// Whether the unit skips a later sub-round: out of AP, done for the round or holding a stance.
    /// </summary>
    private bool SitsOut(Unit unit) =>
        unit.Ap <= 0 || unit.TurnOverRound == State.Round || unit.Stance != Stance.None;

    /// <summary>
    /// Whether an AI unit's strike declared now must wait a sub-round: player units are hostile to it, and none of them
    /// has a turn to come this sub-round to answer it with.
    /// </summary>
    internal bool DefersStrike(Unit unit) =>
        !unit.PlayerControlled && State.Units.Any(u => u is { IsActive: true, PlayerControlled: true } && u.HostileTo(unit))
        && !State.TurnOrder.Skip(State.TurnIndex).Select(State.FindUnit)
            .Any(u => u is { IsActive: true, PlayerControlled: true } && u.HostileTo(unit) && (State.SubRound == 1 || !SitsOut(u)));

    /// <summary>The sub-round's tile attacks come down (<see cref="TileStrikes"/>).</summary>
    private void EndSubRound()
    {
        TileStrikes.ResolveAll(this);
        MissionRules.Update(this);
        UpdateParties();
    }

    /// <summary>
    /// A unit's turn starts: its stance and lookout end (bracing costs <see cref="BraceRules.NextTurnApPenalty"/> AP),
    /// then it gets fresh AP.
    /// </summary>
    private void BeginTurn(Unit unit)
    {
        var braced = unit.Stance == Stance.Brace;
        unit.Stance = Stance.None;
        unit.Reactions = 0;
        unit.Lookout = false;
        unit.PingsUsed = 0;
        unit.Moved = false;
        if (unit.OutplayedUntilRound < State.Round) (unit.OutplayedId, unit.FreeCounterId) = (null, null);
        unit.CardMove = 0;
        unit.CardCover = 0;
        unit.CounterId = null;
        unit.Ap = Profile(unit).MaxAp;
        if (State.Round == 1 && unit.FirstRoundAp != 0) unit.Ap = Math.Max(1, unit.Ap + unit.FirstRoundAp);
        if (braced) unit.Ap = Math.Max(1, unit.Ap - Data.Rules.Fog.Brace.NextTurnApPenalty);
        if (unit.CrashAp > 0)
        {
            unit.Ap = Math.Max(1, unit.Ap - unit.CrashAp);
            unit.CrashAp = 0;
            State.Log(new(State.Round, ScavengeEventType.Crashed, $"{unit.Name} crashes off the stim ({unit.Ap} AP).", unit.Id, Pos: unit.Pos));
        }
        if (unit.EmpUntilRound >= State.Round)
        {
            unit.Ap = 0;
            State.Log(new(State.Round, ScavengeEventType.EmpBurst, $"{unit.Name} is still down from the EMP.", unit.Id, Pos: unit.Pos));
        }
    }

    /// <summary>Bleeding ticks down, then everyone conscious (except idle NPCs) is ordered by initiative.</summary>
    private void StartRound()
    {
        State.Round++;
        State.SubRound = 1;
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

        // Drones fly on their operator's AP and cameras only watch: neither takes turns.
        State.TurnOrder = State.Units
            .Where(u => u.IsActive && u.Side != Unit.NeutralSide && u.Kind is not (UnitKind.Drone or UnitKind.Decoy)
                        && (u.Kind != UnitKind.Device || Profile(u).Actions.Count > 0))
            .Select(u => (u.Id, Profile(u).Initiative))
            .OrderByDescending(t => t.Initiative)
            .ThenBy(t => t.Id)
            .Select(t => t.Id)
            .ToList();
        State.TurnIndex = 0;
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
        if (State.CurrentUnit is not { IsActive: true })
        {
            State.TurnIndex++;
            AdvanceTurns();
        }
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

    private bool _reacting;

    /// <summary>Records which party sides see an event's actor right now (see <see cref="FogLog"/>).</summary>
    private ScavengeEvent StampSeenBy(ScavengeEvent e)
    {
        if (e.UnitId is not { } id || State.FindUnit(id) is not { } actor) return e;
        return e with { SeenBy = Detection.PartySides(State).Where(side => Detection.SideSees(Data, State, side, actor)).ToList() };
    }

    /// <summary>
    /// After a unit moved (player or AI): motion sensors it entered reveal it to their side, sides it is unseen by but
    /// close to hear it (movement contact), and overwatch may fire on it.
    /// </summary>
    /// <param name="forced">Moved by a card (shoved, swapped, stepped): sensors, noise, cameras and raised ground as for any
    /// move, but overwatch does not fire at it.</param>
    internal void Moved(Unit unit, Pos from, bool forced = false)
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
        if (!forced) CheckOverwatch(unit);
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

    /// <summary>
    /// Overwatch: every unit on overwatch hostile to <paramref name="unit"/> that sees it now (by its own eyes, or as
    /// revealed to its side, <see cref="Detection.SeesRevealed"/>) and reaches it fires one reaction, by id, while the unit stands. A
    /// reaction does not set off further reactions.
    /// </summary>
    internal void CheckOverwatch(Unit unit)
    {
        if (_reacting || !unit.IsActive) return;
        _reacting = true;
        try
        {
            foreach (var watcher in State.Units.Where(u => u.IsActive && u.Stance == Stance.Overwatch && u.Reactions > 0 && u.HostileTo(unit))
                         .OrderBy(u => u.Id).ToList())
            {
                if (!unit.IsActive) break;
                if (!watcher.IsActive || watcher.Reactions <= 0) continue;
                var sees = Detection.Spots(Data, State, watcher, unit) || Detection.SeesRevealed(Data, State, watcher, unit)
                           || ElectronicWarfare.SharesOverwatch(this, watcher, unit);
                if (!sees) continue;
                var action = Profile(watcher).Actions
                    .Where(a => Drones.CanStrike(a, unit) && Combat.CheckReach(Data, State.Map, watcher.Pos, a, unit.Pos) == ScavengeError.None)
                    .OrderByDescending(a => Combat.ChanceToHit(this, watcher, a, unit) * (a.MinDamage + a.MaxDamage) / 2)
                    .ThenBy(a => a.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (action is null) continue;
                watcher.Reactions--;
                State.Log(new(State.Round, ScavengeEventType.Overwatch,
                    $"{watcher.Name} reacts on overwatch: {action.Name} at {unit.Name}!", watcher.Id, unit.Id, unit.Pos,
                    Anonymous: $"Overwatch fire from the fog at {unit.Name}!"));
                Combat.Resolve(this, watcher, action, unit, reaction: true);
            }
        }
        finally
        {
            _reacting = false;
        }
    }

    internal void Log(ScavengeEventType type, string message, Unit? unit = null, Unit? target = null) =>
        State.Log(new(State.Round, type, message, unit?.Id, target?.Id, (target ?? unit)?.Pos));

    internal ItemDef Item(string id) => Data.Items[id];

    private void SyncRng() => State.RngState = _rng.State;
}
