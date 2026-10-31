// UNNAMED Presentation - what the stealth HUD reads, worked out without drawing anything
// Godot presentation only: the numbers come from the simulation, and nothing here decides what the AI does.

using UNNAMED.Domain.Combat;

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>
/// The character's own silhouette in the band they are standing in (STEALTH Â§2.7a, the owner's ruling of 2026-10-30): drawn solid black when the
/// character is in the dark and washed almost out when they stand in the light, so the player reads their own body going from hidden to exposed -
/// the same four steps the sight check uses, still four distinct figures, and still readable without colour.
/// </summary>
public readonly record struct ShadowModel(LightBand Band, float Ink, string Words, string Shape, string People)
{
    public static ShadowModel From(LightBand band, string people = "people.kal") => band switch
    {
        LightBand.Dark => new(band, 1f, "Shrouded", "flat", people),
        LightBand.Dim => new(band, 0.72f, "In shadow", "soft", people),
        LightBand.Lit => new(band, 0.42f, "In the light", "edged", people),
        _ => new(band, 0.16f, "Lit up", "washed", people),
    };

    /// <summary>The four steps the silhouette is read in, darkest first: what a settings page lists.</summary>
    public static readonly LightBand[] Bands = { LightBand.Dark, LightBand.Dim, LightBand.Lit, LightBand.Bright };

    /// <summary>
    /// How the hue leans, per people (the owner's "tinted per people"): a token rather than a colour, so the palette stays in the presentation's own
    /// hands and the model stays testable.
    /// </summary>
    public static string Tint(string people) => people switch
    {
        "people.siann" => "chalk",
        "people.veth" => "rust",
        "people.ashborn" => "ash",
        _ => "stone",
    };
}

/// <summary>How much of the stealth read is drawn (STEALTH Â§2.7a): the full silhouette and ring, or the minimal setting's sound and body language only.</summary>
public enum StealthHudMode
{
    Full,
    Minimal,
}

/// <summary>
/// The noise the character is making right now (STEALTH Â§2.7's ring): the actual radius the creatures are hearing this tick, in metres, and
/// the surface or act that made it. The ring is drawn at that radius, so a crouch-walk really is a small circle and a sprint a large one.
/// </summary>
public readonly record struct NoiseRingModel(float RadiusMetres, string Words)
{
    public static NoiseRingModel From(long radiusMm, string? surface = null)
    {
        float metres = Math.Max(0, radiusMm) / 1000f;
        if (metres <= 0)
            return new NoiseRingModel(0, "Silent");
        string words = metres < 1f ? $"{metres:0.0} m" : $"{Math.Round(metres)} m";
        return new NoiseRingModel(metres, surface is null ? words : $"{words} - {surface}");
    }
}

/// <summary>What told an observer there was something there (STEALTH Â§2.7a): an eye saw it, an ear heard it, a print or a body showed them, a nose scented it.</summary>
public enum NoticeSense
{
    Eye,
    Ear,
    Footprint,
    Nose,
}

/// <summary>
/// One observer's awareness as the HUD shows it: the real number over the real scale, what told it, the bearing to the place it thinks the
/// character is - which for a noise or a scent is that noise's own place, not the body - and whether the sense that found them was not a person's.
/// </summary>
public readonly record struct AwarenessRead(int Awareness, int Full, NoticeSense Sense, float BearingDegrees, bool HasDirection, bool Other = false)
{
    public static readonly AwarenessRead None = new(0, 100, NoticeSense.Eye, 0, false);

    /// <summary>The share of the way to full, 0..1, which is the ring's fill.</summary>
    public float Fill => Full <= 0 ? 0 : Math.Clamp(Awareness / (float)Full, 0f, 1f);

    /// <summary>The glyph the meter draws for this read: a shape, so the player never has to read a colour to know what gave them away.</summary>
    public static string Glyph(NoticeSense sense) => sense switch
    {
        NoticeSense.Ear => "ear",
        NoticeSense.Footprint => "print",
        NoticeSense.Nose => "nose",
        _ => "eye",
    };

    /// <summary>
    /// Bearing from the observer to the place it is looking at, in degrees clockwise from world +Z (north), which is the world's own
    /// convention (<c>CombatRules.FacingTowards</c>), so the wedge and the compass agree.
    /// </summary>
    public static (float Bearing, bool Has) Toward(long fromXMm, long fromZMm, long toXMm, long toZMm)
    {
        long dx = toXMm - fromXMm, dz = toZMm - fromZMm;
        if (dx == 0 && dz == 0)
            return (0, false);
        double degrees = Math.Atan2(dx, dz) * 180 / Math.PI;
        return ((float)((degrees + 360) % 360), true);
    }
}

/// <summary>
/// What the carried sensors are reading (D23 S7, STEALTH.md 10): how many bodies they have, which sense found the nearest, and the bearing to it.
/// Empty when no sensor is carried, which is why the HUD draws nothing then - a detector readout that is always on would be a wall-hack.
/// </summary>
public readonly record struct DetectorReadModel(int Contacts, string Sense, float BearingDegrees, bool HasDirection, string Words)
{
    public static readonly DetectorReadModel None = new(0, "none", 0, false, "");

    public static DetectorReadModel From(IEnumerable<(string Sense, long XMm, long ZMm)> contacts, long fromXMm, long fromZMm)
    {
        var list = contacts.ToArray();
        if (list.Length == 0)
            return None;
        var (sense, x, z) = list[0];
        var (bearing, has) = AwarenessRead.Toward(fromXMm, fromZMm, x, z);
        return new DetectorReadModel(list.Length, sense, bearing, has,
            list.Length == 1 ? $"1 body ({sense})" : $"{list.Length} bodies ({sense} nearest)");
    }
}
