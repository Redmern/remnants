using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Tests;

public class StrikeSelectionTests
{
    [Fact]
    public void A_chosen_strike_mode_is_dropped_when_the_round_advances()
    {
        var pick = new StrikeSelection();
        pick.Toggle(StrikeMode.Stab, round: 3);

        pick.Sync(round: 4);

        Assert.Null(pick.Mode);
    }

    [Fact]
    public void A_chosen_strike_mode_stays_within_its_round()
    {
        var pick = new StrikeSelection();
        pick.Toggle(StrikeMode.Slash, round: 3);

        pick.Sync(round: 3);

        Assert.Equal(StrikeMode.Slash, pick.Mode);
    }

    [Fact]
    public void Choosing_the_same_mode_again_deselects_it_and_another_mode_replaces_it()
    {
        var pick = new StrikeSelection();
        pick.Toggle(StrikeMode.Stab, round: 1);
        pick.Toggle(StrikeMode.Slash, round: 1);
        Assert.Equal(StrikeMode.Slash, pick.Mode);

        pick.Toggle(StrikeMode.Slash, round: 1);
        Assert.Null(pick.Mode);
    }

    [Fact]
    public void A_mode_chosen_in_a_later_round_is_kept_through_that_round()
    {
        var pick = new StrikeSelection();
        pick.Toggle(StrikeMode.Stab, round: 1);
        pick.Sync(round: 2);
        pick.Toggle(StrikeMode.Stab, round: 2);

        pick.Sync(round: 2);

        Assert.Equal(StrikeMode.Stab, pick.Mode);
    }

    [Fact]
    public void Choosing_a_mode_left_over_from_an_earlier_round_picks_it_afresh()
    {
        var pick = new StrikeSelection();
        pick.Toggle(StrikeMode.Stab, round: 1);

        pick.Toggle(StrikeMode.Stab, round: 2);

        Assert.Equal(StrikeMode.Stab, pick.Mode);
    }

    [Fact]
    public void Clear_drops_the_mode()
    {
        var pick = new StrikeSelection();
        pick.Toggle(StrikeMode.Stab, round: 1);

        pick.Clear();

        Assert.Null(pick.Mode);
    }
}
