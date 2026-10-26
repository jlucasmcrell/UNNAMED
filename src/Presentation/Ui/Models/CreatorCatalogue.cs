// UNNAMED Presentation - what the HumGen pipeline can make, as the creator offers it (res://Art/humgen_creator.json, written by
// tools/creator/humgen_catalogue.py): the races and their statures, the shaping sliders, the faces, hair, outfits and footwear, and the
// player bodies already baked. Godot-free, so the tests read the shipped file as the game does.

using System.Collections.Immutable;
using System.Text.Json;
using UNNAMED.Domain.Character;

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>A people as the creator shows it: its stature range (RACES.md), build and gear fit family, and whether HumGen makes its body as shipped.</summary>
public sealed record CreatorRace(string People, string Label, string Epithet, int MinStatureMm, int MaxStatureMm, string Build, string FitFamily, bool HumgenBody)
{
    public int MidStatureMm => (MinStatureMm + MaxStatureMm) / 2 / 10 * 10;
}

/// <summary>A shaping slider: its panel and group, the HumGen key it drives, its range in stored units and the HumGen value per unit.</summary>
public sealed record CreatorSlider(string Id, string Label, string Panel, string Group, string Humgen, int Min, int Max, int Default, double Scale);

/// <summary>One choice (a face, a hairstyle, an outfit, ...): the HumGen preset path for each body sex that has it, and for a face, the sliders it sets.</summary>
public sealed record CreatorOption(string Id, string Label, string Group, ImmutableDictionary<string, string> Paths, ImmutableDictionary<string, int> Values)
{
    public bool For(string sex) => Paths.ContainsKey(sex);
}

/// <summary>A player body that exists as a GLB with packaged motion, and the look it was baked with.</summary>
/// <summary>A baked body and its look; <paramref name="FaceOf"/> names the body it is another face of (the same type, its own head and hair).</summary>
public sealed record BakedBody(string Model, string Sex, CharacterAppearance Look, string? FaceOf = null);

public enum CreatorChoice { Face, Hair, FacialHair, Outfit, Footwear }

public sealed class CreatorCatalogue
{
    public static readonly CreatorCatalogue Empty = new(ImmutableArray<CreatorRace>.Empty, ImmutableArray<CreatorSlider>.Empty,
        ImmutableDictionary<CreatorChoice, ImmutableArray<CreatorOption>>.Empty, ImmutableArray<BakedBody>.Empty);

    private readonly ImmutableDictionary<CreatorChoice, ImmutableArray<CreatorOption>> _options;

    private CreatorCatalogue(ImmutableArray<CreatorRace> races, ImmutableArray<CreatorSlider> sliders,
        ImmutableDictionary<CreatorChoice, ImmutableArray<CreatorOption>> options, ImmutableArray<BakedBody> baked)
    {
        Races = races;
        Sliders = sliders;
        _options = options;
        Baked = baked;
    }

    public ImmutableArray<CreatorRace> Races { get; }
    public ImmutableArray<CreatorSlider> Sliders { get; }
    public ImmutableArray<BakedBody> Baked { get; }

    public static CreatorCatalogue Read(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        string S(JsonElement e, string key) => e.GetProperty(key).GetString()!;
        string? N(JsonElement e, string key) => e.GetProperty(key).ValueKind == JsonValueKind.Null ? null : e.GetProperty(key).GetString();
        var races = root.GetProperty("races").EnumerateArray().Select(r =>
        {
            var stature = r.GetProperty("stature_cm");
            return new CreatorRace(S(r, "people"), S(r, "label"), S(r, "epithet"), stature[0].GetInt32() * 10, stature[1].GetInt32() * 10,
                S(r, "build"), S(r, "fit_family"), r.GetProperty("humgen_body").GetBoolean());
        }).ToImmutableArray();
        var sliders = root.GetProperty("sliders").EnumerateArray().Select(s => new CreatorSlider(S(s, "id"), S(s, "label"), S(s, "panel"), S(s, "group"),
            S(s, "humgen"), s.GetProperty("min").GetInt32(), s.GetProperty("max").GetInt32(), s.GetProperty("default").GetInt32(), s.GetProperty("scale").GetDouble()))
            .ToImmutableArray();
        ImmutableArray<CreatorOption> Options(string key) => root.GetProperty(key).EnumerateArray().Select(o => new CreatorOption(S(o, "id"), S(o, "label"), S(o, "group"),
            o.GetProperty("paths").EnumerateObject().ToImmutableDictionary(p => p.Name, p => p.Value.GetString()!),
            o.TryGetProperty("values", out var values) ? values.EnumerateObject().ToImmutableDictionary(p => p.Name, p => p.Value.GetInt32()) : ImmutableDictionary<string, int>.Empty))
            .ToImmutableArray();
        var options = new Dictionary<CreatorChoice, ImmutableArray<CreatorOption>>
        {
            [CreatorChoice.Face] = Options("faces"),
            [CreatorChoice.Hair] = Options("hair"),
            [CreatorChoice.FacialHair] = Options("facial_hair"),
            [CreatorChoice.Outfit] = Options("outfits"),
            [CreatorChoice.Footwear] = Options("footwear"),
        }.ToImmutableDictionary();
        var baked = root.GetProperty("baked").EnumerateArray().Select(b => new BakedBody(S(b, "model"), S(b, "sex"),
            new CharacterAppearance(b.GetProperty("stature_cm").GetInt32() * 10, N(b, "face"), N(b, "hair"), N(b, "facial_hair"), N(b, "outfit"), N(b, "footwear"))
            {
                Sliders = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal).Add("face.age", b.GetProperty("age").GetInt32()),
            }, b.TryGetProperty("face_of", out var faceOf) ? faceOf.GetString() : null)).ToImmutableArray();
        var catalogue = new CreatorCatalogue(races, sliders, options, baked);
        foreach (var body in baked)
        {
            if (body.Look.Problem() is { } problem)
                throw new FormatException($"baked body {body.Model}: {problem}");
            // A face body's own head, hair and beard are baked with it, not catalogue pieces.
            foreach (var (choice, id) in Chosen(body.Look).Where(c => body.FaceOf is null || c.Choice is CreatorChoice.Outfit or CreatorChoice.Footwear))
                if (catalogue.Option(choice, id) is not { } option || !option.For(body.Sex))
                    throw new FormatException($"baked body {body.Model} names {id}, which the catalogue does not offer a '{body.Sex}' body");
        }
        return catalogue;
    }

    /// <summary>The choices of a kind a body of this sex can wear, in catalogue order.</summary>
    public IReadOnlyList<CreatorOption> Options(CreatorChoice choice, string sex) =>
        _options.TryGetValue(choice, out var all) ? all.Where(o => o.For(sex)).ToList() : Array.Empty<CreatorOption>();

    public CreatorOption? Option(CreatorChoice choice, string? id) =>
        id is not null && _options.TryGetValue(choice, out var all) ? all.FirstOrDefault(o => o.Id == id) : null;

    public CreatorRace? Race(string people) => Races.FirstOrDefault(r => r.People == people);

    public BakedBody? BakedFor(string? model) => Baked.FirstOrDefault(b => b.Model == model);

    /// <summary>The faces a body type's model offers: the model as baked, then each body baked as another face of it.</summary>
    public IReadOnlyList<BakedBody> Faces(string? model)
    {
        string? root = BakedFor(model)?.FaceOf ?? model;
        return BakedFor(root) is { } own ? Baked.Where(b => b.FaceOf == root).Prepend(own).ToList() : Array.Empty<BakedBody>();
    }

    /// <summary>The model drawn for a body type's <paramref name="model"/> and a chosen face: the face's own body when it has one.</summary>
    public string? DrawnModel(string? model, string? face) =>
        face is not null && Faces(model).FirstOrDefault(b => b.Look.Face == face) is { } chosen ? chosen.Model : model;

    /// <summary>The look a draft starts from: the baked body's own, or for a body not baked, the race's middle stature and every preset's own choice.</summary>
    public CharacterAppearance Start(string? model, string people) =>
        BakedFor(model)?.Look ?? new CharacterAppearance(Race(people)?.MidStatureMm ?? 1750, null, null, null, null, null);

    /// <summary>A slider's value in a look: moved, else as the look's face preset sets it, else its default.</summary>
    public int Value(CharacterAppearance look, CreatorSlider slider) =>
        look.Sliders.TryGetValue(slider.Id, out int moved) ? moved
        : Option(CreatorChoice.Face, look.Face)?.Values.TryGetValue(slider.Id, out int preset) == true ? preset
        : slider.Default;

    /// <summary>A look with a face preset: as HumGen loads one, its own face, skin and age replace any the look had moved; body and hair stay.</summary>
    public CharacterAppearance WithFace(CharacterAppearance look, string face)
    {
        var facePanel = Sliders.Where(s => s.Panel == "face").Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        return look with { Face = face, Sliders = look.Sliders.RemoveRange(look.Sliders.Keys.Where(facePanel.Contains)) };
    }

    /// <summary>A look with one slider at a value (kept in the slider's range).</summary>
    public CharacterAppearance WithSlider(CharacterAppearance look, CreatorSlider slider, int value) =>
        look with { Sliders = look.Sliders.SetItem(slider.Id, Math.Clamp(value, slider.Min, slider.Max)) };

    /// <summary>
    /// What of <paramref name="look"/> no baked body has yet, in the creator's words ("Height", "Face", "Body shape", "Hair", "Outfit",
    /// "Footwear"): everything the look changes from the body baked as <paramref name="model"/>, or the HumGen body itself when this run
    /// draws no baked one. Empty only when the preview is exactly the character.
    /// </summary>
    public IReadOnlyList<string> Pending(CharacterAppearance? look, string? model, string people)
    {
        var pending = new List<string>();
        if (Race(people) is { HumgenBody: false } race)
            pending.Add($"{race.Label} body");
        if (BakedFor(model)?.Look is not { } baked)
        {
            pending.Add("HumGen body");
            return pending;
        }
        if (look is null)
            return pending;
        bool Moved(string panel, Func<CreatorSlider, bool>? only = null) =>
            Sliders.Where(s => s.Panel == panel && (only?.Invoke(s) ?? true)).Any(s => Value(look, s) != Value(baked, s));
        if (look.StatureMm != baked.StatureMm)
            pending.Add("Height");
        if (look.Face != baked.Face || Moved("face"))
            pending.Add("Face");
        if (Moved("body"))
            pending.Add("Body shape");
        if (look.Hair != baked.Hair || look.FacialHair != baked.FacialHair || Moved("hair"))
            pending.Add("Hair");
        if (look.Outfit != baked.Outfit)
            pending.Add("Outfit");
        if (look.Footwear != baked.Footwear)
            pending.Add("Footwear");
        return pending;
    }

    /// <summary>Whether choosing this option keeps the draft on its baked body (the option the body was baked with).</summary>
    public bool IsBaked(CreatorChoice choice, string? id, string? model) =>
        BakedFor(model)?.Look is { } baked && Chosen(baked).Any(c => c.Choice == choice && c.Id == id)
        || (choice == CreatorChoice.FacialHair && id is null && BakedFor(model) is { Look.FacialHair: null });

    private static IEnumerable<(CreatorChoice Choice, string Id)> Chosen(CharacterAppearance look) =>
        new[] { (CreatorChoice.Face, look.Face), (CreatorChoice.Hair, look.Hair), (CreatorChoice.FacialHair, look.FacialHair),
            (CreatorChoice.Outfit, look.Outfit), (CreatorChoice.Footwear, look.Footwear) }
            .Where(c => c.Item2 is not null).Select(c => (c.Item1, c.Item2!));
}
