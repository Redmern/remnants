using System.Text.Json;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>What the resolve playback shows: outcomes, pattern tiles, and the steps it pauses between (AC-5 to AC-7).</summary>
public class ReplayFeedbackTests
{
    private static (ScavengeSim Sim, Unit A, Unit B) Duel(Pos a, Pos b, ulong seed = 1, string[]? rows = null)
    {
        var ua = SurvivorUnit(1, a, weapon: "pipe");
        var ub = SurvivorUnit(2, b, weapon: "pipe");
        return (Arena(12, 6, [ua, ub], rows, seed, data: Real), ua, ub);
    }

    private static List<ScavengeEvent> Round(ScavengeSim sim, params Unit[] units)
    {
        var before = sim.State.Events.Count;
        foreach (var u in units) Assert.True(sim.Execute(new EndTurn(u.PlayerId!.Value, u.Id)).Ok);
        return sim.State.Events.Skip(before).ToList();
    }

    [Fact]
    public void A_strike_on_nobody_whiffs_and_its_tiles_flash_as_failed()
    {
        var (sim, a, b) = Duel(new(1, 2), new(8, 2));
        var tile = new Pos(2, 2);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, tile, "swing")).Ok);
        var planned = Plans.Of(sim.State, a)!.Steps.Single().Strike!.Tiles.ToList();
        var events = Round(sim, a, b);
        var flash = events.Single(e => e.Type == ScavengeEventType.PatternResolved);
        Assert.Equal(planned, flash.Tiles);
        Assert.False(flash.Landed);
        var whiff = ReplayFeedback.Of(events.Single(e => e.Type == ScavengeEventType.IntentFizzled))!;
        Assert.Equal((OutcomeKind.Whiff, "Whiff"), (whiff.Kind, whiff.Text));
    }

    [Fact]
    public void A_strike_flashes_as_landed_only_when_a_blow_lands()
    {
        var (hits, misses) = (0, 0);
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var (sim, a, b) = Duel(new(1, 2), new(2, 2), seed);
            Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
            var events = Round(sim, a, b);
            var landed = events.Any(e => e.Type == ScavengeEventType.Attacked && e.UnitId == a.Id);
            Assert.Equal(landed, events.Single(e => e.Type == ScavengeEventType.PatternResolved).Landed);
            if (landed)
            {
                hits++;
                var hit = events.First(e => e.Type == ScavengeEventType.Attacked);
                var outcome = ReplayFeedback.Of(hit)!;
                Assert.Equal((OutcomeKind.Hit, $"-{hit.Amount:0}"), (outcome.Kind, outcome.Text));
            }
            else
            {
                misses++;
                Assert.Equal(OutcomeKind.Whiff, ReplayFeedback.Of(events.First(e => e.Type == ScavengeEventType.Missed))!.Kind);
            }
        }
        Assert.True(hits > 0 && misses > 0, $"hits {hits}, misses {misses}");
    }

    [Fact]
    public void A_push_flashes_its_tiles_and_a_wall_makes_it_a_collision()
    {
        var (sim, a, b) = Duel(new(9, 2), new(10, 2));
        var way = new Pos(1, 0);
        var planned = Pushes.Tiles(sim, a, way);
        Assert.True(sim.Execute(new Push(1, a.Id, way)).Ok);
        var events = Round(sim, a, b);
        var flash = events.Single(e => e.Type == ScavengeEventType.PatternResolved);
        Assert.Equal(planned, flash.Tiles);
        Assert.True(flash.Landed);
        Assert.Equal(OutcomeKind.Push, ReplayFeedback.Of(events.First(e => e.Type == ScavengeEventType.Displaced))!.Kind);

        var (walled, c, d) = Duel(new(10, 2), new(11, 2));
        Assert.True(walled.Execute(new Push(1, c.Id, way)).Ok);
        Assert.Equal(OutcomeKind.Collision, ReplayFeedback.Of(Round(walled, c, d).First(e => e.Type == ScavengeEventType.Collided))!.Kind);
    }

    [Fact]
    public void A_push_on_nobody_flashes_as_failed()
    {
        var (sim, a, b) = Duel(new(1, 2), new(8, 2));
        Assert.True(sim.Execute(new Push(1, a.Id, new Pos(1, 0))).Ok);
        Assert.False(Round(sim, a, b).Single(e => e.Type == ScavengeEventType.PatternResolved).Landed);
    }

    [Fact]
    public void A_dash_shows_as_a_dash_and_flashes_the_tiles_it_ran()
    {
        var (sim, a, b) = Duel(new(1, 2), new(8, 2));
        Assert.True(sim.Execute(new Dash(1, a.Id, new Pos(1, 0), 2)).Ok);
        var events = Round(sim, a, b);
        Assert.Equal(OutcomeKind.Dash, ReplayFeedback.Of(events.Single(e => e.Type == ScavengeEventType.Moved))!.Kind);
        var flash = events.Single(e => e.Type == ScavengeEventType.PatternResolved);
        Assert.True(flash.Landed);
        Assert.Equal([new Pos(2, 2), new Pos(3, 2)], flash.Tiles);
    }

    [Fact]
    public void A_walk_is_not_a_dash()
    {
        var e = new ScavengeEvent(1, ScavengeEventType.Moved, "Ana moved (1, 2) -> (2, 2).", 1, Pos: new(2, 2));
        Assert.Null(ReplayFeedback.Of(e));
    }

    [Fact]
    public void A_recover_has_no_pattern_to_flash()
    {
        var (sim, a, b) = Duel(new(1, 2), new(8, 2));
        Assert.True(sim.Execute(new Recover(1, a.Id)).Ok);
        Assert.DoesNotContain(Round(sim, a, b), e => e.Type == ScavengeEventType.PatternResolved);
    }

    [Fact]
    public void A_stun_shows_on_its_victim()
    {
        var e = new ScavengeEvent(1, ScavengeEventType.Stunned, "B is stunned (the block).", 1, 2, new(2, 2));
        var outcome = ReplayFeedback.Of(e)!;
        Assert.Equal((OutcomeKind.Stun, "STUNNED"), (outcome.Kind, outcome.Text));
        Assert.Equal(2, ReplayFeedback.VictimOf(e));
        Assert.Equal(1, ReplayFeedback.VictimOf(e with { TargetId = null }));
    }

    [Fact]
    public void Adrenaline_gains_and_drains_are_logged_for_the_meter()
    {
        var (sim, a, b) = Duel(new(9, 2), new(10, 2));
        var was = a.Adrenaline;
        Assert.True(sim.Execute(new Push(1, a.Id, new Pos(1, 0))).Ok);
        var events = Round(sim, a, b);
        var gain = events.First(e => e.Type == ScavengeEventType.AdrenalineChanged && e.UnitId == a.Id);
        Assert.Equal(sim.Data.Rules.Adrenaline.GainPush, gain.Amount);
        var outcome = ReplayFeedback.Of(gain)!;
        Assert.Equal((OutcomeKind.AdrenalineGain, $"+{gain.Amount:0} ADR"), (outcome.Kind, outcome.Text));
        Assert.Equal(OutcomeKind.AdrenalineDrain, ReplayFeedback.Of(gain with { Amount = -5 })!.Kind);
        Assert.Equal("-5 ADR", ReplayFeedback.Of(gain with { Amount = -5 })!.Text);
        Assert.Equal(0, was);
    }

    [Fact]
    public void A_drain_with_nothing_to_lose_logs_nothing()
    {
        var (sim, a, b) = Duel(new(1, 2), new(8, 2));
        Assert.Equal(0, a.Adrenaline);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new Pos(2, 2), "swing")).Ok);
        Assert.DoesNotContain(Round(sim, a, b), e => e.Type == ScavengeEventType.AdrenalineChanged && e.UnitId == a.Id);
    }

    [Fact]
    public void A_unit_with_one_action_of_stamina_left_is_low()
    {
        var (sim, a, _) = Duel(new(1, 2), new(8, 2));
        var max = sim.Profile(a).MaxStamina;
        Assert.False(ReplayFeedback.LowStamina(sim, a));
        a.Fatigue = max - sim.Data.Rules.Turn.StaminaPerAp;
        Assert.True(ReplayFeedback.LowStamina(sim, a));
        a.Fatigue = max - sim.Data.Rules.Turn.StaminaPerAp - 1;
        Assert.False(ReplayFeedback.LowStamina(sim, a));
    }

    [Fact]
    public void The_steps_go_through_each_walk_then_each_action_in_priority_order()
    {
        var a = SurvivorUnit(1, new(1, 1), weapon: "pipe");
        var c = SurvivorUnit(1, new(1, 4), weapon: "pipe");
        var b = SurvivorUnit(2, new(9, 2), weapon: "pipe");
        var sim = Arena(12, 6, [a, c, b], data: Real);
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 0))).Ok);
        Assert.True(sim.Execute(new Move(1, c.Id, new Pos(1, 5))).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new Pos(2, 0), "swing")).Ok);
        Assert.True(sim.Execute(new Recover(1, c.Id)).Ok);
        var moveOrder = new[] { a, c }.OrderByDescending(u => Priority.Movement(sim, u)).ThenBy(u => Priority.TieBreak(sim.State.Round, u.Id)).Select(u => u.Id).ToList();
        var events = Round(sim, a, c, b);

        var steps = ReplayPacing.StepStarts(events).Select(i => events[i]).ToList();
        Assert.Equal(moveOrder, steps.Take(2).Select(e => e.UnitId!.Value));
        Assert.All(steps.Take(2), e => Assert.Equal(ScavengeEventType.Moved, e.Type));
        Assert.Equal(2, steps.Skip(2).Count(e => e.UnitId == a.Id || e.UnitId == c.Id));
        Assert.DoesNotContain(steps.Skip(2), e => e.Type == ScavengeEventType.Moved);
    }

    [Fact]
    public void One_action_with_many_events_is_one_step_and_the_next_units_is_another()
    {
        ScavengeEvent E(ScavengeEventType type, int unit) => new(1, type, "x", unit);
        List<ScavengeEvent> events =
        [
            E(ScavengeEventType.Attacked, 1), E(ScavengeEventType.AdrenalineChanged, 1), E(ScavengeEventType.Missed, 1),
            E(ScavengeEventType.PatternResolved, 1), E(ScavengeEventType.Attacked, 1), E(ScavengeEventType.PatternResolved, 1),
            E(ScavengeEventType.Recovered, 2),
        ];
        Assert.Equal([0, 4, 6], ReplayPacing.StepStarts(events));
    }

    [Fact]
    public void A_command_in_planning_is_no_step()
    {
        List<ScavengeEvent> played = [new(1, ScavengeEventType.CardPlayed, "x", 1), new(1, ScavengeEventType.AdrenalineChanged, "", 1)];
        Assert.Empty(ReplayPacing.StepStarts(played));
    }

    [Fact]
    public void Every_outcome_has_its_own_sound_in_the_sound_map()
    {
        var path = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(path, "Remnants.sln"))) path = Directory.GetParent(path)!.FullName;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "game/audio/sound_events.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var events = json.RootElement.GetProperty("events");
        List<ScavengeEvent> all =
        [
            new(1, ScavengeEventType.Attacked, "hit", 1, 2, Amount: 3),
            new(1, ScavengeEventType.Missed, "miss", 1, 2),
            new(1, ScavengeEventType.Blocked, "A's block takes 50% off the hit on B.", 1, 2),
            new(1, ScavengeEventType.Blocked, "A's perfect block takes 50% off the hit on B.", 1, 2),
            new(1, ScavengeEventType.Stunned, "s", 1, 2),
            new(1, ScavengeEventType.Displaced, "d", 1, 2),
            new(1, ScavengeEventType.Collided, "c", 1, 2),
            new(1, ScavengeEventType.Moved, "A dashes to (2, 2).", 1),
            new(1, ScavengeEventType.AdrenalineChanged, "", 1, Amount: 5),
            new(1, ScavengeEventType.AdrenalineChanged, "", 1, Amount: -5),
        ];
        var sounds = all.Select(e => ReplayFeedback.Of(e)!.Sound).Append(ReplayFeedback.LowStaminaSound).ToList();
        Assert.Equal(sounds.Count, sounds.Distinct().Count());
        // Hit sounds are by victim: scavenge.hit.{who} falls back to scavenge.hit.
        Assert.All(sounds, s => Assert.True(events.TryGetProperty(s, out _), s));
    }

    [Fact]
    public void A_perfect_block_is_told_apart_unseen_too()
    {
        var seen = new ScavengeEvent(1, ScavengeEventType.Blocked, "A's perfect block takes 50% off the hit on B.", 1, 2);
        Assert.Equal((OutcomeKind.PerfectBlock, "PERFECT BLOCK!"), (ReplayFeedback.Of(seen)!.Kind, ReplayFeedback.Of(seen)!.Text));
        Assert.Equal(OutcomeKind.Block, ReplayFeedback.Of(seen with { Message = "A's block takes 50% off the hit on B." })!.Kind);
        Assert.Equal(OutcomeKind.PerfectBlock, ReplayFeedback.Of(seen with { UnitId = null, Message = "The hit on B is blocked perfectly." })!.Kind);
        Assert.Equal(OutcomeKind.Block, ReplayFeedback.Of(seen with { UnitId = null, Message = "The hit on B is blocked." })!.Kind);
    }

    [Fact]
    public void A_flash_on_someone_in_the_fog_stays_hidden()
    {
        var (sim, a, b) = Duel(new(9, 2), new(10, 2));
        Assert.True(sim.Execute(new Push(1, a.Id, new Pos(1, 0))).Ok);
        var flash = Round(sim, a, b).Single(e => e.Type == ScavengeEventType.PatternResolved) with { SeenBy = null };
        Assert.Equal(b.Id, flash.TargetId);
        Assert.Empty(FogLog.Filter([flash], "them", _ => false, id => id == a.Id, _ => true));
        Assert.Single(FogLog.Filter([flash], "them", _ => false, _ => true, _ => true));
    }

    [Fact]
    public void A_recover_then_the_same_units_next_action_are_two_steps()
    {
        ScavengeEvent E(ScavengeEventType type) => new(1, type, "x", 1);
        Assert.Equal([0, 1], ReplayPacing.StepStarts([E(ScavengeEventType.Recovered), E(ScavengeEventType.Attacked)]));
    }

    [Fact]
    public void A_rival_out_of_sight_keeps_its_pattern_to_itself()
    {
        var tiles = new List<Pos> { new(5, 5) };
        var e = new ScavengeEvent(1, ScavengeEventType.PatternResolved, "", 7, Pos: new(5, 4), Tiles: tiles);
        Assert.Empty(FogLog.Filter([e], "them", _ => false, _ => false, _ => true));
        Assert.Equal(tiles, FogLog.Filter([e], "them", _ => false, _ => true, _ => true).Single().Tiles);
    }
}
