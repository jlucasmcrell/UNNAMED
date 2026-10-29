// UNNAMED Content - D23 S7 wired to D21: the silencer is a marksman part, and its makers ship one

using UNNAMED.Content;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Items;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// S7's last piece: the stealth kit is not a set of free-floating items beside the gear system, it is D21 parts. Canon puts the silencer on the
/// marksman line (GEAR_GENERATION 4.2) and the dampener, cloak and sensors on the body (STEALTH.md 10), and this checks the gear side of that split.
/// </summary>
public class StealthGearWiringTests
{
    private static ContentLoader Content()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return loader;
    }

    [Fact]
    public void TheMarksmanFamily_HasAMuzzleSlot_AndBothCanonMuzzlesInIt()
    {
        var loader = Content();
        var parts = GearContent.BuildParts(loader);

        Assert.True(GearContent.FamilySlots["marksman"].Contains("muzzle"), "the marksman family has no muzzle slot");
        var muzzle = parts.Values.Where(p => p.Family == "marksman" && p.Slot == "muzzle").ToArray();
        Assert.Equal(2, muzzle.Length);
        Assert.Contains(muzzle, p => p.Id == "part.marksman.muzzle.silencer");
        Assert.Contains(muzzle, p => p.Id == "part.marksman.muzzle.cold_core");
        // Both cost something: a muzzle is fitted, not free. The silencer pays in weight and reach, the Cold Core in weight and speed.
        Assert.All(muzzle, p => Assert.True(p.Stats.WeightAddGrams > 0, $"{p.Id} adds no weight, so it costs nothing to fit"));
    }

    [Fact]
    public void EveryMakerThatBuildsAMarksmanDevice_OffersAMuzzle()
    {
        var loader = Content();
        var makers = GearContent.BuildMakers(loader).Values.Where(m => m.PartSets.ContainsKey("marksman")).ToArray();

        Assert.NotEmpty(makers);
        foreach (var maker in makers)
            Assert.True(maker.PartSets["marksman"].TryGetValue("muzzle", out var muzzle) && muzzle.Length > 0,
                $"{maker.Id} builds marksman devices with no muzzle slot on offer");
    }

    [Fact]
    public void TheStealthKitStillNamesItsD21Family()
    {
        // The mapping S7 exposes to the gear system: the silencer is a marksman fitting, the cloak and boots are armour.
        Assert.Equal("marksman", StealthGear.FamilyOf("item.tool.silencer"));
        Assert.Equal("armor", StealthGear.FamilyOf("item.armor.cloak_optical"));
        Assert.Equal("armor", StealthGear.FamilyOf("item.armor.boots_muffled"));
    }
}
