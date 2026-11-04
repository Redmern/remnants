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
    public void A_rival_in_sight_shows_no_tile_it_was_on_before_it_came_into_view()
    {
        var (f, a, b, alice, bob) = Field();
        alice.Pos = bob.Pos + new Pos(-3, 0);
        alice.RoundFrom = alice.Pos + new Pos(-20, 0);
        alice.LastMove = new Pos(20, 0);
        var fog = new SideFog(b.Id, bob.Side);
        fog.Update(f.Sim);
        var seen = fog.View(f.Sim).Units.Single(u => u.Id == alice.Id);
        Assert.Null(seen.RoundFrom);
        Assert.Equal(default, seen.LastMove);
    }

    [Fact]
    public void A_rival_that_walked_in_plain_sight_shows_that_walk()
    {
        var (f, a, b, alice, bob) = Field();
        alice.Pos = bob.Pos + new Pos(-3, 0);
        alice.RoundFrom = alice.Pos + new Pos(1, 0);
        alice.LastMove = new Pos(-1, 0);
        var fog = new SideFog(b.Id, bob.Side);
        fog.Update(f.Sim);
        var seen = fog.View(f.Sim).Units.Single(u => u.Id == alice.Id);
        Assert.Null(seen.RoundFrom);
        Assert.Equal(new Pos(-1, 0), seen.LastMove);
    }

    [Fact]
    public void A_rival_sees_nothing_of_a_planned_item_and_its_owner_does()
    {
        var (f, a, b, alice, bob) = Field();
        alice.Pos = bob.Pos + new Pos(-3, 0);
        alice.Pack.Add("bandage");
        var pack = alice.Pack.Count;
        var fog = new SideFog(b.Id, bob.Side);
        Assert.True(f.Sim.Execute(new UseItem(a.Id, alice.Id, "bandage")).Ok);
        fog.Update(f.Sim);
        var view = fog.View(f.Sim);
        Assert.Empty(view.Plans.Single(p => p.UnitId == alice.Id).Steps);
        Assert.Equal((0, 0), (view.Units.Single(u => u.Id == alice.Id).Ap, view.Units.Single(u => u.Id == alice.Id).Fatigue));
        var own = new SideFog(a.Id, alice.Side);
        own.Update(f.Sim);
        var step = own.View(f.Sim).Plans.Single(p => p.UnitId == alice.Id).Steps.Single();
        Assert.Equal((PlanStepKind.UseItem, "bandage"), (step.Kind, step.ItemId));
        Assert.Equal(pack, alice.Pack.Count);
    }

    [Fact]
    public void A_rival_in_sight_shows_its_moves_and_nothing_of_its_actions_or_what_it_spent()
    {
        var (f, a, b, alice, bob) = Field();
        alice.Pos = bob.Pos + new Pos(-3, 0);
        var (ap, tired) = (alice.Ap, alice.Fatigue);
        var deck = new SquadDeck { PlayerId = a.Id, Hand = ["parry", "shove"], Points = 5, Budget = 5, HandSize = 5 };
        f.Sim.State.Decks.RemoveAll(d => d.PlayerId == a.Id);
        f.Sim.State.Decks.Add(deck);
        var (points, hand) = (deck.Points, deck.Hand.Count);
        var fog = new SideFog(b.Id, bob.Side);
        Assert.True(f.Sim.Execute(new Move(a.Id, alice.Id, alice.Pos + new Pos(-1, 0))).Ok);
        Assert.True(f.Sim.Execute(new Recover(a.Id, alice.Id)).Ok);
        Assert.True(f.Sim.Execute(new PlayCard(a.Id, alice.Id, "parry", Tile: alice.Pos + new Pos(-1, 0))).Ok);
        fog.Update(f.Sim);
        var view = fog.View(f.Sim);
        var plan = view.Plans.Single(p => p.UnitId == alice.Id);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(PlanStepKind.Move, step.Kind);
        Assert.Equal((0, 0, 0), (step.RefundMp, step.RefundAp, step.RefundFatigue));
        var seen = view.Units.Single(u => u.Id == alice.Id);
        Assert.Equal((0, 0, 0, 0.0), (seen.Ap, seen.Mp, seen.Fatigue, seen.PriorityBonus));
        Assert.Equal((0, 0, 0, 0, 0), (seen.CardsPlayed, seen.ExtraMoves, seen.ExtraActions, seen.ExtraCards, seen.FreeActions));
        var shown = view.Decks.Single(d => d.PlayerId == a.Id);
        Assert.Equal((points, hand), (shown.Points, shown.HandCount));
        // The plan's owner sees it all.
        var own = new SideFog(a.Id, alice.Side);
        own.Update(f.Sim);
        var mine = own.View(f.Sim);
        Assert.Equal(3, mine.Plans.Single(p => p.UnitId == alice.Id).Steps.Count);
        Assert.True(mine.Units.Single(u => u.Id == alice.Id).Ap < ap);
        Assert.True(mine.Units.Single(u => u.Id == alice.Id).Fatigue > tired);
    }

    [Fact]
    public void A_rival_sees_nothing_of_a_defend_until_it_resolves_and_its_owner_sees_it_all()
    {
        var (f, a, b, alice, bob) = Field();
        alice.Pos = bob.Pos + new Pos(-3, 0);
        Assert.True(f.Sim.Execute(new Defend(a.Id, alice.Id, new Pos(1, 0))).Ok);
        var fog = new SideFog(b.Id, bob.Side);
        fog.Update(f.Sim);
        var view = fog.View(f.Sim);
        Assert.Empty(view.Plans.Single(p => p.UnitId == alice.Id).Steps);
        Assert.Empty(view.Defences);
        var seen = view.Units.Single(u => u.Id == alice.Id);
        Assert.Equal((0, 0), (seen.Ap, seen.Fatigue));
        var own = new SideFog(a.Id, alice.Side);
        own.Update(f.Sim);
        Assert.Equal(PlanStepKind.Defend, own.View(f.Sim).Plans.Single(p => p.UnitId == alice.Id).Steps.Single().Kind);
    }

    [Fact]
    public void A_fogged_pusher_breaking_a_guard_is_not_named_to_the_defender()
    {
        var mine = new[] { 1 };
        var events = new[] { new ScavengeEvent(1, ScavengeEventType.GuardBroken, "Bob's push breaks Alice's guard.", 2, 1, new Pos(3, 3), Anonymous: "Alice's guard is broken by a push.") };
        var heard = FogLog.Filter(events, "p1", id => mine.Contains(id), _ => false, _ => false).Single();
        Assert.Null(heard.UnitId);
        Assert.DoesNotContain("Bob", heard.Message);
    }

    [Fact]
    public void A_fogged_rivals_move_reaches_a_side_only_as_a_coarse_hint()
    {
        var (f, a, b, alice, bob) = Field();
        var rules = f.Sim.Data.Rules.Fog;
        alice.Pos = new(bob.Pos.X - rules.MoveHintRange + 2, bob.Pos.Y);
        var fog = new SideFog(b.Id, bob.Side);
        fog.Update(f.Sim);
        Assert.False(Detection.SideSees(f.Sim.Data, f.Sim.State, bob.Side, alice), "the test needs Alice out of Bob's sight");
        var to = alice.Pos + new Pos(1, 0);
        Assert.True(f.Sim.Execute(new Move(a.Id, alice.Id, to)).Ok);
        fog.Update(f.Sim);
        var view = fog.View(f.Sim);
        Assert.DoesNotContain(view.Plans, p => p.UnitId == alice.Id);
        Assert.DoesNotContain(view.Units, u => u.Id == alice.Id);
        var hint = Assert.Single(view.MoveHints);
        Assert.True(hint.Center.Chebyshev(to) <= hint.Radius);
    }

    [Fact]
    public void Adrenaline_and_a_stun_show_on_a_rival_in_sight_and_not_on_one_out_of_it()
    {
        var (f, _, b, alice, bob) = Field();
        alice.Adrenaline = 60;
        alice.StunnedRound = f.Sim.State.Round + 1;
        var fog = new SideFog(b.Id, bob.Side);
        fog.Update(f.Sim);
        Assert.DoesNotContain(fog.View(f.Sim).Units, u => u.Id == alice.Id && u.Adrenaline > 0);

        alice.Pos = bob.Pos + new Pos(-1, 0);
        fog.Update(f.Sim);
        var seen = fog.View(f.Sim).Units.Single(u => u.Id == alice.Id);
        Assert.Equal(60, seen.Adrenaline);
        Assert.True(Stuns.Active(f.Sim.State, seen));
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
        Assert.Equal(0, seen[0].UntilRound); // how long it lasts is not shown
        var bobSees = bobFog.Visible;
        Assert.All(seen[0].Tiles, p => Assert.Contains(Pos.Neighbours8.Append(default), d => bobSees.Contains(state.Map.Index(p + d))));
        Assert.True(seen[0].Tiles.Count < 9); // the far side of the cloud is not sent

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

}
