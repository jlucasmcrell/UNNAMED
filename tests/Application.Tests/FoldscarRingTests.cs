using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Foldscar's debris keeps to its ring (grand scale): every band FoldscarDressing places from lies within FoldscarRing.Radius of the
/// heart, and nothing of the hollow but the Foldscar's own stones stands inside that ring - so no piece can hang over the waystation, lie on
/// the road or come through the inn's floor.
/// </summary>
public class FoldscarRingTests
{
    [Fact]
    public void EveryDebrisBand_LiesInsideTheRing()
    {
        var bands = FoldscarRing.Wreck.Concat(FoldscarRing.Hanging).Append(FoldscarRing.Rubble).Append(FoldscarRing.Pebbles);
        foreach (var band in bands)
        {
            Assert.InRange(band.From, 0f, band.To);
            Assert.InRange(band.To, band.From, FoldscarRing.Radius);
            Assert.InRange(band.Low, 0f, band.High);
        }
    }

    [Fact]
    public void NothingButTheFoldscarsOwnStones_StandsInsideTheRing()
    {
        using var profile = new TempProfile();
        var space = Harness.Boot(profile).Setup.Layout.Space;
        var heart = Assert.IsType<CircleBlocker>(Assert.Single(space.Blockers, b => b.Id == "rock_foldscar_heart"));
        var inside = space.Blockers.Where(b => b.DistanceTo(heart.CenterXMm, heart.CenterZMm) < FoldscarRing.Radius * 1000).Select(b => b.Id).Order();
        Assert.Equal(new[] { "rock_foldscar_heart", "rock_stone_north", "rock_stone_southeast", "rock_stone_southwest" }, inside);
        foreach (var building in space.Blockers.Where(b => b.Id.StartsWith("longhouse_") || b.Id.StartsWith("forge_") || b.Id == "rock_waystone"))
            Assert.True(building.DistanceTo(heart.CenterXMm, heart.CenterZMm) > 100_000, building.Id);
    }
}
