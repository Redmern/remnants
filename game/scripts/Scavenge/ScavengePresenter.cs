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
        driver.AllianceNews += text => AllianceNews?.Invoke(text);
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

    /// <summary>Alliance news for a banner (remote only).</summary>
    public event Action<string>? AllianceNews;

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

    /// <summary>Another player's unit on the local team (co-op): shares sight, takes their orders.</summary>
    public bool IsAlly(Unit u) => u.PlayerId is { } pid && pid != PlayerId && u.Side == Side && u.Side != Unit.NeutralSide;

    public string PlayerName(int playerId) => Driver.PlayerName(playerId);

    /// <summary>Players on the local team besides this one.</summary>
    public IEnumerable<int> Allies => State.PlayersOn(Side).Where(id => id != PlayerId);

    /// <summary>The local team's pings.</summary>
    public IEnumerable<Ping> Pings => State.Pings.Where(p => p.Side == Side);

    /// <summary>Alliance offers made to the local player that are still open.</summary>
    public IEnumerable<AllianceProposal> OffersToMe => State.Proposals.Where(p => p.ToPlayerId == PlayerId);

    /// <summary>Breaks announced on the local team (by anyone on it).</summary>
    public IEnumerable<AllianceBreak> TeamBreaks => State.Breaks.Where(b => b.Side == Side);

    /// <summary>Other players' parties still in the zone that are not on the local team.</summary>
    public IEnumerable<Party> Rivals => State.Parties.Where(p => p.PlayerId != PlayerId && !p.Done && p.Side != Side);

    /// <summary>Downed units on the local side next to <paramref name="unit"/> that it could lift.</summary>
    public IEnumerable<Unit> CarryableNear(Unit unit) =>
        State.Units.Where(u => u.Id != unit.Id && u.Status == UnitStatus.Downed && u.CarriedById is null && u.Side == Side && u.Pos.Chebyshev(unit.Pos) <= 1);

    /// <summary>Conscious survivors on the local side next to <paramref name="unit"/> it could hand items to (allies first).</summary>
    public IEnumerable<Unit> GiveTargetsNear(Unit unit) =>
        State.Units.Where(u => u.Id != unit.Id && u.IsActive && u.Kind == UnitKind.Survivor && u.Side == Side && u.Pos.Chebyshev(unit.Pos) <= 1)
            .OrderBy(u => IsAlly(u) ? 0 : 1).ThenBy(u => u.Id);

    /// <summary>A free tile next to the unit to put a carried one down on, or null.</summary>
    public Pos? PutDownSpot(Unit unit)
    {
        var map = State.Map;
        foreach (var d in Pos.Neighbours8)
            if (unit.Pos + d is var p && map.InBounds(p) && Sim.Data.Terrain(map[p]).Passable && !map.IsExit(p) && State.UnitAt(p) is null)
                return p;
        return null;
    }

    /// <summary>Tiles <paramref name="unit"/> can move to with one move action, with AP cost (Core's movement rules).</summary>
    public Dictionary<Pos, int> Reachable(Unit unit) => unit.IsActive ? Sim.Reachable(unit) : new();

    /// <summary>Visible hostile targets in reach of <paramref name="action"/>, with Core's hit chance.</summary>
    public List<AttackOption> AttackOptions(Unit unit, ActionProfile action)
    {
        var data = Sim.Data;
        var map = State.Map;
        return State.Units
            .Where(t => t.OnMap && unit.HostileTo(t) && CanSee(t) && Drones.CanStrike(action, t)
                        && Combat.CheckReach(data, map, unit.Pos, action, t.Pos) == ScavengeError.None)
            .Select(t => new AttackOption(t, action, Bestiary.ShownChance(Sim.Data.Rules.Bestiary, Combat.ChanceToHit(Sim, unit, action, t), KnowledgeOf(t)), Combat.AmbushAgainst(data, State, unit, t)))
            .ToList();
    }

    /// <summary>Seen enemy plans as telegraphs (Core's <see cref="TacticsView"/>; never the card).</summary>
    public List<Telegraph> Telegraphs() => TacticsView.Telegraphs(Sim, Side);

    /// <summary>The threat overlay: planned blows of seen enemies, and what they could hit from where they stand.</summary>
    public Dictionary<Pos, ThreatKind> Threats() => TacticsView.Threats(Sim, Side);

    /// <summary>For each tile <paramref name="unit"/> can move to, its best hit chance at <paramref name="target"/> from there.</summary>
    public Dictionary<Pos, double> MovePreview(Unit unit, Unit target) => TacticsView.MovePreview(Sim, unit, target, KnowledgeOf(target));

    /// <summary>The local player's hand strip (null without a deck).</summary>
    public HandView? Hand() => TacticsView.Hand(Sim, PlayerId);

    /// <summary>The character picker for a card: who can take it now, and why the others cannot.</summary>
    public List<PickerRow> Picker(string cardId, bool boost, int? target, bool fromSlot) =>
        TacticsView.Picker(Sim, PlayerId, cardId, boost, target, fromSlot);

    /// <summary>
    /// Every unit or tile an ability (or reaction) card played by <paramref name="unitId"/> could be aimed at, as the side
    /// knows them: Core's reason where it cannot, the landing tile, the units touched and the outcome where it can. Empty
    /// for a card played on the character itself.
    /// </summary>
    public List<CardTargetOption> Targets(int unitId, string cardId, bool boost, bool fromSlot) =>
        TacticsView.Targets(Sim, PlayerId, unitId, cardId, boost, fromSlot);

    /// <summary>Whether a card is aimed (on the map, after picking who plays it): an ability at a unit or tile, or a reaction.</summary>
    public bool IsAimed(string cardId) => Sim.Data.Cards.Find(cardId) is { } card && (card.Reaction || card.Target != CardTarget.Self);

    /// <summary>Seen enemies with a plan (what a reaction card answers).</summary>
    public List<Unit> ReactionTargets() => TacticsView.ReactionTargets(Sim, PlayerId);

    /// <summary>
    /// The bestiary tier the local player knows an enemy type at: the colony's (<see cref="BestiaryKnowledge"/>) plus this
    /// run's encounters. Units that are not bestiary subjects (people, other players) are always known as seen.
    /// </summary>
    public BestiaryTier KnowledgeOf(Unit unit) => Bestiary.Knowledge(Sim, BestiaryKnowledge.Colony, PlayerId, unit);

    /// <summary>Caches the unit can loot from (its tile or next to it).</summary>
    public IEnumerable<LootCache> CachesInReach(Unit unit) => State.Caches.Where(c => c.Pos.Chebyshev(unit.Pos) <= 1);

    /// <summary>Piles the local side knows of: on tiles it has seen (a remote view holds only those, as last seen).</summary>
    public IEnumerable<LootCache> KnownPiles => State.Caches.Where(c => IsExplored(c.Pos));

    /// <summary>Salvage nodes on tiles the local side has seen.</summary>
    public IEnumerable<SalvageNode> KnownNodes => State.SalvageNodes.Where(n => IsExplored(n.Pos));

    /// <summary>Salvage nodes with something left next to the unit.</summary>
    public IEnumerable<SalvageNode> NodesInReach(Unit unit) => KnownNodes.Where(n => !n.Depleted && n.Pos.Chebyshev(unit.Pos) <= 1);

    /// <summary>Unrecruited NPC survivors standing next to the unit.</summary>
    public IEnumerable<Unit> RecruitableNear(Unit unit) =>
        State.Units.Where(u => u.Kind == UnitKind.Npc && u.IsActive && u.Side == Unit.NeutralSide && u.Pos.Chebyshev(unit.Pos) <= 1);

    /// <summary>Devices the local side knows of (seen, or on its map from a terminal): a remote view only holds those.</summary>
    public IEnumerable<ZoneDevice> KnownDevices => State.Devices.Where(d => IsExplored(d.Pos));

    /// <summary>The side a device works for, as the local player knows it.</summary>
    public string ControllerOf(ZoneDevice d) => Hacking.ControllerOf(State, d);

    /// <summary>A hack the unit can start or continue now: a device in its tool's reach, or with Override a Linked.</summary>
    public sealed record HackOption(ZoneDevice? Device, Unit? Target, string Name, double Chance, int Done, int Needed);

    public List<HackOption> HackOptions(Unit unit)
    {
        var data = Sim.Data;
        if (Hacking.Tool(data, unit)?.HackTool is not { } kit) return [];
        var map = State.Map;
        bool InReach(Pos p) => p.Chebyshev(unit.Pos) <= kit.Range && (p.Chebyshev(unit.Pos) <= 1 || map.HasLineOfSight(data, unit.Pos, p));
        var options = KnownDevices
            .Where(d => IsVisible(d.Pos) && InReach(d.Pos) && Hacking.Working(State, d) && !d.Used && ControllerOf(d) != Side)
            .Select(d => new HackOption(d, null, ScavengeText.Device(d.Kind), Hacking.Chance(Sim, unit, d),
                Hacking.ProgressOf(d, Side)?.Done ?? 0, data.Device(d.Kind).Work))
            .ToList();
        if (unit.Hook(Remnants.Core.Colony.PerkHooks.CanHackLinked) > 0)
            options.AddRange(State.Units.Where(t => t.IsActive && t.Kind == UnitKind.Enemy && unit.HostileTo(t) && CanSee(t) && Network.IsLinked(data, t) && InReach(t.Pos))
                .Select(t => new HackOption(null, t, $"Override {t.Name}", Hacking.Chance(Sim, unit, null, t), 0, 1)));
        return options;
    }

    /// <summary>Free tiles next to the unit a drone can take off onto.</summary>
    public IEnumerable<Pos> DeployTiles(Unit unit) => Pos.Neighbours8.Select(d => unit.Pos + d).Where(p => Drones.CanHover(Sim, p));

    private void RefreshVisibility()
    {
        _visible = Driver.Visible;
        _seenUnits = Driver.SeenUnits;
    }
}
