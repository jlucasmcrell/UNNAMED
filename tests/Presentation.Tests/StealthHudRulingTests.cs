using UNNAMED.Domain.Combat;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The owner's HUD ruling (STEALTH §2.7a) as the models carry it: the character's own shadow, black in the dark and washed out in the light; a
/// glyph for what gave them away, one per cause and never two alike; the Other's detection marked so the violet edge can show it; a minimal
/// setting; and a carried sensor's line, empty when nothing is carried.
/// </summary>
public class StealthHudRulingTests
{
    [Fact]
    public void TheShadow_IsBlackInTheDark_AndWashedOutInTheLight()
    {
        Assert.Equal(1f, ShadowModel.From(LightBand.Dark).Ink);
        Assert.True(ShadowModel.From(LightBand.Bright).Ink < 0.25f);
        Assert.Equal(4, ShadowModel.Bands.Select(b => ShadowModel.From(b).Words).Distinct().Count());
    }

    [Fact]
    public void TheShadow_IsTintedPerPeople()
    {
        Assert.Equal(new[] { "stone", "chalk", "rust", "ash" },
            new[] { "people.kal", "people.siann", "people.veth", "people.ashborn" }.Select(ShadowModel.Tint));
    }

    [Fact]
    public void EveryCause_HasItsOwnGlyph()
    {
        var glyphs = Enum.GetValues<NoticeSense>().Select(AwarenessRead.Glyph).ToArray();
        Assert.Equal(4, glyphs.Length);
        Assert.Equal(glyphs.Length, glyphs.Distinct().Count());
    }

    [Fact]
    public void TheOther_IsCarriedOnTheRead_SoTheVioletEdgeCanShowIt()
    {
        Assert.True(new AwarenessRead(40, 100, NoticeSense.Nose, 0, false, Other: true).Other);
        Assert.False(AwarenessRead.None.Other);
    }

    [Fact]
    public void TheHud_HasAFullAndAMinimalSetting()
    {
        Assert.Equal(new[] { StealthHudMode.Full, StealthHudMode.Minimal }, Enum.GetValues<StealthHudMode>());
    }

    [Fact]
    public void ASensorReadsOnlyWhatItHas_AndNothingWhenNoneIsCarried()
    {
        Assert.Equal(DetectorReadModel.None, DetectorReadModel.From(Array.Empty<(string, long, long)>(), 0, 0));
        var read = DetectorReadModel.From(new[] { ("thermal", 0L, 10_000L), ("motion", 5_000L, 0L) }, 0, 0);
        Assert.Equal(2, read.Contacts);
        Assert.Equal("thermal", read.Sense);
        Assert.Equal(0f, read.BearingDegrees);
        Assert.Equal("2 bodies (thermal nearest)", read.Words);
    }
}
