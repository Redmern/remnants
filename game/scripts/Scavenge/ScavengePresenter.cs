using System;
using System.Collections.Generic;
using System.Linq;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>A hostile unit the acting unit can hit with an action, and the chance Core gives it.</summary>
public sealed record AttackOption(Unit Target, ActionProfile Action, double HitChance);

/// <summary>
/// Presenter between the scavenge screen and Core: owns the <see cref="ScavengeSim"/> for one local player,
/// sends every command, answers the UI's questions by asking Core (reach, hit chance, fog), and hands out
/// the events each command produced so the views can replay them. Holds no game rules.
/// </summary>
public sealed class ScavengePresenter
{
    private ScavengeEvent? _lastSeen;
    private HashSet<int> _visible = [];

    public ScavengePresenter(ScavengeSim sim, int playerId)
    {
        Sim = sim;
        PlayerId = playerId;
        var events = sim.State.Events;
        _lastSeen = events.Count > 0 ? events[^1] : null;
        RefreshVisibility();
    }

    public ScavengeSim Sim { get; }
    public int PlayerId { get; }
    public ScavengeState State => Sim.State;
    public Party Party => State.FindParty(PlayerId)!;
    public string Side => Party.Side;

    /// <summary>The local run is over (everyone extracted, evacuated, down or dead).</summary>
    public bool Done => Party.Done || State.Finished;

    /// <summary>The unit whose turn it is, if the local player controls it.</summary>
    public Unit? ActingUnit => State.CurrentUnit is { PlayerControlled: true } u && u.PlayerId == PlayerId ? u : null;

    /// <summary>Tiles the local side sees right now.</summary>
    public IReadOnlySet<int> Visible => _visible;

    /// <summary>Tiles the local side has ever seen.</summary>
    public IReadOnlySet<int> Explored =>
        State.Explored.TryGetValue(Side, out var explored) ? explored : (IReadOnlySet<int>)new HashSet<int>();

    /// <summary>Command outcome for the log and toast: text and whether it failed.</summary>
    public event Action<string, bool>? Feedback;

    public ScavengeResult Execute(IScavengeCommand command)
    {
        var result = Sim.Execute(command);
        if (!result.Ok) Feedback?.Invoke($"{ScavengeText.Error(result.Error)}: {result.Message}", true);
        RefreshVisibility();
        return result;
    }

    /// <summary>Core events logged since the last call, oldest first (the list is capped, so found by identity).</summary>
    public List<ScavengeEvent> TakeNewEvents()
    {
        var events = State.Events;
        var start = 0;
        if (_lastSeen is not null)
            for (var i = events.Count - 1; i >= 0; i--)
                if (ReferenceEquals(events[i], _lastSeen)) { start = i + 1; break; }
        var fresh = events.Skip(start).ToList();
        if (events.Count > 0) _lastSeen = events[^1];
        return fresh;
    }

    public bool IsVisible(Pos p) => State.Map.InBounds(p) && _visible.Contains(State.Map.Index(p));
    public bool IsExplored(Pos p) => State.Map.InBounds(p) && Explored.Contains(State.Map.Index(p));

    /// <summary>Whether the local player may see this unit: own side always, others only on visible tiles.</summary>
    public bool CanSee(Unit u) => u.OnMap && (u.Side == Side || IsVisible(u.Pos));

    public bool IsMine(Unit u) => u.PlayerId == PlayerId;

    /// <summary>Tiles <paramref name="unit"/> can move to with one move action, with AP cost (Core's movement rules).</summary>
    public Dictionary<Pos, int> Reachable(Unit unit) => unit.IsActive ? Sim.Reachable(unit) : new();

    /// <summary>Visible hostile targets in reach of <paramref name="action"/>, with Core's hit chance.</summary>
    public List<AttackOption> AttackOptions(Unit unit, ActionProfile action)
    {
        var data = Sim.Data;
        var map = State.Map;
        return State.Units
            .Where(t => t.OnMap && unit.HostileTo(t) && CanSee(t)
                        && Combat.CheckReach(data, map, unit.Pos, action, t.Pos) == ScavengeError.None)
            .Select(t => new AttackOption(t, action, Combat.HitChance(data, map, unit.Pos, action, t.Pos, Sim.Profile(t))))
            .ToList();
    }

    /// <summary>Caches the unit can loot from (its tile or next to it).</summary>
    public IEnumerable<LootCache> CachesInReach(Unit unit) => State.Caches.Where(c => c.Pos.Chebyshev(unit.Pos) <= 1);

    /// <summary>Unrecruited NPC survivors standing next to the unit.</summary>
    public IEnumerable<Unit> RecruitableNear(Unit unit) =>
        State.Units.Where(u => u.Kind == UnitKind.Npc && u.IsActive && u.Side == Unit.NeutralSide && u.Pos.Chebyshev(unit.Pos) <= 1);

    private void RefreshVisibility() => _visible = Sim.VisibleTo(PlayerId);
}
