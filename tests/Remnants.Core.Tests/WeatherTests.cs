using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Weather: the seeded chain of spells per region, the forecast, its effects at home (work, masks, mood, lightning and
/// repair), on the road (the travel planner's weather term, closed routes, grounded drones) and in the field (changes
/// during a run, thunder, lightning, dust on aim, drones and the Linked relay).
/// </summary>
public class WeatherTests
{
    private static GameData Shipped => TestData.RealWithWeather;

    /// <summary>The shipped data with one kind of weather forever (at full intensity) at home and everywhere.</summary>
    private static GameData Always(string kind, bool nights = false)
    {
        var d = Shipped;
        var w = d.Weather;
        var only = w.Kind(kind) with { Hours = [10_000, 10_000], Intensity = [1, 1] };
        var kinds = new Dictionary<string, WeatherKindDef>(w.Kinds) { [kind] = only };
        var rules = d.Rules with { DayNight = d.Rules.DayNight with { Enabled = nights } };
        return new GameData(d.Resources.Values, d.Jobs.Values, d.Buildings.Values, rules, d.Recipes.Values, d.Progression, d.Cybernetics?.Def,
            w with { Start = kind, Kinds = kinds });
    }

    private static ColonySim Colony(GameData data, Dictionary<string, double>? stock = null)
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = 1 });
        state.Stockpile.Add("food", 100);
        foreach (var (res, amount) in stock ?? new()) state.Stockpile.Add(res, amount);
        return new ColonySim(data, state, Real);
    }

    /// <summary>The weather-and-shift factor a lone worker at a fresh building gets this tick (what production multiplies by).</summary>
    private static double Produced(GameData data, string building, Action<ColonySim>? setup = null, LifeStage stage = LifeStage.Adult)
    {
        var sim = Colony(data);
        var b = sim.Build(building);
        var s = sim.AddSurvivor(stage);
        setup?.Invoke(sim);
        s.AssignedBuildingId = b.Id;
        return Schedule.Crew(sim.State, sim.Data, b).Sum(c => c.Factor);
    }

    [Fact]
    public void Field_weather_and_colony_weather_name_the_same_kinds_and_masks_are_default_known()
    {
        foreach (var id in Real.Rules.Environment.Weather.Keys) Assert.Contains(id, Shipped.Weather.Kinds.Keys);
        foreach (var id in Shipped.Weather.MaskItems) Assert.True(Real.Items.ContainsKey(id), id);
        var recipe = Shipped.Recipes.Values.Single(r => r.Item == "dust_mask");
        Assert.Equal("tailor", recipe.Building);
        Assert.Null(recipe.Blueprint);
    }

    // ---- the chain ----

    [Fact]
    public void The_same_seed_gives_the_same_weather_whatever_order_it_is_asked_in()
    {
        var a = Colony(Shipped);
        var b = Colony(Shipped);
        b.State.Weather.Seed = a.State.Weather.Seed;
        var far = Weather.SpellAt(b.State, Shipped, "Iron Corridor", 24 * 40);
        for (long t = 0; t <= 24 * 40; t += 7) Weather.SpellAt(a.State, Shipped, "Iron Corridor", t);
        Assert.Equal(far, Weather.SpellAt(a.State, Shipped, "Iron Corridor", 24 * 40));

        var spells = a.State.Weather.Spells["Iron Corridor"];
        for (var i = 1; i < spells.Count; i++)
        {
            Assert.Equal(spells[i - 1].End, spells[i].Start);
            Assert.NotEqual(spells[i - 1].Kind, spells[i].Kind);
        }

        var c = Colony(Shipped);
        c.State.Weather.Seed = a.State.Weather.Seed + 1;
        Assert.NotEqual(Kinds(a, "Home Valley"), Kinds(c, "Home Valley"));
    }

    private static string Kinds(ColonySim sim, string region) =>
        string.Join(",", Weather.SpellsBetween(sim.State, sim.Data, region, 0, 24 * 30).Select(s => s.Kind));

    [Fact]
    public void The_wastes_get_more_dust_than_the_valley()
    {
        var sim = Colony(Shipped);
        double DustShare(string region)
        {
            var spells = Weather.SpellsBetween(sim.State, Shipped, region, 0, 24 * 400);
            return spells.Where(s => s.Kind == "dust_storm").Sum(s => s.Hours) / (double)spells.Sum(s => s.Hours);
        }
        Assert.True(DustShare("The Black Grid") > 2 * DustShare("Home Valley"), $"{DustShare("The Black Grid")} vs {DustShare("Home Valley")}");
    }

    [Fact]
    public void With_weather_off_it_is_always_clear_and_nothing_changes()
    {
        var sim = Colony(TestData.Real);
        Assert.Equal(WeatherRules.Clear, Weather.Current(sim.State, sim.Data).Kind);
        Assert.Empty(sim.State.Weather.Spells);
        Assert.Equal(Produced(TestData.Real, "farm"), Produced(Always("clear"), "farm"), 9);
    }

    [Fact]
    public void Weather_does_not_disturb_the_colony_rng()
    {
        var calm = Colony(Always("rain"));
        var plain = Colony(TestData.Real);
        calm.Advance(50);
        plain.Advance(50);
        Assert.Equal(plain.State.RngState, calm.State.RngState);
    }

    // ---- the forecast ----

    [Fact]
    public void The_forecast_sees_further_with_a_bigger_town_hall_and_a_lab()
    {
        var sim = Colony(Shipped);
        Assert.Equal(1, Weather.ForecastDays(sim.State, Shipped));
        sim.State.Buildings[0].Level = 3;
        Assert.Equal(3, Weather.ForecastDays(sim.State, Shipped));
        sim.State.Buildings.Add(new Building { Id = sim.State.NextBuildingId++, DefId = "electronics_lab", Level = 2 });
        Assert.Equal(4, Weather.ForecastDays(sim.State, Shipped));

        var forecast = Weather.Forecast(sim.State, Shipped);
        Assert.True(forecast.Entries[0].Current);
        Assert.Equal(Weather.Current(sim.State, Shipped).Kind, forecast.Entries[0].Called);
        Assert.True(forecast.Entries[^1].Start < sim.State.Tick + 4 * 24);
    }

    [Fact]
    public void A_scanner_in_the_stores_makes_the_forecast_surer_and_it_is_stable()
    {
        var sim = Colony(Shipped);
        var before = Weather.ForecastAccuracy(sim.State, Shipped);
        sim.State.Inventory.Add("scanner");
        Assert.Equal(before + Shipped.Weather.Forecast.InstrumentAccuracy, Weather.ForecastAccuracy(sim.State, Shipped), 9);
        Assert.Equal(Weather.Forecast(sim.State, Shipped), Weather.Forecast(sim.State, Shipped), new ForecastComparer());
    }

    [Fact]
    public void A_forecast_that_misses_names_the_kind_it_is_confused_with()
    {
        var sim = Colony(Shipped);
        sim.State.Buildings[0].Level = 3;
        var misses = 0;
        for (var seed = 1UL; seed < 60; seed++)
        {
            sim.State.Weather = new WeatherState { Seed = seed };
            foreach (var e in Weather.Forecast(sim.State, Shipped).Entries.Where(e => !e.Current))
            {
                var real = Weather.SpellAt(sim.State, Shipped, Shipped.Weather.ColonyRegion, e.Start).Kind;
                if (real == e.Called) continue;
                misses++;
                Assert.Equal(Shipped.Weather.Kind(real).ConfusedWith, e.Called);
            }
        }
        Assert.True(misses > 0, "some calls miss");
    }

    private sealed class ForecastComparer : IEqualityComparer<WeatherForecast>
    {
        public bool Equals(WeatherForecast? x, WeatherForecast? y) => x!.Days == y!.Days && x.Accuracy == y.Accuracy && x.Entries.SequenceEqual(y.Entries);
        public int GetHashCode(WeatherForecast obj) => obj.Days;
    }

    // ---- at home ----

    [Fact]
    public void Rain_waters_the_farm_and_slows_the_woodcutter()
    {
        var clear = Always("clear");
        var rain = Always("rain");
        var kind = rain.Weather.Kind("rain").Colony;
        Assert.Equal(Produced(clear, "farm") * kind.OutdoorOutput * kind.CropOutput, Produced(rain, "farm"), 6);
        Assert.Equal(Produced(clear, "woodcutter") * kind.OutdoorOutput, Produced(rain, "woodcutter"), 6);
        var crop = Assert.Single(Crops.Modifiers(rain, Weather.SpellAt(new ColonyState(), rain, "Home Valley", 0)));
        Assert.Equal(("weather", kind.CropOutput), (crop.Source, crop.Multiplier));
    }

    [Fact]
    public void A_dust_storm_needs_masks_children_stay_in_and_the_unmasked_suffer()
    {
        var clear = Always("clear");
        var dust = Always("dust_storm");
        var kind = dust.Weather.Kind("dust_storm").Colony;
        Assert.Equal(Produced(clear, "woodcutter") * kind.OutdoorOutput, Produced(dust, "woodcutter"), 6);
        Assert.Equal(Produced(clear, "woodcutter") * kind.MaskedOutput!.Value, Produced(dust, "woodcutter", s => s.State.Inventory.Add("dust_mask")), 6);
        Assert.Equal(0, Produced(dust, "woodcutter", stage: LifeStage.Child), 9);
    }

    [Fact]
    public void The_first_outdoor_worker_by_id_gets_the_only_mask()
    {
        var sim = Colony(Always("dust_storm"));
        var b = sim.Build("woodcutter");
        var first = sim.AddSurvivor();
        var second = sim.AddSurvivor();
        sim.State.Inventory.Add("dust_mask");
        Assert.True(sim.Execute(new AssignJob(first.Id, b.Id)).Ok);
        Assert.True(sim.Execute(new AssignJob(second.Id, b.Id)).Ok);
        first.Health = second.Health = 100;
        sim.Advance(10);
        Assert.True(first.Health > second.Health + 1, $"{first.Health} vs {second.Health}");
        Assert.Equal([first.Id], Weather.Masked(sim.State, sim.Data, Weather.Current(sim.State, sim.Data)));
    }

    [Fact]
    public void Night_shift_and_weather_stack()
    {
        var clear = Always("clear", nights: true);
        var rain = Always("rain", nights: true);
        double NightOutput(GameData data)
        {
            var sim = Colony(data);
            sim.State.Tick = 21;
            var b = sim.Build("woodcutter");
            b.Level = 2;
            var s = sim.AddSurvivor();
            Assert.True(sim.Execute(new AssignJob(s.Id, b.Id)).Ok);
            Assert.True(sim.Execute(new SetNightShift(b.Id, true)).Ok);
            var before = sim.State.Stockpile.Get("wood");
            sim.Advance(1);
            return sim.State.Stockpile.Get("wood") - before;
        }
        Assert.True(NightOutput(clear) > 0);
        Assert.Equal(NightOutput(clear) * rain.Weather.Kind("rain").Colony.OutdoorOutput, NightOutput(rain), 6);
    }

    [Fact]
    public void Fog_and_storms_weigh_on_mood()
    {
        var sim = Colony(Always("fog"));
        var s = sim.AddSurvivor();
        s.Mood = 50;
        sim.Advance(10);
        var plain = Colony(Always("clear"));
        var p = plain.AddSurvivor();
        p.Mood = 50;
        plain.Advance(10);
        Assert.Equal(p.Mood - 10 * sim.Data.Weather.Kind("fog").Colony.MoodPerTick, s.Mood, 6);
    }

    [Fact]
    public void Lightning_damages_a_building_which_works_at_half_pace_until_repaired()
    {
        var sim = Colony(Always("thunderstorm"), new() { ["wood"] = 50, ["scrap"] = 50 });
        sim.Build("woodcutter");
        var ticks = 0;
        while (!sim.State.Buildings.Any(b => b.Damaged) && ticks++ < 2000) sim.Tick();
        var hit = sim.State.Buildings.Single(b => b.Damaged);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.LightningStruck && e.BuildingId == hit.Id);

        var woodcutter = sim.State.Buildings.First(b => b.DefId == "woodcutter");
        var def = sim.Data.Buildings["woodcutter"];
        var worker = sim.AddSurvivor();
        var spell = Weather.Current(sim.State, sim.Data);
        woodcutter.Damaged = false;
        var whole = Weather.WorkFactor(sim.Data, spell, woodcutter, def, worker, false);
        woodcutter.Damaged = true;
        Assert.Equal(whole * sim.Data.Weather.Repair.DamagedOutput, Weather.WorkFactor(sim.Data, spell, woodcutter, def, worker, false), 9);

        foreach (var other in sim.State.Buildings.Where(b => b != woodcutter)) other.Damaged = false;
        Assert.Equal(CommandError.NotDamaged, sim.Execute(new RepairBuilding(sim.State.Buildings[0].Id)).Error);
        var wood = sim.State.Stockpile.Get("wood");
        Assert.True(sim.Execute(new RepairBuilding(woodcutter.Id)).Ok);
        Assert.False(woodcutter.Damaged);
        Assert.Equal(wood - sim.Data.Weather.Repair.Cost["wood"], sim.State.Stockpile.Get("wood"), 9);
    }

    [Fact]
    public void The_log_says_when_the_weather_turns()
    {
        var sim = Colony(Shipped);
        sim.Advance(24 * 10);
        Assert.Contains(sim.State.Events, e => e.Type is ColonyEventType.WeatherChanged or ColonyEventType.Storm);
    }

    // ---- on the road ----

    [Fact]
    public void A_storm_adds_days_to_the_road_as_a_separate_term()
    {
        var sim = Colony(Always("thunderstorm"));
        var zone = Real.Zones["suburb_ruins"];
        var plan = WorldMap.Travel(sim, zone, sim.State.Tick);
        Assert.Equal(WorldMap.TravelTicks(sim.Data, zone), plan.BaseTicks);
        var term = Assert.Single(plan.Modifiers);
        Assert.Equal("weather", term.Source);
        Assert.Equal(plan.BaseTicks + 12, plan.Ticks);
        Assert.False(plan.Blocked);

        // Terms compose: another source's multiplier scales the base, extra ticks add on top.
        var withVehicle = plan with { Modifiers = [.. plan.Modifiers, new TravelModifier("vehicle", "Handcart", Multiplier: 0.5)] };
        Assert.Equal(plan.BaseTicks / 2 + 12, withVehicle.Ticks);

        var doorstep = Real.Zones.Values.First(z => z.TravelDays == 0);
        Assert.Empty(WorldMap.Travel(sim, doorstep, sim.State.Tick).Modifiers);
    }

    [Fact]
    public void A_dust_storm_closes_far_routes_and_costs_rations_on_near_ones()
    {
        var sim = Colony(Always("dust_storm"), new() { ["food"] = 200 });
        var a = sim.AddSurvivor();
        var far = Real.Zones["flooded_mall"];
        sim.State.Zones[far.Id] = new ZoneStatus { Intel = ZoneIntel.Scouted };
        var r = sim.Execute(new LaunchExpedition(far.Id, [new(a.Id)]));
        Assert.Equal(CommandError.RouteBlocked, r.Error);

        var near = Real.Zones["suburb_ruins"];
        sim.State.Zones[near.Id] = new ZoneStatus { Intel = ZoneIntel.Scouted };
        var plan = sim.PlanArrival(near.Id);
        Assert.True(plan.DelayRationsPerMember > 0);
        var food = sim.State.Stockpile.Get("food");
        Assert.True(sim.Execute(new LaunchExpedition(near.Id, [new(a.Id)])).Ok);
        Assert.Equal(WorldMap.RationsPerMember(sim.Data, near) + plan.ExtraRationsPerMember, food - sim.State.Stockpile.Get("food"), 9);
        Assert.Equal(WorldMap.TravelTicks(sim.Data, near) + 24, sim.State.Expedition!.TravelTicks);
    }

    [Fact]
    public void Drones_stay_grounded_in_a_dust_storm()
    {
        var sim = Colony(Always("dust_storm"));
        sim.State.Inventory.Add("scout_drone");
        var zone = Real.Zones.Values.First(z => z.StartIntel == ZoneIntel.Rumored || z.TravelDays > 0);
        sim.State.Zones[zone.Id] = new ZoneStatus { Intel = ZoneIntel.Rumored };
        Assert.Equal(CommandError.DronesGrounded, sim.Execute(new SendScoutDrone(zone.Id)).Error);
    }

    // ---- in the field ----

    [Fact]
    public void A_mission_takes_the_weather_over_its_zone_and_its_changes()
    {
        var sim = Colony(Shipped);
        var zone = Real.Zones["rail_depot"];
        var tick = 24L * 7;
        var env = sim.MissionEnvironment(tick, zone.Id);
        var spells = Weather.SpellsBetween(sim.State, Shipped, zone.Region, tick, tick + Shipped.Weather.Field.LookaheadHours);
        Assert.Equal(spells[0].Kind, env.Weather);
        Assert.Equal(spells.Count - 1, env.Shifts.Count);
        if (env.Shifts.Count > 0)
            Assert.Equal(1 + (spells[1].Start - tick) * Shipped.Weather.Field.RoundsPerHour, env.Shifts[0].Round);
        Assert.Null(Colony(TestData.Real).MissionEnvironment(tick, zone.Id).Weather);
    }

    private static ScavengeSim Field(string? weather, IEnumerable<Unit> units, int width = 24, int height = 5, List<WeatherShift>? shifts = null) =>
        Arena(width, height, units, setup: (_, s) =>
        {
            s.Environment = ScavengeEnvironment.At(12, DayPhase.Day, weather);
            s.Environment.Shifts = shifts ?? new();
        });

    private static void NextRound(ScavengeSim sim)
    {
        var round = sim.State.Round;
        while (sim.State.Round == round && !sim.State.Finished)
        {
            var u = sim.State.CurrentUnit!;
            Assert.True(sim.Execute(new EndTurn(u.PlayerId!.Value, u.Id)).Ok);
        }
    }

    [Fact]
    public void The_weather_turns_during_a_run_and_everyone_hears_it()
    {
        var p = SurvivorUnit(1, new Pos(1, 2));
        var sim = Field("rain", [p], shifts: [new WeatherShift(2, "fog", 0.8)]);
        var rainSight = sim.Profile(p).Sight;
        NextRound(sim);
        Assert.Equal("fog", sim.State.Environment.Weather);
        Assert.Equal(0.8, sim.State.Environment.Intensity);
        Assert.Empty(sim.State.Environment.Shifts);
        Assert.True(sim.Profile(p).Sight < rainSight);
        var e = sim.State.Events.Last(e => e.Type == ScavengeEventType.WeatherChanged);
        Assert.Null(e.UnitId);
        Assert.Single(FogLog.Filter([e], "enemy", _ => false, _ => false, _ => false));
    }

    [Fact]
    public void Rain_drowns_out_noise_and_puts_flares_out_sooner()
    {
        var rain = Real.Rules.Environment.Weather["rain"];
        Assert.True(rain.HearingMultiplier < 1);
        var thrower = SurvivorUnit(1, new Pos(1, 2), pack: "flare");
        var sim = Field("rain", [thrower]);
        Assert.True(sim.Execute(new UseItem(1, thrower.Id, "flare", At: new Pos(4, 2))).Ok);
        var use = Real.Items["flare"].Use!;
        var rounds = Math.Max(1, (int)Math.Round(use.LightRounds * rain.FlareRoundsMultiplier));
        Assert.Equal(sim.State.Round + rounds - 1, sim.State.Flares.Single().UntilRound);

        // Footsteps behind a wall 3 tiles off are heard in the dry, not in the rain or under thunder.
        Assert.True(Heard(null));
        Assert.False(Heard("rain"));
        Assert.False(Heard("thunderstorm", thunder: true));
    }

    /// <summary>Whether a watcher behind a wall hears a stranger step to 3 tiles away.</summary>
    private static bool Heard(string? weather, bool thunder = false)
    {
        var walker = SurvivorUnit(2, new Pos(6, 2));
        var watcher = SurvivorUnit(1, new Pos(1, 2));
        var rows = Enumerable.Repeat("...#........", 5).ToArray();
        var sim = Arena(12, 5, [walker, watcher], rows: rows, setup: (_, st) => st.Environment = ScavengeEnvironment.At(12, DayPhase.Day, weather));
        if (thunder) sim.State.Environment.ThunderRound = sim.State.Round;
        Assert.True(sim.Execute(new Move(2, walker.Id, new Pos(4, 2))).Ok);
        return sim.State.Contacts.Any(c => c.Side == watcher.Side && c.Kind == ContactKind.Movement);
    }

    [Fact]
    public void Fog_cuts_sight_hard()
    {
        var p = SurvivorUnit(1, new Pos(1, 2));
        var clear = Field(null, [p]).Profile(p).Sight;
        var q = SurvivorUnit(1, new Pos(1, 2));
        Assert.Equal(Math.Max(Real.Rules.Environment.Weather["fog"].MinSight, clear * Real.Rules.Environment.Weather["fog"].SightFor(SenseKind.Human)),
            Field("fog", [q]).Profile(q).Sight, 9);
    }

    [Fact]
    public void Dust_spoils_ranged_aim_but_not_melee_and_husks_see_through_it()
    {
        var shooter = SurvivorUnit(1, new Pos(1, 2), weapon: "pistol");
        var husk = EnemyUnit("husk", new Pos(3, 2));
        var dust = Field("dust_storm", [shooter, husk]);
        var shooter2 = SurvivorUnit(1, new Pos(1, 2), weapon: "pistol");
        var husk2 = EnemyUnit("husk", new Pos(3, 2));
        var clear = Field(null, [shooter2, husk2]);
        var gun = dust.Profile(shooter).Actions.First(a => a.Pattern != Items.AttackPattern.Melee);
        var penalty = Real.Rules.Environment.Weather["dust_storm"].RangedAccuracyPenalty;
        Assert.True(penalty > 0);
        Assert.True(Combat.ChanceToHit(dust, shooter, gun, husk) < Combat.ChanceToHit(clear, shooter2, gun, husk2));
        Assert.Equal(0, Combat.WeatherPenalty(dust, dust.Profile(husk).Actions.First()));
        Assert.Equal(clear.Profile(husk2).Sight, dust.Profile(husk).Sight, 9);
    }

    [Fact]
    public void A_dust_storm_grounds_drones_and_cuts_the_Linked_relay()
    {
        var op = SurvivorUnit(1, new Pos(1, 2), pack: "scout_drone");
        var sim = Field("dust_storm", [op]);
        Assert.Equal(ScavengeError.DroneGrounded, sim.Execute(new DeployDrone(1, op.Id, new Pos(2, 2))).Error);

        var range = Real.Enemies["linked"].RelayRange;
        Unit[] Pair() => [SurvivorUnit(1, new Pos(0, 0)), EnemyUnit("linked", new Pos(10, 2)), EnemyUnit("linked", new Pos(10 + range, 2))];
        Assert.Single(Network.Groups(Field(null, Pair(), width: 30)), g => g.Units.Count == 2);
        Assert.DoesNotContain(Network.Groups(Field("dust_storm", Pair(), width: 30)), g => g.Units.Count == 2);
    }

    [Fact]
    public void Lightning_shows_everyone_for_a_round()
    {
        var p = SurvivorUnit(1, new Pos(1, 2));
        var far = EnemyUnit("husk", new Pos(22, 2));
        var sim = Field("thunderstorm", [p, far]);
        Assert.False(Detection.SideSees(Real, sim.State, p.Side, far));
        sim.State.Environment.LightningRound = sim.State.Round;
        Assert.True(Detection.SideSees(Real, sim.State, p.Side, far));
        sim.State.Environment.LightningRound = sim.State.Round - 1;
        Assert.False(Detection.SideSees(Real, sim.State, p.Side, far));

    }

    [Fact]
    public void Storms_roll_thunder_and_lightning_from_the_run_rng()
    {
        var p = SurvivorUnit(1, new Pos(1, 2));
        var sim = Field("thunderstorm", [p]);
        for (var i = 0; i < 40; i++) NextRound(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Thunder);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Lightning);

        var q = SurvivorUnit(1, new Pos(1, 2));
        var calm = Field("fog", [q]);
        var rng = calm.State.RngState;
        NextRound(calm);
        Assert.Equal(rng, calm.State.RngState);
    }

    // ---- saves ----

    [Fact]
    public void Weather_and_damage_survive_a_save_and_old_saves_get_a_seed()
    {
        var sim = Colony(Shipped);
        sim.Build("woodcutter").Damaged = true;
        sim.Advance(30);
        var json = Serialization.SaveGame.Save(sim.State);
        var loaded = Serialization.SaveGame.Load(Shipped, json, Real);
        Assert.Equal(sim.State.Weather.Seed, loaded.State.Weather.Seed);
        Assert.True(loaded.State.Buildings.Single(b => b.DefId == "woodcutter").Damaged);
        Assert.Equal(Weather.SpellAt(sim.State, Shipped, "Ashgrove", 500), Weather.SpellAt(loaded.State, Shipped, "Ashgrove", 500));

        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["version"] = 11;
        node["colony"]!.AsObject().Remove("weather");
        var old = Serialization.SaveGame.Load(Shipped, node.ToJsonString(), Real);
        Assert.NotEqual(0UL, old.State.Weather.Seed);
    }

    [Fact]
    public void A_run_keeps_its_coming_weather_through_a_save()
    {
        var sim = NewScavenge.Create(Real, "outskirts", 3, [Party(1)],
            new ZoneConditions(Environment: new ScavengeEnvironment { Weather = "rain", Intensity = 0.7, Shifts = [new WeatherShift(5, "thunderstorm", 0.9)] }));
        var loaded = ScavengeSave.Load(Real, ScavengeSave.Save(sim.State));
        Assert.Equal("rain", loaded.State.Environment.Weather);
        Assert.Equal(0.7, loaded.State.Environment.Intensity);
        Assert.Equal(new WeatherShift(5, "thunderstorm", 0.9), Assert.Single(loaded.State.Environment.Shifts));
    }
}
