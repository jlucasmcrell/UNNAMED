using System.Collections.Immutable;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

/// <summary>The creator's skin tones and hair colours (owner, 2026-10-25): what an index draws, per people, and that a save can keep it.</summary>
public sealed class LookSwatchesTests
{
    [Fact]
    public void NoChoice_AndTheFirstChoice_AreTheBodyAsBaked()
    {
        foreach (string people in new[] { "people.veth", "people.orenth", "people.kal" })
        {
            Assert.Null(LookSwatches.Chosen(LookSwatches.Skin(people), null));
            Assert.Null(LookSwatches.Chosen(LookSwatches.Skin(people), 0));
            Assert.Null(LookSwatches.Chosen(LookSwatches.Hair(people), null));
        }
        Assert.Equal("Deep", LookSwatches.Chosen(LookSwatches.Skin("people.veth"), 9)!.Label);
        Assert.Equal("Blond", LookSwatches.Chosen(LookSwatches.Hair("people.veth"), 7)!.Label);
    }

    [Fact]
    public void AnIndexTheListDoesNotHave_DrawsTheBodyAsBaked()
    {
        Assert.Null(LookSwatches.Chosen(LookSwatches.Skin("people.veth"), 99));
        Assert.Null(LookSwatches.Chosen(LookSwatches.Skin("people.veth"), -1));
    }

    [Fact]
    public void SiannHair_IsAlwaysPlatinumToWhite()
    {
        var hair = LookSwatches.Hair("people.siann");
        Assert.Equal("Platinum", LookSwatches.Chosen(hair, null)!.Label);
        Assert.All(hair, s => Assert.True(Math.Min(s.R, Math.Min(s.G, s.B)) >= .6f, s.Label));
    }

    [Fact]
    public void EachPeoplesTones_HaveDistinctNames()
    {
        foreach (string people in new[] { "people.veth", "people.kal", "people.siann" })
        {
            Assert.Equal(LookSwatches.Skin(people).Count, LookSwatches.Skin(people).Select(s => s.Label).Distinct().Count());
            Assert.Equal(LookSwatches.Hair(people).Count, LookSwatches.Hair(people).Select(s => s.Label).Distinct().Count());
        }
    }

    [Fact]
    public void TheChoices_AreAppearanceSliders_ASaveAlreadyKeeps()
    {
        var look = new CharacterAppearance(1750, null, null, null, null, null)
        {
            Sliders = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal).Add(LookSwatches.SkinSlider, 4).Add(LookSwatches.HairSlider, 7),
        };
        Assert.Null(look.Problem());
    }

    [Fact]
    public void AdasOwnHair_IsTheFirstStyle_AndOtherBodiesHaveNoStyleChoiceYet()
    {
        var ada = LookSwatches.HairStyles("humgen_player_ada_f");
        Assert.Equal(("Ponytail", (string?)null), ada[0]);
        Assert.Contains(ada, s => s.Asset == "humgen_hair_f_bun");
        Assert.Empty(LookSwatches.HairStyles("humgen_player_mateo_m"));
    }

    [Fact]
    public void Shirts_AndTrousers_AreDyedEarthTones_AndTheRestKeepsItsColour()
    {
        Assert.Equal("shirt", LookSwatches.GarmentKind("player_mateo_m_TSHIRT_Male.hg_tmp"));
        Assert.Equal("shirt", LookSwatches.GarmentKind("player_ada_f_Sweater_1_Female.hg_tmp"));
        Assert.Equal("trousers", LookSwatches.GarmentKind("player_mateo_m_Skinny_Jeans_Male.hg_tmp"));
        Assert.Equal("trousers", LookSwatches.GarmentKind("player_ada_f_Chinos_Female.hg_tmp"));
        Assert.Equal("boots", LookSwatches.GarmentKind("player_ada_f_Garden_Boots_Female.hg_tmp"));
        Assert.Null(LookSwatches.GarmentKind("player_ada_f_Eyes"));
        Assert.Null(LookSwatches.GarmentKind("ada_frontier_final_Pirate_Shirt.hg_tmp"));
        Assert.Null(LookSwatches.GarmentKind("mateo_frontier_final_Pirate_Pants.hg_tmp"));
        Assert.Equal("Undyed linen", LookSwatches.Cloth(LookSwatches.Shirts, null).Label);
        Assert.Equal("Oxblood", LookSwatches.Cloth(LookSwatches.Shirts, 1).Label);
        Assert.Equal("Brown", LookSwatches.Cloth(LookSwatches.Trousers, 9).Label);
        // No blue cloth: every dye's blue is no stronger than its red.
        Assert.All(LookSwatches.Shirts.Concat(LookSwatches.Trousers), s => Assert.True(s.B <= s.R + .05f, s.Label));
    }
}
