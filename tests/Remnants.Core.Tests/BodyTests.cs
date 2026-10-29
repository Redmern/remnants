using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

public class BodyTests
{
    [Fact]
    public void Healthy_body_is_fully_efficient()
    {
        var body = new Body();
        Assert.All(Enum.GetValues<BodyRegion>(), r => Assert.Equal(1.0, body.RegionEfficiency(r)));
    }

    [Fact]
    public void Injury_severity_reduces_efficiency_and_healing_restores_it()
    {
        var body = new Body();
        body.Injure(BodyPart.Head, 0.4);
        Assert.Equal(PartCondition.Injured, body[BodyPart.Head].Condition);
        Assert.Equal(0.6, body[BodyPart.Head].Efficiency, 9);

        body.Heal(BodyPart.Head);
        Assert.Equal(1.0, body[BodyPart.Head].Efficiency);
    }

    [Fact]
    public void Lost_part_cannot_be_injured_or_healed_back()
    {
        var body = new Body();
        body.Lose(BodyPart.RightLeg);
        body.Injure(BodyPart.RightLeg, 0.2);
        body.Heal(BodyPart.RightLeg);
        Assert.Equal(PartCondition.Missing, body[BodyPart.RightLeg].Condition);
        Assert.Equal(0.5, body.RegionEfficiency(BodyRegion.Legs), 9);
    }

    [Fact]
    public void Prosthetic_only_fits_a_missing_part()
    {
        var body = new Body();
        Assert.Throws<InvalidOperationException>(() => body.FitProsthetic(BodyPart.LeftArm, 1.0));
        body.Lose(BodyPart.LeftArm);
        body.FitProsthetic(BodyPart.LeftArm, 0.8);
        Assert.Equal(PartCondition.Prosthetic, body[BodyPart.LeftArm].Condition);
        Assert.Equal(0.8, body[BodyPart.LeftArm].Efficiency, 9);
    }
}
