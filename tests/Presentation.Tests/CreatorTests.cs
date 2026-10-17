using System.Text.Json;
using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The character creator (WP2's local part): the draft it holds becomes the spec a new game starts from, the name rules are the domain's,
/// worded for the player, and the bodies it offers are read from the bindings' <c>player_bodies</c>.
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
          "types": [
            { "id": "veth_m", "label": "Masculine", "models": { "production": [ "pc_veth_m_body_c" ], "metahuman": [ "mh_veth_m" ] } },
            { "id": "veth_f", "label": "Feminine", "models": { "production": [ "pc_veth_f_new", "pc_veth_f" ], "metahuman": [ "mh_veth_f" ] } }
          ],
          "builds": [
            { "id": "lean", "label": "Lean", "girth": 0.93 },
            { "id": "average", "label": "Average", "girth": 1.0 },
            { "id": "heavy", "label": "Heavy", "girth": 3.0 }
          ]
        }
        """;

    private static PlayerBodies Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PlayerBodies.Read(document.RootElement);
    }

    [Fact]
    public void Bodies_AreATypeAndABuild_AndBodyDefaultIsTheDefault()
    {
        var bodies = Read(Bindings);

        Assert.Equal(("veth_f", "lean"), bodies.Parse("body.veth_f.lean") is { } p ? (p.Type.Id, p.Build.Id) : default);
        Assert.Equal(("veth_m", "average"), bodies.Parse(CharacterSpec.DefaultBodyPreset) is { } d ? (d.Type.Id, d.Build.Id) : default);
        Assert.Null(bodies.Parse("body.veth_x.lean"));
        Assert.Null(bodies.Parse("body.veth_f"));
        Assert.Null(bodies.Parse("people.veth"));
        // An older save's preset this build does not offer is drawn as the default, so it still loads and plays.
        Assert.Equal("veth_m", bodies.Resolve("body.build_slender")!.Value.Type.Id);
        Assert.Equal(1.15f, bodies.Builds.Single(b => b.Id == "heavy").Girth);   // a build's girth is bounded (PJ04 §5.2)
        Assert.Equal("body.veth_f.heavy", PlayerBodies.Id(bodies.Types[1], bodies.Builds[2]));
    }

    [Fact]
    public void ABodysModel_IsTheFirstOfItsFamilysModelsThatIsInstalled()
    {
        var female = Read(Bindings).Types[1];

        Assert.Equal("pc_veth_f", PlayerBodies.Model(female, "production", m => m == "pc_veth_f"));
        Assert.Equal("pc_veth_f_new", PlayerBodies.Model(female, "production", _ => true));
        Assert.Null(PlayerBodies.Model(female, "production", _ => false));
        Assert.Equal("mh_veth_f", PlayerBodies.Model(female, PlayerBodies.Family("metahuman_m"), _ => true));
        Assert.Equal("production", PlayerBodies.Family("production"));
        Assert.Equal("production", PlayerBodies.Family(null));
    }

    [Fact]
    public void NoBodiesBound_MeansTheDomainsDefault()
    {
        var none = Read("""{ "types": [ { "id": "veth_m" } ] }""");

        Assert.True(none.IsEmpty);
        Assert.Equal(CharacterSpec.DefaultBodyPreset, none.Default);
        Assert.Null(none.Resolve("body.veth_m.average"));
    }

    [Fact]
    public void TheShippedBindings_OfferTwoTypesAndThreeBuilds_EachAPresetTheDomainAccepts()
    {
        string path = Path.Combine(Root(), "src", "Presentation", "Art", "art_bindings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var bodies = PlayerBodies.Read(document.RootElement.GetProperty("player_bodies"));

        Assert.Equal(new[] { "veth_m", "veth_f" }, bodies.Types.Select(t => t.Id));
        Assert.Equal(new[] { "lean", "average", "heavy" }, bodies.Builds.Select(b => b.Id));
        foreach (var type in bodies.Types)
        {
            Assert.Contains("production", type.Models.Keys);
            foreach (var build in bodies.Builds)
                Assert.Equal(PlayerBodies.Id(type, build), (Draft("Wenna Lind") with { BodyPresetId = PlayerBodies.Id(type, build) }).Spec().BodyPresetId);
        }
        Assert.NotNull(bodies.Parse(bodies.Default));
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
