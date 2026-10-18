using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

public class WornOutfitTests
{
    [Fact]
    public void ChangingFeetAndHeadSelectsThoseItems_AndUnequippingRemovesTheirGarments()
    {
        var equipment = new Dictionary<string, string[]>
        {
            ["vest"] = new[] { "hide_backing" },
            ["jack"] = new[] { "hide_backing", "iron_lames" },
            ["boots"] = new[] { "sealed_boots" },
            ["sabatons"] = new[] { "sealed_boots", "foot_lames" },
            ["coif"] = new[] { "cap", "head_lames" },
        };
        var permanent = new[] { "shirt" };
        var plain = new WornOutfit(Chest: "vest", Feet: "boots");
        var armoured = plain with { Chest = "jack", Head = "coif", Feet = "sabatons" };
        Assert.Equal(new[] { "shirt", "hide_backing", "iron_lames", "sealed_boots", "foot_lames", "cap", "head_lames" },
            armoured.Garments(permanent, equipment));
        Assert.Equal(new[] { "shirt", "hide_backing", "sealed_boots" }, plain.Garments(permanent, equipment));
        Assert.Equal(permanent, new WornOutfit().Garments(permanent, equipment));
    }

    [Fact]
    public void SharedBackingIsLoadedOnce_AndLegacyVariantsKeepTheirExistingNames()
    {
        var equipment = new Dictionary<string, string[]> { ["jack"] = new[] { "shirt", "lames" } };
        Assert.Equal(new[] { "shirt", "lames" }, new WornOutfit(Chest: "jack").Garments(new[] { "shirt" }, equipment));
        Assert.Equal("item.armor.hide_vest_cap", new WornOutfit(Chest: "item.armor.hide_vest", Head: "cap").LegacyVariant);
        Assert.Null(new WornOutfit(Feet: "boots").LegacyVariant);
    }
}
