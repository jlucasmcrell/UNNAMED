using UNNAMED.World.Runtime;

namespace UNNAMED.Content.Tests;

/// <summary>
/// Chatter from content (WP16): the game's templates build and cover every use the schema names, and the corpus lint refuses what could
/// not be said - an unknown tag or slot, a long line, a raw ID, a proper noun outside the glossary, a banned term, a near-duplicate, a
/// line without its provenance.
/// </summary>
public class ChatterContentTests : IDisposable
{
    private readonly List<string> _roots = new();

    public void Dispose()
    {
        foreach (string root in _roots.Where(Directory.Exists))
            Directory.Delete(root, recursive: true);
    }

    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    [Fact]
    public void ChattersSchema_IsPartOfTheContentHash()
    {
        string source = Path.Combine(RepoPaths.Root(), "content");
        string root = Path.Combine(Path.GetTempPath(), "unnamed-cht-hash-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        string before = Load(root).ComputeContentHash();
        File.AppendAllText(Path.Combine(root, "chatter", "_schema.yaml"), "\n# edited\n");
        Assert.NotEqual(before, Load(root).ComputeContentHash());
    }

    [Fact]
    public void TheGamesChatter_Lints_AndItsTemplatesCoverEveryUse_ByHand()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.DoesNotContain(loader.Errors, e => e.Code.StartsWith("CHT", StringComparison.Ordinal));
        var schema = ChatterContent.ReadSchema(loader);
        var chat = ChatterContent.Build(loader);

        Assert.Equal(10, schema.SpeakerTypes.Count);
        Assert.Equal(new[] { "ashen_hollow", "cairnwold", "old_east_road", "wilds" }, schema.Places.Keys);
        Assert.Equal(schema.Places, chat.Places);
        Assert.InRange(chat.Templates.Length, 36, 60);
        Assert.All(schema.Uses.Keys, use => Assert.NotEmpty(chat.TemplatesFor(use)));
        Assert.All(chat.Templates, t => Assert.False(t.AiGenerated));
        Assert.All(chat.Templates, t => Assert.Equal(schema.Uses[t.Use!].Channel, t.Channel));
        Assert.NotEmpty(chat.Corpus);
        Assert.All(chat.Corpus, line => Assert.True(line.AiGenerated));   // every corpus line comes from a recorded batch
        // Every speaker x place x time-band cell can always say something: the corpus is written to leave no cell thin.
        Assert.Empty(ChatterContent.Coverage(chat.Corpus, schema));
        Assert.Equal(20_000, chat.Rules.LocalRadiusMm);
        Assert.Equal((6, 24), (chat.Rules.SpeakerRepeatHours, chat.Rules.RegionRepeatHours));
    }

    private const string Good = "{ key: chat.b0001.0001, text: \"Rain's set in over {place}. The ditches past Cairnwold will run by dusk.\", speakers: [farmer], places: [cairnwold], times: [afternoon], weather: [rain], ai_generated: true }";

    /// <summary>A pack of chatter alone: the game's schema and banned terms, a small glossary, one recorded batch, and the lines given.</summary>
    private ContentLoader Pack(IEnumerable<string> lines, string batches = "[{ id: b0001, model: test-model, date: 2026-10-17, prompt_hash: \"sha256:0000000000000000000000000000000000000000000000000000000000000000\" }]")
    {
        string root = Path.Combine(Path.GetTempPath(), "unnamed-cht-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);   // the lint reads the meta files again, so the pack stays until the test ends
        string chatter = Path.Combine(root, "chatter");
        Directory.CreateDirectory(chatter);
        string source = Path.Combine(RepoPaths.Root(), "content", "chatter");
        File.Copy(Path.Combine(source, "_schema.yaml"), Path.Combine(chatter, "_schema.yaml"));
        File.Copy(Path.Combine(source, "_banned.yaml"), Path.Combine(chatter, "_banned.yaml"));
        File.WriteAllText(Path.Combine(chatter, "_glossary.yaml"), "terms:\n  - { term: Cairnwold }\n  - { term: Ashen Hollow }\n");
        File.WriteAllText(Path.Combine(chatter, "_provenance.yaml"), $"batches: {batches}\n");
        File.WriteAllText(Path.Combine(chatter, "b0001.yaml"),
            "id: chatter.b0001\nkind: chatter\nschema: 1\ndisplay_key: chatter.b0001.name\ntags: [chatter]\nrole: corpus\nbatch: b0001\nlines:\n"
            + string.Concat(lines.Select(l => "  - " + l + "\n")));
        return Load(root);
    }

    [Fact]
    public void AGoodLine_Lints_AndFillsItsCell()
    {
        var loader = Pack(new[] { Good });
        Assert.Empty(loader.Errors);
        var (_, gaps) = ChatterContent.Lint(loader);
        Assert.Equal(1, gaps.Single(g => (g.SpeakerType, g.Place, g.TimeBand) == ("farmer", "cairnwold", "afternoon")).Lines);
        var line = Assert.Single(ChatterContent.Build(loader).Corpus);
        Assert.Equal((ChatChannel.Local, "b0001", true), (line.Channel, line.Batch, line.AiGenerated));
        Assert.Equal(new[] { "place" }, line.Slots);
    }

    [Theory]
    [InlineData("chat.b0001.0001,", "chatter_1,", "CHT003", "not a corpus key")]
    [InlineData("chat.b0001.0001,", "chat.b0002.0001,", "CHT003", "not of the file's batch")]
    [InlineData("speakers: [farmer]", "speakers: [wizard]", "CHT004", "speaker 'wizard'")]
    [InlineData("weather: [rain]", "weather: [fold]", "CHT004", "weather 'fold'")]
    [InlineData("ai_generated: true }", "channel: party, ai_generated: true }", "CHT004", "local or world")]
    [InlineData("{place}", "{anchor}", "CHT005", "only templates may use")]
    [InlineData("{place}", "{nonsense}", "CHT005", "not a placeholder")]
    [InlineData("over {place}.", "over {claimant}.", "CHT005", "waits on bounty_claimed")]
    [InlineData("over {place}.", "over {place.", "CHT005", "brace that is not")]
    [InlineData("will run by dusk.", "will run by dusk, and the low field with them, and the lane past the mill, and the sheep will want moving before the light goes.", "CHT006", "at most 140")]
    [InlineData("The ditches", "The loot.wolf_grey ditches", "CHT007", "raw content ID")]
    [InlineData("The ditches", "The wolf_grey ditches", "CHT007", "reads as an ID")]
    [InlineData("past Cairnwold", "past Gorvath", "CHT008", "'Gorvath'")]
    [InlineData("will run by dusk.", "will run by dusk, okay.", "CHT009", "modern slang")]
    [InlineData("Rain's set in", "Press on, rain's set in", "CHT009", "ui words")]
    [InlineData("will run by dusk.", "will run by dusk, give or take.", "CHT009", "named npc tics")]
    [InlineData("ai_generated: true", "ai_generated: false", "CHT011", "ai_generated: true from a recorded batch")]
    public void ALineThatCannotBeSaid_IsRefused(string find, string replace, string code, string mentions)
    {
        Assert.Contains(find, Good);
        var loader = Pack(new[] { Good.Replace(find, replace) });
        Assert.Contains(loader.Errors, e => e.Code == code && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void ALineFromABatchNobodyRecorded_IsRefused() =>
        Assert.Contains(Pack(new[] { Good }, batches: "[]").Errors, e => e.Code == "CHT011" && e.Message.Contains("does not record", StringComparison.Ordinal));

    [Fact]
    public void ABadProvenanceRecord_IsRefused() =>
        Assert.Contains(Pack(new[] { Good }, batches: "[{ id: b0001, model: m, date: 17-10-2026, prompt_hash: x }]").Errors, e => e.Code == "CHT001");

    [Fact]
    public void ANearDuplicate_IsRefused_AndADifferentLine_IsNot()
    {
        string twin = Good.Replace("chat.b0001.0001", "chat.b0001.0002").Replace("will run by dusk.", "will run by dusk!");
        string other = Good.Replace("chat.b0001.0001", "chat.b0001.0003").Replace("Rain's set in over {place}. The ditches past Cairnwold will run by dusk.",
            "Hens are off their laying again. Something has been at the coop.");
        var loader = Pack(new[] { Good, twin, other });
        var duplicate = Assert.Single(loader.Errors);
        Assert.Equal("CHT010", duplicate.Code);
        Assert.Contains("chat.b0001.0002 is a near-duplicate of chat.b0001.0001", duplicate.Message);
    }

    [Fact]
    public void ProperNouns_AreTheCapitalisedWordsInsideASentence()
    {
        Assert.Equal(new[] { "Cairnwold", "Gorvath" }, ChatterContent.ProperNouns("I'd go past Cairnwold. Then Gorvath's mill - Rain or not, I said."));
    }
}
