namespace Remnants.Core.Scavenge;

/// <summary>
/// Teams of players. Allied parties share one side (<see cref="Unit.TeamSide"/>), so every side-based rule treats them
/// as one: no friendly fire, one fog of war (what any ally sees, contacts, reveals, sensors and explored tiles), overwatch
/// never fires on allies, and allies count for "watching each other's backs". Ownership, turns and results stay per
/// player (<see cref="Unit.PlayerId"/>).
/// <list type="bullet">
/// <item>Teams start allied (a co-op mission, <see cref="PartySetup.Team"/>) or form in the field: a player whose party
///   and another's see each other offers an alliance (<see cref="ProposeAlliance"/>), the other accepts or declines on
///   their turn; on accept the two teams merge and share vision from that moment.</item>
/// <item>Breaking (<see cref="BreakAlliance"/>) is announced to the team and takes effect when round
///   announced + 1 + <see cref="CoopRules.BreakWarningRounds"/> starts: no instant backstab. The leaver goes back to
///   their own side; a team left with one player dissolves. Each side keeps a copy of what the team knew (contacts,
///   reveals, explored tiles) and sensors go with whoever placed them; vision is separate from then on.</item>
/// </list>
/// </summary>
public static class Alliances
{
    /// <summary>Whether the two players' parties see each other right now (each side sees a unit of the other).</summary>
    public static bool InContact(ScavengeSim sim, int a, int b)
    {
        var state = sim.State;
        if (state.FindParty(a) is not { } pa || state.FindParty(b) is not { } pb) return false;
        bool Sees(Party seer, int owner) => state.Units.Any(u =>
            u.PlayerId == owner && u.PlayerControlled && u.IsActive && Detection.SideSees(sim.Data, state, seer.Side, u));
        return Sees(pa, b) && Sees(pb, a);
    }

    /// <summary>Why <paramref name="from"/> cannot offer <paramref name="to"/> an alliance now, or null when it can.</summary>
    public static ScavengeResult? CheckPropose(ScavengeSim sim, int from, int to)
    {
        var state = sim.State;
        if (from == to || state.FindParty(to) is not { Done: false })
            return ScavengeResult.Fail(ScavengeError.NotAPlayer, $"Player {to} has no party in the zone.");
        if (state.Allied(from, to)) return ScavengeResult.Fail(ScavengeError.AlreadyAllied, $"Player {to} is already an ally.");
        if (state.Proposals.Any(p => (p.FromPlayerId, p.ToPlayerId) == (from, to) || (p.FromPlayerId, p.ToPlayerId) == (to, from)))
            return ScavengeResult.Fail(ScavengeError.ProposalPending, "An alliance offer between you is already open.");
        if (!InContact(sim, from, to)) return ScavengeResult.Fail(ScavengeError.NoContact, "Your parties must see each other to talk.");
        return null;
    }

    internal static ScavengeResult Propose(ScavengeSim sim, Unit unit, ProposeAlliance c)
    {
        if (CheckPropose(sim, c.PlayerId, c.ToPlayerId) is { } problem) return problem;
        var state = sim.State;
        state.Proposals.Add(new AllianceProposal(c.PlayerId, c.ToPlayerId, state.Round));
        state.Log(new(state.Round, ScavengeEventType.AllianceProposed,
            $"{unit.Name} (player {c.PlayerId}) offers player {c.ToPlayerId} an alliance.", unit.Id, Pos: unit.Pos,
            Sides: [Side(state, c.PlayerId), Side(state, c.ToPlayerId)]));
        return ScavengeResult.Success($"Alliance offered to player {c.ToPlayerId}.");
    }

    internal static ScavengeResult Respond(ScavengeSim sim, Unit unit, RespondAlliance c)
    {
        var state = sim.State;
        var offer = state.Proposals.Find(p => p.FromPlayerId == c.FromPlayerId && p.ToPlayerId == c.PlayerId);
        if (offer is null) return ScavengeResult.Fail(ScavengeError.NoProposal, $"Player {c.FromPlayerId} made you no offer.");
        state.Proposals.Remove(offer);
        var sides = new List<string> { Side(state, c.FromPlayerId), Side(state, c.PlayerId) };
        if (!c.Accept)
        {
            state.Log(new(state.Round, ScavengeEventType.AllianceDeclined,
                $"Player {c.PlayerId} declines player {c.FromPlayerId}'s alliance.", unit.Id, Pos: unit.Pos, Sides: sides));
            return ScavengeResult.Success($"Declined player {c.FromPlayerId}'s offer.");
        }
        if (state.FindParty(c.FromPlayerId) is not { Done: false })
            return ScavengeResult.Fail(ScavengeError.NotAPlayer, $"Player {c.FromPlayerId} has left the zone.");
        var side = Merge(state, c.FromPlayerId, c.PlayerId);
        state.Log(new(state.Round, ScavengeEventType.AllianceAccepted,
            $"Players {c.FromPlayerId} and {c.PlayerId} are allies now: shared sight, no friendly fire.", unit.Id, Pos: unit.Pos,
            Sides: [side]));
        return ScavengeResult.Success($"Allied with player {c.FromPlayerId}.");
    }

    internal static ScavengeResult Break(ScavengeSim sim, Unit unit, BreakAlliance c)
    {
        var state = sim.State;
        var side = Side(state, c.PlayerId);
        if (state.PlayersOn(side).Count() < 2) return ScavengeResult.Fail(ScavengeError.NotAllied, "You have no allies to leave.");
        if (state.Breaks.Any(b => b.PlayerId == c.PlayerId))
            return ScavengeResult.Fail(ScavengeError.BreakPending, "You are already leaving the alliance.");
        var effective = state.Round + 1 + sim.Data.Rules.Coop.BreakWarningRounds;
        state.Breaks.Add(new AllianceBreak(c.PlayerId, side, state.Round, effective));
        state.Log(new(state.Round, ScavengeEventType.AllianceBreakWarning,
            $"Player {c.PlayerId} will leave the alliance when round {effective} starts.", unit.Id, Pos: unit.Pos, Sides: [side]));
        return ScavengeResult.Success($"Leaving the alliance at round {effective}.");
    }

    /// <summary>Round start: offers lapse, pings fade, announced breaks whose round has come take effect.</summary>
    internal static void RoundStarted(ScavengeSim sim)
    {
        var state = sim.State;
        var coop = sim.Data.Rules.Coop;
        state.Proposals.RemoveAll(p => p.Round + coop.ProposalRounds <= state.Round);
        state.Pings.RemoveAll(p => p.Round + coop.PingRounds <= state.Round);
        foreach (var b in state.Breaks.Where(b => b.EffectiveRound <= state.Round).ToList())
        {
            state.Breaks.Remove(b);
            var team = Side(state, b.PlayerId);
            if (state.PlayersOn(team).Count() < 2) continue;
            var staying = state.PlayersOn(team).First(id => id != b.PlayerId);
            var own = Split(state, b.PlayerId);
            var rest = Side(state, staying);
            state.Log(new(state.Round, ScavengeEventType.AllianceBroken,
                $"Player {b.PlayerId} has left the alliance: no more shared sight, and you are fair game to each other.",
                Sides: [own, rest]));
        }
    }

    /// <summary>
    /// Puts every player on <paramref name="a"/>'s or <paramref name="b"/>'s side on one new team side and moves what
    /// those sides knew over to it. Returns the team side. Public for setting up co-op missions and tests.
    /// </summary>
    public static string Merge(ScavengeState state, int a, int b)
    {
        var old = new HashSet<string> { Side(state, a), Side(state, b) };
        var team = Unit.TeamSide(state.NextTeamId++);
        foreach (var party in state.Parties.Where(p => old.Contains(p.Side))) Move(state, party, team);
        state.Contacts = state.Contacts.Select(c => old.Contains(c.Side) ? c with { Side = team } : c).ToList();
        state.Sensors = state.Sensors.Select(s => old.Contains(s.Side) ? s with { Side = team } : s).ToList();
        state.Pings = state.Pings.Select(p => old.Contains(p.Side) ? p with { Side = team } : p).ToList();
        state.Reveals = state.Reveals
            .Select(r => old.Contains(r.Side) ? r with { Side = team } : r)
            .GroupBy(r => (r.UnitId, r.Side)).Select(g => g.MaxBy(r => r.UntilRound)!).ToList();
        var explored = new SortedSet<int>();
        foreach (var side in old)
            if (state.Explored.Remove(side, out var tiles)) explored.UnionWith(tiles);
        state.Explored[team] = explored;
        return team;
    }

    /// <summary>
    /// Takes <paramref name="playerId"/> off its team onto its own side; a team left with one player dissolves the same
    /// way. Both keep a copy of the team's contacts, reveals and explored tiles; sensors and pings go with their owner.
    /// Returns the leaver's side.
    /// </summary>
    internal static string Split(ScavengeState state, int playerId)
    {
        var team = Side(state, playerId);
        var leaving = new List<int> { playerId };
        var staying = state.PlayersOn(team).Where(id => id != playerId).ToList();
        if (staying.Count == 1) leaving.Add(staying[0]);
        foreach (var id in leaving)
        {
            var side = Unit.PlayerSide(id);
            Move(state, state.FindParty(id)!, side);
            state.Contacts.AddRange(state.Contacts.Where(c => c.Side == team).Select(c => c with { Side = side }).ToList());
            state.Reveals.AddRange(state.Reveals.Where(r => r.Side == team).Select(r => r with { Side = side }).ToList());
            if (state.Explored.TryGetValue(team, out var tiles)) state.Explored[side] = new SortedSet<int>(tiles);
            state.Sensors = state.Sensors.Select(s => s.Side == team && s.PlayerId == id ? s with { Side = side } : s).ToList();
            state.Pings = state.Pings.Select(p => p.Side == team && p.PlayerId == id ? p with { Side = side } : p).ToList();
        }
        if (!state.Parties.Any(p => p.Side == team))
        {
            state.Contacts.RemoveAll(c => c.Side == team);
            state.Reveals.RemoveAll(r => r.Side == team);
            state.Explored.Remove(team);
            state.Sensors.RemoveAll(s => s.Side == team);
            state.Pings.RemoveAll(p => p.Side == team);
        }
        state.Breaks.RemoveAll(b => leaving.Contains(b.PlayerId) && b.PlayerId != playerId);
        // Ex-allies put down each other's downed: nobody walks off with a former ally.
        foreach (var carrier in state.Units.Where(u => u.CarryingId is { } id && state.FindUnit(id) is { } c && c.Side != u.Side).ToList())
            CoopActions.Release(state, carrier);
        return Unit.PlayerSide(playerId);
    }

    /// <summary>A party and every unit its player owns (survivors, recruits) change side; a carried unit stays where it is.</summary>
    private static void Move(ScavengeState state, Party party, string side)
    {
        party.Side = side;
        foreach (var u in state.Units.Where(u => u.PlayerId == party.PlayerId && u.Side != Unit.NeutralSide)) u.Side = side;
    }

    private static string Side(ScavengeState state, int playerId) => state.FindParty(playerId)?.Side ?? Unit.PlayerSide(playerId);
}
