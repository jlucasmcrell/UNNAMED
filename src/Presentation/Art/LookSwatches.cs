// UNNAMED Presentation - the creator's skin tones and hair colours by people, over each body as it was baked (owner, 2026-10-25)
// Godot-free: plain values, so the tests read the same tables the game draws with

namespace UNNAMED.Presentation.Art;

/// <summary>One choice: its name, and three numbers - a multiplier for a skin tone, an added colour for hair (sRGB, 0 to 1 and over).</summary>
public sealed record Swatch(string Label, float R, float G, float B);

/// <summary>
/// The tones a body can take without baking it again (RACE_REFERENCE_REVIEW_20261025.md): a skin tone scales the baked skin's colour, a hair
/// colour is added over the baked cards (they are near-black, so no multiplier could reach blond). The first of each list is the body as it
/// was baked. The choice is kept in the appearance's sliders, as an index into the people's list, so a save needs no new field.
/// </summary>
public static class LookSwatches
{
    public const string SkinSlider = "look.skin_tone", HairSlider = "look.hair_colour", StyleSlider = "look.hair_style";

    /// <summary>
    /// The hairstyles a body can wear: its own baked hair first, then HumGen hair baked on the same rig and shipped as rigged models
    /// (<c>rigged/&lt;asset&gt;/&lt;asset&gt;_rigged.glb</c>). A style whose model is not installed is not offered.
    /// </summary>
    public static IReadOnlyList<(string Label, string? Asset)> HairStyles(string? bodyModel) => bodyModel switch
    {
        "humgen_player_ada_f" => new (string, string?)[] { ("Ponytail", null), ("Bun", "humgen_hair_f_bun"), ("Long bob", "humgen_hair_f_bob_long") },
        _ => Array.Empty<(string, string?)>(),
    };

    private static readonly Swatch AsMade = new("As made", 1, 1, 1), Unchanged = new("As made", 0, 0, 0);

    private static readonly Swatch[] HumanSkin =
    {
        AsMade, new("Fair", 2.1f, 2f, 1.95f), new("Light", 1.8f, 1.7f, 1.62f), new("Light warm", 1.5f, 1.42f, 1.32f),
        new("Medium", 1.25f, 1.16f, 1.05f), new("Olive", 1.12f, 1.08f, .88f), new("Tan", .95f, .85f, .72f), new("Brown", .75f, .62f, .52f),
        new("Dark brown", .56f, .45f, .38f), new("Deep", .42f, .33f, .28f),
    };

    private static readonly Swatch[] KalSkin =
    {
        AsMade, new("Slate", .9f, .95f, 1.05f), new("Basalt", .6f, .6f, .65f), new("Sandstone", 1.35f, 1.15f, .85f),
        new("Granite", 1.1f, 1.08f, 1.05f), new("Ochre", 1.2f, .95f, .7f),
    };

    private static readonly Swatch[] SiannSkin =
    {
        AsMade, new("Pearl", 2f, 1.95f, 2.05f), new("Lavender", 1.85f, 1.75f, 2.05f), new("Rose", 1.95f, 1.75f, 1.85f), new("Sea glass", 1.75f, 1.95f, 1.95f),
    };

    private static readonly Swatch[] NaturalHair =
    {
        Unchanged, new("Dark brown", .12f, .07f, .04f), new("Chestnut", .26f, .13f, .06f), new("Auburn", .34f, .11f, .05f),
        new("Copper", .48f, .17f, .06f), new("Light brown", .32f, .22f, .13f), new("Dark blond", .45f, .34f, .2f), new("Blond", .62f, .5f, .3f),
        new("Grey", .42f, .42f, .42f), new("White", .75f, .74f, .72f),
    };

    // Siann hair runs from platinum to pure white with age, and nothing else.
    private static readonly Swatch[] SiannHair =
    {
        new("Platinum", .7f, .68f, .62f), new("Silver", .72f, .72f, .74f), new("White", .86f, .86f, .86f),
    };

    public static IReadOnlyList<Swatch> Skin(string? people) => people switch
    {
        "people.kal" => KalSkin,
        "people.siann" => SiannSkin,
        _ => HumanSkin,
    };

    public static IReadOnlyList<Swatch> Hair(string? people) => people == "people.siann" ? SiannHair : NaturalHair;

    /// <summary>
    /// The swatch an index names (none chosen is the first), or null where that is the body as baked or the list has no such index. A list
    /// without "As made" (Siann hair) therefore always recolours.
    /// </summary>
    public static Swatch? Chosen(IReadOnlyList<Swatch> swatches, int? index) =>
        (index ?? 0) is var i && i >= 0 && i < swatches.Count && swatches[i] != AsMade && swatches[i] != Unchanged ? swatches[i] : null;
}
