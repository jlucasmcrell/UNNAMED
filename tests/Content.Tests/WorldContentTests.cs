using UNNAMED.Domain.Spatial;

namespace UNNAMED.Content.Tests;

/// <summary>The region layout from content, and the WLD lint that refuses a broken one before the game boots.</summary>
public class WorldContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    /// <summary>A copy of the game's content with one file edited, in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string relativePath, string find, string replace)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string path = Path.Combine(Root, relativePath);
            string text = File.ReadAllText(path);
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-wld-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string code, string relativePath, string find, string replace, string mentions = "")
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == code && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheHollow_BuildsFromTheGamesContent()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        var layout = WorldContent.BuildLayout(loader, "region.ashen_hollow");

        Assert.Equal(4, layout.CellKeys.Length);
        Assert.Equal((41, 41, 5_000L), (layout.Space.Terrain.Columns, layout.Space.Terrain.Rows, layout.Space.Terrain.SpacingMm));
        Assert.Equal(2, layout.Doors.Length);
        Assert.All(layout.Doors, d => Assert.StartsWith("world.hollow.", d.FlagId));
        Assert.Equal(6, layout.Locations.Length);
        Assert.Equal(8_000, layout.Locations.Single(l => l.Id == "location.den_mouth").DiscoveryRadiusMm);   // PROTOTYPE.md O2
        Assert.InRange(layout.Space.Terrain.HeightsMm.Max() - layout.Space.Terrain.HeightsMm.Min(), 6_000, 11_000);   // content bible §3: about 10 m
        Assert.Equal(new MovementRules(3_200, 50, 160, 350, 1_600), WorldContent.BuildMovement(loader));
        Assert.Equal(new TierRules(150_000, 600_000, 2_000_000, 10_000), WorldContent.BuildTiers(loader));
        Assert.Equal(50, WorldContent.TickMilliseconds(loader));
    }

    /// <summary>M6: the Foldscar's three Quiet Stones and its heart are switches, and the fold that holds Tavar is a barrier (bible §16).</summary>
    [Fact]
    public void TheFoldscar_HasThreeStonesAndAHeart_AsSwitches_AndTheFoldThatHoldsTavar()
    {
        var layout = WorldContent.BuildLayout(Load(Path.Combine(RepoPaths.Root(), "content")), "region.ashen_hollow");

        Assert.Equal(new[] { "switch.stone_north", "switch.stone_southwest", "switch.stone_southeast", "switch.foldscar_heart" },
            layout.Switches.Select(s => s.Key));
        var heart = layout.FindSwitch("switch.foldscar_heart")!;
        Assert.Equal(("world.foldscar.steadied", "Steady", "heart of the Foldscar"), (heart.FlagId, heart.Verb, heart.Name));
        Assert.Equal(layout.Switches.Take(3).Select(s => s.FlagId), heart.Requires);
        Assert.Equal(new CircleBlocker("rock_foldscar_heart", 153_000, 48_000, 1_500, 3_500), heart.Body);
        Assert.All(layout.Switches.Take(3), s => Assert.Empty(s.Requires));
        var fold = Assert.Single(layout.Barriers);
        Assert.Equal(("barrier.foldscar_fold", "world.foldscar.steadied"), (fold.Key, fold.FlagId));
        Assert.Equal(new CircleBlocker("barrier.foldscar_fold", 145_000, 42_000, 3_000, 2_600), fold.Footprint);
        Assert.Contains(layout.Npcs, n => (n.NpcId, n.XMm, n.ZMm) == ("npc.ashen_hollow.tavar_orr", 145_000, 42_000));   // inside it
    }

    [Fact]
    public void ASwitchOnAStructureTheRegionDoesNotHave_IsRefused() =>
        AssertRefused("WLD013", "regions/ashen_hollow.yaml", "structure: rock_stone_north", "structure: rock_nowhere", "stands on structure 'rock_nowhere'");

    [Fact]
    public void ASwitchWhoseRequirementNothingInItsCellSets_IsRefused() =>
        // The heart moved onto the iron seam's rock, in Blackvein Cut: the stones that steady it stand in another cell.
        AssertRefused("WLD013", "regions/ashen_hollow.yaml", "structure: rock_foldscar_heart", "structure: rock_iron_seam",
            "requires world.foldscar.stone_north_aligned, which no switch in its cell sets");

    [Fact]
    public void ASwitchWithRequirements_SaysWhyItWillNotWork() =>
        AssertRefused("WLD013", "regions/ashen_hollow.yaml", "    locked_text: ", "    unlocked_text: ", "says why it will not work");

    [Fact]
    public void ABarrierNoSwitchInItsCellLifts_IsRefused() =>
        AssertRefused("WLD014", "regions/ashen_hollow.yaml", "circle_m: [145, 42, 3]", "circle_m: [45, 42, 3]",
            "is lifted by world.foldscar.steadied, which no switch in its cell sets");

    [Fact]
    public void ADoorNamingAnUndeclaredFlag_IsRefused() =>
        AssertRefused("WLD004", "regions/ashen_hollow.yaml", "flag_ref: world.hollow.longhouse_door_open", "flag_ref: world.hollow.no_such_flag");

    [Fact]
    public void APlaceInAnUnknownRegion_IsRefused() =>
        AssertRefused("WLD005", "locations/den_mouth.yaml", "region_ref: region.ashen_hollow", "region_ref: region.elsewhere");

    [Fact]
    public void AFlagThatDoesNotStartAtZero_IsRefused() =>
        AssertRefused("WLD006", "world_flags/hollow/longhouse_door_open.yaml", "default: 0", "default: 1");

    [Fact]
    public void AStringFlag_IsRefused() =>
        AssertRefused("WLD006", "world_flags/hollow/longhouse_door_open.yaml", "type: bool", "type: string");

    [Fact]
    public void ASpawnInsideAWall_IsRefused() =>
        AssertRefused("WLD007", "regions/ashen_hollow.yaml", "spawn: { position_m: [30, 158]", "spawn: { position_m: [44, 124.2]");   // in the lodge's south wall

    [Fact]
    public void BoundsBeyondTheRegionsCells_AreRefused() =>
        AssertRefused("WLD002", "regions/ashen_hollow.yaml", "bounds_m: { min: [0, 0], max: [200, 200] }", "bounds_m: { min: [0, 0], max: [250, 200] }");

    [Fact]
    public void ATerrainGridOfTheWrongShape_IsRefused() =>
        AssertRefused("WLD001", "regions/ashen_hollow.yaml", "  rows: 41", "  rows: 40");

    [Fact]
    public void AMissingMovementConfig_IsRefused_WhenThePackHasARegion() =>
        AssertRefused("WLD008", "config/base_speeds.yaml", "base_m_s: 3.2", "base_speed: 3.2");
}
