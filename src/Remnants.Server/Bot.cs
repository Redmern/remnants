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

        var transport = await TcpClientTransport.ConnectAsync(host, port);
        var inbox = Channel.CreateUnbounded<Action>();
        var client = new NetClient(transport, data, work => inbox.Writer.TryWrite(work));
        client.Noticed += text => Program.Print($"{name}: {text}");
        transport.Start();

        var survivors = NewGame.Create(game, (ulong)name.Sum(c => c)).State.Survivors.Where(s => !s.IsChild).Take(2).ToList();
        var party = survivors.Select((s, i) => new PartyMember(s,
            new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = i == 0 ? "pistol" : "pipe" }, ["bandage"])).ToList();
        client.Join(name, party);
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
                // A rejected order falls back to waiting so the bot never stalls its own turn.
                if (command is not EndTurn) Send(new EndTurn(command.PlayerId, command.UnitId));
            }));
        }

        await foreach (var work in inbox.Reader.ReadAllAsync())
        {
            work();
            if (client.Phase is ClientPhase.Ended or ClientPhase.Disconnected || client.RefusedReason is not null) break;
            if (!busy && client.View is { } view && Decide(client, view, rounds) is { } command) Send(command);
        }

        var result = client.Result;
        Program.Print(client.RefusedReason is { } reason
            ? $"{name}: refused: {reason}"
            : $"{name}: done ({client.EndReason ?? client.Phase.ToString()}); returned {result?.Returned.Count ?? 0}, dead {result?.Deaths.Count ?? 0}, loot {result?.Loot.Values.Sum() ?? 0}");
        transport.Close();
        return client.RefusedReason is null ? 0 : 2;
    }

    private static IScavengeCommand? Decide(NetClient client, ScavengeSim view, int rounds)
    {
        var state = view.State;
        if (state.CurrentUnit is not { } unit || unit.PlayerId != client.PlayerId || !unit.PlayerControlled) return null;
        var pid = client.PlayerId;
        var map = state.Map;
        var profile = view.Profile(unit);

        var shot = profile.Actions.Where(x => x.ApCost <= unit.Ap)
            .SelectMany(x => state.Units.Where(t => t.OnMap && unit.HostileTo(t) && Combat.CheckReach(view.Data, map, unit.Pos, x, t.Pos) == ScavengeError.None)
                .Select(t => (Action: x, Target: t, Chance: Combat.HitChance(view.Data, map, unit.Pos, x, t.Pos, view.Profile(t)))))
            .OrderByDescending(o => o.Chance).FirstOrDefault();
        if (shot.Target is not null) return new Attack(pid, unit.Id, shot.Target.Id, shot.Action.Id);

        var leaving = state.Round > rounds;
        if (leaving && map.IsExit(unit.Pos)) return new Extract(pid, unit.Id);
        var centre = new Pos(map.Width / 2, map.Height / 2);
        int Distance(Pos p) => leaving ? map.Exits.Min(e => e.Chebyshev(p)) : p.Chebyshev(centre);
        var step = view.Reachable(unit).Where(kv => kv.Value <= unit.Ap && Distance(kv.Key) < Distance(unit.Pos))
            .OrderBy(kv => Distance(kv.Key)).ThenBy(kv => kv.Value).ThenBy(kv => map.Index(kv.Key))
            .Select(kv => (Pos?)kv.Key).FirstOrDefault();
        return step is { } to ? new Move(pid, unit.Id, to) : new EndTurn(pid, unit.Id);
    }
}
