using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Only movement is telegraphed: a seen hostile's path precisely, a fogged one's as a coarse hint, nothing else at all.</summary>
public class MoveTelegraphTests
{
    private static (ScavengeSim Sim, Unit A, Unit B) Duel(Pos b)
    {
        var a = SurvivorUnit(1, new(2, 2));
        var foe = SurvivorUnit(2, b);
        return (Arena(30, 12, [a, foe], rows: Enumerable.Repeat(new string('.', 30), 12).ToArray()), a, foe);
    }

    [Fact]
    public void A_seen_hostiles_planned_path_shows_while_it_plans_and_goes_with_an_undo()
    {
        var (sim, a, b) = Duel(new(6, 2));
        Assert.Empty(TacticsView.Telegraphs(sim, a.Side));
        Assert.True(sim.Execute(new Move(2, b.Id, new(4, 2))).Ok);
        var t = Assert.Single(TacticsView.Telegraphs(sim, a.Side));
        Assert.Equal((b.Id, new Pos(6, 2), new Pos(4, 2)), (t.UnitId, t.From, t.To));
        Assert.Equal(new Pos(4, 2), t.Path[^1]);
        Assert.Empty(TacticsView.Telegraphs(sim, b.Side)); // not one's own: those are the plan preview
        Assert.True(sim.Execute(new UndoPlan(2, b.Id)).Ok);
        Assert.Empty(TacticsView.Telegraphs(sim, a.Side));
    }

    [Fact]
    public void Chained_legs_are_one_path_and_actions_never_show()
    {
        var (sim, a, b) = Duel(new(8, 2));
        Assert.True(sim.Execute(new Move(2, b.Id, new(7, 2))).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(6, 2))).Ok);
        Assert.True(sim.Execute(new Push(2, b.Id, new Pos(-1, 0))).Ok);
        var t = Assert.Single(TacticsView.Telegraphs(sim, a.Side));
        Assert.Equal(new Pos(6, 2), t.To);
        Assert.Equal(2, t.Path.Count);
        Assert.DoesNotContain(ThreatKind.Strike, TacticsView.Threats(sim, a.Side).Values);
    }

    [Fact]
    public void A_dash_is_not_telegraphed()
    {
        var (sim, a, b) = Duel(new(8, 2));
        Assert.True(sim.Execute(new Dash(2, b.Id, new Pos(-1, 0), 3)).Ok);
        Assert.Empty(TacticsView.Telegraphs(sim, a.Side));
    }

    [Fact]
    public void A_fogged_hostiles_move_is_only_a_coarse_hint_near_the_side()
    {
        var (sim, a, b) = Duel(new(10, 2));
        var fog = sim.Data.Rules.Fog;
        b.Pos = new(a.Pos.X + fog.MoveHintRange - 2, 2);
        sim.State.Reveals.Clear();
        Assert.False(Detection.SideSees(sim.Data, sim.State, a.Side, b), "the test needs the hostile out of sight");
        var to = b.Pos + new Pos(1, 0);
        Assert.True(sim.Execute(new Move(2, b.Id, to)).Ok);
        Assert.Empty(TacticsView.Telegraphs(sim, a.Side));
        var hint = Assert.Single(TacticsView.MoveHints(sim, a.Side));
        Assert.True(hint.Center.Chebyshev(to) <= hint.Radius, $"{hint} misses {to}");
        Assert.Equal(fog.MoveHintCell / 2, hint.Radius);
        // Anywhere in the same cell gives the same hint: the exact tile is not in it.
        Assert.Equal(0, hint.Center.X % fog.MoveHintCell - fog.MoveHintCell / 2);
        Assert.Equal(0, hint.Center.Y % fog.MoveHintCell - fog.MoveHintCell / 2);
    }

    [Fact]
    public void A_fogged_move_far_from_the_side_gives_no_hint()
    {
        var (sim, a, b) = Duel(new(28, 10));
        Assert.False(Detection.SideSees(sim.Data, sim.State, a.Side, b), "the test needs the hostile out of sight");
        Assert.True(sim.Execute(new Move(2, b.Id, new(27, 10))).Ok);
        Assert.Empty(TacticsView.MoveHints(sim, a.Side));
    }
}
