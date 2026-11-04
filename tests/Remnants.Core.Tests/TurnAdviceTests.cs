using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Whether a unit whose turn it is has anything affordable left to do (else the UI may end its turn for it).</summary>
public class TurnAdviceTests
{
    [Fact]
    public void A_unit_with_ap_and_room_to_move_can_still_act()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var sim = Arena(6, 5, [a, SurvivorUnit(2, new(5, 4))]);
        Assert.True(TurnAdvice.CanStillAct(sim, a));
    }

    [Fact]
    public void A_unit_out_of_ap_with_nobody_to_strike_cannot()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var sim = Arena(16, 5, [a, SurvivorUnit(2, new(15, 4))]);
        (a.Ap, a.Mp) = (0, 0);
        Assert.False(TurnAdvice.CanStillAct(sim, a));
    }

    [Fact]
    public void A_downed_unit_cannot()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var sim = Arena(6, 5, [a, SurvivorUnit(2, new(5, 4))]);
        a.Status = UnitStatus.Downed;
        Assert.False(TurnAdvice.CanStillAct(sim, a));
    }

    /// <summary>Every stance and lookout dearer than a turn's AP, so only what a test adds can be affordable.</summary>
    private static readonly ScavengeData DearStances = With(r => r with
    {
        Fog = r.Fog with
        {
            Brace = r.Fog.Brace with { ApCost = 5 },
            Lookout = r.Fog.Lookout with { ApCost = 5 },
        },
    });

    [Fact]
    public void A_unit_whose_ap_buys_nothing_cannot()
    {
        // Boxed in by walls with nobody in sight, and every order dearer than the AP left.
        var a = SurvivorUnit(1, new(1, 1), weapon: "pipe");
        var sim = Arena(5, 3, [a, SurvivorUnit(2, new(4, 1))], ["###..", "#.#..", "###.."],
            data: With(r => r with
            {
                Fog = r.Fog with
                {
                    Brace = r.Fog.Brace with { ApCost = 5 },
                    Lookout = r.Fog.Lookout with { ApCost = 5 },
                },
            }));
        a.Ap = 1;
        Assert.False(TurnAdvice.CanStillAct(sim, a));
        a.Ap = 5;
        Assert.True(TurnAdvice.CanStillAct(sim, a), "a stance is affordable again");
    }

    /// <summary>A P1 survivor walled in at (1,1), out of AP: nothing to do unless something below gives it an order.</summary>
    private static (ScavengeSim Sim, Unit A) Stuck(Action<ScavengeData, ScavengeState>? setup = null)
    {
        var a = SurvivorUnit(1, new(1, 1), weapon: "pipe", attributes: 70);
        var sim = Arena(5, 3, [a, SurvivorUnit(2, new(4, 1))], ["###..", "#E#..", "###.."], setup: setup, data: DearStances);
        a.Ap = 0;
        return (sim, a);
    }

    [Fact]
    public void A_unit_on_an_exit_can_still_extract()
    {
        var (sim, a) = Stuck();
        Assert.True(sim.State.Map.IsExit(a.Pos));
        Assert.True(TurnAdvice.CanStillAct(sim, a));
        sim.State.Map.Exits.Clear();
        Assert.False(TurnAdvice.CanStillAct(sim, a));
    }

    [Fact]
    public void A_card_it_can_play_for_no_ap_counts()
    {
        var (sim, a) = Stuck();
        sim.State.Map.Exits.Clear();
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Hand = ["dash"], Points = 3, Budget = 3, HandSize = 3 });
        Assert.True(TurnAdvice.CanStillAct(sim, a));
        sim.State.Decks[0].Points = 0;
        Assert.False(TurnAdvice.CanStillAct(sim, a), "no card points left");
    }

    [Fact]
    public void A_pile_in_reach_counts_while_there_is_ap_to_pick_up()
    {
        var (sim, a) = Stuck();
        sim.State.Map.Exits.Clear();
        FieldLoot.Pile(sim.State, a.Pos, LootPileKind.Remains, "test").Items.Add("scrap");
        Assert.False(TurnAdvice.CanStillAct(sim, a));
        a.Ap = sim.Data.Rules.Turn.PickUpApCost;
        Assert.True(TurnAdvice.CanStillAct(sim, a));
    }

    [Fact]
    public void A_seen_hostile_gives_a_unit_with_no_ap_nothing_left_to_do()
    {
        var a = SurvivorUnit(1, new(1, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new(2, 2));
        var sim = Arena(8, 5, [a, b], data: DearStances);
        Assert.True(TurnAdvice.CanStillAct(sim, a));
        (a.Ap, a.Mp) = (0, 0);
        Assert.False(TurnAdvice.CanStillAct(sim, a), "a blow is paid when it is readied");
    }

    [Fact]
    public void A_device_it_can_hack_counts()
    {
        ZoneDevice terminal = null!;
        var (sim, a) = Stuck((d, s) => terminal = Hacking.Place(d, s, DeviceKind.Terminal, new Pos(2, 1)));
        sim.State.Map.Exits.Clear();
        Assert.False(TurnAdvice.CanStillAct(sim, a), "no tool");
        a.Pack.Add("hacking_kit");
        Assert.False(TurnAdvice.CanStillAct(sim, a), "no AP");
        a.Ap = Hacking.ApCost(sim.Data, a);
        Assert.True(TurnAdvice.CanStillAct(sim, a));
    }

    [Fact]
    public void A_node_it_can_salvage_counts()
    {
        var (sim, a) = Stuck((d, s) => FieldSalvage.Add(d, s, d.Salvage.Node("rubble")!, new Pos(2, 1), new Random.DeterministicRng(1)));
        sim.State.Map.Exits.Clear();
        Assert.False(TurnAdvice.CanStillAct(sim, a));
        a.Ap = sim.Data.Salvage.Node("rubble")!.ApCost;
        Assert.True(TurnAdvice.CanStillAct(sim, a));
    }

    [Fact]
    public void A_drone_it_can_deploy_or_fly_counts()
    {
        var a = SurvivorUnit(1, new(1, 1), pack: ["scout_drone"]);
        var sim = Arena(6, 3, [a, SurvivorUnit(2, new(5, 1))], data: DearStances);
        a.Ap = sim.Data.Rules.Drone.DeployApCost;
        Assert.True(TurnAdvice.CanUseDrone(sim, a));
        a.Ap = 0;
        Assert.False(TurnAdvice.CanUseDrone(sim, a));
        a.Ap = sim.Data.Rules.Drone.DeployApCost;
        a.Pack.Clear();
        Assert.False(TurnAdvice.CanUseDrone(sim, a));
    }
}
