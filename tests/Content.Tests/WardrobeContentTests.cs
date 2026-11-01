using System.Text.Json;
using UNNAMED.Domain.Character;

namespace UNNAMED.Content.Tests;

public class WardrobeContentTests
{
    [Fact]
    public void ShippedWardrobeMatchesTheOwnedCreatorCatalogue_ForBothBodiesAndLegacyStatures()
    {
        var loader = new ContentLoader();
        Assert.True(loader.LoadAll(Path.Combine(RepoPaths.Root(), "content")), string.Join('\n', loader.Errors));
        var wardrobe = WardrobeContent.Build(loader);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "src", "Presentation", "Art", "humgen_creator.json")));
        foreach (var (key, choices) in new[] { ("outfits", wardrobe.Outfits), ("footwear", wardrobe.Footwear) })
        {
            var owned = document.RootElement.GetProperty(key).EnumerateArray().ToArray();
            Assert.Equal(owned.Select(o => o.GetProperty("id").GetString()).OrderBy(id => id), choices.Select(c => c.Id).OrderBy(id => id));
            foreach (var option in owned)
            {
                var choice = Assert.Single(choices, c => c.Id == option.GetProperty("id").GetString());
                Assert.Equal(option.GetProperty("label").GetString(), choice.Name);
                Assert.Equal(option.GetProperty("paths").TryGetProperty("m", out _), choice.For("veth_m"));
                Assert.Equal(option.GetProperty("paths").TryGetProperty("f", out _), choice.For("veth_f"));
                Assert.InRange(choice.DyeRegions, 0, 3);
            }
        }
        foreach (var race in document.RootElement.GetProperty("races").EnumerateArray())
        {
            var range = race.GetProperty("stature_cm").EnumerateArray().Select(n => n.GetInt32() * 10).ToArray();
            Assert.Equal((range[0] + range[1]) / 2 / 10 * 10, wardrobe.DefaultStatures[race.GetProperty("people").GetString()!]);
        }
        Assert.Equal(0, wardrobe.Outfits.Single(c => c.Id == "outfit.extra_outfits_pack.pirate").DyeRegions);
        Assert.Equal(3, wardrobe.Outfits.Single(c => c.Id == "outfit.office.new_intern").DyeRegions);
        Assert.Equal(4, wardrobe.Dyes.Length);
    }
}
