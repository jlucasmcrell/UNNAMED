using System.Collections.Immutable;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Tests.Spatial;

public class WalkableSurfacesTests
{
    private static readonly MovementRules Rules = new(3200,50,160,350,1600);
    private static WalkSpace Space() => new(0,0,10000,10000,
        new TerrainGrid(0,0,10000,2,2,new long[] {0,0,0,0}), ImmutableArray<Blocker>.Empty)
    {
        WalkableSurfaces = ImmutableArray.Create(new TerrainGrid(2000,2000,1000,3,3,
            Enumerable.Repeat(1000L,9)))
    };

    [Fact]
    public void PropSupport_IsBoundedAndLeavesBaseTerrainUnchanged()
    {
        var space=Space();
        Assert.Equal(1000,space.HeightAtMm(3000,3000));
        Assert.Equal(0,space.HeightAtMm(1999,3000));
        Assert.Equal(0,space.HeightAtMm(4001,3000));
        Assert.Equal(0,space.HeightAtMm(3000,1999));
        Assert.Equal(0,space.HeightAtMm(3000,4001));
        Assert.Equal(0,space.Terrain.HeightAtMm(3000,3000));
    }

    [Fact]
    public void StandingAndJumping_UseTheSamePropTop()
    {
        var body=new Body(3000,1000,3000,0);
        Assert.Equal(1000,Kinematics.Step(body,MoveIntent.Idle(0),Rules,Space(),Array.Empty<Blocker>(),50).YMm);
        var jumping=Kinematics.Step(body,new Posture(Stance.Standing,true,0),MoveIntent.Idle(0),Rules,Space(),Array.Empty<Blocker>(),420);
        Assert.Equal(2150,jumping.Body.YMm);
        Assert.Equal(0,Kinematics.Step(body with { XMm=5000 },MoveIntent.Idle(0),Rules,Space(),Array.Empty<Blocker>(),50).YMm);
    }
}
