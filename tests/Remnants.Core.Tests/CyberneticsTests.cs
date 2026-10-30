using Remnants.Core.Schematics;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

internal static class CyberTestColony
{
    /// <summary>A Town Hall 3 colony with food and enough shelter that nobody leaves while a test runs.</summary>
    public static ColonySim Make(ulong seed = 0)
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 5000);
        if (seed != 0)
        {
            sim.State.RngState = new Random.DeterministicRng(seed).State;
            sim = new ColonySim(sim.Data, sim.State, sim.Scavenge);
        }
        for (var i = 0; i < 3; i++) sim.Build("shelter");
        return sim;
    }
}

public class CyberneticsDataTests
{
    private static CyberneticsData Cyber => TestData.Real.Cybernetics!;

    [Fact]
    public void The_tree_has_every_tier_per_limb_and_specialises_from_powered_up()
    {
        foreach (var slot in new[] { CyberSlot.Arm, CyberSlot.Leg })
            Assert.Equal([0, 1, 2, 3, 4], Cyber.Nodes.Values.Where(n => n.Slot == slot).Select(n => n.Tier).Distinct().Order());
        Assert.All(Cyber.Nodes.Values.Where(n => n.Spec is not null), n => Assert.True(n.Tier >= 2, n.Id));
        Assert.All(Cyber.Nodes.Values.Where(n => n.Slot == CyberSlot.Eye), n => Assert.True(n.Tier >= 3, n.Id));
        // A real tree: some design has more than one way in, and some design leads to several.
        Assert.Contains(Cyber.Nodes.Values, n => n.Requires.Count > 1);
        Assert.Contains(Cyber.Nodes.Values, n => Cyber.Nodes.Values.Count(m => m.Requires.Contains(n.Id)) > 2);
        Assert.Equal(["blade", "gun", "hacker", "labor", "precision"],
            Cyber.Nodes.Values.Where(n => n.Slot == CyberSlot.Arm && n.Spec is not null).Select(n => n.Spec!).Distinct().Order());
    }

    [Fact]
    public void Every_design_above_crude_is_made_somewhere_and_gated_by_a_perk()
    {
        foreach (var node in Cyber.Nodes.Values.Where(n => n.Tier > 0))
        {
            Assert.NotNull(node.Perk);
            Assert.NotNull(Cybernetics.RecipeFor(TestData.Real, node));
        }
    }

    [Fact]
    public void Every_prosthetic_item_is_a_node_and_the_new_hooks_are_known()
    {
        Assert.All(Real.Items.Values.Where(i => i.Prosthetic is not null && i.BaseId is null), i => Assert.NotNull(Cyber.Node(i.Id)));
        foreach (var hook in new[] { PerkHooks.SurgerySuccessBonus, PerkHooks.SurgerySpeedBonus, PerkHooks.CyberSurgery,
                     PerkHooks.CyberInterferenceReduction, PerkHooks.ArmorBonus, PerkHooks.ThermalRadius, PerkHooks.NightDetectionBonus })
            Assert.Contains(hook, PerkHooks.All.Keys);
    }

    [Fact]
    public void A_node_requiring_a_higher_tier_is_rejected()
    {
        var def = Cyber.Def with
        {
            Nodes = Cyber.Def.Nodes.Select(n => n.Id == "mech_leg" ? n with { Requires = ["runner_leg"] } : n).ToList(),
        };
        Assert.Contains("lower tier", Assert.Throws<GameDataException>(() => new CyberneticsData(def)).Message);
    }
}

public class CyberneticsTreeTests
{
    private static ColonySim Colony()
    {
        var sim = CyberTestColony.Make();
        foreach (var (res, n) in new Dictionary<string, double> { ["scrap"] = 500, ["wood"] = 200, ["cloth"] = 100, ["electronics"] = 100, ["medicine"] = 50 })
            sim.State.Stockpile.Add(res, n);
        // The tree's own rules are under test here: every tier and specialisation schematic is known except T3 and T4.
        foreach (var s in sim.Data.Schematics.All.Values.Where(s => s.Unlocks.Any(u => u.Kind == UnlockKind.Prosthetic && u.Id is not ("tier:3" or "tier:4"))))
            sim.State.Schematics.Known.Add(s.Id);
        return sim;
    }

    private static CyberNodeStatus Status(ColonySim sim, string id) => Cybernetics.Status(sim, sim.Data.Cybernetics!.Nodes[id]);

    [Fact]
    public void Each_tier_needs_the_tier_below_known_a_perk_and_the_building()
    {
        var sim = Colony();
        var workshop = sim.Build("workshop");
        var crafter = sim.AddSurvivor(attributes: 60);

        Assert.Equal(CyberNodeState.Known, Status(sim, "peg_leg").State);
        var mech = Status(sim, "mech_leg");
        Assert.Equal(CyberNodeState.Locked, mech.State);
        Assert.Contains(mech.Missing, m => m.Contains("Tinkerer"));
        Assert.Contains(mech.Missing, m => m.Contains("Workshop level 2"));
        Assert.Equal(CommandError.RecipeLevelTooLow, sim.Execute(new QueueRecipe(workshop.Id, "mech_leg")).Error);

        workshop.Level = 2;
        Assert.Equal(CommandError.RecipeLocked, sim.Execute(new QueueRecipe(workshop.Id, "mech_leg")).Error);
        crafter.Perks.Add("tinkerer");
        Assert.Equal(CyberNodeState.Available, Status(sim, "mech_leg").State);
        Assert.True(sim.Execute(new QueueRecipe(workshop.Id, "mech_leg")).Ok);

        // Powered legs need a mechanical one known, and Biomech.
        var lab = sim.Build("electronics_lab");
        crafter.Perks.Add("biomech");
        var runner = Status(sim, "runner_leg");
        Assert.Equal(CyberNodeState.Locked, runner.State);
        Assert.Equal(["know Mechanical Leg"], runner.Missing);
        Assert.Equal(CommandError.RecipeLocked, sim.Execute(new QueueRecipe(lab.Id, "runner_leg")).Error);

        // The tier-3 schematic skips a tier: Cybernetics opens cyborg designs without a powered one known.
        crafter.Perks.Add("neural_interface");
        Assert.Contains(Status(sim, "cyber_leg").Missing, m => m.StartsWith("know Runner Leg or Silent Leg or Armored Leg"));
        Assert.Contains("Schematic: Cybernetics", Status(sim, "cyber_leg").Missing);
        sim.State.Schematics.Known.Add("sch_cybernetics");
        Assert.DoesNotContain(Status(sim, "cyber_leg").Missing, m => m.StartsWith("know") || m.StartsWith("Schematic"));

        sim.State.Inventory.Add("mech_leg");
        sim.Tick();
        Assert.Equal(CyberNodeState.Known, Status(sim, "mech_leg").State);
        Assert.Equal(CyberNodeState.Available, Status(sim, "runner_leg").State);
        Assert.Equal(1, Status(sim, "mech_leg").Owned);
    }

    [Fact]
    public void Crafting_a_design_teaches_it_to_the_colony()
    {
        var sim = Colony();
        var workshop = sim.Build("workshop");
        workshop.Level = 2;
        var crafter = sim.AddSurvivor(attributes: 70);
        crafter.Set(Skill.Crafting, 30);
        crafter.Perks.Add("tinkerer");
        Assert.True(sim.Execute(new AssignJob(crafter.Id, workshop.Id)).Ok);
        Assert.True(sim.Execute(new QueueRecipe(workshop.Id, "mech_arm")).Ok);
        for (var i = 0; i < 200 && sim.State.Inventory.Count("mech_arm") == 0; i++) sim.Tick();
        sim.Tick();
        Assert.Contains("mech_arm", sim.State.KnownCybernetics);
    }

    [Fact]
    public void Perks_count_only_for_people_at_home()
    {
        var sim = Colony();
        var s = sim.AddSurvivor();
        s.Perks.Add("tinkerer");
        Assert.True(Cybernetics.ColonyHasPerk(sim.State, "tinkerer"));
        s.Away = true;
        Assert.False(Cybernetics.ColonyHasPerk(sim.State, "tinkerer"));
    }
}

public class SurgeryTests
{
    private static (ColonySim Sim, Building Bay, Survivor Medic) Hospital(int level = 2, double medicine = 50, ulong seed = 0)
    {
        var sim = CyberTestColony.Make(seed);
        var bay = sim.Build("med_bay");
        bay.Level = level;
        sim.State.Stockpile.Add("medicine", medicine);
        sim.State.Stockpile.Add("scrap", 200);
        sim.State.Stockpile.Add("electronics", 200);
        var medic = sim.AddSurvivor(attributes: 70);
        medic.Set(Skill.Medicine, 45);
        Assert.True(sim.Execute(new AssignJob(medic.Id, bay.Id)).Ok);
        return (sim, bay, medic);
    }

    private static Survivor Amputee(ColonySim sim, BodyPart part)
    {
        var s = sim.AddSurvivor();
        s.Body.Lose(part);
        return s;
    }

    private static void RunUntilDone(ColonySim sim, int max = 24 * 20)
    {
        for (var i = 0; i < max && sim.State.Surgeries.Count > 0; i++) sim.Tick();
        Assert.Empty(sim.State.Surgeries);
    }

    [Fact]
    public void Powered_parts_go_on_by_surgery_which_takes_time_medicine_and_a_bed()
    {
        var (sim, bay, _) = Hospital();
        var s = Amputee(sim, BodyPart.LeftLeg);
        sim.State.Inventory.Add("runner_leg");
        Assert.Equal(CommandError.NeedsSurgery, sim.Execute(new FitProsthetic(s.Id, BodyPart.LeftLeg, "runner_leg")).Error);

        var booked = sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftLeg, "runner_leg"));
        Assert.True(booked.Ok, booked.Message);
        Assert.Equal(0, sim.State.Inventory.Count("runner_leg"));
        Assert.Equal(bay.Id, s.PatientAtBuildingId);
        Assert.Equal(PartCondition.Missing, s.Body[BodyPart.LeftLeg].Condition);
        var medicine = sim.State.Stockpile.Get("medicine");

        sim.Tick();
        Assert.Equal(bay.Id, s.PatientAtBuildingId); // not discharged although not injured
        Assert.True(sim.State.Surgeries[0].Progress > 0);
        RunUntilDone(sim);

        var leg = s.Body[BodyPart.LeftLeg];
        Assert.Equal((PartCondition.Prosthetic, "runner_leg"), (leg.Condition, leg.ProstheticItemId));
        Assert.Equal(8, leg.HumanityCost);
        Assert.False(leg.Elective);
        Assert.Equal(92, s.Body.Humanity);
        Assert.True(sim.State.Stockpile.Get("medicine") < medicine);
        Assert.Contains(sim.State.Events, e => e.Type is ColonyEventType.SurgeryDone or ColonyEventType.SurgeryComplication);
    }

    [Fact]
    public void Surgery_needs_a_medic_and_cyborg_parts_a_cyber_surgeon()
    {
        var (sim, bay, medic) = Hospital();
        var s = Amputee(sim, BodyPart.RightArm);
        sim.State.Inventory.Add("cyber_arm");
        Assert.True(sim.Execute(new ScheduleSurgery(s.Id, BodyPart.RightArm, "cyber_arm")).Ok);
        Assert.Equal("needs a Cyber-Surgeon at the Med Bay", Cybernetics.Blocked(sim, bay, 3));
        sim.Tick();
        Assert.Equal(0, sim.State.Surgeries[0].Progress);

        medic.Perks.Add("cyber_surgeon");
        Assert.Null(Cybernetics.Blocked(sim, bay, 3));
        Assert.True(sim.Execute(new Unassign(medic.Id)).Ok);
        Assert.Equal("needs a medic at the Med Bay", Cybernetics.Blocked(sim, bay, 3));
        Assert.True(sim.Execute(new AssignJob(medic.Id, bay.Id)).Ok);
        RunUntilDone(sim);
        Assert.Equal(15, s.Body[BodyPart.RightArm].HumanityCost);
    }

    [Fact]
    public void Surgeon_perks_raise_the_odds_and_the_speed()
    {
        var (sim, bay, medic) = Hospital();
        var odds = Cybernetics.SuccessChance(sim, bay, 3, elective: false);
        var speed = Cybernetics.EffortPerTick(sim, bay);
        medic.Perks.UnionWith(["surgery_1", "surgery_2"]);
        Assert.True(Cybernetics.SuccessChance(sim, bay, 3, elective: false) > odds + 0.15);
        Assert.Equal(speed * 1.5, Cybernetics.EffortPerTick(sim, bay), 6);
        Assert.True(Cybernetics.SuccessChance(sim, bay, 4, false) < Cybernetics.SuccessChance(sim, bay, 2, false));
        Assert.True(Cybernetics.SuccessChance(sim, bay, 3, true) < Cybernetics.SuccessChance(sim, bay, 3, false));
    }

    [Fact]
    public void A_failed_surgery_fits_the_part_but_leaves_an_infection_never_a_death()
    {
        var complications = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var (sim, _, medic) = Hospital(medicine: 200, seed: seed);
            medic.Set(Skill.Medicine, 0);
            var s = Amputee(sim, BodyPart.LeftLeg);
            sim.State.Inventory.Add("silent_leg");
            Assert.True(sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftLeg, "silent_leg")).Ok);
            RunUntilDone(sim);
            Assert.Equal(PartCondition.Prosthetic, s.Body[BodyPart.LeftLeg].Condition);
            if (!sim.State.Events.Any(e => e.Type == ColonyEventType.SurgeryComplication)) continue;
            complications++;
            Assert.Equal(PartCondition.Injured, s.Body[BodyPart.Torso].Condition);
            Assert.NotNull(s.PatientAtBuildingId);
            for (var i = 0; i < 24 * 30 && s.IsInjured; i++) sim.Tick();
            Assert.Contains(s, sim.State.Survivors);
            Assert.False(s.IsInjured);
        }
        Assert.InRange(complications, 3, 20); // 70% odds at Medicine 0 for a powered part
    }

    [Fact]
    public void Replacing_a_prosthetic_gives_the_old_one_back()
    {
        var (sim, _, _) = Hospital();
        var s = Amputee(sim, BodyPart.LeftLeg);
        sim.State.Inventory.Add("peg_leg");
        sim.State.Inventory.Add("mech_leg");
        Assert.True(sim.Execute(new FitProsthetic(s.Id, BodyPart.LeftLeg, "peg_leg")).Ok);
        Assert.Equal(0, s.Body[BodyPart.LeftLeg].HumanityCost);
        Assert.True(sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftLeg, "mech_leg")).Ok);
        RunUntilDone(sim);
        Assert.Equal("mech_leg", s.Body[BodyPart.LeftLeg].ProstheticItemId);
        Assert.Equal(3, s.Body[BodyPart.LeftLeg].HumanityCost);
        Assert.Equal(1, sim.State.Inventory.Count("peg_leg"));
    }

    [Fact]
    public void Elective_augmentation_is_cyborg_grade_only_adults_only_and_needs_confirming()
    {
        var (sim, _, medic) = Hospital();
        medic.Perks.Add("cyber_surgeon");
        var s = sim.AddSurvivor();
        var child = sim.AddSurvivor(LifeStage.Child);
        sim.State.Inventory.Add("runner_leg");
        sim.State.Inventory.Add("scope_eye");
        sim.State.Inventory.Add("cyber_leg", 2);

        Assert.Equal(CommandError.ElectiveNotAllowed, sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftLeg, "runner_leg", Elective: true)).Error);
        var unconfirmed = sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftLeg, "cyber_leg"));
        Assert.Equal(CommandError.ElectiveNotAllowed, unconfirmed.Error);
        Assert.Contains("−35 humanity", unconfirmed.Message);
        Assert.Equal(CommandError.ElectiveNotAllowed, sim.Execute(new ScheduleSurgery(child.Id, BodyPart.LeftLeg, "cyber_leg", Elective: true)).Error);
        Assert.Equal(CommandError.WrongLimb, sim.Execute(new ScheduleSurgery(s.Id, BodyPart.Head, "scope_eye", Elective: true)).Error);
        s.Body.Injure(BodyPart.RightLeg, 0.2);
        Assert.Equal(CommandError.PartInjured, sim.Execute(new ScheduleSurgery(s.Id, BodyPart.RightLeg, "cyber_leg", Elective: true)).Error);

        Assert.True(sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftLeg, "cyber_leg", Elective: true)).Ok);
        Assert.Equal(CommandError.AlreadyBooked, sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftLeg, "cyber_leg", Elective: true)).Error);
        Assert.True(sim.Execute(new ScheduleSurgery(s.Id, BodyPart.Eyes, "scope_eye", Elective: true)).Ok);
        RunUntilDone(sim, 24 * 40);

        Assert.True(s.Body[BodyPart.LeftLeg].Elective);
        Assert.Equal(35, s.Body[BodyPart.LeftLeg].HumanityCost);
        Assert.Equal(PartCondition.Prosthetic, s.Body[BodyPart.Eyes].Condition);
        Assert.Equal(30, s.Body.Humanity);
        Assert.True(s.Body.RegionEfficiency(BodyRegion.Head) > 1); // the scope eye sharpens the head's sight and aim
    }

    [Fact]
    public void Cancelling_returns_the_part_and_discharges()
    {
        var (sim, _, _) = Hospital();
        var s = Amputee(sim, BodyPart.LeftArm);
        sim.State.Inventory.Add("labor_arm");
        var id = sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftArm, "labor_arm")).EntityId!.Value;
        Assert.True(sim.Execute(new CancelSurgery(id)).Ok);
        Assert.Equal(1, sim.State.Inventory.Count("labor_arm"));
        Assert.Null(s.PatientAtBuildingId);
        Assert.Equal(CommandError.UnknownSurgery, sim.Execute(new CancelSurgery(id)).Error);
    }
}

public class CyberUpkeepAndHumanityTests
{
    private static (ColonySim Sim, Survivor S) WithPart(string item, BodyPart part, double scrap = 0, double electronics = 0)
    {
        var sim = CyberTestColony.Make();
        foreach (var res in new[] { "scrap", "electronics" }) sim.State.Stockpile.TryTake(res, sim.State.Stockpile.Get(res));
        sim.State.Stockpile.Add("scrap", scrap);
        sim.State.Stockpile.Add("electronics", electronics);
        var s = sim.AddSurvivor();
        s.Body.Lose(part);
        Cybernetics.Fit(sim.State, sim.Data.Cybernetics, s, part, sim.Scavenge!.Items[item], elective: false);
        return (sim, s);
    }

    [Fact]
    public void Powered_parts_are_serviced_from_the_stockpile_every_few_days()
    {
        var (sim, s) = WithPart("runner_leg", BodyPart.LeftLeg, scrap: 10, electronics: 10);
        sim.Advance(24 * 10);
        Assert.Equal(8, sim.State.Stockpile.Get("scrap"));
        Assert.Equal(9, sim.State.Stockpile.Get("electronics"));
        Assert.Equal(1, s.Body[BodyPart.LeftLeg].Upkeep);
    }

    [Fact]
    public void Without_parts_a_powered_limb_wears_down_and_a_service_restores_it()
    {
        var (sim, s) = WithPart("runner_leg", BodyPart.LeftLeg);
        var leg = s.Body[BodyPart.LeftLeg];
        sim.Advance(24 * 10);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.ProstheticNeedsService);
        sim.Advance(24 * 20);
        Assert.Equal(0.6, leg.Upkeep, 6);
        Assert.Equal(1.1 * 0.6, leg.Efficiency, 6);

        sim.State.Stockpile.Add("scrap", 2);
        sim.State.Stockpile.Add("electronics", 1);
        sim.Tick();
        Assert.Equal(1, leg.Upkeep);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.ProstheticServiced);
    }

    [Fact]
    public void Crude_and_mechanical_parts_need_no_upkeep()
    {
        var (sim, s) = WithPart("mech_leg", BodyPart.LeftLeg);
        sim.Advance(24 * 40);
        Assert.Equal(1, s.Body[BodyPart.LeftLeg].Upkeep);
    }

    [Fact]
    public void Low_humanity_caps_mood_strains_the_couple_and_the_children()
    {
        var sim = CyberTestColony.Make();
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        var kid = sim.AddSurvivor(LifeStage.Child);
        kid.MotherId = a.Id;
        (a.PartnerId, b.PartnerId) = (b.Id, a.Id);
        sim.State.Relationships.Add(new Relationship { A = a.Id, B = b.Id, Affinity = 100 });
        foreach (var part in new[] { BodyPart.LeftArm, BodyPart.RightArm, BodyPart.LeftLeg, BodyPart.RightLeg })
        {
            Cybernetics.Fit(sim.State, sim.Data.Cybernetics, a, part, sim.Scavenge!.Items["network_leg"].Prosthetic!.Region == Body.RegionOf(part)
                ? sim.Scavenge.Items["network_leg"] : sim.Scavenge.Items["network_arm"], elective: true);
        }
        Assert.Equal(0, a.Body.Humanity);

        sim.Tick();
        Assert.Equal(40, a.Mood, 6);
        Assert.Equal(70, kid.Mood, 6);
        Assert.Equal(100, b.Mood, 6);

        for (var i = 0; i < 24 * 60 && a.PartnerId is not null; i++) sim.Tick();
        Assert.Null(a.PartnerId);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.BrokeUp);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Humanity && e.Message.Contains(a.Name));
    }

    [Fact]
    public void The_mood_cap_starts_at_seventy_humanity()
    {
        var rules = TestData.Real.Cybernetics!.Humanity;
        Assert.Equal(100, Cybernetics.MoodCap(rules, 70));
        Assert.Equal(70, Cybernetics.MoodCap(rules, 35), 6);
        Assert.Equal(40, Cybernetics.MoodCap(rules, 0));
    }

    [Fact]
    public void Cybernetic_effects_feed_job_output_crafting_quality_and_hooks()
    {
        var (sim, s) = WithPart("labor_arm_mk2", BodyPart.LeftArm);
        var effects = PerkEffects.For(s, sim.Data);
        Assert.Equal(0.15, effects.JobOutputBonus("woodcutting"), 6);
        Assert.Equal(1.15, JobOutput.PerkFactor(sim.Data.Jobs["mining"], s, sim.Data), 6);

        Cybernetics.Fit(sim.State, sim.Data.Cybernetics, s, BodyPart.RightArm, sim.Scavenge!.Items["precision_arm"], elective: true);
        effects = PerkEffects.For(s, sim.Data);
        Assert.Equal(0.05, effects.CraftQualityBonus("crafting"), 6);
        Assert.Equal(0.03, effects.Hook(PerkHooks.AccuracyBonus), 6);
    }
}

public class CyberFieldTests
{
    private static Unit Cyborg(Pos pos, params (BodyPart Part, string Item)[] parts)
    {
        var u = SurvivorUnit(pos: pos);
        foreach (var (part, item) in parts) u.Body.Fit(part, Real.Items[item].Prosthetic!.Quality, item);
        u.CyberHooks = Real.CyberHooksFor(u.Body);
        return u;
    }

    private static UnitProfile Profile(Unit u) => ProfileDeriver.Derive(Real, u);

    [Fact]
    public void A_runner_pair_moves_further_and_armored_legs_trade_range_for_armor()
    {
        var plain = Profile(SurvivorUnit());
        var one = Profile(Cyborg(default, (BodyPart.LeftLeg, "runner_leg_mk2")));
        var pair = Profile(Cyborg(default, (BodyPart.LeftLeg, "runner_leg_mk2"), (BodyPart.RightLeg, "runner_leg_mk2")));
        Assert.True(pair.Movement.Primary.Range > one.Movement.Primary.Range);
        Assert.True(pair.Initiative > plain.Initiative);

        var armored = Profile(Cyborg(default, (BodyPart.LeftLeg, "armored_leg_mk2"), (BodyPart.RightLeg, "armored_leg_mk2")));
        Assert.Equal(2 + 2 + 1, armored.Armor);
        Assert.True(armored.Movement.Primary.Range <= 3);
        Assert.True(armored.Stealth < plain.Stealth);
    }

    [Fact]
    public void Jumper_legs_jump_like_a_knight_and_a_pair_jumps_cheaper()
    {
        var one = Profile(Cyborg(default, (BodyPart.LeftLeg, "jumper_leg")));
        var jump = Assert.Single(one.Movement.Modes, m => m.Pattern == MovePattern.Knight);
        Assert.Equal(2, jump.ApCost);
        var pair = Profile(Cyborg(default, (BodyPart.LeftLeg, "jumper_leg"), (BodyPart.RightLeg, "jumper_leg")));
        Assert.Equal(1, Assert.Single(pair.Movement.Modes, m => m.Pattern == MovePattern.Knight).ApCost);
    }

    [Fact]
    public void Blade_and_gun_arms_bring_their_own_attacks_and_the_hacker_arm_hacks()
    {
        var blade = Profile(Cyborg(default, (BodyPart.RightArm, "blade_arm_mk2")));
        Assert.NotNull(blade.Action("arm_blade"));
        var gun = Profile(Cyborg(default, (BodyPart.LeftArm, "gun_arm")));
        Assert.Equal(AttackPattern.Ranged, gun.Action("arm_gun")!.Pattern);

        var hacker = Cyborg(default, (BodyPart.LeftArm, "hacker_arm"));
        Assert.Equal("hacker_arm", Hacking.Tool(Real, hacker)?.Id);
        Assert.Null(Hacking.Tool(Real, SurvivorUnit(pack: ["hacker_arm"]))); // a spare arm in the pack is no tool
        Assert.Equal(0.05, hacker.Hook(PerkHooks.HackChanceBonus), 6);
    }

    [Fact]
    public void Eyes_sharpen_sight_and_a_thermal_eye_sees_through_walls_nearby()
    {
        var plain = Profile(SurvivorUnit());
        var scope = Profile(Cyborg(default, (BodyPart.Eyes, "scope_eye")));
        Assert.True(scope.Sight > plain.Sight + 1);
        var thermal = Cyborg(default, (BodyPart.Eyes, "thermal_eye"));
        Assert.Equal(2, Detection.ScanRadius(Real, thermal));
        Assert.Equal(0, Detection.ScanRadius(Real, SurvivorUnit()));
    }

    [Fact]
    public void Linked_nearby_lock_network_grade_parts_and_linked_salvage_halves_the_odds()
    {
        var cyborg = Cyborg(new(0, 0), (BodyPart.LeftLeg, "network_leg"), (BodyPart.RightLeg, "network_leg"));
        var far = Arena(20, 3, [cyborg, EnemyUnit("linked", new(15, 0))]);
        Assert.Equal(0, Interference.Chance(far, cyborg));

        var near = Arena(8, 3, [cyborg, EnemyUnit("linked", new(4, 0)), EnemyUnit("linked", new(5, 0))]);
        Assert.Equal(0.30, Interference.Chance(near, cyborg), 6);
        cyborg.Hooks[PerkHooks.CyberInterferenceReduction] = 0.5;
        Assert.Equal(0.15, Interference.Chance(near, cyborg), 6);
        cyborg.Hooks.Clear();

        var locked = 0;
        for (var round = 0; round < 200; round++)
        {
            Interference.RoundStarted(near);
            locked += cyborg.Body.Parts.Values.Count(p => p.Interfered);
        }
        Assert.InRange(locked, 80, 160); // 400 rolls at 30%
        Assert.Contains(near.State.Events, e => e.Type == ScavengeEventType.LimbLocked);
    }

    [Fact]
    public void A_locked_part_works_at_zero_and_loses_its_hooks_and_set()
    {
        var cyborg = Cyborg(default, (BodyPart.LeftArm, "network_arm"), (BodyPart.LeftLeg, "runner_leg"), (BodyPart.RightLeg, "runner_leg"));
        var before = Profile(cyborg);
        Assert.Equal(0.05, cyborg.Hook(PerkHooks.AccuracyBonus), 6);
        cyborg.Body[BodyPart.LeftArm].Interfered = true;
        Assert.Equal(0, cyborg.Body[BodyPart.LeftArm].Efficiency);
        Assert.Equal(0, cyborg.Hook(PerkHooks.AccuracyBonus));
        cyborg.Body[BodyPart.LeftLeg].Interfered = true;
        Assert.True(Profile(cyborg).Movement.Primary.Range < before.Movement.Primary.Range);
    }

    [Fact]
    public void Any_jammer_disrupts_network_grade_parts_even_your_own()
    {
        var cyborg = Cyborg(new(0, 0), (BodyPart.Eyes, "network_eye"));
        var ally = SurvivorUnit(pos: new(1, 0), tool: "signal_jammer");
        var sim = Arena(6, 3, [cyborg, ally]);
        Assert.Equal(0.20, Interference.Chance(sim, cyborg), 6);
    }

    [Fact]
    public void Party_units_bring_their_cybernetic_hooks_into_the_field_and_redacted_views_hide_them()
    {
        var s = ScavengeTestData.Survivor();
        s.Body.Fit(BodyPart.LeftArm, 1.0, "precision_arm");
        s.Body.Fit(BodyPart.RightArm, 1.0, "precision_arm");
        var hooks = Real.CyberHooksFor(s.Body);
        Assert.Equal(3, hooks.Count); // two arms and the Steady Pair set
        Assert.Equal([BodyPart.LeftArm, BodyPart.RightArm], hooks[2].Parts);
    }

    [Fact]
    public void A_killed_warden_leaves_three_linked_cores()
    {
        var warden = EnemyUnit("linked_warden", new(1, 1));
        var sim = Arena(3, 3, [SurvivorUnit(pos: new(0, 0)), warden]);
        Combat.ApplyDamage(sim, warden, BodyPart.Torso, 9999);
        Assert.Equal(3, sim.State.Caches.Single().Items.Count(i => i == "linked_core"));
    }
}

public class CyberSaveTests
{
    [Fact]
    public void A_version_9_save_maps_old_prosthetics_into_the_tree_and_grows_eyes()
    {
        var sim = LoopTestData.Colony();
        var s = sim.AddSurvivor();
        s.Body.Lose(BodyPart.LeftLeg);
        s.Body.FitProsthetic(BodyPart.LeftLeg, 1.2); // before items: no item id
        s.Body.Lose(BodyPart.RightArm);
        s.Body.FitProsthetic(BodyPart.RightArm, 0.5, "hook_arm");
        var json = SaveGame.Save(sim.State).Replace($"\"version\": {SaveGame.Version}", "\"version\": 9");
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        foreach (var survivor in node["colony"]!["survivors"]!.AsArray())
        {
            survivor!["body"]!["parts"]!.AsObject().Remove("Eyes");
            foreach (var (_, part) in survivor["body"]!["parts"]!.AsObject())
                foreach (var key in new[] { "humanityCost", "elective", "upkeep", "serviceTicks", "interfered" }) part!.AsObject().Remove(key);
        }

        var loaded = SaveGame.Load(TestData.Real, node.ToJsonString(), ScavengeTestData.Real);
        var body = loaded.State.Survivors.Single().Body;
        Assert.Equal(("cyber_leg", 1.2, 15.0), (body[BodyPart.LeftLeg].ProstheticItemId, body[BodyPart.LeftLeg].ProstheticQuality, body[BodyPart.LeftLeg].HumanityCost));
        Assert.Equal(0, body[BodyPart.RightArm].HumanityCost);
        Assert.Equal(1, body[BodyPart.LeftLeg].Upkeep);
        Assert.Equal(PartCondition.Healthy, body[BodyPart.Eyes].Condition);
        Assert.Equal(85, body.Humanity);
        Assert.Contains("cyber_leg", loaded.State.KnownCybernetics);
    }

    [Fact]
    public void Surgeries_and_knowledge_survive_a_save()
    {
        var sim = CyberTestColony.Make();
        sim.Build("med_bay").Level = 2;
        var s = sim.AddSurvivor();
        s.Body.Lose(BodyPart.LeftLeg);
        sim.State.Inventory.Add("runner_leg");
        Assert.True(sim.Execute(new ScheduleSurgery(s.Id, BodyPart.LeftLeg, "runner_leg")).Ok);
        sim.Tick();
        var loaded = SaveGame.Load(TestData.Real, SaveGame.Save(sim.State), ScavengeTestData.Real);
        Assert.Equal("runner_leg", Assert.Single(loaded.State.Surgeries).ItemId);
        Assert.Contains("runner_leg", loaded.State.KnownCybernetics);
    }
}
