using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Net.Tests;

/// <summary>
/// Multiplayer fog of war: a hidden attacker stays hidden from the victim's client (who gets the hit, an "ambushed
/// from the fog" line and a contact pointing back, not who or where), and contacts, sensors and reveals stay per side.
/// </summary>
public class FogNetTests
{
    /// <summary>
    /// Alice's sniper, cloaked, with binoculars and a rifle, 9 tiles from Bob's short-sighted pair on open ground: Alice
    /// sees Bob; Bob (sight ~5, a muzzle flash ~8) cannot see her even when she fires. The map is flattened and the AI
    /// removed so only the duel matters.
    /// </summary>
    private static (NetFixture F, TestPlayer A, TestPlayer B, Unit Sniper, Unit Victim) Duel()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var a = f.Join("Alice");
        var bobs = NetFixture.Party("B");
        foreach (var m in bobs) m.Survivor.Set(AttributeKind.Perception, 20);
        var b = f.Join("Bob", bobs);
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a, a, b);
        var state = f.Sim.State;
        foreach (var p in state.Map.AllPositions.ToList()) state.Map[p] = Terrain.Floor;
        foreach (var e in state.Units.Where(u => u.Kind == UnitKind.Enemy || u.Kind == UnitKind.Npc)) e.Status = UnitStatus.Dead;

        var sniper = state.CurrentUnit!;
        var spotter = state.Units.First(u => u.PlayerId == a.Id && u.Id != sniper.Id);
        var pair = state.Units.Where(u => u.PlayerId == b.Id).ToList();
        sniper.Pos = new(11, 10);
        sniper.Equipment[EquipSlot.Weapon] = "rifle";
        sniper.Equipment[EquipSlot.Body] = "camo_cloak";
        sniper.Equipment[EquipSlot.Tool] = "binoculars";
        sniper.Ap = f.Sim.Profile(sniper).MaxAp;
        spotter.Pos = new(state.Map.Width - 1, state.Map.Height - 1);
        pair[0].Pos = new(2, 10);
        pair[1].Pos = new(2, 11);
        return (f, a, b, sniper, pair[0]);
    }

    [Fact]
    public void A_hidden_attacker_is_never_named_or_placed_for_the_victims_client()
    {
        var (f, a, b, sniper, victim) = Duel();
        var bobSide = Unit.PlayerSide(b.Id);
        Assert.False(Detection.SideSees(f.Sim.Data, f.Sim.State, bobSide, sniper));
        Assert.True(Detection.SideSees(f.Sim.Data, f.Sim.State, Unit.PlayerSide(a.Id), victim));
        Assert.True(Combat.AmbushAgainst(f.Sim.Data, f.Sim.State, sniper, victim) is { HitBonus: > 0 });

        var result = a.Do(new Attack(a.Id, sniper.Id, victim.Id, "aimed_shot"));
        Assert.True(result.Ok, result.Message);

        var bobHeard = b.Last<Snapshot>().Events;
        Assert.Contains(bobHeard, e => e.Type == ScavengeEventType.Ambushed && e.UnitId is null && e.TargetId == victim.Id
                                      && e.Message.Contains("ambushed from the fog"));
        Assert.Contains(bobHeard, e => e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed && e.UnitId is null);
        Assert.DoesNotContain(bobHeard, e => e.UnitId == sniper.Id || e.Message.Contains(sniper.Name));
        Assert.DoesNotContain(b.View.Units, u => u.Id == sniper.Id);

        // Bob knows roughly where it came from, and nothing more.
        var contact = Assert.Single(b.View.Contacts, c => c.Kind == ContactKind.Attack);
        Assert.Equal(victim.Pos, contact.Toward);
        Assert.True(contact.Pos.Chebyshev(sniper.Pos) <= f.Sim.Data.Rules.Fog.ContactFuzz);
        Assert.Empty(b.View.Reveals);

        // Alice saw her own shot, named.
        Assert.Contains(a.Last<Snapshot>().Events, e => e.Type == ScavengeEventType.Ambushed && e.UnitId == sniper.Id);
        Assert.DoesNotContain(a.View.Contacts, c => c.Side == bobSide);
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_revealed_attacker_within_sight_shows_up_for_the_victim()
    {
        var (f, a, b, sniper, victim) = Duel();
        sniper.Pos = new(7, 10); // inside a muzzle flash's range of Bob, still hidden by the cloak
        Assert.False(Detection.SideSees(f.Sim.Data, f.Sim.State, Unit.PlayerSide(b.Id), sniper));

        Assert.True(a.Do(new Attack(a.Id, sniper.Id, victim.Id, "aimed_shot")).Ok);

        Assert.Contains(b.View.Units, u => u.Id == sniper.Id); // muzzle flash
        Assert.Contains(b.View.Reveals, r => r.UnitId == sniper.Id);
        f.AssertNoLeaks();
    }

    [Fact]
    public void Sensors_contacts_and_stances_stay_with_their_side_over_the_wire()
    {
        var (f, a, b, sniper, victim) = Duel();
        sniper.Pack.Add("motion_sensor");
        Assert.True(a.Do(new UseItem(a.Id, sniper.Id, "motion_sensor", At: sniper.Pos + new Pos(-1, 0))).Ok);
        Assert.Single(a.View.Sensors);
        Assert.Empty(b.View.Sensors);
        Assert.DoesNotContain(b.Last<Snapshot>().Events, e => e.Type == ScavengeEventType.SensorPlaced);

        Assert.True(a.Do(new SetStance(a.Id, sniper.Id, Stance.Overwatch)).Ok);
        Assert.Equal(Stance.Overwatch, a.View.Units.Single(u => u.Id == sniper.Id).Stance);

        // Bob walks into the sensor's radius: Alice's sensor reveals him, the overwatch may fire; Bob only hears about
        // what was done to him.
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == b, a, b);
        var mover = f.Sim.State.CurrentUnit!;
        mover.Pos = new(6, 10);
        f.Sim.State.FindUnit(mover.Id)!.Ap = f.Sim.Profile(mover).MaxAp;
        var to = f.Sim.Reachable(mover).Keys.Where(p => p.Chebyshev(new Pos(10, 10)) <= 3).OrderBy(p => p.X).First();
        Assert.True(b.Do(new Move(b.Id, mover.Id, to)).Ok);

        Assert.Contains(a.Last<Snapshot>().Events, e => e.Type == ScavengeEventType.SensorTripped);
        Assert.DoesNotContain(b.Link.Inbox.OfType<Snapshot>().SelectMany(s => s.Events), e => e.Type is ScavengeEventType.SensorTripped or ScavengeEventType.SensorPlaced);
        Assert.Empty(b.View.Sensors);
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_hidden_units_plan_never_reaches_the_other_side()
    {
        var (f, a, b, sniper, victim) = Duel();
        Assert.False(Detection.SideSees(f.Sim.Data, f.Sim.State, Unit.PlayerSide(b.Id), sniper));
        var result = a.Do(new DeclareIntent(a.Id, sniper.Id, [], victim.Pos, "aimed_shot"));
        Assert.True(result.Ok, result.Message);
        Assert.NotNull(sniper.Plan);

        Assert.DoesNotContain(b.View.Units, u => u.Id == sniper.Id);
        Assert.DoesNotContain(b.Last<Snapshot>().Events, e => e.Type == ScavengeEventType.IntentDeclared);
        // Alice sees her own plan, aimed at the one she saw on the tile.
        var mine = Assert.Single(a.View.Units, u => u.Id == sniper.Id);
        Assert.Equal(victim.Pos, mine.Plan?.AttackTile);
        Assert.Contains(a.Last<Snapshot>().Events, e => e.Type == ScavengeEventType.IntentDeclared && e.UnitId == sniper.Id);
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_seen_units_plan_shows_its_path_and_tile_but_never_its_card_or_target()
    {
        var (f, a, b, sniper, victim) = Duel();
        victim.Plan = new PlannedAction { Path = [new(3, 10)], AttackTile = new(4, 10), ActionId = "swing", TargetId = sniper.Id, CardId = "secret_card", Round = 1 };
        victim.OutplayedId = sniper.Id;
        sniper.Plan = new PlannedAction { Path = [new(10, 10)], AttackTile = new(3, 10), ActionId = "aimed_shot", TargetId = victim.Id, CardId = "hidden_card", Round = 1 };

        var alice = new SideFog(a.Id, Unit.PlayerSide(a.Id));
        alice.Update(f.Sim);
        var seen = Assert.Single(alice.View(f.Sim).Units, u => u.Id == victim.Id);
        var plan = Assert.IsType<PlannedAction>(seen.Plan);
        Assert.Equal([new Pos(3, 10)], plan.Path);
        Assert.Equal(new Pos(4, 10), plan.AttackTile);
        Assert.Null(plan.CardId);
        Assert.Null(plan.TargetId);
        Assert.Null(seen.OutplayedId);
        // Her own unit keeps everything.
        Assert.Equal("hidden_card", Assert.Single(alice.View(f.Sim).Units, u => u.Id == sniper.Id).Plan?.CardId);

        var bob = new SideFog(b.Id, Unit.PlayerSide(b.Id));
        bob.Update(f.Sim);
        var json = ProtocolJson.Serialize(new Snapshot(bob.View(f.Sim), [], []));
        Assert.DoesNotContain("hidden_card", json);
        Assert.DoesNotContain(bob.View(f.Sim).Units, u => u.Id == sniper.Id);
        // Bob's own unit's plan keeps its card in his own view.
        Assert.Contains("secret_card", ProtocolJson.Serialize(new Snapshot(bob.View(f.Sim), [], [])));
    }
}
