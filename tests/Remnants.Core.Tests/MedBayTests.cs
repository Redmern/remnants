using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class MedBayTests
{
    private static (ColonySim Sim, Building MedBay) WithMedBay(double medicine = 10, int level = 1)
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 1000);
        sim.Build("shelter"); // long stays outlast the homeless grace period
        var bay = sim.Build("med_bay");
        bay.Level = level;
        sim.State.Stockpile.Add("medicine", medicine);
        return (sim, bay);
    }

    private static Survivor Injured(ColonySim sim, BodyPart part, double severity)
    {
        var s = sim.AddSurvivor();
        s.Body.Injure(part, severity);
        return s;
    }

    [Fact]
    public void Admitted_patient_heals_consuming_medicine_and_is_discharged()
    {
        var (sim, bay) = WithMedBay();
        var s = Injured(sim, BodyPart.LeftLeg, 0.5);
        Assert.True(sim.Execute(new AdmitPatient(s.Id)).Ok);
        var eta = sim.HealEta(s)!.Value;
        var rate = sim.HealPerTick(bay);
        Assert.Equal(Math.Ceiling(0.5 / rate - 1e-9), eta);

        sim.Advance(eta - 1);
        Assert.Equal(PartCondition.Injured, s.Body[BodyPart.LeftLeg].Condition);
        sim.Advance(1);

        Assert.Equal(PartCondition.Healthy, s.Body[BodyPart.LeftLeg].Condition);
        Assert.Null(s.PatientAtBuildingId);
        Assert.Equal(10 - eta * sim.Data.Rules.Medical.MedicinePerPatientTick, sim.State.Stockpile.Get("medicine"), 6);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Discharged && e.SurvivorId == s.Id);
    }

    [Fact]
    public void Medic_skill_and_med_bay_level_speed_up_healing()
    {
        var (sim, bay) = WithMedBay();
        var unstaffed = sim.HealPerTick(bay);
        Assert.Equal(sim.Data.Rules.Medical.HealPerTick * sim.Data.Rules.Medical.UnstaffedCare, unstaffed, 9);

        var medic = sim.AddSurvivor(attributes: 50);
        Assert.True(sim.Execute(new AssignJob(medic.Id, bay.Id)).Ok);
        var staffed = sim.HealPerTick(bay);
        medic.Set(Skill.Medicine, 80);
        var skilled = sim.HealPerTick(bay);
        bay.Level = 2;
        var upgraded = sim.HealPerTick(bay);

        Assert.True(unstaffed < staffed && staffed < skilled && skilled < upgraded);
        Assert.Equal(skilled * 1.5, upgraded, 9);
    }

    [Fact]
    public void Medics_train_only_while_treating()
    {
        var (sim, bay) = WithMedBay();
        var medic = sim.AddSurvivor();
        Assert.True(sim.Execute(new AssignJob(medic.Id, bay.Id)).Ok);
        sim.Advance(5);
        Assert.Equal(0, medic.Get(Skill.Medicine));

        var s = Injured(sim, BodyPart.Torso, 0.9);
        Assert.True(sim.Execute(new AdmitPatient(s.Id)).Ok);
        sim.Advance(5);
        Assert.True(medic.Get(Skill.Medicine) > 0);
    }

    [Fact]
    public void Without_medicine_patients_heal_only_at_the_natural_rate_but_do_not_worsen()
    {
        var (sim, _) = WithMedBay(medicine: 0);
        var s = Injured(sim, BodyPart.LeftArm, 0.8);
        Assert.True(sim.Execute(new AdmitPatient(s.Id)).Ok);

        sim.Advance(10);

        Assert.Equal(0.8 - 10 * sim.Data.Rules.Medical.NaturalHealPerTick, s.Body[BodyPart.LeftArm].Severity, 6);
    }

    [Fact]
    public void Small_injuries_heal_on_their_own()
    {
        var sim = LoopTestData.Colony(food: 1000);
        var s = Injured(sim, BodyPart.LeftArm, 0.1);
        var ticks = (int)Math.Ceiling(0.1 / sim.Data.Rules.Medical.NaturalHealPerTick);

        sim.Advance(ticks);

        Assert.Equal(PartCondition.Healthy, s.Body[BodyPart.LeftArm].Condition);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Healed && e.SurvivorId == s.Id);
    }

    [Fact]
    public void Severe_untreated_limb_wound_worsens_until_the_limb_is_lost()
    {
        var sim = LoopTestData.Colony(food: 1000);
        var s = Injured(sim, BodyPart.RightLeg, 0.9);
        Assert.True(sim.IsWorsening(s));
        var rules = sim.Data.Rules.Medical;

        sim.Advance(1);
        Assert.Equal(0.9 + rules.WorsenPerTick, s.Body[BodyPart.RightLeg].Severity, 9);
        sim.Advance((int)Math.Ceiling(0.1 / rules.WorsenPerTick));

        Assert.Equal(PartCondition.Missing, s.Body[BodyPart.RightLeg].Condition);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.LimbLost && e.SurvivorId == s.Id);
    }

    [Fact]
    public void Severe_untreated_torso_wound_drains_health_and_can_kill()
    {
        var sim = LoopTestData.Colony(food: 0);
        var s = Injured(sim, BodyPart.Torso, 0.95);
        s.Health = 5;

        sim.Advance(10);

        Assert.DoesNotContain(s, sim.State.Survivors);
        Assert.Contains(sim.State.Memorial, m => m.SurvivorId == s.Id);
    }

    [Fact]
    public void Middling_wounds_stay_put_without_care()
    {
        var sim = LoopTestData.Colony(food: 1000);
        var s = Injured(sim, BodyPart.LeftArm, 0.45);
        sim.Advance(50);
        Assert.Equal(0.45, s.Body[BodyPart.LeftArm].Severity, 9);
    }

    [Fact]
    public void Admission_is_validated()
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 1000);
        var healthy = sim.AddSurvivor();
        var hurt = Injured(sim, BodyPart.LeftArm, 0.5);
        Assert.Equal(CommandError.NoMedBay, sim.Execute(new AdmitPatient(hurt.Id)).Error);

        var bay = sim.Build("med_bay");
        Assert.Equal(CommandError.NotInjured, sim.Execute(new AdmitPatient(healthy.Id)).Error);
        Assert.True(sim.Execute(new AdmitPatient(hurt.Id)).Ok);
        Assert.Equal(CommandError.IsPatient, sim.Execute(new AssignJob(hurt.Id, bay.Id)).Error);

        var patients = sim.Data.Buildings["med_bay"].Level(1).Patients;
        for (var i = 1; i < patients; i++) Assert.True(sim.Execute(new AdmitPatient(Injured(sim, BodyPart.Head, 0.5).Id)).Ok);
        Assert.Equal(CommandError.MedBayFull, sim.Execute(new AdmitPatient(Injured(sim, BodyPart.Head, 0.5).Id)).Error);

        Assert.True(sim.Execute(new Discharge(hurt.Id)).Ok);
        Assert.Equal(CommandError.NotAPatient, sim.Execute(new Discharge(hurt.Id)).Error);
    }
}

public class ProstheticTests
{
    private static (ColonySim Sim, Survivor S) Amputee(BodyPart part, int medBayLevel = 1, params string[] items)
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 1000, items: items.ToDictionary(i => i, _ => 1));
        sim.Build("med_bay").Level = medBayLevel;
        var s = sim.AddSurvivor();
        s.Body.Lose(part);
        return (sim, s);
    }

    [Fact]
    public void Fitting_uses_the_item_and_restores_the_limb()
    {
        var (sim, s) = Amputee(BodyPart.LeftLeg, 1, "peg_leg");
        var result = sim.Execute(new FitProsthetic(s.Id, BodyPart.LeftLeg, "peg_leg"));

        Assert.True(result.Ok, result.Message);
        var part = s.Body[BodyPart.LeftLeg];
        Assert.Equal((PartCondition.Prosthetic, 0.6, "peg_leg"), (part.Condition, part.ProstheticQuality, part.ProstheticItemId));
        Assert.Equal(0, sim.State.Inventory.Count("peg_leg"));
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.ProstheticFitted);
    }

    [Fact]
    public void Fitting_is_validated()
    {
        var (sim, s) = Amputee(BodyPart.LeftLeg, 1, "peg_leg", "cyber_leg", "hook_arm", "pipe");
        CommandError Fit(BodyPart part, string item) => sim.Execute(new FitProsthetic(s.Id, part, item)).Error;

        Assert.Equal(CommandError.PartNotMissing, Fit(BodyPart.RightLeg, "peg_leg"));
        Assert.Equal(CommandError.WrongLimb, Fit(BodyPart.LeftLeg, "hook_arm"));
        Assert.Equal(CommandError.NotAProsthetic, Fit(BodyPart.LeftLeg, "pipe"));
        Assert.Equal(CommandError.MedBayLevelTooLow, Fit(BodyPart.LeftLeg, "cyber_leg"));
        Assert.Equal(CommandError.UnknownItem, Fit(BodyPart.LeftLeg, "wooden_spoon"));

        var (bare, t) = Amputee(BodyPart.LeftLeg, 1);
        Assert.Equal(CommandError.MissingItems, bare.Execute(new FitProsthetic(t.Id, BodyPart.LeftLeg, "peg_leg")).Error);
        bare.State.Buildings.RemoveAll(b => b.DefId == "med_bay");
        bare.State.Inventory.Add("peg_leg");
        Assert.Equal(CommandError.NoMedBay, bare.Execute(new FitProsthetic(t.Id, BodyPart.LeftLeg, "peg_leg")).Error);
    }

    [Fact]
    public void Cybernetic_legs_beat_the_original_in_colony_jobs_and_in_the_field()
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 1000, items: new() { ["cyber_leg"] = 2 });
        sim.Build("med_bay").Level = 2;
        var farm = sim.Data.Buildings["farm"];
        var farming = sim.Data.Jobs["farming"];
        var healthy = sim.AddSurvivor(attributes: 60);
        var cyborg = sim.AddSurvivor(attributes: 60);
        var amputee = sim.AddSurvivor(attributes: 60);
        foreach (var part in new[] { BodyPart.LeftLeg, BodyPart.RightLeg })
        {
            cyborg.Body.Lose(part);
            Assert.True(sim.Execute(new FitProsthetic(cyborg.Id, part, "cyber_leg")).Ok);
        }
        amputee.Body.Lose(BodyPart.LeftLeg);

        double Output(Survivor s) => JobOutput.PerTick(farm, 1, farming, s, sim.Data.Rules.Output);
        Assert.True(Output(cyborg) > Output(healthy));
        Assert.True(Output(amputee) < Output(healthy));

        int Range(Survivor s) => sim.PreviewProfile(s).Movement.Primary.Range;
        Assert.True(Range(cyborg) > Range(healthy));
        Assert.Equal(1, Range(amputee));
    }

    [Fact]
    public void Prosthetic_arm_restores_two_handed_weapons()
    {
        var (sim, s) = Amputee(BodyPart.RightArm, 1, "hook_arm");
        var spear = new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = "spear" };
        Assert.Null(sim.PreviewProfile(s, spear).Action("thrust"));

        Assert.True(sim.Execute(new FitProsthetic(s.Id, BodyPart.RightArm, "hook_arm")).Ok);

        Assert.NotNull(sim.PreviewProfile(s, spear).Action("thrust"));
    }
}

public class WorkshopTests
{
    private static (ColonySim Sim, Building Workshop, Survivor Crafter) Workshop()
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 1000);
        var shop = sim.Build("workshop");
        var crafter = sim.AddSurvivor(attributes: 60);
        Assert.True(sim.Execute(new AssignJob(crafter.Id, shop.Id)).Ok);
        return (sim, shop, crafter);
    }

    private static int TicksFor(ColonySim sim, Building shop, string recipeId) =>
        (int)Math.Ceiling(sim.Data.Recipes[recipeId].Work / sim.CraftEffortPerTick(shop) - 1e-9);

    [Fact]
    public void Workshop_makes_a_peg_leg_from_wood_and_scrap()
    {
        var (sim, shop, crafter) = Workshop();
        sim.State.Stockpile.Add("wood", 15);
        sim.State.Stockpile.Add("scrap", 5);
        Assert.True(sim.Execute(new SetRecipe(shop.Id, "peg_leg")).Ok);
        var ticks = TicksFor(sim, shop, "peg_leg");

        sim.Advance(1);
        Assert.Equal(0, sim.State.Stockpile.Get("wood"), 6);
        sim.Advance(ticks - 1);

        Assert.Equal(1, sim.State.Inventory.Count("peg_leg"));
        Assert.True(crafter.Get(Skill.Crafting) > 0);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Crafted);
    }

    [Fact]
    public void Workshop_makes_medicine_and_idles_without_inputs()
    {
        var (sim, shop, _) = Workshop();
        var before = sim.State.Stockpile.Get("food");
        sim.State.Stockpile.Add("wood", 2);
        Assert.True(sim.Execute(new SetRecipe(shop.Id, "herbal_remedy")).Ok);

        sim.Advance(TicksFor(sim, shop, "herbal_remedy") + 20);

        Assert.Equal(sim.Data.Recipes["herbal_remedy"].Amount, sim.State.Stockpile.Get("medicine"), 6);
        var order = Assert.Single(shop.Queue);
        Assert.Equal(0, order.Progress);
        Assert.False(order.Paid);
    }

    [Fact]
    public void Recipes_are_validated_and_switching_refunds()
    {
        var (sim, shop, _) = Workshop();
        Assert.Equal(CommandError.UnknownRecipe, sim.Execute(new SetRecipe(shop.Id, "laser")).Error);
        Assert.Equal(CommandError.RecipeLevelTooLow, sim.Execute(new SetRecipe(shop.Id, "med_kit")).Error);

        sim.State.Stockpile.Add("wood", 15);
        sim.State.Stockpile.Add("scrap", 5);
        Assert.True(sim.Execute(new SetRecipe(shop.Id, "peg_leg")).Ok);
        sim.Advance(1);
        Assert.True(sim.Execute(new SetRecipe(shop.Id, null)).Ok);

        Assert.Equal(15, sim.State.Stockpile.Get("wood"), 6);
        Assert.Equal(5, sim.State.Stockpile.Get("scrap"), 6);
    }
}
