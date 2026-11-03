using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Weapon tile patterns: reach straight and diagonal, stab (a line) and slash (an arc).</summary>
public class TileAttackPatternTests
{
    private static readonly Pos O = new(5, 5);
    private static readonly Pos East = new(1, 0);
    private static readonly Pos SouthEast = new(1, 1);

    private static ActionProfile Action(string weapon) =>
        ProfileDeriver.Derive(Real, SurvivorUnit(weapon: weapon)).Actions.First(a => a.ItemId == weapon);

    private static List<Pos> Covered(string weapon, StrikeMode mode, Pos dir) =>
        Combat.Covered(new TileMap(12, 12), Action(weapon), mode, O, dir).ToList();

    [Theory]
    [InlineData("pipe", 1, 1)]
    [InlineData("knife", 1, 1)]
    [InlineData("sword", 2, 2)]
    [InlineData("spear", 3, 2)]
    public void A_stab_is_a_line_of_the_weapons_reach_straight_and_diagonal(string weapon, int straight, int diagonal)
    {
        Assert.Equal(Enumerable.Range(1, straight).Select(i => new Pos(O.X + i, O.Y)), Covered(weapon, StrikeMode.Stab, East));
        Assert.Equal(Enumerable.Range(1, diagonal).Select(i => new Pos(O.X + i, O.Y + i)), Covered(weapon, StrikeMode.Stab, SouthEast));
    }

    [Fact]
    public void A_slash_covers_the_front_tile_and_its_two_flanks()
    {
        Assert.Equal([new Pos(6, 5), new Pos(6, 4), new Pos(6, 6)], Covered("sword", StrikeMode.Slash, East));
        Assert.Equal([new Pos(6, 6), new Pos(6, 5), new Pos(5, 6)], Covered("sword", StrikeMode.Slash, SouthEast));
        Assert.Equal([new Pos(5, 4), new Pos(4, 4), new Pos(6, 4)], Covered("knife", StrikeMode.Slash, new Pos(0, -1)));
    }

    [Fact]
    public void The_mode_changes_the_covered_tiles()
    {
        Assert.NotEqual(Covered("sword", StrikeMode.Stab, East), Covered("sword", StrikeMode.Slash, East));
    }

    [Theory]
    [InlineData("pipe", new[] { StrikeMode.Stab, StrikeMode.Slash })]
    [InlineData("knife", new[] { StrikeMode.Stab, StrikeMode.Slash })]
    [InlineData("sword", new[] { StrikeMode.Stab, StrikeMode.Slash })]
    [InlineData("spear", new[] { StrikeMode.Stab })]
    [InlineData("machete", new[] { StrikeMode.Slash })]
    public void Weapons_offer_their_modes(string weapon, StrikeMode[] modes)
    {
        Assert.Equal(modes, Action(weapon).Modes);
    }

    [Fact]
    public void A_mode_the_weapon_lacks_covers_nothing()
    {
        Assert.Empty(Covered("spear", StrikeMode.Slash, East));
    }

    [Fact]
    public void Covered_tiles_stay_on_the_map()
    {
        var map = new TileMap(4, 4);
        Assert.Equal([new Pos(1, 0), new Pos(2, 0), new Pos(3, 0)], Combat.Covered(map, Action("spear"), StrikeMode.Stab, new(0, 0), East));
        Assert.Equal([new Pos(1, 0), new Pos(1, 1)], Combat.Covered(map, Action("knife"), StrikeMode.Slash, new(0, 0), East));
    }

    [Fact]
    public void Knife_and_sword_load_as_weapons()
    {
        foreach (var id in new[] { "knife", "sword" })
        {
            var item = Real.Items[id];
            Assert.Equal(ItemKind.Weapon, item.Kind);
            Assert.NotEmpty(item.Actions);
        }
    }

    [Fact]
    public void Check_reach_follows_the_diagonal_reach()
    {
        var map = new TileMap(12, 12);
        var spear = Action("spear");
        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, O, spear, new(8, 5)));
        Assert.Equal(ScavengeError.None, Combat.CheckReach(Real, map, O, spear, new(7, 7)));
        Assert.Equal(ScavengeError.OutOfRange, Combat.CheckReach(Real, map, O, spear, new(8, 8)));
    }
}
