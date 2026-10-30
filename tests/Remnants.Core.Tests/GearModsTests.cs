using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Gear mods and Mk upgrades: modded item ids, what mods do to weapons, armor and clothing, the Gear Bench in the colony
/// and Jury-rig in the field, schematic gates, the field effects (suppressor, light, scope, stock, EMP, shock, coverage,
/// padding, camo, weave), and modded gear through expeditions and saves.
/// </summary>
public class GearModsTests
{
    private static ItemDef Item(string id) => Real.Items[id];

    private static ColonySim Bench(params (string Id, int Count)[] items)
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, items: items.ToDictionary(i => i.Id, i => i.Count));
        sim.Build("workshop");
        sim.Build("armory");
        return sim;
    }

    [Fact]
    public void Gear_mods_load_with_their_items_recipes_and_schematics()
    {
        var mods = Real.GearMods!;
        Assert.All(mods.Def.Mods, m => Assert.Equal(ItemKind.Mod, Item(m.Id).Kind));
        Assert.All(mods.Def.Kits, k => Assert.Equal(ItemKind.Material, Item(k.Id).Kind));
        Assert.True(Item("mod_scope@fine").HasQuality, "crafted mods roll a quality");
        Assert.Equal(EquipSlot.Head, Item("helmet_scrap").Slot);
        Assert.Equal(ItemKind.Schematic, Item("sch_mod_scope").Kind);

        var catalog = TestData.Real.Schematics;
        Assert.Null(catalog.GateOfRecipe("mod_iron_sight"));
        Assert.Null(catalog.GateOfRecipe("kit_sidearm_mk2"));
        Assert.Null(catalog.GateOfRecipe("mod_armor_padding"));
        Assert.Equal("sch_mod_scope", catalog.GateOfRecipe("mod_scope"));
        Assert.Equal("sch_mod_camo", catalog.GateOfRecipe("mod_camo_foliage"));
        Assert.True(catalog.Find("sch_mod_emp_coil")!.Encrypted);
        Assert.Contains(Real.Enemies["linked"].Drops, d => d.Item == "sch_mod_emp_coil");
    }

    [Fact]
    public void A_modded_id_resolves_only_as_a_valid_build_in_canonical_order()
    {
        Assert.True(Real.Items.ContainsKey("pistol+mod_iron_sight+mod_suppressor"));
        Assert.False(Real.Items.ContainsKey("pistol+mod_suppressor+mod_iron_sight"), "mods go in slot order");
        Assert.False(Real.Items.ContainsKey("pistol+mod_iron_sight+mod_reflex_sight"), "one mod per slot");
        Assert.False(Real.Items.ContainsKey("pistol+mod_iron_sight+mod_suppressor+mod_stock"), "Mk I firearms take two mods");
        Assert.True(Real.Items.ContainsKey("pistol+mk2+mod_iron_sight+mod_suppressor+mod_stock"));
        Assert.False(Real.Items.ContainsKey("pistol+mod_sharpened_edge"), "a melee mod does not fit a gun");
        Assert.False(Real.Items.ContainsKey("pistol+mod_nothing"));
        Assert.False(Real.Items.ContainsKey("pistol+mk9"));
        Assert.False(Real.Items.ContainsKey("med_kit+mod_iron_sight"), "only gear in gear_mods.json takes mods");
        Assert.False(Real.Items.Values.Any(i => i.IsModded), "modded gear is never listed (never loot)");

        var item = Item("pistol@fine+mk2+mod_scope@fine");
        Assert.Equal("pistol", item.DefinedId);
        Assert.Equal("pistol@fine", item.UnmoddedId);
        Assert.Equal(2, item.Mk);
        Assert.Equal(["mod_scope@fine"], item.Mods);
        Assert.Same(item, Item("pistol@fine+mk2+mod_scope@fine"));
    }

    [Fact]
    public void Weapon_mods_trade_one_number_for_another()
    {
        var shoot = Item("pistol").Actions[0];
        var quiet = Item("pistol+mod_suppressor").Actions[0];
        Assert.True(quiet.Noise < shoot.Noise / 2, "a suppressor is much quieter");
        Assert.Equal(shoot.Reveal - 1, quiet.Reveal);
        Assert.True(quiet.MaxDamage < shoot.MaxDamage, "and loses a little damage");

        var scoped = Item("pistol+mod_scope").Actions[0];
        Assert.Equal(shoot.Reach + 2, scoped.Reach);
        Assert.True(scoped.Accuracy > shoot.Accuracy);
        Assert.Equal(1, scoped.CloseApPenalty);

        Assert.True(Item("pistol+mod_long_barrel").Weight > Item("pistol").Weight + 1);
        Assert.Equal(Item("linked_carbine").Actions[0].Accuracy + 0.09, Item("linked_carbine+mod_compensator").Actions[0].Accuracy, 6);
        Assert.Equal(shoot.Accuracy + 0.03, Item("pistol+mod_compensator").Actions[0].Accuracy, 6);

        var heavy = Item("pipe+mod_weighted_head").Actions[0];
        Assert.Equal(Item("pipe").Actions[0].ApCost + 1, heavy.ApCost);
        Assert.True(heavy.MaxDamage > Item("pipe").Actions[0].MaxDamage * 1.5);

        // The rifle's butt strike is melee: a scope does nothing for it.
        Assert.Equal(Item("rifle").Actions[1].Accuracy, Item("rifle+mod_scope").Actions[1].Accuracy);
        Assert.Equal(Item("rifle").Actions[1].Reach, Item("rifle+mod_scope").Actions[1].Reach);
    }

    [Fact]
    public void Quality_scales_a_mods_benefits_up_and_its_drawbacks_down()
    {
        int Noise(string id) => Item(id).Actions[0].Noise;
        double Dmg(string id) => Item(id).Actions[0].MaxDamage;
        Assert.True(Noise("pistol+mod_suppressor@fine") < Noise("pistol+mod_suppressor") || Noise("pistol+mod_suppressor@fine") == Noise("pistol+mod_suppressor"));
        Assert.True(Noise("pistol+mod_suppressor@crude") >= Noise("pistol+mod_suppressor"));
        Assert.True(Dmg("rifle+mod_suppressor@fine") >= Dmg("rifle+mod_suppressor"), "a fine suppressor costs less damage");
        Assert.True(Item("pistol+mod_reflex_sight@fine").Actions[0].Accuracy > Item("pistol+mod_reflex_sight@crude").Actions[0].Accuracy);
        Assert.True(Item("padded_vest+mod_armor_scrap_plates@fine").Armor > Item("padded_vest+mod_armor_scrap_plates@crude").Armor);
    }

    [Fact]
    public void Mk_upgrades_stack_with_quality()
    {
        double Max(string id) => Item(id).Actions[0].MaxDamage;
        Assert.True(Max("pistol@fine+mk2") > Max("pistol@fine"));
        Assert.True(Max("pistol@fine+mk3") > Max("pistol@fine+mk2"));
        Assert.True(Max("pistol@fine+mk2") > Max("pistol+mk2"));
        Assert.True(Item("padded_vest+mk2").Armor > Item("padded_vest").Armor);
        Assert.True(Item("helmet_scrap+mk3").Coverage![BodyRegion.Head] <= 1);
    }

    [Fact]
    public void Armor_mods_change_the_piece()
    {
        var vest = Item("padded_vest");
        var plated = Item("padded_vest+mod_armor_scrap_plates");
        Assert.Equal(vest.Armor + 1.5, plated.Armor, 6);
        Assert.Equal(vest.Weight + 3, plated.Weight, 2);
        Assert.True(plated.Stealth < vest.Stealth);
        Assert.Equal(1, plated.MoveNoise);
        Assert.Equal(6, Item("rag_coat+mod_armor_webbing").CarryBonus, 6);
        Assert.Equal(0.2, Item("rag_coat+mod_armor_padding").InjuryResist, 6);
        Assert.True(Item("rag_coat+mod_stab_lining").MeleeArmor > 0);
        Assert.Contains("rain", Item("rag_coat+mod_waterproofing").WeatherProtection);
    }

    [Fact]
    public void The_bench_fits_swaps_removes_and_upgrades()
    {
        var sim = Bench(("pistol", 1), ("mod_iron_sight", 1), ("mod_reflex_sight", 1), ("mod_suppressor", 1), ("mod_stock", 1), ("kit_sidearm_mk2", 1), ("kit_melee_mk2", 1));
        var inv = sim.State.Inventory;

        Assert.True(sim.Execute(new InstallMod("pistol", "mod_iron_sight")).Ok);
        Assert.True(inv.Has("pistol+mod_iron_sight") && !inv.Has("pistol") && !inv.Has("mod_iron_sight"));

        var swap = sim.Execute(new InstallMod("pistol+mod_iron_sight", "mod_reflex_sight"));
        Assert.True(swap.Ok, swap.Message);
        Assert.True(inv.Has("pistol+mod_reflex_sight") && inv.Has("mod_iron_sight"), "the old sight comes back");

        Assert.True(sim.Execute(new InstallMod("pistol+mod_reflex_sight", "mod_suppressor")).Ok);
        Assert.Equal(CommandError.NoFreeModSlot, sim.Execute(new InstallMod("pistol+mod_reflex_sight+mod_suppressor", "mod_stock")).Error);
        Assert.Equal(CommandError.WrongKit, sim.Execute(new UpgradeGear("pistol+mod_reflex_sight+mod_suppressor", "kit_melee_mk2")).Error);
        Assert.True(sim.Execute(new UpgradeGear("pistol+mod_reflex_sight+mod_suppressor", "kit_sidearm_mk2")).Ok);
        Assert.True(sim.Execute(new InstallMod("pistol+mk2+mod_reflex_sight+mod_suppressor", "mod_stock")).Ok, "Mk II opens a third slot");

        var off = sim.Execute(new RemoveMod("pistol+mk2+mod_reflex_sight+mod_suppressor+mod_stock", "muzzle"));
        Assert.True(off.Ok, off.Message);
        Assert.True(inv.Has("pistol+mk2+mod_reflex_sight+mod_stock") && inv.Has("mod_suppressor"), "removing a mod returns it intact");
        Assert.Equal(CommandError.EmptyModSlot, sim.Execute(new RemoveMod("pistol+mk2+mod_reflex_sight+mod_stock", "muzzle")).Error);
        Assert.Equal(CommandError.UnknownItem, sim.Execute(new InstallMod("pistol+mk2+mod_reflex_sight+mod_stock", "kit_melee_mk2")).Error);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.GearModded);
    }

    [Fact]
    public void The_bench_needs_its_building_and_a_kit_can_take_gear_only_so_far()
    {
        var sim = LoopTestData.Colony(townHallLevel: 3, items: new() { ["pipe"] = 1, ["mod_wrapped_grip"] = 1, ["kit_melee_mk2"] = 2, ["kit_melee_mk3"] = 2 });
        Assert.Equal(CommandError.NoBench, sim.Execute(new InstallMod("pipe", "mod_wrapped_grip")).Error);
        sim.Build("workshop");
        Assert.True(sim.Execute(new InstallMod("pipe", "mod_wrapped_grip")).Ok);
        // Kits go on at the Armory or the Tailor.
        Assert.Equal(CommandError.NoBench, sim.Execute(new UpgradeGear("pipe+mod_wrapped_grip", "kit_melee_mk2")).Error);
        sim.Build("armory");
        Assert.Equal(CommandError.WrongKit, sim.Execute(new UpgradeGear("pipe+mod_wrapped_grip", "kit_melee_mk3")).Error);
        Assert.True(sim.Execute(new UpgradeGear("pipe+mod_wrapped_grip", "kit_melee_mk2")).Ok);
        Assert.True(sim.Execute(new UpgradeGear("pipe+mk2+mod_wrapped_grip", "kit_melee_mk3")).Ok);
        Assert.Equal(CommandError.MaxMk, sim.Execute(new UpgradeGear("pipe+mk3+mod_wrapped_grip", "kit_melee_mk2")).Error);
    }

    [Fact]
    public void A_fitted_gun_arm_takes_one_mod_and_stays_its_cybernetic_node()
    {
        var sim = Bench(("mod_laser", 1), ("mod_iron_sight", 1));
        var s = sim.AddSurvivor();
        s.Body.Lose(BodyPart.RightArm);
        s.Body.Fit(BodyPart.RightArm, 1.2, "gun_arm");
        Assert.True(sim.Execute(new InstallMod("gun_arm", "mod_laser", s.Id, BodyPart.RightArm)).Ok);
        Assert.Equal("gun_arm+mod_laser", s.Body[BodyPart.RightArm].ProstheticItemId);
        Assert.Equal(CommandError.NoFreeModSlot, sim.Execute(new InstallMod("gun_arm+mod_laser", "mod_iron_sight", s.Id, BodyPart.RightArm)).Error);
        Assert.Equal("gun_arm", Real.Cybernetics!.Node("gun_arm+mod_laser")!.Id);

        var unit = SurvivorUnit();
        unit.Body = s.Body;
        var arm = ProfileDeriver.Derive(Real, unit).Action("arm_gun")!;
        unit.Body = new Body();
        unit.Body.Lose(BodyPart.RightArm);
        unit.Body.Fit(BodyPart.RightArm, 1.2, "gun_arm");
        Assert.True(arm.Accuracy > ProfileDeriver.Derive(Real, unit).Action("arm_gun")!.Accuracy, "the laser helps the arm gun");
    }

    [Fact]
    public void Gated_mod_recipes_need_their_schematic_and_modded_gear_is_never_a_recipe_input()
    {
        var sim = Bench(("padded_vest+mod_armor_padding", 1));
        var armory = sim.State.Buildings.First(b => b.DefId == "armory");
        armory.Level = 3;
        Schematics.Schematics.Grant(sim, "sch_armory_3", Schematics.SchematicSource.Research);
        Assert.False(sim.Execute(new QueueRecipe(armory.Id, "mod_scope", 1)).Ok);
        Assert.False(GearModSchematics.RecipeUnlocked(sim, "mod_scope"));
        Assert.True(GearModSchematics.RecipeUnlocked(sim, "mod_iron_sight"), "the default set is open");
        Schematics.Schematics.Grant(sim, "sch_mod_scope", Schematics.SchematicSource.Loot);
        Assert.True(GearModSchematics.Has(sim, "sch_mod_scope"));
        Assert.True(sim.Execute(new QueueRecipe(armory.Id, "mod_scope", 1)).Ok);

        // Linked plating eats a padded vest: never a modded one.
        Schematics.Schematics.Grant(sim, "sch_linked_plating", Schematics.SchematicSource.Loot);
        sim.State.Stockpile.Add("scrap", 50);
        sim.State.Stockpile.Add("electronics", 10);
        var smith = sim.AddSurvivor();
        Assert.True(sim.Execute(new AssignJob(smith.Id, armory.Id)).Ok);
        Assert.True(sim.Execute(new SetRecipe(armory.Id, "linked_plating")).Ok);
        for (var i = 0; i < 48; i++) sim.Tick();
        Assert.True(sim.State.Inventory.Has("padded_vest+mod_armor_padding"));
        Assert.Equal(CraftStatus.WaitingForInputs, sim.CraftStatus(armory));
    }

    [Fact]
    public void Jury_rig_fits_and_removes_mods_in_the_field()
    {
        var unit = SurvivorUnit(weapon: "pistol", pack: ["mod_suppressor"]);
        var sim = Arena(8, 8, [unit]);
        Assert.Equal(ScavengeError.MissingPerk, sim.Execute(new FieldMod(1, unit.Id, "mod_suppressor")).Error);
        unit.Hooks["field_repair"] = 1;
        var ap = unit.Ap;
        var fit = sim.Execute(new FieldMod(1, unit.Id, "mod_suppressor"));
        Assert.True(fit.Ok, fit.Message);
        Assert.Equal("pistol+mod_suppressor", unit.Equipment[EquipSlot.Weapon]);
        Assert.Empty(unit.Pack);
        Assert.Equal(ap - Real.GearMods!.Def.Rules.FieldApCost, unit.Ap);
        unit.Ap = 5;
        Assert.True(sim.Execute(new FieldMod(1, unit.Id, RemoveSlot: "muzzle")).Ok);
        Assert.Equal("pistol", unit.Equipment[EquipSlot.Weapon]);
        Assert.Equal(["mod_suppressor"], unit.Pack);
    }

    [Fact]
    public void A_suppressor_keeps_husks_asleep()
    {
        bool Woke(string weapon)
        {
            var shooter = SurvivorUnit(pos: new Pos(1, 1), weapon: weapon);
            var target = EnemyUnit("husk", new Pos(3, 1));
            var sleeper = EnemyUnit("husk", new Pos(7, 1));
            var sim = Arena(12, 4, [shooter, target, sleeper]);
            Assert.True(sim.Execute(new Attack(1, shooter.Id, target.Id, "shoot")).Ok);
            return sleeper.Ai.Mode == AiMode.Hunting;
        }
        Assert.True(Woke("pistol"));
        Assert.False(Woke("pistol+mod_suppressor"));
    }

    [Fact]
    public void A_scope_costs_more_ap_up_close()
    {
        var shooter = SurvivorUnit(pos: new Pos(1, 1), weapon: "pistol+mod_scope");
        var near = EnemyUnit("husk", new Pos(2, 1));
        var sim = Arena(12, 4, [shooter, near]);
        var action = sim.Profile(shooter).Action("shoot")!;
        Assert.Equal(action.ApCost + 1, Combat.ApCost(action, shooter.Pos, near.Pos));
        Assert.Equal(action.ApCost, Combat.ApCost(action, shooter.Pos, new Pos(8, 1)));
        var ap = shooter.Ap;
        Assert.True(sim.Execute(new Attack(1, shooter.Id, near.Id, "shoot")).Ok);
        Assert.Equal(ap - action.ApCost - 1, shooter.Ap);
    }

    [Fact]
    public void Shooting_after_moving_costs_accuracy_a_stock_takes_most_of_it_and_the_Linked_aim_on_the_move()
    {
        double Acc(Unit u) => ProfileDeriver.Derive(Real, u).Action(u.Equipment[EquipSlot.Weapon] == "linked_carbine" ? "burst" : "shoot")!.Accuracy;
        var still = SurvivorUnit(weapon: "pistol");
        var moved = SurvivorUnit(weapon: "pistol");
        moved.Moved = true;
        var penalty = Real.GearMods!.Def.Rules.MovedAccuracyPenalty;
        Assert.Equal(Acc(still) - penalty, Acc(moved), 6);
        var stocked = SurvivorUnit(weapon: "pistol+mod_stock");
        var stockedStill = Acc(stocked);
        stocked.Moved = true;
        Assert.Equal(stockedStill - penalty * 0.3, Acc(stocked), 6);

        var linked = EnemyUnit("linked", default);
        var before = ProfileDeriver.Derive(Real, linked).Action("burst")!.Accuracy;
        linked.Moved = true;
        Assert.Equal(before, ProfileDeriver.Derive(Real, linked).Action("burst")!.Accuracy);
    }

    [Fact]
    public void A_weapon_light_sees_in_the_dark_and_gives_its_carrier_away()
    {
        bool Sees(string weapon, out bool seenBack)
        {
            var scout = SurvivorUnit(pos: new Pos(1, 3), weapon: weapon);
            scout.Facing = new Pos(1, 0);
            var husk = EnemyUnit("husk", new Pos(7, 3));
            var sim = Arena(12, 7, [scout, husk], setup: (_, s) => s.Environment = ScavengeEnvironment.At(23, DayPhase.Night));
            seenBack = Detection.SideSees(Real, sim.State, Unit.EnemySide, scout);
            return Detection.SideSees(Real, sim.State, "player:1", husk);
        }
        Assert.False(Sees("pistol", out var dark));
        Assert.True(Sees("pistol+mod_flashlight", out var lit));
        Assert.False(dark);
        Assert.True(lit, "the beam gives the carrier away");

        // Behind the beam the dark still hides.
        var scout = SurvivorUnit(pos: new Pos(6, 3), weapon: "pistol+mod_flashlight");
        scout.Facing = new Pos(1, 0);
        var behind = EnemyUnit("husk", new Pos(1, 3));
        var sim2 = Arena(12, 7, [scout, behind], setup: (_, s) => s.Environment = ScavengeEnvironment.At(23, DayPhase.Night));
        Assert.False(Detection.SideSees(Real, sim2.State, "player:1", behind));
        // By day the light does nothing.
        Assert.Null(GearEffects.ActiveLight(Real, Arena(4, 4, [SurvivorUnit(weapon: "pistol+mod_flashlight")]).State, scout));
    }

    [Fact]
    public void An_emp_coil_adds_its_bane_to_the_carbines_and_feeds_back_on_network_implants()
    {
        var shooter = SurvivorUnit(pos: new Pos(1, 1), weapon: "linked_carbine+mod_emp_coil");
        var linked = EnemyUnit("linked", new Pos(5, 1));
        var sim = Arena(10, 4, [shooter, linked]);
        var burst = sim.Profile(shooter).Action("burst")!;
        var bane = Combat.Bane(Real, burst, linked)!;
        Assert.Equal(1 + 1.0 + 0.4, bane.DamageMultiplier, 6);
        Assert.Equal(0.3 + 0.1, bane.HitBonus, 6);
        Assert.Null(Combat.Bane(Real, burst, EnemyUnit("husk", default)));
        Assert.NotNull(Combat.Bane(Real, sim.Profile(SurvivorUnit(weapon: "pistol+mod_incendiary")).Action("shoot")!, EnemyUnit("husk", default)));

        var always = burst with { NetworkFeedback = 1 };
        shooter.Body.Fit(BodyPart.LeftLeg, 1.4, "network_leg");
        GearEffects.Feedback(sim, shooter, always);
        Assert.True(shooter.Body[BodyPart.LeftLeg].Interfered);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.LimbLocked);
    }

    [Fact]
    public void A_shock_coil_stuns_a_drone_it_hits()
    {
        var unit = SurvivorUnit(pos: new Pos(1, 1), weapon: "pipe+mod_shock_coil", attributes: 90);
        unit.Skills[Skill.Melee] = 100;
        var drone = EnemyUnit("linked_drone", new Pos(2, 1));
        var sim = Arena(6, 4, [unit, drone], seed: 3);
        for (var i = 0; i < 10 && !drone.Suppressed && drone.IsActive; i++)
        {
            unit.Ap = 5;
            sim.Execute(new Attack(1, unit.Id, drone.Id, "swing"));
        }
        Assert.True(drone.Suppressed || !drone.IsActive);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Suppressed && e.Message.Contains("stunned"));
    }

    [Fact]
    public void Helmets_cover_the_head_padding_softens_wounds_linings_stop_claws()
    {
        var unit = SurvivorUnit(body: "rag_coat+mod_armor_padding");
        unit.Equipment[EquipSlot.Head] = "helmet_scrap";
        var p = ProfileDeriver.Derive(Real, unit);
        Assert.Equal(p.Armor + 2, p.ArmorAgainst(BodyRegion.Head, false), 6);
        Assert.Equal(p.Armor, p.ArmorAgainst(BodyRegion.Torso, false), 6);
        Assert.Equal(0.2, p.InjuryResist, 6);

        var lined = ProfileDeriver.Derive(Real, SurvivorUnit(body: "rag_coat+mod_stab_lining"));
        Assert.True(lined.ArmorAgainst(BodyRegion.Torso, true) > lined.ArmorAgainst(BodyRegion.Torso, false));

        // Padding takes a share off the injury a hit leaves.
        double Severity(string body)
        {
            var u = SurvivorUnit(body: body);
            var sim = Arena(4, 4, [u]);
            Combat.ApplyDamage(sim, u, BodyPart.Torso, 12, 1 - ProfileDeriver.Derive(Real, u).InjuryResist);
            return u.Body[BodyPart.Torso].Severity;
        }
        Assert.True(Severity("rag_coat+mod_armor_padding") < Severity("rag_coat"));
    }

    [Fact]
    public void Camo_works_in_its_biome_and_weave_and_masking_hide_from_the_Linked()
    {
        var watcher = EnemyUnit("linked", new Pos(1, 1));
        double Bonus(string body, string zone, int hour = 12)
        {
            var u = SurvivorUnit(body: body, pos: new Pos(4, 1));
            var sim = Arena(8, 4, [u, watcher], zoneId: zone, setup: (_, s) => s.Environment = ScavengeEnvironment.AtHour(hour));
            return GearEffects.StealthAgainst(Real, sim.State, u, watcher);
        }
        Assert.Equal(8, Bonus("rag_coat+mod_camo_urban", "suburb_ruins"), 6);
        Assert.Equal(0, Bonus("rag_coat+mod_camo_urban", "outskirts"), 6);
        Assert.Equal(8, Bonus("rag_coat+mod_camo_wasteland", "outskirts"), 6);
        Assert.True(Bonus("rag_coat+mod_network_weave", "outskirts") > 0);
        Assert.Equal(0, Bonus("rag_coat+mod_thermal_masking", "outskirts"), 6);
        Assert.True(Bonus("rag_coat+mod_thermal_masking", "outskirts", hour: 23) > 0, "masking hides body heat at night");

        Assert.Equal(0.5, GearEffects.InterferenceReduction(Real, SurvivorUnit(body: "rag_coat+mod_network_weave")), 6);
        Assert.Equal(2, GearEffects.HuskLure(Real, SurvivorUnit(body: "rag_coat+mod_network_weave")));
    }

    [Fact]
    public void Modded_gear_goes_on_an_expedition_and_survives_saves()
    {
        var sim = LoopTestData.Colony(items: new() { ["pistol@fine+mk2+mod_scope+mod_suppressor"] = 1, ["padded_vest+mod_armor_webbing"] = 1, ["helmet_scrap"] = 1 });
        var s = sim.AddSurvivor();
        var t = sim.AddSurvivor();
        var gear = new Dictionary<EquipSlot, string>
        {
            [EquipSlot.Weapon] = "pistol@fine+mk2+mod_scope+mod_suppressor", [EquipSlot.Body] = "padded_vest+mod_armor_webbing", [EquipSlot.Head] = "helmet_scrap",
        };
        var launch = sim.Execute(new LaunchExpedition("outskirts", [new ExpeditionMember(s.Id, gear), new ExpeditionMember(t.Id)]));
        Assert.True(launch.Ok, launch.Message);

        var loaded = SaveGame.Load(TestData.Real, SaveGame.Save(sim.State), Real);
        Assert.Equal(SaveGame.Save(sim.State), SaveGame.Save(loaded.State));

        var field = sim.OpenExpeditionScavenge();
        var unit = field.State.Units.First(u => u.Origin?.Id == s.Id);
        Assert.Equal("pistol@fine+mk2+mod_scope+mod_suppressor", unit.Equipment[EquipSlot.Weapon]);
        Assert.Equal("helmet_scrap", unit.Equipment[EquipSlot.Head]);
        Assert.True(field.Profile(unit).Action("shoot")!.Reach > Item("pistol").Actions[0].Reach);

        var again = ScavengeSave.Load(Real, ScavengeSave.Save(field.State));
        Assert.Equal(ScavengeSave.Save(field.State), ScavengeSave.Save(again.State));
    }

    [Fact]
    public void A_save_with_a_broken_mod_build_is_refused()
    {
        var sim = LoopTestData.Colony(items: new() { ["pistol+mod_iron_sight"] = 1 });
        Assert.NotNull(SaveGame.Load(TestData.Real, SaveGame.Save(sim.State), Real));
        sim.State.Inventory.Add("pistol+mod_sharpened_edge");
        Assert.ThrowsAny<Exception>(() => SaveGame.Load(TestData.Real, SaveGame.Save(sim.State), Real));
    }
}
