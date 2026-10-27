using System.Collections.Immutable;
using System.Text.Json;
using UNNAMED.Content;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.World.Tests;

public sealed class SedgeGate2Tests
{
    [Fact]
    public void RecoveredLoopWalksAndNavigatesAtBothTides()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(TestWorlds.RepoRoot(), "content"));
        var layout = WorldContent.BuildLayout(loader, "region.sedge_levels");
        var rules = WorldContent.BuildMovement(loader);
        var tide = Assert.IsType<TidalWater>(layout.Tide);
        Assert.Equal(-600, tide.LevelAt(0));
        Assert.Equal(750, tide.LevelAt(14400));
        Assert.Equal(tide.LevelAt(2000), tide.LevelAt(30800));
        Assert.True(tide.Open(tide.Passages[0], 0));
        Assert.False(tide.Open(tide.Passages[0], 14400));
        using var route = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestWorlds.RepoRoot(), "content/sedge_gate2_route.json")));
        var points = route.RootElement.GetProperty("points").EnumerateArray().Select(p => new NavPoint((long)(p[0].GetDouble()*1000),(long)(p[1].GetDouble()*1000))).ToArray();
        var grid = NavigationLayout.Build(layout, NavConfig.Default);
        foreach (long tick in new long[] {0,14400})
        {
            bool Gate(NavInput input) => input.Kind == NavInputKind.Tide && tide.Open(tide.Passages.Single(p=>p.Key==input.GateKey),tick);
            var dynamic = tide.Passages.Where(p=>!tide.Open(p,tick)).Select(p=>p.Footprint).ToImmutableArray();
            var body = new Body(points[0].XMm, layout.Space.HeightAtMm(points[0].XMm,points[0].ZMm), points[0].ZMm, 0);
            var query = new NavQuery(grid,Gate,NavConfig.Default,new NavScratch(),null);
            for(int i=1;i<points.Length;i++)
            {
                var target=points[i];
                for(int step=0;step<100;step++)
                {
                    double dx=target.XMm-body.XMm,dz=target.ZMm-body.ZMm,d=Math.Sqrt(dx*dx+dz*dz);
                    if(d<140)break;
                    body=Kinematics.Step(body,new MoveIntent((int)(dx/d*1000),(int)(dz/d*1000),Gait.Walk,0),rules,layout.Space,dynamic,50);
                }
                Assert.True(Math.Abs(body.XMm-target.XMm)<160 && Math.Abs(body.ZMm-target.ZMm)<160,$"Tide {tick}, point {i}: body {body}; target {target}");
                if(i%10==0)
                {
                    var plan=NavSearch.Plan(query,new NavAgent(0,true),points[i-10],target);
                    Assert.Equal(NavOutcome.Found,plan.Outcome);
                }
            }
            var gateInput = new NavInput(NavInputKind.Tide,tide.Passages[0].Footprint,tide.Passages[0].Key);
            Assert.Equal(tick==0, NavGrid.Passable(gateInput,new NavAgent(0,true),Gate));
            Assert.Equal(tick==0,Kinematics.IsClear(5000000,1000000,350,layout.Space,dynamic));
        }
        var crossing = new Body(5000000, layout.Space.HeightAtMm(5000000,1055000), 1055000, 0);
        for(int step=0;step<1700;step++)
            crossing=Kinematics.Step(crossing,new MoveIntent(0,-1000,Gait.Walk,0),rules,layout.Space,ImmutableArray<Blocker>.Empty,50);
        Assert.True(crossing.ZMm < 950000, "The actual dry low-tide span must be walkable, including its ramps");
        Assert.False(Kinematics.IsClear(5000000-50000,1000000,350,layout.Space,ImmutableArray<Blocker>.Empty));
    }
}
