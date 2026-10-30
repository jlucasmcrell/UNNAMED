using UNNAMED.Domain.Social;

namespace UNNAMED.Content.Tests;

/// <summary>
/// An NPC's <c>appearance</c> block (F1 Gate C): the look a named cast member is drawn with, read beside the people that constrains it.
/// Content can check the record's shape only - the people's own ranges and what the catalogue offers a sex live in presentation - so the
/// five refusals here are the ones a loader can see.
/// </summary>
public class NpcLooksContentTests
{
    private const string Tam = "npc.cairnwold.tam_crowden";

    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    /// <summary>A copy of the game's content with one NPC file written fresh, in a directory removed afterwards.</summary>
    private sealed class RewrittenNpc : IDisposable
    {
        public RewrittenNpc(string relativePath, string yaml)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            File.WriteAllText(Path.Combine(Root, relativePath), yaml);
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-npclooks-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    /// <summary>Tam's own file, dressed as the case at hand dresses her: the block is written under the id and role the rest of content names.</summary>
    private static string TamDressed(string? people, string appearance) => $"""
        id: npc.cairnwold.tam_crowden
        kind: npc
        sex: f
        schema: 1
        display_key: npc.cairnwold.tam_crowden.name
        tags: [npc]
        name: Tam Crowden
        notes: A woman who holds the staddle granary at the quiet west end of the village.
        role: villager
        dialogue_ref: dialogue.cairnwold.tam_crowden
        unique: true
        {(people is null ? "" : people)}
        {appearance}
        """;

    private const string Dressed = """
        appearance:
          stature_cm: 166
          face: face.f.caucasian_presets.caucasian_2
          hair: hair.short.short_side_part
          outfit: outfit.casual.smart_casual
          footwear: footwear.outdoor.garden_boot
          sliders:
            face.age: 28
            body.skinny: 500
            look.hair_style: 1
        """;

    private static void AssertRefused(string? people, string appearance, string mentions)
    {
        using var content = new RewrittenNpc("npcs/cairnwold/tam_crowden.yaml", TamDressed(people, appearance));
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "SOC001" && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    /// <summary>The record reads as the catalogue's own units: whole centimetres in the content, millimetres in the domain, every slider kept.</summary>
    [Fact]
    public void AnAppearance_ReadsAsTheCatalogueUnits()
    {
        using var content = new RewrittenNpc("npcs/cairnwold/tam_crowden.yaml", TamDressed("people_ref: people.veth", Dressed));
        var tam = SocialContent.BuildNpcs(Load(content.Root))[Tam].Appearance!;

        Assert.Equal(1660, tam.StatureMm);
        Assert.Equal(("face.f.caucasian_presets.caucasian_2", "hair.short.short_side_part", null, "outfit.casual.smart_casual", "footwear.outdoor.garden_boot"),
            (tam.Face, tam.Hair, tam.FacialHair, tam.Outfit, tam.Footwear));
        Assert.Equal(new[] { "body.skinny", "face.age", "look.hair_style" }, tam.Sliders.Keys);
        Assert.Equal(new[] { 500, 28, 1 }, tam.Sliders.Values);
    }

    /// <summary>A person not yet dressed is legal: the record is a field of a file, not a requirement of one.</summary>
    [Fact]
    public void AnNpcWithoutARecord_StillLoads()
    {
        using var content = new RewrittenNpc("npcs/cairnwold/tam_crowden.yaml", TamDressed("people_ref: people.veth", ""));
        var tam = SocialContent.BuildNpcs(Load(content.Root))[Tam];

        Assert.Null(tam.Appearance);
        Assert.Equal("people.veth", tam.PeopleId);
    }

    /// <summary>A record is constrained by its people's ranges, so it cannot stand without one.</summary>
    [Fact]
    public void AnAppearanceWithoutAPeoples_IsRefused() => AssertRefused(null, Dressed, "constrained by its people, so it names people_ref");

    /// <summary>An unknown key would be read by nothing: it is a typo, not a look.</summary>
    [Fact]
    public void AnAppearanceWithAnUnknownKey_IsRefused() =>
        AssertRefused("people_ref: people.veth", Dressed + "\n  eyes: eyes.blue", "'eyes' is not one of stature_cm, face, hair, facial_hair, outfit, footwear, sliders");

    /// <summary>Every ID the record names carries the prefix its field draws.</summary>
    [Fact]
    public void AnAppearanceWithAnIdOfTheWrongKind_IsRefused() =>
        AssertRefused("people_ref: people.veth", Dressed.Replace("hair.short.short_side_part", "hair_style.short"), "is not a hair ID");

    /// <summary>The stature is the whole character range's, so a record cannot scale a body past it.</summary>
    [Fact]
    public void AnAppearanceWithAStatureOutsideTheBand_IsRefused() =>
        AssertRefused("people_ref: people.veth", Dressed.Replace("stature_cm: 166", "stature_cm: 400"), "outside 1000-3000 mm");

    /// <summary>A slider's value is a whole number in the catalogue's units; anything else would be clamped or dropped unseen.</summary>
    [Fact]
    public void AnAppearanceWhoseSliderIsNotAWholeNumber_IsRefused() =>
        AssertRefused("people_ref: people.veth", Dressed.Replace("body.skinny: 500", "body.skinny: wide"), "slider body.skinny is a whole number");
}
