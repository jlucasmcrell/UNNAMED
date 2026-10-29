// UNNAMED Presentation - what the stealth HUD reads, worked out without drawing anything
// Godot presentation only: the numbers come from the simulation, and nothing here decides what the AI does.

using UNNAMED.Domain.Combat;

namespace UNNAMED.Presentation.Ui.Models;

/// <summary>
/// The light gem's whole content (STEALTH §2.7): the band the character is standing in as a named shape, a fill and a word. Shape carries the
/// band as well as colour, so a colour-blind player reads the same four steps. It is the band the sight check itself uses - there is no
/// second, hidden number.
/// </summary>
public readonly record struct LightGemModel(LightBand Band, string Shape, string Words, float Fill)
{
    public static LightGemModel From(LightBand band) => band switch
    {
        LightBand.Dark => new(band, "closed", "Shrouded", 0.12f),
        LightBand.Dim => new(band, "half", "In shadow", 0.4f),
        LightBand.Lit => new(band, "open", "In the light", 0.72f),
        _ => new(band, "radiant", "Lit up", 1f),
    };

    /// <summary>The four steps the gem is read in, brightest last: what a settings page lists.</summary>
    public static readonly LightBand[] Bands = { LightBand.Dark, LightBand.Dim, LightBand.Lit, LightBand.Bright };
}

/// <summary>
/// The noise the character is making right now (STEALTH §2.7's ring): the actual radius the creatures are hearing this tick, in metres, and
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

/// <summary>What told an observer there was something there (STEALTH §2.7): an eye saw it, an ear heard it, a nose scented it.</summary>
public enum NoticeSense
{
    Eye,
    Ear,
    Nose,
}

/// <summary>
/// One observer's awareness as the HUD shows it: the real number over the real scale, what told it, and the bearing to the place it thinks
/// the character is - which for a noise or a scent is that noise's own place, not the body. The fill ring is the same value
/// <c>Perception.Accrue</c> keeps, so the player reads exactly what the creature knows.
/// </summary>
public readonly record struct AwarenessRead(int Awareness, int Full, NoticeSense Sense, float BearingDegrees, bool HasDirection)
{
    public static readonly AwarenessRead None = new(0, 100, NoticeSense.Eye, 0, false);

    /// <summary>The share of the way to full, 0..1, which is the ring's fill.</summary>
    public float Fill => Full <= 0 ? 0 : Math.Clamp(Awareness / (float)Full, 0f, 1f);

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
