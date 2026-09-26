using UNNAMED.Domain.Building;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Content.Tests;

/// <summary>
/// Building from content (M7 design §4.3, §4.10, §4.16, §4.20): the shipped pieces, <c>config.building</c>, the build area and the timber
/// stack; and each building lint refusing a file crafted to break it.
/// </summary>
public class BuildingContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    private static string GameContent => Path.Combine(RepoPaths.Root(), "content");

    /// <summary>
    /// A copy of a content directory with some files edited, and optionally one subdirectory left out, in a directory removed afterwards.
    /// </summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string source, params (string File, string Find, string Replace)[] edits)
            : this(source, null, edits)
        {
        }

        public EditedContent(string source, string? without, params (string File, string Find, string Replace)[] edits)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                         .Where(f => without is null || !Path.GetRelativePath(source, f).StartsWith(without + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            foreach (var (relative, find, replace) in edits)
            {
                string path = Path.Combine(Root, relative);
                string text = File.ReadAllText(path).Replace("\r\n", "\n");
                Assert.Contains(find, text);
                File.WriteAllText(path, text.Replace(find, replace));
            }
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-bld-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void Refused(string code, string expected, params (string File, string Find, string Replace)[] edits)
    {
        using var content = new EditedContent(GameContent, edits);
        var refusals = Load(content.Root).Errors.Where(e => e.Code == code).Select(e => e.Message).ToList();
        Assert.True(refusals.Any(r => r.Contains(expected, StringComparison.Ordinal)),
            $"expected a {code} refusal containing \"{expected}\"; got:\n" + string.Join("\n", refusals));
    }

    private const string Wall = "pieces/wall/timber.yaml";
    private const string Pad = "pieces/pad/timber.yaml";
    private const string Config = "config/building.yaml";
    private const string Region = "regions/ashen_hollow.yaml";
    private const string Area = "{ key: build_area.hollow_crossing, box_m: [87, 87, 114, 114], max_pieces: 256 }";

    [Fact]
    public void TheGamePack_BuildsTheCatalogueTheAreaAndTheTimberStack()
    {
        var loader = Load(GameContent);
        Assert.DoesNotContain(loader.Errors, e => e.Code.StartsWith("BLD", StringComparison.Ordinal) || e.Code == "WLD015");
        var setup = BuildingContent.Build(loader);

        // The pieces of §4.21 that E5 and E6 ship.
        Assert.Equal(new[] { "piece.door.timber", "piece.doorway.timber", "piece.pad.timber", "piece.roof.timber", "piece.wall.timber" },
            setup.Catalog.Pieces.Keys);
        var pad = setup.Catalog.Find("piece.pad.timber")!;
        Assert.Equal((PieceFamily.Pad, PieceSlot.Square, new BoundsMm(-1500, -1500, 1500, 1500), 200, false),
            (pad.Family, pad.Slot, pad.Bounds, pad.HealthMax, pad.SupportsRoof));
        Assert.Empty(pad.Parts);
        Assert.Equal(new[] { (SocketType.Edge, 0L, 1500L, SocketAxis.X), (SocketType.Edge, 0L, -1500L, SocketAxis.X), (SocketType.Edge, 1500L, 0L, SocketAxis.Z),
            (SocketType.Edge, -1500L, 0L, SocketAxis.Z), (SocketType.Square, 0L, 0L, SocketAxis.None) }, pad.Sockets.Select(s => (s.Type, s.XMm, s.ZMm, s.Axis)));
        var wall = setup.Catalog.Find("piece.wall.timber")!;
        Assert.Equal(new PiecePart(new BoundsMm(-1700, -200, 1700, 200), 3000, TraversalClass.Solid), Assert.Single(wall.Parts));
        Assert.True(wall.SupportsRoof);
        var doorway = setup.Catalog.Find("piece.doorway.timber")!;
        Assert.Equal(new[] { new BoundsMm(-1700, -200, -800, 200), new BoundsMm(800, -200, 1700, 200) }, doorway.Parts.Select(p => p.Box));
        Assert.Equal(new[] { SocketType.EdgeMount, SocketType.Door }, doorway.Sockets.Select(s => s.Type));
        var roof = setup.Catalog.Find("piece.roof.timber")!;
        Assert.Equal((PieceSlot.Roof, 100, 0, 0), (roof.Slot, roof.HealthMax, roof.Parts.Length, roof.Sockets.Length));
        // E6: the door, a leaf the width of the doorway's opening that blocks while shut, on the doorway's door socket.
        var door = setup.Catalog.Find("piece.door.timber")!;
        Assert.Equal((PieceFamily.Door, PieceSlot.Door, new BoundsMm(-800, -200, 800, 200), 120, false),
            (door.Family, door.Slot, door.Bounds, door.HealthMax, door.SupportsRoof));
        Assert.Equal(new PiecePart(new BoundsMm(-800, -200, 800, 200), 2400, TraversalClass.Door), Assert.Single(door.Parts));
        var mount = Assert.Single(door.Sockets);
        Assert.Equal((SocketType.DoorMount, 0L, 0L, SocketAxis.X), (mount.Type, mount.XMm, mount.ZMm, mount.Axis));
        Assert.Equal(new[] { 1, 2, 2, 1, 1 }, new[] { pad, wall, doorway, roof, door }.Select(p => Assert.Single(p.Cost).Count));
        Assert.All(setup.Catalog.Pieces.Values, p => Assert.Equal("item.material.timber", Assert.Single(p.Cost).ItemId));
        Assert.All(setup.Catalog.Pieces.Values, p => Assert.Equal(new[] { 0, 1, 2, 3 }, p.Rotations));

        // config.building: the shipped numbers, melee the only damage.
        Assert.Equal(BuildingConstants.Default with { Damage = setup.Constants.Damage }, setup.Constants);
        Assert.Equal(new[] { ("melee", 10) }, setup.Constants.Damage.Select(d => (d.Key, d.Value)));
        Assert.Null(setup.Constants.Problem());

        // The one build area, on the four-cell corner; the timber stack beside it, 80 timber that traders will not buy.
        var layout = WorldContent.BuildLayout(loader, "region.ashen_hollow");
        Assert.Equal(new BuildAreaSite("build_area.hollow_crossing", 87_000, 87_000, 114_000, 114_000, 256), Assert.Single(layout.BuildAreas));
        Assert.Equal(new ContainerSite("container.timber_stack", "loot.timber_stack", 84_000, 118_000, 12), layout.FindContainer("container.timber_stack"));
        var stack = ItemContent.BuildLootTables(loader)["loot.timber_stack"];
        Assert.Equal(0, stack.Rolls);
        var timber = Assert.Single(stack.Guaranteed);
        Assert.Equal(("item.material.timber", 80, 80), (timber.ItemId, timber.CountMin, timber.CountMax));
        var item = ItemContent.BuildCatalog(loader).Find("item.material.timber")!;
        Assert.Equal((20, true), (item.StackMax, item.NoSell));
    }

    /// <summary>
    /// BLD006 as the owner ruled it (2026-09-26, the E5 STOP): config.building is required wherever a piece or a build area exists, and is
    /// validated in full whenever it is present - even in a pack with nothing to build, where a valid one is allowed.
    /// </summary>
    [Fact]
    public void Bld006_RequiresTheConfigWhereAnythingCanBeBuilt_AndValidatesItWheneverPresent()
    {
        List<string> Bld006(string? without, params (string File, string Find, string Replace)[] edits)
        {
            using var content = new EditedContent(GameContent, without, edits);
            return Load(content.Root).Errors.Where(e => e.Code.StartsWith("BLD", StringComparison.Ordinal)).Select(e => $"{e.Code}: {e.Message}").ToList();
        }
        var noConfig = (Config, "id: config.building", "id: config.building_old");
        var noArea = (Region, Area + "\n", "");

        // 1. Something to build and no config.building: refused - pieces and an area, or an area alone.
        Assert.Contains("BLD006: config.building is missing, but the content has pieces or build areas", Bld006(null, noConfig));
        Assert.Contains("BLD006: config.building is missing, but the content has pieces or build areas", Bld006("pieces", noConfig));
        // 2. Nothing to build and a valid config.building: allowed.
        Assert.Empty(Bld006("pieces", noArea));
        // 3. Nothing to build and an invalid config.building: refused all the same.
        Assert.Contains("BLD006: config.building: place_reach_m must be between 1 and 12",
            Bld006("pieces", noArea, (Config, "place_reach_m: 6.0", "place_reach_m: 20.0")));
        Assert.Contains("BLD006: config.building: 'navigability_radius_m' is not a building setting",
            Bld006("pieces", noArea, (Config, "refund_percent: 50", "refund_percent: 50\nnavigability_radius_m: 0.35")));
    }

    [Fact]
    public void EachBuildingLint_RefusesItsCraftedBadFile()
    {
        // BLD001: the catalogue builds.
        Refused("BLD001", "piece.wall.timber does not build", (Wall, "bounds_m: [-1.7, -0.2, 1.7, 0.2]", "bounds_m: [-1.7, -0.2, 1.7]"));
        // BLD002: closed fields and keys; the family's slot; quarter turns; health.
        Refused("BLD002", "'colour' is not a piece field", (Wall, "health_max: 200\n", "health_max: 200\ncolour: brown\n"));
        Refused("BLD002", "family 'fence' is not one of", (Wall, "family: wall", "family: fence"));
        Refused("BLD002", "a wall goes in the edge slot, not square", (Wall, "slot: edge", "slot: square"));
        Refused("BLD002", "rotations must be distinct quarter turns", (Wall, "rotations: [0, 1, 2, 3]", "rotations: [0, 1, 4]"));
        Refused("BLD002", "health_max must be at least 1", (Wall, "health_max: 200", "health_max: 0"));
        Refused("BLD002", "a square socket's axis is absent", (Pad, "{ type: square, at_m: [0, 0] }", "{ type: square, at_m: [0, 0], axis: x }"));
        // BLD003: consistent with the lattice.
        Refused("BLD003", "the edge socket at (0, 1.4) is not on the lattice", (Pad, "at_m: [0, 1.5], axis: x", "at_m: [0, 1.4], axis: x"));
        Refused("BLD003", "a part lies outside the piece's bounds", (Wall, "box_m: [-1.7, -0.2, 1.7, 0.2]", "box_m: [-1.7, -0.2, 1.7, 0.3]"));
        Refused("BLD003", "only a door has a door part", (Wall, "traversal: solid", "traversal: door"));
        Refused("BLD003", "a square or roof piece's bounds are exactly the square", (Pad, "bounds_m: [-1.5, -1.5, 1.5, 1.5]", "bounds_m: [-1.5, -1.5, 1.5, 1.4]"));
        // BLD004: the cost is in defined items.
        Refused("BLD004", "cost names 'item.material.oak', which is not an item", (Wall, "item_ref: item.material.timber", "item_ref: item.material.oak"));
        // BLD006: config.building present exactly when there is something to build, and sane.
        Refused("BLD006", "place_reach_m must be between 1 and 12", (Config, "place_reach_m: 6.0", "place_reach_m: 20.0"));
        Refused("BLD006", "module_m must be 3.0", (Config, "module_m: 3.0", "module_m: 2.0"));
        Refused("BLD006", "damage 'fire' is not a damage source", (Config, "melee: 10", "melee: 10\n  fire: 5"));
        Refused("BLD006", "'navigability_radius_m' is not a building setting", (Config, "refund_percent: 50", "refund_percent: 50\nnavigability_radius_m: 0.35"));
        Refused("BLD006", "config.building is missing", (Config, "id: config.building", "id: config.building_old"));
        // BLD007: on the lattice, clear of authored geometry and of every protected zone.
        Refused("BLD007", "corners are not on the 3 m building lattice", (Region, Area, Area.Replace("[87, 87, 114, 114]", "[88, 87, 114, 114]")));
        Refused("BLD007", "meets ground kept clear (npc.ashen_hollow.sel_arien's place)", (Region, Area, Area.Replace("[87, 87, 114, 114]", "[66, 117, 78, 129]")));
        Refused("BLD007", "overlaps", (Region, Area, Area.Replace("[87, 87, 114, 114]", "[48, 135, 63, 147]")));
        // BLD009: the ceilings.
        Refused("BLD009", "allow 300 pieces; a region holds at most 256", (Region, Area, Area.Replace("256", "300")));
        Refused("BLD009", "the build areas meeting r_0_0:c_00_00 allow 300 pieces", (Region, Area, Area.Replace("256", "300")));
        // WLD015: the area's shape.
        Refused("WLD015", "build area key 'area.crossing' must start with 'build_area.'", (Region, Area, Area.Replace("build_area.hollow_crossing", "area.crossing")));
        Refused("WLD015", "must lie inside the walkable bounds", (Region, Area, Area.Replace("[87, 87, 114, 114]", "[87, 87, 213, 114]")));
        Refused("WLD015", "must allow at least one piece", (Region, Area, Area.Replace("256", "0")));
    }
}
