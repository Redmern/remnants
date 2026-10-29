using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class TrainingTests
{
    private static readonly TrainingRules Rules = new() { SkillPerTick = 0.1, AttributePerTick = 0.1, ChildMultiplier = 2 };

    [Fact]
    public void Working_raises_the_job_skill_with_diminishing_returns()
    {
        var job = TestData.Job();
        var novice = TestData.Survivor(skill: 0);
        var expert = TestData.Survivor(skill: 90);

        Training.TrainTick(job, novice, Rules);
        Training.TrainTick(job, expert, Rules);

        Assert.Equal(0.1, novice.Get(Skill.Woodcutting), 9);
        Assert.Equal(90.01, expert.Get(Skill.Woodcutting), 9);
    }

    [Fact]
    public void Only_weighted_attributes_train_in_proportion_to_weight()
    {
        var job = TestData.Job(new() { [AttributeKind.Strength] = 3, [AttributeKind.Endurance] = 1 });
        var s = TestData.Survivor(0);

        Training.TrainTick(job, s, Rules);

        Assert.Equal(0.075, s.Get(AttributeKind.Strength), 9);
        Assert.Equal(0.025, s.Get(AttributeKind.Endurance), 9);
        Assert.Equal(0, s.Get(AttributeKind.Intellect));
    }

    [Fact]
    public void Children_train_faster()
    {
        var job = TestData.Job();
        var adult = TestData.Survivor(0);
        var child = TestData.Survivor(0, stage: LifeStage.Child);

        Training.TrainTick(job, adult, Rules);
        Training.TrainTick(job, child, Rules);

        Assert.Equal(2 * adult.Get(Skill.Woodcutting), child.Get(Skill.Woodcutting), 9);
        Assert.Equal(2 * adult.Get(AttributeKind.Strength), child.Get(AttributeKind.Strength), 9);
    }

    [Fact]
    public void Stats_never_exceed_100()
    {
        var s = TestData.Survivor(100, skill: 100);
        for (var i = 0; i < 1000; i++) Training.TrainTick(TestData.Job(), s, Rules with { SkillPerTick = 50 });
        Assert.Equal(100, s.Get(Skill.Woodcutting));
        Assert.Equal(100, s.Get(AttributeKind.Strength));
    }

    [Fact]
    public void Workers_train_while_working_in_the_colony()
    {
        var sim = TestData.EmptyColony(stock: new() { ["food"] = 100 });
        var worker = sim.AddSurvivor();
        var woodcutter = sim.Build("woodcutter");
        sim.Execute(new AssignJob(worker.Id, woodcutter.Id));
        var before = worker.Get(Skill.Woodcutting);

        sim.Advance(10);

        Assert.True(worker.Get(Skill.Woodcutting) > before);
    }
}
