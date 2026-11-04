using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Playtest;

/// <summary>What one scavenge run looked like from the party's side.</summary>
public sealed record RunStats(int Rounds, int Kills, int LinkedSeen, int LinkedKills, int ItemsLooted, int Reinforcements,
    int AmbushedTimes = 0, int OverwatchShots = 0, int DefensesUsed = 0,
    int Hacked = 0, int HackFailed = 0, int Overrides = 0, int DroneFlights = 0, int DronesLost = 0, int Marks = 0, int FromDevices = 0,
    int LimbLocks = 0, int Emps = 0, int Decoys = 0, int NoiseDecoys = 0, int DroneHeals = 0, int Ferried = 0, int DroneShots = 0,
    int Salvaged = 0, int PilesLooted = 0)
{
    /// <summary>Plans and cards (counted from every event the run logged, not just the last <see cref="ScavengeState.MaxEvents"/>).</summary>
    public PlanCardStats Tactics { get; init; } = new();
}

/// <summary>
/// Telegraphed plans by everyone (<see cref="EnemyDeclared"/>: by enemies), cards the party played, the damage the party
/// dealt and took, and the hit tiers (graze, hit, solid) of the attacks it landed and took.
/// </summary>
public sealed record PlanCardStats
{
    public int Declared { get; set; }
    public int EnemyDeclared { get; set; }
    public int Resolved { get; set; }
    public int Fizzled { get; set; }
    public int Canceled { get; set; }
    public int CardsPlayed { get; set; }
    public int EnemyCardsPlayed { get; set; }
    public double DamageDealt { get; set; }
    public double DamageTaken { get; set; }
    public int[] TiersDealt { get; set; } = new int[3];
    public int[] TiersTaken { get; set; } = new int[3];

    /// <summary>Attacks (and suppressing fire) by enemies, and enemy-rounds (active enemies counted at each round start).</summary>
    public int EnemyAttacks { get; set; }
    public int EnemyRounds { get; set; }

    /// <summary>Units the party's cards moved (push, pull, swap, spin) and turns they reordered; enemy cards' alike.</summary>
    public int Displaced { get; set; }
    public int Reordered { get; set; }
    public int EnemyDisplaced { get; set; }

    /// <summary>The party's card plays by card name.</summary>
    public SortedDictionary<string, int> CardsByName { get; set; } = new(StringComparer.Ordinal);

    public void Add(ScavengeState state, ScavengeEvent e, ISet<int> mine)
    {
        bool Mine(int? id) => id is { } i && mine.Contains(i);
        bool Enemy(int? id) => id is { } i && state.FindUnit(i)?.Kind is UnitKind.Enemy or UnitKind.Device;
        if (Enemy(e.UnitId) && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed or ScavengeEventType.Suppressed && e.TargetId is not null)
            EnemyAttacks++;
        switch (e.Type)
        {
            case ScavengeEventType.RoundStarted:
                EnemyRounds += state.Units.Count(u => u.IsActive && u.Kind is UnitKind.Enemy or UnitKind.Device);
                break;
            case ScavengeEventType.IntentDeclared:
                Declared++;
                if (Enemy(e.UnitId)) EnemyDeclared++;
                break;
            case ScavengeEventType.IntentResolved: Resolved++; break;
            case ScavengeEventType.IntentFizzled: Fizzled++; break;
            case ScavengeEventType.IntentCanceled: Canceled++; break;
            case ScavengeEventType.CardPlayed:
                if (Mine(e.UnitId))
                {
                    CardsPlayed++;
                    var name = System.Text.RegularExpressions.Regex.Match(e.Message, @" plays (.+?)(?: \(boosted\))?:").Groups[1].Value;
                    CardsByName[name] = CardsByName.GetValueOrDefault(name) + 1;
                }
                else EnemyCardsPlayed++;
                break;
            case ScavengeEventType.Displaced:
                if (Mine(e.UnitId)) Displaced++;
                else if (Enemy(e.UnitId)) EnemyDisplaced++;
                break;
            case ScavengeEventType.InitiativeChanged when Mine(e.UnitId):
                Reordered++;
                break;
            case ScavengeEventType.Attacked when e.Tier is { } tier:
                if (Mine(e.UnitId))
                {
                    DamageDealt += e.Amount ?? 0;
                    TiersDealt[(int)tier]++;
                }
                if (Mine(e.TargetId))
                {
                    DamageTaken += e.Amount ?? 0;
                    TiersTaken[(int)tier]++;
                }
                break;
        }
    }

    public void Add(PlanCardStats o)
    {
        Declared += o.Declared; EnemyDeclared += o.EnemyDeclared; Resolved += o.Resolved; Fizzled += o.Fizzled; Canceled += o.Canceled;
        CardsPlayed += o.CardsPlayed; EnemyCardsPlayed += o.EnemyCardsPlayed; DamageDealt += o.DamageDealt; DamageTaken += o.DamageTaken;
        EnemyAttacks += o.EnemyAttacks; EnemyRounds += o.EnemyRounds;
        Displaced += o.Displaced; Reordered += o.Reordered; EnemyDisplaced += o.EnemyDisplaced;
        foreach (var (name, count) in o.CardsByName) CardsByName[name] = CardsByName.GetValueOrDefault(name) + count;
        for (var i = 0; i < 3; i++) { TiersDealt[i] += o.TiersDealt[i]; TiersTaken[i] += o.TiersTaken[i]; }
    }
}

/// <summary>
/// Plays the colony's party in a scavenge run through Core commands only, roughly like a careful player:
/// revive or patch up allies, fight what it sees, loot the nearest cache, and head for an exit once hurt, loaded
/// or out of caches. It knows where caches are (a player would find them by exploring), not where enemies are, and
/// only fights what its side actually sees (stealth hides the Linked). Against the fog (unless <c>defenses</c> is off)
/// it scans (Lookout) when it hears something it cannot see, throws a flare at where an ambush came from, keeps close to
/// an ally, and ends idle turns on overwatch, or braced once it has been shot at from the fog.
/// With a hacking tool (unless <c>tech</c> is off) it hacks lockers and terminals it can reach when nothing hostile is in
/// sight and the chance is decent (<see cref="HackFloor"/>), a camera that watches it, a relay node once the Linked are
/// about, and with Override a Linked next to it; it walks to devices worth hacking like to caches. With a scout drone it
/// launches it when the coast is clear, flies it ahead toward the next goal once a turn, marks a Linked it sees (Spotter),
/// and recalls it before the battery runs flat or when leaving.
/// Loot: it takes everything that fits from a pile (Take all), the most valuable piece first when it is nearly full,
/// and (unless <see cref="Salvage"/> is off) works salvage nodes it has seen when nothing hostile is in sight and it
/// has room for a lot.
/// </summary>
public static class PartyBot
{
    /// <summary>Units pathing around other units until this round (they were boxed in).</summary>
    private static readonly Dictionary<int, int> Detours = new();

    /// <summary>Whether the bot salvages (<c>--no-salvage</c> turns it off, for before/after comparisons).</summary>
    public static bool Salvage { get; set; } = true;

    /// <summary>Whether the bot steps off tiles seen enemy plans aim at (<c>--no-dodge</c> turns it off, for comparisons).</summary>
    public static bool Dodge { get; set; } = true;
    /// <summary>Benchmarks: called after every command the bot sends, with the wall-clock milliseconds its Execute took (AI turns included).</summary>
    public static Action<IScavengeCommand, double>? Timed { get; set; }

    /// <summary>Room (kg) the bot wants free under <see cref="SalvageLoad"/> of its capacity before it starts on a salvage node.</summary>
    private const double SalvageRoom = 5;

    /// <summary>The share of carry capacity the bot salvages up to (heavier slows it down a lot: the encumbrance steps).</summary>
    private const double SalvageLoad = 0.8;

    /// <summary>Over this share of capacity the bot drops its least valuable weight (it can hardly walk).</summary>
    private const double DropLoad = 0.85;

    private static bool RoomToSalvage(UnitProfile profile) => profile.CarryCapacity * SalvageLoad - profile.Load >= SalvageRoom;

    /// <summary>
    /// Salvage is loud and slow, so a careful player works a node only when the unit is in good shape, nothing has been
    /// heard for a couple of rounds and the run is not getting late.
    /// </summary>
    private static bool SafeToSalvage(ScavengeState state, Unit unit) =>
        unit.Hp >= unit.MaxHp * 0.75 && state.Round <= MaxRounds * 0.6
        && !state.Contacts.Any(c => c.Side == unit.Side && c.Round >= state.Round - 2);

    private const int MaxCommands = 20000;
    private const int MaxRounds = 40;

    /// <summary>A run still going this long is stuck; the bot gives up (the result stays incomplete).</summary>
    private const int StallRounds = 300;

    /// <summary>Plays the party until it is done. <paramref name="trace"/> gets every scavenge event as it happens.</summary>
    public static (ExtractionResult Result, RunStats Stats) Play(ScavengeSim sim, int playerId, Action<string>? trace = null, bool recruit = true,
        bool defenses = true, bool fightLinked = false)
    {
        var (results, stats) = PlayAll(sim, [playerId], trace, recruit, defenses, fightLinked);
        return (results[0], stats);
    }

    /// <summary>
    /// Plays several parties (co-op: allies on one team) until all are done, each turn for whichever of them is up.
    /// Allies count as the bot's own for healing and keeping close (it treats its side, not its player, as "us").
    /// </summary>
    public static (List<ExtractionResult> Results, RunStats Stats) PlayAll(ScavengeSim sim, IReadOnlyList<int> playerIds, Action<string>? trace = null,
        bool recruit = true, bool defenses = true, bool fightLinked = false, bool tech = true)
    {
        Detours.Clear();
        ScavengeEvent? last = null;
        var tactics = new PlanCardStats();
        var party = sim.State.Units.Where(u => u.PlayerId is { } p && playerIds.Contains(p)).Select(u => u.Id).ToHashSet();
        ScavengeEvent? tallied = null;
        // Every event once, as it is logged (the state keeps only the last MaxEvents).
        void Tally()
        {
            var log = sim.State.Events;
            var start = 0;
            if (tallied is not null)
                for (var i = log.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(log[i], tallied)) { start = i + 1; break; }
            foreach (var u in sim.State.Units.Where(u => u.PlayerId is { } p && playerIds.Contains(p))) party.Add(u.Id);
            foreach (var e in log.Skip(start)) tactics.Add(sim.State, e, party);
            if (log.Count > 0) tallied = log[^1];
        }
        Tally();
        void Flush()
        {
            if (trace is null) return;
            var log = sim.State.Events;
            var start = 0;
            if (last is not null)
                for (var i = log.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(log[i], last)) { start = i + 1; break; }
            foreach (var e in log.Skip(start)) trace($"r{e.Round,-3} {e.Type,-10} {e.Message}");
            if (log.Count > 0) last = log[^1];
        }
        Flush();
        var state = sim.State;
        var enemiesAtStart = state.Units.Where(u => u.Kind == UnitKind.Enemy).Select(u => u.Id).ToHashSet();
        var linkedSeen = new HashSet<int>();
        var stalls = new Dictionary<int, int>();
        var acted = new HashSet<int>();
        var looted = 0;
        var salvaged = 0;
        for (var i = 0; i < MaxCommands && playerIds.Any(id => !(state.FindParty(id)?.Done ?? true)) && !state.Finished; i++)
        {
            if (state.CurrentUnit is not { PlayerId: { } playerId } unit || !playerIds.Contains(playerId) || !unit.PlayerControlled || state.Round > StallRounds) break;
            foreach (var u in state.Units.Where(u => IsLinked(sim, u) && Detection.SideSees(sim.Data, state, unit.Side, u)))
                linkedSeen.Add(u.Id);
            var command = Decide(sim, unit, playerId, recruit, stalls.GetValueOrDefault(unit.Id), defenses, fightLinked, tech);
            // A turn ended without doing anything counts as a stall; any action resets it.
            var ends = command is EndTurn or SetStance or CommitOverwatch;
            if (ends && !acted.Contains(unit.Id)) stalls[unit.Id] = stalls.GetValueOrDefault(unit.Id) + 1;
            else if (!ends) stalls[unit.Id] = 0;
            if (ends) acted.Remove(unit.Id);
            else acted.Add(unit.Id);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = sim.Execute(command);
            Timed?.Invoke(command, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (result.Ok && command is PickUp or TakeAll) looted++;
            if (result.Ok && command is Salvage) salvaged++;
            if (!result.Ok && command is not EndTurn)
            {
                trace?.Invoke($"bot: {command} failed: {result.Message}");
                sim.Execute(new EndTurn(playerId, unit.Id));
            }
            Flush();
            Tally();
        }

        var enemies = state.Units.Where(u => u.Kind == UnitKind.Enemy).ToList();
        var kills = enemies.Count(u => u.Status == UnitStatus.Dead);
        var linkedKills = enemies.Count(u => u.Status == UnitStatus.Dead && IsLinked(sim, u));
        var reinforcements = enemies.Count(u => !enemiesAtStart.Contains(u.Id));
        var mine = state.Units.Where(u => u.PlayerId is { } p && playerIds.Contains(p)).Select(u => u.Id).ToHashSet();
        var ambushed = state.Events.Count(e => e.Type == ScavengeEventType.Ambushed && e.TargetId is { } t && mine.Contains(t));
        var reactions = state.Events.Count(e => e.Type == ScavengeEventType.Overwatch && e.TargetId is not null && e.UnitId is { } w && mine.Contains(w));
        var defended = state.Events.Count(e => e.UnitId is { } d && mine.Contains(d) && e.Type is ScavengeEventType.Braced
            or ScavengeEventType.Lookout or ScavengeEventType.FlareLit or ScavengeEventType.SensorPlaced
            || (e.Type == ScavengeEventType.Overwatch && e.TargetId is null && e.UnitId is { } o && mine.Contains(o)));
        int Count(ScavengeEventType type) => state.Events.Count(e => e.Type == type && e.UnitId is { } u && mine.Contains(u));
        var dronesLost = state.Units.Count(u => u.Kind == UnitKind.Drone && u.Status == UnitStatus.Dead && u.PlayerId is { } p && playerIds.Contains(p));
        var fromDevices = state.Devices.Count(d => d.Used && d.Hacked);
        return (playerIds.Select(sim.ResultFor).ToList(), new RunStats(state.Round, kills, linkedSeen.Count, linkedKills, looted, reinforcements, ambushed, reactions, defended,
            state.Events.Count(e => e.Type == ScavengeEventType.Hacked && e.Side is null && e.UnitId is { } h && mine.Contains(h)),
            Count(ScavengeEventType.HackFailed), Count(ScavengeEventType.Overridden),
            state.Events.Count(e => e.Type == ScavengeEventType.DroneFlown && e.UnitId is { } d && state.FindUnit(d)?.PlayerId is { } p && playerIds.Contains(p)),
            dronesLost,
            state.Events.Count(e => e.Type == ScavengeEventType.Marked && e.UnitId is { } m && state.FindUnit(m)?.PlayerId is { } p && playerIds.Contains(p)),
            fromDevices, Count(ScavengeEventType.LimbLocked),
            state.Events.Count(e => e.Type == ScavengeEventType.EmpBurst && e.Message.Contains(" goes off at ") && e.UnitId is { } t && mine.Contains(t)),
            Count(ScavengeEventType.DecoyDeployed), Count(ScavengeEventType.NoiseDecoy), Count(ScavengeEventType.DroneHealed),
            Count(ScavengeEventType.DroneFerried), Count(ScavengeEventType.DroneFired),
            salvaged, Count(ScavengeEventType.PickedUp)) { Tactics = tactics });
    }

    private static bool IsLinked(ScavengeSim sim, Unit u) =>
        u.Kind == UnitKind.Enemy && u.EnemyDefId is { } id && sim.Data.Enemies[id].Ai == AiKind.Linked;

    /// <summary>The least hack chance the bot takes a try at, by device (a failure shocks, alarms and locks it out).</summary>
    private static double HackFloor(DeviceKind kind) => kind switch
    {
        DeviceKind.Locker => 0.45,
        DeviceKind.Terminal => 0.45,
        DeviceKind.Camera => 0.4,
        DeviceKind.Relay => 0.5,
        _ => 0.6,
    };

    /// <summary>Devices this unit could usefully hack now (in reach, not ours, not locked out, chance over the floor).</summary>
    private static IEnumerable<ZoneDevice> Hackable(ScavengeSim sim, Unit unit, bool linkedAbout)
    {
        if (Hacking.Tool(sim.Data, unit) is null) yield break;
        var state = sim.State;
        foreach (var d in state.Devices.OrderBy(d => d.Id))
        {
            if (!Hacking.Working(state, d) || d.Used || Hacking.ControllerOf(state, d) == unit.Side || Hacking.LockedOut(state, d, unit.Side)) continue;
            var wanted = d.Kind switch
            {
                DeviceKind.Locker or DeviceKind.Terminal => true,
                DeviceKind.Camera => true,
                DeviceKind.Relay => linkedAbout,
                _ => false,
            };
            if (wanted && Hacking.Chance(sim, unit, d) >= HackFloor(d.Kind)) yield return d;
        }
    }

    private static IScavengeCommand Decide(ScavengeSim sim, Unit unit, int playerId, bool recruit, int stalled, bool defenses, bool fightLinked, bool tech)
    {
        var state = sim.State;
        var map = state.Map;
        var profile = sim.Profile(unit);
        var visible = sim.VisibleTo(playerId);
        var hostiles = state.Units.Where(t => t.IsActive && unit.HostileTo(t) && Detection.SideSees(sim.Data, state, unit.Side, t)).ToList();
        var fog = sim.Data.Rules.Fog;
        // What the side heard but cannot see, freshest first; attack contacts are where shots came from.
        var contacts = state.Contacts.Where(c => c.Side == unit.Side).OrderByDescending(c => c.Round).ToList();
        var shotAt = contacts.Where(c => c.Kind == ContactKind.Attack && c.Round >= state.Round - 1).ToList();
        var allies = state.Units.Where(t => t.Side == unit.Side && t.PlayerId is not null && t.OnMap && t.Kind == UnitKind.Survivor).ToList();

        // 0. Cards first (they mostly cost no AP): a reaction to a plan aimed at us, aim, defense, a mend, a draw.
        if (BotTactics.Card(sim, unit, playerId) is { } card) return card;

        // 1. A downed ally next to us gets the med-kit; a badly hurt one (or we) a bandage when nobody is adjacent.
        foreach (var ally in allies.Where(a => a.Status == UnitStatus.Downed && a.Pos.Chebyshev(unit.Pos) <= 1))
            if (Best(sim, unit, u => u.ReviveHp > 0 || u.Stabilize, u => -u.ReviveHp) is { } kit)
                return new UseItem(playerId, unit.Id, kit, ally.Id);
            // Co-op: no med-kit, so carry them out (Core says whether we can).
            else if (ally.PlayerId != playerId && CoopActions.CheckCarry(sim, unit, ally) is null)
                return new Carry(playerId, unit.Id, ally.Id);
        var hurt = allies.Where(a => a.IsActive && a.Hp < a.MaxHp * 0.5 && a.Pos.Chebyshev(unit.Pos) <= 1).OrderBy(a => a.Hp).FirstOrDefault();
        if (hurt is not null && !hostiles.Any(h => h.Pos.Chebyshev(unit.Pos) <= 1)
            && Best(sim, unit, u => u.Heal > 0 && u.ReviveHp == 0 && u.HumanityCost == 0, u => -u.Heal) is { } bandage)
            return new UseItem(playerId, unit.Id, bandage, hurt.Id);
        // A broken limb gets a splint when nothing is in reach; a survivor in pain takes a painkiller once.
        if (!hostiles.Any(h => h.Pos.Chebyshev(unit.Pos) <= 2) && sim.Data.Medicine is { } med)
        {
            if (allies.FirstOrDefault(a => a.IsActive && a.Pos.Chebyshev(unit.Pos) <= 1 && Treatment.UnsplintedFracture(med, a.Body) is not null) is { } broken
                && Best(sim, unit, u => u.Splint, _ => 0) is { } splint)
                return new UseItem(playerId, unit.Id, splint, broken.Id);
            if (Treatment.InPain(med, unit.Body) && unit.Body.Doses < 1 && Best(sim, unit, u => u.PainRelief > 0 && u.HumanityCost == 0, _ => 0) is { } pill)
                return new UseItem(playerId, unit.Id, pill);
        }

        // 1b. Against the fog: light up where an ambush came from; scan when something is heard but not seen.
        if (defenses && hostiles.Count == 0)
        {
            if (shotAt.FirstOrDefault(c => c.Pos.Chebyshev(unit.Pos) <= ThrowRange(sim, unit, "flare")) is { } source
                && Usable(sim, unit, "flare", u => u.LightRadius > 0) is { } flare
                && !state.Flares.Any(f => f.UntilRound >= state.Round && f.Pos.Chebyshev(source.Pos) <= 1))
                return new UseItem(playerId, unit.Id, flare, At: source.Pos);
            if (!unit.Lookout && shotAt.Count == 0 && contacts.Count > 0 && unit.Ap == profile.MaxAp && unit.Ap > fog.Lookout.ApCost
                && contacts.Any(c => c.Pos.Chebyshev(unit.Pos) <= profile.Sight + 2))
                return new Lookout(playerId, unit.Id);
        }

        // 1c. Electronics: an EMP over two or more Network machines (never our own drone), a holo-decoy when a Linked
        // closes on a hurt unit, a noise decoy to pull hunting Husks off the party.
        if (tech && Electronics(sim, unit, playerId, hostiles) is { } device) return device;

        // 2. Hit what gives the most expected damage per AP.
        // The true AP counts (a scope costs more up close).
        var attack = profile.Actions
            .SelectMany(a => hostiles
                .Where(t => Combat.ApCost(a, unit.Pos, t.Pos) <= unit.Ap && Combat.CheckReach(sim.Data, map, unit.Pos, a, t.Pos) == ScavengeError.None)
                .Select(t => (Action: a, Target: t, Chance: Combat.ChanceToHit(sim, unit, a, t))))
            .OrderByDescending(o => o.Chance * (o.Action.MinDamage + o.Action.MaxDamage) / 2 / Combat.ApCost(o.Action, unit.Pos, o.Target.Pos))
            .ThenBy(o => o.Target.Hp).FirstOrDefault();
        if (attack.Target is not null && attack.Chance >= 0.2 && (fightLinked || !(IsLinked(sim, attack.Target) && attack.Target.Hp > 25))
            && TileStrikes.StrikeAt(sim, unit, attack.Action, attack.Target) is { } strike)
            return strike;



        // 2a. Tech: hack what is worth it in reach, work the drone.
        var linkedAbout = hostiles.Any(h => IsLinked(sim, h)) || state.Units.Any(u => IsLinked(sim, u) && u.Ai.Mode == AiMode.Hunting);
        var hackTool = Hacking.Tool(sim.Data, unit)?.HackTool;
        if (tech && hackTool is not null && unit.Ap >= Hacking.ApCost(sim.Data, unit))
        {
            var visibleNow = sim.VisibleTo(playerId);
            var inReach = Hackable(sim, unit, linkedAbout)
                .Where(d => visibleNow.Contains(map.Index(d.Pos)) && d.Pos.Chebyshev(unit.Pos) <= hackTool.Range
                            && (d.Pos.Chebyshev(unit.Pos) <= 1 || map.HasLineOfSight(sim.Data, unit.Pos, d.Pos)))
                .FirstOrDefault(d => hostiles.Count == 0 || d.Kind is DeviceKind.Camera or DeviceKind.Relay);
            if (inReach is not null) return new Hack(playerId, unit.Id, inReach.Id);
            if (unit.Hook(PerkHooks.CanHackLinked) > 0
                && hostiles.FirstOrDefault(h => IsLinked(sim, h) && h.Pos.Chebyshev(unit.Pos) <= hackTool.Range) is { } turn
                && Hacking.Chance(sim, unit, null, turn) >= 0.4)
                return new Hack(playerId, unit.Id, TargetId: turn.Id);
        }
        // The drone gets the last AP of a turn (or the turn when there is nowhere to walk), so it never slows the party.
        if (tech && TryDrone(sim, unit, playerId, hostiles, lastAp: unit.Ap <= 1) is { } droneOrder) return droneOrder;

        // 2b. Against the fog when not fleeing: a Linked in view but out of reach is left to come to us on overwatch;
        // shot at from the fog with nothing to see, brace (the next ambush gains nothing).
        if (defenses && fightLinked && unit.Ap == profile.MaxAp)
        {
            var ranged = profile.Actions.Any(a => a.Pattern != AttackPattern.Melee);
            var reach = profile.Actions.Select(a => a.Reach).DefaultIfEmpty(1).Max();
            if (ranged && hostiles.Any(h => IsLinked(sim, h) && h.Pos.Chebyshev(unit.Pos) <= reach + 2) && Overwatches.OnArc(sim, unit, playerId) is { } watch)
                return watch;
            if (hostiles.Count == 0 && shotAt.Count > 0 && unit.Ap >= fog.Brace.ApCost)
                return new SetStance(playerId, unit.Id, Stance.Brace);
        }

        // 2c. Beside the party's vehicle: load the haul into it (the vehicle carries it home), heaviest first.
        var parked = ParkedVehicles.Of(state, playerId) is { Wrecked: false } pv ? pv : null;
        var room = parked is null ? 0 : parked.CargoKg - ParkedVehicles.Load(sim.Data, parked);
        if (parked is not null && parked.Pos.Chebyshev(unit.Pos) <= 1 && unit.Ap >= sim.Data.Rules.Turn.PickUpApCost
            && unit.Pack.Where(id => sim.Data.Items[id].Kind is ItemKind.Material or ItemKind.Schematic && sim.Data.Items[id].Weight <= room + 1e-9)
                .OrderByDescending(id => sim.Data.Items[id].Weight).ThenBy(id => id, StringComparer.Ordinal).FirstOrDefault() is { } haul)
            return new StashItem(playerId, unit.Id, haul);

        // 2c. Too heavy to walk: drop the carried material worth least per kilo.
        if (profile.Load > profile.CarryCapacity * DropLoad
            && unit.Pack.Where(id => sim.Data.Items[id].Kind == ItemKind.Material && sim.Data.Items[id].Weight > 0)
                .OrderBy(id => Value(sim.Data.Items[id])).ThenByDescending(id => sim.Data.Items[id].Weight).FirstOrDefault() is { } heavy)
            return new DropItem(playerId, unit.Id, heavy);

        // 3. Decide where to go: out when hurt, loaded, late or out of caches; else the nearest cache.
        // A Linked in sight is beyond a starting party: break off and head out. Loaded with room in the vehicle, unload first.
        var loaded = profile.Load >= profile.CarryCapacity * 0.8;
        // Only worth the walk when something in the pack is loot that fits.
        var unload = loaded && unit.Pack.Any(id => sim.Data.Items[id].Kind is ItemKind.Material or ItemKind.Schematic && sim.Data.Items[id].Weight <= room + 1e-9);
        var leaving = Leaving(sim, unit, hostiles, allies, fightLinked, tech, unload);
        if (leaving && (map.IsExit(unit.Pos) || ParkedVehicles.Beside(state, unit) is not null)) return new Extract(playerId, unit.Id);
        // A seen enemy plan aims at our tile: step off it (the blow lands on an empty tile). Not when leaving: the way out
        // moves us off anyway, and dodging every turn could keep us from the exit.
        if (Dodge && !leaving && BotTactics.Dodge(sim, unit, playerId) is { } dodge) return dodge;

        var strangers = recruit
            ? state.Units.Where(u => u.Kind == UnitKind.Npc && u.IsActive && u.Side == Unit.NeutralSide && visible.Contains(map.Index(u.Pos))).ToList()
            : [];
        if (!leaving && strangers.FirstOrDefault(n => n.Pos.Chebyshev(unit.Pos) <= 1) is { } near && unit.Ap >= sim.Data.Rules.Turn.RecruitApCost)
            return new Recruit(playerId, unit.Id, near.Id);

        if (!leaving)
        {
            var cache = state.Caches.Where(c => c.Pos.Chebyshev(unit.Pos) <= 1 && Carriable(sim, profile, c)).FirstOrDefault();
            if (cache is not null && unit.Ap >= sim.Data.Rules.Turn.PickUpApCost)
            {
                // Room for all of it: take it all for one pick-up; else the most valuable piece that fits.
                if (Fits(profile, cache.Items.Sum(id => sim.Data.Items[id].Weight)))
                    return new TakeAll(playerId, unit.Id, cache.Id);
                var item = cache.Items.Where(id => Fits(profile, sim.Data.Items[id].Weight))
                    .OrderByDescending(id => Value(sim.Data.Items[id])).FirstOrDefault();
                if (item is not null) return new PickUp(playerId, unit.Id, cache.Id, item);
            }
            if (Salvage && hostiles.Count == 0 && RoomToSalvage(profile) && SafeToSalvage(state, unit)
                && state.SalvageNodes.FirstOrDefault(n => !n.Depleted && n.Pos.Chebyshev(unit.Pos) <= 1
                                                          && sim.Data.Salvage.Node(n.Kind) is { } d && d.ApCost <= unit.Ap) is { } node)
                return new Salvage(playerId, unit.Id, node.Id);
        }

        // Goals in order (close in on a visible enemy, a stranger to recruit, the nearest cache we can still carry from,
        // an exit); take the first one we can actually make progress towards this turn.
        var goals = new List<Pos>();
        // Tiles beside the vehicle are where loot is unloaded and where the party leaves from.
        var bySide = parked is null ? [] : Pos.Neighbours8.Select(d => parked.Pos + d)
            .Where(p => map.InBounds(p) && sim.Data.Terrain(map[p]).Passable).OrderBy(p => p.Chebyshev(unit.Pos)).ThenBy(map.Index).ToList();
        if (unload) goals.AddRange(bySide);
        if (!leaving)
        {
            goals.AddRange(hostiles.OrderBy(h => h.Pos.Chebyshev(unit.Pos)).Select(h => h.Pos));
            goals.AddRange(strangers.OrderBy(n => n.Pos.Chebyshev(unit.Pos)).Select(n => n.Pos));
            goals.AddRange(state.Caches.Where(c => Carriable(sim, profile, c))
                .OrderBy(c => c.Pos.Chebyshev(unit.Pos)).Select(c => c.Pos));
            if (Salvage && hostiles.Count == 0 && RoomToSalvage(profile) && SafeToSalvage(state, unit))
                goals.AddRange(KnownNodes(sim, unit).OrderBy(n => n.Pos.Chebyshev(unit.Pos)).Select(n => n.Pos));
            if (tech)
                goals.AddRange(Hackable(sim, unit, linkedAbout).Where(d => d.Kind is DeviceKind.Locker or DeviceKind.Terminal
                                                                           && state.Explored.GetValueOrDefault(unit.Side)?.Contains(map.Index(d.Pos)) == true)
                    .OrderBy(d => d.Pos.Chebyshev(unit.Pos)).Select(d => d.Pos));
        }
        // The nearest exits (and the vehicle) always stay in the running, however many places are left to loot.
        goals = goals.Take(5).Concat(bySide.Concat(map.Exits).OrderBy(e => e.Chebyshev(unit.Pos)).Take(3)).ToList();

        var reachable = sim.Reachable(unit).Where(kv => kv.Value <= unit.Mp).ToList();
        // Boxed in (often by our own follower in a doorway): for a few rounds, path around the units in the way.
        if (stalled >= 2) Detours[unit.Id] = state.Round + 3;
        var detour = Detours.GetValueOrDefault(unit.Id) >= state.Round;
        foreach (var goal in goals)
        {
            var field = Movement.DistanceField(state, unit, profile.Movement, goal);
            if (detour)
            {
                var around = new TileMap(map.Width, map.Height) { Tiles = map.Tiles.ToList(), Exits = map.Exits };
                foreach (var p in field.Keys.Where(p => p != goal && state.UnitAt(p) is { } o && o.Id != unit.Id)) around[p] = Terrain.Wall;
                foreach (var p in map.AllPositions.Where(p => !field.ContainsKey(p) && p != goal)) around[p] = Terrain.Wall;
                if (Movement.DistanceField(around, profile.Movement, goal) is var clear && clear.ContainsKey(unit.Pos)) field = clear;
            }
            if (!field.TryGetValue(unit.Pos, out var here)) continue;
            // Among equally good steps, one next to an ally (backs covered against ambushes).
            bool Covered(Pos p) => defenses && allies.Any(x => x.Id != unit.Id && x.IsActive && x.Pos.Chebyshev(p) <= 1);
            var step = reachable
                .Where(kv => field.TryGetValue(kv.Key, out var d) && d < here)
                .OrderBy(kv => field[kv.Key]).ThenBy(kv => Covered(kv.Key) ? 0 : 1).ThenBy(kv => kv.Value).ThenBy(kv => map.Index(kv.Key))
                .Select(kv => (Pos?)kv.Key).FirstOrDefault();
            if (step is { } to) return new Move(playerId, unit.Id, to);
            // Boxed in for a couple of turns: sidestep to the best free tile.
            if (stalled >= 2 && reachable.Where(kv => field.ContainsKey(kv.Key)).OrderBy(kv => field[kv.Key]).ThenBy(kv => map.Index(kv.Key))
                    .Select(kv => (Pos?)kv.Key).FirstOrDefault() is { } aside)
                return new Move(playerId, unit.Id, aside);
        }
        if ((map.IsExit(unit.Pos) || ParkedVehicles.Beside(state, unit) is not null) && (leaving || state.Round > MaxRounds / 2))
            return new Extract(playerId, unit.Id);
        if (tech && TryDrone(sim, unit, playerId, hostiles, lastAp: true) is { } idleDrone) return idleDrone;
        // Nothing in reach but a hostile we would fight: close in, with the AP for a blow left.
        if (!leaving && BotTactics.Approach(sim, unit, playerId, hostiles.Where(h => fightLinked || !IsLinked(sim, h)).ToList()) is { } approach) return approach;
        return defenses ? Hold(sim, unit, playerId, hostiles, contacts, shotAt) : new EndTurn(playerId, unit.Id);
    }

    /// <summary>Out when hurt, loaded, late, an ally is down, or nothing is left to loot (or hack); a starting party also on sighting a Linked.</summary>
    private static bool Leaving(ScavengeSim sim, Unit unit, List<Unit> hostiles, List<Unit> allies, bool fightLinked, bool tech, bool unload = false)
    {
        var state = sim.State;
        var profile = sim.Profile(unit);
        return (!fightLinked && hostiles.Any(h => IsLinked(sim, h)))
               || unit.Hp < unit.MaxHp * 0.5
               || unit.CarryingId is not null
               || (profile.Load >= profile.CarryCapacity * 0.8 && !unload)
               || state.Round >= MaxRounds
               || allies.Any(a => a.Status == UnitStatus.Downed)
               || (!state.Caches.Any(c => Carriable(sim, profile, c))
                   && !(Salvage && RoomToSalvage(profile) && SafeToSalvage(state, unit) && KnownNodes(sim, unit).Any())
                   && !(tech && Hackable(sim, unit, false).Any(d => d.Kind is DeviceKind.Locker or DeviceKind.Terminal)));
    }

    /// <summary>A pile with something in it the unit can still carry.</summary>
    private static bool Carriable(ScavengeSim sim, UnitProfile profile, LootCache c) =>
        c.Items.Any(id => Fits(profile, sim.Data.Items[id].Weight));

    /// <summary>Whether the bot would still walk well carrying this much more (it never loads itself past <see cref="DropLoad"/>).</summary>
    private static bool Fits(UnitProfile profile, double kg) => profile.Load + kg <= profile.CarryCapacity * DropLoad + 1e-9;

    /// <summary>Salvage nodes with lots left on tiles the unit's side has seen.</summary>
    private static IEnumerable<SalvageNode> KnownNodes(ScavengeSim sim, Unit unit)
    {
        var explored = sim.State.Explored.GetValueOrDefault(unit.Side);
        return sim.State.SalvageNodes.Where(n => !n.Depleted && explored?.Contains(sim.State.Map.Index(n.Pos)) == true);
    }

    private static IScavengeCommand? Electronics(ScavengeSim sim, Unit unit, int playerId, List<Unit> hostiles)
    {
        var state = sim.State;
        if (unit.Pack.FirstOrDefault(id => sim.Data.Items[id].Use is { Emp: not null } u && u.ApCost <= unit.Ap) is { } emp)
        {
            var use = sim.Data.Items[emp].Use!;
            var machines = hostiles.Where(h => IsLinked(sim, h) || h.Kind == UnitKind.Device).ToList();
            var best = machines.Where(m => m.Pos.Chebyshev(unit.Pos) <= use.Range)
                .Select(m => (At: m.Pos, Hits: machines.Count(o => o.Pos.Chebyshev(m.Pos) <= use.Emp!.Radius)))
                .Where(t => !state.Units.Any(o => o.IsActive && o.Side == unit.Side && o.Kind == UnitKind.Drone && o.Pos.Chebyshev(t.At) <= use.Emp!.Radius))
                .OrderByDescending(t => t.Hits).ThenBy(t => state.Map.Index(t.At)).FirstOrDefault();
            if (best.Hits >= 2 || best.Hits == 1 && machines.Any(m => m.Pos.Chebyshev(unit.Pos) <= 3 && IsLinked(sim, m)))
                return new UseItem(playerId, unit.Id, emp, At: best.At);
        }
        var linkedNear = hostiles.Where(h => IsLinked(sim, h) && h.Pos.Chebyshev(unit.Pos) <= 7).OrderBy(h => h.Pos.Chebyshev(unit.Pos)).FirstOrDefault();
        if (linkedNear is not null && unit.Hp < unit.MaxHp * 0.7 && !state.Units.Any(u => u.Kind == UnitKind.Decoy && u.IsActive && u.Side == unit.Side)
            && unit.Pack.FirstOrDefault(id => sim.Data.Items[id].Use is { Decoy: not null } u && u.ApCost <= unit.Ap) is { } holo)
        {
            var range = sim.Data.Items[holo].Use!.Range;
            // Between us and them, a few tiles out: they turn to it and we slip away.
            var spot = Enumerable.Range(-range, 2 * range + 1).SelectMany(dy => Enumerable.Range(-range, 2 * range + 1).Select(dx => unit.Pos + new Pos(dx, dy)))
                .Where(p => state.Map.InBounds(p) && p.Chebyshev(unit.Pos) is >= 2 && p.Chebyshev(unit.Pos) <= range
                            && sim.Data.Terrain(state.Map[p]).Passable && state.UnitAt(p) is null && state.DeviceAt(p) is null)
                .OrderBy(p => p.Chebyshev(linkedNear.Pos)).ThenBy(state.Map.Index).Select(p => (Pos?)p).FirstOrDefault();
            if (spot is { } at) return new UseItem(playerId, unit.Id, holo, At: at);
        }
        var husks = hostiles.Where(h => !IsLinked(sim, h) && h.Kind == UnitKind.Enemy && h.Pos.Chebyshev(unit.Pos) <= 6).ToList();
        if (husks.Count >= 2 && unit.Pack.FirstOrDefault(id => sim.Data.Items[id].Use is { NoiseRadius: > 0 } u && u.ApCost <= unit.Ap) is { } noise)
        {
            var use = sim.Data.Items[noise].Use!;
            // At full throw, as far from the way out as it goes: the Husks wander off from the route home.
            var exit = state.Map.Exits.OrderBy(e => e.Chebyshev(unit.Pos)).First();
            var r = use.Range;
            var at = Enumerable.Range(-r, 2 * r + 1).SelectMany(dy => Enumerable.Range(-r, 2 * r + 1).Select(dx => unit.Pos + new Pos(dx, dy)))
                .Where(p => state.Map.InBounds(p) && p.Chebyshev(unit.Pos) == r)
                .OrderByDescending(p => p.Chebyshev(exit)).ThenBy(state.Map.Index).First();
            if (at.Chebyshev(unit.Pos) >= 3) return new UseItem(playerId, unit.Id, noise, At: at);
        }
        return null;
    }

    /// <summary>
    /// A drone variant's job, before any scouting: the medic drone treats a hurt teammate next to it (or flies to one),
    /// the combat drone fires at what it sees, the cargo drone loads from a cache next to it and ferries a full hold (or
    /// anything when the party is leaving) from beside an exit.
    /// </summary>
    private static IScavengeCommand? DroneRole(ScavengeSim sim, Unit unit, int playerId, Unit drone, bool leaving)
    {
        var state = sim.State;
        if (DroneRoles.Spec(sim.Data, drone) is not { } spec || spec.Role == Remnants.Core.Items.DroneRole.Scout) return null;
        var flew = state.Events.Any(e => e.Round == state.Round && e.Type == ScavengeEventType.DroneFlown && e.UnitId == drone.Id);
        IScavengeCommand? Toward(Pos goal)
        {
            if (flew || unit.Ap < sim.Data.Rules.Drone.FlyApCost) return null;
            var best = Drones.FlightTargets(sim, unit).OrderBy(p => p.Chebyshev(goal)).ThenBy(state.Map.Index).Select(p => (Pos?)p).FirstOrDefault();
            return best is { } to && to.Chebyshev(goal) < drone.Pos.Chebyshev(goal) ? new FlyDrone(playerId, unit.Id, to) : null;
        }
        switch (spec.Role)
        {
            case Remnants.Core.Items.DroneRole.Medic:
            {
                var hurt = state.Units.Where(t => t.Side == unit.Side && t.OnMap && t.Kind == UnitKind.Survivor
                                                  && (t.Status == UnitStatus.Downed || t.Hp < t.MaxHp * 0.6) && DroneRoles.HealItem(sim.Data, unit, t) is not null)
                    .OrderBy(t => t.Status == UnitStatus.Downed ? 0 : 1).ThenBy(t => t.Hp).FirstOrDefault();
                if (hurt is null) return null;
                if (hurt.Pos.Chebyshev(drone.Pos) <= 1 && unit.Ap >= spec.ActionApCost) return new DroneHeal(playerId, unit.Id, hurt.Id);
                return Toward(hurt.Pos);
            }
            case Remnants.Core.Items.DroneRole.Combat:
                return DroneRoles.FireTargets(sim, unit).OrderBy(t => t.Hp).FirstOrDefault() is { } target
                       && sim.Profile(drone).Actions.FirstOrDefault() is { } gun && unit.Ap >= gun.ApCost
                    ? new DroneFire(playerId, unit.Id, target.Id) : null;
            case Remnants.Core.Items.DroneRole.Cargo:
            {
                if ((drone.Pack.Count >= spec.Cargo || leaving && drone.Pack.Count > 0) && DroneRoles.FerryPoint(sim, drone.Pos) && unit.Ap >= spec.ActionApCost)
                    return new DroneFerry(playerId, unit.Id);
                if (drone.Pack.Count >= spec.Cargo || leaving && drone.Pack.Count > 0)
                    return Toward(state.Map.Exits.OrderBy(e => e.Chebyshev(drone.Pos)).First());
                if (state.Caches.Where(c => c.Pos.Chebyshev(drone.Pos) <= 1 && c.Items.Count > 0).FirstOrDefault() is { } cache && unit.Ap >= spec.ActionApCost)
                    return new DroneLoad(playerId, unit.Id, cache.Id, cache.Items.OrderByDescending(id => Value(sim.Data.Items[id])).ThenBy(id => id).First());
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// The scout drone: recall it when leaving or nearly flat; mark a Linked it sees (Spotter); with the turn's last AP
    /// (<paramref name="lastAp"/>) fly it once ahead toward the nearest cache (else an exit), keeping out of reach of known
    /// hostiles, or launch it when nothing hostile is in sight and the battery is up.
    /// </summary>
    private static IScavengeCommand? TryDrone(ScavengeSim sim, Unit unit, int playerId, List<Unit> hostiles, bool lastAp)
    {
        var state = sim.State;
        var rules = sim.Data.Rules.Drone;
        var allies = state.Units.Where(t => t.Side == unit.Side && t.PlayerId is not null && t.OnMap && t.Kind == UnitKind.Survivor).ToList();
        var leaving = Leaving(sim, unit, hostiles, allies, false, true);
        if (Drones.DroneOf(state, unit) is { } drone)
        {
            if (DroneRole(sim, unit, playerId, drone, leaving) is { } job) return job;
            var range = Drones.ControlRange(sim.Data, unit, drone.DroneItemId!);
            if ((leaving || drone.Battery <= 1) && unit.Ap >= rules.RecallApCost && unit.Pos.Chebyshev(drone.Pos) <= range)
                return new RecallDrone(playerId, unit.Id);
            if (unit.Hook(PerkHooks.DroneCanMark) > 0 && unit.Ap >= rules.MarkApCost
                && state.Units.FirstOrDefault(t => t.IsActive && IsLinked(sim, t) && drone.HostileTo(t) && !Drones.IsMarked(state, t.Id, unit.Side)
                                                   && Detection.Spots(sim.Data, state, drone, t)) is { } mark)
                return new MarkTarget(playerId, unit.Id, mark.Id);
            if (!lastAp || hostiles.Count > 0 || unit.Ap < rules.FlyApCost || state.Events.Any(e => e.Round == state.Round && e.Type == ScavengeEventType.DroneFlown && e.UnitId == drone.Id))
                return null;
            var goal = state.Caches.OrderBy(c => c.Pos.Chebyshev(unit.Pos)).Select(c => (Pos?)c.Pos).FirstOrDefault()
                       ?? state.Map.Exits.OrderBy(e => e.Chebyshev(unit.Pos)).First();
            var best = Drones.FlightTargets(sim, unit)
                .Where(p => !hostiles.Any(h => h.Pos.Chebyshev(p) <= 2))
                .OrderBy(p => p.Chebyshev(goal)).ThenBy(p => state.Map.Index(p))
                .Select(p => (Pos?)p).FirstOrDefault();
            if (best is { } to && to.Chebyshev(goal) < drone.Pos.Chebyshev(goal)) return new FlyDrone(playerId, unit.Id, to);
            return null;
        }
        if (!lastAp || leaving || hostiles.Count > 0 || unit.Ap < rules.DeployApCost || Drones.CarriedDrone(sim.Data, unit) is not { } item) return null;
        if (Drones.PackedBattery(sim.Data, unit, item) < Math.Max(rules.MinBattery, 3)) return null;
        var spot = Pos.Neighbours8.Select(d => unit.Pos + d).Where(p => Drones.CanHover(sim, p)).OrderBy(state.Map.Index).Select(p => (Pos?)p).FirstOrDefault();
        return spot is { } at ? new DeployDrone(playerId, unit.Id, at) : null;
    }

    /// <summary>
    /// Ending a turn with AP left: brace when the side was just shot at from the fog or a Linked is about (an ambush
    /// gains nothing then), else overwatch when there is something out there (a contact, a hostile in sight), else wait.
    /// </summary>
    private static IScavengeCommand Hold(ScavengeSim sim, Unit unit, int playerId, List<Unit> hostiles, List<Contact> contacts, List<Contact> shotAt)
    {
        var fog = sim.Data.Rules.Fog;
        var threatened = shotAt.Count > 0 || hostiles.Any(h => IsLinked(sim, h));
        if (threatened && unit.Ap >= fog.Brace.ApCost && !hostiles.Any(h => h.Pos.Chebyshev(unit.Pos) <= 1))
            return new SetStance(playerId, unit.Id, Stance.Brace);
        if ((contacts.Count > 0 || hostiles.Count > 0) && Overwatches.OnArc(sim, unit, playerId) is { } watch)
            return watch;
        return new EndTurn(playerId, unit.Id);
    }

    private static int ThrowRange(ScavengeSim sim, Unit unit, string baseId) =>
        unit.Pack.Select(id => sim.Data.Items[id]).Where(i => i.DefinedId == baseId && i.Use is not null).Select(i => i.Use!.Range).DefaultIfEmpty(0).Max();

    /// <summary>The pack item that does this and fits the AP, lowest <paramref name="rank"/> first (then the pack order).</summary>
    private static string? Best(ScavengeSim sim, Unit unit, Func<UseEffect, bool> does, Func<UseEffect, double> rank) =>
        unit.Pack.Where(id => sim.Data.Items[id].Use is { } use && does(use) && use.ApCost <= unit.Ap)
            .OrderBy(id => rank(sim.Data.Items[id].Use!)).FirstOrDefault();

    private static string? Usable(ScavengeSim sim, Unit unit, string baseId, Func<UseEffect, bool> does) =>
        unit.Pack.FirstOrDefault(id => sim.Data.Items[id] is { Use: { } use } item && item.DefinedId == baseId && does(use) && use.ApCost <= unit.Ap);

    /// <summary>Rough value per kilo so packs fill with the good stuff first.</summary>
    private static double Value(ItemDef item) => item.Kind switch
    {
        ItemKind.Schematic => 100,
        ItemKind.Prosthetic or ItemKind.Weapon or ItemKind.Armor or ItemKind.Clothing or ItemKind.Tool => 20,
        ItemKind.Consumable => 15,
        _ => item.Salvage is { } s ? 10 * s.Amount / Math.Max(item.Weight, 0.1) / 3 : 1,
    };
}
