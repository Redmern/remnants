using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

/// <summary>Fixtures for the colony ↔ scavenge loop: a colony that knows the scavenge data.</summary>
public static class LoopTestData
{
    public static ColonySim Colony(int townHallLevel = 1, double food = 100, Dictionary<string, int>? items = null)
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = townHallLevel });
        state.Stockpile.Add("food", food);
        foreach (var (id, n) in items ?? new()) state.Inventory.Add(id, n);
        return new ColonySim(TestData.Real, state, ScavengeTestData.Real);
    }

    public static ExpeditionMember Member(Survivor s, string? weapon = null, string? body = null, string? tool = null, params string[] pack)
    {
        var equipment = new Dictionary<EquipSlot, string>();
        if (weapon is not null) equipment[EquipSlot.Weapon] = weapon;
        if (body is not null) equipment[EquipSlot.Body] = body;
        if (tool is not null) equipment[EquipSlot.Tool] = tool;
        return new ExpeditionMember(s.Id, equipment, pack.ToList());
    }

    public static ReturnedSurvivor Returned(Survivor s, Action<Survivor>? change = null, Dictionary<EquipSlot, string>? equipment = null, params string[] pack)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<Survivor>(System.Text.Json.JsonSerializer.Serialize(s, Json.Options), Json.Options)!;
        change?.Invoke(copy);
        return new ReturnedSurvivor(copy, false, equipment ?? new(), pack.ToList());
    }

    public static ExtractionResult Result(
        List<ReturnedSurvivor>? returned = null, List<Survivor>? deaths = null, List<ReturnedSurvivor>? recruits = null, bool complete = true)
    {
        var loot = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in (returned ?? []).Concat(recruits ?? []).SelectMany(r => r.Pack)) loot[id] = loot.GetValueOrDefault(id) + 1;
        return new ExtractionResult(ColonySim.ExpeditionPlayerId, complete, returned ?? [], recruits ?? [], deaths ?? [], loot);
    }
}

public class LaunchExpeditionTests
{
    [Fact]
    public void Launch_takes_gear_and_rations_and_marks_the_party_away()
    {
        var sim = LoopTestData.Colony(items: new() { ["pipe"] = 1, ["rag_coat"] = 1, ["bandage"] = 2 });
        var farm = sim.Build("farm");
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        Assert.True(sim.Execute(new AssignJob(a.Id, farm.Id)).Ok);

        var result = sim.Execute(new LaunchExpedition("outskirts",
            [LoopTestData.Member(a, weapon: "pipe", body: "rag_coat", pack: "bandage"), LoopTestData.Member(b, pack: "bandage")]));

        Assert.True(result.Ok, result.Message);
        Assert.True(a.Away && b.Away);
        Assert.Null(a.AssignedBuildingId);
        Assert.Empty(sim.State.Inventory.Counts);
        Assert.Equal(100 - 2 * sim.Data.Rules.Expedition.RationFoodPerMember, sim.State.Stockpile.Get("food"), 6);
        Assert.Equal("outskirts", sim.State.Expedition!.ZoneId);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.ExpeditionLaunched);
        Assert.Equal(CommandError.SurvivorAway, sim.Execute(new AssignJob(a.Id, farm.Id)).Error);
    }

    [Fact]
    public void Away_survivors_do_not_eat_colony_food()
    {
        var sim = LoopTestData.Colony(food: 10);
        var a = sim.AddSurvivor();
        var home = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Ok);
        var before = sim.State.Stockpile.Get("food");

        sim.Advance(10);

        Assert.Equal(before - 10 * sim.Data.Rules.Food.AdultPerTick, sim.State.Stockpile.Get("food"), 6);
        Assert.Equal(0, a.StarvingTicks);
        Assert.Contains(home, sim.State.Survivors);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void Party_size_cap_comes_from_the_town_hall_level(int level, int cap)
    {
        var sim = LoopTestData.Colony(townHallLevel: level, food: 1000);
        Assert.Equal(cap, sim.MaxPartySize);
        var members = Enumerable.Range(0, cap + 1).Select(_ => LoopTestData.Member(sim.AddSurvivor())).ToList();

        Assert.Equal(CommandError.PartyTooLarge, sim.Execute(new LaunchExpedition("outskirts", members)).Error);
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", members.Take(cap).ToList())).Ok);
    }

    [Fact]
    public void Launch_is_validated()
    {
        var sim = LoopTestData.Colony(food: 4, items: new() { ["pipe"] = 1 });
        var adult = sim.AddSurvivor();
        var other = sim.AddSurvivor();
        var child = sim.AddSurvivor(LifeStage.Child);

        CommandError Launch(string zone, params ExpeditionMember[] members) => sim.Execute(new LaunchExpedition(zone, members.ToList())).Error;

        Assert.Equal(CommandError.UnknownZone, Launch("moon", LoopTestData.Member(adult)));
        Assert.Equal(CommandError.EmptyParty, Launch("outskirts"));
        Assert.Equal(CommandError.NotAnAdult, Launch("outskirts", LoopTestData.Member(child)));
        Assert.Equal(CommandError.DuplicateMember, Launch("outskirts", LoopTestData.Member(adult), LoopTestData.Member(adult)));
        Assert.Equal(CommandError.InvalidGear, Launch("outskirts", new ExpeditionMember(adult.Id, new() { [EquipSlot.Body] = "pipe" })));
        Assert.Equal(CommandError.UnknownItem, Launch("outskirts", LoopTestData.Member(adult, pack: "laser")));
        Assert.Equal(CommandError.MissingItems, Launch("outskirts", LoopTestData.Member(adult, weapon: "pipe"), LoopTestData.Member(other, weapon: "pipe")));
        Assert.Equal(CommandError.CannotAfford, Launch("outskirts", LoopTestData.Member(adult), LoopTestData.Member(other)));

        Assert.Equal(CommandError.None, Launch("outskirts", LoopTestData.Member(adult, weapon: "pipe")));
        Assert.Equal(CommandError.ExpeditionActive, Launch("outskirts", LoopTestData.Member(other)));
        Assert.Equal(0, sim.State.Inventory.Count("pipe"));
    }

    [Fact]
    public void Colony_without_scavenge_data_cannot_launch()
    {
        var sim = TestData.EmptyColony(stock: new() { ["food"] = 10 });
        var a = sim.AddSurvivor();
        Assert.Equal(CommandError.NoScavengeData, sim.Execute(new LaunchExpedition("outskirts", [new ExpeditionMember(a.Id)])).Error);
    }

    [Fact]
    public void Scavenge_party_is_built_from_the_survivors_real_state()
    {
        var sim = LoopTestData.Colony(items: new() { ["spear"] = 1, ["med_kit"] = 1 });
        var a = sim.AddSurvivor(attributes: 70);
        a.Set(Skill.Melee, 33);
        a.Health = 80;
        a.Body.Injure(BodyPart.LeftArm, 0.4);
        a.Body.Lose(BodyPart.RightLeg);
        a.Body.FitProsthetic(BodyPart.RightLeg, 1.3, "cyber_leg");
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a, weapon: "spear", pack: "med_kit")])).Ok);

        var scavenge = sim.OpenExpeditionScavenge();

        var unit = Assert.Single(scavenge.State.Units, u => u.Kind == UnitKind.Survivor);
        Assert.Equal(a.Id, unit.Origin!.Id);
        Assert.Equal(80, unit.Hp);
        Assert.Equal(70, unit.Get(AttributeKind.Agility));
        Assert.Equal(33, unit.Get(Skill.Melee));
        Assert.Equal(0.4, unit.Body[BodyPart.LeftArm].Severity, 6);
        Assert.Equal(PartCondition.Prosthetic, unit.Body[BodyPart.RightLeg].Condition);
        Assert.Equal("spear", unit.Equipment[EquipSlot.Weapon]);
        Assert.Equal(["med_kit"], unit.Pack);
        Assert.Same(scavenge.State, sim.State.Expedition!.Scavenge);
    }

    [Fact]
    public void Preview_profile_shows_the_movement_mode_gear_gives()
    {
        var sim = LoopTestData.Colony();
        var a = sim.AddSurvivor(attributes: 50);

        Assert.Equal(MovePattern.King, sim.PreviewProfile(a).Movement.Primary.Pattern);
        Assert.Equal(MovePattern.Rook, sim.PreviewProfile(a, new() { [EquipSlot.Body] = "scout_jacket" }).Movement.Primary.Pattern);
        var armored = sim.PreviewProfile(a, new() { [EquipSlot.Body] = "riot_armor" }).Movement.Primary;
        Assert.Equal((MovePattern.King, 1), (armored.Pattern, armored.Range));
    }
}

public class ApplyExtractionResultTests
{
    private static (ColonySim Sim, Survivor A, Survivor B) Launched(params string[] gearA)
    {
        var sim = LoopTestData.Colony(items: gearA.GroupBy(g => g).ToDictionary(g => g.Key, g => g.Count()));
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        var memberA = new ExpeditionMember(a.Id, gearA.Where(g => g == "pipe").ToDictionary(_ => EquipSlot.Weapon, g => g), gearA.Where(g => g != "pipe").ToList());
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [memberA, new ExpeditionMember(b.Id)])).Ok);
        return (sim, a, b);
    }

    [Fact]
    public void Returned_survivors_bring_back_injuries_skills_gear_and_loot()
    {
        var (sim, a, b) = Launched("pipe");
        var result = LoopTestData.Result(returned:
        [
            LoopTestData.Returned(a, s =>
            {
                s.Health = 40;
                s.Body.Injure(BodyPart.LeftLeg, 0.5);
                s.Set(Skill.Melee, 12);
                s.Set(AttributeKind.Strength, 55);
            }, new() { [EquipSlot.Weapon] = "pipe" }, "scrap", "scrap", "machete", "med_supplies"),
            LoopTestData.Returned(b, s => s.Body.Lose(BodyPart.RightArm)),
        ]);

        var applied = sim.Execute(new ApplyExtractionResult(result));

        Assert.True(applied.Ok, applied.Message);
        Assert.Null(sim.State.Expedition);
        Assert.False(a.Away || b.Away);
        Assert.Equal(40, a.Health);
        Assert.Equal(0.5, a.Body[BodyPart.LeftLeg].Severity, 6);
        Assert.Equal(12, a.Get(Skill.Melee));
        Assert.Equal(55, a.Get(AttributeKind.Strength));
        Assert.Equal(PartCondition.Missing, b.Body[BodyPart.RightArm].Condition);

        Assert.Equal(1, sim.State.Inventory.Count("pipe"));
        Assert.Equal(1, sim.State.Inventory.Count("machete"));
        Assert.Equal(0, sim.State.Inventory.Count("scrap"));
        Assert.Equal(6, sim.State.Stockpile.Get("scrap"), 6);
        Assert.Equal(2, sim.State.Stockpile.Get("medicine"), 6);

        var report = sim.State.LastReport!;
        Assert.Equal(2, report.Returned.Count);
        Assert.Contains("LeftLeg injured (50%)", report.Returned.Single(r => r.SurvivorId == a.Id).NewInjuries);
        Assert.Contains("RightArm lost", report.Returned.Single(r => r.SurvivorId == b.Id).NewInjuries);
        Assert.Equal(2, report.Loot["scrap"]);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.ExpeditionReturned);
    }

    [Fact]
    public void Report_loot_leaves_out_what_the_party_took_from_home()
    {
        var (sim, a, b) = Launched("bandage");
        var result = LoopTestData.Result(returned: [LoopTestData.Returned(a, pack: ["bandage", "bandage"]), LoopTestData.Returned(b)]);

        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);

        Assert.Equal(new SortedDictionary<string, int> { ["bandage"] = 1 }, sim.State.LastReport!.Loot);
        Assert.Equal(2, sim.State.Inventory.Count("bandage"));
    }

    [Fact]
    public void The_dead_are_removed_for_good_with_their_gear()
    {
        var (sim, a, b) = Launched("pipe", "bandage");
        var result = LoopTestData.Result(returned: [LoopTestData.Returned(b)], deaths: [a]);

        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);

        Assert.DoesNotContain(a, sim.State.Survivors);
        var memorial = Assert.Single(sim.State.Memorial);
        Assert.Equal((a.Id, a.Name), (memorial.SurvivorId, memorial.Name));
        Assert.Contains("Town Outskirts", memorial.Cause);
        Assert.Contains(sim.State.Events, e => e.Type == ColonyEventType.Died && e.SurvivorId == a.Id);
        Assert.Equal(0, sim.State.Inventory.Count("pipe"));
        Assert.Equal(new SortedDictionary<string, int> { ["bandage"] = 1, ["pipe"] = 1 }, sim.State.LastReport!.LostGear);
        Assert.Equal([a.Name], sim.State.LastReport.Dead);
    }

    [Fact]
    public void A_wipe_loses_everyone_and_everything_they_carried()
    {
        var (sim, a, b) = Launched("pipe");
        Assert.True(sim.Execute(new ApplyExtractionResult(LoopTestData.Result(deaths: [a, b]))).Ok);

        Assert.Empty(sim.State.Survivors);
        Assert.Equal(2, sim.State.Memorial.Count);
        Assert.Empty(sim.State.Inventory.Counts);
    }

    [Fact]
    public void Recruits_join_as_new_adults_and_need_a_bed_within_the_grace_period()
    {
        var (sim, a, b) = Launched();
        var stranger = new Survivor { Name = "Wren", Sex = Sex.Female, Health = 70 };
        foreach (var attr in Enum.GetValues<AttributeKind>()) stranger.Set(attr, 40);
        var result = LoopTestData.Result(returned: [LoopTestData.Returned(a), LoopTestData.Returned(b)],
            recruits: [new ReturnedSurvivor(stranger, false, new(), ["cloth"])]);

        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);

        var wren = sim.State.Survivors.Single(s => s.Name == "Wren");
        Assert.NotEqual(0, wren.Id);
        Assert.DoesNotContain(sim.State.Survivors, s => s != wren && s.Id == wren.Id);
        Assert.Equal(LifeStage.Adult, wren.LifeStage);
        Assert.InRange(wren.AgeTicks / sim.Data.Rules.TicksPerYear, 18, 40);
        Assert.Equal(2, sim.State.Stockpile.Get("cloth"), 6);
        Assert.Equal(["Wren"], sim.State.LastReport!.Recruits);

        // No housing in this colony: the recruit leaves once the grace timer runs out.
        sim.Advance(sim.Data.Rules.Housing.HomelessGraceTicks);
        Assert.Contains(wren, sim.State.Survivors);
        sim.Advance(1);
        Assert.DoesNotContain(wren, sim.State.Survivors);
    }

    [Fact]
    public void Unfinished_or_foreign_results_are_rejected()
    {
        var (sim, a, b) = Launched();
        Assert.Equal(CommandError.ResultNotFinal, sim.Execute(new ApplyExtractionResult(LoopTestData.Result(returned: [LoopTestData.Returned(a)], complete: false))).Error);

        var outsider = new Survivor { Id = 999, Name = "Nobody" };
        Assert.Equal(CommandError.ResultMismatch, sim.Execute(new ApplyExtractionResult(LoopTestData.Result(returned: [LoopTestData.Returned(outsider)]))).Error);
        Assert.True(a.Away);

        Assert.True(sim.Execute(new ApplyExtractionResult(LoopTestData.Result(returned: [LoopTestData.Returned(a), LoopTestData.Returned(b)]))).Ok);
        Assert.Equal(CommandError.NoExpedition, sim.Execute(new ApplyExtractionResult(LoopTestData.Result())).Error);
    }

    [Fact]
    public void Full_loop_through_a_real_scavenge_run()
    {
        var sim = LoopTestData.Colony(items: new() { ["pipe"] = 1 });
        var a = sim.AddSurvivor();
        var b = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a, weapon: "pipe"), LoopTestData.Member(b)])).Ok);
        var scavenge = sim.OpenExpeditionScavenge();

        // Stand-in for play: the first survivor walks out with a find, the second falls.
        var units = scavenge.State.Units.Where(u => u.PlayerId == ColonySim.ExpeditionPlayerId).OrderBy(u => u.Id).ToList();
        units[0].Pack.Add("electronics");
        units[0].Body.Injure(BodyPart.Torso, 0.3);
        units[0].Status = UnitStatus.Extracted;
        units[1].Status = UnitStatus.Dead;
        scavenge.State.FindParty(ColonySim.ExpeditionPlayerId)!.Done = true;

        var applied = sim.Execute(new ApplyExtractionResult(scavenge.ResultFor(ColonySim.ExpeditionPlayerId)));

        Assert.True(applied.Ok, applied.Message);
        Assert.Equal([a], sim.State.Survivors);
        Assert.Equal(0.3, a.Body[BodyPart.Torso].Severity, 6);
        Assert.Equal(1, sim.State.Inventory.Count("pipe"));
        Assert.Equal(2, sim.State.Stockpile.Get("electronics"), 6);
        Assert.Single(sim.State.Memorial);
    }
}

public class ExpeditionSaveTests
{
    [Fact]
    public void Save_mid_mission_resumes_the_same_run()
    {
        var sim = LoopTestData.Colony(items: new() { ["pipe"] = 1 });
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a, weapon: "pipe")])).Ok);
        var scavenge = sim.OpenExpeditionScavenge();
        var unitId = scavenge.State.CurrentUnitId!.Value;
        Assert.True(scavenge.Execute(new EndTurn(ColonySim.ExpeditionPlayerId, unitId)).Ok);

        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(TestData.Real, json, ScavengeTestData.Real);
        var resumed = loaded.OpenExpeditionScavenge();

        Assert.True(loaded.State.FindSurvivor(a.Id)!.Away);
        Assert.Equal(ScavengeSave.Save(scavenge.State), ScavengeSave.Save(resumed.State));
        Assert.Equal(json, SaveGame.Save(loaded.State));
    }

    [Fact]
    public void Save_before_the_run_starts_rebuilds_it_from_the_seed()
    {
        var sim = LoopTestData.Colony();
        var a = sim.AddSurvivor();
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [LoopTestData.Member(a)])).Ok);
        var loaded = SaveGame.Load(TestData.Real, SaveGame.Save(sim.State), ScavengeTestData.Real);

        Assert.Equal(ScavengeSave.Save(sim.OpenExpeditionScavenge().State), ScavengeSave.Save(loaded.OpenExpeditionScavenge().State));
    }

    [Fact]
    public void Save_with_unknown_items_or_stray_away_flags_is_rejected()
    {
        var sim = LoopTestData.Colony(items: new() { ["pipe"] = 1 });
        var a = sim.AddSurvivor();
        var json = SaveGame.Save(sim.State).Replace("\"pipe\"", "\"laser\"");
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SaveGame.Load(TestData.Real, json, ScavengeTestData.Real));

        a.Away = true;
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SaveGame.Load(TestData.Real, SaveGame.Save(sim.State), ScavengeTestData.Real));
    }

    [Fact]
    public void Version_1_saves_still_load()
    {
        var json = SaveGame.Save(NewGame.Create(TestData.Real, 3).State).Replace("\"version\": 2", "\"version\": 1");
        var loaded = SaveGame.Load(TestData.Real, json, ScavengeTestData.Real);
        Assert.Null(loaded.State.Expedition);
    }

    [Fact]
    public void New_game_starts_with_the_configured_items()
    {
        var sim = NewGame.Create(TestData.Real, 1, ScavengeTestData.Real);
        foreach (var (id, count) in TestData.Real.Rules.NewGame.Items) Assert.Equal(count, sim.State.Inventory.Count(id));
    }
}
