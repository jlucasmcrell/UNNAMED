using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Ui;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

/// <summary>
/// The creator's HumGen look (humgen_creator.json): what it offers is what the HumGen content can make, the baked player bodies name only
/// options it offers, every change from a baked body is reported as pending bake, and the draft hands the look to the domain to keep.
/// </summary>
public class CreatorCatalogueTests
{
    private static readonly CreatorCatalogue Shipped = CreatorCatalogue.Read(File.ReadAllText(Path.Combine(Root(), "src", "Presentation", "Art", "humgen_creator.json")));

    private const string Ada = "humgen_player_ada_f";

    [Fact]
    public void TheShippedCatalogue_OffersWhatTheHumGenContentHas_PerSex()
    {
        Assert.Equal(36, Shipped.Options(CreatorChoice.Hair, "f").Count);
        Assert.Equal(34, Shipped.Options(CreatorChoice.Hair, "m").Count);
        Assert.Equal(9, Shipped.Options(CreatorChoice.FacialHair, "m").Count);
        Assert.Empty(Shipped.Options(CreatorChoice.FacialHair, "f"));
        Assert.All(new[] { CreatorChoice.Face, CreatorChoice.Outfit, CreatorChoice.Footwear },
            choice => Assert.All(new[] { "f", "m" }, sex => Assert.NotEmpty(Shipped.Options(choice, sex))));
        Assert.All(Shipped.Sliders, s => Assert.True(s.Min <= s.Default && s.Default <= s.Max, s.Id));
    }

    [Fact]
    public void TheRaces_AreTheEightPeoples_AtTheirCanonStatures()
    {
        Assert.Equal(8, Shipped.Races.Length);
        Assert.Equal((1400, 1550), (Shipped.Race("people.siann")!.MinStatureMm, Shipped.Race("people.siann")!.MaxStatureMm));
        Assert.Equal((2000, 2600), (Shipped.Race("people.ondrek")!.MinStatureMm, Shipped.Race("people.ondrek")!.MaxStatureMm));
        Assert.True(Shipped.Race("people.veth")!.HumgenBody);
        Assert.False(Shipped.Race("people.kal")!.HumgenBody);
    }

    [Fact]
    public void ABakedBody_IsPendingNothing_AndEveryChangeIsNamed()
    {
        var baked = Shipped.Start(Ada, "people.veth");
        Assert.Equal(Shipped.BakedFor(Ada)!.Look, baked);
        Assert.Empty(Shipped.Pending(null, Ada, "people.veth"));
        Assert.Empty(Shipped.Pending(baked, Ada, "people.veth"));

        var hair = Shipped.Options(CreatorChoice.Hair, "f").First(o => o.Id != baked.Hair).Id;
        var muscular = Shipped.Sliders.Single(s => s.Id == "body.muscular");
        var changed = Shipped.WithSlider(baked with { Hair = hair, StatureMm = 1800 }, muscular, 600);

        Assert.Equal(new[] { "Height", "Body shape", "Hair" }, Shipped.Pending(changed, Ada, "people.veth"));
        Assert.True(Shipped.IsBaked(CreatorChoice.Hair, baked.Hair, Ada));
        Assert.False(Shipped.IsBaked(CreatorChoice.Hair, hair, Ada));
        Assert.True(Shipped.IsBaked(CreatorChoice.FacialHair, null, Ada));
        Assert.Equal(new[] { "HumGen body" }, Shipped.Pending(null, "humgen_not_baked", "people.veth"));
        Assert.Contains("Kal body", Shipped.Pending(null, Ada, "people.kal"));
    }

    [Fact]
    public void AFace_SetsTheFaceSlidersAsHumGenLoadsIt_AndKeepsTheBody()
    {
        var baked = Shipped.Start(Ada, "people.veth");
        var nose = Shipped.Sliders.Single(s => s.Id == "face.nose_tip_size");
        var muscular = Shipped.Sliders.Single(s => s.Id == "body.muscular");
        var moved = Shipped.WithSlider(Shipped.WithSlider(baked, nose, 500), muscular, 300);
        var face = Shipped.Options(CreatorChoice.Face, "f").First(f => f.Values.ContainsKey(nose.Id));

        var withFace = Shipped.WithFace(moved, face.Id);

        Assert.Equal(face.Values[nose.Id], Shipped.Value(withFace, nose));
        Assert.Equal(300, Shipped.Value(withFace, muscular));
        Assert.Equal(nose.Max, Shipped.Value(Shipped.WithSlider(baked, nose, nose.Max + 1), nose));
    }

    [Fact]
    public void TheDraftsLook_ReachesTheSpec_AndABadOneIsRefused()
    {
        var look = Shipped.Start(Ada, "people.veth") with { StatureMm = 1720 };
        var draft = CreatorDraft.Fresh("body.veth_f.average", CreatorDraft.FirstCall, StartPoint.Road) with { Name = "Wenna Lind", Appearance = look };

        Assert.Equal(look, draft.Spec().Appearance);
        Assert.Equal(look, CharacterIdentity.Of(draft.Spec()).Appearance);
        Assert.Throws<ArgumentException>(() => (draft with { Appearance = look with { StatureMm = 400 } }).Spec());
        Assert.Throws<ArgumentException>(() => (draft with { Appearance = look with { Hair = "outfit.casual.smart_casual" } }).Spec());
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("The repository root (AGENTS.md) was not found");
    }
}
