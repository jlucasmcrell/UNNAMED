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
        Assert.Equal(new[] { "grass", "leaves", "stones", "tufts", "blades_a", "blades_b", "blades_c", "blades_d", "nettles", "ferns",
                "litter_a", "litter_b", "litter_c", "bark", "dandelions", "brambles", "mirrorferns", "heather", "boneleaf", "reeds", "rock_clusters" },
            rules.Kinds.Select(k => k.Name));
        // Phase B's plant kinds scatter prepared models (sourced, veg_ph_*, or generated, veg_gen_*; the demo's undergrowth and rocks from
        // the library's own flora_* and rock_* models), each with its levels and a size range.
        Assert.All(rules.Kinds.Where(k => k.ModelId is not null), k =>
        {
            Assert.Matches("^((veg|env)_(ph|gen)_|flora_|rock_)", k.ModelId);
            Assert.NotNull(k.LodsM);
            Assert.InRange(k.SizeMin, 0.01f, k.SizeMax);
        });
        Assert.Equal(18, rules.Kinds.Count(k => k.ModelId is not null));
        // Reeds follow the Charwood drainage (near_water), nothing else does.
        Assert.Equal(new[] { "reeds" }, rules.Kinds.Where(k => k.NearWaterAdd > 0).Select(k => k.Name));
        Assert.Equal("classic", rules.Kinds.Single(k => k.Name == "leaves").OnlyWhen?["plants"]);
        // The old crossed-card grass is the classic look only: model plants replace it with geometry blades (remediation, H07).
        Assert.Equal("classic", rules.Kinds.Single(k => k.Name == "grass").OnlyWhen?["plants"]);
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
    public void TheRegionRules_CastShadowsOnlyFromStillKinds()
    {
        // The grass-shadow blink (owner, 2026-10-02 and again 2026-10-15): a swaying plant in the sun's shadow map blinks as it crosses
        // the map's texels, and an asset swap put real-scan grass with shadows on back into the playtest. Plants that sway receive
        // shadows only; what casts is still and keeps its shadow on every level, so it never switches off at a band's edge either.
        var rules = ScatterRules.Parse(File.ReadAllText(Repository.File("content", "_scatter_rules.json")));
        Assert.Empty(rules.Problems);
        Assert.All(rules.Kinds.Where(k => k.Shadows), k =>
        {
            Assert.False(k.Wind, $"{k.Name} sways and casts a shadow");
            Assert.True(k.FarShadows, $"{k.Name} casts from its near levels only");
        });
    }

    [Fact]
    public void FarShadows_IsReadAndOffByDefault()
    {
        var rules = ScatterRules.Parse("""
            { "kinds": { "rock": { "mesh": "model:rock_a", "max_per_m2": 1, "shadows": true, "wind": false, "far_shadows": true },
                         "fern": { "mesh": "model:fern_a", "max_per_m2": 1, "shadows": true } } }
            """);
        Assert.True(rules.Kinds.Single(k => k.Name == "rock").FarShadows);
        Assert.False(rules.Kinds.Single(k => k.Name == "fern").FarShadows);
    }

    [Fact]
    public void Otherworld_FieldAndOtherfloraAreRead()
    {
        // The otherworld concepts (art direction D.1): the shipped region names its field, and the signature plants draw only under a concept.
        var shipped = ScatterRules.Parse(File.ReadAllText(Repository.File("content", "_scatter_rules.json")));
        Assert.NotNull(shipped.Otherworld);
        Assert.Equal(2, shipped.Otherworld!.Calm.Count);
        Assert.All(new[] { "veilthistle", "listening_grass", "nightlace" },
            n => Assert.Equal("subtle|bold", shipped.Kinds.Single(k => k.Name == n).OnlyWhen?["otherworld"]));

        var rules = ScatterRules.Parse("""
            { "otherworld": { "calm": [[1, 2]], "fold": [3, 4], "fold_m": [10, 50] },
              "kinds": { "fern": { "mesh": "model:fern_a", "max_per_m2": 1, "otherflora": { "hue": -20, "glow": [0.1, 0.2, 0.3], "late": 2 } },
                         "rock": { "mesh": "model:rock_a", "max_per_m2": 1 } } }
            """);
        Assert.Empty(rules.Problems);
        Assert.Equal(new GroundPoint(3, 4), rules.Otherworld!.Fold);
        var fern = rules.Kinds.Single(k => k.Name == "fern").Otherflora!;
        Assert.Equal(-20, fern.Hue);
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, fern.Glow);
        Assert.Equal(1, fern.Late);
        Assert.Null(rules.Kinds.Single(k => k.Name == "rock").Otherflora);
        Assert.Single(ScatterRules.Parse("""{ "otherworld": { "calm": [], "fold": [0, 0], "fold_m": [1, 2] } }""").Problems);
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

    /// <summary>
    /// The owner's finding (2026-10-25): a wolf, a boar, a spider and a golem all sounded alike under a weapon, and every creature landed
    /// the same generic blow. Every creature the content defines is bound, across the base file and its overlays, to a body material
    /// with impact sets of its own (never the generic "flesh") and to its own attack (the swing and blow families build_combat.py makes).
    /// </summary>
    [Fact]
    public void EveryCreature_ResolvesToItsOwnMaterialAndBlow()
    {
        var materials = new[] { "fur", "hide", "chitin", "bone", "plate", "stone" };
        var blows = new[] { "bite", "claw", "gore", "sting", "slam" };
        var bound = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string file in new[] { Repository.File("src", "Presentation", "Art", "art_bindings.json"), Repository.File("content", "_art_bindings.json"),
                     Repository.File("content", "_art_bindings_barrowworks.json") })
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var creature in json.RootElement.GetProperty("creatures").EnumerateObject())
            {
                var fields = bound.TryGetValue(creature.Name, out var known) ? known : bound[creature.Name] = new(StringComparer.Ordinal);
                foreach (var field in creature.Value.EnumerateObject().Where(f => f.Value.ValueKind == JsonValueKind.String))
                    fields[field.Name] = field.Value.GetString()!;
            }
        }
        var defined = Directory.EnumerateFiles(Repository.File("content", "creatures"), "*.yaml", SearchOption.AllDirectories)
            .Select(f => File.ReadLines(f).First(l => l.StartsWith("id: ", StringComparison.Ordinal))[4..].Trim()).ToList();
        Assert.Equal(20, defined.Count);
        Assert.Contains("creature.beast.hunting_spider_matriarch", defined);
        Assert.All(defined, id =>
        {
            Assert.True(bound.TryGetValue(id, out var fields), $"{id} has no binding");
            Assert.Contains(fields!.GetValueOrDefault("flesh"), materials);
            Assert.Contains(fields.GetValueOrDefault("blow"), blows);
        });
        // The families are told apart: no two kinds of beast share both a body and a blow.
        Assert.Equal(new[] { "bone/claw", "chitin/sting", "fur/bite", "hide/gore", "plate/slam" },
            defined.Select(id => $"{bound[id]["flesh"]}/{bound[id]["blow"]}").Distinct().Order(StringComparer.Ordinal));
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
