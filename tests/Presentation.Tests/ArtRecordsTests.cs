using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The Phase-1 technical audit's H-02, the part in ArtLibrary: a generated record with a field of the wrong kind is refused, with why, and
/// never throws - its one material or clip is drawn in greybox, the scene is built.
/// </summary>
public class ArtRecordsTests
{
    private const string Good = """
        { "asset_id": "material_x", "tile_size_m": 3.5,
          "maps": { "basecolor": "x_basecolor.png", "normal": "x_normal.png", "orm": "x_orm.png", "ao": "x_ao.png" },
          "pbr": { "normal_strength": 0.8 } }
        """;

    [Fact]
    public void AWellFormedMaterialRecord_IsRead()
    {
        var record = ArtRecords.Material(Good, out string? problem);
        Assert.Null(problem);
        Assert.Equal(new MaterialRecord("x_basecolor.png", "x_normal.png", "x_orm.png", 3.5f, 0.8f), record);
    }

    [Fact]
    public void AMaterialWithOnlyItsColourMap_TakesTheDefaults()
    {
        var record = ArtRecords.Material("""{ "maps": { "basecolor": "a.png" } }""", out string? problem);
        Assert.Null(problem);
        Assert.Equal(new MaterialRecord("a.png", null, null, ArtRecords.DefaultTileSizeM, 1f), record);
    }

    [Theory]
    [InlineData("""{ "maps": { "basecolor": null } }""", "maps.basecolor is null")]
    [InlineData("""{ "maps": { "basecolor": 7 } }""", "maps.basecolor is 7")]
    [InlineData("""{ "maps": { "basecolor": "" } }""", "maps.basecolor")]
    [InlineData("""{ "maps": { } }""", "maps.basecolor is missing")]
    [InlineData("""{ "maps": null }""", "maps is null")]
    [InlineData("""{ "tile_size_m": 4 }""", "names no maps")]
    [InlineData("""{ "maps": { "basecolor": "a.png", "normal": null } }""", "maps.normal is null")]
    [InlineData("""{ "maps": { "basecolor": "a.png", "orm": [1] } }""", "maps.orm is a list")]
    [InlineData("""{ "maps": { "basecolor": "../other/a.png" } }""", "not a file in the material's folder")]
    [InlineData("""{ "maps": { "basecolor": "C:\\elsewhere\\a.png" } }""", "not a file in the material's folder")]
    [InlineData("""{ "maps": { "basecolor": "a.png" }, "tile_size_m": 0 }""", "tile_size_m is 0")]
    [InlineData("""{ "maps": { "basecolor": "a.png" }, "tile_size_m": -2 }""", "tile_size_m is -2")]
    [InlineData("""{ "maps": { "basecolor": "a.png" }, "tile_size_m": "4" }""", "tile_size_m is \"4\"")]
    [InlineData("""{ "maps": { "basecolor": "a.png" }, "tile_size_m": null }""", "tile_size_m is null")]
    [InlineData("""{ "maps": { "basecolor": "a.png" }, "pbr": 1 }""", "pbr is a number")]
    [InlineData("""{ "maps": { "basecolor": "a.png" }, "pbr": { "normal_strength": null } }""", "pbr.normal_strength is null")]
    [InlineData("""[1, 2]""", "not an object")]
    [InlineData("""{ "maps": """, "not JSON")]
    [InlineData("", "not JSON")]
    public void AMalformedMaterialRecord_IsRefusedWithWhy_NeverThrown(string json, string why)
    {
        var record = ArtRecords.Material(json, out string? problem);
        Assert.Null(record);
        Assert.NotNull(problem);
        Assert.Contains(why, problem);
    }

    [Fact]
    public void AWellFormedClipRecord_IsRead()
    {
        var record = ArtRecords.Clip("""{ "loop": true, "duration_s": 1.2, "events": [ { "id": "foot_contact", "time": 0.0 }, { "id": "foot_leave", "time": 0.5 } ] }""",
            out string? problem);
        Assert.Null(problem);
        Assert.NotNull(record);
        Assert.True(record!.Loop);
        Assert.Equal(1.2, record.Seconds);
        Assert.Equal(new[] { ("foot_contact", 0.0), ("foot_leave", 0.5) }, record.Events);
    }

    [Fact]
    public void AClipRecordWithoutItsOptionalFields_DoesNotLoopAndHasNoEvents()
    {
        var record = ArtRecords.Clip("{}", out string? problem);
        Assert.Null(problem);
        Assert.False(record!.Loop);
        Assert.Equal(0, record.Seconds);
        Assert.Empty(record.Events);
    }

    [Theory]
    [InlineData("""{ "events": [ { "id": null, "time": 0.1 } ] }""", "event 0 has no id")]
    [InlineData("""{ "events": [ { "time": 0.1 } ] }""", "event 0 has no id")]
    [InlineData("""{ "events": [ { "id": "hit", "time": "soon" } ] }""", "event 0 (hit) has no time")]
    [InlineData("""{ "events": [ { "id": "hit" } ] }""", "event 0 (hit) has no time")]
    [InlineData("""{ "events": [ 3 ] }""", "event 0 is a number")]
    [InlineData("""{ "events": { "id": "hit" } }""", "events is an object")]
    [InlineData("""{ "loop": "yes" }""", "loop is \"yes\"")]
    [InlineData("""{ "loop": null }""", "loop is null")]
    [InlineData("""{ "duration_s": -1 }""", "duration_s is -1")]
    [InlineData("""{ "duration_s": null }""", "duration_s is null")]
    [InlineData("""null""", "not an object")]
    [InlineData("""{ "loop": tru }""", "not JSON")]
    public void AMalformedClipRecord_IsRefusedWithWhy_NeverThrown(string json, string why)
    {
        var record = ArtRecords.Clip(json, out string? problem);
        Assert.Null(record);
        Assert.NotNull(problem);
        Assert.Contains(why, problem);
    }

    /// <summary>The promoted seamless world materials are drawn: only the cast-iron tray is still withheld (Phase A).</summary>
    [Fact]
    public void TheSeamlessWorldMaterials_AreNoLongerWithheld()
    {
        using var bindings = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Repository.File("src", "Presentation", "Art", "art_bindings.json")));
        var withheld = bindings.RootElement.GetProperty("withheld").EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (string id in new[] { "packed_dirt_ground", "marsh_grass_turf", "loose_gravel", "churned_wet_mud", "rubble_stone_wall", "plaster_lath_wall",
                     "oak_plank_floor", "limestone_ashlar", "slate_roof_scale" })
            Assert.DoesNotContain("material_" + id, withheld);
        Assert.Contains("material_cast_iron_surface", withheld);
    }
}

internal static class Repository
{
    public static string File(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }
}
