// UNNAMED Domain - what kit does to being seen and to being heard (STEALTH.md 10, S7 of D23)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;

namespace UNNAMED.Domain.Combat;

/// <summary>
/// The stealth kit's numbers, read by effect id (STEALTH.md 10). Canon's rule is that no item makes the character unseeable, only less likely to be
/// seen under stated physical conditions: every factor here is a multiplier on a physical reading, never a switch, and each has a price the table
/// names (a cell, weight, heat, rain, a strange smell).
/// </summary>
public static class StealthGear
{
    /// <summary>An optical cloak: a quarter while still, six tenths while moving - the shimmer is what gives a moving body away.</summary>
    public const string Cloak = "effect.cloaked";

    /// <summary>A sound dampener: footfall at a fifth, a swing or a shot at six tenths.</summary>
    public const string Muffled = "effect.muffled";

    /// <summary>A marksman's silencer: a shot's report at four tenths. The rail's crack is not damped.</summary>
    public const string Silenced = "effect.silenced";

    /// <summary>A scent mask: scent read at half range, and washed out by rain.</summary>
    public const string ScentMasked = "effect.scent_masked";

    /// <summary>A local patch of Dark made in a lit space: a light manipulation, not a sight one.</summary>
    public const string ShadowStuff = "effect.shadow_stuffed";

    /// <summary>How the effect ids map to what the world multiplies, and the one place those numbers are written.</summary>
    public static readonly ImmutableDictionary<string, double> VisibilityFactors = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        [Cloak] = 0.25,
        [ShadowStuff] = 0.35,
    }.ToImmutableDictionary();

    public static readonly ImmutableDictionary<string, double> NoiseFactors = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        [Muffled] = 0.20,
    }.ToImmutableDictionary();

    public static readonly ImmutableDictionary<string, double> ShotNoiseFactors = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        [Silenced] = 0.40,
    }.ToImmutableDictionary();

    public static readonly ImmutableDictionary<string, double> ScentFactors = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        [ScentMasked] = 0.50,
    }.ToImmutableDictionary();

    /// <summary>The cloak's moving penalty: what a moving body wearing one is worth, against the still factor it starts at.</summary>
    public const double CloakMoving = 0.60;

    /// <summary>
    /// The visibility multiplier for a set of active effects. A cloak is applied last and is worth <see cref="CloakMoving"/> when the body moves, so
    /// a moving cloaked body is six tenths even if it is standing in a Dark strip - the two conditions multiply, they do not replace each other.
    /// </summary>
    public static double Visibility(IEnumerable<string> effectIds, bool moving)
    {
        double factor = 1.0;
        bool cloaked = false;
        foreach (string id in effectIds)
        {
            if (id == Cloak)
            {
                cloaked = true;
                continue;
            }
            if (VisibilityFactors.TryGetValue(id, out double value))
                factor *= value;
        }
        if (cloaked)
            factor *= moving ? CloakMoving : VisibilityFactors[Cloak];
        return Math.Clamp(factor, 0.05, 1.0);
    }

    /// <summary>The noise multiplier for a set of active effects; anything not in the table is silent about itself.</summary>
    public static double Noise(IEnumerable<string> effectIds)
    {
        double factor = 1.0;
        foreach (string id in effectIds)
            if (NoiseFactors.TryGetValue(id, out double value))
                factor *= value;
        return factor;
    }

    /// <summary>The multiplier on a shot's report.</summary>
    public static double ShotNoise(IEnumerable<string> effectIds)
    {
        double factor = 1.0;
        foreach (string id in effectIds)
            if (ShotNoiseFactors.TryGetValue(id, out double value))
                factor *= value;
        return factor;
    }

    /// <summary>The multiplier on how far a scent is read.</summary>
    public static double Scent(IEnumerable<string> effectIds)
    {
        double factor = 1.0;
        foreach (string id in effectIds)
            if (ScentFactors.TryGetValue(id, out double value))
                factor *= value;
        return factor;
    }

    /// <summary>The D21 item families the kit is made for: the marksman's silencer goes on the marksman family's emitter, and nothing else.</summary>
    public static string FamilyOf(string itemId) => itemId switch
    {
        "item.tool.silencer" => "marksman",
        "item.armor.cloak_optical" => "armor",
        "item.armor.boots_muffled" => "armor",
        _ => "",
    };
}
