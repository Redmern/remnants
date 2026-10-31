using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Raids;
using Remnants.Core.Story;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.StoryTestData;

namespace Remnants.Core.Tests;

/// <summary>The briefing's warnings before a story mission, and the Act 1 defense task (BALANCE.md "Full-game pass 2").</summary>
public class MissionReadinessTests
{
    private static List<ExpeditionMember> Party(ColonySim sim, string weapon, int count = 3) =>
        sim.State.Survivors.Where(s => !s.IsChild).Take(count)
            .Select(s => new ExpeditionMember(s.Id, new() { [EquipSlot.Weapon] = weapon }, [])).ToList();

    [Fact]
    public void A_wounded_arm_warns_with_what_it_does_to_the_hit_chance()
    {
        var sim = NewStory();
        var noor = sim.State.Survivors.First(s => !s.IsChild);
        noor.Body.Injure(BodyPart.RightArm, 0.4);
        noor.Body.Injure(BodyPart.LeftArm, 0.5);

        var warning = Assert.Single(MissionReadiness.Check(sim, Real.Mission("m2")!), w => w.Id == $"hurt:{noor.Id}");
        Assert.Contains("right arm 40%", warning.Text);
        Assert.Contains("hits about 60% as often", warning.Text);
        // One arm: a one-handed weapon still swings with the good one.
        noor.Body.Heal(BodyPart.LeftArm);
        var one = Assert.Single(MissionReadiness.Check(sim, Real.Mission("m2")!), w => w.Id == $"hurt:{noor.Id}");
        Assert.Contains("hits about 80% as often with a two-handed weapon", one.Text);
        // Only the party picked counts once there is one.
        var others = sim.State.Survivors.Where(s => !s.IsChild && s.Id != noor.Id).Select(s => new ExpeditionMember(s.Id)).ToList();
        Assert.DoesNotContain(MissionReadiness.Check(sim, Real.Mission("m2")!, others), w => w.Id.StartsWith("hurt:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_threat_1_mission_wants_two_and_a_threat_2_mission_as_many_as_may_go()
    {
        var sim = NewStory();
        sim.State.Buildings.First(b => b.DefId == "town_hall").Level = 2;
        var m2 = Real.Mission("m2")!;
        var m7 = Real.Mission("m7")!;
        Assert.Equal(2, MissionReadiness.RecommendedParty(m2));
        Assert.Equal(4, MissionReadiness.RecommendedParty(m7));
        Assert.Equal(3, MissionReadiness.RecommendedParty(Real.Mission("m4")!));

        sim.AddSurvivor();
        Assert.DoesNotContain(MissionReadiness.Check(sim, m2, Party(sim, "pipe", 2)), w => w.Id == "party");
        Assert.Contains(MissionReadiness.Check(sim, m7, Party(sim, "machete", 3)), w => w.Id == "party");
        Assert.DoesNotContain(MissionReadiness.Check(sim, m7, Party(sim, "machete", 4)), w => w.Id == "party");
    }

    [Fact]
    public void Starting_weapons_into_a_stand_up_fight_warn_and_armory_blades_do_not()
    {
        var sim = NewStory();
        var m7 = Real.Mission("m7")!;
        Assert.Equal(2, m7.GearTier);
        Assert.Contains(MissionReadiness.Check(sim, m7, Party(sim, "pipe")), w => w.Id == "gear");
        Assert.DoesNotContain(MissionReadiness.Check(sim, m7, Party(sim, "machete")), w => w.Id == "gear");
        // A Mk II pipe counts a tier up.
        Assert.DoesNotContain(MissionReadiness.Check(sim, m7, Party(sim, "pipe+mk2")), w => w.Id == "gear");
        // Below it, every wave of two or more comes one lighter.
        var lighter = MissionReadiness.Lighter(m7.Field!);
        int Spawned(Remnants.Core.Scavenge.FieldMission f) => f.Triggers.SelectMany(t => t.Actions)
            .Where(a => a.Kind == Remnants.Core.Scavenge.TriggerActionKind.Spawn).Sum(a => a.Count);
        Assert.True(Spawned(lighter) < Spawned(m7.Field!));
        Assert.All(lighter.Triggers.SelectMany(t => t.Actions).Where(a => a.Kind == Remnants.Core.Scavenge.TriggerActionKind.Spawn), a => Assert.True(a.Count >= 1));
        // M4 is threat 2 too, but a sneak-and-carry: no gear warning (the Armory is not even open before Town Hall 2).
        Assert.Null(Real.Mission("m4")!.GearTier);
        Assert.DoesNotContain(MissionReadiness.Check(sim, Real.Mission("m4")!, Party(sim, "pipe")), w => w.Id == "gear");
    }

    [Fact]
    public void The_fence_line_task_is_done_by_a_tower_and_two_strips()
    {
        var sim = NewStory();
        sim.Raids = RaidTestData.Real;
        foreach (var r in new[] { "wood", "stone", "scrap" }) sim.State.Stockpile.Add(r, 200);
        var campaign = sim.State.Campaign!;
        campaign.Progress("m2").Status = MissionStatus.Done;
        Campaign.Tick(sim);
        Assert.Equal(MissionStatus.Active, campaign.Progress("t_fence").Status);

        void Place(string id, Side side, int inset, int offset)
        {
            var at = RaidTestData.Edge(sim, side, inset, offset);
            Assert.True(sim.Execute(new PlaceDefense(id, at.X, at.Y)).Ok);
            Campaign.Tick(sim);
        }
        Place("watchtower_1", Side.North, 3, -2);
        Place("spike_strip", Side.North, 1, 0);
        Assert.Equal(MissionStatus.Active, campaign.Progress("t_fence").Status);
        Place("spike_strip", Side.South, 1, 0);
        Assert.Equal(MissionStatus.Done, campaign.Progress("t_fence").Status);
        Assert.True(Schematics.Schematics.Has(sim, "sch_barricade"));
    }
}
