using System.Text.Json.Nodes;
using Remnants.Core.Colony;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class DayNightTests
{
    private static ColonySim Colony(int hour, Dictionary<string, double>? stock = null)
    {
        var state = new ColonyState { Tick = hour };
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = 1 });
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "shelter", Level = 1, Plot = 1 });
        foreach (var (res, amount) in stock ?? new()) state.Stockpile.Add(res, amount);
        return new ColonySim(TestData.RealWithNights, state);
    }

    private static void RunTo(ColonySim sim, int hour)
    {
        while (sim.Hour != hour) sim.Tick();
    }

    [Theory]
    [InlineData(0, DayPhase.Night)]
    [InlineData(5, DayPhase.Dawn)]
    [InlineData(6, DayPhase.Dawn)]
    [InlineData(7, DayPhase.Day)]
    [InlineData(17, DayPhase.Day)]
    [InlineData(18, DayPhase.Dusk)]
    [InlineData(20, DayPhase.Night)]
    [InlineData(23, DayPhase.Night)]
    public void Phase_follows_the_hour(int hour, DayPhase phase)
    {
        var rules = TestData.RealWithNights.Rules;
        Assert.Equal(phase, DayNight.PhaseAt(hour, rules));
        Assert.Equal(phase, DayNight.PhaseAt(hour + 5 * rules.TicksPerDay, rules));
    }

    [Fact]
    public void Windows_wrap_past_midnight()
    {
        Assert.True(DayNight.InWindow(23, [22, 6]));
        Assert.True(DayNight.InWindow(3, [22, 6]));
        Assert.False(DayNight.InWindow(6, [22, 6]));
        Assert.True(DayNight.InWindow(6, [6, 20]));
        Assert.False(DayNight.InWindow(20, [6, 20]));
        Assert.Equal(8, DayNight.WindowLength([22, 6], 24));
    }

    [Fact]
    public void A_new_game_starts_at_the_start_hour()
    {
        var sim = NewGame.Create(TestData.RealWithNights, 1);
        Assert.Equal(TestData.RealWithNights.Rules.NewGame.StartHour, sim.Hour);
        Assert.Equal(1, DayNight.Day(sim.State.Tick, sim.Data.Rules));
    }

    [Fact]
    public void Workers_gather_on_the_day_shift_and_rest_at_night()
    {
        var sim = Colony(12);
        var cutter = sim.Build("woodcutter");
        var s = sim.AddSurvivor();
        Assert.True(sim.Execute(new AssignJob(s.Id, cutter.Id)).Ok);

        var before = sim.State.Stockpile.Get("wood");
        var rate = sim.OutputPerTick(cutter);
        sim.Tick();
        Assert.Equal(rate, sim.State.Stockpile.Get("wood") - before, 9);

        RunTo(sim, 23);
        before = sim.State.Stockpile.Get("wood");
        Assert.False(sim.OnShift(s));
        Assert.True(sim.IsAsleep(s));
        sim.Tick();
        Assert.Equal(before, sim.State.Stockpile.Get("wood"), 9);
    }

    [Fact]
    public void Output_per_day_counts_the_shift_hours()
    {
        var sim = Colony(12);
        var farm = sim.Build("farm");
        var s = sim.AddSurvivor();
        sim.Execute(new AssignJob(s.Id, farm.Id));
        var hours = DayNight.WindowLength(sim.Data.Rules.DayNight.WorkHours, 24);

        Assert.Equal(sim.OutputPerTick(farm) * hours, sim.OutputPerDay(farm), 9);

        var expectedBefore = sim.OutputPerDay(farm);
        var before = sim.State.Stockpile.Get("food");
        // Nobody eats here except the farmer; add the food eaten back to compare production.
        sim.Advance(24);
        var eaten = sim.Data.Rules.Food.AdultPerTick * 24;
        // Training during the day nudges the rate up a little.
        Assert.InRange(sim.State.Stockpile.Get("food") - before + eaten, expectedBefore * 0.99, expectedBefore * 1.03);
    }

    [Fact]
    public void Children_stop_work_and_sleep_earlier_than_adults()
    {
        var sim = Colony(19);
        var farm = sim.Build("farm");
        var adult = sim.AddSurvivor();
        var child = sim.AddSurvivor(LifeStage.Child);
        sim.Execute(new AssignJob(adult.Id, farm.Id));
        sim.Execute(new AssignJob(child.Id, farm.Id));

        Assert.True(sim.OnShift(adult));
        Assert.False(sim.OnShift(child));
        Assert.True(sim.IsAsleep(child));
        Assert.False(sim.IsAsleep(adult));
    }

    [Fact]
    public void Outdoor_night_shift_needs_light_from_a_level_or_a_floodlight()
    {
        var sim = Colony(12, new() { ["wood"] = 50, ["scrap"] = 50 });
        var cutter = sim.Build("woodcutter");

        var refused = sim.Execute(new SetNightShift(cutter.Id, true));
        Assert.Equal(CommandError.NeedsLight, refused.Error);
        Assert.False(sim.CanRunNightShift(cutter));

        var wood = sim.State.Stockpile.Get("wood");
        Assert.True(sim.Execute(new InstallFloodlight(cutter.Id)).Ok);
        Assert.Equal(wood - sim.Data.Rules.DayNight.FloodlightCost["wood"], sim.State.Stockpile.Get("wood"), 9);
        Assert.Equal(CommandError.AlreadyLit, sim.Execute(new InstallFloodlight(cutter.Id)).Error);
        Assert.True(sim.Execute(new SetNightShift(cutter.Id, true)).Ok);
        Assert.True(cutter.NightShift);

        var quarry = sim.Build("quarry");
        quarry.Level = 2;
        Assert.True(sim.CanRunNightShift(quarry));
        Assert.Equal(CommandError.AlreadyLit, sim.Execute(new InstallFloodlight(quarry.Id)).Error);

        var workshop = sim.Build("workshop");
        Assert.True(sim.Execute(new SetNightShift(workshop.Id, true)).Ok);
        Assert.Equal(CommandError.NotOutdoor, sim.Execute(new InstallFloodlight(workshop.Id)).Error);
        Assert.Equal(CommandError.NoJobAtBuilding, sim.Execute(new SetNightShift(1, true)).Error);
    }

    [Fact]
    public void A_night_shift_works_at_reduced_output_costs_mood_and_leaves_workers_tired()
    {
        var sim = Colony(12, new() { ["wood"] = 50, ["scrap"] = 50 });
        var cutter = sim.Build("woodcutter");
        sim.Execute(new InstallFloodlight(cutter.Id));
        sim.Execute(new SetNightShift(cutter.Id, true));
        var adult = sim.AddSurvivor();
        var child = sim.AddSurvivor(LifeStage.Child);
        var farm = sim.Build("farm");
        sim.Execute(new AssignJob(adult.Id, cutter.Id));
        sim.Execute(new AssignJob(child.Id, farm.Id));

        // A standing night shift: 14 day hours + 10 night hours at 70%, all at the tired rate.
        var r = sim.Data.Rules.DayNight;
        Assert.Equal(sim.OutputPerTick(cutter) * (14 + 10 * r.NightShift.OutputMultiplier) * r.Tired.OutputMultiplier, sim.OutputPerDay(cutter), 9);

        RunTo(sim, 23);
        Assert.True(sim.OnShift(adult));
        Assert.False(sim.IsAsleep(adult));
        var before = sim.State.Stockpile.Get("wood");
        var rate = sim.OutputPerTick(cutter);
        sim.Tick();
        var rules = sim.Data.Rules.DayNight;
        Assert.Equal(rate * rules.NightShift.OutputMultiplier, sim.State.Stockpile.Get("wood") - before, 9);

        RunTo(sim, 7);
        Assert.True(sim.IsTired(adult));
        Assert.True(adult.Mood < Survivor.MaxMood);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.NightShift && e.Message.Contains("−30%"));
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Tired && e.SurvivorId == adult.Id);

        // Tired workers make less on the day shift too.
        before = sim.State.Stockpile.Get("wood");
        rate = sim.OutputPerTick(cutter);
        sim.Tick();
        Assert.Equal(rate * rules.Tired.OutputMultiplier, sim.State.Stockpile.Get("wood") - before, 9);

        // Off the night shift, a night's sleep pays the debt back.
        sim.Execute(new SetNightShift(cutter.Id, false));
        RunTo(sim, 7);
        Assert.False(sim.IsTired(adult));
    }

    [Fact]
    public void An_unlit_night_shift_logs_why_nothing_happens()
    {
        var sim = Colony(12);
        var cutter = sim.Build("woodcutter");
        var s = sim.AddSurvivor();
        sim.Execute(new AssignJob(s.Id, cutter.Id));
        cutter.NightShift = true;  // e.g. set in an edited save; Core never lets it run unlit
        RunTo(sim, 21);
        Assert.False(sim.OnShift(s));
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.NightShift && e.Message.Contains("Too dark"));
    }

    [Fact]
    public void Sleep_in_a_bed_restores_mood_and_health()
    {
        var sim = Colony(22);
        var s = sim.AddSurvivor();
        s.HasBed = true;
        s.Mood = 40;
        s.Health = 50;
        sim.State.Stockpile.Add("food", 10);
        var rules = sim.Data.Rules;
        sim.Tick();
        Assert.Equal(40 + rules.Mood.RecoveryPerTick + rules.DayNight.SleepMoodPerTick, s.Mood, 9);
        Assert.Equal(50 + rules.Food.RecoveryPerTick + rules.DayNight.SleepHealthPerTick, s.Health, 9);
    }

    [Fact]
    public void An_evening_off_at_the_campfire_lifts_mood_and_a_hot_supper_more()
    {
        var sim = Colony(20, new() { ["food"] = 10 });
        var s = sim.AddSurvivor();
        s.Mood = 40;
        var rules = sim.Data.Rules;
        sim.Tick();
        Assert.Equal(40 + rules.Mood.RecoveryPerTick + rules.DayNight.CampfireMoodPerTick, s.Mood, 9);

        sim.State.Stockpile.Add("stew", 5);
        s.Mood = 40;
        sim.Tick();
        var stew = rules.Food.Meals.First(m => m.Resource == "stew");
        Assert.Equal(40 + stew.MoodPerTick + rules.DayNight.NightMealMoodPerTick + rules.Mood.RecoveryPerTick + rules.DayNight.CampfireMoodPerTick, s.Mood, 9);
    }

    [Fact]
    public void Classes_keep_the_day_shift()
    {
        var sim = Colony(23);
        var s = sim.AddSurvivor();
        var hall = sim.State.Buildings[0];
        Assert.True(sim.Execute(new StartStudy(s.Id, hall.Id, Skill.Farming)).Ok);
        sim.Tick();
        Assert.Equal(0, s.Get(Skill.Farming), 9);
        RunTo(sim, 10);
        sim.Tick();
        Assert.True(s.Get(Skill.Farming) > 0);
    }

    [Fact]
    public void Night_shift_floodlight_and_sleep_debt_survive_a_save()
    {
        var sim = Colony(12, new() { ["wood"] = 50, ["scrap"] = 50 });
        var cutter = sim.Build("woodcutter");
        sim.Execute(new InstallFloodlight(cutter.Id));
        sim.Execute(new SetNightShift(cutter.Id, true));
        var s = sim.AddSurvivor();
        s.SleepDebt = 7;

        var loaded = SaveGame.Load(sim.Data, SaveGame.Save(sim.State));
        var b = loaded.State.FindBuilding(cutter.Id)!;
        Assert.True(b.NightShift);
        Assert.True(b.Floodlight);
        Assert.Equal(7, loaded.State.FindSurvivor(s.Id)!.SleepDebt);
    }

    [Fact]
    public void A_version_9_save_loads_rested_with_no_night_shifts()
    {
        var sim = Colony(3);
        sim.Build("woodcutter");
        sim.AddSurvivor();
        var node = JsonNode.Parse(SaveGame.Save(sim.State))!;
        node["version"] = 9;
        foreach (var b in node["colony"]!["buildings"]!.AsArray()) { b!.AsObject().Remove("nightShift"); b.AsObject().Remove("floodlight"); }
        foreach (var x in node["colony"]!["survivors"]!.AsArray()) x!.AsObject().Remove("sleepDebt");

        var loaded = SaveGame.Load(sim.Data, node.ToJsonString());
        Assert.All(loaded.State.Buildings, b => Assert.False(b.NightShift || b.Floodlight));
        Assert.All(loaded.State.Survivors, x => Assert.Equal(0, x.SleepDebt));
        Assert.Equal(3, loaded.Hour);
    }

    [Fact]
    public void With_day_and_night_off_everyone_works_every_tick()
    {
        var sim = new ColonySim(TestData.Real, new ColonyState { Tick = 2 });
        sim.State.Buildings.Add(new Building { Id = sim.State.NextBuildingId++, DefId = "town_hall", Level = 1 });
        var farm = sim.Build("farm");
        var s = sim.AddSurvivor();
        sim.Execute(new AssignJob(s.Id, farm.Id));
        Assert.True(sim.OnShift(s));
        Assert.False(sim.IsAsleep(s));
        Assert.Equal(sim.OutputPerTick(farm) * 24, sim.OutputPerDay(farm), 9);
    }
}
