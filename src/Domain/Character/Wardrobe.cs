using System.Collections.Immutable;

namespace UNNAMED.Domain.Character;

/// <summary>Cosmetic choices only. These never supply equipment, requirements, protection or heritage rights.</summary>
public sealed record WardrobeChoice(string Id, string Name, string Bodies, int DyeRegions = 0)
{
    public bool For(string bodyType) => Bodies.Contains(bodyType.EndsWith("_f", StringComparison.Ordinal) ? 'f' : 'm');
}

public sealed record ClothDye(string ItemId, int Shirt, int Trousers);

public sealed record WardrobeSetup(ImmutableArray<WardrobeChoice> Outfits, ImmutableArray<WardrobeChoice> Footwear,
    ImmutableArray<ClothDye> Dyes, ImmutableSortedDictionary<string, int> DefaultStatures)
{
    public static WardrobeSetup Empty { get; } = new([], [], [], ImmutableSortedDictionary<string, int>.Empty);
}

public static class WardrobeLook
{
    public const string OverGear = "look.wardrobe_over_gear", Shirt = "look.shirt_colour", Trousers = "look.trouser_colour";
    public static bool CoversGear(CharacterAppearance? look) => look?.Sliders.GetValueOrDefault(OverGear) == 1;
    // Pre-creator saves use the renderer's original masculine base rather than an authored body preset.
    public static string? BodyType(CharacterSetup setup, CharacterIdentity identity)
    {
        string? type = setup.Bodies.TryGetValue(identity.BodyPresetId, out var body) ? body.Type
            : identity.BodyPresetId == CharacterSpec.DefaultBodyPreset ? "veth_m" : null;
        if (type is null) return null;
        // Native face presets select the renderer's actual base, including on a legacy body.default save.
        return identity.Appearance?.Face is { } face && face.StartsWith("face.f.", StringComparison.Ordinal) ? "veth_f"
            : identity.Appearance?.Face is { } male && male.StartsWith("face.m.", StringComparison.Ordinal) ? "veth_m" : type;
    }
}
