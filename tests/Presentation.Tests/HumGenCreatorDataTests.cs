using UNNAMED.Domain.Character;
using UNNAMED.Presentation.Ui.Models;

namespace UNNAMED.Presentation.Tests;

public class HumGenCreatorDataTests
{
    [Fact]
    public void Native_presets_keep_their_keys_and_people_randomization_respects_each_group()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Presentation"))) directory = directory.Parent;
        string json = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Presentation", "Art", "humgen_creator.json"));
        var catalogue = CreatorCatalogue.Read(json);
        var native = new HumGenCreatorData(json);
        foreach (string key in new[] { "Eye Depth", "Eye Distance", "Eye Height", "Eye Scale" })
            Assert.StartsWith("f_", native.Key(key));
        Assert.Equal(83, catalogue.Options(CreatorChoice.Face, "f").Count + catalogue.Options(CreatorChoice.Face, "m").Count);
        foreach (string sex in new[] { "f", "m" })
        foreach (var face in catalogue.Options(CreatorChoice.Face, sex))
        {
            var look = new CharacterAppearance(1750, face.Id, null, null, null, null);
            var weights = native.Weights(look, catalogue, "people.veth", sex);
            foreach (var key in native.Preset(face.Id).GetProperty("keys").EnumerateObject())
                if (native.Key(key.Name) is { } shape && shape.StartsWith("f_")) Assert.Equal(key.Value.GetSingle(), weights[shape]);
        }
        foreach (string people in new[] { "people.veth", "people.orenth", "people.kal", "people.siann" })
        {
            var start = new CharacterAppearance(1750, native.FirstFace("f"), null, null, null, null);
            var rolled = native.Randomize(start, catalogue, people, "f", 1234);
            Assert.Equal(rolled, native.Randomize(start, catalogue, people, "f", 1234));
            foreach (string panel in new[] { "face", "body", "hair" })
            foreach (var slider in native.Sliders(catalogue, people, panel))
                Assert.InRange(rolled.Sliders[slider.Id], slider.Min, slider.Max);
            Assert.DoesNotContain(native.Sliders(catalogue, people, "body"), s => s.Humgen.Contains("Length"));
            var reset = native.Reset(rolled, catalogue, people, "face", "Nose");
            Assert.DoesNotContain(reset.Sliders.Keys, key => key.StartsWith("face.nose_"));
            Assert.Equal(rolled.StatureMm, reset.StatureMm);
            Assert.NotEqual(rolled, native.Randomize(start, catalogue, people, "f", 5678));
            var body = native.WithBodyPreset(rolled, catalogue, people, "f", 0);
            Assert.Equal(rolled.Face, body.Face);
            Assert.InRange(body.StatureMm, catalogue.Race(people)!.MinStatureMm, catalogue.Race(people)!.MaxStatureMm);
            Assert.Equal(rolled.Face, native.WithBuild(rolled, catalogue, people, "slim").Face);
        }
    }
}
