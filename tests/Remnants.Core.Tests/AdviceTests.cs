using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class AdviceTests
{
    private static ColonySim Fresh() => NewGame.Create(TestData.Real, 1, ScavengeTestData.Real);

    private static Building Build(ColonySim sim, string id)
    {
        foreach (var (res, amount) in sim.Data.Buildings[id].Level(1).Cost) sim.State.Stockpile.Add(res, amount);
        var r = sim.Execute(new Construct(id));
        Assert.True(r.Ok, r.Message);
        return sim.State.FindBuilding(r.EntityId!.Value)!;
    }

    private static void Staff(ColonySim sim)
    {
        foreach (var s in sim.State.Survivors.Where(s => s.AssignedBuildingId is null && !s.IsInfant))
        foreach (var b in sim.State.Buildings.Where(b => sim.Data.Buildings[b.DefId].Job is { } job && (!s.IsChild || sim.Data.Jobs[job].ChildrenAllowed)))
            if (sim.FreeSlots(b) > 0 && sim.Execute(new AssignJob(s.Id, b.Id)).Ok) break;
    }

    [Fact]
    public void A_new_colony_is_told_to_build_a_farm_first_then_gatherers()
    {
        var sim = Fresh();
        var tip = Advice.Next(sim)!;
        Assert.Equal("farm", tip.Id);
        Assert.Contains("Farm", tip.Text);

        Build(sim, "farm");
        Assert.Equal("gather", Advice.Next(sim)!.Id);
        Build(sim, "woodcutter");
        Build(sim, "quarry");
        Assert.Equal("idle", Advice.Next(sim)!.Id);
    }

    [Fact]
    public void A_staffed_colony_is_pointed_at_the_first_expedition_then_the_med_bay_for_wounds()
    {
        var sim = Fresh();
        foreach (var id in new[] { "farm", "woodcutter", "quarry" }) Build(sim, id);
        Staff(sim);
        Assert.Equal("expedition", Advice.Next(sim)!.Id);
        Assert.Contains("Town Outskirts", Advice.Next(sim)!.Text);

        sim.State.NextExpeditionId = 2; // one went out
        var hurt = sim.State.Survivors.First(s => !s.IsChild);
        hurt.Body.Injure(BodyPart.LeftLeg, 0.45);
        Assert.Equal("medbay", Advice.Next(sim)!.Id);

        Build(sim, "med_bay");
        Assert.Equal("admit", Advice.Next(sim)!.Id);
        Assert.True(sim.Execute(new AdmitPatient(hurt.Id)).Ok);
        Assert.NotEqual("admit", Advice.Next(sim)?.Id);
    }

    [Fact]
    public void Full_beds_are_flagged()
    {
        var sim = Fresh();
        foreach (var id in new[] { "farm", "woodcutter", "quarry", "workshop", "med_bay" }) Build(sim, id);
        sim.State.NextExpeditionId = 2;
        while (sim.State.Survivors.Count < sim.BedCapacity) sim.AddSurvivor().HasBed = true;
        Staff(sim);
        Assert.Equal("beds", Advice.Next(sim)!.Id);
    }

    [Fact]
    public void Defenses_are_suggested_a_few_days_before_raids_can_start()
    {
        var sim = Fresh();
        sim.Raids = RaidTestData.Real;
        foreach (var id in new[] { "farm", "woodcutter", "quarry", "workshop", "med_bay", "campfire" }) Build(sim, id);
        sim.State.NextExpeditionId = 2;
        Staff(sim);
        Assert.NotEqual("defenses", Advice.Next(sim)?.Id);

        var grace = RaidTestData.Real.Def.Scheduler.GraceDays;
        sim.State.Tick = (grace - 6) * sim.Data.Rules.TicksPerDay;
        Assert.Equal("defenses", Advice.Next(sim)!.Id);
        sim.State.Stockpile.Add("wood", 10);
        sim.State.Stockpile.Add("scrap", 10);
        var at = RaidTestData.Edge(sim, Remnants.Core.Raids.Side.North);
        Assert.True(sim.Execute(new Remnants.Core.Raids.PlaceDefense("spike_strip", at.X, at.Y)).Ok);
        Assert.NotEqual("defenses", Advice.Next(sim)?.Id);
    }
}
