// UNNAMED Content tests - the fishing rules the pack declares (D20 follow-on): rod, baits, day bands and per-water catch tables.

using System.IO;
using UNNAMED.Content;
using Xunit;

namespace UNNAMED.Content.Tests;

public sealed class FishingContentTests
{
    private static ContentLoader LoadPack()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        return loader;
    }

    [Fact]
    public void ThePackDeclaresARodAndTwoBaits()
    {
        var rules = WorldContent.BuildFishing(LoadPack());
        Assert.Equal("item.tool.fishing_rod", rules.RodItemId);
        Assert.Equal(2, rules.Baits.Count);
        Assert.True(rules.Baits["item.material.bait_fly"].BiteScale < rules.Baits["item.material.bait_worm"].BiteScale);
    }

    [Fact]
    public void TheDaysBands_ReadTheHourAndDoNotOverlap()
    {
        var rules = WorldContent.BuildFishing(LoadPack());
        Assert.Equal(4, rules.Bands.Length);
        Assert.Equal("dawn", rules.BandAt(6)!.Id);
        Assert.Equal("day", rules.BandAt(12)!.Id);
        Assert.Equal("dusk", rules.BandAt(19)!.Id);
        Assert.Equal("night", rules.BandAt(23)!.Id);
        Assert.Equal("night", rules.BandAt(2)!.Id);
    }

    [Fact]
    public void ACreekAtNight_RollsADifferentTableFromACreekAtDawn()
    {
        var rules = WorldContent.BuildFishing(LoadPack());
        Assert.Equal("loot.fishing.ah_creek", rules.TableFor("creek", 6));
        Assert.Equal("loot.fishing.cw_millpond", rules.TableFor("creek", 23));
        Assert.Null(rules.TableFor("lake", 12));
    }
}
