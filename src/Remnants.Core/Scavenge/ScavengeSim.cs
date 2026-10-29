using Remnants.Core.Items;
using Remnants.Core.Random;

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
    }

    /// <summary>Sim-owned randomness. Always draw through this so the state stays in the save.</summary>
    public DeterministicRng Rng => _rng;

    public UnitProfile Profile(Unit unit) => ProfileDeriver.Derive(Data, unit);

    /// <summary>Tiles the unit can move to with one move action now, with AP cost (for UI highlights).</summary>
    public Dictionary<Pos, int> Reachable(Unit unit) => Movement.Reachable(State, unit, Profile(unit).Movement);

    /// <summary>Tile indices the player's side sees right now.</summary>
    public HashSet<int> VisibleTo(int playerId) =>
        State.FindParty(playerId) is { } party ? Visibility.SideVisible(Data, State, party.Side) : [];

    public ExtractionResult ResultFor(int playerId) => ExtractionResult.Build(this, playerId);

    /// <summary>Starts round 1 and runs AI turns until the first player-controlled unit is up.</summary>
    public void Start()
    {
        if (State.Round == 0) AdvanceTurns();
        Visibility.UpdateExplored(Data, State);
        SyncRng();
    }

    public ScavengeResult Execute(IScavengeCommand command)
    {
        var result = ScavengeCommandHandlers.Validate(this, command, out var unit) ?? command switch
        {
            Move c => ScavengeCommandHandlers.Move(this, unit!, c),
            Attack c => ScavengeCommandHandlers.Attack(this, unit!, c),
            UseItem c => ScavengeCommandHandlers.UseItem(this, unit!, c),
            PickUp c => ScavengeCommandHandlers.PickUp(this, unit!, c),
            Recruit c => ScavengeCommandHandlers.Recruit(this, unit!, c),
            EndTurn => ScavengeResult.Success($"{unit!.Name} ends their turn."),
            Extract => ScavengeCommandHandlers.Extract(this, unit!),
            _ => ScavengeResult.Fail(ScavengeError.UnknownCommand, $"Unknown command {command.GetType().Name}."),
        };

        if (result.Ok)
        {
            UpdateParties();
            // The acting unit's turn ends on EndTurn, or when it left the map or can no longer act.
            if (command is EndTurn || unit is not { IsActive: true })
            {
                State.TurnIndex++;
                AdvanceTurns();
            }
            Visibility.UpdateExplored(Data, State);
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
                StartRound();
                continue;
            }
            var unit = State.CurrentUnit;
            if (unit is not { IsActive: true })
            {
                State.TurnIndex++;
                continue;
            }
            unit.Ap = Profile(unit).MaxAp;
            if (unit.Suppressed)
            {
                unit.Suppressed = false;
                unit.Ap = Math.Max(1, unit.Ap - Data.Rules.Network.SuppressApPenalty);
                State.Log(new(State.Round, ScavengeEventType.Suppressed, $"{unit.Name} is pinned down ({unit.Ap} AP).", unit.Id, Pos: unit.Pos));
            }
            if (unit.PlayerControlled) return;

            ScavengeAi.TakeTurn(this, unit);
            UpdateParties();
            State.TurnIndex++;
        }
    }

    /// <summary>Bleeding ticks down, then everyone conscious (except idle NPCs) is ordered by initiative.</summary>
    private void StartRound()
    {
        State.Round++;
        State.Log(new(State.Round, ScavengeEventType.RoundStarted, $"Round {State.Round}."));
        foreach (var u in State.Units.Where(u => u.Status == UnitStatus.Downed && !u.Stabilized).ToList())
        {
            u.BleedOut--;
            if (u.BleedOut <= 0) Kill(u, ScavengeEventType.BledOut, $"{u.Name} bled out.");
        }
        UpdateParties();
        Network.RoundStarted(this);

        State.TurnOrder = State.Units
            .Where(u => u.IsActive && u.Side != Unit.NeutralSide)
            .Select(u => (u.Id, Profile(u).Initiative))
            .OrderByDescending(t => t.Initiative)
            .ThenBy(t => t.Id)
            .Select(t => t.Id)
            .ToList();
        State.TurnIndex = 0;
    }

    /// <summary>
    /// A party with no conscious player-controlled unit left is done: its downed are left behind and die,
    /// its recruits stay behind as strangers. When every party is done the instance is finished.
    /// </summary>
    internal void UpdateParties()
    {
        foreach (var party in State.Parties.Where(p => !p.Done))
        {
            var mine = State.Units.Where(u => u.PlayerId == party.PlayerId).ToList();
            if (mine.Any(u => u.PlayerControlled && u.IsActive)) continue;

            party.Done = true;
            foreach (var u in mine.Where(u => u.Status == UnitStatus.Downed && u.Kind == UnitKind.Survivor))
                Kill(u, ScavengeEventType.Died, $"{u.Name} was left behind.");
            foreach (var u in mine.Where(u => u.OnMap && u.Kind == UnitKind.Npc))
            {
                u.Side = Unit.NeutralSide;
                u.PlayerId = null;
                u.FollowingId = null;
            }
            State.Log(new(State.Round, ScavengeEventType.PartyDone, $"Player {party.PlayerId}'s run is over."));
        }
        if (!State.Finished && State.Parties.All(p => p.Done))
        {
            State.Finished = true;
            State.Log(new(State.Round, ScavengeEventType.Finished, "Everyone has left the zone."));
        }
    }

    /// <summary>Dead units drop what is worth taking into a cache on their tile.</summary>
    internal void Kill(Unit unit, ScavengeEventType type, string message)
    {
        unit.Status = UnitStatus.Dead;
        unit.Hp = Math.Min(unit.Hp, 0);
        unit.Ap = 0;
        State.Log(new(State.Round, type, message, unit.Id, Pos: unit.Pos));

        var drops = unit.AllItems.Where(id => unit.Kind != UnitKind.Enemy || Data.Items[id].LootWeight > 0).ToList();
        if (drops.Count == 0) return;
        var cache = State.Caches.Find(c => c.Pos == unit.Pos);
        if (cache is null)
        {
            cache = new LootCache { Id = State.NextCacheId++, Pos = unit.Pos };
            State.Caches.Add(cache);
        }
        cache.Items.AddRange(drops);
    }

    /// <summary>Idle AI enemies within the radius that are hostile to the source start hunting toward it.</summary>
    internal void MakeNoise(Unit source, int radius)
    {
        foreach (var u in State.Units.Where(u => u.IsActive && u.Kind == UnitKind.Enemy && u.Ai.Mode == AiMode.Idle))
        {
            if (!u.HostileTo(source) || u.Pos.Chebyshev(source.Pos) > radius) continue;
            u.Ai.Mode = AiMode.Hunting;
            u.Ai.TargetId = null;
            u.Ai.LastKnown = source.Pos;
        }
    }

    internal void Log(ScavengeEventType type, string message, Unit? unit = null, Unit? target = null) =>
        State.Log(new(State.Round, type, message, unit?.Id, target?.Id, (target ?? unit)?.Pos));

    internal ItemDef Item(string id) => Data.Items[id];

    private void SyncRng() => State.RngState = _rng.State;
}
