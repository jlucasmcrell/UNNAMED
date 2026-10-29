using System.Text.Json;
using UNNAMED.Content;

namespace UNNAMED.Content.Tests;

/// <summary>content/credits.yaml: the Credits screen's data, held to the art bindings the game ships.</summary>
public class CreditsContentTests
{
    private static string Repo
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "credits.yaml")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("content/credits.yaml not found above the test binaries");
        }
    }

    private static CreditsContent Credits => CreditsContent.Load(Path.Combine(Repo, "content", "credits.yaml"));

    [Theory]
    [InlineData("sedge_drowned","DM-913")]
    [InlineData("sedge_eel_serpent","KrazyKaijus")]
    public void SedgeImportedCreaturesKeepTheirArtistAndLicence(string model,string artist)
    {
        var entry=Credits.Credit(model);
        Assert.NotNull(entry);
        Assert.False(Credits.IsFirstParty(model));
        Assert.Contains(artist,entry!.Text);
        Assert.Contains("CC BY 4.0",entry.Text);
        Assert.Contains(entry.Lines,line=>line.Contains("sketchfab.com",StringComparison.Ordinal));
    }

    [Fact]
    public void BothDisclosuresNameAmbientChatGeneratedImageTexturesAndVoice()
    {
        string screen = string.Join("\n", Credits.Entries.Select(e => e.Text));
        string notice = File.ReadAllText(Path.Combine(Repo, "tools", "release", "payload", "THIRD_PARTY_NOTICES", "NOTICE.txt"));
        foreach (string text in new[] { screen, notice })
        {
            Assert.Contains("3,000", text);
            Assert.Contains("DeepSeek", text);
            Assert.Contains("AI-generated image textures", text);
            Assert.Contains("ElevenLabs", text);
        }
    }

    private static readonly string[] BindingFiles =
    {
        Path.Combine("content", "_art_bindings.json"),
        Path.Combine("content", "_art_bindings_barrowworks.json"),
        Path.Combine("src", "Presentation", "Art", "art_bindings.json"),
    };

    /// <summary>Every model id the bindings name: a "model" string, a "models" list, a "spent" model, anywhere in the file.</summary>
    private static IEnumerable<string> BoundModels(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var p in element.EnumerateObject())
            {
                if (p.Name == "model" && p.Value.ValueKind == JsonValueKind.String)
                    yield return p.Value.GetString()!;
                else if ((p.Name == "models" || p.Name == "spent") && p.Value.ValueKind == JsonValueKind.Array)
                    foreach (var m in p.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String))
                        yield return m.GetString()!;
                else if (p.Name == "spent" && p.Value.ValueKind == JsonValueKind.String)
                    yield return p.Value.GetString()!;
                else
                    foreach (var inner in BoundModels(p.Value))
                        yield return inner;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var inner in BoundModels(item))
                    yield return inner;
    }

    [Fact]
    public void TheStudioLineComesFirst()
    {
        var credits = Credits;
        Assert.Equal("Rift Studios — J.L. McRell", credits.Studio);
        Assert.Contains("Rift Studios — J.L. McRell", credits.StudioNote);
    }

    [Fact]
    public void EveryBoundModelIsCreditedOrFirstParty()
    {
        var credits = Credits;
        var unknown = new SortedSet<string>();
        foreach (string file in BindingFiles)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, file)));
            foreach (string model in BoundModels(doc.RootElement))
                if (credits.Credit(model) is null && !credits.IsFirstParty(model))
                    unknown.Add($"{model} ({file})");
        }
        Assert.True(unknown.Count == 0, "bound models with no credits entry and not first-party:\n" + string.Join("\n", unknown));
    }

    [Theory]
    [InlineData("Fab", "Fab Standard License")]
    [InlineData("Fab motion", "Fab Standard License")]
    [InlineData("Rokoko", "Rokoko")]
    [InlineData("Mixamo", "Mixamo")]
    [InlineData("CMU", "mocap.cs.cmu.edu")]
    [InlineData("KayKit", "KayKit")]
    [InlineData("Quaternius", "Quaternius")]
    [InlineData("Poly Haven", "Poly Haven")]
    [InlineData("BlenderKit", "BlenderKit")]
    [InlineData("Cosmos", "Cosmos Premium")]
    [InlineData("MetaHuman", "MetaHuman")]
    [InlineData("WildMesh", "WildMesh")]
    [InlineData("HumanShaders", "HumanShaders")]
    [InlineData("Sketchfab", "sketchfab.com")]
    [InlineData("Stable Audio", "Stability AI")]
    [InlineData("Godot", "Godot Engine")]
    public void ASourceNamedInTheBindingsOrTheLedgerHasItsCreditLine(string source, string credited)
    {
        string text = string.Join("\n", Credits.Entries.Select(e => e.Text + " " + string.Join(" ", e.Lines)));
        Assert.Contains(credited, text);
        // And where the bindings name the source, the credits must too.
        string bindings = string.Join("\n", BindingFiles.Select(f => File.ReadAllText(Path.Combine(Repo, f))));
        if (bindings.Contains(source, StringComparison.Ordinal))
            Assert.Contains(credited, text);
    }

    [Fact]
    public void EveryEntryHasWordsAndEveryLedgerIdIsShapedLikeOne()
    {
        foreach (var entry in Credits.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Text));
            Assert.All(entry.Ledger, id => Assert.Matches("^[A-Z]{1,2}(-[A-Z]+)?[0-9]+$", id));
        }
    }
}
