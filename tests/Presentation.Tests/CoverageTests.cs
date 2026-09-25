using System.Text.Json;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Audio;

namespace UNNAMED.Presentation.Tests;

/// <summary>Phase A's art coverage report (the owner's A3): what drew each visual, every greybox and why, and the gate line.</summary>
public class ArtCoverageTests
{
    [Fact]
    public void EachVisual_CountsOnce_AndAFallbackOnAnyOccasion_IsAFallback()
    {
        var coverage = new ArtCoverage();
        coverage.Resolved("structure", "rock_waystone", "landmark_ashen_waystone");
        coverage.Resolved("creature", "creature.beast.wolf_grey", "creature_frost_wolf");
        coverage.Resolved("creature", "creature.beast.wolf_grey", "creature_frost_wolf");
        coverage.Resolved("ground_item", "item.material.iron_ore", "prop_canvas_haversack");
        coverage.Fallback("ground_item", "item.material.iron_ore", "withheld: shards", "prop_canvas_haversack");
        coverage.Fallback("structure", "rock_rim_01", "no file at ready/rock_outcrop_rim_a: a cylinder", "rock_outcrop_rim_a");

        Assert.Equal(4, coverage.Requested);
        Assert.Equal(2, coverage.ResolvedCount);
        Assert.Equal(2, coverage.FallbackCount);
        Assert.Equal(2, coverage.UnexpectedCount);
        var wolf = coverage.Entries.Single(e => e.Key == "creature:creature.beast.wolf_grey");
        Assert.Equal(("resolved", 2, 0), (wolf.Outcome, wolf.Resolved, wolf.Fallbacks));
        var item = coverage.Entries.Single(e => e.Key == "ground_item:item.material.iron_ore");
        Assert.Equal(("fallback", "withheld: shards"), (item.Outcome, item.Why));
        Assert.Equal("UNNAMED art coverage: 4 requested, 2 resolved, 2 fallbacks, 2 unexpected", coverage.GateLine);
    }

    [Fact]
    public void TheAllowlist_AllowsByKeyOrByPrefix_AndNothingElse()
    {
        var coverage = new ArtCoverage();
        coverage.Allow(new Dictionary<string, string> { ["debug:*"] = "the F3 overlay only", ["water:stream"] = "reason" });
        coverage.Fallback("debug", "discovery_rings", "rings");
        coverage.Fallback("water", "stream", "a plane");
        coverage.Fallback("water", "pond", "a plane");
        coverage.Fallback("debugger", "x", "not under the prefix's colon");

        Assert.Equal(4, coverage.FallbackCount);
        Assert.Equal(2, coverage.UnexpectedCount);
        Assert.Equal("the F3 overlay only", coverage.Entries.Single(e => e.Key == "debug:discovery_rings").Allowed);
        Assert.Null(coverage.Entries.Single(e => e.Key == "water:pond").Allowed);
    }

    [Fact]
    public void TheShippedAllowlist_ReadsCleanly_AndAllowsOnlyWhatIsOutsideOrdinaryPlay()
    {
        var allowed = ArtCoverage.ParseAllowlist(File.ReadAllText(Repository.File("src", "Presentation", "Art", "art_coverage_allowlist.json")), out string? problem);
        Assert.Null(problem);
        Assert.Equal(new[] { "debug:*" }, allowed.Keys);
    }

    [Theory]
    [InlineData("""{ "allowed": { "debug:*": "" } }""", "needs its reason")]
    [InlineData("""{ "allowed": { "debug:*": null } }""", "needs its reason")]
    [InlineData("""{ "allow": {} }""", "no \"allowed\" object")]
    [InlineData("""{""", "not JSON")]
    public void AnAllowlistEntryWithoutItsReason_IsNotAllowed(string json, string why)
    {
        var allowed = ArtCoverage.ParseAllowlist(json, out string? problem);
        Assert.Empty(allowed);
        Assert.Contains(why, problem);
    }

    [Fact]
    public void TheReport_IsWrittenAsJsonAndMarkdown_WithTheGateLine()
    {
        string directory = Path.Combine(Path.GetTempPath(), "unnamed-coverage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coverage = new ArtCoverage();
            coverage.Resolved("terrain", "r_0_0:c_00_00", "material_loose_gravel");
            coverage.Fallback("structure", "rock_rim_01", "no file: a cylinder", "rock_outcrop_rim_a");
            string gate = coverage.Write(directory, "art_coverage", "test", new Dictionary<string, object?> { ["asset_root"] = "G:/assets" });

            Assert.Equal("UNNAMED art coverage: 2 requested, 1 resolved, 1 fallbacks, 1 unexpected", gate);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "art_coverage.json")));
            Assert.Equal(gate, json.RootElement.GetProperty("gate").GetString());
            Assert.Equal("structure:rock_rim_01", json.RootElement.GetProperty("unexpected")[0].GetProperty("key").GetString());
            Assert.Equal("G:/assets", json.RootElement.GetProperty("asset_root").GetString());
            string md = File.ReadAllText(Path.Combine(directory, "art_coverage.md"));
            Assert.Contains(gate, md);
            Assert.Contains("`structure:rock_rim_01` (asked for `rock_outcrop_rim_a`): no file: a cylinder", md);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}

/// <summary>Phase A's scatter rules: data the scatter view follows, read without trusting the file.</summary>
public class ScatterRulesTests
{
    [Fact]
    public void TheShippedRules_ReadCleanly()
    {
        var rules = ScatterRules.Parse(File.ReadAllText(Repository.File("src", "Presentation", "Art", "scatter_rules.json")));
        Assert.Empty(rules.Problems);
        Assert.Equal(new[] { "grass", "leaves", "stones", "tufts", "nettles", "ferns", "litter_a", "litter_b", "litter_c", "bark", "dandelions" },
            rules.Kinds.Select(k => k.Name));
        // Phase B's plant kinds scatter prepared models, each with its levels and a size range.
        Assert.All(rules.Kinds.Where(k => k.ModelId is not null), k =>
        {
            Assert.Matches("^(veg|env)_ph_", k.ModelId);
            Assert.NotNull(k.LodsM);
            Assert.InRange(k.SizeMin, 0.01f, k.SizeMax);
        });
        Assert.Equal(8, rules.Kinds.Count(k => k.ModelId is not null));
        Assert.Equal("classic", rules.Kinds.Single(k => k.Name == "leaves").OnlyWhen?["plants"]);
        Assert.Equal(3, rules.Paths["region.ashen_hollow"].Count);
        Assert.All(rules.Kinds, k => Assert.All(k.PerM2.Values, d => Assert.InRange(d, 0, k.MaxPerM2)));
        var grass = rules.Kinds.Single(k => k.Name == "grass");
        Assert.True(grass.OffPath);
        Assert.False(rules.Kinds.Single(k => k.Name == "stones").OffPath);
        Assert.Equal(new[] { "material_packed_dirt_ground" }, grass.BareYardGrounds);
    }

    [Theory]
    [InlineData("""{ "kinds": { "moss": { "max_per_m2": 1 } } }""", "kinds.moss: no mesh")]
    [InlineData("""{ "kinds": { "moss": { "mesh": "grass_clumps", "max_per_m2": 0 } } }""", "max_per_m2 is not a positive number")]
    [InlineData("""{ "kinds": { "moss": { "mesh": "grass_clumps", "max_per_m2": 1, "per_m2": { "a": 2 } } } }""", "outside 0 to max_per_m2")]
    [InlineData("""{ "paths": { "r": [ { "points": [[1, 2]] } ] } }""", "two or more [x, z] points")]
    [InlineData("""{ "paths": { "r": [ { "points": [[1, "2"], [3, 4]] } ] } }""", "two or more [x, z] points")]
    public void AMalformedRule_IsLeftOutWithWhy(string json, string why)
    {
        var rules = ScatterRules.Parse(json);
        Assert.Empty(rules.Kinds);
        Assert.Contains(rules.Problems, p => p.Contains(why));
    }

    [Fact]
    public void RulesThatAreNotJson_ScatterNothing()
    {
        var rules = ScatterRules.Parse("{ kinds");
        Assert.Empty(rules.Kinds);
        Assert.Single(rules.Problems);
    }
}

/// <summary>Phase A's audio coverage report (the owner's A4): the sound set against the event mapping.</summary>
public class AudioCoverageTests
{
    [Theory]
    [InlineData("sfx.player.footstep.dirt.walk.01", "sfx.player.footstep.dirt.walk")]
    [InlineData("sfx.magic.strain.high.01", "sfx.magic.strain.high")]
    [InlineData("sfx.ui.menu.open", "sfx.ui.menu.open")]
    [InlineData("amb.charwood_verge.01", "amb.charwood_verge")]
    public void AnIdsFamily_DropsItsVariationNumber(string id, string family) => Assert.Equal(family, AudioCoverage.FamilyOf(id));

    [Fact]
    public void TheStaticCheck_FindsUnreachableIds_MissingNames_AndIdsWithoutFiles()
    {
        var ids = new Dictionary<string, bool>
        {
            ["sfx.player.footstep.dirt.walk.01"] = true, ["sfx.player.footstep.dirt.walk.02"] = false, ["sfx.player.downed.01"] = true,
            ["sfx.magic.strain.high.01"] = true, ["sfx.ui.select"] = true,
        };
        var mapped = new[]
        {
            new MappedSound("sfx.player.footstep.dirt.walk", "footstep(dirt, walk)", false),
            new MappedSound("sfx.player.footstep.wood.walk", "footstep(wood, walk)", false),
            new MappedSound("sfx.magic.strain.high.01", "strain(high)", false),
            new MappedSound("sfx.ui.select", "confirm()", false),
            new MappedSound("sfx.magic.brace_ward.cast", "cast_start()", true),
        };
        var check = AudioCoverage.Check(ids, mapped);
        Assert.Equal(new[] { "sfx.magic.strain.high.01", "sfx.player.footstep.dirt.walk.01", "sfx.player.footstep.dirt.walk.02", "sfx.ui.select" }, check.Reachable);
        Assert.Equal(new[] { "sfx.player.downed.01" }, check.Unreachable);
        Assert.Equal(new[] { "sfx.player.footstep.wood.walk" }, check.MappedMissing.Select(m => m.Family));
        Assert.Equal(new[] { "sfx.magic.brace_ward.cast" }, check.OptionalAbsent.Select(m => m.Family));
        Assert.Equal(new[] { "sfx.player.footstep.dirt.walk.02" }, check.WithoutFile);
        Assert.Equal("UNNAMED audio coverage: 5 ids, 4 reachable, 1 unreachable, 1 mapped missing, 1 without file; this run 3 requested, 2 resolved, "
                     + "1 missing, 0 malformed, 0 runtime errors, 0 unmapped", AudioCoverage.GateLine(check, 3, 2, 1, 0, 0, 0));
    }

    [Fact]
    public void ARequestTheMappingDoesNotList_IsReportedUnmapped()
    {
        string directory = Path.Combine(Path.GetTempPath(), "unnamed-audio-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mapped = new[] { new MappedSound("sfx.ui.select", "confirm()", false) };
            var check = AudioCoverage.Check(new Dictionary<string, bool> { ["sfx.ui.select"] = true }, mapped);
            string gate = AudioCoverage.Write(directory, "audio_coverage", "test", check, mapped,
                new Dictionary<string, int> { ["sfx.ui.select"] = 2, ["sfx.ui.drift"] = 1 }, new[] { "sfx.ui.select" }, new[] { "sfx.ui.drift" },
                Array.Empty<string>(), new Dictionary<string, string>());
            Assert.EndsWith("this run 2 requested, 1 resolved, 1 missing, 0 malformed, 0 runtime errors, 1 unmapped", gate);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "audio_coverage.json")));
            Assert.Equal("sfx.ui.drift", json.RootElement.GetProperty("runtime").GetProperty("unmapped")[0].GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
