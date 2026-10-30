using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Who sees whom. Sight (<see cref="Visibility"/>) says which tiles a unit could see; inside that, a hostile is only
/// <em>seen</em> when the observer's detection beats its stealth, less <see cref="FogRules.PerTile"/> per tile beyond
/// <see cref="FogRules.CloseRange"/> (where everything in sight is seen). Time of day and weather change sight and stealth
/// (<see cref="FieldEnvironment"/>). No randomness: the same positions and gear
/// always give the same answer. A side also sees
/// <list type="bullet">
/// <item>everyone on a tile lit by a <see cref="Flare"/> (every side sees lit tiles), and everyone on the map in the round
/// lightning strikes (<see cref="FieldEnvironment.LightningNow"/>);</item>
/// <item>a unit <see cref="Reveal"/>ed to it (muzzle flash, motion sensor) while one of its units has line of sight to it
/// within <see cref="FogRules.RevealSightMultiplier"/> times its sight;</item>
/// <item>hostiles within a working scanner's (or thermal eye's) radius of one of its units, through walls and stealth;</item>
/// <item>a hostile its scout drone marked (<see cref="Drones"/>), wherever it is, while the mark lasts.</item>
/// </list>
/// Contacts ("?" markers) are the middle state: the side knows something is near a tile, not what.
/// </summary>
public static class Detection
{
    /// <summary>
    /// Whether <paramref name="observer"/> picks out <paramref name="target"/> by its own eyes, with both optionally at
    /// hypothetical tiles (AI position scoring). Ignores flares, reveals and scanners (see <see cref="SideSees"/>).
    /// </summary>
    public static bool Spots(ScavengeData data, ScavengeState state, Unit observer, Unit target, Pos? targetAt = null, Pos? observerAt = null)
    {
        var from = observerAt ?? observer.Pos;
        var to = targetAt ?? target.Pos;
        var seer = ProfileDeriver.Derive(data, observer);
        // A weapon light shows what it falls on as by day: full sight out to its range, and the dark hides nothing there.
        var lit = GearEffects.Illuminates(data, state, observer, from, to);
        var sight = FieldEnvironment.Sight(data, state, observer, seer.Sight);
        if (lit) sight = Math.Max(sight, seer.Sight);
        if (!Visibility.CanSee(data, state.Map, from, sight, to)) return false;
        var fog = data.Rules.Fog;
        if (from.Chebyshev(to) <= fog.CloseRange) return true;
        var distance = Math.Sqrt(from.DistanceSquared(to));
        var stealth = ProfileDeriver.Derive(data, target).Stealth + (lit ? 0 : FieldEnvironment.StealthBonusAgainst(data, state, observer))
            + GearEffects.StealthAgainst(data, state, target, observer);
        return seer.Detection - stealth - (distance - fog.CloseRange) * fog.PerTile >= -1e-9;
    }

    /// <summary>Whether <paramref name="side"/> sees <paramref name="target"/> right now (its own units always).</summary>
    public static bool SideSees(ScavengeData data, ScavengeState state, string side, Unit target)
    {
        if (target.Side == side) return true;
        if (!target.OnMap) return false;
        var observers = state.Units.Where(u => u.Side == side && u.IsActive).ToList();
        if (observers.Count == 0) return false;
        if (IsLit(state, target.Pos) || FieldEnvironment.LightningNow(state)) return true;
        if (Drones.IsMarked(state, target.Id, side)) return true;
        if (IsRevealed(state, target.Id, side) && observers.Any(o => SeesRevealed(data, state, o, target))) return true;
        if (observers.Any(o => GearEffects.BeaconSeenBy(data, state, o, target))) return true;
        var masked = GearEffects.ThermalMasked(data, target);
        if (observers.Any(o => o.HostileTo(target) && ScanRadius(data, o, thermal: !masked) is var r && r > 0 && o.Pos.Chebyshev(target.Pos) <= r))
            return true;
        return observers.Any(o => Spots(data, state, o, target));
    }

    /// <summary>
    /// Whether an observer picks out a unit revealed to its side: line of sight within the stretched reveal range. The
    /// observer's sight before the environment counts: a muzzle flash or a sensor ping shows up in the dark too.
    /// </summary>
    public static bool SeesRevealed(ScavengeData data, ScavengeState state, Unit observer, Unit target) =>
        IsRevealed(state, target.Id, observer.Side)
        && Visibility.CanSee(data, state.Map, observer.Pos, ProfileDeriver.Derive(data, observer).Sight * data.Rules.Fog.RevealSightMultiplier, target.Pos);

    /// <summary>Ids of every unit on the map the side sees right now, its own included.</summary>
    public static HashSet<int> SeenBy(ScavengeData data, ScavengeState state, string side) =>
        state.Units.Where(u => u.OnMap && SideSees(data, state, side, u)).Select(u => u.Id).ToHashSet();

    public static bool IsRevealed(ScavengeState state, int unitId, string side) =>
        state.Reveals.Any(r => r.UnitId == unitId && r.Side == side && r.UntilRound >= state.Round);

    public static bool IsLit(ScavengeState state, Pos tile) =>
        state.Flares.Any(f => f.UntilRound >= state.Round && f.Pos.DistanceSquared(tile) <= f.Radius * f.Radius + 1e-9);

    /// <summary>Tile indices lit by burning flares.</summary>
    public static IEnumerable<int> LitTiles(ScavengeState state)
    {
        var map = state.Map;
        foreach (var f in state.Flares.Where(f => f.UntilRound >= state.Round))
        {
            var r = (int)Math.Ceiling(f.Radius);
            for (var y = f.Pos.Y - r; y <= f.Pos.Y + r; y++)
                for (var x = f.Pos.X - r; x <= f.Pos.X + r; x++)
                {
                    var p = new Pos(x, y);
                    if (map.InBounds(p) && IsLit(state, p)) yield return map.Index(p);
                }
        }
    }

    /// <summary>Radius of the unit's working scanner or thermal eye (<c>thermal_radius</c>), 0 without either.</summary>
    /// <param name="thermal">False against a target in thermal masking: only a scanner device counts.</param>
    public static int ScanRadius(ScavengeData data, Unit unit, bool thermal = true) =>
        Devices.Working(data, unit).Where(d => d.Effect == DeviceEffect.Scanner).Select(d => d.Radius)
            .Append(unit.IsActive && thermal ? (int)unit.Hook(Colony.PerkHooks.ThermalRadius) : 0).Max();

    /// <summary>Reveals a unit to a side through <paramref name="untilRound"/> (keeps the later of two reveals).</summary>
    internal static void Reveal(ScavengeState state, Unit unit, string side, int untilRound)
    {
        var existing = state.Reveals.FindIndex(r => r.UnitId == unit.Id && r.Side == side);
        if (existing >= 0)
        {
            if (state.Reveals[existing].UntilRound >= untilRound) return;
            state.Reveals.RemoveAt(existing);
        }
        state.Reveals.Add(new Reveal(unit.Id, side, untilRound));
    }

    /// <summary>
    /// Adds a contact for a side near <paramref name="at"/>, offset by up to <see cref="FogRules.ContactFuzz"/> per axis
    /// from a hash of (round, source, side), so it is approximate but deterministic and draws nothing from the RNG.
    /// A later contact of the same kind this round close to it (within the fuzz) replaces it, so a walk is one "?".
    /// </summary>
    internal static void AddContact(ScavengeData data, ScavengeState state, string side, Pos at, ContactKind kind, int sourceId, Pos? toward = null)
    {
        var fuzz = data.Rules.Fog.ContactFuzz;
        var h = Hash(state.Round, sourceId, side, (int)kind);
        var pos = new Pos(
            Math.Clamp(at.X + (int)(h % (ulong)(2 * fuzz + 1)) - fuzz, 0, state.Map.Width - 1),
            Math.Clamp(at.Y + (int)(h / 7919 % (ulong)(2 * fuzz + 1)) - fuzz, 0, state.Map.Height - 1));
        state.Contacts.RemoveAll(c => c.Side == side && c.Round == state.Round && c.Kind == kind && c.Pos.Chebyshev(pos) <= 2 * fuzz + 1);
        state.Contacts.Add(new Contact(side, pos, kind, state.Round, toward));
    }

    /// <summary>Round start: drops faded contacts, reveals and flares.</summary>
    internal static void Expire(ScavengeData data, ScavengeState state)
    {
        var rounds = data.Rules.Fog.ContactRounds;
        state.Contacts.RemoveAll(c => c.Round + rounds <= state.Round);
        state.Reveals.RemoveAll(r => r.UntilRound < state.Round);
        state.Flares.RemoveAll(f => f.UntilRound < state.Round);
        state.Marks.RemoveAll(m => m.UntilRound < state.Round);
    }

    /// <summary>Party sides still playing (the sides that keep contacts).</summary>
    internal static IEnumerable<string> PartySides(ScavengeState state) =>
        state.Parties.Where(p => !p.Done).Select(p => p.Side).Distinct();

    private static ulong Hash(int round, int source, string side, int salt)
    {
        var h = 14695981039346656037UL;
        void Mix(int v)
        {
            h ^= (uint)v;
            h *= 1099511628211UL;
        }
        Mix(round);
        Mix(source);
        Mix(salt);
        foreach (var ch in side) Mix(ch);
        h ^= h >> 29;
        return h;
    }
}
