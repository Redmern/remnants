using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Story;
using static Remnants.Core.Tests.StoryTestData;

namespace Remnants.Core.Tests;

public class StoryDataTests
{
    [Fact]
    public void Story_data_loads_and_every_reference_is_valid()
    {
        var errors = Real.Validate(TestData.Real, ScavengeTestData.Real);
        Assert.Empty(errors);
        Assert.Equal(["m1", "m2", "m3", "m4", "m5", "m6", "m7", "m8", "m9", "m10", "m11", "m12", "m13", "m14", "m15", "m16",
            "m17_gate", "m17_choir", "m17_core"], Real.Missions.Where(m => m.Main).Select(m => m.Id));
    }

    [Fact]
    public void Every_story_reward_is_a_schematic_named_in_the_schematics_doc()
    {
        var doc = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "SCHEMATICS.md"));
        foreach (var e in Real.Missions.SelectMany(m => m.Rewards).Where(e => e.Kind == EffectKind.Schematic))
            Assert.Contains($"`{e.Id}`", doc);
    }

    [Fact]
    public void Every_handmade_map_parses_is_connected_and_has_spawn_and_exit()
    {
        foreach (var map in Real.Maps.Values)
        {
            Assert.Empty(HandmadeMaps.Validate(ScavengeTestData.Real, map));
            var built = HandmadeMaps.Build(map);
            if (map.Room) continue;
            Assert.NotEmpty(built.Spawns);
            Assert.NotEmpty(built.Map.Exits);
        }
        Assert.All(new[] { 1, 2, 3 }, th => Assert.True(Real.Maps.ContainsKey($"perimeter_th{th}")));
    }

    [Fact]
    public void Validation_catches_a_dead_end_and_a_broken_map()
    {
        var bad = new DialogueDef { Id = "bad", Nodes = [new DialogueNode { Id = "a", Speaker = "iris", Next = "nowhere" }] };
        var walled = new HandmadeMapDef { Id = "walled", Zone = "outskirts", Rows = ["#####", "#S#.#", "##E##"] };
        var story = new StoryData(Real.Characters.Values, [], [bad], [walled]);
        var errors = story.Validate(TestData.Real, ScavengeTestData.Real);
        Assert.Contains(errors, e => e.Contains("next 'nowhere'"));
        Assert.Contains(errors, e => e.Contains("cannot be reached"));
    }

    [Fact]
    public void A_room_behind_a_door_device_is_reachable_but_one_behind_a_terminal_is_not()
    {
        HandmadeMapDef Map(DeviceKind kind) => new()
        {
            Id = "door", Zone = "outskirts", Rows = ["#####", "#S..#", "##d##", "#...#", "##E##"], Markers = new() { ["d"] = "door" },
            Devices = [new MapDeviceDef(kind, "door")],
        };
        Assert.Empty(HandmadeMaps.Validate(ScavengeTestData.Real, Map(DeviceKind.Door)));
        Assert.Contains(HandmadeMaps.Validate(ScavengeTestData.Real, Map(DeviceKind.Terminal)), e => e.Contains("cannot be reached"));
    }

    [Fact]
    public void The_Stranger_has_no_Linked_in_the_Ruins()
    {
        foreach (var seed in new ulong[] { 1, 2, 3, 4, 5, 6 })
        {
            var sim = NewScavenge.Create(ScavengeTestData.Real, "suburb_ruins", seed, [ScavengeTestData.Party(1)], null,
                new MissionSetup(Real.Mission("m4")!.Field!, Room: Real.Maps["ruins_house"]));
            Assert.DoesNotContain(sim.State.Units, u => u.Kind == UnitKind.Enemy && u.EnemyDefId is { } e
                                                        && ScavengeTestData.Real.Enemies[e].Tags.Contains("network"));
        }
    }

    [Fact]
    public void A_fixed_room_is_stamped_whole_and_reachable_from_the_spawn_and_exits()
    {
        foreach (var seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            var sim = NewScavenge.Create(ScavengeTestData.Real, "scrapyard_flats", seed, [ScavengeTestData.Party(1)], null,
                new MissionSetup(Real.Mission("m3")!.Field!, Room: Real.Maps["scrapyard_terminal"]));
            var run = sim.State.Mission!;
            var terminal = sim.State.Devices.Single(d => d.Tag == "terminal");
            var locker = sim.State.Devices.Single(d => d.Tag == "locker");
            Assert.Equal(run.Marker("terminal")[0], terminal.Pos);
            var start = sim.Mine().Pos;
            var reach = MapGenerator.Flood(ScavengeTestData.Real, sim.State.Map, start);
            Assert.Contains(Pos.Neighbours8.Select(d => terminal.Pos + d), reach.Contains);
            Assert.Contains(Pos.Neighbours8.Select(d => locker.Pos + d), reach.Contains);
            Assert.All(sim.State.Map.Exits, e => Assert.Contains(e, reach));
            Assert.Single(sim.State.Caches, c => c.Items.Contains("hacking_kit"));
        }
    }
}

public class CampaignTests
{
    [Fact]
    public void A_sandbox_game_has_no_campaign_and_a_story_game_starts_with_pell_and_home()
    {
        var sandbox = NewGame.Create(TestData.Real, 7, ScavengeTestData.Real);
        Assert.Null(sandbox.State.Campaign);

        var sim = NewStory();
        var pell = Assert.Single(sim.State.Survivors, s => s.UniqueId == Campaign.PellId);
        Assert.Equal("Pell", pell.Name);
        Assert.True(pell.IsChild);
        sim.Advance(7 * 24);
        Assert.True(pell.IsChild);
        Assert.Equal(MissionStatus.Active, sim.State.Campaign!.Progress("m1").Status);
        Assert.Equal(MissionStatus.Locked, sim.State.Campaign.Progress("m2").Status);
        Assert.Contains(Campaign.Waiting(sim), c => c.DialogueId == "m1_home");
    }

    [Fact]
    public void Home_needs_the_buildings_then_the_dusk_scene_then_First_Light_unlocks_with_its_call()
    {
        var sim = NewStory(nights: true);
        TakeNextCall(sim);
        foreach (var b in new[] { "farm", "woodcutter", "workshop" }) sim.Build(b);
        sim.Tick();
        Assert.Equal(MissionStatus.Active, sim.State.Campaign!.Progress("m1").Status);
        sim.Build("radio_mast");
        sim.Tick();
        Assert.Equal(4, sim.State.Campaign.Progress("m1").Objectives.Count);

        // The scene calls at dusk only.
        var scene = TakeNextCall(sim, 48);
        Assert.Equal("m1_tuning", scene!.Id);
        Assert.Equal(DayPhase.Dusk, sim.Phase);
        sim.Tick();
        Assert.Equal(MissionStatus.Done, sim.State.Campaign.Progress("m1").Status);
        Assert.Equal(MissionStatus.Available, sim.State.Campaign.Progress("m2").Status);
        Assert.Contains(Campaign.Waiting(sim), c => c.DialogueId == "m2_first_light");
        Assert.Equal(1, sim.State.Campaign.Flag("heard_channel_nine"));
    }

    [Fact]
    public void Unlocks_follow_missions_buildings_and_town_hall_in_order()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        void Done(string id) { campaign.Progress(id).Status = MissionStatus.Done; campaign.Progress(id).Outcome = "done"; }
        Done("m1");
        Done("m2");
        sim.Tick();
        Assert.Equal(MissionStatus.Locked, campaign.Progress("m3").Status);
        sim.Build("workshop");
        sim.Tick();
        Assert.Equal(MissionStatus.Available, campaign.Progress("m3").Status);
        Assert.Equal(MissionStatus.Active, campaign.Progress("t_med_bay").Status);
        Done("m3");
        sim.Tick();
        Assert.Equal(MissionStatus.Locked, campaign.Progress("m4").Status);
        sim.Build("med_bay");
        sim.Tick();
        Assert.Equal(MissionStatus.Available, campaign.Progress("m4").Status);
        Assert.Equal(MissionStatus.Done, campaign.Progress("t_med_bay").Status);
        Done("m4");
        sim.Tick();
        Assert.Equal(MissionStatus.Locked, campaign.Progress("m5").Status);
        sim.State.Buildings.Single(b => b.DefId == "town_hall").Level = 2;
        sim.Tick();
        Assert.Equal(MissionStatus.Available, campaign.Progress("m5").Status);
    }

    [Fact]
    public void A_failed_mission_comes_back_after_its_retry_days_with_more_enemies()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        campaign.Progress("m1").Status = MissionStatus.Done;
        sim.Tick();
        var launched = LaunchM2(sim);
        Assert.True(launched.Ok, launched.Message);
        var run = sim.OpenExpeditionScavenge();
        var husks = run.State.Units.Count(u => u.EnemyDefId == "husk");
        foreach (var u in run.State.Units.Where(u => u.PlayerId == 1)) { u.Status = UnitStatus.Dead; u.Hp = 0; }
        run.UpdateParties();
        Assert.True(sim.Execute(new ApplyExtractionResult(run.ResultFor(1))).Ok);
        Assert.Equal(MissionStatus.Failed, campaign.Progress("m2").Status);
        Assert.Contains(Campaign.Waiting(sim), c => c.DialogueId == "m2_retry");

        sim.Advance(3 * 24 - 1);
        Assert.Equal(MissionStatus.Failed, campaign.Progress("m2").Status);
        sim.Tick();
        Assert.Equal(MissionStatus.Available, campaign.Progress("m2").Status);
        var adult = sim.State.Survivors.First(s => !s.IsChild);
        Assert.True(sim.Execute(new LaunchStoryMission("m2", [new ExpeditionMember(adult.Id)])).Ok);
        var retry = sim.OpenExpeditionScavenge();
        Assert.Equal(husks + 2, retry.State.Units.Count(u => u.EnemyDefId == "husk"));
    }

    [Fact]
    public void Done_pays_schematic_rewards_through_the_schematics_module_and_queues_the_debrief()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        campaign.Progress("m1").Status = MissionStatus.Done;
        sim.Tick();
        Assert.True(LaunchM2(sim).Ok);
        var run = sim.OpenExpeditionScavenge();
        var mine = run.State.Units.Where(u => u.PlayerId == 1).ToList();
        mine[0].Pack.Add("relay_antenna");
        foreach (var u in mine) u.Status = UnitStatus.Extracted;
        run.State.Mission!.Find("reach")!.Status = ObjectiveStatus.Done;
        run.UpdateParties();
        var medicine = sim.State.Stockpile.Get("medicine");
        Assert.True(sim.Execute(new ApplyExtractionResult(run.ResultFor(1))).Ok);

        Assert.Equal(MissionStatus.Done, campaign.Progress("m2").Status);
        Assert.True(Schematics.Schematics.Has(sim, "sch_watchtower"));
        Assert.Equal(Schematics.SchematicSource.Story, sim.State.Schematics.LearnedFrom["sch_watchtower"]);
        Assert.Equal(medicine + 3, sim.State.Stockpile.Get("medicine"));
        Assert.Equal(0, sim.State.Inventory.Count("relay_antenna"));
        Assert.Equal(ZoneIntel.Rumored, WorldMap.IntelOf(sim.State, ScavengeTestData.Real.Zones["suburb_ruins"]));
        Assert.Contains(Campaign.Waiting(sim), c => c.DialogueId == "m2_debrief");
        Assert.Equal("done", campaign.LastDebrief!.Outcome);
        Assert.Contains(campaign.LastDebrief.Rewards, r => r.Contains("Watchtower"));

        // A duplicate becomes Research Data.
        var data = sim.State.Stockpile.Get("research_data");
        Campaign.Apply(sim, new EffectDef { Kind = EffectKind.Schematic, Id = "sch_watchtower" }, "test");
        Assert.Equal(data + 2, sim.State.Stockpile.Get("research_data"));  // tier 1 duplicate
    }

    [Fact]
    public void A_building_reward_is_put_up_for_free_or_left_as_materials()
    {
        var sim = NewStory();
        sim.State.Buildings.Single(b => b.DefId == "town_hall").Level = 2;
        var (wood, stone) = (sim.State.Stockpile.Get("wood"), sim.State.Stockpile.Get("stone"));
        var house = new EffectDef { Kind = EffectKind.Building, Id = "house" };
        var lines = new List<string>();

        Campaign.Apply(sim, house, "test", lines);

        Assert.Single(sim.State.Buildings, b => b.DefId == "house");
        Assert.Equal((wood, stone), (sim.State.Stockpile.Get("wood"), sim.State.Stockpile.Get("stone")));  // their timber, not ours
        Assert.Contains(lines, l => l.Contains("House, built for you"));

        // Town Hall 1 cannot have a House: the crew leaves the timber.
        var low = NewStory();
        var lowWood = low.State.Stockpile.Get("wood");
        Campaign.Apply(low, house, "test", lines);
        Assert.DoesNotContain(low.State.Buildings, b => b.DefId == "house");
        Assert.Equal(lowWood + TestData.Real.Buildings["house"].Level(1).Cost["wood"], low.State.Stockpile.Get("wood"));
    }

    [Fact]
    public void Marrow_pays_for_Juniper_with_a_hand_and_ends_Act_1_with_a_house()
    {
        var hello = Real.Dialogues["marrow_hello"];
        Assert.Contains(hello.Nodes.SelectMany(n => n.Effects), e => e.Kind == EffectKind.Recruit && e.Text == "Hollis");
        Assert.Contains(Real.Mission("m5")!.Rewards, e => e.Kind == EffectKind.Building && e.Id == "house");

        var sim = NewStory();
        var adults = sim.State.Survivors.Count(s => !s.IsChild);
        Assert.True(sim.Execute(new TakeCall(Campaign.QueueCall(sim, "marrow_hello").Id)).Ok);
        PlayOut(sim);
        Assert.Equal(adults + 1, sim.State.Survivors.Count(s => !s.IsChild));
        Assert.Contains(sim.State.Survivors, s => s.Name == "Hollis");
    }

    [Fact]
    public void Leaving_early_is_not_a_fail_the_mission_stays_open()
    {
        var sim = NewStory();
        sim.State.Campaign!.Progress("m1").Status = MissionStatus.Done;
        sim.Tick();
        Assert.True(LaunchM2(sim).Ok);
        var run = sim.OpenExpeditionScavenge();
        foreach (var u in run.State.Units.Where(u => u.PlayerId == 1)) u.Status = UnitStatus.Extracted;
        run.UpdateParties();
        Assert.True(sim.Execute(new ApplyExtractionResult(run.ResultFor(1))).Ok);
        Assert.Equal(MissionStatus.Available, sim.State.Campaign.Progress("m2").Status);
    }

    [Fact]
    public void A_mission_failed_for_good_closes_as_failed_so_the_story_goes_on()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        foreach (var id in new[] { "m1", "m2", "m3" }) campaign.Progress(id).Status = MissionStatus.Done;
        sim.Build("med_bay");
        sim.Tick();
        var adult = sim.State.Survivors.First(s => !s.IsChild);
        sim.AddFood(50);
        Assert.True(sim.Execute(new LaunchStoryMission("m4", [new ExpeditionMember(adult.Id)])).Ok);
        sim.Advance(48);
        var run = sim.OpenExpeditionScavenge();
        var juniper = run.State.Units.Single(u => u.Tag == "juniper");
        juniper.Status = UnitStatus.Dead;
        MissionRules.Update(run);
        Assert.Equal(MissionOutcome.Failed, run.State.Mission!.Outcome);
        foreach (var u in run.State.Units.Where(u => u.PlayerId == 1)) u.Status = UnitStatus.Extracted;
        run.UpdateParties();
        Assert.True(sim.Execute(new ApplyExtractionResult(run.ResultFor(1))).Ok);
        sim.Advance(48);
        Assert.Equal(MissionStatus.Done, campaign.Progress("m4").Status);
        Assert.Equal("failed", campaign.Progress("m4").Outcome);
        Assert.Equal(1, campaign.Flag("marrow_grieving"));
        Assert.Equal(1, campaign.Flag("caravan_open"));
        sim.State.Buildings.Single(b => b.DefId == "town_hall").Level = 2;
        sim.Tick();
        Assert.Equal(MissionStatus.Available, campaign.Progress("m5").Status);
    }

    [Fact]
    public void Save_and_load_mid_campaign_and_mid_mission_round_trips()
    {
        var sim = NewStory();
        sim.State.Campaign!.Progress("m1").Status = MissionStatus.Done;
        sim.Tick();
        TakeNextCall(sim);
        Assert.True(LaunchM2(sim).Ok);
        var run = sim.OpenExpeditionScavenge();
        var u = run.State.CurrentUnit!;
        run.Execute(new EndTurn(1, u.Id));
        sim.State.Expedition!.Scavenge = run.State;

        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(TestData.Real, json, ScavengeTestData.Real);
        loaded.Story = Real;
        Assert.Equal(json, SaveGame.Save(loaded.State));
        Assert.Equal("m2", loaded.State.Expedition!.MissionId);
        var resumed = loaded.OpenExpeditionScavenge();
        Assert.Equal("relay_shed", resumed.State.Mission!.MapId);
        Assert.Contains(resumed.State.Mission.Chatter, c => c.Text.StartsWith("Husks walk"));

        var scavengeJson = ScavengeSave.Save(run.State);
        Assert.Equal(scavengeJson, ScavengeSave.Save(ScavengeSave.Load(ScavengeTestData.Real, scavengeJson).State));
    }

    [Fact]
    public void An_old_save_loads_as_sandbox()
    {
        var sim = NewGame.Create(TestData.Real, 3, ScavengeTestData.Real);
        var json = SaveGame.Save(sim.State).Replace("\"version\": 14", "\"version\": 12");
        Assert.Null(SaveGame.Load(TestData.Real, json, ScavengeTestData.Real).State.Campaign);
    }

    [Fact]
    public void The_perimeter_variant_follows_the_town_hall_and_watchtowers_raise_posts()
    {
        var sim = NewStory();
        var m5 = Real.Mission("m5")!;
        sim.State.Buildings.Single(b => b.DefId == "town_hall").Level = 2;
        Assert.Equal("perimeter_th2", Campaign.Setup(sim, "m5").Map!.Id);
        sim.State.Buildings.Single(b => b.DefId == "town_hall").Level = 3;
        Schematics.Schematics.Grant(sim, "sch_watchtower", Schematics.SchematicSource.Story);  // M2's reward
        sim.Build("watchtower");
        var setup = Campaign.Setup(sim, "m5");
        Assert.Equal("perimeter_th3", setup.Map!.Id);
        Assert.Equal(1, setup.Towers);
        var run = NewScavenge.Create(ScavengeTestData.Real, m5.Zone!, 1, [ScavengeTestData.Party(1, 4)], null, setup);
        Assert.Single(run.State.Mission!.Raised);
        var mast = run.State.Units.Single(u => u.Tag == "mast");
        Assert.Equal(run.State.Parties[0].Side, mast.Side);
    }

    private static CommandResult LaunchM2(ColonySim sim)
    {
        var adults = sim.State.Survivors.Where(s => !s.IsChild).Take(2).Select(s => new ExpeditionMember(s.Id)).ToList();
        return sim.Execute(new LaunchStoryMission("m2", adults));
    }
}

public class DialogueTests
{
    [Fact]
    public void Choices_apply_their_effects_and_go_to_their_line_and_everything_is_logged()
    {
        var sim = NewStory();
        var def = new DialogueDef
        {
            Id = "t", Caller = "cole", Title = "Test",
            Nodes =
            [
                new DialogueNode { Id = "a", Speaker = "cole", Text = "Well?", Choices =
                [
                    new ChoiceDef { Text = "Talk.", Next = "b", Effects = [new EffectDef { Kind = EffectKind.AddFlag, Id = "cole_respect", Value = 1 }] },
                    new ChoiceDef { Text = "Fight.", Next = "c", Effects = [new EffectDef { Kind = EffectKind.AddFlag, Id = "cole_respect", Value = -1 }] },
                    new ChoiceDef { Text = "Only friends.", Next = "b", Requires = [new ConditionDef { Flag = "cole_respect", Min = 2 }] },
                ] },
                new DialogueNode { Id = "b", Speaker = "cole", Text = "One you owe me.", Effects = [new EffectDef { Kind = EffectKind.Item, Id = "bandage", Value = 2 }] },
                new DialogueNode { Id = "c", Speaker = "cole", Text = "Your call." },
            ],
        };
        var story = new StoryData(Real.Characters.Values, Real.Missions, Real.Dialogues.Values.Append(def), Real.Maps.Values);
        sim.Story = story;
        Dialogue.Open(sim, def);
        var view = Dialogue.View(sim)!;
        Assert.Equal(2, view.Choices.Count);
        Assert.Equal(CommandError.InvalidChoice, sim.Execute(new ChooseDialogue("a", 2)).Error);
        Assert.Equal(CommandError.InvalidChoice, sim.Execute(new AdvanceDialogue()).Error);
        var bandages = sim.State.Inventory.Count("bandage");
        Assert.True(sim.Execute(new ChooseDialogue("a", 0)).Ok);
        Assert.Equal(1, sim.State.Campaign!.Flag("cole_respect"));
        Assert.Equal("b", Dialogue.View(sim)!.NodeId);
        Assert.Equal(bandages + 2, sim.State.Inventory.Count("bandage"));
        Assert.True(sim.Execute(new AdvanceDialogue()).Ok);
        Assert.Null(Dialogue.View(sim));
        Assert.Contains("t", sim.State.Campaign.Heard);
        Assert.Contains(sim.State.Campaign.Log, l => l.Choice && l.Text == "Talk.");
        Assert.Contains(sim.State.Campaign.Log, l => l.Text == "One you owe me." && l.Speaker == "Cole Brannigan");
    }

    [Fact]
    public void Conditional_lines_are_skipped_unless_they_hold()
    {
        var sim = NewStory();
        void Play(int respect)
        {
            sim.State.Campaign!.Flags["cole_respect"] = respect;
            sim.State.Campaign.Log.Clear();
            Dialogue.Open(sim, Real.Dialogues["m3_debrief"]);
            PlayOut(sim);
        }
        Play(1);
        Assert.Contains(sim.State.Campaign!.Log, l => l.Text.Contains("split it with the Tally"));
        Assert.DoesNotContain(sim.State.Campaign.Log, l => l.Text.Contains("on one now"));
        Play(-1);
        Assert.Contains(sim.State.Campaign.Log, l => l.Text.Contains("on one now"));
        Assert.DoesNotContain(sim.State.Campaign.Log, l => l.Text.Contains("split it"));
        Play(0);
        Assert.DoesNotContain(sim.State.Campaign.Log, l => l.Text.Contains("split it") || l.Text.Contains("on one now"));
    }

    [Fact]
    public void Placeholders_and_speakers_resolve_to_the_colonys_real_people()
    {
        var sim = NewStory();
        var leader = Dialogue.Leader(sim)!;
        Assert.Equal(leader.Name, Dialogue.Resolve(sim, "{survivor:leader}"));
        Assert.Equal("Pell", Dialogue.Resolve(sim, "{name:pell}"));
        Assert.Equal(sim.State.Survivors.Count.ToString(), Dialogue.Resolve(sim, "{colony.population}"));
        Assert.Equal("0", Dialogue.Resolve(sim, "{colony.dead_last_month}"));
        Assert.Equal("Iris Vahn", Dialogue.Resolve(sim, "{name:iris}"));
        Assert.Equal("{unknown:thing}", Dialogue.Resolve(sim, "{unknown:thing}"));
        Assert.Equal("Lantern", Dialogue.SpeakerName(sim, "iris"));
        Assert.Equal(sim.State.Survivors.Single(s => s.UniqueId == "pell").Id, Dialogue.SurvivorOf(sim, "pell")!.Id);
    }

    [Fact]
    public void Story_commands_fail_in_sandbox()
    {
        var sim = NewGame.Create(TestData.Real, 7, ScavengeTestData.Real);
        Assert.Equal(CommandError.NoCampaign, sim.Execute(new AdvanceDialogue()).Error);
    }
}

public class ObjectiveTests
{
    private static readonly string[] Open =
    [
        "##########",
        "#S.......#",
        "#........#",
        "#....a...#",
        "#........#",
        "#....h...#",
        "#E########",
    ];

    [Fact]
    public void Reach_passes_when_a_survivor_gets_there_and_triggers_fire_once()
    {
        var sim = Field(Open, new() { ["a"] = "spot" }, [new ObjectiveDef { Id = "go", Kind = ObjectiveKind.Reach, Marker = "spot", Text = "Go" }],
            [new TriggerDef { Id = "t", When = new TriggerWhen { Kind = TriggerKind.Enter, Marker = "spot", Radius = 2 },
                Actions = [new TriggerAction { Kind = TriggerActionKind.Say, Speaker = "Lantern", Text = "Close." }] }]);
        var me = sim.Mine();
        Assert.Equal(ObjectiveStatus.Active, sim.Status("go"));
        Assert.True(sim.Execute(new Move(1, me.Id, new Pos(3, 3))).Ok);
        Assert.Single(sim.State.Mission!.Chatter);
        Assert.Equal(ObjectiveStatus.Active, sim.Status("go"));
        Assert.True(sim.Execute(new Move(1, me.Id, new Pos(4, 3))).Ok);
        Assert.Equal(ObjectiveStatus.Done, sim.Status("go"));
        Assert.Single(sim.State.Mission.Chatter);
    }

    [Fact]
    public void Retrieve_counts_at_extraction_and_the_result_says_done()
    {
        var sim = Field(Open, new() { ["a"] = "crate" }, [new ObjectiveDef { Id = "get", Kind = ObjectiveKind.Retrieve, Item = "relay_antenna", Text = "Get" }],
            caches: [new MapCacheDef("crate", ["relay_antenna"])]);
        var me = sim.Mine();
        var cache = sim.State.Caches.Single();
        me.Pos = new Pos(4, 2);
        Assert.True(sim.Execute(new PickUp(1, me.Id, cache.Id, "relay_antenna")).Ok);
        Assert.Equal(1, sim.State.Mission!.Find("get")!.Progress);
        Assert.Equal(MissionOutcome.Incomplete, MissionRules.Result(sim.State, 1)!.Outcome);
        me.Pos = new Pos(1, 6);
        Assert.True(sim.Execute(new Extract(1, me.Id)).Ok);
        var result = sim.ResultFor(1);
        Assert.Equal(MissionOutcome.Done, result.Mission!.Outcome);
        Assert.Equal(ObjectiveStatus.Done, result.Mission.Objectives["get"]);
    }

    [Fact]
    public void Aid_then_escort_a_story_npc_who_comes_home_to_the_story_not_the_colony()
    {
        var sim = Field(Open, new() { ["a"] = "scout" },
            [
                new ObjectiveDef { Id = "aid", Kind = ObjectiveKind.Aid, Tag = "j", FailsMission = true },
                new ObjectiveDef { Id = "out", Kind = ObjectiveKind.Escort, Tag = "j", FailsMission = true },
            ],
            [new TriggerDef { Id = "f", When = new TriggerWhen { Kind = TriggerKind.ObjectiveDone, Objective = "aid" },
                Actions = [new TriggerAction { Kind = TriggerActionKind.Follow, Tag = "j" }] }],
            npcs: [new MapNpcDef("Juniper", "scout", "j", "juniper", 18, NoRecruit: true)]);
        var me = sim.Mine();
        var j = sim.State.Units.Single(u => u.Tag == "j");
        me.Pos = new Pos(4, 2);
        Assert.Equal(ScavengeError.NotRecruitable, sim.Execute(new Recruit(1, me.Id, j.Id)).Error);
        Assert.True(sim.Execute(new UseItem(1, me.Id, "med_kit", j.Id)).Ok);
        Assert.Equal(ObjectiveStatus.Done, sim.Status("aid"));
        Assert.Equal(me.Id, j.FollowingId);
        me.Pos = new Pos(1, 6);
        j.Pos = new Pos(1, 5);
        Assert.True(sim.Execute(new Extract(1, me.Id)).Ok);
        var result = sim.ResultFor(1);
        Assert.Empty(result.Recruits);
        Assert.Equal(["juniper"], result.Mission!.Escorted);
        Assert.Equal(MissionOutcome.Done, result.Mission.Outcome);
    }

    [Fact]
    public void An_escorted_truck_drives_on_but_an_escorted_stranger_joins_the_colony()
    {
        // M6's trucks follow the party (noRecruit on the map); M7's patient follows and joins. A follow keeps the map's word.
        var sim = Field(Open, new() { ["a"] = "scout", ["h"] = "stranger" }, [],
            [new TriggerDef { Id = "f", When = new TriggerWhen { Kind = TriggerKind.Start },
                Actions = [new TriggerAction { Kind = TriggerActionKind.Follow, Tag = "truck" }, new TriggerAction { Kind = TriggerActionKind.Follow, Tag = "patient" }] }],
            npcs: [new MapNpcDef("Cargo truck", "scout", "truck", NoRecruit: true), new MapNpcDef("Tomas", "stranger", "patient")]);
        MissionRules.Update(sim);
        var me = sim.Mine();
        var truck = sim.State.Units.Single(u => u.Tag == "truck");
        var patient = sim.State.Units.Single(u => u.Tag == "patient");
        Assert.Equal(me.Id, truck.FollowingId);
        Assert.Equal(me.Id, patient.FollowingId);
        me.Pos = new Pos(1, 6);
        truck.Pos = new Pos(1, 5);
        patient.Pos = new Pos(2, 5);
        Assert.True(sim.Execute(new Extract(1, me.Id)).Ok);
        Assert.Equal(["Tomas"], sim.ResultFor(1).Recruits.Select(r => r.Survivor.Name));
    }

    [Fact]
    public void Escort_fails_the_mission_when_the_escorted_unit_dies()
    {
        var sim = Field(Open, new() { ["a"] = "scout" }, [new ObjectiveDef { Id = "out", Kind = ObjectiveKind.Escort, Tag = "j", FailsMission = true }],
            npcs: [new MapNpcDef("Juniper", "scout", "j", "juniper")]);
        sim.State.Units.Single(u => u.Tag == "j").Status = UnitStatus.Dead;
        MissionRules.Update(sim);
        Assert.Equal(ObjectiveStatus.Failed, sim.Status("out"));
        Assert.Equal(MissionOutcome.Failed, sim.State.Mission!.Outcome);
        Assert.False(sim.State.Finished);
    }

    [Fact]
    public void Survive_counts_rounds_and_defend_at_home_ends_the_run_both_ways()
    {
        ScavengeSim Defense() => Field(Open, new() { ["a"] = "mast" },
            [
                new ObjectiveDef { Id = "hold", Kind = ObjectiveKind.Survive, Count = 3 },
                new ObjectiveDef { Id = "mast", Kind = ObjectiveKind.Defend, Tag = "mast", FailsMission = true },
            ], structures: [new MapStructureDef("Radio Mast", "mast", 50, "mast")], atHome: true);

        var win = Defense();
        win.RunTo(3);
        Assert.Equal(ObjectiveStatus.Active, win.Status("hold"));
        win.RunTo(4);
        Assert.True(win.State.Finished);
        var result = win.ResultFor(1);
        Assert.Equal(MissionOutcome.Done, result.Mission!.Outcome);
        Assert.Single(result.Returned);

        var lose = Defense();
        lose.State.Units.Single(u => u.Tag == "mast").Status = UnitStatus.Dead;
        MissionRules.Update(lose);
        Assert.True(lose.State.Finished);
        Assert.Equal(MissionOutcome.Failed, lose.ResultFor(1).Mission!.Outcome);
        Assert.Single(lose.ResultFor(1).Returned);
    }

    [Fact]
    public void Hack_passes_on_the_tagged_device_only()
    {
        var sim = Field(Open, new() { ["a"] = "t", ["h"] = "l" },
            [new ObjectiveDef { Id = "hack", Kind = ObjectiveKind.Hack, Tag = "terminal" }],
            devices: [new MapDeviceDef(DeviceKind.Terminal, "t", "terminal", ["scrap"]), new MapDeviceDef(DeviceKind.Locker, "l", "locker")]);
        var side = sim.State.Parties[0].Side;
        sim.State.Devices.Single(d => d.Tag == "locker").Controller = side;
        MissionRules.Update(sim);
        Assert.Equal(ObjectiveStatus.Active, sim.Status("hack"));
        sim.State.Devices.Single(d => d.Tag == "terminal").Controller = side;
        MissionRules.Update(sim);
        Assert.Equal(ObjectiveStatus.Done, sim.Status("hack"));
    }

    [Fact]
    public void Kill_tag_counts_only_tagged_dead()
    {
        var sim = Field(Open, new() { ["h"] = "h", ["a"] = "x" }, [new ObjectiveDef { Id = "k", Kind = ObjectiveKind.KillTag, Tag = "boss", Count = 1 }],
            enemies: [new MapEnemyDef("husk", "h", "boss"), new MapEnemyDef("husk", "x")]);
        sim.State.Units.Single(u => u.EnemyDefId == "husk" && u.Tag is null).Status = UnitStatus.Dead;
        MissionRules.Update(sim);
        Assert.Equal(ObjectiveStatus.Active, sim.Status("k"));
        sim.State.Units.Single(u => u.Tag == "boss").Status = UnitStatus.Dead;
        MissionRules.Update(sim);
        Assert.Equal(ObjectiveStatus.Done, sim.Status("k"));
    }

    [Fact]
    public void Undetected_fails_the_moment_a_hostile_picks_out_the_party()
    {
        var sim = Field(
        [
            "##########",
            "#S.......#",
            "#E########",
            "#....h...#",
            "##########",
        ], new() { ["h"] = "h" }, [new ObjectiveDef { Id = "sneak", Kind = ObjectiveKind.Undetected, FailsMission = true }],
            enemies: [new MapEnemyDef("husk", "h")]);
        var me = sim.Mine();
        // A wall between them: nobody sees anybody yet.
        Assert.Equal(ObjectiveStatus.Active, sim.Status("sneak"));
        me.Pos = new Pos(4, 3);
        MissionRules.Update(sim);
        Assert.Equal(ObjectiveStatus.Failed, sim.Status("sneak"));
        Assert.Equal(MissionOutcome.Failed, sim.State.Mission!.Outcome);
    }

    [Fact]
    public void Round_triggers_spawn_and_a_prompt_blocks_until_answered_then_branches()
    {
        var sim = Field(Open, new() { ["a"] = "door", ["h"] = "x" }, [new ObjectiveDef { Id = "deal", Kind = ObjectiveKind.Reach, Marker = "none", Optional = true, Hidden = true }],
            [
                new TriggerDef { Id = "come", When = new TriggerWhen { Kind = TriggerKind.Round, Round = 2 }, Actions =
                [
                    new TriggerAction { Kind = TriggerActionKind.Spawn, Enemy = "tally_scav", Count = 2, Marker = "door", Tag = "tally", Side = "neutral" },
                ] },
                new TriggerDef { Id = "seen", When = new TriggerWhen { Kind = TriggerKind.Sees, Tag = "tally" }, Actions =
                [
                    new TriggerAction { Kind = TriggerActionKind.Activate, Objective = "deal" },
                    new TriggerAction { Kind = TriggerActionKind.Prompt, Prompt = new PromptDef { Id = "p", Speaker = "Cole", Text = "Well?",
                        Options = [new PromptOption("talk", "Talk"), new PromptOption("fight", "Fight")] } },
                ] },
                new TriggerDef { Id = "fought", When = new TriggerWhen { Kind = TriggerKind.Chosen, Choice = "p:fight" }, Actions =
                [
                    new TriggerAction { Kind = TriggerActionKind.SetSide, Tag = "tally", Side = "hostile" },
                    new TriggerAction { Kind = TriggerActionKind.Flag, Flag = "cole_respect", Value = -1, Add = true },
                    new TriggerAction { Kind = TriggerActionKind.Complete, Objective = "deal" },
                ] },
            ]);
        sim.RunTo(2);
        var tally = sim.State.Units.Where(u => u.Tag == "tally").ToList();
        Assert.Equal(2, tally.Count);
        Assert.All(tally, u => Assert.Equal(Unit.NeutralSide, u.Side));
        Assert.NotNull(sim.State.Mission!.Pending);
        Assert.Equal(ObjectiveStatus.Active, sim.Status("deal"));
        var me = sim.State.CurrentUnit!;
        Assert.Equal(ScavengeError.ChoicePending, sim.Execute(new EndTurn(1, me.Id)).Error);
        Assert.Equal(ScavengeError.NoPrompt, sim.Execute(new ChooseOption(1, me.Id, "p", "dance")).Error);
        Assert.True(sim.Execute(new ChooseOption(1, me.Id, "p", "fight")).Ok);
        Assert.All(tally, u => Assert.Equal("band:tally", u.Side));
        Assert.True(me.HostileTo(tally[0]));
        Assert.Equal(ObjectiveStatus.Done, sim.Status("deal"));
        Assert.Equal(-1, MissionRules.Result(sim.State, 1)!.Flags["cole_respect"]);
        Assert.Equal("fight", sim.State.Mission.Choices["p"]);
        // The spawn trigger does not fire again.
        sim.RunTo(4);
        Assert.Equal(2, sim.State.Units.Count(u => u.Tag == "tally"));
    }

    [Fact]
    public void A_wiped_party_fails_the_mission()
    {
        var sim = Field(Open, null, [new ObjectiveDef { Id = "go", Kind = ObjectiveKind.Reach, Marker = "none" }]);
        var me = sim.Mine();
        me.Status = UnitStatus.Dead;
        sim.UpdateParties();
        Assert.Equal(MissionOutcome.Failed, sim.ResultFor(1).Mission!.Outcome);
    }

    [Fact]
    public void Patrols_walk_their_route_while_idle_and_the_same_seed_plays_the_same()
    {
        string Play()
        {
            var sim = Field(
            [
                "##############",
                "#SE###########",
                "##############",
                "#............#",
                "#............#",
                "#p....h.....q#",
                "##############",
            ], new() { ["p"] = "w", ["q"] = "e", ["h"] = "h" }, [new ObjectiveDef { Id = "go", Kind = ObjectiveKind.Reach, Marker = "none" }],
                enemies: [new MapEnemyDef("husk", "h", "patrol", Patrol: ["w", "e"])]);
            var husk = sim.State.Units.Single(u => u.Tag == "patrol");
            var start = husk.Pos;
            sim.RunTo(2);
            Assert.True(husk.Pos.X < start.X);
            return ScavengeSave.Save(sim.State);
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Story_calls_ring_on_the_radio_mast_and_answering_there_opens_the_dialogue()
    {
        var sim = NewStory();
        var call = Campaign.QueueCall(sim, Campaign.Waiting(sim).FirstOrDefault()?.DialogueId ?? sim.Story!.Dialogues.Keys.First());
        var radio = Assert.Single(Remnants.Core.Schematics.RadioMast.Calls(sim), c => c.Tag == Campaign.CallTagPrefix + call.Id);
        Assert.Equal(Remnants.Core.Schematics.RadioCallKind.Story, radio.Kind);

        Assert.True(sim.Execute(new Remnants.Core.Schematics.AnswerCall(radio.Id)).Ok);

        Assert.True(call.Taken);
        Assert.NotNull(sim.State.Campaign!.Active);
        Assert.DoesNotContain(Remnants.Core.Schematics.RadioMast.Calls(sim), c => c.Tag == Campaign.CallTagPrefix + call.Id);
    }

    [Fact]
    public void Taking_a_call_from_the_story_hud_clears_it_from_the_radio_and_delayed_calls_ring_later()
    {
        var sim = NewStory();
        var dialogue = sim.Story!.Dialogues.Keys.First();
        var later = Campaign.QueueCall(sim, dialogue, delayTicks: 5);
        Assert.DoesNotContain(Remnants.Core.Schematics.RadioMast.Calls(sim), c => c.Tag == Campaign.CallTagPrefix + later.Id);
        for (var i = 0; i < 6; i++) sim.Tick();
        Assert.Contains(Remnants.Core.Schematics.RadioMast.Calls(sim), c => c.Tag == Campaign.CallTagPrefix + later.Id);
        Assert.True(sim.Execute(new TakeCall(later.Id)).Ok);
        Assert.DoesNotContain(Remnants.Core.Schematics.RadioMast.Calls(sim), c => c.Tag == Campaign.CallTagPrefix + later.Id);
    }

    [Fact]
    public void Every_story_schematic_reward_is_in_the_catalogue()
    {
        var catalog = TestData.Real.Schematics;
        var dir = Path.Combine(AppContext.BaseDirectory, "data", "story");
        var ids = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\"(sch_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.NotNull(catalog.Find(id)));
    }
}
