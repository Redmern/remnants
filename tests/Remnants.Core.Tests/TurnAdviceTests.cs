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
    public void A_unit_out_of_ap_cannot()
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var sim = Arena(6, 5, [a, SurvivorUnit(2, new(5, 4))]);
        a.Ap = 0;
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
                    Overwatch = r.Fog.Overwatch with { ApCost = 5 }, Brace = r.Fog.Brace with { ApCost = 5 }, Lookout = r.Fog.Lookout with { ApCost = 5 },
                },
            }));
        a.Ap = 1;
        Assert.False(TurnAdvice.CanStillAct(sim, a));
        a.Ap = 5;
        Assert.True(TurnAdvice.CanStillAct(sim, a), "a stance is affordable again");
    }
}
