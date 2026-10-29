using Remnants.Core.Colony;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

/// <summary>Fixtures for population life: a fed, housed colony and a ready-made couple.</summary>
public static class LifeTestData
{
    public static ColonySim Colony(double food = 1000, int shelters = 1)
    {
        var sim = TestData.EmptyColony(townHallLevel: 3, stock: new() { ["food"] = food });
        for (var i = 0; i < shelters; i++) sim.Build("shelter");
        return sim;
    }

    public static (Survivor Mother, Survivor Father) Couple(ColonySim sim)
    {
        var mother = sim.AddSurvivor();
        mother.Sex = Sex.Female;
        var father = sim.AddSurvivor();
        father.Sex = Sex.Male;
        mother.PartnerId = father.Id;
        father.PartnerId = mother.Id;
        return (mother, father);
    }

    /// <summary>Runs until the mother is pregnant (or fails after many conception checks).</summary>
    public static void UntilPregnant(ColonySim sim, Survivor mother)
    {
        for (var i = 0; i < 100 * sim.Data.Rules.Family.ConceptionCheckTicks && mother.Pregnancy is null; i++) sim.Tick();
        Assert.NotNull(mother.Pregnancy);
    }
}

public class GrowthTests
{
    [Fact]
    public void Infant_becomes_child_then_adult_at_the_growth_thresholds()
    {
        var sim = LifeTestData.Colony();
        var life = sim.Data.Rules.Life;
        var baby = sim.AddSurvivor(LifeStage.Infant);

        sim.Advance(life.InfantTicks - 1);
        Assert.Equal(LifeStage.Infant, baby.LifeStage);
        sim.Tick();
        Assert.Equal(LifeStage.Child, baby.LifeStage);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.BecameChild && e.SurvivorId == baby.Id);

        sim.Advance(life.ChildTicks - 1);
        Assert.Equal(LifeStage.Child, baby.LifeStage);
        sim.Tick();
        Assert.Equal(LifeStage.Adult, baby.LifeStage);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.BecameAdult && e.SurvivorId == baby.Id);
        Assert.Equal(sim.Data.Rules.AdultAge * sim.Data.Rules.TicksPerYear, baby.AgeTicks);
    }

    [Fact]
    public void Progress_and_eta_follow_the_stage()
    {
        var life = TestData.Real.Rules.Life;
        var child = new Survivor { LifeStage = LifeStage.Child, GrowthTicks = life.InfantTicks + life.ChildTicks / 4 };

        Assert.Equal(0.25, Bonds.StageProgress(child, life), 2);
        Assert.Equal(life.ChildTicks - life.ChildTicks / 4, Bonds.TicksToNextStage(child, life));
        Assert.Null(Bonds.TicksToNextStage(new Survivor(), life));
    }

    [Fact]
    public void Fed_children_grow_towards_their_aptitude_and_hungry_ones_do_not()
    {
        Survivor Grow(double food)
        {
            var sim = LifeTestData.Colony(food);
            var child = sim.AddSurvivor(LifeStage.Child, attributes: 10);
            foreach (var a in Enum.GetValues<AttributeKind>()) child.Aptitude[a] = 80;
            sim.Advance(50);
            return child;
        }

        var fed = Grow(1000);
        var hungry = Grow(0);

        Assert.True(fed.Get(AttributeKind.Strength) > 20);
        Assert.Equal(10, hungry.Get(AttributeKind.Strength), 9);
        Assert.Equal(50, hungry.MalnourishedTicks);
        Assert.Equal(0, fed.MalnourishedTicks);
    }

    [Fact]
    public void A_malnourished_child_comes_of_age_weaker_and_it_is_noted()
    {
        var sim = LifeTestData.Colony();
        var child = sim.AddSurvivor(LifeStage.Child);
        child.GrowthTicks = sim.Data.Rules.Life.GrowthTicks - 1;
        child.MalnourishedTicks = sim.Data.Rules.Life.GrowthTicks / 2;

        sim.Tick();

        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.BecameAdult && e.Message.Contains("hunger"));
        Assert.Empty(child.Aptitude);
    }

    [Fact]
    public void Infants_eat_less_than_children_and_are_fed_first()
    {
        var sim = LifeTestData.Colony(food: 0);
        var food = sim.Data.Rules.Food;
        var child = sim.AddSurvivor(LifeStage.Child);
        var baby = sim.AddSurvivor(LifeStage.Infant);
        sim.State.Stockpile.Add("food", food.InfantPerTick);

        sim.Tick();

        Assert.True(food.InfantPerTick < food.ChildPerTick);
        Assert.Equal(0, baby.StarvingTicks);
        Assert.Equal(1, child.StarvingTicks);
    }

    [Fact]
    public void Infants_cannot_work_and_children_only_do_light_jobs()
    {
        var sim = LifeTestData.Colony();
        sim.State.Stockpile.Add("wood", 100);
        var farm = sim.Build("farm");
        var woodcutter = sim.Build("woodcutter");
        var baby = sim.AddSurvivor(LifeStage.Infant);
        var child = sim.AddSurvivor(LifeStage.Child);

        Assert.Equal(CommandError.TooYoung, sim.Execute(new AssignJob(baby.Id, farm.Id)).Error);
        Assert.Equal(CommandError.NotForChildren, sim.Execute(new AssignJob(child.Id, woodcutter.Id)).Error);
        Assert.True(sim.Execute(new AssignJob(child.Id, farm.Id)).Ok);
    }

    [Fact]
    public void Apprentice_children_grow_into_their_job_faster_than_adults()
    {
        var sim = LifeTestData.Colony();
        var farm = sim.Build("farm");
        var child = sim.AddSurvivor(LifeStage.Child, attributes: 30);
        var adult = sim.AddSurvivor(attributes: 30);
        sim.Execute(new AssignJob(child.Id, farm.Id));
        sim.Execute(new AssignJob(adult.Id, farm.Id));

        sim.Advance(40);

        Assert.True(child.Get(Skill.Farming) > adult.Get(Skill.Farming));
    }
}

public class RelationshipTests
{
    [Fact]
    public void Single_adults_grow_closer_and_become_a_couple()
    {
        var sim = LifeTestData.Colony();
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        var rules = sim.Data.Rules.Relationships;

        sim.Tick();
        var perTick = Bonds.Affinity(sim.State, a.Id, b.Id);
        Assert.True(perTick > 0);

        sim.Advance((int)Math.Ceiling(rules.CoupleThreshold / perTick));

        Assert.Equal(b.Id, a.PartnerId);
        Assert.Equal(a.Id, b.PartnerId);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.NewCouple);
    }

    [Fact]
    public void A_shared_workplace_and_encouragement_speed_it_up_and_discouragement_stops_it()
    {
        var rules = TestData.Real.Rules.Relationships;
        var a = TestData.Survivor();
        var b = TestData.Survivor();
        b.Id = 2;

        var apart = Bonds.AffinityPerTick(a, b, PairStance.Neutral, rules);
        a.AssignedBuildingId = b.AssignedBuildingId = 7;
        var together = Bonds.AffinityPerTick(a, b, PairStance.Neutral, rules);

        Assert.Equal(rules.SharedWorkplacePerTick, together - apart, 9);
        Assert.Equal(together * rules.EncourageMultiplier, Bonds.AffinityPerTick(a, b, PairStance.Encourage, rules), 9);
        Assert.Equal(0, Bonds.AffinityPerTick(a, b, PairStance.Discourage, rules));
    }

    [Fact]
    public void Similar_people_get_along_better()
    {
        var rules = TestData.Real.Rules.Relationships;
        var a = TestData.Survivor(50);
        var alike = TestData.Survivor(50);
        var unlike = TestData.Survivor(10);

        Assert.True(Bonds.AffinityPerTick(a, alike, PairStance.Neutral, rules) > Bonds.AffinityPerTick(a, unlike, PairStance.Neutral, rules));
    }

    [Fact]
    public void Children_and_couples_do_not_bond_with_others()
    {
        var sim = LifeTestData.Colony();
        var (mother, father) = LifeTestData.Couple(sim);
        var single = sim.AddSurvivor();
        var child = sim.AddSurvivor(LifeStage.Child);

        sim.Advance(10);

        Assert.Equal(0, Bonds.Affinity(sim.State, single.Id, mother.Id));
        Assert.Equal(0, Bonds.Affinity(sim.State, single.Id, child.Id));
        Assert.Equal(father.Id, mother.PartnerId);
    }

    [Fact]
    public void Matchmake_validates_and_discouraging_a_couple_splits_them()
    {
        var sim = LifeTestData.Colony();
        var (mother, father) = LifeTestData.Couple(sim);
        var single = sim.AddSurvivor();
        var child = sim.AddSurvivor(LifeStage.Child);

        Assert.Equal(CommandError.SamePerson, sim.Execute(new Matchmake(single.Id, single.Id, PairStance.Encourage)).Error);
        Assert.Equal(CommandError.NotAnAdult, sim.Execute(new Matchmake(single.Id, child.Id, PairStance.Encourage)).Error);
        Assert.Equal(CommandError.AlreadyPartnered, sim.Execute(new Matchmake(single.Id, mother.Id, PairStance.Encourage)).Error);
        Assert.Equal(CommandError.UnknownSurvivor, sim.Execute(new Matchmake(single.Id, 999, PairStance.Encourage)).Error);

        Assert.True(sim.Execute(new Matchmake(father.Id, mother.Id, PairStance.Discourage)).Ok);

        Assert.Null(mother.PartnerId);
        Assert.Null(father.PartnerId);
        Assert.True(mother.Mood < Survivor.MaxMood);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.BrokeUp);
        sim.Advance(50);
        Assert.Equal(0, Bonds.Affinity(sim.State, mother.Id, father.Id));
    }

    [Fact]
    public void Encouraged_pair_becomes_a_couple_before_a_neutral_one()
    {
        var sim = LifeTestData.Colony();
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        var c = sim.AddSurvivor();
        var d = sim.AddSurvivor();
        Assert.True(sim.Execute(new Matchmake(c.Id, d.Id, PairStance.Encourage)).Ok);

        for (var i = 0; i < 5000 && c.PartnerId is null; i++) sim.Tick();

        Assert.Equal(d.Id, c.PartnerId);
        Assert.Null(a.PartnerId);
        Assert.Null(b.PartnerId);
    }
}

public class BirthTests
{
    [Fact]
    public void A_fed_housed_couple_conceives_and_a_baby_is_born_after_the_pregnancy()
    {
        var sim = LifeTestData.Colony();
        var (mother, father) = LifeTestData.Couple(sim);
        mother.Set(AttributeKind.Strength, 20);
        father.Set(AttributeKind.Strength, 60);

        LifeTestData.UntilPregnant(sim, mother);
        Assert.Equal(father.Id, mother.Pregnancy!.FatherId);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Pregnant && e.SurvivorId == mother.Id);
        var due = mother.Pregnancy.DueTick;
        Assert.Equal(sim.Data.Rules.Family.PregnancyTicks, due - mother.Pregnancy.ConceivedTick);

        while (sim.State.Tick <= due) sim.Tick();

        Assert.Null(mother.Pregnancy);
        var baby = Assert.Single(Bonds.Children(sim.State, mother));
        Assert.Equal(LifeStage.Infant, baby.LifeStage);
        Assert.Equal(father.Id, baby.FatherId);
        Assert.True(baby.HasBed);
        var variation = sim.Data.Rules.Life.InheritVariation;
        Assert.InRange(baby.Aptitude[AttributeKind.Strength], 40 - variation, 40 + variation);
        var atBirth = baby.Aptitude[AttributeKind.Strength] * sim.Data.Rules.Life.BirthAttributeFraction;
        Assert.InRange(baby.Get(AttributeKind.Strength), atBirth, atBirth + 1);  // it has grown for a tick since
        Assert.Contains(baby.Name, sim.Data.Rules.Family.MaleNames.Concat(sim.Data.Rules.Family.FemaleNames));
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Born && e.SurvivorId == baby.Id);
    }

    [Fact]
    public void No_conception_without_a_free_bed()
    {
        var sim = LifeTestData.Colony();
        var (mother, _) = LifeTestData.Couple(sim);
        var beds = sim.BedCapacity;
        while (sim.State.Survivors.Count < beds) sim.AddSurvivor(LifeStage.Child);

        sim.Advance(50 * sim.Data.Rules.Family.ConceptionCheckTicks);

        Assert.Null(mother.Pregnancy);
        Assert.Equal(0, Bonds.FreeBedsForBabies(sim.State, sim.Data));
    }

    [Fact]
    public void No_conception_without_enough_food()
    {
        var sim = LifeTestData.Colony(food: 1);
        var (mother, _) = LifeTestData.Couple(sim);
        sim.Build("farm");

        sim.Advance(20 * sim.Data.Rules.Family.ConceptionCheckTicks);

        Assert.Null(mother.Pregnancy);
    }

    [Fact]
    public void Same_sex_couples_do_not_conceive()
    {
        var sim = LifeTestData.Colony();
        var (a, b) = LifeTestData.Couple(sim);
        b.Sex = Sex.Female;

        sim.Advance(50 * sim.Data.Rules.Family.ConceptionCheckTicks);

        Assert.Null(a.Pregnancy);
        Assert.Null(b.Pregnancy);
    }

    [Fact]
    public void A_mother_with_an_infant_does_not_conceive_again()
    {
        var sim = LifeTestData.Colony();
        var (mother, father) = LifeTestData.Couple(sim);
        var baby = sim.AddSurvivor(LifeStage.Infant);
        baby.MotherId = mother.Id;

        Assert.False(Bonds.CanConceive(sim.State, mother, father));
        baby.LifeStage = LifeStage.Child;
        Assert.True(Bonds.CanConceive(sim.State, mother, father));
    }

    [Fact]
    public void A_pregnant_survivor_stays_home_from_expeditions()
    {
        var sim = LoopTestData.Colony();
        var mother = sim.AddSurvivor();
        mother.Pregnancy = new Pregnancy { DueTick = 50 };

        Assert.Equal(CommandError.Pregnant, sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(mother)])).Error);
    }
}

public class GriefTests
{
    [Fact]
    public void Partner_and_children_grieve_a_death_on_an_expedition_and_work_slower()
    {
        var sim = LoopTestData.Colony();
        sim.Build("shelter");
        var farm = sim.Build("farm");
        var (mother, father) = LifeTestData.Couple(sim);
        var child = sim.AddSurvivor(LifeStage.Child);
        child.FatherId = father.Id;
        Assert.True(sim.Execute(new AssignJob(mother.Id, farm.Id)).Ok);
        var job = sim.Data.Jobs["farming"];
        var before = JobOutput.PerTick(sim.Data.Buildings["farm"], 1, job, mother, sim.Data.Rules.Output);

        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(father)])).Ok);
        Assert.True(sim.Execute(new ApplyExtractionResult(LoopTestData.Result(deaths: [father]))).Ok);

        Assert.Null(mother.PartnerId);
        Assert.Equal(Survivor.MaxMood - sim.Data.Rules.Mood.PartnerGrief, mother.Mood, 9);
        Assert.Equal(Survivor.MaxMood - sim.Data.Rules.Mood.FamilyGrief, child.Mood, 9);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Grieving && e.SurvivorId == mother.Id);
        Assert.True(JobOutput.PerTick(sim.Data.Buildings["farm"], 1, job, mother, sim.Data.Rules.Output) < before);
        Assert.DoesNotContain(sim.State.Relationships, r => r.Involves(father.Id));
    }

    [Fact]
    public void Mood_recovers_over_time()
    {
        var sim = LifeTestData.Colony();
        var s = sim.AddSurvivor();
        s.Mood = 10;

        sim.Advance(10);

        Assert.Equal(10 + 10 * sim.Data.Rules.Mood.RecoveryPerTick, s.Mood, 9);
    }

    [Fact]
    public void Mood_factor_scales_from_low_mood_multiplier_to_one()
    {
        var rules = TestData.Real.Rules.Output;
        var s = TestData.Survivor();
        Assert.Equal(1, JobOutput.MoodFactor(s, rules));
        s.Mood = 0;
        Assert.Equal(rules.LowMoodMultiplier, JobOutput.MoodFactor(s, rules));
    }
}

public class HousingPressureTests
{
    [Fact]
    public void A_homeless_infant_leaves_with_its_mother_and_the_father_is_heartbroken()
    {
        var sim = LifeTestData.Colony();
        var (mother, father) = LifeTestData.Couple(sim);
        while (sim.State.Survivors.Count < sim.BedCapacity) sim.AddSurvivor();
        sim.Tick();
        var baby = sim.AddSurvivor(LifeStage.Infant);
        baby.MotherId = mother.Id;
        baby.FatherId = father.Id;

        sim.Advance(sim.Data.Rules.Housing.HomelessGraceTicks + 1);

        Assert.DoesNotContain(baby, sim.State.Survivors);
        Assert.DoesNotContain(mother, sim.State.Survivors);
        Assert.Contains(father, sim.State.Survivors);
        Assert.Null(father.PartnerId);
        Assert.True(father.Mood < Survivor.MaxMood);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Left && e.SurvivorId == baby.Id && e.Message.Contains(mother.Name));
    }

    [Fact]
    public void A_homeless_child_with_no_parent_at_home_is_taken_in_by_travelers()
    {
        var sim = LifeTestData.Colony(shelters: 0);
        var child = sim.AddSurvivor(LifeStage.Child);

        sim.Advance(sim.Data.Rules.Housing.HomelessGraceTicks + 1);

        Assert.DoesNotContain(child, sim.State.Survivors);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Left && e.Message.Contains("travelers"));
    }

    [Fact]
    public void Couples_and_children_share_a_home_when_there_is_room()
    {
        var sim = LifeTestData.Colony(shelters: 2);
        var loners = Enumerable.Range(0, sim.Data.Buildings["shelter"].Level(1).Beds - 1).Select(_ => sim.AddSurvivor()).ToList();
        sim.Tick();
        var (mother, father) = LifeTestData.Couple(sim);
        var child = sim.AddSurvivor(LifeStage.Child);
        child.MotherId = mother.Id;

        sim.Tick();

        Assert.NotNull(mother.HomeBuildingId);
        Assert.Equal(mother.HomeBuildingId, father.HomeBuildingId);
        Assert.Equal(mother.HomeBuildingId, child.HomeBuildingId);
        Assert.All(loners, l => Assert.NotNull(l.HomeBuildingId));
    }
}

public class LifeSaveTests
{
    private static ColonySim FamilyColony(ulong seed)
    {
        var sim = NewGame.Create(TestData.Real, seed);
        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        sim.Execute(new Matchmake(adults[0].Id, adults[1].Id, PairStance.Encourage));
        sim.State.Stockpile.Add("food", 500);
        sim.Advance(1500);
        return sim;
    }

    [Fact]
    public void A_played_colony_has_families_and_stays_deterministic_through_save_and_load()
    {
        var sim = FamilyColony(3);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Born);

        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(TestData.Real, json);
        Assert.Equal(json, SaveGame.Save(loaded.State));
        Assert.Equal(json, SaveGame.Save(FamilyColony(3).State));

        sim.Advance(300);
        loaded.Advance(300);
        Assert.Equal(SaveGame.Save(sim.State), SaveGame.Save(loaded.State));
    }

    [Fact]
    public void Version_2_saves_load_and_their_children_join_the_growth_track()
    {
        var state = NewGame.Create(TestData.Real, 1).State;
        var child = state.Survivors.First(s => s.IsChild);
        var json = SaveGame.Save(state)
            .Replace($"\"version\": {SaveGame.Version}", "\"version\": 2");
        // A version 2 child: no growth fields yet, aged in years.
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        foreach (var s in node["colony"]!["survivors"]!.AsArray())
        {
            var obj = s!.AsObject();
            foreach (var key in new[] { "growthTicks", "aptitude", "mood", "partnerId", "homeBuildingId" }) obj.Remove(key);
            if (obj["id"]!.GetValue<int>() == child.Id) obj["ageTicks"] = 10 * TestData.Real.Rules.TicksPerYear;
        }
        node["colony"]!.AsObject().Remove("relationships");

        var loaded = SaveGame.Load(TestData.Real, node.ToJsonString());

        var migrated = loaded.State.FindSurvivor(child.Id)!;
        Assert.Equal(LifeStage.Child, migrated.LifeStage);
        Assert.Equal(Bonds.GrowthAtAge(10 * TestData.Real.Rules.TicksPerYear, TestData.Real.Rules), migrated.GrowthTicks);
        Assert.Equal(Survivor.MaxMood, migrated.Mood);
        loaded.Advance(TestData.Real.Rules.Life.GrowthTicks);
        Assert.Equal(LifeStage.Adult, migrated.LifeStage);
    }

    [Fact]
    public void A_one_sided_partnership_is_rejected()
    {
        var state = NewGame.Create(TestData.Real, 1).State;
        state.Survivors[0].PartnerId = state.Survivors[1].Id;
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SaveGame.Load(TestData.Real, SaveGame.Save(state)));
    }
}
