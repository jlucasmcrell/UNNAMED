using System.Collections.Immutable;
using System.Text.Json;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The character creator (WP2's local part): the draft it holds becomes the spec a new game starts from, the name rules are the domain's,
/// worded for the player, and the bodies it offers (content <c>body.*</c>) are drawn with the bindings' <c>player_bodies</c>.
/// </summary>
public class CreatorTests
{
    private static CreatorDraft Draft(string name) =>
        CreatorDraft.Fresh("body.veth_f.lean", CreatorDraft.FirstCall, StartPoint.Road) with { Name = name, Pronouns = Pronouns.She };

    [Fact]
    public void ADraft_MakesTheSpecANewGameStartsFrom_EveryChoiceKept()
    {
        var spec = Draft("  Wenna   Lind ").Spec();

        Assert.Equal(new CharacterSpec("Wenna Lind", Pronouns.She, "people.veth", "body.veth_f.lean", "call.wayfarer") { Start = StartPoint.Road }, spec);
    }

    [Fact]
    public void AFreshDraft_HasAValueForEveryStep_ButTheName()
    {
        var fresh = CreatorDraft.Fresh("body.veth_m.average", "call.wayfarer", StartPoint.Road);

        Assert.Equal((Pronouns.They, CharacterSpec.DefaultPeople, "body.veth_m.average", "call.wayfarer"), (fresh.Pronouns, fresh.PeopleId, fresh.BodyPresetId, fresh.CallId));
        Assert.Equal("Give your character a name.", fresh.Problem);
        Assert.Throws<ArgumentException>(() => fresh.Spec());
    }

    [Theory]
    [InlineData("", "Give your character a name.")]
    [InlineData("    ", "Give your character a name.")]
    [InlineData("W", null)]
    [InlineData("Kester Vale-of-Stones", null)]
    [InlineData("abcdefghijklmnopqrstuvwx", null)]            // 24: the most a name can be
    [InlineData("abcdefghijklmnopqrstuvwxy", "A name is at most 24 characters.")]
    [InlineData("Tab\there", "A name cannot hold control characters.")]
    public void TheNameRules_AreTheDomains_WordedForThePlayer(string name, string? problem)
    {
        var draft = Draft(name);

        Assert.Equal(problem, draft.Problem);
        if (problem is null)
            Assert.Equal(CharacterSpec.Normalize(name), draft.Spec().Name);
        else
            Assert.Throws<ArgumentException>(() => draft.Spec());
    }

    [Fact]
    public void TheCounter_CountsTheNameAsItWillBeKept()
    {
        Assert.Equal("10 / 24", Draft("  Wenna    Lind  ").Count);
        Assert.Equal("0 / 24", Draft("").Count);
    }

    [Theory]
    [InlineData("body.default")]
    [InlineData("people.veth")]
    [InlineData("body")]
    public void ADraftWhoseBodyIsNotABodyPreset_IsRefusedByTheDomain_OnlyWhenItIsNotOne(string body)
    {
        var draft = Draft("Wenna Lind") with { BodyPresetId = body };

        if (body == "body.default")
            Assert.Equal("body.default", draft.Spec().BodyPresetId);
        else
            Assert.Throws<ArgumentException>(() => draft.Spec());
    }

    [Fact]
    public void PronounWords_AreTheThreeSets()
    {
        Assert.Equal(new[] { "he/him", "she/her", "they/them" }, new[] { Pronouns.He, Pronouns.She, Pronouns.They }.Select(CreatorDraft.Words));
    }

    // ── the bodies ──────────────────────────────────────────────────────────

    private const string Bindings = """
        {
          "default": "body.veth_m.average",
          "base_build": "average",
          "types": [
            { "id": "veth_m", "models": [ "mh_veth_m" ] },
            { "id": "veth_f", "models": [ "mh_veth_f_new", "mh_veth_f" ] }
          ]
        }
        """;

    private static PlayerBodies Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PlayerBodies.Read(document.RootElement);
    }

    private static BodyPreset Body(string type, string build) => new($"body.{type}.{build}", "people.veth", type, type, build, build, 1, true, 0,
        ImmutableSortedDictionary<string, int>.Empty, ImmutableSortedDictionary<string, BoneOffset>.Empty);

    [Fact]
    public void ABuildIsItsOwnModel_TheBaseBuildTheTypesModelItself_TheFirstInstalledDrawn()
    {
        var bodies = Read(Bindings);

        Assert.Equal("mh_veth_f", bodies.Model(Body("veth_f", "average"), m => m == "mh_veth_f"));
        Assert.Equal("mh_veth_f_new", bodies.Model(Body("veth_f", "average"), _ => true));
        Assert.Equal("mh_veth_f_heavy", bodies.Model(Body("veth_f", "heavy"), m => m == "mh_veth_f_heavy"));
        Assert.Equal("mh_veth_m_very_lean", bodies.Model(Body("veth_m", "very_lean"), _ => true));
        // A build whose model is not installed is not drawn at all: there is no stand-in shape any more.
        Assert.Null(bodies.Model(Body("veth_m", "very_heavy"), m => m == "mh_veth_m"));
        Assert.Null(bodies.Model(Body("veth_x", "average"), _ => true));
    }

    [Fact]
    public void NoBodiesBound_MeansTheDomainsDefault()
    {
        var none = Read("{}");

        Assert.Empty(none.Types);
        Assert.Equal(CharacterSpec.DefaultBodyPreset, none.Default);
        Assert.Null(none.Model(Body("veth_m", "average"), _ => true));
    }

    [Fact]
    public void AGarment_IsWornAsItsVariantBakedForTheBody()
    {
        Assert.Equal("player_shirt_m__mh_veth_m_heavy", PlayerBodies.Garment("player_shirt_m__mh_veth_m", "mh_veth_m_heavy"));
        Assert.Equal("player_shirt_m__mh_veth_m", PlayerBodies.Garment("player_shirt_m__mh_veth_m", "mh_veth_m"));
        Assert.Equal("loose_cape", PlayerBodies.Garment("loose_cape", "mh_veth_m_heavy"));
    }

    [Fact]
    public void TheShippedBindings_DrawEveryBodyContentOffers_AsAMetaHuman()
    {
        string root = Root();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "Presentation", "Art", "art_bindings.json")));
        var bodies = PlayerBodies.Read(document.RootElement.GetProperty("player_bodies"));
        var types = Directory.GetFiles(Path.Combine(root, "content", "bodies"), "*.yaml")
            .Select(f => File.ReadAllLines(f).Single(l => l.StartsWith("type: ", StringComparison.Ordinal))[6..]).Distinct().ToList();

        Assert.Equal(new[] { "veth_m", "veth_f" }, bodies.Types.Select(t => t.Type));
        Assert.Equal(types.OrderBy(t => t), bodies.Types.Select(t => t.Type).OrderBy(t => t));
        foreach (var type in bodies.Types)
            Assert.All(type.Models, m => Assert.StartsWith("mh_", m));
        Assert.True(File.Exists(Path.Combine(root, "content", "bodies", bodies.Default["body.".Length..].Replace('.', '_') + ".yaml")));
    }

    [Fact]
    public void TheRoad_IsOfferedToSkip_OnlyOnceWalked_AndTheSkipDefaultsOn()
    {
        var fresh = new ProfilePrefs();
        Assert.False(fresh.RoadWalked);
        Assert.True(fresh.SkipRoad);

        var walked = ClientPrefs.FromJson(new ClientPrefs { Profile = new ProfilePrefs { RoadWalked = true, SkipRoad = false } }.ToJson());
        Assert.True(walked.Profile.RoadWalked);
        Assert.False(walked.Profile.SkipRoad);
        // A preferences file from before the opening keeps the defaults.
        Assert.True(ClientPrefs.FromJson("""{ "profile": { "first_launch_done": true } }""").Profile.SkipRoad);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("the repository root");
    }
}
