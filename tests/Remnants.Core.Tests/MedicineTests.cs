using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class MedicineDataTests
{
    private static MedicineData Med => TestData.Real.Medicine!;

    [Fact]
    public void The_shipped_data_has_the_conditions_the_rules_name()
    {
        foreach (var id in new[] { "infection", "surgical_infection", "fracture", "food_sickness", "husk_sickness", "dependency", Body.PainkillerConditionId })
            Assert.NotNull(Med.Condition(id));
        Assert.Equal("husk_sickness", Med.Def.SicknessKinds["husk"]);
        Assert.NotNull(ScavengeTestData.Real.Medicine);
    }

    [Fact]
    public void Medical_supplies_come_in_quality_tiers_that_scale_what_they_do()
    {
        var items = ScavengeTestData.Real.Items;
        Assert.True(items["bandage@fine"].Use!.Heal > items["bandage"].Use!.Heal);
        Assert.True(items["blood_bag@crude"].Use!.ReviveHp < items["blood_bag"].Use!.ReviveHp);
        Assert.True(items["antibiotics@crude"].Use!.CureStrength < 1);
        Assert.Equal("antibiotics", items["antibiotics@fine"].DefinedId);
    }

    [Fact]
    public void Herbs_fall_back_to_food_until_food_cooking_brings_them()
    {
        Assert.False(TestData.Real.Resources.ContainsKey("herbs"));
        var antiseptic = TestData.Real.Recipes["antiseptic"];
        Assert.False(antiseptic.Inputs.ContainsKey("herbs"));
        Assert.Equal(1.5, antiseptic.Inputs["food"], 9);
    }

    [Fact]
    public void Gated_recipes_wait_for_their_schematic_and_default_ones_do_not()
    {
        var catalog = TestData.Real.Schematics;
        Assert.Equal("sch_antibiotics", catalog.GateOfRecipe("antibiotics"));
        Assert.Equal("sch_calm_injector", catalog.GateOfRecipe("calm_injector"));
        Assert.True(catalog.Find("sch_calm_injector")!.Encrypted);
        foreach (var open in new[] { "bandage", "splint", "herbal_remedy", "antiseptic", "med_kit" }) Assert.Null(catalog.GateOfRecipe(open));
        Assert.Equal(ItemKind.Schematic, ScavengeTestData.Real.Items["sch_antibiotics"].Kind);
        Assert.Contains("M15", catalog.Find("sch_trauma_kit")!.StoryMissions);
    }
}

public class ColonyMedicineTests
{
    private static (ColonySim Sim, Building Bay) Colony(int level = 1, double medicine = 20)
    {
        var sim = CyberTestColony.Make();
        var bay = sim.Build("med_bay");
        bay.Level = level;
        sim.State.Stockpile.Add("medicine", medicine);
        return (sim, bay);
    }

    private static Survivor Hurt(ColonySim sim, BodyPart part, double severity)
    {
        var s = sim.AddSurvivor();
        s.Body.Injure(part, severity);
        return s;
    }

    [Fact]
    public void A_dirty_wound_gets_infected_and_a_clean_one_never_does()
    {
        var (sim, _) = Colony();
        var dirty = Hurt(sim, BodyPart.LeftArm, 0.5);
        var clean = Hurt(sim, BodyPart.LeftArm, 0.5);
        clean.Body[BodyPart.LeftArm].Dressing = Dressing.Clean;

        for (var i = 0; i < 2000 && !dirty.Body.Has("infection"); i++) sim.Tick();

        Assert.True(dirty.Body.Has("infection"));
        Assert.False(clean.Body.Has("infection"));
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Infected && e.SurvivorId == dirty.Id);
    }

    [Fact]
    public void An_infection_grows_blocks_healing_and_costs_output_until_antibiotics_cure_it()
    {
        var (sim, _) = Colony();
        var s = Hurt(sim, BodyPart.RightLeg, 0.2); // would heal naturally
        Treatment.Afflict(s.Body, "infection", 0.2, BodyPart.RightLeg);
        var job = sim.Data.Jobs["woodcutting"];
        var sick = JobOutput.ConditionFactor(s, sim.Data);
        Assert.True(sick < 1);

        sim.Advance(20);
        Assert.Equal(0.2, s.Body[BodyPart.RightLeg].Severity, 9);
        Assert.True(s.Body.Find("infection")!.Severity > 0.2);

        sim.State.Inventory.Add("antibiotics");
        var result = sim.Execute(new TreatSurvivor(s.Id, "antibiotics"));
        Assert.True(result.Ok, result.Message);
        Assert.False(s.Body.Has("infection"));
        Assert.Equal(0, sim.State.Inventory.Count("antibiotics"));
        Assert.Equal(1, JobOutput.ConditionFactor(s, sim.Data), 9);
        _ = job;
    }

    [Fact]
    public void The_med_bay_treats_bleeding_before_infection_and_follows_the_players_order()
    {
        var (sim, bay) = Colony();
        var s = Hurt(sim, BodyPart.Torso, 0.5);
        Treatment.Afflict(s.Body, "infection", 0.3, BodyPart.Torso);
        sim.State.Inventory.Add("bandage");
        sim.State.Inventory.Add("antibiotics");
        Assert.True(sim.Execute(new AdmitPatient(s.Id, bay.Id)).Ok);

        sim.Tick();
        Assert.Equal(Dressing.Bandaged, s.Body[BodyPart.Torso].Dressing);
        Assert.True(s.Body.Has("infection"));
        sim.Tick();
        Assert.False(s.Body.Has("infection"));

        // Infection first, bleeding off.
        var other = Hurt(sim, BodyPart.Torso, 0.5);
        Treatment.Afflict(other.Body, "infection", 0.3, BodyPart.Torso);
        sim.State.Inventory.Add("bandage");
        sim.State.Inventory.Add("antibiotics");
        Assert.True(sim.Execute(new SetCarePriorities([TreatPriority.Infection])).Ok);
        Assert.True(sim.Execute(new AdmitPatient(other.Id, bay.Id)).Ok);
        sim.Advance(3);
        Assert.False(other.Body.Has("infection"));
        Assert.Equal(Dressing.None, other.Body[BodyPart.Torso].Dressing);
        Assert.Equal(1, sim.State.Inventory.Count("bandage"));
    }

    [Fact]
    public void A_sick_survivor_can_be_admitted_and_an_antidote_cures_husk_sickness()
    {
        var (sim, _) = Colony();
        var s = sim.AddSurvivor();
        Assert.Equal(CommandError.NotInjured, sim.Execute(new AdmitPatient(s.Id)).Error);

        var c = Sickness.Apply(sim.State, sim.Data, s, "husk", 0.6);
        Assert.Equal("husk_sickness", c!.Id);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.FellSick);
        Assert.True(sim.Execute(new AdmitPatient(s.Id)).Ok);
        sim.State.Inventory.Add("antidote");
        sim.Tick();
        Assert.False(s.Body.Has("husk_sickness"));
        sim.Tick();
        Assert.Null(s.PatientAtBuildingId); // nothing left to treat
    }

    [Fact]
    public void Food_sickness_passes_on_its_own()
    {
        var (sim, _) = Colony();
        var s = sim.AddSurvivor();
        Sickness.Apply(sim.State, sim.Data, s, "food", 0.3);
        sim.Advance(200);
        Assert.False(s.Body.Has("food_sickness"));
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Recovered && e.SurvivorId == s.Id);
    }

    [Fact]
    public void A_splinted_fracture_hurts_less_and_heals_faster()
    {
        var med = TestData.Real.Medicine!;
        var body = new Body();
        body.Injure(BodyPart.LeftLeg, 0.4);
        Treatment.Fracture(med, body, BodyPart.LeftLeg);
        Assert.Equal(1 - 0.4 - med.Fracture.Penalty, body.PartEfficiency(BodyPart.LeftLeg), 9);
        Assert.Equal(med.Fracture.UnsplintedHealMultiplier, Treatment.HealFactor(med, body, BodyPart.LeftLeg), 9);

        Assert.NotEmpty(Treatment.Apply(med, body, ScavengeTestData.Real.Items["splint"].Use!, field: false));
        Assert.Equal(1 - 0.4 - med.Fracture.SplintedPenalty, body.PartEfficiency(BodyPart.LeftLeg), 9);
        Assert.Equal(med.Fracture.SplintedHealMultiplier, Treatment.HealFactor(med, body, BodyPart.LeftLeg), 9);

        body.Heal(BodyPart.LeftLeg);
        Assert.Empty(body.Conditions);
        Assert.Equal(1, body.PartEfficiency(BodyPart.LeftLeg));
    }

    [Fact]
    public void Painkillers_dull_severe_injuries_and_too_many_grow_a_mild_dependency()
    {
        var (sim, _) = Colony();
        var s = Hurt(sim, BodyPart.Torso, 0.7);
        var med = sim.Data.Medicine!;
        Assert.True(Treatment.InPain(med, s.Body));
        var hurting = JobOutput.ConditionFactor(s, sim.Data);
        var before = s.Body.PartEfficiency(BodyPart.Torso);

        sim.State.Inventory.Add("painkiller", 3);
        Assert.True(sim.Execute(new TreatSurvivor(s.Id, "painkiller")).Ok);
        Assert.False(Treatment.InPain(med, s.Body));
        Assert.True(JobOutput.ConditionFactor(s, sim.Data) > hurting);
        Assert.True(s.Body.PartEfficiency(BodyPart.Torso) > before);
        Assert.False(s.Body.Has("dependency"));

        Assert.True(sim.Execute(new TreatSurvivor(s.Id, "painkiller")).Ok);
        Assert.True(sim.Execute(new TreatSurvivor(s.Id, "painkiller")).Ok);
        Assert.True(s.Body.Has("dependency"));
        s.Mood = 80;
        sim.Tick();
        Assert.True(s.Mood < 80 + sim.Data.Rules.Mood.RecoveryPerTick);
    }

    [Fact]
    public void The_med_bay_does_not_hand_out_painkillers_to_someone_on_the_edge_of_a_dependency()
    {
        var med = TestData.Real.Medicine!;
        var s = TestData.Survivor();
        s.Body.Injure(BodyPart.Torso, 0.7);
        Assert.True(Medicine.Wants(med, s, TreatPriority.Pain));
        s.Body.Doses = med.Painkiller.DependencyDoses - 1;
        Assert.False(Medicine.Wants(med, s, TreatPriority.Pain));
    }

    [Fact]
    public void A_blood_bag_waits_for_a_donor_who_gives_health()
    {
        var (sim, bay) = Colony(level: 2);
        var medic = sim.AddSurvivor();
        medic.Set(Skill.Medicine, 30);
        Assert.True(sim.Execute(new AssignJob(medic.Id, bay.Id)).Ok);
        sim.State.Stockpile.Add("cloth", 10);
        sim.State.Stockpile.Add("scrap", 10);
        Schematics.Schematics.Grant(sim, "sch_blood_transfusion", Schematics.SchematicSource.Debug);
        Assert.True(sim.Execute(new QueueRecipe(bay.Id, "blood_bag")).Ok);
        medic.Health = 60; // the only adult at home is too weak to give
        sim.Advance(5);
        Assert.Equal(CraftStatus.WaitingForInputs, sim.CraftStatus(bay));

        var donor = sim.AddSurvivor();
        for (var i = 0; i < 100 && bay.Queue.Count > 0; i++) sim.Tick();
        Assert.Empty(bay.Queue);
        Assert.Equal(1, sim.State.Inventory.Counts.Where(kv => kv.Key.Split('@')[0] == "blood_bag").Sum(kv => kv.Value));
        Assert.True(donor.Health < Survivors.Survivor.MaxHealth);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.BloodDonated && e.SurvivorId == donor.Id);
    }

    [Fact]
    public void Medics_craft_supplies_at_the_dispensary_with_quality_from_their_medicine_skill()
    {
        var (sim, bay) = Colony();
        var medic = sim.AddSurvivor();
        medic.Set(Skill.Medicine, 100);
        Assert.True(sim.Execute(new AssignJob(medic.Id, bay.Id)).Ok);
        sim.State.Stockpile.Add("cloth", 20);
        sim.State.Stockpile.Add("wood", 20);
        Assert.Equal(CommandError.RecipeLocked, sim.Execute(new QueueRecipe(bay.Id, "clean_dressing")).Error);
        Assert.Equal(CommandError.RecipeLevelTooLow, sim.Execute(new QueueRecipe(bay.Id, "antibiotics")).Error);
        Assert.True(sim.Execute(new QueueRecipe(bay.Id, "splint", 5)).Ok);
        for (var i = 0; i < 300 && bay.Queue.Count > 0; i++) sim.Tick();
        Assert.Empty(bay.Queue);
        Assert.True(sim.State.Inventory.Count("splint@fine") >= 3);
    }

    [Fact]
    public void A_surgical_kit_raises_the_odds_is_used_up_and_a_failed_surgery_leaves_an_infection()
    {
        var (sim, bay) = Colony(level: 2);
        var medic = sim.AddSurvivor();
        medic.Set(Skill.Medicine, 20);
        Assert.True(sim.Execute(new AssignJob(medic.Id, bay.Id)).Ok);
        var without = Cybernetics.SuccessChance(sim, bay, 2, elective: false);
        sim.State.Inventory.Add("surgical_kit@fine");
        var with = Cybernetics.SuccessChance(sim, bay, 2, elective: false);
        Assert.Equal(without + 0.12 * 1.25, with, 6);

        var s = sim.AddSurvivor();
        s.Body.Lose(BodyPart.LeftLeg);
        sim.State.Inventory.Add("runner_leg");
        sim.State.KnownCybernetics.Add("runner_leg");
        Assert.True(sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftLeg, "runner_leg")).Ok, "booking");
        for (var i = 0; i < 400 && sim.State.Surgeries.Count > 0; i++) sim.Tick();
        Assert.Empty(sim.State.Surgeries);
        Assert.Equal(0, sim.State.Inventory.Count("surgical_kit@fine"));
        var failed = sim.State.Events.Any(e => e.Type == ColonyEventType.SurgeryComplication && e.SurvivorId == s.Id);
        Assert.Equal(failed, s.Body.Find("surgical_infection", BodyPart.LeftLeg) is not null);
    }

    [Fact]
    public void A_surgical_infection_needs_care_but_a_mild_one_costs_no_ap()
    {
        var med = TestData.Real.Medicine!;
        var body = new Body();
        Treatment.Afflict(body, med.Infection.SurgicalCondition, med.Def.Surgery.InfectionSeverity, BodyPart.LeftLeg);
        var s = TestData.Survivor();
        s.Body = body;
        Assert.True(Medicine.NeedsCare(s, TestData.Real));
        Assert.True(Treatment.FieldApPenalty(med, body) == 0); // mild: not yet at its field threshold
    }

    [Fact]
    public void Old_saves_turn_part_of_their_medicine_into_antiseptic()
    {
        var (sim, _) = Colony(medicine: 0);
        sim.State.Stockpile.Add("medicine", 10);
        var json = SaveGame.Save(sim.State).Replace($"\"version\": {SaveGame.Version}", $"\"version\": {SaveGame.MedicineVersion - 1}");
        var loaded = SaveGame.Load(sim.Data, json, sim.Scavenge);
        Assert.Equal(2, loaded.State.Inventory.Count("antiseptic"));
        Assert.Equal(6, loaded.State.Stockpile.Get("medicine"), 9);
    }

    [Fact]
    public void Conditions_dressings_and_priorities_survive_a_save()
    {
        var (sim, _) = Colony();
        var s = Hurt(sim, BodyPart.LeftArm, 0.5);
        s.Body[BodyPart.LeftArm].Dressing = Dressing.Bandaged;
        Treatment.Fracture(sim.Data.Medicine!, s.Body, BodyPart.LeftArm);
        Assert.True(sim.Execute(new SetCarePriorities([TreatPriority.Fracture, TreatPriority.Bleeding])).Ok);

        var loaded = SaveGame.Load(sim.Data, SaveGame.Save(sim.State), sim.Scavenge);
        var back = loaded.State.FindSurvivor(s.Id)!;
        Assert.Equal(Dressing.Bandaged, back.Body[BodyPart.LeftArm].Dressing);
        Assert.Equal("fracture", Assert.Single(back.Body.Conditions).Id);
        Assert.Equal([TreatPriority.Fracture, TreatPriority.Bleeding], Medicine.Priorities(loaded.State, loaded.Data));
        Assert.Equal(sim.Data.Medicine!.Fracture.Penalty, back.Body[BodyPart.LeftArm].ConditionPenalty, 9);
    }
}

public class FieldMedicineTests
{
    [Fact]
    public void A_blood_bag_gets_a_downed_survivor_up_with_more_hp_than_a_med_kit()
    {
        var medic = SurvivorUnit(pos: new(0, 0), pack: ["blood_bag"]);
        var hurt = SurvivorUnit(pos: new(1, 0));
        var sim = Arena(4, 4, [medic, hurt]);
        Combat.ApplyDamage(sim, hurt, BodyPart.Torso, 100);
        Assert.Equal(UnitStatus.Downed, hurt.Status);

        var result = sim.Execute(new UseItem(1, medic.Id, "blood_bag", hurt.Id));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(UnitStatus.Active, hurt.Status);
        Assert.Equal(45, hurt.Hp);
        Assert.Empty(medic.Pack);
    }

    [Fact]
    public void A_stim_gives_an_ap_now_and_takes_one_next_turn()
    {
        var a = SurvivorUnit(pos: new(0, 0), pack: ["stim"]);
        var sim = Arena(6, 6, [a]);
        var ap = a.Ap;
        Assert.True(sim.Execute(new UseItem(1, a.Id, "stim")).Ok);
        Assert.Equal(ap + 1, a.Ap);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(ap - 1, a.Ap);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Crashed);
    }

    [Fact]
    public void The_calm_injector_heals_stops_pinning_and_costs_humanity()
    {
        var a = SurvivorUnit(pos: new(0, 0), pack: ["calm_injector"]);
        var sim = Arena(6, 6, [a]);
        a.Hp = 30;
        a.Suppressed = true;
        Assert.True(sim.Execute(new UseItem(1, a.Id, "calm_injector")).Ok);
        Assert.Equal(70, a.Hp);
        Assert.False(a.Suppressed);
        Assert.True(a.CalmUntilRound >= sim.State.Round);
        Assert.Equal(Body.MaxHumanity - 4, a.Body.Humanity, 9);
    }

    [Fact]
    public void Field_treatments_dress_splint_and_cure_and_travel_home_on_the_body()
    {
        var med = ScavengeTestData.Real.Medicine!;
        var medic = SurvivorUnit(pos: new(0, 0), pack: ["splint", "antiseptic", "bandage"]);
        var hurt = SurvivorUnit(pos: new(1, 0));
        var sim = Arena(4, 4, [medic, hurt]);
        hurt.Body.Injure(BodyPart.LeftLeg, 0.5);
        Treatment.Fracture(med, hurt.Body, BodyPart.LeftLeg);
        var legBefore = hurt.Body.PartEfficiency(BodyPart.LeftLeg);

        Assert.True(sim.Execute(new UseItem(1, medic.Id, "splint", hurt.Id)).Ok);
        Assert.True(hurt.Body.PartEfficiency(BodyPart.LeftLeg) > legBefore);
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "antiseptic", hurt.Id)).Ok);
        Assert.Equal(Dressing.Clean, hurt.Body[BodyPart.LeftLeg].Dressing);
        // Full HP and every wound already clean: a bandage does nothing.
        medic.Ap = 3;
        Assert.Equal(ScavengeError.NotUsable, sim.Execute(new UseItem(1, medic.Id, "bandage", hurt.Id)).Error);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Treated);
    }

    [Fact]
    public void A_fever_costs_ap_in_the_field_and_pain_does_not()
    {
        var a = SurvivorUnit(pos: new(0, 0));
        var sim = Arena(4, 4, [a]);
        var full = sim.Profile(a).MaxAp;
        a.Body.Injure(BodyPart.Torso, 0.7);
        Assert.True(Treatment.InPain(sim.Data.Medicine!, a.Body));
        Assert.Equal(full, sim.Profile(a).MaxAp);
        Treatment.Afflict(a.Body, "infection", 0.6, BodyPart.Torso);
        Assert.Equal(full - 1, sim.Profile(a).MaxAp);
        Treatment.Afflict(a.Body, "husk_sickness", 0.6);
        Assert.Equal(full - 1, sim.Profile(a).MaxAp); // capped at field.maxApPenalty
        Assert.True(sim.Profile(a).MaxAp >= 2);
    }

    [Fact]
    public void Combat_medics_pay_less_ap_for_medical_items()
    {
        var a = SurvivorUnit(pos: new(0, 0), pack: ["splint"]);
        a.Hooks[PerkHooks.MedicalApDiscount] = 1;
        var sim = Arena(4, 4, [a]);
        a.Body.Injure(BodyPart.LeftArm, 0.5);
        Treatment.Fracture(ScavengeTestData.Real.Medicine!, a.Body, BodyPart.LeftArm);
        var ap = a.Ap;
        Assert.True(sim.Execute(new UseItem(1, a.Id, "splint")).Ok);
        Assert.Equal(ap - 1, a.Ap);
    }

    [Fact]
    public void Hard_hits_on_a_limb_sometimes_break_it()
    {
        var broke = 0;
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var a = SurvivorUnit(pos: new(0, 0));
            var sim = Arena(4, 4, [a], seed: seed);
            Combat.ApplyDamage(sim, a, BodyPart.LeftLeg, 20);
            if (a.Body.Has("fracture")) broke++;
        }
        Assert.InRange(broke, 5, 40);
    }
}
