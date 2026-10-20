using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>The words on a loot window row.</summary>
public class LootModelTests
{
    [Fact]
    public void ARow_ShowsTheCountOfAStack_AndTheWeightOfAllOfIt()
    {
        Assert.Equal("", LootModel.Count(1));
        Assert.Equal("x3", LootModel.Count(3));
        Assert.Equal("1.5 kg", LootModel.Weight(500, 3));
        Assert.Equal("0.2 kg", LootModel.Weight(200, 1));
    }
}
