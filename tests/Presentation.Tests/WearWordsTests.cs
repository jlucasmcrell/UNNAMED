using UNNAMED.Domain.Items;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>Durability and the laden band in words (WP3b; D4, D5): what the item rows, the inventory and the character sheet say.</summary>
public class WearWordsTests
{
    [Theory]
    [InlineData(0, WearBand.Sound)]
    [InlineData(59, WearBand.Sound)]     // 61 of 120 left: more than half
    [InlineData(60, WearBand.Worn)]      // half left
    [InlineData(90, WearBand.BadlyWorn)] // a quarter left
    [InlineData(119, WearBand.BadlyWorn)]
    [InlineData(120, WearBand.WornOut)]
    public void TheBand_FollowsWhatIsLeft(int points, WearBand band) =>
        Assert.Equal(band, WearWords.Band(new ItemWear(points, 0), 120));

    [Fact]
    public void AnItemWithNoDurability_HasNoBand_ChipOrLine()
    {
        Assert.Null(WearWords.Band(null, 0));
        Assert.Null(WearWords.Chip(new ItemWear(5, 0), 0));
        Assert.Null(WearWords.Line(null, 0));
    }

    [Fact]
    public void ASoundPiece_KeepsItsRowQuiet_AndAWornOutOneSaysSo()
    {
        Assert.Null(WearWords.Chip(null, 120));   // never worn: no wear recorded
        Assert.Equal(("Worn", WordTone.Quiet), WearWords.Chip(new ItemWear(60, 0), 120));
        Assert.Equal(("Badly worn", WordTone.Warning), WearWords.Chip(new ItemWear(100, 0), 120));
        Assert.Equal(("Worn out", WordTone.Refusal), WearWords.Chip(new ItemWear(120, 0), 120));
        Assert.Equal("Durability 80 of 120", WearWords.Line(new ItemWear(40, 0), 120));
    }

    [Theory]
    [InlineData(10_000, LoadBand.Light)]
    [InlineData(40_000, LoadBand.Light)]    // exactly 80%: the band starts above it
    [InlineData(40_001, LoadBand.Laden)]
    [InlineData(49_999, LoadBand.Laden)]
    [InlineData(50_000, LoadBand.Full)]
    public void TheLoadBand_IsLadenAboveEightyPercent_AndFullAtTheLimit(long carried, LoadBand band) =>
        Assert.Equal(band, LoadWords.Band(carried, 50_000, LadenRules.Default));

    [Fact]
    public void TheLoadWords_SayWhatTheBandCosts()
    {
        var rules = LadenRules.Default;
        Assert.Null(LoadWords.Chip(1_000, 50_000, rules));
        Assert.Equal(("Laden", WordTone.Warning), LoadWords.Chip(45_000, 50_000, rules));
        Assert.Equal(("At the limit", WordTone.Refusal), LoadWords.Chip(50_000, 50_000, rules));
        Assert.Equal("10 kg more before the pack weighs on you.", LoadWords.Sentence(30_000, 50_000, rules));
        Assert.Equal("Laden: a sprint costs 50% more stamina and a dodge 25% more.", LoadWords.Sentence(45_000, 50_000, rules));
        Assert.StartsWith("At your limit", LoadWords.Sentence(50_000, 50_000, rules));
    }
}
