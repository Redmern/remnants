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
        a.ExtraCards = 4;
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
    }

    [Theory]
    [InlineData(ScavengeSave.IntentsVersion)]
    [InlineData(ScavengeSave.TileAttacksVersion)]
    [InlineData(ScavengeSave.TelegraphedStrikesVersion)]
    public void A_run_from_before_simultaneous_rounds_is_refused(int version)
    {
        var sim = NewScavenge.Create(WholeTurns, "suburb_ruins", 11, [Party(1)]);
        sim.Start();
        var node = JsonNode.Parse(ScavengeSave.Save(sim.State))!;
        node["version"] = version;
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ScavengeSave.Load(WholeTurns, node.ToJsonString()));
    }

    [Theory]
    [InlineData(ScavengeSave.SimultaneousVersion)]
    [InlineData(ScavengeSave.PlanSlotsVersion)]
    public void A_run_from_before_defend_loads_with_its_plan_and_plays_on(int version)
    {
        var sim = NewScavenge.Create(WholeTurns, "suburb_ruins", 11, [Party(1)]);
        sim.Start();
        var unit = sim.State.CurrentUnit!;
        Assert.True(sim.Execute(new Recover(1, unit.Id)).Ok);
        var node = JsonNode.Parse(ScavengeSave.Save(sim.State))!;
        node["version"] = version;
        var loaded = ScavengeSave.Load(WholeTurns, node.ToJsonString());
        var again = loaded.State.FindUnit(unit.Id)!;
        Assert.Equal(PlanStepKind.Recover, Plans.Of(loaded.State, again)!.Steps.Single().Kind);
        Assert.True(loaded.Execute(new ChangePlan(1, again.Id, new Defend(1, again.Id, new Pos(1, 0)))).Ok);
    }

    [Theory]
    [InlineData(PlanStepKind.UseItem)]
    [InlineData(PlanStepKind.Hack)]
    [InlineData(PlanStepKind.DroneFire)]
    public void A_planned_item_hack_or_shot_round_trips(PlanStepKind kind)
    {
        var sim = NewScavenge.Create(WholeTurns, "suburb_ruins", 11, [Party(1)]);
        sim.Start();
        var unit = sim.State.CurrentUnit!;
        Plans.For(sim.State, unit).Steps.Add(new PlanStep { Kind = kind, ItemId = "bandage", DeviceId = 7, TargetId = 3, Tile = new Pos(2, 4), From = unit.Pos });
        var loaded = ScavengeSave.Load(WholeTurns, ScavengeSave.Save(sim.State));
        var step = Plans.Of(loaded.State, loaded.State.FindUnit(unit.Id)!)!.Steps.Single();
        Assert.Equal((kind, "bandage", 7, 3, new Pos(2, 4)), (step.Kind, step.ItemId!, step.DeviceId!.Value, step.TargetId!.Value, step.Tile!.Value));
    }

    [Fact]
    public void A_queued_defend_round_trips_and_this_build_writes_the_new_version()
    {
        var sim = NewScavenge.Create(WholeTurns, "suburb_ruins", 11, [Party(1)]);
        sim.Start();
        var unit = sim.State.CurrentUnit!;
        Assert.True(sim.Execute(new Defend(1, unit.Id, new Pos(0, 1))).Ok);
        Assert.Equal(20, ScavengeSave.Version);
        var loaded = ScavengeSave.Load(WholeTurns, ScavengeSave.Save(sim.State));
        var step = Plans.Of(loaded.State, loaded.State.FindUnit(unit.Id)!)!.Steps.Single();
        Assert.Equal((PlanStepKind.Defend, new Pos(0, 1)), (step.Kind, step.Direction));
    }

    [Fact]
    public void Stun_adrenaline_and_a_queued_plan_round_trip()
    {
        var sim = NewScavenge.Create(WholeTurns, "suburb_ruins", 11, [Party(1)]);
        sim.Start();
        var unit = sim.State.CurrentUnit!;
        unit.Adrenaline = 42;
        unit.StunnedRound = sim.State.Round + 1;
        unit.StunCount = 2;
        Assert.True(sim.Execute(new Recover(1, unit.Id)).Ok);
        var loaded = ScavengeSave.Load(WholeTurns, ScavengeSave.Save(sim.State));
        var again = loaded.State.FindUnit(unit.Id)!;
        Assert.Equal((42.0, unit.StunnedRound, 2), (again.Adrenaline, again.StunnedRound, again.StunCount));
        Assert.Equal(PlanStepKind.Recover, Plans.Of(loaded.State, again)!.Steps.Single().Kind);
    }

    [Fact]
    public void A_colony_holding_the_old_buff_cards_still_finds_them()
    {
        foreach (var id in new[] { "dash", "sprint", "heavy_swing", "called_shot", "roll_with_it", "hold_the_line", "scout_ahead", "sidestep", "counterstrike" })
            Assert.NotNull(Real.Cards.Find(id));
    }
}
