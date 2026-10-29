using System.Collections.Immutable;
// UNNAMED Domain - how visible a body is, and to whom (STEALTH.md §2: light, cover, posture, silhouette)
// No Godot references - pure C#

using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Combat;

/// <summary>
/// The four bands light is read in (STEALTH §2.1). Named, never hidden: the player sees the band they are standing in, and a creature's
/// sight is scaled by it by the same stated number.
/// </summary>
public enum LightBand
{
    /// <summary>Deep dark, or a shuttered room: shadow shortens sight, it never grants invisibility.</summary>
    Dark,

    /// <summary>Twilight, a low fire across a yard, the edge of a torch's reach.</summary>
    Dim,

    /// <summary>Ordinary work light: the light a plain look is measured in.</summary>
    Lit,

    /// <summary>Full day, or a fire at arm's length.</summary>
    Bright,
}

/// <summary>What a posture does to a body (STEALTH §2.6): how tall it stands and how much harder it is to pick out.</summary>
public sealed record PostureRule(long EyeMm, double SightGain, long HeightMm);

/// <summary>
/// The light, cover and posture numbers (STEALTH §2), as content. Every factor here is a physical condition the player can read and change -
/// a light doused, a hedge crouched behind, a body laid flat - and none of them is a stealth flag.
/// </summary>
public sealed record LightRules
{
    /// <summary>The bands, as the ceiling of each in per mille: at or below is that band.</summary>
    public int DarkBelow { get; init; } = 150;

    public int DimBelow { get; init; } = 400;
    public int LitBelow { get; init; } = 750;

    /// <summary>What each band multiplies a creature's sight by: the floor is why dark is not a cloak (§2.1).</summary>
    public double DarkFactor { get; init; } = 0.35;

    public double DimFactor { get; init; } = 0.6;
    public double LitFactor { get; init; } = 1.0;
    public double BrightFactor { get; init; } = 1.15;

    /// <summary>What a creature that hunts by night gets out of the dark instead (§2.1).</summary>
    public double NightSightDarkFactor { get; init; } = 0.9;

    /// <summary>Ambient light by the sky clock, in per mille: the day is Lit, the deep night is Dark under a new moon (§2.4).</summary>
    public int AmbientDay { get; init; } = 800;

    public int AmbientNight { get; init; } = 90;

    /// <summary>An interior gets no sky, so its ambient is this, and the room's own lights do the rest (§2.1).</summary>
    public int InteriorAmbient { get; init; } = 120;

    /// <summary>How far a carried light is seen from, as a multiple of the light's own radius (§2.2, the Thief rule).</summary>
    public long CarrierSightMultiple { get; init; } = 8;

    public PostureRule Standing { get; init; } = new(1_800, 1.0, 1_800);
    public PostureRule Crouched { get; init; } = new(1_100, 0.7, 1_100);
    public PostureRule Prone { get; init; } = new(500, 0.35, 500);

    public LightBand Band(int litPermille) =>
        litPermille <= DarkBelow ? LightBand.Dark
        : litPermille <= DimBelow ? LightBand.Dim
        : litPermille <= LitBelow ? LightBand.Lit
        : LightBand.Bright;

    /// <summary>What the band does to a creature's sight, unless the creature sees in the dark (§2.1).</summary>
    public double SightFactor(LightBand band, bool nightSight) => band switch
    {
        LightBand.Dark => nightSight ? NightSightDarkFactor : DarkFactor,
        LightBand.Dim => DimFactor,
        LightBand.Lit => LitFactor,
        _ => BrightFactor,
    };

    public PostureRule Posture(Stance stance) => stance switch
    {
        Stance.Prone => Prone,
        Stance.Crouched => Crouched,
        _ => Standing,
    };

    /// <summary>The ambient light of a space at an hour: the sky outside, and indoors what a room keeps without a window (§2.1, §2.4).</summary>
    public int Ambient(bool exterior, bool night) => exterior ? (night ? AmbientNight : AmbientDay) : InteriorAmbient;

    /// <summary>The whole light at a point: the ambient, plus every lit light standing over it, to the per mille (§2.1).</summary>
    public int At(IEnumerable<LightSource> lights, long xMm, long zMm, int ambient)
    {
        int total = ambient;
        foreach (var light in lights)
            total += light.At(xMm, zMm);
        return Math.Clamp(total, 0, PerMille);
    }

    /// <summary>The scale of the light itself: per mille of full day.</summary>
    public const int PerMille = 1000;
}

public static class Visibility
{
    /// <summary>The three sample points a body is looked at through (§2.3): head, chest and feet.</summary>
    public const int SamplePoints = 3;

    /// <summary>
    /// Whether a cover volume hides this body at this posture: the body must be standing in it, and no taller than it - a 1.2 m hedge hides
    /// a crouched body and not a standing one (§2.3). <paramref name="toCoverMm"/> is how far the body is from the cover's own footprint.
    /// </summary>
    public static bool Hides(CoverVolume cover, PostureRule posture, long toCoverMm) =>
        toCoverMm <= cover.RadiusMm && posture.HeightMm <= cover.HeightMm;

    /// <summary>
    /// How much of a body is seen through cover: three sample points, each blocked or not, so a body behind a low wall is a third seen, two
    /// thirds, or all of it - never a number nobody can read (§2.3). No cover is all of it.
    /// </summary>
    public static double SeenFraction(CoverVolume? cover, PostureRule posture, long toCoverMm)
    {
        if (cover is null || !Hides(cover, posture, toCoverMm))
            return 1.0;
        int blocked = Math.Clamp(cover.BlocksSightPercent, 0, 100) * SamplePoints / 100;
        return Math.Clamp((SamplePoints - blocked) / (double)SamplePoints, 0.0, 1.0);
    }

    /// <summary>
    /// The whole visibility of a body for one observer (STEALTH §2): the band's factor, the share of it the cover leaves, the posture's own
    /// gain, whether the observer has only its periphery on it, and a silhouette against a bright backdrop. Never zero: 0.35 at two metres is
    /// still a body standing in front of you.
    /// </summary>
    /// <param name="gearFactor">
    /// What the character is wearing or carrying does to being seen (STEALTH.md 10): an optical cloak's 0.25 standing still or 0.6 moving, and 1.0
    /// for nothing. It scales the whole product, so no piece of kit ever makes a body unseeable - it only makes them less likely to be seen under the
    /// stated conditions, exactly as the canon table says.
    /// </param>
    public static double Factor(LightRules rules, LightBand band, bool nightSight, double seenFraction, PostureRule posture,
        bool peripheralOnly, bool silhouette, double gearFactor = 1.0)
    {
        double factor = rules.SightFactor(band, nightSight) * Math.Clamp(seenFraction, 0.0, 1.0) * posture.SightGain;
        if (peripheralOnly)
            factor *= 0.4;
        if (silhouette)
            factor *= rules.BrightFactor;
        return factor * Math.Clamp(gearFactor, 0.05, 1.0);
    }

    /// <summary>
    /// The peripheral band of a field of view (STEALTH §2.3): inside 35° of the facing a body is read whole, out to the field's own edge only
    /// its movement and only at 0.4, and behind it not at all. A distracted observer has only the peripheral band.
    /// </summary>
    public static bool InPeriphery(long facingMdeg, long fieldOfViewMdeg, long fromXMm, long fromZMm, long toXMm, long toZMm)
    {
        double dx = toXMm - fromXMm, dz = toZMm - fromZMm;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance < 1)
            return false;
        double facing = facingMdeg / 1000.0 * Math.PI / 180;
        double cos = (dx * Math.Sin(facing) + dz * Math.Cos(facing)) / distance;
        return cos < Math.Cos(35 * Math.PI / 180) && cos >= Math.Cos(fieldOfViewMdeg / 2000.0 * Math.PI / 180);
    }

    /// <summary>Whether the point is inside the observer's field of view at all (the central band included).</summary>
    public static bool InField(long facingMdeg, long fieldOfViewMdeg, long fromXMm, long fromZMm, long toXMm, long toZMm)
    {
        double dx = toXMm - fromXMm, dz = toZMm - fromZMm;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance < 1)
            return true;
        double facing = facingMdeg / 1000.0 * Math.PI / 180;
        double cos = (dx * Math.Sin(facing) + dz * Math.Cos(facing)) / distance;
        return cos >= Math.Cos(fieldOfViewMdeg / 2000.0 * Math.PI / 180);
    }
}

/// <summary>
/// An authored way through a place that does not fight for it (STEALTH.md section 8.1): a shadow line, a grass line, a ledge, a roof way, a vent,
/// a servant's passage, a wall, a sewer or a disguise. Each carries what it needs of the character and how much sight it denies, so a route is a
/// real choice with a price rather than a free path.
/// </summary>
public sealed record StealthRouteSite(
    string Key,
    string Kind,
    long XMm,
    long ZMm,
    long ToXMm,
    long ToZMm,
    int CoverPercent)
{
    /// <summary>The route kinds canon allows (STEALTH.md 8.1).</summary>
    public static readonly ImmutableArray<string> Kinds = ImmutableArray.Create(
        "shadow", "grass", "ledge", "roof", "vent", "servant", "wall", "sewer", "trap", "disguise");

    /// <summary>The skill and band a route asks for, when it asks for one (a ledge is athletics, a vent is lockpicking).</summary>
    public string? SkillId { get; init; }

    public int SkillMin { get; init; }

    /// <summary>How far this route can see to a target zone: a perch is a route that looks down one (STEALTH.md 8.2).</summary>
    public long SightlineMm { get; init; }
}
