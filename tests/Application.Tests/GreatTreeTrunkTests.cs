using System.Collections.Immutable;
using System.Text.Json;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Application.Tests;

public class GreatTreeTrunkTests
{
    [Fact]
    public void TrunkBlocksFromThreeSides_AndTheVisibleTreeKeepsItsWorldPosition()
    {
        using var profile=new TempProfile();
        var session=Harness.Boot(profile);
        var original=session.Setup.Layout.Space;
        // The kapok that stood for Old Late until it became the stone giant (GS-3), moved into the hollow as its great tree (GS-10).
        var trunk=Assert.IsType<CircleBlocker>(Assert.Single(original.Blockers,b=>b.Id=="great_tree"));
        Assert.Equal((578_000L,748_000L,4_500L),(trunk.CenterXMm,trunk.CenterZMm,trunk.RadiusMm));
        var space=original with { Blockers=ImmutableArray.Create<Blocker>(trunk) };
        var rules=session.Setup.Movement;
        foreach(var (x,z) in new[]{(-1,0),(0,-1),(1,0)})
        {
            var body=new Body(trunk.CenterXMm+x*8_000,0,trunk.CenterZMm+z*8_000,0);
            var intent=new MoveIntent(-x*1000,-z*1000,Gait.Run,0);
            for(int tick=0;tick<100;tick++)body=Kinematics.Step(body,intent,rules,space,ImmutableArray<Blocker>.Empty,50);
            var dx=body.XMm-trunk.CenterXMm;var dz=body.ZMm-trunk.CenterZMm;
            var distance=Math.Sqrt((double)dx*dx+(double)dz*dz);
            Assert.InRange(distance,trunk.RadiusMm+rules.BodyRadiusMm-2,trunk.RadiusMm+rules.BodyRadiusMm+2);
        }
        using var bindings=JsonDocument.Parse(File.ReadAllText(Path.Combine(Harness.RepoRoot(),"content","_art_bindings.json")));
        var offset=bindings.RootElement.GetProperty("structures").GetProperty("great_tree").GetProperty("offset").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        Assert.Equal(580.5, trunk.CenterXMm/1000.0+offset[0],6);
        Assert.Equal(746.6, trunk.CenterZMm/1000.0+offset[2],6);
        Assert.Equal(original.Terrain.HeightAtMm(580_500,746_600)/1000.0,
            original.Terrain.HeightAtMm(trunk.CenterXMm,trunk.CenterZMm)/1000.0+offset[1],6);
    }
}
