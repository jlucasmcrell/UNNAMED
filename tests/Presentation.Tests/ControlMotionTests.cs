using UNNAMED.Domain.Combat;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

public class ControlMotionTests
{
    [Fact]
    public void FootingPlaysFallIdleAndRise_AndIsStandingAtTheDomainDeadline()
    {
        Assert.Equal(("prone_fall", 0f), ControlMotion.Prone(ControlRules.ProneRiseTicks, 0));
        Assert.Equal("prone_idle", ControlMotion.Prone(15, 0).Clip);
        Assert.Equal("prone_rise", ControlMotion.Prone(10, 0).Clip);
        Assert.Equal(("prone_rise", 1f), ControlMotion.Prone(0, 0));
        Assert.Equal(("prone_rise", 1f), ControlMotion.Prone(-1, 1));
        Assert.Equal("shove", ControlMotion.Action(ControlMove.Shove));
        Assert.Equal("trip", ControlMotion.Action(ControlMove.Trip));
        Assert.Equal("disarm", ControlMotion.Action(ControlMove.Disarm));
    }
}
