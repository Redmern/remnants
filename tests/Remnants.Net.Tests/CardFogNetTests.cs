using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Net.Tests;

/// <summary>Ability cards over the network: barriers, smoke and a reordered round reach a side only as far as it sees.</summary>
public class CardFogNetTests
{
    /// <summary>Alice and Bob on open floor, far apart; Alice is up.</summary>
    private static (NetFixture F, TestPlayer A, TestPlayer B, Unit Alice, Unit Bob) Field()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a, a, b);
        var state = f.Sim.State;
        foreach (var p in state.Map.AllPositions.ToList()) state.Map[p] = Terrain.Floor;
        foreach (var e in state.Units.Where(u => u.Kind == UnitKind.Enemy || u.Kind == UnitKind.Npc)) e.Status = UnitStatus.Dead;
        var alices = state.Units.Where(u => u.PlayerId == a.Id).ToList();
        var bobs = state.Units.Where(u => u.PlayerId == b.Id).ToList();
        for (var i = 0; i < alices.Count; i++) alices[i].Pos = new(1, 1 + i);
        for (var i = 0; i < bobs.Count; i++) bobs[i].Pos = new(state.Map.Width - 2, state.Map.Height - 2 - i);
        return (f, a, b, state.CurrentUnit!, bobs[0]);
    }

    [Fact]
    public void A_side_gets_the_barriers_and_smoke_it_sees_and_its_own()
    {
        var (f, a, b, alice, bob) = Field();
        var state = f.Sim.State;
        var aliceSide = alice.Side;
        state.FieldEffects.Add(new FieldEffect { Kind = FieldEffectKind.Barrier, Tiles = [new(2, 1)], UntilRound = 99, Side = aliceSide });
        state.FieldEffects.Add(new FieldEffect { Kind = FieldEffectKind.Smoke, Tiles = FieldEffects.SmokeTiles(state.Map, bob.Pos + new Pos(-2, 0), 1),
            UntilRound = 99, Side = aliceSide });
        FieldEffects.Sync(state);

        var bobFog = new SideFog(b.Id, bob.Side);
        bobFog.Update(f.Sim);
        var seen = bobFog.View(f.Sim).FieldEffects;
        Assert.Equal(FieldEffectKind.Smoke, Assert.Single(seen).Kind); // the cloud next to Bob, not the barrier by Alice
        Assert.Equal("", seen[0].Side);

        var aliceFog = new SideFog(a.Id, aliceSide);
        aliceFog.Update(f.Sim);
        Assert.Equal(2, aliceFog.View(f.Sim).FieldEffects.Count);
    }

    [Fact]
    public void A_client_lays_the_effects_it_gets_over_its_map()
    {
        var (f, a, b, alice, bob) = Field();
        var state = f.Sim.State;
        state.FieldEffects.Add(new FieldEffect { Kind = FieldEffectKind.Barrier, Tiles = [bob.Pos + new Pos(-1, 0)], UntilRound = 99, Side = alice.Side });
        FieldEffects.Sync(state);
        f.EndTurnsUntil(() => b.Client.View?.State.FieldEffects.Count == 1, a, b);
        Assert.Equal(Terrain.Cover, b.Client.View!.State.Map[bob.Pos + new Pos(-1, 0)]);
    }

    [Fact]
    public void A_reordered_turn_order_shows_to_a_side_that_sees_the_units()
    {
        var (f, a, b, alice, bob) = Field();
        var state = f.Sim.State;
        var others = state.TurnOrder.Skip(state.TurnIndex + 1).ToList();
        var moved = others.First();
        state.TurnOrder.Remove(moved);
        state.TurnOrder.Add(moved);
        var bobFog = new SideFog(b.Id, bob.Side);
        bobFog.Update(f.Sim);
        var view = bobFog.View(f.Sim);
        var known = view.Units.Select(u => u.Id).ToHashSet();
        Assert.Equal(state.TurnOrder.Where(known.Contains), view.TurnOrder.Where(id => id != SideFog.HiddenUnitId));
    }

    [Fact]
    public void Whether_a_rival_unit_was_shoved_is_not_sent()
    {
        var (f, a, b, alice, bob) = Field();
        bob.Pos = alice.Pos + new Pos(2, 0);
        bob.Displaced = true;
        var aliceFog = new SideFog(a.Id, alice.Side);
        aliceFog.Update(f.Sim);
        Assert.False(Assert.Single(aliceFog.View(f.Sim).Units, u => u.Id == bob.Id).Displaced);
    }
}
