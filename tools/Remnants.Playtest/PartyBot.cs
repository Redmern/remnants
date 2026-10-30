using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Playtest;

/// <summary>What one scavenge run looked like from the party's side.</summary>
public sealed record RunStats(int Rounds, int Kills, int LinkedSeen, int LinkedKills, int ItemsLooted, int Reinforcements,
    int AmbushedTimes = 0, int OverwatchShots = 0, int DefensesUsed = 0);

/// <summary>
/// Plays the colony's party in a scavenge run through Core commands only, roughly like a careful player:
/// revive or patch up allies, fight what it sees, loot the nearest cache, and head for an exit once hurt, loaded
/// or out of caches. It knows where caches are (a player would find them by exploring), not where enemies are, and
/// only fights what its side actually sees (stealth hides the Linked). Against the fog (unless <c>defenses</c> is off)
/// it scans (Lookout) when it hears something it cannot see, throws a flare at where an ambush came from, keeps close to
/// an ally, and ends idle turns on overwatch, or braced once it has been shot at from the fog.
/// </summary>
public static class PartyBot
{
    private const int MaxCommands = 20000;
    private const int MaxRounds = 40;

    /// <summary>A run still going this long is stuck; the bot gives up (the result stays incomplete).</summary>
    private const int StallRounds = 300;

    /// <summary>Plays the party until it is done. <paramref name="trace"/> gets every scavenge event as it happens.</summary>
    public static (ExtractionResult Result, RunStats Stats) Play(ScavengeSim sim, int playerId, Action<string>? trace = null, bool recruit = true,
        bool defenses = true, bool fightLinked = false)
    {
        ScavengeEvent? last = null;
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
        for (var i = 0; i < MaxCommands && !(state.FindParty(playerId)?.Done ?? true) && !state.Finished; i++)
        {
            if (state.CurrentUnit is not { } unit || unit.PlayerId != playerId || !unit.PlayerControlled || state.Round > StallRounds) break;
            foreach (var u in state.Units.Where(u => IsLinked(sim, u) && Detection.SideSees(sim.Data, state, unit.Side, u)))
                linkedSeen.Add(u.Id);
            var command = Decide(sim, unit, playerId, recruit, stalls.GetValueOrDefault(unit.Id), defenses, fightLinked);
            // A turn ended without doing anything counts as a stall; any action resets it.
            var ends = command is EndTurn or SetStance;
            if (ends && !acted.Contains(unit.Id)) stalls[unit.Id] = stalls.GetValueOrDefault(unit.Id) + 1;
            else if (!ends) stalls[unit.Id] = 0;
            if (ends) acted.Remove(unit.Id);
            else acted.Add(unit.Id);
            var result = sim.Execute(command);
            if (result.Ok && command is PickUp) looted++;
            if (!result.Ok && command is not EndTurn) sim.Execute(new EndTurn(playerId, unit.Id));
            Flush();
        }

        var enemies = state.Units.Where(u => u.Kind == UnitKind.Enemy).ToList();
        var kills = enemies.Count(u => u.Status == UnitStatus.Dead);
        var linkedKills = enemies.Count(u => u.Status == UnitStatus.Dead && IsLinked(sim, u));
        var reinforcements = enemies.Count(u => !enemiesAtStart.Contains(u.Id));
        var mine = state.Units.Where(u => u.PlayerId == playerId).Select(u => u.Id).ToHashSet();
        var ambushed = state.Events.Count(e => e.Type == ScavengeEventType.Ambushed && e.TargetId is { } t && mine.Contains(t));
        var reactions = state.Events.Count(e => e.Type == ScavengeEventType.Overwatch && e.TargetId is not null && e.UnitId is { } w && mine.Contains(w));
        var defended = state.Events.Count(e => e.UnitId is { } d && mine.Contains(d) && e.Type is ScavengeEventType.Braced
            or ScavengeEventType.Lookout or ScavengeEventType.FlareLit or ScavengeEventType.SensorPlaced
            || (e.Type == ScavengeEventType.Overwatch && e.TargetId is null && e.UnitId is { } o && mine.Contains(o)));
        return (sim.ResultFor(playerId), new RunStats(state.Round, kills, linkedSeen.Count, linkedKills, looted, reinforcements, ambushed, reactions, defended));
    }

    private static bool IsLinked(ScavengeSim sim, Unit u) =>
        u.Kind == UnitKind.Enemy && u.EnemyDefId is { } id && sim.Data.Enemies[id].Ai == AiKind.Linked;

    private static IScavengeCommand Decide(ScavengeSim sim, Unit unit, int playerId, bool recruit, int stalled, bool defenses, bool fightLinked)
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
        var allies = state.Units.Where(t => t.PlayerId == playerId && t.OnMap && t.Kind == UnitKind.Survivor).ToList();

        // 1. A downed ally next to us gets the med-kit; a badly hurt one (or we) a bandage when nobody is adjacent.
        foreach (var ally in allies.Where(a => a.Status == UnitStatus.Downed && a.Pos.Chebyshev(unit.Pos) <= 1))
            if (Usable(sim, unit, "med_kit", u => u.ReviveHp > 0 || u.Stabilize) is { } kit)
                return new UseItem(playerId, unit.Id, kit, ally.Id);
        var hurt = allies.Where(a => a.IsActive && a.Hp < a.MaxHp * 0.5 && a.Pos.Chebyshev(unit.Pos) <= 1).OrderBy(a => a.Hp).FirstOrDefault();
        if (hurt is not null && !hostiles.Any(h => h.Pos.Chebyshev(unit.Pos) <= 1)
            && Usable(sim, unit, "bandage", u => u.Heal > 0) is { } bandage)
            return new UseItem(playerId, unit.Id, bandage, hurt.Id);

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

        // 2. Hit what gives the most expected damage per AP.
        var attack = profile.Actions.Where(a => a.ApCost <= unit.Ap)
            .SelectMany(a => hostiles
                .Where(t => Combat.CheckReach(sim.Data, map, unit.Pos, a, t.Pos) == ScavengeError.None)
                .Select(t => (Action: a, Target: t, Chance: Combat.ChanceToHit(sim, unit, a, t))))
            .OrderByDescending(o => o.Chance * (o.Action.MinDamage + o.Action.MaxDamage) / 2 / o.Action.ApCost).ThenBy(o => o.Target.Hp).FirstOrDefault();
        if (attack.Target is not null && attack.Chance >= 0.2 && (fightLinked || !(IsLinked(sim, attack.Target) && attack.Target.Hp > 25)))
            return new Attack(playerId, unit.Id, attack.Target.Id, attack.Action.Id);

        // 2b. Against the fog when not fleeing: a Linked in view but out of reach is left to come to us on overwatch;
        // shot at from the fog with nothing to see, brace (the next ambush gains nothing).
        if (defenses && fightLinked && unit.Ap == profile.MaxAp)
        {
            var ranged = profile.Actions.Any(a => a.Pattern != AttackPattern.Melee);
            var reach = profile.Actions.Select(a => a.Reach).DefaultIfEmpty(1).Max();
            if (ranged && hostiles.Any(h => IsLinked(sim, h) && h.Pos.Chebyshev(unit.Pos) <= reach + 2) && unit.Ap >= fog.Overwatch.ApCost)
                return new SetStance(playerId, unit.Id, Stance.Overwatch);
            if (hostiles.Count == 0 && shotAt.Count > 0 && unit.Ap >= fog.Brace.ApCost)
                return new SetStance(playerId, unit.Id, Stance.Brace);
        }

        // 3. Decide where to go: out when hurt, loaded, late or out of caches; else the nearest cache.
        // A Linked in sight is beyond a starting party: break off and head out.
        var leaving = (!fightLinked && hostiles.Any(h => IsLinked(sim, h)))
                      || unit.Hp < unit.MaxHp * 0.5
                      || profile.Load >= profile.CarryCapacity * 0.8
                      || state.Round >= MaxRounds
                      || allies.Any(a => a.Status == UnitStatus.Downed)
                      || state.Caches.Count == 0;
        if (leaving && map.IsExit(unit.Pos)) return new Extract(playerId, unit.Id);

        var strangers = recruit
            ? state.Units.Where(u => u.Kind == UnitKind.Npc && u.IsActive && u.Side == Unit.NeutralSide && visible.Contains(map.Index(u.Pos))).ToList()
            : [];
        if (!leaving && strangers.FirstOrDefault(n => n.Pos.Chebyshev(unit.Pos) <= 1) is { } near && unit.Ap >= sim.Data.Rules.Turn.RecruitApCost)
            return new Recruit(playerId, unit.Id, near.Id);

        if (!leaving)
        {
            var cache = state.Caches.Where(c => c.Pos.Chebyshev(unit.Pos) <= 1).FirstOrDefault();
            if (cache is not null && unit.Ap >= sim.Data.Rules.Turn.PickUpApCost)
            {
                var item = cache.Items.Where(id => profile.Load + sim.Data.Items[id].Weight <= profile.CarryCapacity + 1e-9)
                    .OrderByDescending(id => Value(sim.Data.Items[id])).FirstOrDefault();
                if (item is not null) return new PickUp(playerId, unit.Id, cache.Id, item);
            }
        }

        // Goals in order (close in on a visible enemy, a stranger to recruit, the nearest cache we can still carry from,
        // an exit); take the first one we can actually make progress towards this turn.
        var goals = new List<Pos>();
        if (!leaving)
        {
            goals.AddRange(hostiles.OrderBy(h => h.Pos.Chebyshev(unit.Pos)).Select(h => h.Pos));
            goals.AddRange(strangers.OrderBy(n => n.Pos.Chebyshev(unit.Pos)).Select(n => n.Pos));
            goals.AddRange(state.Caches.Where(c => c.Items.Any(id => profile.Load + sim.Data.Items[id].Weight <= profile.CarryCapacity + 1e-9))
                .OrderBy(c => c.Pos.Chebyshev(unit.Pos)).Select(c => c.Pos));
        }
        goals.AddRange(map.Exits.OrderBy(e => e.Chebyshev(unit.Pos)));

        var reachable = sim.Reachable(unit).Where(kv => kv.Value <= unit.Ap).ToList();
        foreach (var goal in goals.Take(6))
        {
            var field = Movement.DistanceField(map, profile.Movement, goal);
            if (!field.TryGetValue(unit.Pos, out var here)) continue;
            // Among equally good steps, one next to an ally (backs covered against ambushes).
            bool Covered(Pos p) => defenses && allies.Any(x => x.Id != unit.Id && x.IsActive && x.Pos.Chebyshev(p) <= 1);
            var step = reachable
                .Where(kv => field.TryGetValue(kv.Key, out var d) && d < here)
                .OrderBy(kv => field[kv.Key]).ThenBy(kv => Covered(kv.Key) ? 0 : 1).ThenBy(kv => kv.Value).ThenBy(kv => map.Index(kv.Key))
                .Select(kv => (Pos?)kv.Key).FirstOrDefault();
            if (step is { } to) return new Move(playerId, unit.Id, to);
            // Boxed in (often by our own follower) for a couple of turns: sidestep to the best free tile.
            if (stalled >= 2 && reachable.Where(kv => field.ContainsKey(kv.Key)).OrderBy(kv => field[kv.Key]).ThenBy(kv => map.Index(kv.Key))
                    .Select(kv => (Pos?)kv.Key).FirstOrDefault() is { } aside)
                return new Move(playerId, unit.Id, aside);
        }
        if (map.IsExit(unit.Pos) && (leaving || state.Round > MaxRounds / 2)) return new Extract(playerId, unit.Id);
        return defenses ? Hold(sim, unit, playerId, hostiles, contacts, shotAt) : new EndTurn(playerId, unit.Id);
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
        if ((contacts.Count > 0 || hostiles.Count > 0) && unit.Ap >= fog.Overwatch.ApCost)
            return new SetStance(playerId, unit.Id, Stance.Overwatch);
        return new EndTurn(playerId, unit.Id);
    }

    private static int ThrowRange(ScavengeSim sim, Unit unit, string baseId) =>
        unit.Pack.Select(id => sim.Data.Items[id]).Where(i => i.DefinedId == baseId && i.Use is not null).Select(i => i.Use!.Range).DefaultIfEmpty(0).Max();

    private static string? Usable(ScavengeSim sim, Unit unit, string baseId, Func<UseEffect, bool> does) =>
        unit.Pack.FirstOrDefault(id => sim.Data.Items[id] is { Use: { } use } item && item.DefinedId == baseId && does(use) && use.ApCost <= unit.Ap);

    /// <summary>Rough value per kilo so packs fill with the good stuff first.</summary>
    private static double Value(ItemDef item) => item.Kind switch
    {
        ItemKind.Blueprint => 100,
        ItemKind.Prosthetic or ItemKind.Weapon or ItemKind.Armor or ItemKind.Clothing or ItemKind.Tool => 20,
        ItemKind.Consumable => 15,
        _ => item.Salvage is { } s ? 10 * s.Amount / Math.Max(item.Weight, 0.1) / 3 : 1,
    };
}
