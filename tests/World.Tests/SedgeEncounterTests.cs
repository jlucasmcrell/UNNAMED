using System.Text.Json;
using UNNAMED.Content;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.World.Tests;

public sealed class SedgeEncounterTests
{
    [Fact]
    public void OuterRecordHallIsDryReachableAndItsDeepStairIsSealed()
    {
        var loader = new ContentLoader();
        Assert.True(loader.LoadAll(Path.Combine(TestWorlds.RepoRoot(), "content")));
        var layout = WorldContent.BuildLayout(loader, "region.sedge_levels");
        var grid = NavigationLayout.Build(layout, NavConfig.Default);
        var query = new NavQuery(grid, _ => true, NavConfig.Default, new NavScratch(), null);
        var path = NavSearch.Plan(query, new NavAgent(0, true),
            new NavPoint(5000000, 40000), new NavPoint(5016000, 40000));
        Assert.Equal(NavOutcome.Found, path.Outcome);
        Assert.Equal(2450, layout.Space.HeightAtMm(5016000,40000));
        Assert.True(layout.Space.HeightAtMm(5016000,40000)>layout.Tide!.LevelAt(14400));
        Assert.False(Kinematics.IsClear(5016000,45400,300,layout.Space,
            System.Collections.Immutable.ImmutableArray<Blocker>.Empty));
    }

    [Fact]
    public void MoveEncountersHaveClearReachableBanksOutsideTheServicePosts()
    {
        var loader = new ContentLoader();
        Assert.True(loader.LoadAll(Path.Combine(TestWorlds.RepoRoot(), "content")));
        var layout = WorldContent.BuildLayout(loader, "region.sedge_levels");
        var definitions = CombatContent.BuildCreatures(loader, WorldContent.TickMilliseconds(loader));
        var sites = CombatContent.BuildSpawns(loader, "region.sedge_levels");
        Assert.Equal(5, sites.Length);
        using var route = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestWorlds.RepoRoot(), "content/sedge_gate2_route.json")));
        var points = route.RootElement.GetProperty("points").EnumerateArray()
            .Select(p => new NavPoint((long)(p[0].GetDouble()*1000), (long)(p[1].GetDouble()*1000))).ToArray();
        var grid = NavigationLayout.Build(layout, NavConfig.Default);
        var query = new NavQuery(grid, _ => true, NavConfig.Default, new NavScratch(), null);
        foreach (var site in sites)
        {
            Assert.NotEmpty(site.Members);
            var members = site.Members.Select(member => definitions[member.CreatureId]).ToArray();
            Assert.All(members, definition =>
            {
                Assert.NotNull(definition.Charge);
                Assert.NotNull(definition.CloseAttack);
            });
            var radius = members.Max(definition => definition.RadiusMm);
            Assert.True(Kinematics.IsClear(site.XMm, site.ZMm, radius, layout.Space,
                System.Collections.Immutable.ImmutableArray<Blocker>.Empty), site.Key + " is obstructed");
            var nearest = points.MinBy(p => Math.Abs(p.XMm-site.XMm)+Math.Abs(p.ZMm-site.ZMm))!;
            var path = NavSearch.Plan(query, new NavAgent(0, true), nearest, new NavPoint(site.XMm, site.ZMm));
            Assert.True(path.Outcome == NavOutcome.Found, site.Key + ": " + path.Outcome);
            Assert.All(layout.Npcs, npc => Assert.True(Math.Abs(npc.XMm-site.XMm)+Math.Abs(npc.ZMm-site.ZMm)>60_000));
        }
    }
}
