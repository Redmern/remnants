using Remnants.Core.Random;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>Attack reach, hit chance and damage: injuries to body parts, HP, Downed and Dead.</summary>
public static class Combat
{
    /// <summary>Whether <paramref name="action"/> can reach <paramref name="target"/> from <paramref name="from"/>.</summary>
    public static ScavengeError CheckReach(ScavengeData data, TileMap map, Pos from, ActionProfile action, Pos target)
    {
        var d = target - from;
        var inReach = action.Pattern switch
        {
            AttackPattern.Melee => from.Chebyshev(target) <= action.Reach,
            AttackPattern.Ranged => from.DistanceSquared(target) <= action.Reach * action.Reach,
            AttackPattern.Line => (d.X == 0 || d.Y == 0 || Math.Abs(d.X) == Math.Abs(d.Y)) && from.Chebyshev(target) <= action.Reach,
            _ => false,
        };
        if (!inReach || from == target) return ScavengeError.OutOfRange;
        if (from.Chebyshev(target) > 1 && !map.HasLineOfSight(data, from, target)) return ScavengeError.NoLineOfSight;
        return ScavengeError.None;
    }

    /// <summary>Cover a target gets from the tile next to it on the attacker's side. Nothing at point-blank range.</summary>
    public static double CoverAgainst(ScavengeData data, TileMap map, Pos target, Pos attacker)
    {
        if (target.Chebyshev(attacker) <= 1) return 0;
        var shield = target + new Pos(Math.Sign(attacker.X - target.X), Math.Sign(attacker.Y - target.Y));
        return map.InBounds(shield) ? data.Terrain(map[shield]).Cover : 0;
    }

    /// <summary>
    /// Action accuracy minus the target's dodge and cover (plus its brace bonus), minus range falloff past the first tile,
    /// plus an ambush bonus and a bane's hit bonus, clamped to the rules' min/max. An ambush may also take the dodge away.
    /// </summary>
    public static double HitChance(ScavengeData data, TileMap map, Pos from, ActionProfile action, Pos target, UnitProfile targetProfile,
        AmbushInfo? ambush = null, BaneDef? bane = null)
    {
        var c = data.Rules.Combat;
        var chance = action.Accuracy
            - (ambush is { IgnoreDodge: true } ? Math.Min(0, targetProfile.Dodge) : targetProfile.Dodge)
            - CoverAgainst(data, map, target, from)
            - targetProfile.CoverBonus
            - Math.Max(0, Math.Sqrt(from.DistanceSquared(target)) - 1) * c.RangeFalloff
            + (ambush?.HitBonus ?? 0)
            + (bane?.HitBonus ?? 0);
        return Math.Clamp(chance, c.MinHitChance, c.MaxHitChance);
    }

    /// <summary>The hit chance of this attack as it would resolve now, ambush included (for previews and the AI).</summary>
    public static double ChanceToHit(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target, Pos? from = null) =>
        HitChance(sim.Data, sim.State.Map, from ?? attacker.Pos, action with { Accuracy = action.Accuracy + MarkBonus(sim, attacker, target) },
            target.Pos, sim.Profile(target), AmbushAgainst(sim.Data, sim.State, attacker, target, from), Bane(sim.Data, action, target));

    /// <summary>What a drone's mark on the target adds to the attacker's side's hit chance.</summary>
    public static double MarkBonus(ScavengeSim sim, Unit attacker, Unit target) =>
        Drones.IsMarked(sim.State, target.Id, attacker.Side) ? sim.Data.Rules.Drone.MarkHitBonus : 0;

    /// <summary>
    /// Whether an attack on <paramref name="target"/> would come from the fog: its side does not see the attacker (at
    /// <paramref name="from"/>, default where it stands). Null when it would not. A braced target gets no bonus against
    /// it; an ally next to the target ("watching each other's backs") takes off <see cref="AmbushRules.BacksReduction"/>.
    /// </summary>
    public static AmbushInfo? AmbushAgainst(ScavengeData data, ScavengeState state, Unit attacker, Unit target, Pos? from = null)
    {
        if (!attacker.HostileTo(target)) return null;
        bool Hidden()
        {
            if (from is not { } at || at == attacker.Pos) return !Detection.SideSees(data, state, target.Side, attacker);
            var moved = attacker.Pos;
            attacker.Pos = at;
            try { return !Detection.SideSees(data, state, target.Side, attacker); }
            finally { attacker.Pos = moved; }
        }
        if (!Hidden()) return null;

        var rules = data.Rules.Fog.Ambush;
        if (target.Stance == Stance.Brace) return new AmbushInfo(0, 1, false, Braced: true, Backs: false);
        var backs = state.Units.Any(u => u.Id != target.Id && u.IsActive && u.Side == target.Side && u.Pos.Chebyshev(target.Pos) <= 1);
        var keep = backs ? 1 - rules.BacksReduction : 1;
        return new AmbushInfo(rules.HitBonus * keep, 1 + (rules.DamageMultiplier - 1) * keep, rules.IgnoreDodge, Braced: false, Backs: backs);
    }

    /// <summary>
    /// Pays the AP (not for an overwatch reaction, which instead loses accuracy), makes noise, and strikes the target; an
    /// area action then strikes every other unit hostile to the attacker within <see cref="ActionProfile.Area"/> of the
    /// target (by id, each with its own roll). Whether each strike comes from the fog is judged before anything resolves.
    /// Afterwards the attacker is revealed to every side it hit at for <see cref="ActionProfile.Reveal"/> rounds, those
    /// sides get an attack contact pointing at it, every other side in earshot a noise contact, and overwatch may react.
    /// </summary>
    internal static void Resolve(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target, bool reaction = false)
    {
        if (!reaction) attacker.Ap -= action.ApCost;
        sim.MakeNoise(attacker, action.Noise);
        var center = target.Pos;
        var targets = new List<Unit> { target };
        if (action.Area > 0)
            targets.AddRange(sim.State.Units
                .Where(u => u.Id != target.Id && u.IsActive && attacker.HostileTo(u) && u.Pos.Chebyshev(center) <= action.Area)
                .OrderBy(u => u.Id));
        var ambushes = targets.Select(t => AmbushAgainst(sim.Data, sim.State, attacker, t)).ToList();
        var penalty = reaction ? sim.Data.Rules.Fog.Overwatch.AccuracyPenalty : 0;
        for (var i = 0; i < targets.Count; i++)
            Strike(sim, attacker, action with { Accuracy = action.Accuracy - penalty }, targets[i], ambushes[i]);
        AfterAttack(sim, attacker, action, targets);
    }

    /// <summary>
    /// Suppressing fire: no damage roll, but the target is pinned and loses AP next turn. Needs a ranged action that
    /// reaches the target. Gives the attacker away like any shot.
    /// </summary>
    internal static void Suppress(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target)
    {
        attacker.Ap -= action.ApCost;
        sim.MakeNoise(attacker, action.Noise);
        target.Suppressed = target.CalmUntilRound < sim.State.Round;
        sim.State.Log(new(sim.State.Round, ScavengeEventType.Suppressed,
            $"{attacker.Name} pins {target.Name} down with suppressing fire ({action.Name}).", attacker.Id, target.Id, target.Pos,
            Anonymous: $"{target.Name} is pinned down by fire from the fog."));
        AfterAttack(sim, attacker, action, [target]);
    }

    private static void AfterAttack(ScavengeSim sim, Unit attacker, ActionProfile action, List<Unit> targets)
    {
        var state = sim.State;
        var data = sim.Data;
        var struck = new HashSet<string>();
        foreach (var t in targets.Where(t => t.Side != attacker.Side))
        {
            if (struck.Add(t.Side) && !Detection.SideSees(data, state, t.Side, attacker))
                Detection.AddContact(data, state, t.Side, attacker.Pos, ContactKind.Attack, attacker.Id, t.Pos);
            if (action.Reveal > 0) Detection.Reveal(state, attacker, t.Side, state.Round + action.Reveal);
        }
        foreach (var side in Detection.PartySides(state).Where(s => s != attacker.Side && !struck.Contains(s)))
            if (state.Units.Any(u => u.Side == side && u.IsActive && u.Pos.Chebyshev(attacker.Pos) <= action.Noise)
                && !Detection.SideSees(data, state, side, attacker))
                Detection.AddContact(data, state, side, attacker.Pos, ContactKind.Noise, attacker.Id);
        sim.CheckOverwatch(attacker);
    }

    private static void Strike(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target, AmbushInfo? ambush)
    {
        var data = sim.Data;
        var state = sim.State;
        var bane = Bane(data, action, target);
        var chance = HitChance(data, state.Map, attacker.Pos, action with { Accuracy = action.Accuracy + MarkBonus(sim, attacker, target) },
            target.Pos, sim.Profile(target), ambush, bane);
        if (ambush is { } a)
            state.Log(new(state.Round, ScavengeEventType.Ambushed,
                $"{attacker.Name} ambushes {target.Name} from the fog{a.Describe()}.", attacker.Id, target.Id, target.Pos,
                Anonymous: $"{target.Name} was ambushed from the fog{a.Describe()}."));

        if (sim.Rng.NextDouble() >= chance)
        {
            state.Log(new(state.Round, ScavengeEventType.Missed,
                $"{attacker.Name} missed {target.Name} ({action.Name}, {chance:P0}).", attacker.Id, target.Id, target.Pos,
                Anonymous: $"An attack from the fog missed {target.Name}."));
            return;
        }

        var raw = (action.MinDamage + (action.MaxDamage - action.MinDamage) * sim.Rng.NextDouble()) * (ambush?.DamageMultiplier ?? 1);
        var armor = sim.Profile(target).Armor;
        if (bane is not null)
        {
            raw *= bane.DamageMultiplier;
            armor = Math.Max(0, armor - bane.ArmorPierce);
        }
        var damage = Math.Max(1, raw - armor);
        var part = PickHitLocation(sim, target);
        state.Log(new(state.Round, ScavengeEventType.Attacked,
            $"{attacker.Name} hit {target.Name}'s {part} for {damage:0} ({action.Name}).", attacker.Id, target.Id, target.Pos,
            Anonymous: $"{target.Name} was hit in the {part} for {damage:0} from the fog."));
        ApplyDamage(sim, target, part, damage);
        if (bane is { DisruptRounds: > 0 } && target.IsActive && Network.IsLinked(data, target))
        {
            target.OffRelayUntilRound = Math.Max(target.OffRelayUntilRound, state.Round + bane.DisruptRounds - 1);
            state.Log(new(state.Round, ScavengeEventType.RelayDisrupted, $"{target.Name} is knocked off the relay.", attacker.Id, target.Id, target.Pos,
                Anonymous: $"{target.Name} is knocked off the relay."));
        }
    }

    /// <summary>The action's bane when the target is an enemy carrying its tag, else null.</summary>
    public static BaneDef? Bane(ScavengeData data, ActionProfile action, Unit target) =>
        action.Bane is { } bane && target.EnemyDefId is { } id && data.Enemies[id].Tags.Contains(bane.Tag) ? bane : null;

    /// <summary>Weighted by rules.combat.hitLocations; a hit on a missing limb lands on the torso.</summary>
    private static BodyPart PickHitLocation(ScavengeSim sim, Unit target)
    {
        var weights = sim.Data.Rules.Combat.HitLocations.OrderBy(kv => kv.Key).ToList();
        var roll = sim.Rng.NextDouble() * weights.Sum(kv => kv.Value);
        var part = weights[^1].Key;
        foreach (var (p, w) in weights)
        {
            if (roll < w) { part = p; break; }
            roll -= w;
        }
        return target.Body[part].Condition == PartCondition.Missing ? BodyPart.Torso : part;
    }

    /// <summary>Damage to one body part: injury severity, possible limb loss, then HP and Downed/Dead.</summary>
    public static void ApplyDamage(ScavengeSim sim, Unit target, BodyPart part, double damage)
    {
        var data = sim.Data;
        var state = sim.State;
        var c = data.Rules.Combat;

        var bodyPart = target.Body[part];
        if (bodyPart.Condition is PartCondition.Healthy or PartCondition.Injured)
        {
            var severity = bodyPart.Severity + damage / c.PartToughness[Body.RegionOf(part)];
            target.Body.Injure(part, severity);
            state.Log(new(state.Round, ScavengeEventType.Injured,
                $"{target.Name}'s {part} is injured ({target.Body[part].Severity:P0}).", target.Id, Pos: target.Pos));
            var limb = Body.RegionOf(part) is BodyRegion.Arms or BodyRegion.Legs;
            if (limb && severity >= 1 && sim.Rng.NextDouble() < c.LimbLossChance)
            {
                target.Body.Lose(part);
                state.Log(new(state.Round, ScavengeEventType.LimbLost, $"{target.Name} lost their {part}.", target.Id, Pos: target.Pos));
            }
            else if (target.Kind == UnitKind.Survivor
                     && Treatment.MaybeFracture(data.Medicine, target.Body, part, SideRoll.Next(sim.Rng, state.Round, target.Id, (long)part, (long)(damage * 1000))))
                state.Log(new(state.Round, ScavengeEventType.Fractured, $"{target.Name}'s {part} is broken.", target.Id, Pos: target.Pos));
        }

        var wasDowned = target.Status == UnitStatus.Downed;
        target.Hp -= damage;
        if (target.Hp > 0) return;

        if (wasDowned || !target.CanBeDowned || target.Hp <= c.InstantDeathHp)
        {
            sim.Kill(target, ScavengeEventType.Died, $"{target.Name} is dead.");
            return;
        }
        target.Status = UnitStatus.Downed;
        target.BleedOut = c.BleedOutRounds;
        target.Stabilized = false;
        target.Ap = 0;
        state.Log(new(state.Round, ScavengeEventType.Downed,
            $"{target.Name} is down and bleeding out ({c.BleedOutRounds} rounds).", target.Id, Pos: target.Pos));
    }
}

/// <summary>
/// An attack from the fog: what it adds to the hit chance and damage, and whether the target loses its dodge.
/// <see cref="Braced"/>: the target was braced, so the attack is unseen but gains nothing. <see cref="Backs"/>: an ally
/// next to the target cut the bonuses.
/// </summary>
public sealed record AmbushInfo(double HitBonus, double DamageMultiplier, bool IgnoreDodge, bool Braced, bool Backs)
{
    /// <summary>" (+20% hit)", " (braced)", " (+10% hit, backs covered)" for the log.</summary>
    public string Describe() =>
        Braced ? " (braced: no bonus)" : $" (+{HitBonus * 100:0}% hit{(Backs ? ", backs covered" : "")})";
}
