using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>
/// A hostile unit the acting unit can hit with an action, and the chance Core gives it. <see cref="Ambush"/>: the
/// target's side does not see the attacker, so it would strike from the fog (bonus included in the chance).
/// </summary>
public sealed record AttackOption(Unit Target, ActionProfile Action, double HitChance, AmbushInfo? Ambush = null);

/// <summary>
/// Presenter between the scavenge screen and Core: plays one local player through an <see cref="IScavengeDriver"/>
/// (an in-process <see cref="ScavengeSim"/>, or the multiplayer server), sends every command, answers the UI's
/// questions by asking Core (reach, hit chance, fog), and hands out the events each command produced so the views
/// can replay them. Holds no game rules.
/// </summary>
public sealed class ScavengePresenter
{
    private HashSet<int> _visible = [];
    private HashSet<int> _seenUnits = [];

    public ScavengePresenter(IScavengeDriver driver)
    {
        Driver = driver;
        driver.Pushed += () =>
        {
            RefreshVisibility();
            Pushed?.Invoke();
        };
        driver.Noticed += text => Feedback?.Invoke(text, false);
        RefreshVisibility();
    }

    public IScavengeDriver Driver { get; }

    /// <summary>The sim to ask; for a remote game a fresh read model after every server update, so never cache it.</summary>
    public ScavengeSim Sim => Driver.Sim;
    public int PlayerId => Driver.PlayerId;
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

    /// <summary>The game moved on without a local command (remote: another player acted, a turn timed out).</summary>
    public event Action? Pushed;

    public async Task<ScavengeResult> Execute(IScavengeCommand command)
    {
        var result = await Driver.Execute(command);
        if (!result.Ok) Feedback?.Invoke($"{ScavengeText.Error(result.Error)}: {result.Message}", true);
        RefreshVisibility();
        return result;
    }

    /// <summary>Events since the last call, oldest first.</summary>
    public List<ScavengeEvent> TakeNewEvents() => Driver.TakeNewEvents();

    /// <summary>The local player's result; null while a remote one is still on its way.</summary>
    public ExtractionResult? Result() => Driver.Result();

    public bool IsVisible(Pos p) => State.Map.InBounds(p) && _visible.Contains(State.Map.Index(p));
    public bool IsExplored(Pos p) => State.Map.InBounds(p) && Explored.Contains(State.Map.Index(p));

    /// <summary>Whether the local player may see this unit: own side always, others only when the side sees them (not just their tile).</summary>
    public bool CanSee(Unit u) => u.OnMap && (u.Side == Side || _seenUnits.Contains(u.Id));

    /// <summary>The local side's contacts: "something is there" without what.</summary>
    public IEnumerable<Contact> Contacts => State.Contacts.Where(c => c.Side == Side);

    /// <summary>Whether a hostile unit is revealed to the local side (muzzle flash, motion sensor).</summary>
    public bool IsRevealed(Unit u) => Detection.IsRevealed(State, u.Id, Side);

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
            .Select(t => new AttackOption(t, action, Combat.ChanceToHit(Sim, unit, action, t), Combat.AmbushAgainst(data, State, unit, t)))
            .ToList();
    }

    /// <summary>Caches the unit can loot from (its tile or next to it).</summary>
    public IEnumerable<LootCache> CachesInReach(Unit unit) => State.Caches.Where(c => c.Pos.Chebyshev(unit.Pos) <= 1);

    /// <summary>Unrecruited NPC survivors standing next to the unit.</summary>
    public IEnumerable<Unit> RecruitableNear(Unit unit) =>
        State.Units.Where(u => u.Kind == UnitKind.Npc && u.IsActive && u.Side == Unit.NeutralSide && u.Pos.Chebyshev(unit.Pos) <= 1);

    private void RefreshVisibility()
    {
        _visible = Driver.Visible;
        _seenUnits = Driver.SeenUnits;
    }
}
