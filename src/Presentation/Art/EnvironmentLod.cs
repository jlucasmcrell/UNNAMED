namespace UNNAMED.Presentation.Art;

// Owner Ultra-first ruling, 2026-10-22. Distances refer to the common bounds centre.
internal static class EnvironmentLod
{
    internal static bool FullQuality(string tier) => tier is "high" or "ultra";

    internal static float[] ModelStarts(string tier, bool tree, float size, float scale)
    {
        if (tree)
            return (tier switch
            {
                "ultra" => new[] { 0f, 400f, 650f, 950f },
                "high" => new[] { 0f, 250f, 450f, 750f },
                _ => new[] { 0f, 35f, 80f, 150f },
            }).Select(d => d * scale).ToArray();
        float first = FullQuality(tier) ? Math.Max(tier == "ultra" ? 160 : 100, size * 8) : Math.Max(8, size * 4);
        return new[] { 0, first * scale, Math.Max(20, first * 2.5f) * scale, Math.Max(45, first * 5.5f) * scale };
    }

    // Godot Self fades over [threshold-margin, threshold+margin]. Stagger adjacent
    // thresholds by a margin: one level remains fully opaque throughout its neighbour's fade.
    internal static (float Begin, float End, float BeginMargin, float EndMargin) Band(float begin, float end, bool fade)
    {
        float bm = fade ? begin * .1f : 0, em = fade ? end * .1f : 0;
        return (begin > 0 ? begin - bm : 0, end > 0 ? end + em : 0, bm, em);
    }

    internal static float GroundFullReach(string tier, float authored, float scale, bool tree = false) => scale * (tier switch
    {
        "ultra" => Math.Max(tree ? 400 : 120, authored * 1.8f),
        "high" => Math.Max(tree ? 250 : 90, authored * 1.5f),
        _ => authored,
    });

    internal static float GroundEnd(string tier, float full) => FullQuality(tier) ? full + Math.Max(8, full * .1f) : full;
}
