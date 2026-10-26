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
    public const string SkinSlider = "look.skin_tone", HairSlider = "look.hair_colour", StyleSlider = "look.hair_style",
        ShirtSlider = "look.shirt_colour", TrouserSlider = "look.trouser_colour", StoneSlider = "look.kal_stone";

    /// <summary>
    /// How much of a Kal's skin has turned to stone (owner, 2026-10-26), as the creator offers it: the skin shader's stone_coverage, on the
    /// graded mask's scale (race-looks: full needs 1.2 to clear the mask's noisy floor). None chosen is full stone; the choice takes the
    /// place of the coverage in the body's kal_stone.json.
    /// </summary>
    // Patched (.62) is left out until playtest 2: on the graded mask it is a smudge on the nose that reads as a hurt (coordinator, 2026-10-26).
    public static readonly (string Label, float Coverage)[] KalStone = { ("Mostly stone", .7f), ("Full stone", 1.2f) };

    /// <summary>The stone coverage an index names, full stone when none is chosen or the index is not in the list.</summary>
    public static float StoneCoverage(int? index) => index is int i && i >= 0 && i < KalStone.Length ? KalStone[i].Coverage : 1.2f;

    /// <summary>
    /// Undyed and earth-dyed cloth for the interim garments (owner, 2026-10-25: no blue denim or bright tees on the frontier): the colour a
    /// shirt or a pair of trousers is drawn in, whatever its baked colour (sRGB). The first is the default, so every body is dressed in one.
    /// </summary>
    public static readonly Swatch[] Shirts =
    {
        new("Undyed linen", .7f, .64f, .52f), new("Oxblood", .42f, .14f, .13f), new("Moss", .36f, .4f, .24f), new("Ochre", .66f, .5f, .25f),
        new("Slate", .36f, .38f, .4f),
    };

    public static readonly Swatch[] Trousers = { new("Brown", .33f, .25f, .18f), new("Charcoal", .2f, .2f, .21f) };

    /// <summary>The interim boots are black rubber garden boots; they are always drawn as worn leather.</summary>
    public static readonly Swatch Boots = new("Leather", .3f, .2f, .13f);

    /// <summary>
    /// The names the creator shows for what the bodies are baked with. HumGen's catalogue names its own pieces ("Caucasian 2", "Pirate
    /// Boots", "Smart Casual"): real-world and modern words for a fantasy people, or garments the dye draws otherwise. A body in the
    /// frontier outfit wears it in place of whatever outfit it was baked in (<see cref="FrontierOutfit"/>). Faces are named apart
    /// (<see cref="BakedFace"/>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ShownNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["outfit.casual.smart_casual"] = "Work shirt and trousers",
        ["outfit.casual.casual_weekday"] = "Work shirt and trousers",
        ["outfit.extra_outfits_pack.pirate"] = FrontierOutfit,
        ["footwear.outdoor.garden_boot"] = "Leather boots",
        ["footwear.extra_outfits_pack.pirate_boots"] = "Leather boots",
        ["hair.long.ponytail"] = "Ponytail",
        ["hair.long.ponytail_short"] = "Short ponytail",
        ["hair.long.bob_short"] = "Short bob",
        ["hair.long.medium_center_part"] = "Long, parted in the centre",
        ["hair.short.short_combed"] = "Short, combed back",
        ["facehair.other.stubble_short"] = "Short stubble",
        ["face.veth.m_holt"] = "Long and lean",
        ["face.veth.m_marsh"] = "Round and broad",
        ["face.veth.m_tarr"] = "Angular",
        ["face.veth.f_holt"] = "Long and lean",
        ["face.veth.f_marsh"] = "Round and broad",
        ["face.veth.f_tarr"] = "Angular",
        ["hair.veth.m_holt"] = "Short, swept back",
        ["hair.veth.m_marsh"] = "Close-cropped",
        ["hair.veth.m_tarr"] = "Short and thick",
        ["hair.veth.f_holt"] = "Straight bob",
        ["hair.veth.f_marsh"] = "Tied back",
        ["hair.veth.f_tarr"] = "Short, parted at the side",
        ["facehair.veth.m_holt"] = "Goatee",
        ["facehair.veth.m_tarr"] = "Long moustache",
    };

    /// <summary>The one face each body is baked with (HumGen names its presets by real-world ethnicity).</summary>
    public const string BakedFace = "Their own face";

    public const string FrontierOutfit = "Frontier: linen shirt, baldric and sash";

    /// <summary>
    /// A garment's kind from its material name: a top, trousers, boots, or null (belts and the rest keep their colour). The frontier
    /// outfit's pieces (HumGen's Pirate set) keep their own colours: its shirt is one material with the baldric and sash.
    /// </summary>
    public static string? GarmentKind(string material)
    {
        string name = material.ToLowerInvariant();
        if (name.Contains("_pirate_"))
            return null;
        if (new[] { "tshirt", "shirt", "sweater", "top", "tunic", "blouse", "hoodie" }.Any(name.Contains))
            return "shirt";
        if (name.Contains("boots"))
            return "boots";
        return new[] { "jeans", "chinos", "trousers", "pants", "shorts" }.Any(name.Contains) ? "trousers" : null;
    }

    /// <summary>The cloth an index names, the first when none is chosen or the index is not in the list.</summary>
    public static Swatch Cloth(IReadOnlyList<Swatch> cloth, int? index) => index is int i && i >= 0 && i < cloth.Count ? cloth[i] : cloth[0];

    /// <summary>
    /// The hairstyles a body can wear: its own baked hair first, then HumGen hair baked on the same rig and shipped as rigged models
    /// (<c>rigged/&lt;asset&gt;/&lt;asset&gt;_rigged.glb</c>). A style whose model is not installed is not offered.
    /// </summary>
    public static IReadOnlyList<(string Label, string? Asset)> HairStyles(string? bodyModel) => bodyModel switch
    {
        // The long bob (humgen_hair_f_bob_long) is left out: in game its cards draw as one flat sheet, worse than the ponytail.
        "humgen_player_ada_f" => new (string, string?)[] { ("Ponytail", null), ("Bun", "humgen_hair_f_bun") },
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

    /// <summary>Stable resident variation from the existing natural palettes; identity and garments stay as authored.</summary>
    public static (int Skin, int Hair) ResidentColours(string? people, string instance)
    {
        uint Hash(string part)
        {
            uint value = 2166136261;
            foreach (char c in instance + part) value = unchecked((value ^ c) * 16777619);
            return value;
        }
        int[] skin = people switch { "people.siann" => new[] { 0, 1, 2, 3, 4 }, "people.kal" => new[] { 0, 1, 2, 4 }, _ => new[] { 0, 5, 6, 7 } };
        return (skin[Hash("|skin") % (uint)skin.Length], (int)(Hash("|hair") % (uint)Hair(people).Count));
    }

    /// <summary>
    /// The swatch an index names (none chosen is the first), or null where that is the body as baked or the list has no such index. A list
    /// without "As made" (Siann hair) therefore always recolours.
    /// </summary>
    public static Swatch? Chosen(IReadOnlyList<Swatch> swatches, int? index) =>
        (index ?? 0) is var i && i >= 0 && i < swatches.Count && swatches[i] != AsMade && swatches[i] != Unchanged ? swatches[i] : null;
}
