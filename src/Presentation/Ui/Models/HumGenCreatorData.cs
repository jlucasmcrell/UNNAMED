using System.Collections.Immutable;
using System.Text.Json;
using UNNAMED.Domain.Character;
using UNNAMED.World;

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>Native presets and people-safe controls, shared by the creator and seeded residents.</summary>
public sealed class HumGenCreatorData
{
    public sealed record Range(int Min, int Max, int Default);
    private readonly JsonElement _data;
    private readonly ImmutableDictionary<string, JsonElement> _faces;
    public HumGenCreatorData(string json)
    {
        using var document = JsonDocument.Parse(json);
        _data = document.RootElement.Clone();
        _faces = _data.GetProperty("faces").EnumerateArray().Where(f => f.TryGetProperty("native", out _))
            .ToImmutableDictionary(f => f.GetProperty("id").GetString()!, f => f);
    }

    public bool Available => _data.TryGetProperty("runtime", out _);
    public bool HasFace(string? face) => face is not null && _faces.ContainsKey(face);
    public string Base(string sex) => _data.GetProperty("runtime").GetProperty("bases").GetProperty(sex).GetString()!;
    public JsonElement Preset(string face) => _faces.TryGetValue(face, out var preset) ? preset.GetProperty("native")
        : throw new ArgumentException("Unknown native HumGen preset: " + face);
    public string FirstFace(string sex) => _data.GetProperty("faces").EnumerateArray().First(f => f.GetProperty("paths").TryGetProperty(sex, out _)
        && f.GetProperty("label").GetString() == "Caucasian 2").GetProperty("id").GetString()!;
    public string? Key(string name) => _data.GetProperty("runtime").GetProperty("keys").TryGetProperty(name, out var key) ? key.GetString() : null;
    public Range? Allowed(string people, CreatorSlider slider)
    {
        if (!Available || !_data.GetProperty("runtime").GetProperty("people").TryGetProperty(people, out var allowed)
            || !allowed.TryGetProperty(slider.Id, out var value)) return null;
        return new(value.GetProperty("min").GetInt32(), value.GetProperty("max").GetInt32(), value.GetProperty("default").GetInt32());
    }

    public IReadOnlyList<CreatorSlider> Sliders(CreatorCatalogue catalogue, string people, string panel) => catalogue.Sliders
        .Where(s => s.Panel == panel && Allowed(people, s) is not null).Select(s =>
        {
            var range = Allowed(people, s)!;
            return s with { Min = range.Min, Max = range.Max, Default = range.Default };
        }).ToArray();

    public int Value(CharacterAppearance look, CreatorCatalogue catalogue, string people, CreatorSlider slider) =>
        look.Sliders.TryGetValue(slider.Id, out int moved) ? Math.Clamp(moved, slider.Min, slider.Max)
        : people is "people.siann" or "people.kal" && Key(slider.Humgen) is not null ? slider.Default
        : catalogue.Value(look, slider);

    public CharacterAppearance Reset(CharacterAppearance look, CreatorCatalogue catalogue, string people, string panel, string group)
    {
        var values = look.Sliders;
        foreach (var slider in Sliders(catalogue, people, panel).Where(s => s.Group == group))
            values = values.Remove(slider.Id);
        return look with { Sliders = values };
    }

    public CharacterAppearance Randomize(CharacterAppearance look, CreatorCatalogue catalogue, string people, string sex, ulong seed)
    {
        int Pick(string key, int min, int max) => RngChannel.Open(seed, default, "character.appearance", people + "/" + key).Int(0, min, max);
        var faces = catalogue.Options(CreatorChoice.Face, sex);
        look = catalogue.WithFace(look, faces[Pick("preset", 0, faces.Count)].Id);
        var values = look.Sliders;
        foreach (var panel in new[] { "face", "body", "hair" })
        foreach (var slider in Sliders(catalogue, people, panel))
        {
            int centre = Value(look, catalogue, people, slider);
            int radius = Math.Max(1, (slider.Max - slider.Min) / 12);
            values = values.SetItem(slider.Id, Pick(slider.Id, Math.Max(slider.Min, centre - radius), Math.Min(slider.Max, centre + radius) + 1));
        }
        var race = catalogue.Race(people)!;
        return look with { StatureMm = Pick("stature", race.MinStatureMm / 10, race.MaxStatureMm / 10 + 1) * 10,
            FacialHair = people == "people.siann" ? null : look.FacialHair, Sliders = values };
    }

    public IReadOnlyDictionary<string, float> Weights(CharacterAppearance look, CreatorCatalogue catalogue, string people, string sex)
    {
        var preset = Preset(look.Face ?? FirstFace(sex));
        var weights = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var property in preset.GetProperty("keys").EnumerateObject())
            if (Key(property.Name) is { } shape && shape.StartsWith("f_", StringComparison.Ordinal))
                weights[shape] = property.Value.GetSingle();
        foreach (var panel in new[] { "face", "body" })
        foreach (var slider in Sliders(catalogue, people, panel))
        {
            if (Key(slider.Humgen) is not { } shape) continue;
            var range = Allowed(people, slider)!;
            if (panel == "face" && people is not ("people.siann" or "people.kal") && !look.Sliders.ContainsKey(slider.Id)) continue;
            // Preset features remain native; people defaults constrain only their canonical morphology.
            int chosen = look.Sliders.TryGetValue(slider.Id, out int value) ? value
                : people is "people.siann" or "people.kal" ? range.Default : catalogue.Value(look, slider);
            weights[shape] = Math.Clamp(chosen, range.Min, range.Max) * (float)slider.Scale;
        }
        int age = look.Sliders.GetValueOrDefault("face.age", preset.GetProperty("age").GetProperty("set").GetInt32());
        foreach (var entry in _data.GetProperty("runtime").GetProperty("keys").EnumerateObject())
            if (entry.Value.GetString() is { } shape && shape.StartsWith("s_{age}_", StringComparison.Ordinal))
                weights[shape] = entry.Name == "aged_young" ? Math.Max(0, 3 - age * .1f) : Math.Max(0, (age - 40) / 30f);
        return weights;
    }

    public CharacterAppearance WithBodyPreset(CharacterAppearance look, CreatorCatalogue catalogue, string people, string sex, int index)
    {
        var presets = BodyPresets(sex);
        if (index < 0 || index >= presets.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var keys = presets[index].GetProperty("keys");
        var values = look.Sliders;
        foreach (var slider in Sliders(catalogue, people, "body"))
        {
            int value = keys.TryGetProperty(slider.Humgen, out var native) ? (int)Math.Round(native.GetDouble() / slider.Scale) : slider.Default;
            values = values.SetItem(slider.Id, Math.Clamp(value, slider.Min, slider.Max));
        }
        var race = catalogue.Race(people)!;
        int stature = (int)Math.Round(presets[index].GetProperty("height_cm").GetDouble() * 10);
        return look with { StatureMm = Math.Clamp(stature, race.MinStatureMm, race.MaxStatureMm), Sliders = values.SetItem("look.body_preset", index) };
    }

    public CharacterAppearance WithBuild(CharacterAppearance look, CreatorCatalogue catalogue, string people, string build)
    {
        var values = look.Sliders;
        foreach (var slider in Sliders(catalogue, people, "body"))
        {
            int value = slider.Default;
            if (build == "slim" && slider.Humgen == "skinny") value = 650;
            if (build == "heavy" && slider.Humgen == "overweight") value = 650;
            if (build is "athletic" or "strong" && slider.Humgen == "muscular") value = 650;
            values = values.SetItem(slider.Id, Math.Clamp(value, slider.Min, slider.Max));
        }
        return look with { Sliders = values.Remove("look.body_preset") };
    }

    public IReadOnlyList<JsonElement> BodyPresets(string sex) => _data.GetProperty("runtime").GetProperty("body_presets").EnumerateArray()
        .Where(p => p.GetProperty("sex").GetString() == sex).ToArray();
}
