using System.Text.Json;

namespace UNNAMED.Content.Tests;

public class StepsPeopleLooksTests
{
    [Fact]
    public void EveryStepsPersonHasAnIndividualNativeLookAndWearablePeriodClothes()
    {
        string root = RepoPaths.Root();
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(root, "content"));
        var people = SocialContent.BuildNpcs(loader).Values.Where(n => n.Id.StartsWith("npc.otherhome.")).ToArray();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "Presentation", "Art", "humgen_creator.json")));
        var catalogue = json.RootElement;

        Assert.Equal(27, people.Length);
        Assert.Equal(16, people.Count(n => n.Id.Contains(".mkt_")));
        Assert.Equal(27, people.Select(n => n.Appearance?.Face).Distinct().Count());
        Assert.Equal(4, people.Select(n => n.PeopleId).Distinct().Count());
        foreach (var person in people)
        {
            var look = Assert.IsType<UNNAMED.Domain.Character.CharacterAppearance>(person.Appearance);
            Assert.StartsWith("outfit.period.", look.Outfit);
            foreach (var (list, id) in new[] { ("faces", look.Face), ("hair", look.Hair), ("facial_hair", look.FacialHair),
                         ("outfits", look.Outfit), ("footwear", look.Footwear) })
                if (id is not null)
                    Assert.Contains(catalogue.GetProperty(list).EnumerateArray(), option =>
                        option.GetProperty("id").GetString() == id && option.GetProperty("paths").TryGetProperty(person.Sex!, out _));

            var allowed = catalogue.GetProperty("runtime").GetProperty("people").GetProperty(person.PeopleId!);
            foreach (var (id, value) in look.Sliders.Where(s => !s.Key.StartsWith("look.")))
            {
                var range = allowed.GetProperty(id);
                Assert.InRange(value, range.GetProperty("min").GetInt32(), range.GetProperty("max").GetInt32());
            }
            if (person.PeopleId == "people.kal") { Assert.Null(look.Hair); Assert.Null(look.FacialHair); }
            if (person.PeopleId == "people.siann") { Assert.NotNull(look.Hair); Assert.Null(look.FacialHair); }
            if (person.Id == "npc.otherhome.row_child") Assert.True(look.StatureMm < 1600);
            else
            {
                var race = catalogue.GetProperty("races").EnumerateArray().Single(r => r.GetProperty("people").GetString() == person.PeopleId);
                Assert.InRange(look.StatureMm, race.GetProperty("stature_cm")[0].GetInt32() * 10,
                    race.GetProperty("stature_cm")[1].GetInt32() * 10);
            }
        }
    }
}
