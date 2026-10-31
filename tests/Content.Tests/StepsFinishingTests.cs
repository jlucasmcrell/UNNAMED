using UNNAMED.Domain.Spatial;

namespace UNNAMED.Content.Tests;

public class StepsFinishingTests
{
    [Fact]
    public void TerraceFillsMeetTheirWallsAndEveryStairKeepsItsCentreClear()
    {
        var loader = new ContentLoader(); loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        var layout = WorldContent.BuildLayout(loader, "region.otherhome");
        var walls = layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith("oh_riser_")).ToArray();
        var cheeks = layout.Space.Blockers.OfType<BoxBlocker>().Where(b => b.Id.StartsWith("oh_stair_cheek_")).ToArray();
        var origins = walls.Select(b => (b.MinXMm, Z: b.MaxZMm - 100)).ToHashSet();
        var fills = layout.Space.WalkableSurfaces.Where(s => origins.Contains((s.OriginXMm, s.OriginZMm))).ToArray();
        Assert.Equal(36, fills.Length); Assert.Equal(192, cheeks.Length);
        foreach (var fill in fills)
        {
            var backing = walls.Where(w => w.MaxZMm - 100 == fill.OriginZMm && w.MinXMm >= fill.OriginXMm && w.MaxXMm <= fill.MaxXMm)
                .OrderBy(w => w.MinXMm).ToArray();
            Assert.NotEmpty(backing);
            Assert.Equal(fill.OriginXMm, backing[0].MinXMm); Assert.Equal(fill.MaxXMm, backing[^1].MaxXMm);
            for (int i = 1; i < backing.Length; i++) Assert.Equal(backing[i-1].MaxXMm, backing[i].MinXMm);
            Assert.All(fill.HeightsMm, height => Assert.Equal(fill.HeightsMm[0], height));
            long x = (fill.OriginXMm + fill.MaxXMm) / 2;
            Assert.Equal(fill.HeightsMm[0], layout.Space.HeightAtMm(x, fill.OriginZMm));
            Assert.InRange(Math.Abs(fill.HeightsMm[0] - layout.Space.Terrain.HeightAtMm(x, fill.MaxZMm)), 0L, 200L);
        }
        var stairs = layout.Space.WalkableSurfaces.Where(s => s.Columns == 7 && s.Rows == 17 && s.HeightsMm[0] != s.HeightsMm[^1]).ToArray();
        Assert.Equal(24, stairs.Length);
        foreach (var stair in stairs)
        {
            long x = (stair.OriginXMm + stair.MaxXMm) / 2;
            for (long z = stair.OriginZMm; z <= stair.MaxZMm; z += 1_000)
                Assert.All(cheeks, c => Assert.True(c.DistanceTo(x,z) >= 350, $"{c.Id} blocks a stair centre at {x},{z}"));
            Assert.DoesNotContain(fills, f => f.OriginXMm < stair.MaxXMm && f.MaxXMm > stair.OriginXMm
                && f.OriginZMm < stair.MaxZMm && f.MaxZMm > stair.OriginZMm);
        }
        Assert.Contains(layout.Barriers, b => b.Key == "barrier.oh_drowned_gate" && b.Footprint is CircleBlocker { RadiusMm: 22_000, HeightMm: 14_000 });
    }
}
