namespace UNNAMED.Content.Tests;

/// <summary>
/// Brief D15: the world-hooks audit reads the machine-readable minimums and the region, location, spawn and node
/// content, and reports per area what is placed, what is missing and what the content model cannot hold. A missing
/// hook is a WARNING while the area is in development and an ERROR once it is released.
/// </summary>
public class WorldHookLintTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "unnamed-hooks-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void Write(string relativePath, string yaml)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml.Replace("\r\n", "\n") + "\n");
    }

    private WorldHookReport Audit()
    {
        var loader = new ContentLoader();
        loader.LoadAll(_root);
        return WorldHookChecks.Audit(_root, loader);
    }

    private static string Rules(string status, string min = "{town: 1}") => $$"""
        meta: { schema: 1 }
        band_scales: { "1-5": 1.0 }
        areas:
          - { slug: test-hollow, name: Test Hollow, region_ref: region.test, locations: [location.test], type: town, status: {{status}}, band: "1-5" }
        hooks:
          - { id: HOOK.STATION.FORGE, class: placeable, scope: area, skills: [skill.smithing], evidence: { field_equals: { section: stations, field: kind, equals: forge } }, min: {{min}}, band_scaled: false, note: "a forge" }
          - { id: HOOK.LOCK.CONTAINER.T1, class: placeable, scope: area, skills: [skill.lockpicking], evidence: { any_field: { section: containers, field: lock_tier } }, min: { town: 1 }, band_scaled: false, note: "a locked chest" }
          - { id: HOOK.WATER.FISH, class: node, scope: area, skills: [skill.fishing], evidence: { unmodelled: { why: "no water body" } }, min: { town: 1 }, band_scaled: false, note: "water" }
        """;

    private void Region(string body) => Write("regions/test.yaml", $$"""
        id: region.test
        kind: region
        schema: 1
        display_key: region.test.name
        tags: [region]
        region_key: r_0_0
        cells: [r_0_0:c_00_00]
        bounds_m: { min: [0, 0], max: [100, 100] }
        spawn: { position_m: [10, 10], facing_deg: 0 }
        {{body}}
        """);

    private void Location(string id, double x, double z) => Write($"locations/{id.Replace('.', '_')}.yaml", $$"""
        id: {{id}}
        kind: location
        schema: 1
        display_key: {{id}}.name
        tags: [location]
        region_ref: region.test
        kind_of_place: settlement
        anchor: { world: [{{x}}, 0.0, {{z}}] }
        """);

    [Fact]
    public void AHookPlacedInTheContentIsCounted()
    {
        Write("_world_hooks.yaml", Rules("development"));
        Location("location.test", 50, 50);
        Region("stations:\n  - { key: station.forge, kind: forge, position_m: [50, 50] }");
        var report = Audit();
        Assert.Contains(report.Findings, f => f.HookId == "HOOK.STATION.FORGE" && f.State == HookState.Placed && f.Placed == 1);
    }

    [Fact]
    public void ADevelopmentAreaMissingAHookWarnsAndDoesNotFail()
    {
        Write("_world_hooks.yaml", Rules("development"));
        Location("location.test", 50, 50);
        Region("stations: []");
        var report = Audit();
        var forge = Assert.Single(report.Findings.Where(f => f.HookId == "HOOK.STATION.FORGE"));
        Assert.Equal(HookState.Missing, forge.State);
        Assert.Equal("WARNING", forge.Severity(report.StatusOf("test-hollow")));
        Assert.False(report.Fails());
    }

    [Fact]
    public void AReleasedAreaMissingAHookIsAnError()
    {
        Write("_world_hooks.yaml", Rules("released"));
        Location("location.test", 50, 50);
        Region("stations: []");
        var report = Audit();
        var forge = Assert.Single(report.Findings.Where(f => f.HookId == "HOOK.STATION.FORGE"));
        Assert.Equal("ERROR", forge.Severity(report.StatusOf("test-hollow")));
        Assert.True(report.Fails());
    }

    [Fact]
    public void AHookTheSchemaCannotHoldIsReportedAsUnmodelledRatherThanPassing()
    {
        Write("_world_hooks.yaml", Rules("released"));
        Location("location.test", 50, 50);
        Region("stations: []");
        var report = Audit();
        Assert.Equal(HookState.Unmodelled, Assert.Single(report.Findings.Where(f => f.HookId == "HOOK.WATER.FISH")).State);
    }

    [Fact]
    public void AMountedSwitchCountsAtItsStructure()
    {
        Write("_world_hooks.yaml", """
            meta: { schema: 1 }
            band_scales: { "1-5": 1.0 }
            areas:
              - { slug: test-hollow, name: Test Hollow, region_ref: region.test, locations: [location.test], type: ruin, status: development, band: "1-5" }
            hooks:
              - { id: HOOK.OTHER.ANCHOR, class: placeable, scope: area, skills: [skill.anchoring], evidence: { any_field: { section: switches, field: flag_ref } }, min: { ruin: 1 }, band_scaled: false, note: "an anchor to still" }
            """);
        Location("location.test", 50, 50);
        Region("""
            structures:
              - { id: test_anchor, circle_m: [50, 50, 1.0], height_m: 2.0 }
            switches:
              - { key: switch.test.anchor, structure: test_anchor, flag_ref: world.test.anchor_stilled }
            """);
        var report = Audit();
        Assert.Equal(HookState.Placed, Assert.Single(report.Findings.Where(f => f.HookId == "HOOK.OTHER.ANCHOR")).State);
    }

    [Fact]
    public void APlacementBelongsToTheNearestAreasAnchor()
    {
        Write("_world_hooks.yaml", """
            meta: { schema: 1 }
            band_scales: { "1-5": 1.0 }
            areas:
              - { slug: near, name: Near, region_ref: region.test, locations: [location.near], type: town, status: development, band: "1-5" }
              - { slug: far, name: Far, region_ref: region.test, locations: [location.far], type: town, status: development, band: "1-5" }
            hooks:
              - { id: HOOK.STATION.FORGE, class: placeable, scope: area, skills: [skill.smithing], evidence: { field_equals: { section: stations, field: kind, equals: forge } }, min: { town: 1 }, band_scaled: false, note: "a forge" }
            """);
        Location("location.near", 20, 20);
        Location("location.far", 500, 500);
        Region("stations:\n  - { key: station.near_forge, kind: forge, position_m: [24, 24] }");
        var report = Audit();
        Assert.Equal(HookState.Placed, Assert.Single(report.Findings.Where(f => f.Area == "near")).State);
        Assert.Equal(HookState.Missing, Assert.Single(report.Findings.Where(f => f.Area == "far")).State);
    }

    [Fact]
    public void AMissingRulesFileIsAProblemRatherThanAPass()
    {
        Location("location.test", 50, 50);
        Region("stations: []");
        var report = Audit();
        Assert.NotEmpty(report.Problems);
        Assert.Empty(report.Findings);
    }
}
