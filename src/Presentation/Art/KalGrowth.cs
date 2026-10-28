namespace UNNAMED.Presentation.Art;

/// <summary>
/// The stone growths a Kal grows instead of hair (owner's ruling, 2026-10-28). They are authored in Blender on the real creator
/// head and shipped as head-attached parts (<c>tools/creator/export_kal_growths.py</c>), so these are only the names the creator
/// offers and the asset each one loads - no geometry is built at runtime.
/// </summary>
public static class KalGrowth
{
    /// <summary>The growths the Kal hair step offers; the first is bare stone, which is the people's own head.</summary>
    public static readonly string[] Styles = { "Bare stone", "Crown crest", "Temple ridges", "Crystal outcrops", "Brow horns" };

    /// <summary>The jaw and chin growths the beard slot offers; the first is none.</summary>
    public static readonly string[] Jaw = { "None", "Jaw ridges", "Calcite fringe" };

    /// <summary>The part each style loads, in the same order as <see cref="Styles"/> without its bare-stone entry.</summary>
    public static readonly string[] StyleAssets = { "crown_crest", "temple_ridges", "crystal_outcrops", "brow_horns" };

    /// <summary>The part each jaw choice loads, in the same order as <see cref="Jaw"/> without its none entry.</summary>
    public static readonly string[] JawAssets = { "jaw_ridges", "calcite_fringe" };

    public const string StyleSlider = "look.kal_growth", JawSlider = "look.kal_jaw";

    /// <summary>The part id for a growth choice, or null for bare stone and none.</summary>
    public static string? Part(string sex, int style, int jaw) => style > 0 && style <= StyleAssets.Length
        ? "humgen_creator_" + sex + "_growth_" + StyleAssets[style - 1]
        : jaw > 0 && jaw <= JawAssets.Length ? "humgen_creator_" + sex + "_growth_" + JawAssets[jaw - 1] : null;
}
