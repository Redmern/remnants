using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class JobOutputTests
{
    private static readonly OutputRules Rules = new() { PerformanceFloor = 0.25, ChildOutputMultiplier = 0.5, LowHealthMultiplier = 0.5 };

    [Fact]
    public void Effectiveness_is_the_weighted_average_of_attributes_and_skill()
    {
        var job = TestData.Job(new() { [AttributeKind.Strength] = 3, [AttributeKind.Endurance] = 1 }, skillWeight: 1);
        var s = TestData.Survivor(0);
        s.Set(AttributeKind.Strength, 80);
        s.Set(AttributeKind.Endurance, 40);
        s.Set(Skill.Woodcutting, 20);

        // (3·80 + 1·40 + 1·20) / 5 / 100 = 0.6
        Assert.Equal(0.6, JobOutput.Effectiveness(job, s), 9);
    }

    [Fact]
    public void Only_weighted_attributes_matter()
    {
        var job = TestData.Job(new() { [AttributeKind.Strength] = 1 });
        var strong = TestData.Survivor(0);
        strong.Set(AttributeKind.Strength, 90);
        var clever = TestData.Survivor(0);
        clever.Set(AttributeKind.Intellect, 90);

        Assert.True(JobOutput.Effectiveness(job, strong) > JobOutput.Effectiveness(job, clever));
        Assert.Equal(0, JobOutput.Effectiveness(job, clever), 9);
    }

    [Theory]
    [InlineData(0, 0.25)]
    [InlineData(50, 0.625)]
    [InlineData(100, 1.0)]
    public void Performance_scales_from_floor_to_one(double stats, double expected)
    {
        var s = TestData.Survivor(stats, skill: stats);
        Assert.Equal(expected, JobOutput.Performance(TestData.Job(), s, Rules), 9);
    }

    [Fact]
    public void Tool_quality_adds_the_job_tool_bonus()
    {
        var s = TestData.Survivor();
        Assert.Equal(1.0, JobOutput.ToolFactor(TestData.Job(toolBonus: 0.5), s), 9);
        s.ToolQuality = 1;
        Assert.Equal(1.5, JobOutput.ToolFactor(TestData.Job(toolBonus: 0.5), s), 9);
    }

    [Fact]
    public void Low_health_reduces_output()
    {
        var s = TestData.Survivor();
        Assert.Equal(1.0, JobOutput.HealthFactor(TestData.Job(), s, Rules), 9);
        s.Health = 0;
        Assert.Equal(0.5, JobOutput.HealthFactor(TestData.Job(), s, Rules), 9);
    }

    [Fact]
    public void Missing_arm_halves_an_arms_job_and_a_good_prosthetic_beats_the_original()
    {
        var job = TestData.Job(body: new() { [BodyRegion.Arms] = 1 });
        var s = TestData.Survivor();
        s.Body.Lose(BodyPart.LeftArm);
        Assert.Equal(0.5, JobOutput.HealthFactor(job, s, Rules), 9);

        s.Body.FitProsthetic(BodyPart.LeftArm, 1.4);
        Assert.Equal(1.2, JobOutput.HealthFactor(job, s, Rules), 9);
    }

    [Fact]
    public void Injury_only_affects_jobs_that_use_that_region()
    {
        var s = TestData.Survivor();
        s.Body.Injure(BodyPart.LeftLeg, 1.0);
        s.Body.Injure(BodyPart.RightLeg, 1.0);
        Assert.Equal(1.0, JobOutput.HealthFactor(TestData.Job(body: new() { [BodyRegion.Arms] = 1 }), s, Rules), 9);
        Assert.Equal(0.0, JobOutput.HealthFactor(TestData.Job(body: new() { [BodyRegion.Legs] = 1 }), s, Rules), 9);
    }

    [Fact]
    public void Children_produce_at_the_child_multiplier()
    {
        Assert.Equal(1.0, JobOutput.LifeFactor(TestData.Survivor(), Rules));
        Assert.Equal(0.5, JobOutput.LifeFactor(TestData.Survivor(stage: LifeStage.Child), Rules));
    }

    [Fact]
    public void Per_tick_output_multiplies_base_level_and_all_factors()
    {
        var building = new BuildingDef
        {
            Id = "b",
            BaseOutput = 2.0,
            Levels = [new() { OutputMultiplier = 1.0 }, new() { OutputMultiplier = 1.5 }],
        };
        var job = TestData.Job(toolBonus: 0.5);
        var s = TestData.Survivor(100, skill: 100); // performance 1
        s.ToolQuality = 1;                           // tool 1.5

        Assert.Equal(2.0 * 1.0 * 1.5, JobOutput.PerTick(building, 1, job, s, Rules), 9);
        Assert.Equal(2.0 * 1.5 * 1.5, JobOutput.PerTick(building, 2, job, s, Rules), 9);
    }
}
