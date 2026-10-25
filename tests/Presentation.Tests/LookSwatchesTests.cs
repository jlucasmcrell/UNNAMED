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
}
