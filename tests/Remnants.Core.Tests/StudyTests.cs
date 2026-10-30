using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class StudyTests
{
    private static ColonySim Colony(int townHallLevel = 1)
    {
        var sim = new ColonySim(TestData.Real, TestData.EmptyColony(townHallLevel, new() { ["food"] = 1000 }).State, ScavengeTestData.Real);
        sim.Build("shelter");
        sim.Build("shelter");
        return sim;
    }

    private static Building TownHall(ColonySim sim) => sim.State.Buildings.Single(b => b.DefId == "town_hall");

    [Fact]
    public void Studying_alone_raises_a_skill_up_to_the_self_study_cap()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var started = sim.Execute(new StartStudy(s.Id, TownHall(sim).Id, Skill.Melee));
        Assert.True(started.Ok, started.Message);

        sim.Advance(2000);

        Assert.Equal(sim.Data.Progression.Study.SelfStudyCap, s.Get(Skill.Melee), 6);
        Assert.Empty(sim.State.Studies);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.StudyFinished && e.SurvivorId == s.Id);
        Assert.True(s.Xp > 0);
    }

    [Fact]
    public void A_mentor_makes_it_faster_and_caps_at_the_mentors_level()
    {
        var sim = Colony(townHallLevel: 2);
        var alone = sim.AddSurvivor();
        var taught = sim.AddSurvivor();
        var mentor = sim.AddSurvivor();
        mentor.Set(Skill.Firearms, 35);
        var th = TownHall(sim);
        Assert.True(sim.Execute(new StartStudy(alone.Id, th.Id, Skill.Firearms)).Ok);
        Assert.True(sim.Execute(new StartStudy(taught.Id, th.Id, Skill.Firearms, MentorId: mentor.Id)).Ok);

        sim.Advance(24);
        Assert.InRange(taught.Get(Skill.Firearms) / alone.Get(Skill.Firearms), 1.8, 2.0);
        Assert.True(mentor.Xp > 0);

        sim.Advance(5000);
        Assert.Equal(35, taught.Get(Skill.Firearms), 6);
    }

    [Fact]
    public void Children_learn_faster()
    {
        var sim = Colony(townHallLevel: 2);
        var adult = sim.AddSurvivor(attributes: 30);
        var child = sim.AddSurvivor(LifeStage.Child, attributes: 30);
        var th = TownHall(sim);
        sim.Execute(new StartStudy(adult.Id, th.Id, Skill.Farming));
        sim.Execute(new StartStudy(child.Id, th.Id, Skill.Farming));

        sim.Advance(10);

        Assert.True(child.Get(Skill.Farming) > 1.9 * adult.Get(Skill.Farming));
    }

    [Fact]
    public void Plan_gives_the_cap_rate_and_eta()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();

        var plan = sim.PlanStudy(new StartStudy(s.Id, TownHall(sim).Id, Skill.Cooking));

        Assert.True(plan.Ok);
        Assert.Equal(sim.Data.Progression.Study.SelfStudyCap, plan.Cap);
        Assert.True(plan.RatePerTick > 0);
        Assert.NotNull(plan.EtaTicks);
        sim.Execute(new StartStudy(s.Id, TownHall(sim).Id, Skill.Cooking));
        sim.Advance(plan.EtaTicks!.Value);
        Assert.Empty(sim.State.Studies);
    }

    [Fact]
    public void Start_study_reports_typed_errors()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        var other = sim.AddSurvivor();
        var infant = sim.AddSurvivor(LifeStage.Infant);
        var th = TownHall(sim);
        var farm = sim.Build("farm");

        CommandError Try(StartStudy c) => sim.Execute(c).Error;

        Assert.Equal(CommandError.UnknownSurvivor, Try(new(999, th.Id, Skill.Melee)));
        Assert.Equal(CommandError.UnknownBuilding, Try(new(s.Id, 999, Skill.Melee)));
        Assert.Equal(CommandError.NotAStudyBuilding, Try(new(s.Id, farm.Id, Skill.Melee)));
        Assert.Equal(CommandError.InvalidStudy, Try(new(s.Id, th.Id)));
        Assert.Equal(CommandError.CannotStudyHere, Try(new(s.Id, th.Id, Skill.Hacking)));
        Assert.Equal(CommandError.TooYoung, Try(new(infant.Id, th.Id, Skill.Melee)));
        Assert.Equal(CommandError.MentorNotAhead, Try(new(s.Id, th.Id, Skill.Melee, MentorId: other.Id)));
        Assert.Equal(CommandError.SamePerson, Try(new(s.Id, th.Id, Skill.Melee, MentorId: s.Id)));
        Assert.Equal(CommandError.NeedsMentor, Try(new(s.Id, th.Id, PerkId: "brawler")));
        Assert.Equal(CommandError.NotAManual, Try(new(s.Id, th.Id, Skill.Melee, ManualItemId: "pipe")));
        Assert.Equal(CommandError.MissingItems, Try(new(s.Id, th.Id, Skill.Melee, ManualItemId: "manual_electronics")));
        s.Set(Skill.Melee, 25);
        Assert.Equal(CommandError.AlreadyAtCap, Try(new(s.Id, th.Id, Skill.Melee)));

        Assert.True(sim.Execute(new StartStudy(s.Id, th.Id, Skill.Farming)).Ok);
        Assert.Equal(CommandError.InClass, Try(new(s.Id, th.Id, Skill.Cooking)));
        Assert.Equal(CommandError.StudyFull, Try(new(other.Id, th.Id, Skill.Cooking)));
    }

    [Fact]
    public void Students_and_mentors_leave_their_jobs_and_cannot_be_assigned_while_in_class()
    {
        var sim = Colony();
        var farm = sim.Build("farm");
        var s = sim.AddSurvivor();
        var mentor = sim.AddSurvivor();
        mentor.Set(Skill.Farming, 50);
        sim.Execute(new AssignJob(s.Id, farm.Id));
        sim.Execute(new AssignJob(mentor.Id, farm.Id));

        Assert.True(sim.Execute(new StartStudy(s.Id, TownHall(sim).Id, Skill.Farming, MentorId: mentor.Id)).Ok);

        Assert.Null(s.AssignedBuildingId);
        Assert.Null(mentor.AssignedBuildingId);
        Assert.Equal(CommandError.InClass, sim.Execute(new AssignJob(mentor.Id, farm.Id)).Error);
        var id = sim.ClassOf(s)!.Id;
        Assert.True(sim.Execute(new CancelStudy(id)).Ok);
        Assert.True(sim.Execute(new AssignJob(mentor.Id, farm.Id)).Ok);
        Assert.Equal(CommandError.UnknownStudy, sim.Execute(new CancelStudy(id)).Error);
    }

    [Fact]
    public void A_class_ends_when_the_student_is_admitted_and_goes_on_alone_when_the_mentor_is()
    {
        var sim = Colony(townHallLevel: 2);
        sim.Build("med_bay");
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        var mentor = sim.AddSurvivor();
        mentor.Set(Skill.Melee, 50);
        var th = TownHall(sim);
        sim.Execute(new StartStudy(a.Id, th.Id, Skill.Melee, MentorId: mentor.Id));
        sim.Execute(new StartStudy(b.Id, th.Id, Skill.Farming));

        b.Body.Injure(BodyPart.LeftArm, 0.5);
        mentor.Body.Injure(BodyPart.LeftArm, 0.5);
        Assert.True(sim.Execute(new AdmitPatient(b.Id)).Ok);
        Assert.True(sim.Execute(new AdmitPatient(mentor.Id)).Ok);
        sim.Tick();

        Assert.Single(sim.State.Studies);
        Assert.Null(sim.ClassOf(a)!.MentorId);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.StudyEnded && e.SurvivorId == b.Id);
    }

    [Fact]
    public void Electronics_lab_teaches_hacking_and_a_manual_lifts_the_cap_and_speeds_it_up()
    {
        var sim = Colony(townHallLevel: 2);
        var lab = sim.Build("electronics_lab");
        var plain = sim.AddSurvivor();
        var reader = sim.AddSurvivor();
        sim.State.Inventory.Add("manual_intrusion");
        var cap = ScavengeTestData.Real.Items["manual_intrusion"].Manual!.Cap;

        Assert.Equal(CommandError.WrongManual, sim.Execute(new StartStudy(reader.Id, lab.Id, Skill.DronePiloting, ManualItemId: "manual_intrusion")).Error);
        var withManual = sim.PlanStudy(new StartStudy(reader.Id, lab.Id, Skill.Hacking, ManualItemId: "manual_intrusion"));
        var without = sim.PlanStudy(new StartStudy(plain.Id, lab.Id, Skill.Hacking));
        Assert.Equal(cap, withManual.Cap);
        Assert.True(withManual.RatePerTick > without.RatePerTick);

        Assert.True(sim.Execute(new StartStudy(reader.Id, lab.Id, Skill.Hacking, ManualItemId: "manual_intrusion")).Ok);
        Assert.Equal(0, sim.State.Inventory.Count("manual_intrusion"));
        sim.Advance(5000);
        Assert.Equal(cap, reader.Get(Skill.Hacking), 6);
    }

    [Fact]
    public void A_perk_is_learned_from_a_mentor_who_has_it_without_a_point()
    {
        var sim = Colony(townHallLevel: 2);
        var lab = sim.Build("electronics_lab");
        var s = sim.AddSurvivor();
        var mentor = sim.AddSurvivor();
        mentor.Perks.Add("hacking_1");
        var th = TownHall(sim);

        Assert.Equal(CommandError.CannotStudyHere, sim.Execute(new StartStudy(s.Id, th.Id, PerkId: "hacking_1", MentorId: mentor.Id)).Error);
        Assert.Equal(CommandError.PerkRequirementsNotMet, sim.Execute(new StartStudy(s.Id, lab.Id, PerkId: "hacking_1", MentorId: mentor.Id)).Error);
        s.Set(Skill.Hacking, 10);
        var other = sim.AddSurvivor();
        Assert.Equal(CommandError.MentorNotAhead, sim.Execute(new StartStudy(s.Id, lab.Id, PerkId: "hacking_1", MentorId: other.Id)).Error);

        var plan = sim.PlanStudy(new StartStudy(s.Id, lab.Id, PerkId: "hacking_1", MentorId: mentor.Id));
        Assert.True(sim.Execute(new StartStudy(s.Id, lab.Id, PerkId: "hacking_1", MentorId: mentor.Id)).Ok);
        sim.Advance(plan.EtaTicks!.Value);

        Assert.True(s.HasPerk("hacking_1"));
        Assert.Empty(sim.State.Studies);
    }

    [Fact]
    public void A_mentor_takes_a_limited_number_of_students()
    {
        var sim = Colony(townHallLevel: 3);
        var mentor = sim.AddSurvivor();
        mentor.Set(Skill.Cooking, 60);
        var th = TownHall(sim);
        var max = sim.Data.Progression.Study.MaxStudentsPerMentor;
        for (var i = 0; i < max; i++)
            Assert.True(sim.Execute(new StartStudy(sim.AddSurvivor().Id, th.Id, Skill.Cooking, MentorId: mentor.Id)).Ok);

        Assert.Equal(CommandError.MentorBusy, sim.Execute(new StartStudy(sim.AddSurvivor().Id, th.Id, Skill.Cooking, MentorId: mentor.Id)).Error);
    }

    [Fact]
    public void Study_is_deterministic()
    {
        string Run()
        {
            var sim = Colony(townHallLevel: 2);
            var a = sim.AddSurvivor();
            var m = sim.AddSurvivor();
            m.Set(Skill.Medicine, 40);
            sim.Execute(new StartStudy(a.Id, TownHall(sim).Id, Skill.Medicine, MentorId: m.Id));
            sim.Advance(200);
            return Serialization.SaveGame.Save(sim.State);
        }
        Assert.Equal(Run(), Run());
    }
}
