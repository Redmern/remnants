using System.Text.Json.Nodes;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Ability cards and saves: the same commands give the same run, hands and effects round-trip, older saves load.</summary>
public class CardSaveTests
{
    /// <summary>A short fight with every kind of board change: shove, smoke, barrier, feint, swap; returns the saved run.</summary>
    /// <param name="skip">The UI's skip-animations setting, as its replay would read the events after each command.</param>
    private static string Script(bool? skip = null)
    {
        var a = SurvivorUnit(1, new(2, 2), weapon: "pipe");
        var f = SurvivorUnit(1, new(1, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(3, 2), weapon: "knife");
        var c = SurvivorUnit(2, new(7, 3), weapon: "knife");
        var sim = Arena(10, 6, [a, f, b, c], seed: 7);
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Hand = ["shove", "barricade", "feint", "smoke_bomb", "switch_places"], Points = 9, Budget = 9,
            HandSize = 5, DrawPile = ["yank", "go_now"] });
        var seen = 0;
        void Ok(IScavengeCommand cmd)
        {
            Assert.True(sim.Execute(cmd).Ok, cmd.ToString());
            if (skip is not { } fast) return;
            // Replay the new events as the screen paces them (sight, holds, walks): a view only, it must change nothing.
            var visible = Visibility.SideVisible(sim.Data, sim.State, a.Side);
            foreach (var e in sim.State.Events.Skip(seen))
                _ = ReplayPacing.Hold(0.4, ReplayPacing.InSight(sim.State, a.Side, e, visible, null, null), fast)
                    + ReplayPacing.WalkSeconds(0.2, 2, false, fast);
            seen = sim.State.Events.Count;
        }
        Ok(new PlayCard(1, a.Id, "shove", b.Id));
        Ok(new PlayCard(1, a.Id, "barricade", Tile: new Pos(3, 3)));
        Ok(new PlayCard(1, a.Id, "feint", b.Id));
        Ok(new PlayCard(1, a.Id, "smoke_bomb", Tile: new Pos(6, 3)));
        Ok(new PlayCard(1, a.Id, "switch_places", f.Id));
        Ok(new EndTurn(1, a.Id));
        for (var i = 0; i < 12 && sim.State.Round < 2; i++)
            Ok(new EndTurn(sim.State.CurrentUnit!.PlayerId!.Value, sim.State.CurrentUnitId!.Value));
        return ScavengeSave.Save(sim.State);
    }

    [Fact]
    public void The_same_commands_give_the_same_run()
    {
        // Core never reads the UI's animation settings: replayed skipped, paced or not at all, the run is the same.
        Assert.Equal(Script(skip: true), Script(skip: false));
        Assert.Equal(Script(), Script(skip: true));
    }

    [Fact]
    public void Hands_effects_and_displacement_survive_a_save()
    {
        var json = Script();
        var loaded = ScavengeSave.Load(WholeTurns, json);
        Assert.Equal(json, ScavengeSave.Save(loaded.State));
        var deck = loaded.State.Decks[0];
        Assert.Equal(7, deck.Hand.Count + deck.DrawPile.Count + deck.Used.Count);
        Assert.Contains("shove", deck.Hand.Concat(deck.DrawPile).Concat(deck.Used));
        Assert.Equal(2, loaded.State.FieldEffects.Count);
    }

    [Fact]
    public void A_save_from_before_ability_cards_loads_with_nothing_on_the_map()
    {
        var node = JsonNode.Parse(Script())!;
        var state = node["scavenge"]!.AsObject();
        state.Remove("fieldEffects");
        foreach (var u in state["units"]!.AsArray()) u!.AsObject().Remove("displaced");
        var loaded = ScavengeSave.Load(WholeTurns, node.ToJsonString());
        Assert.Empty(loaded.State.FieldEffects);
        Assert.Empty(loaded.State.Map.Overlay);
        Assert.Empty(loaded.State.Map.Smoke);
        Assert.All(loaded.State.Units, u => Assert.False(u.Displaced));
    }

    [Theory]
    [InlineData(ScavengeSave.IntentsVersion)]
    [InlineData(ScavengeSave.TileAttacksVersion)]
    public void An_older_save_with_a_pending_plan_loads_and_plays_on_with_the_plan_gone(int version)
    {
        var sim = NewScavenge.Create(WholeTurns, "suburb_ruins", 11, [Party(1)]);
        sim.Start();
        var node = JsonNode.Parse(ScavengeSave.Save(sim.State))!;
        node["version"] = version;
        var unit = node["scavenge"]!["units"]!.AsArray().First(u => u!["id"]!.GetValue<int>() == sim.State.CurrentUnitId)!.AsObject();
        unit["plan"] = new JsonObject
        {
            ["path"] = new JsonArray(),
            ["attackTile"] = new JsonObject { ["x"] = 1, ["y"] = 1 },
            ["actionId"] = "swing",
            ["round"] = 1,
            ["quick"] = true,
        };
        unit["stance"] = "Overwatch";
        unit["reactions"] = 2;

        var loaded = ScavengeSave.Load(WholeTurns, node.ToJsonString());

        var current = loaded.State.CurrentUnit!;
        Assert.Equal(sim.State.CurrentUnitId, current.Id);
        Assert.Empty(loaded.State.Strikes);
        Assert.Null(current.Watch);
        Assert.Empty(Overwatches.Watched(loaded, current));
        Assert.True(loaded.Execute(new EndTurn(1, current.Id)).Ok);
    }

    [Fact]
    public void A_colony_holding_the_old_buff_cards_still_finds_them()
    {
        foreach (var id in new[] { "dash", "sprint", "heavy_swing", "called_shot", "roll_with_it", "hold_the_line", "scout_ahead", "sidestep", "counterstrike" })
            Assert.NotNull(Real.Cards.Find(id));
    }
}
