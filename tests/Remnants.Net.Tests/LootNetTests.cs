using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Net.Tests;

/// <summary>
/// Field loot over the network: a kill's pile and its "dropped" line reach only sides that see the tile (a rival far
/// away learns nothing), allies share the pile, and salvage nodes are known once their tile was seen.
/// </summary>
public class LootNetTests
{
    private static (NetFixture F, TestPlayer A, TestPlayer B, Unit Killer, Unit Husk) Setup()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a, a, b);
        var state = f.Sim.State;
        foreach (var e in state.Units.Where(u => u.Kind is UnitKind.Enemy or UnitKind.Npc or UnitKind.Device)) e.Status = UnitStatus.Dead;
        state.Caches.Clear();
        var killer = state.CurrentUnit!;
        var husk = NewScavenge.EnemyUnit(f.Sim.Data, state, f.Sim.Data.Enemies["husk"], f.FreeNeighbour(killer.Pos));
        state.Units.Add(husk);
        return (f, a, b, killer, husk);
    }

    [Fact]
    public void A_kill_far_from_a_rival_never_reaches_their_client()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var (f, a, b, killer, husk) = Setup();
            f.Sim.State.LootSeed = (ulong)attempt + 1; // a fresh loot roll each attempt
            var bobSide = Unit.PlayerSide(b.Id);
            if (Visibility.SideVisible(f.Sim.Data, f.Sim.State, bobSide).Contains(f.Sim.State.Map.Index(husk.Pos))) continue;
            Assert.True(a.Do(new Lookout(a.Id, killer.Id)).Ok); // Alice sees the husk first
            Combat.ApplyDamage(f.Sim, husk, BodyPart.Torso, 999, attacker: killer);
            if (f.Sim.State.Caches.Count == 0) continue;
            Assert.True(a.Do(new EndTurn(a.Id, killer.Id)).Ok);

            var pile = Assert.Single(a.View.Caches);
            Assert.Equal((LootPileKind.Remains, husk.Name), (pile.Kind, pile.Source));
            Assert.Contains(a.Last<Snapshot>().Events, e => e.Type == ScavengeEventType.LootDropped);
            Assert.Empty(b.View.Caches);
            Assert.DoesNotContain(b.Link.Inbox.OfType<Snapshot>().SelectMany(s => s.Events), e => e.Type == ScavengeEventType.LootDropped);
            f.AssertNoLeaks();
            return;
        }
        Assert.Fail("No kill out of Bob's sight left a pile.");
    }

    [Fact]
    public void Salvage_commands_round_trip_and_nodes_stay_in_the_fog()
    {
        var (f, a, b, killer, _) = Setup();
        var node = FieldSalvage.Add(f.Sim.Data, f.Sim.State, f.Sim.Data.Salvage.Node("bedding")!, f.FreeNeighbour(killer.Pos), new(1));
        Assert.True(a.Do(new Salvage(a.Id, killer.Id, node.Id)).Ok);
        Assert.Contains(a.View.SalvageNodes, n => n.Id == node.Id);
        f.AssertNoLeaks();
    }
}
