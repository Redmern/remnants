using System.Threading.Channels;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Net;

namespace Remnants.Server;

/// <summary>
/// A scripted headless client: joins with two armed adults from a fresh colony, readies, then on each of its turns
/// shoots the best visible target, else walks toward the map centre (for <c>--rounds</c> rounds) and then to the
/// nearest exit and extracts. It only uses what its fog-filtered view shows, like a real client.
/// <para>
/// As an ally (a co-op mission, <c>--team N</c> in a versus lobby, or an alliance it accepted: it accepts every offer
/// unless <c>--hostile</c>; with <c>--propose</c> it also offers one to every rival party it is in contact with) it patches up and revives allies next to it, carries a downed one it cannot revive to the
/// exit, follows the nearest ally instead of the map centre, and covers them on overwatch when there is nothing to
/// shoot; it leaves once the rounds are up or its allies are gone.
/// </para>
/// </summary>
internal static class Bot
{
    public static async Task<int> Run(string target, Args a, ScavengeData data, GameData game)
    {
        var colon = target.LastIndexOf(':');
        var host = colon > 0 ? target[..colon] : target;
        var port = colon > 0 ? int.Parse(target[(colon + 1)..]) : Protocol.DefaultPort;
        var name = a.Value("--name") ?? "Bot";
        var rounds = a.Int("--rounds", 30);
        var hostile = a.Flag("--hostile");
        var propose = a.Flag("--propose") && !hostile;
        var offered = new HashSet<int>();

        var transport = await TcpClientTransport.ConnectAsync(host, port);
        var inbox = Channel.CreateUnbounded<Action>();
        var client = new NetClient(transport, data, work => inbox.Writer.TryWrite(work));
        client.Noticed += text => Program.Print($"{name}: {text}");
        if (a.Flag("--trade")) BotTrade.Attach(client, name, BotTrade.ParseGoods(a.Value("--trade-offer") ?? "scrap:20,bandage", game, data));
        transport.Start();

        var survivors = NewGame.Create(game, (ulong)name.Sum(c => c)).State.Survivors.Where(s => !s.IsChild).Take(2).ToList();
        var party = survivors.Select((s, i) => new PartyMember(s,
            new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = i == 0 ? "pistol" : "pipe" }, ["bandage", "med_kit"])).ToList();
        client.Join(name, party);
        if (a.Value("--team") is { } team) client.SetTeam(int.Parse(team));
        client.SetReady(true);
        Program.Print($"{name}: joining {target} with {string.Join(", ", party.Select(p => p.Survivor.Name))}");

        var busy = false;
        void Send(IScavengeCommand command)
        {
            busy = true;
            _ = client.Execute(command).ContinueWith(t => inbox.Writer.TryWrite(() =>
            {
                busy = false;
                if (t.Result.Ok) return;
                Program.Print($"{name}: rejected {command.GetType().Name}: {t.Result.Message}");
                // A rejected order falls back to waiting so the bot never stalls its own turn (talk is free: just go on).
                if (command is not EndTurn and not ProposeAlliance) Send(new EndTurn(command.PlayerId, command.UnitId));
            }));
        }

        await foreach (var work in inbox.Reader.ReadAllAsync())
        {
            work();
            if (client.Phase is ClientPhase.Ended or ClientPhase.Disconnected || client.RefusedReason is not null) break;
            if (!busy && client.View is { } view && Decide(client, view, rounds, hostile, propose ? offered : null) is { } command) Send(command);
        }

        var result = client.Result;
        Program.Print(client.RefusedReason is { } reason
            ? $"{name}: refused: {reason}"
            : $"{name}: done ({client.EndReason ?? client.Phase.ToString()}); returned {result?.Returned.Count ?? 0}, dead {result?.Deaths.Count ?? 0}, loot {result?.Loot.Values.Sum() ?? 0}");
        transport.Close();
        return client.RefusedReason is null ? 0 : 2;
    }

    private static IScavengeCommand? Decide(NetClient client, ScavengeSim view, int rounds, bool hostile, HashSet<int>? offered)
    {
        var state = view.State;
        if (state.CurrentUnit is not { } unit || unit.PlayerId != client.PlayerId || !unit.PlayerControlled) return null;
        var pid = client.PlayerId;
        var map = state.Map;
        var profile = view.Profile(unit);

        if (!hostile && state.Proposals.Find(p => p.ToPlayerId == pid) is { } offer)
            return new RespondAlliance(pid, unit.Id, offer.FromPlayerId, Accept: true);

        if (offered is not null && state.CurrentUnit is { } speaker)
            foreach (var rival in state.Parties.Where(p => p.PlayerId != pid && !p.Done && p.Side != speaker.Side && !offered.Contains(p.PlayerId)))
                if (state.Units.Any(u => u.PlayerId == rival.PlayerId && u.IsActive))
                {
                    offered.Add(rival.PlayerId);
                    return new ProposeAlliance(pid, speaker.Id, rival.PlayerId);
                }

        // Hold fire on a player with an open alliance offer either way.
        bool Talking(Unit t) => t.PlayerId is { } other && state.Proposals.Any(p =>
            (p.FromPlayerId, p.ToPlayerId) == (pid, other) || (p.FromPlayerId, p.ToPlayerId) == (other, pid));

        var allies = state.Units.Where(u => u.Side == unit.Side && u.PlayerId != pid && u.Kind == UnitKind.Survivor && u.OnMap).ToList();
        if (Help(view, unit, allies) is { } help) return help;

        var shot = profile.Actions
            .SelectMany(x => state.Units.Where(t => t.OnMap && unit.HostileTo(t) && !Talking(t) && Combat.ApCost(x, unit.Pos, t.Pos) <= unit.Ap
                                                    && Combat.CheckReach(view.Data, map, unit.Pos, x, t.Pos) == ScavengeError.None)
                .Select(t => (Action: x, Target: t, Chance: Combat.HitChance(view.Data, map, unit.Pos, x, t.Pos, view.Profile(t)))))
            .OrderByDescending(o => o.Chance).FirstOrDefault();
        if (shot.Target is not null) return new Attack(pid, unit.Id, shot.Target.Id, shot.Action.Id);

        var hadAllies = client.Mission?.Mode == MissionMode.Coop || state.Parties.Any(p => p.PlayerId != pid && p.Side == unit.Side);
        var leaving = state.Round > rounds || unit.CarryingId is not null || (hadAllies && allies.All(a => !a.IsActive));
        if (leaving && map.IsExit(unit.Pos)) return new Extract(pid, unit.Id);

        // Follow the nearest ally (a downed one first); once next to them, cover them on overwatch.
        var buddy = allies.OrderBy(a => a.IsActive ? 1 : 0).ThenBy(a => a.Pos.Chebyshev(unit.Pos)).ThenBy(a => a.Id).FirstOrDefault();
        if (!leaving && buddy is not null && buddy.Pos.Chebyshev(unit.Pos) <= 2)
        {
            var cost = view.Data.Rules.Fog.Overwatch.ApCost;
            return profile.Actions.Count > 0 && unit.Ap > cost ? new SetStance(pid, unit.Id, Stance.Overwatch) : new EndTurn(pid, unit.Id);
        }
        var centre = buddy is not null && !leaving ? buddy.Pos : new Pos(map.Width / 2, map.Height / 2);
        int Distance(Pos p) => leaving ? map.Exits.Min(e => e.Chebyshev(p)) : p.Chebyshev(centre);
        var step = view.Reachable(unit).Where(kv => kv.Value <= unit.Ap && Distance(kv.Key) < Distance(unit.Pos))
            .OrderBy(kv => Distance(kv.Key)).ThenBy(kv => kv.Value).ThenBy(kv => map.Index(kv.Key))
            .Select(kv => (Pos?)kv.Key).FirstOrDefault();
        return step is { } to ? new Move(pid, unit.Id, to) : new EndTurn(pid, unit.Id);
    }

    /// <summary>An ally next to the unit needs help: revive with a med-kit, patch up with a bandage, or carry them out.</summary>
    private static IScavengeCommand? Help(ScavengeSim view, Unit unit, List<Unit> allies)
    {
        var pid = unit.PlayerId!.Value;
        foreach (var ally in allies.Where(a => a.Pos.Chebyshev(unit.Pos) <= 1).OrderBy(a => a.Hp).ThenBy(a => a.Id))
        {
            bool Can(string item) => unit.Pack.Contains(item) && view.Data.Items[item].Use is { } use && use.ApCost <= unit.Ap;
            if (ally.Status == UnitStatus.Downed && ally.CarriedById is null)
            {
                if (Can("med_kit")) return new UseItem(pid, unit.Id, "med_kit", ally.Id);
                if (CoopActions.CheckCarry(view, unit, ally) is null) return new Carry(pid, unit.Id, ally.Id);
            }
            else if (ally.IsActive && ally.Hp < ally.MaxHp * 0.6 && Can("bandage")) return new UseItem(pid, unit.Id, "bandage", ally.Id);
        }
        return null;
    }
}
