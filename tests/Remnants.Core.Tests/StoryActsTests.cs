using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Story;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.StoryTestData;

namespace Remnants.Core.Tests;

/// <summary>Acts 2–4: unique survivors, choices with long-term effects, the Offer, chains, endings, the Codex.</summary>
public class StoryActsTests
{
    private static ColonySim Joined(params string[] who)
    {
        var sim = NewStory();
        foreach (var id in who) Assert.NotNull(StoryCharacters.Join(sim, id));
        return sim;
    }

    /// <summary>Every mission before <paramref name="upTo"/> (in campaign order) done, so its own requirements can be tested.</summary>
    private static void DoneUpTo(ColonySim sim, string upTo)
    {
        foreach (var m in Real.Missions)
        {
            if (m.Id == upTo) break;
            var p = sim.State.Campaign!.Progress(m.Id);
            p.Status = MissionStatus.Done;
            p.Outcome = "done";
        }
    }

    /// <summary>Plays a dialogue from its start, picking on each choice line the first choice whose text contains one of the wants.</summary>
    private static void Play(ColonySim sim, string dialogueId, params string[] wants)
    {
        Dialogue.Open(sim, Real.Dialogues[dialogueId]);
        for (var i = 0; i < 200 && Dialogue.View(sim) is { } view; i++)
        {
            if (view.Choices.Count == 0) { Assert.True(sim.Execute(new AdvanceDialogue()).Ok); continue; }
            var pick = wants.Select(w => view.Choices.FirstOrDefault(c => c.Text.Contains(w, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(c => c.Text is not null);
            if (pick.Text is null) pick = view.Choices[0];
            Assert.True(sim.Execute(new ChooseDialogue(view.NodeId, pick.Index)).Ok);
        }
        Assert.Null(Dialogue.View(sim));
    }

    private static void Days(ColonySim sim, int days)
    {
        for (var i = 0; i < days * sim.Data.Rules.TicksPerDay; i++) sim.Tick();
    }

    // ---- unique survivors -------------------------------------------------------------------------------------------

    [Fact]
    public void A_character_joins_as_a_unique_survivor_with_stats_perks_and_trait_hooks_and_stays()
    {
        var sim = Joined("cole");
        var cole = StoryCharacters.SurvivorOf(sim, "cole")!;
        Assert.Equal("Cole", cole.Name);
        Assert.Equal(Sex.Male, cole.Sex);
        Assert.Equal(50, cole.Get(Skill.Firearms));
        Assert.Contains("steady_aim", cole.Perks);
        Assert.Contains("quartermaster", cole.Traits);
        Assert.True(cole.TraitHooks[PerkHooks.MoodFoodSurplus] > 0);
        Days(sim, 3);
        Assert.Same(cole, StoryCharacters.SurvivorOf(sim, "cole"));
        Assert.False(sim.State.Campaign!.Character("cole").Dead);
    }

    [Fact]
    public void Trait_hooks_count_like_perk_hooks_at_home_and_in_the_field()
    {
        var sim = Joined("hale", "seven");
        var doc = StoryCharacters.SurvivorOf(sim, "hale")!;
        Assert.True(PerkEffects.For(doc, sim.Data).Hook(PerkHooks.SurgerySuccessBonus) >= 0.2);
        var seven = StoryCharacters.SurvivorOf(sim, "seven")!;
        var party = new PartySetup(1, [new PartyMember(seven, new())]);
        var run = NewScavenge.Create(ScavengeTestData.Real, "outskirts", 1, [party]);
        var unit = run.State.Units.Single(u => u.PlayerId == 1);
        Assert.Equal(2, unit.Hook(PerkHooks.HackRange));
        // Ghost in the Wire: it hacks without a kit.
        Assert.NotNull(Hacking.Tool(ScavengeTestData.Real, unit));
    }

    [Fact]
    public void Seven_starts_at_zero_humanity_grows_back_to_its_cap_and_unsettles_the_others_until_thirty()
    {
        var sim = Joined("seven");
        var seven = StoryCharacters.SurvivorOf(sim, "seven")!;
        Assert.Equal(0, seven.Body.Humanity);
        var other = sim.State.Survivors.First(s => s.UniqueId is null && !s.IsChild);
        other.Mood = 80;
        Days(sim, 2);
        Assert.InRange(seven.Body.Humanity, 0.9, 1.1);
        Assert.True(other.Mood < 80);
        seven.Body.HumanityLoss = 41;
        Days(sim, 10);
        Assert.Equal(StoryCharacters.HumanityGrowthCap, seven.Body.Humanity, 3);
    }

    [Fact]
    public void Docs_oath_warns_at_the_first_network_grade_limb_and_she_leaves_at_the_second()
    {
        var sim = Joined("hale");
        var campaign = sim.State.Campaign!;
        var a = sim.State.Survivors.First(s => s.UniqueId is null && !s.IsChild);
        a.Body.Fit(BodyPart.LeftArm, 1, "network_arm");
        sim.Tick();
        Assert.Equal(1, campaign.Flag(StoryCharacters.DocT4Count));
        Assert.Contains(campaign.Inbox, c => c.DialogueId == "doc_oath_warn");
        Assert.NotNull(StoryCharacters.SurvivorOf(sim, "hale"));
        sim.Tick();
        Assert.Equal(1, campaign.Flag(StoryCharacters.DocT4Count));
        var b = sim.State.Survivors.Last(s => s.UniqueId is null && !s.IsChild);
        b.Body.Fit(BodyPart.RightLeg, 1, "network_leg");
        sim.Tick();
        Assert.Null(StoryCharacters.SurvivorOf(sim, "hale"));
        Assert.Equal(3, campaign.Flag("doc_state"));
        Assert.True(campaign.Character("hale").Left);
        Assert.False(campaign.Character("hale").Dead);
        Assert.Contains(campaign.Inbox, c => c.DialogueId == "doc_oath_leave");
    }

    [Fact]
    public void A_joined_character_who_dies_is_marked_dead_and_one_who_walks_away_only_left()
    {
        var sim = Joined("iris", "cole");
        var campaign = sim.State.Campaign!;
        var iris = StoryCharacters.SurvivorOf(sim, "iris")!;
        sim.State.Survivors.Remove(iris);
        sim.State.Memorial.Add(new(iris.Id, iris.Name, sim.State.Tick, "Died"));
        sim.State.Survivors.Remove(StoryCharacters.SurvivorOf(sim, "cole")!);
        sim.Tick();
        Assert.True(campaign.Character("iris").Dead);
        Assert.Equal(1, campaign.Flag("iris_dead"));
        Assert.False(campaign.Character("cole").Dead);
        Assert.True(campaign.Character("cole").Left);
    }

    [Fact]
    public void Iris_stays_home_from_ordinary_runs_but_goes_where_the_story_needs_her()
    {
        var sim = Joined("iris");
        var campaign = sim.State.Campaign!;
        var iris = StoryCharacters.SurvivorOf(sim, "iris")!;
        var other = sim.State.Survivors.First(s => s.UniqueId is null && !s.IsChild);
        Assert.NotNull(Campaign.CannotTake(sim, Real.Mission("m13")!, [iris.Id, other.Id]));
        campaign.Flags["key_carrier"] = 1;
        Assert.Null(Campaign.CannotTake(sim, Real.Mission("m16")!, [iris.Id, other.Id]));
        Assert.Contains("must be in the party", Campaign.CannotTake(sim, Real.Mission("m16")!, [other.Id]));
    }

    // ---- conditions, effects, placeholders --------------------------------------------------------------------------

    [Fact]
    public void Conditions_read_humanity_characters_skills_and_combine_with_any_and_not()
    {
        var sim = Joined("seven");
        Assert.False(Campaign.Holds(sim, new ConditionDef { HumanityMin = 50, Of = "seven" }));
        Assert.True(Campaign.Holds(sim, new ConditionDef { HumanityMax = 30, Of = "seven" }));
        Assert.True(Campaign.Holds(sim, new ConditionDef { Joined = "seven" }));
        Assert.False(Campaign.Holds(sim, new ConditionDef { Joined = "iris" }));
        Assert.False(Campaign.Holds(sim, new ConditionDef { Adult = Campaign.PellId }));
        Assert.True(Campaign.Holds(sim, new ConditionDef { Alive = Campaign.PellId }));
        Assert.True(Campaign.Holds(sim, new ConditionDef { Skill = "Hacking", SkillMin = 50 }));
        Assert.True(Campaign.Holds(sim, new ConditionDef { Any = [new() { Joined = "iris" }, new() { Joined = "seven" }] }));
        Assert.True(Campaign.Holds(sim, new ConditionDef { Not = new() { Dead = "marrow" } }));
        // The colony's own humanity: Seven pulls the average down.
        var average = StoryCharacters.ColonyHumanity(sim);
        Assert.True(average < 100);
        Assert.True(Campaign.Holds(sim, new ConditionDef { HumanityMax = (int)Math.Ceiling(average) }));
    }

    [Fact]
    public void Story_effects_change_people_missions_and_the_roster()
    {
        var sim = Joined("hale");
        var campaign = sim.State.Campaign!;
        var doc = StoryCharacters.SurvivorOf(sim, "hale")!;
        doc.Mood = 50;
        Campaign.Apply(sim, new EffectDef { Kind = EffectKind.Mood, Id = "hale", Value = -15 }, "test");
        Assert.Equal(35, doc.Mood);
        Campaign.Apply(sim, new EffectDef { Kind = EffectKind.Humanity, Id = "hale", Value = -20 }, "test");
        Assert.Equal(77, doc.Body.Humanity, 3);
        var before = sim.State.Survivors.Count;
        Campaign.Apply(sim, new EffectDef { Kind = EffectKind.Recruit, Id = "child", Text = "Mira", Value = 60 }, "test");
        var mira = sim.State.Survivors.Single(s => s.Name == "Mira");
        Assert.True(mira.IsChild);
        Assert.Equal(60, mira.Body.Humanity, 3);
        Assert.Equal(before + 1, sim.State.Survivors.Count);
        Campaign.Apply(sim, new EffectDef { Kind = EffectKind.Close, Id = "m14", Text = "offer" }, "test");
        Assert.Equal("offer", campaign.Progress("m14").Outcome);
        Assert.True(Campaign.Holds(sim, new ConditionDef { Mission = "m14", Outcome = "offer" }));
        Campaign.Apply(sim, new EffectDef { Kind = EffectKind.Dies, Id = "hale" }, "test");
        Assert.Null(StoryCharacters.SurvivorOf(sim, "hale"));
        Assert.Contains(sim.State.Memorial, m => m.Name == "Doc Hale");
    }

    [Fact]
    public void Placeholders_name_marrows_successor_sevens_chosen_name_and_pronouns()
    {
        var sim = Joined("seven");
        Assert.Equal("Abel Marrow runs it.", Dialogue.Resolve(sim, "{name:marrow} runs it."));
        sim.State.Campaign!.Character("marrow").Dead = true;
        Assert.Equal("Dee runs it.", Dialogue.Resolve(sim, "{name:marrow} runs it."));
        Assert.Equal("Seven said it was its kitchen.", Dialogue.Resolve(sim, "{seven} said {seven.they} was {seven.their} kitchen."));
        Play(sim, "sd_seven_name", "Pick one yourself", "We could say 'they'");
        Assert.Equal("Tile said they was their kitchen.", Dialogue.Resolve(sim, "{seven} said {seven.they} was {seven.their} kitchen."));
        Assert.Equal(1, sim.State.Campaign.Flag("seven_named"));
        Assert.Equal(10, StoryCharacters.SurvivorOf(sim, "seven")!.Body.Humanity, 3);
    }

    // ---- Halcyon's Offer, world effects, the truce --------------------------------------------------------------------

    [Fact]
    public void Accepting_the_offer_closes_m14_starts_a_sixty_day_truce_and_angers_iris()
    {
        var sim = Joined("iris");
        DoneUpTo(sim, "m14");
        var campaign = sim.State.Campaign!;
        var iris = StoryCharacters.SurvivorOf(sim, "iris")!;
        iris.Mood = 80;
        Play(sim, "m14_open", "Sixty days");
        Assert.Equal(1, campaign.Flag(StoryWorld.OfferFlag));
        Assert.Equal("offer", campaign.Progress("m14").Outcome);
        Assert.True(StoryWorld.Active(sim, StoryWorld.Truce));
        Assert.Contains("sch_lab_3", sim.State.Schematics.Known);
        Assert.Equal(60, iris.Mood);
        var ruins = StoryWorld.Conditions(sim, "suburb_ruins", new ZoneConditions());
        Assert.Equal(0, ruins.NetworkWeight);
        Assert.Equal(1, StoryWorld.Conditions(sim, "network_relay", new ZoneConditions()).NetworkWeight);
        Days(sim, 61);
        Assert.False(StoryWorld.Active(sim, StoryWorld.Truce));
        Assert.Equal(1, campaign.Flag(StoryWorld.OfferFlag));
    }

    [Fact]
    public void Going_to_the_relay_during_the_truce_breaks_it_and_halcyon_comes_first()
    {
        var sim = Joined("hale");
        DoneUpTo(sim, "m15");
        var campaign = sim.State.Campaign!;
        campaign.Progress("m14b").Status = MissionStatus.Locked;
        campaign.Progress("m14b").Outcome = null;
        Play(sim, "m14_open", "Sixty days");
        sim.State.Buildings.Add(new Building { Id = sim.State.NextBuildingId++, DefId = "med_bay", Level = 2, Plot = 40 });
        sim.Tick();
        Assert.Equal(MissionStatus.Available, campaign.Progress("m15").Status);
        var party = sim.State.Survivors.Where(s => !s.IsChild && s.UniqueId is null).Take(2).Select(s => new ExpeditionMember(s.Id)).ToList();
        var launched = sim.Execute(new LaunchStoryMission("m15", party));
        Assert.False(launched.Ok);
        Assert.Contains("truce", launched.Message);
        Assert.Equal(3, campaign.Flag(StoryWorld.OfferFlag));
        Assert.False(StoryWorld.Active(sim, StoryWorld.Truce));
        sim.Tick();
        Assert.Equal(MissionStatus.Available, campaign.Progress("m14b").Status);
        // M15 waits for Halcyon's answer to be fought off.
        campaign.Progress("m15").Status = MissionStatus.Locked;
        sim.Tick();
        Assert.Equal(MissionStatus.Locked, campaign.Progress("m15").Status);
    }

    [Fact]
    public void World_effects_shape_the_zones_relay_down_for_a_month_and_the_endings_for_good()
    {
        var sim = NewStory();
        Campaign.Apply(sim, new EffectDef { Kind = EffectKind.WorldEffect, Id = "relay_down", Value = 30 }, "test");
        Assert.Equal(0.5, StoryWorld.Conditions(sim, "flooded_mall", new ZoneConditions()).NetworkWeight);
        Assert.Equal(1, StoryWorld.Conditions(sim, "rail_depot", new ZoneConditions()).NetworkWeight);
        Days(sim, 31);
        Assert.Equal(1, StoryWorld.Conditions(sim, "flooded_mall", new ZoneConditions()).NetworkWeight);
        Campaign.Apply(sim, new EffectDef { Kind = EffectKind.Ending, Id = "silence" }, "test");
        var after = StoryWorld.Conditions(sim, "core_spire", new ZoneConditions());
        Assert.Equal(0, after.NetworkWeight);
        Assert.Equal(1.5, after.HuskCountMultiplier);
        // In the field: a zone without its Network enemies.
        var party = ScavengeTestData.Party(1, 2);
        var run = NewScavenge.Create(ScavengeTestData.Real, "network_relay", 3, [party], after);
        Assert.DoesNotContain(run.State.Units, u => u.EnemyDefId is { } e && ScavengeTestData.Real.Enemies[e].Tags.Contains("network") && u.Kind == UnitKind.Enemy);
        var terms = NewScavenge.Create(ScavengeTestData.Real, "network_relay", 3, [party], new ZoneConditions { NetworkNeutral = true });
        Assert.All(terms.State.Units.Where(u => u.Kind == UnitKind.Enemy && ScavengeTestData.Real.Enemies[u.EnemyDefId!].Tags.Contains("network")),
            u => Assert.Equal(Unit.NeutralSide, u.Side));
    }

    // ---- missions: unlocks, repeat, chain ---------------------------------------------------------------------------

    [Fact]
    public void Acts_unlock_in_order_and_m6_waits_for_two_caravan_visits()
    {
        var sim = NewStory();
        DoneUpTo(sim, "m6");
        var campaign = sim.State.Campaign!;
        campaign.Flags["caravan_open"] = 1;
        sim.Tick();
        Assert.Equal(MissionStatus.Locked, campaign.Progress("m6").Status);
        sim.State.Radio.Visits += 2;
        sim.Tick();
        Assert.Equal(2, campaign.Flag("marrow_visits"));
        Assert.Equal(MissionStatus.Available, campaign.Progress("m6").Status);
        Assert.Contains(campaign.Inbox, c => c.DialogueId == "m6_open");
    }

    [Fact]
    public void Docs_rounds_comes_back_ten_days_after_it_is_done()
    {
        var sim = Joined("hale");
        var campaign = sim.State.Campaign!;
        var def = Real.Mission("side_docs_rounds")!;
        var p = campaign.Progress(def.Id);
        p.Status = MissionStatus.Active;
        Campaign.Returned(sim, def.Id, new ObjectiveResult(def.Id, MissionOutcome.Done, new(), new(), new(), []));
        Assert.Equal(MissionStatus.Done, p.Status);
        Assert.Equal(1, campaign.Flag("rounds_done"));
        Days(sim, 9);
        Assert.Equal(MissionStatus.Done, p.Status);
        Days(sim, 2);
        Assert.Equal(MissionStatus.Available, p.Status);
    }

    [Fact]
    public void A_spire_floor_takes_only_who_came_out_of_the_floor_below()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        campaign.Flags["key_carrier"] = 3;
        campaign.Flags["key_survivor"] = adults[0].Id;
        var gate = Real.Mission("m17_gate")!;
        campaign.Progress(gate.Id).Status = MissionStatus.Active;
        Campaign.Returned(sim, gate.Id, new ObjectiveResult(gate.Id, MissionOutcome.Done, new(), new(), new(), []), [adults[0].Id]);
        Assert.Equal([adults[0].Id], campaign.ChainParties[gate.Id]);
        var choir = Real.Mission("m17_choir")!;
        Assert.Null(Campaign.CannotTake(sim, choir, [adults[0].Id]));
        Assert.NotNull(Campaign.CannotTake(sim, choir, [adults[0].Id, adults[1].Id]));
    }

    [Fact]
    public void Objective_outcomes_come_home_as_flags_and_rewards_read_them()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        var m6 = Real.Mission("m6")!;
        campaign.Progress("m6").Status = MissionStatus.Active;
        var statuses = new SortedDictionary<string, ObjectiveStatus>(StringComparer.Ordinal)
        {
            ["o_people"] = ObjectiveStatus.Done, ["o_cargo"] = ObjectiveStatus.Failed, ["o_marrow"] = ObjectiveStatus.Failed, ["o_out"] = ObjectiveStatus.Done,
        };
        var flags = new SortedDictionary<string, int>(StringComparer.Ordinal) { ["marrow_dead"] = 1 };
        Campaign.Returned(sim, "m6", new ObjectiveResult("m6", MissionOutcome.Done, statuses, flags, new(), []));
        Assert.Equal(1, campaign.Flag("m6_saved"));
        Assert.Equal(1, campaign.Flag("convoy_ready"));
        Assert.True(campaign.Character("marrow").Dead);
        Assert.Contains("sch_motorbike", sim.State.Schematics.Known);
    }

    [Fact]
    public void Doc_dying_at_the_clinic_closes_m7_for_good_with_her_notes_paid()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        campaign.Progress("m7").Status = MissionStatus.Active;
        var flags = new SortedDictionary<string, int>(StringComparer.Ordinal) { ["doc_died"] = 1 };
        Campaign.Returned(sim, "m7", new ObjectiveResult("m7", MissionOutcome.Failed, new(), flags, new(), []));
        var p = campaign.Progress("m7");
        Assert.Equal(MissionStatus.Done, p.Status);
        Assert.Equal("failed", p.Outcome);
        Assert.Null(p.RetryTick);
        Assert.Equal(2, campaign.Flag("doc_state"));
        Assert.Contains("sch_mech_limbs", sim.State.Schematics.Known);
        Assert.Contains(campaign.Inbox, c => c.DialogueId == "m7_lost");
    }

    // ---- endings, epilogue, Codex -----------------------------------------------------------------------------------

    [Fact]
    public void Endings_open_by_carrier_humanity_seven_and_the_offer_and_the_calm_stays_hidden()
    {
        var sim = Joined("iris");
        var campaign = sim.State.Campaign!;
        string[] Open() => StoryWorld.Endings(sim).Where(e => e.Open).Select(e => e.Def.Id).ToArray();
        Assert.Equal(["silence"], Open());
        Assert.DoesNotContain(StoryWorld.Endings(sim), e => e.Def.Id == "calm");
        campaign.Flags["key_carrier"] = 1;
        Assert.Contains("terms", Open());
        campaign.Flags["seven_fate"] = 2;
        Assert.Contains("voice", Open());
        campaign.Flags["seven_fate"] = 3;
        campaign.Character("seven").Dead = true;
        Assert.DoesNotContain("voice", Open());
        campaign.Flags[StoryWorld.OfferFlag] = 1;
        foreach (var s in sim.State.Survivors.Where(s => !s.IsChild)) s.Body.HumanityLoss = 90;
        Assert.Contains("calm", Open());
        Assert.DoesNotContain("terms", Open());
    }

    [Fact]
    public void The_core_scene_sets_the_ending_and_the_epilogue_reads_the_campaign()
    {
        var sim = Joined("iris");
        var campaign = sim.State.Campaign!;
        campaign.Flags["key_carrier"] = 1;
        campaign.Flags["m6_saved"] = 2;
        Play(sim, "m17_core_scene", "Ask them");
        Assert.Equal("terms", campaign.Ending);
        Assert.Equal(2, campaign.Flag("ending"));
        Assert.False(campaign.Over);
        Assert.True(StoryWorld.Active(sim, "terms"));
        var slides = StoryWorld.Epilogue(sim);
        Assert.Contains(slides, s => s.Text.Contains("Iris carried the question"));
        Assert.Contains(slides, s => s.Text.StartsWith("Marrow hired"));
        Assert.Equal("title", slides[^1].Still);
        Assert.DoesNotContain(slides, s => s.Text.Contains('{'));
    }

    [Fact]
    public void The_calm_ends_the_game()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        campaign.Flags[StoryWorld.OfferFlag] = 1;
        foreach (var s in sim.State.Survivors.Where(s => !s.IsChild)) s.Body.HumanityLoss = 90;
        Play(sim, "m17_core_scene", "another way", "Let it in");
        Assert.Equal("calm", campaign.Ending);
        Assert.True(campaign.Over);
        Assert.Contains(StoryWorld.Epilogue(sim), s => s.Text.Contains("Nobody was hungry"));
    }

    [Fact]
    public void The_second_voice_takes_seven_into_the_spire()
    {
        var sim = Joined("seven");
        var campaign = sim.State.Campaign!;
        campaign.Flags["seven_fate"] = 1;
        campaign.Flags["key_carrier"] = 2;
        Play(sim, "m17_core_scene", "another way", "Seven goes", "someone else");
        Assert.Equal("voice", campaign.Ending);
        Assert.Equal(2, campaign.Flag("uploaded"));
        Assert.Null(StoryCharacters.SurvivorOf(sim, "seven"));
        Assert.True(StoryWorld.Active(sim, "second_voice"));
    }

    [Fact]
    public void Codex_pages_unlock_with_the_story_and_grow_with_it()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        Assert.DoesNotContain(StoryWorld.Codex(sim), c => c.Def.Id == "log_iris_3");
        campaign.Flags["log_iris_3"] = 1;
        Assert.Contains(StoryWorld.Codex(sim), c => c.Def.Id == "log_iris_3" && c.Text.Contains("lantern"));
        campaign.Progress("m10").Status = MissionStatus.Done;
        campaign.Flags["seven_fate"] = 3;
        var seven = StoryWorld.Codex(sim).Single(c => c.Def.Id == "cx_seven");
        Assert.Contains("Asked if it was night", seven.Text);
    }

    // ---- the taken, saves ---------------------------------------------------------------------------------------------

    [Fact]
    public void Survivors_the_linked_take_leave_the_colony_for_the_story_and_terms_brings_them_home()
    {
        var sim = NewStory();
        var campaign = sim.State.Campaign!;
        var taken = sim.State.Survivors.First(s => !s.IsChild);
        StoryWorld.TakeAway(sim, [taken.Id]);
        Assert.DoesNotContain(taken, sim.State.Survivors);
        Assert.DoesNotContain(sim.State.Memorial, m => m.SurvivorId == taken.Id);
        Assert.Equal(1, campaign.Flag("taken"));
        Assert.Equal(taken.Name, Dialogue.Resolve(sim, "{survivor:taken_1}"));
        var names = StoryWorld.ReturnTaken(sim, 20);
        Assert.Equal([taken.Name], names);
        var back = sim.State.Survivors.Single(s => s.Name == taken.Name);
        Assert.Equal(80, back.Body.Humanity, 3);
        Assert.Empty(campaign.Taken);
    }

    [Fact]
    public void Save_and_load_keeps_unique_survivors_traits_world_effects_and_the_taken()
    {
        var sim = Joined("seven", "hale");
        Campaign.Apply(sim, new EffectDef { Kind = EffectKind.WorldEffect, Id = StoryWorld.Truce, Value = 60 }, "test");
        StoryWorld.TakeAway(sim, [sim.State.Survivors.First(s => !s.IsChild && s.UniqueId is null).Id]);
        sim.State.Campaign!.ChainParties["m17_gate"] = [1, 2];
        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(TestData.Real, json, ScavengeTestData.Real);
        loaded.Story = Real;
        Assert.Equal(json, SaveGame.Save(loaded.State));
        var seven = StoryCharacters.SurvivorOf(loaded, "seven")!;
        Assert.Equal(0, seven.Body.Humanity);
        Assert.Contains("not_human", seven.Traits);
        Assert.True(StoryWorld.Active(loaded, StoryWorld.Truce));
        Assert.Single(loaded.State.Campaign!.Taken);
    }
}

/// <summary>The field rules Acts 2–4 add: heavy carrying, the Linked taking the downed, waves after an objective, guards.</summary>
public class StoryFieldTests
{
    private static readonly string[] Room =
    [
        "##########",
        "#S..a....#",
        "#........#",
        "#....e...#",
        "#........#",
        "#....h...#",
        "#E########",
    ];

    private static ScavengeSim Run(List<ObjectiveDef> objectives, List<TriggerDef>? triggers = null, List<MapEnemyDef>? enemies = null,
        List<MapDeviceDef>? devices = null, int members = 2, Dictionary<string, int>? campaign = null, int players = 1, string? takeTag = null,
        int? fuse = null)
    {
        var map = new HandmadeMapDef
        {
            Id = "test", Zone = "outskirts", Rows = Room.ToList(), Markers = new() { ["a"] = "engine", ["e"] = "east", ["h"] = "hold" },
            Enemies = enemies ?? [], Devices = devices ?? [],
        };
        var mission = new FieldMission { Id = "test", Name = "Test", Objectives = objectives, Triggers = triggers ?? [], TakeTag = takeTag, FuseAfter = fuse };
        var parties = Enumerable.Range(1, players).Select(p => ScavengeTestData.Party(p, members, "pipe") with { Team = players > 1 ? 1 : null }).ToList();
        return NewScavenge.Create(ScavengeTestData.Real, "outskirts", 1, parties, null,
            new MissionSetup(mission, map) { CampaignFlags = campaign, GuestSchematics = ["sch_scanner"] });
    }

    [Fact]
    public void Carry_needs_two_survivors_next_to_it_for_three_rounds_then_the_item_is_in_a_pack()
    {
        var sim = Run([new ObjectiveDef { Id = "lift", Kind = ObjectiveKind.Carry, Marker = "engine", Count = 3, Carriers = 2, Item = "diesel_engine" }]);
        var ours = sim.State.Units.Where(u => u.PlayerId == 1).ToList();
        ours[0].Pos = new Pos(3, 1);
        sim.RunTo(3);
        Assert.Equal(0, sim.State.Mission!.Find("lift")!.Progress);
        ours[1].Pos = new Pos(5, 2);
        sim.RunTo(6);
        Assert.Equal(ObjectiveStatus.Done, sim.Status("lift"));
        Assert.Contains(ours, u => u.Pack.Contains("diesel_engine"));
    }

    [Fact]
    public void The_linked_carry_off_a_downed_survivor_after_two_rounds_and_keep_fails_past_half()
    {
        var sim = Run([new ObjectiveDef { Id = "half", Kind = ObjectiveKind.Keep, Count = 50, FailsMission = true }], takeTag: "link",
            enemies: [new MapEnemyDef("linked", "east", "link", 1)]);
        var ours = sim.State.Units.Where(u => u.PlayerId == 1).ToList();
        var linked = sim.State.Units.Single(u => u.Tag == "link");
        linked.Pos = new Pos(5, 3);
        ours[0].Pos = new Pos(5, 2);
        ours[0].Status = UnitStatus.Downed;
        ours[0].Hp = 0;
        linked.Ap = 0;
        MissionRules.RoundStarted(sim);
        Assert.Empty(sim.State.Mission!.Taken);
        MissionRules.RoundStarted(sim);
        Assert.Equal([ours[0].Origin!.Id], sim.State.Mission.Taken);
        Assert.False(ours[0].OnMap);
        // One of two taken is half, not more than half.
        Assert.Equal(ObjectiveStatus.Active, sim.Status("half"));
        ours[1].Pos = new Pos(4, 3);
        ours[1].Status = UnitStatus.Downed;
        MissionRules.RoundStarted(sim);
        MissionRules.RoundStarted(sim);
        Assert.Equal(ObjectiveStatus.Failed, sim.Status("half"));
        Assert.Equal(MissionOutcome.Failed, sim.State.Mission.Outcome);
        Assert.Equal(2, MissionRules.Result(sim.State, 1)!.Taken.Count);
    }

    [Fact]
    public void After_waves_come_rounds_after_an_objective_starts_and_guards_read_run_and_campaign_flags()
    {
        var sim = Run([new ObjectiveDef { Id = "hold", Kind = ObjectiveKind.Survive, Count = 9, Hidden = true }],
        [
            new TriggerDef { Id = "go", When = new TriggerWhen { Kind = TriggerKind.Round, Round = 2 }, Actions = [new TriggerAction { Kind = TriggerActionKind.Activate, Objective = "hold" }] },
            new TriggerDef { Id = "wave", When = new TriggerWhen { Kind = TriggerKind.After, Objective = "hold", Round = 2 },
                Actions = [new TriggerAction { Kind = TriggerActionKind.Flag, Flag = "wave", Value = 1 }] },
            new TriggerDef { Id = "truce", When = new TriggerWhen { Kind = TriggerKind.Start, If = new FieldCondition { Flag = "halcyon_offer", Min = 1, Max = 1 } },
                Actions = [new TriggerAction { Kind = TriggerActionKind.Flag, Flag = "heard", Value = 1 }] },
            new TriggerDef { Id = "no", When = new TriggerWhen { Kind = TriggerKind.Start, If = new FieldCondition { Flag = "halcyon_offer", Max = 0 } },
                Actions = [new TriggerAction { Kind = TriggerActionKind.Flag, Flag = "unheard", Value = 1 }] },
        ], campaign: new() { ["halcyon_offer"] = 1 });
        Assert.Equal(1, sim.State.Mission!.Flags["heard"]);
        Assert.False(sim.State.Mission.Flags.ContainsKey("unheard"));
        sim.RunTo(3);
        Assert.False(sim.State.Mission.Flags.ContainsKey("wave"));
        sim.RunTo(4);
        Assert.Equal(1, sim.State.Mission.Flags["wave"]);
        // Campaign flags are read, never sent home.
        Assert.False(MissionRules.Result(sim.State, 1)!.Flags.ContainsKey("halcyon_offer"));
    }

    [Fact]
    public void A_hurt_trigger_lets_cole_get_away_and_prompt_options_hide_behind_their_guards()
    {
        var sim = Run([new ObjectiveDef { Id = "x", Kind = ObjectiveKind.Reach, Marker = "hold", Optional = true }],
        [
            new TriggerDef { Id = "run", When = new TriggerWhen { Kind = TriggerKind.Hurt, Tag = "cole", Below = 0.5 }, Actions = [new TriggerAction { Kind = TriggerActionKind.Leave, Tag = "cole" }] },
            new TriggerDef { Id = "ask", When = new TriggerWhen { Kind = TriggerKind.Start }, Actions =
            [
                new TriggerAction { Kind = TriggerActionKind.Prompt, Prompt = new PromptDef { Id = "p", Speaker = "Iris", Text = "?", Options =
                    [new PromptOption("home", "Home"), new PromptOption("blame", "Blame", null, new FieldCondition { Flag = "iris_logs", Min = 3 })] } },
            ] },
        ], enemies: [new MapEnemyDef("cole", "east", "cole", 1, Side: "neutral")]);
        Assert.Equal(["home"], sim.State.Mission!.Pending!.Options.Select(o => o.Id));
        var cole = sim.State.Units.Single(u => u.Tag == "cole");
        cole.Hp = cole.MaxHp * 0.4;
        MissionRules.Update(sim);
        Assert.Equal(UnitStatus.Extracted, cole.Status);
    }

    [Fact]
    public void A_locked_hatch_cannot_be_hacked_an_opened_one_is_the_partys_and_a_fused_node_fails_the_mission()
    {
        var sim = Run([new ObjectiveDef { Id = "n", Kind = ObjectiveKind.Hack, Tag = "node", FailsMission = true }],
        [
            new TriggerDef { Id = "lock", When = new TriggerWhen { Kind = TriggerKind.Round, Round = 2 }, Actions = [new TriggerAction { Kind = TriggerActionKind.Lock, Tag = "hatch" }] },
            new TriggerDef { Id = "open", When = new TriggerWhen { Kind = TriggerKind.Round, Round = 3 }, Actions = [new TriggerAction { Kind = TriggerActionKind.Open, Tag = "hatch" }] },
        ], devices: [new MapDeviceDef(DeviceKind.Door, "east", "hatch"), new MapDeviceDef(DeviceKind.Relay, "hold", "node")], fuse: 3);
        var hatch = sim.State.Devices.Single(d => d.Tag == "hatch");
        sim.RunTo(2);
        Assert.True(Hacking.LockedOut(sim.State, hatch, MissionRules.PartySide(sim.State)));
        sim.RunTo(3);
        Assert.Equal(MissionRules.PartySide(sim.State), hatch.Controller);
        // A fused (destroyed) node fails its hack objective, and with it the mission.
        sim.State.Devices.Single(d => d.Tag == "node").Disabled = true;
        MissionRules.Update(sim);
        Assert.Equal(ObjectiveStatus.Failed, sim.Status("n"));
        Assert.Equal(MissionOutcome.Failed, sim.State.Mission!.Outcome);
    }

    [Fact]
    public void Reach_by_a_tag_counts_only_that_unit()
    {
        var sim = Run([new ObjectiveDef { Id = "core", Kind = ObjectiveKind.Reach, Marker = "hold", Radius = 0, By = "key" }]);
        var ours = sim.State.Units.Where(u => u.PlayerId == 1).ToList();
        ours[0].Pos = new Pos(5, 5);
        MissionRules.Update(sim);
        Assert.Equal(ObjectiveStatus.Active, sim.Status("core"));
        ours[0].Pos = new Pos(1, 1);
        ours[1].Tag = "key";
        ours[1].Pos = new Pos(5, 5);
        MissionRules.Update(sim);
        Assert.Equal(ObjectiveStatus.Done, sim.Status("core"));
    }

    [Fact]
    public void A_coop_guest_copies_the_missions_schematics_but_carries_no_campaign_progress()
    {
        var sim = Run([new ObjectiveDef { Id = "go", Kind = ObjectiveKind.Reach, Marker = "engine" }],
            [new TriggerDef { Id = "f", When = new TriggerWhen { Kind = TriggerKind.Start }, Actions = [new TriggerAction { Kind = TriggerActionKind.Flag, Flag = "cole_respect", Value = 2 }] }],
            players: 2);
        // The guest's party starts at the map's second spawn point (the last S tile: here the only one, so beside the host).
        Assert.Contains(sim.State.Units, u => u.PlayerId == 2);
        sim.State.Units.First(u => u.PlayerId == 1).Pos = new Pos(4, 1);
        MissionRules.Update(sim);
        foreach (var u in sim.State.Units.Where(u => u.PlayerId is 1 or 2)) u.Status = UnitStatus.Extracted;
        sim.UpdateParties();
        var host = MissionRules.Result(sim.State, 1)!;
        var guest = MissionRules.Result(sim.State, 2)!;
        Assert.Equal(MissionOutcome.Done, host.Outcome);
        Assert.Equal(2, host.Flags["cole_respect"]);
        Assert.Empty(host.GuestSchematics);
        Assert.Equal(MissionOutcome.Incomplete, guest.Outcome);
        Assert.Empty(guest.Flags);
        Assert.Equal(["sch_scanner"], guest.GuestSchematics);
    }
}
