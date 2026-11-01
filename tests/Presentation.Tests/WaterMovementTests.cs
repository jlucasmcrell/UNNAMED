using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

public class WaterMovementTests
{
    [Fact]
    public void BankToWadeToSwimToTreadAndBackUsesOnlyTheFiveSoundStates()
    {
        var water = new WaterMovement();
        water.Observe(false, false, 1);
        Assert.Null(water.State);
        Assert.Null(water.Transition);
        water.Observe(true, false, 1);
        Assert.Equal("wade", water.State);
        Assert.Equal("enter_water", water.Transition);
        water.Observe(true, true, 1);
        Assert.Equal("swim", water.State);
        Assert.Null(water.Transition);
        water.Observe(true, true, 0);
        Assert.Equal("tread", water.State);
        Assert.Null(water.Transition);
        water.Observe(true, false, 1);
        Assert.Equal("wade", water.State);
        Assert.Null(water.Transition);
        water.Observe(false, false, 1);
        Assert.Null(water.State);
        Assert.Equal("exit_water", water.Transition);
        water.Observe(false, false, 0);
        Assert.Null(water.Transition);
    }
}
