// UNNAMED Domain - what the ground is made of, and what that does to a footfall (STEALTH.md §3.1)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// A patch of ground with one surface (STEALTH §3.1): the yard is gravel, the smithy's floor is stone, the jetty is wood, the creek is water.
/// A patch is a rectangle in the space's own metres, and the smallest one that contains a point wins, so an author can lay a wooden floor
/// inside a gravel yard without listing every square metre.
/// </summary>
public sealed record SurfacePatch(string Key, string Kind, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm)
{
    public bool Holds(long xMm, long zMm) => xMm >= MinXMm && xMm <= MaxXMm && zMm >= MinZMm && zMm <= MaxZMm;
}

public static class Surfaces
{
    /// <summary>The surfaces the level kit offers (§3.1); anything authored must be one of them.</summary>
    public static ImmutableArray<string> Kinds { get; } = ImmutableArray.Create(
        "earth", "grass", "mud", "gravel", "wood", "stone", "metal", "water");

    /// <summary>
    /// What a surface multiplies a footfall's radius by (§3.1): loose gravel crunches half again as far as soil, a metal grate rings nearly
    /// twice, and wet mud and grass swallow it. This is the one place the surface matters, and every gait goes through it.
    /// </summary>
    public static double Factor(string kind) => kind switch
    {
        "gravel" => 1.5,
        "metal" => 1.8,
        "stone" => 1.2,
        "wood" => 1.25,
        "water" => 1.3,
        "grass" => 0.8,
        "mud" => 0.7,
        _ => 1.0,
    };

    /// <summary>
    /// The surface under a point: the smallest authored patch containing it, else the space's default - bare earth, which is the floor of
    /// everywhere nobody bothered to author (HOOK.STEALTH.SURFACE's own default).
    /// </summary>
    public static string At(IEnumerable<SurfacePatch> patches, long xMm, long zMm)
    {
        SurfacePatch? best = null;
        foreach (var patch in patches)
        {
            if (!patch.Holds(xMm, zMm))
                continue;
            if (best is null || Area(patch) < Area(best))
                best = patch;
        }
        return best?.Kind ?? "earth";
    }

    private static long Area(SurfacePatch patch) => (patch.MaxXMm - patch.MinXMm) * (patch.MaxZMm - patch.MinZMm);
}
