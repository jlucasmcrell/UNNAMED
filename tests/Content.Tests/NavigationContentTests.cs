using UNNAMED.Domain.Spatial;

namespace UNNAMED.Content.Tests;

/// <summary>Navigation's tuning from content, and the NAV lints (M7 design §3.16, §3.20.4).</summary>
public class NavigationContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    private static string GameContent => Path.Combine(RepoPaths.Root(), "content");

    private static string FixturePack => Path.Combine(RepoPaths.Root(), "tests", "Persistence.Tests", "Fixtures", "content");

    /// <summary>A copy of a content directory with one file edited (or removed), in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string source, string relativePath, string? find, string? replace)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string path = Path.Combine(Root, relativePath);
            if (find is null)
            {
                File.Delete(path);
                return;
            }
            string text = File.ReadAllText(path);
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-nav-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Theory]
    [InlineData("NAV001", "config/navigation.yaml", "node_m: 0.25", "node_m: 0.3")]
    [InlineData("NAV002", "config/navigation.yaml", "{ id: person, radius_m: 0.35 }", "{ id: human, radius_m: 0.35 }")]
    [InlineData("NAV002", "config/navigation.yaml", "{ id: person, radius_m: 0.35 }", "{ id: person, radius_m: 0.6 }")]
    [InlineData("NAV003", "config/navigation.yaml", "margin_m: 0.05", "margin_m: 0.01")]
    [InlineData("NAV004", "config/navigation.yaml", "{ id: person, radius_m: 0.35 }", "{ id: person, radius_m: 0.3 }")]
    [InlineData("NAV005", "config/navigation.yaml", "agent_height_m: 1.8", "agent_height_m: 1.7")]
    [InlineData("NAV006", "regions/ashen_hollow.yaml", "position_m: [112, 189]", "position_m: [101, 189]")]
    [InlineData("NAV007", "config/navigation.yaml", "max_expansions: 65536", "max_expansions: 70000")]
    [InlineData("NAV007", "config/navigation.yaml", "probe_nodes: 2048", "probe_nodes: 20000")]
    [InlineData("NAV007", "config/navigation.yaml", "window_margin_m: 20", "window_margin_m: 64")]
    [InlineData("NAV007", "config/navigation.yaml", "retry_s: 2", "retry_s: 0")]
    [InlineData("NAV007", "config/navigation.yaml", "max_corners: 32", "max_corners: many")]
    public void NavigationLints_RefuseBadConfigs_AndPassTheGame(string code, string file, string find, string replace)
    {
        using var content = new EditedContent(GameContent, file, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == code);

        // The game's content and the fixture pack pass every NAV lint: Tavar's place at the fold's centre, and the two location
        // anchors that lie inside rocks, among them.
        Assert.DoesNotContain(Load(GameContent).Errors, e => e.Code.StartsWith("NAV", StringComparison.Ordinal));
        Assert.DoesNotContain(Load(FixturePack).Errors, e => e.Code.StartsWith("NAV", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHollow_ReachesEveryAuthoredPlace_FromTheSpawn()
    {
        var loader = Load(GameContent);
        var layout = WorldContent.BuildLayout(loader, "region.ashen_hollow");
        var config = NavigationContent.Build(loader);
        Assert.Empty(NavigationContent.Unreachable(layout, config));

        // Tavar stands in the fold, which only a lifted barrier lets a body into; NAV006 floods with every gate passable.
        var tavar = layout.Npcs.Single(n => n.NpcId == "npc.ashen_hollow.tavar_orr");
        Assert.Contains(layout.Barriers, b => NavGeometry.Within(b.Footprint, tavar.XMm, tavar.ZMm, 0));
        // The Foldscar's and the Ruined Cart's anchors lie inside rocks, and pass as reach points.
        foreach (string id in new[] { "location.foldscar", "location.ruined_cart" })
        {
            var anchor = layout.Locations.Single(l => l.Id == id);
            Assert.Contains(layout.Space.Blockers, b => NavGeometry.Within(b, anchor.XMm, anchor.ZMm, 0));
        }
    }

    [Fact]
    public void NavConfigDefault_IsTheShippedFile()
    {
        var shipped = NavigationContent.Build(Load(GameContent));
        AssertSame(NavConfig.Default, shipped);

        // Without config.navigation, a pack navigates by the default.
        using var without = new EditedContent(GameContent, "config/navigation.yaml", null, null);
        var loader = Load(without.Root);
        Assert.Empty(loader.Errors);
        AssertSame(NavConfig.Default, NavigationContent.Build(loader));

        static void AssertSame(NavConfig expected, NavConfig actual)
        {
            Assert.Equal((expected.NodeMm, expected.MarginMm, expected.AgentHeightMm), (actual.NodeMm, actual.MarginMm, actual.AgentHeightMm));
            Assert.Equal(expected.Classes.ToArray(), actual.Classes.ToArray());
            Assert.Equal(expected.Limits, actual.Limits);
            Assert.Equal(expected.Digest(), actual.Digest());
        }
    }
}
