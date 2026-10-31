// UNNAMED Domain - what a device can and cannot see, and what a ward does when it is crossed (STEALTH.md 10, S7 of D23)
// No Godot references - pure C#

using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Combat;

/// <summary>
/// A body a detector could read, as the detector sees it: where it is, whether it moves, how it is postured, how warm it is to a thermal eye, and how
/// much solid matter stands between it and the lens. Cover is a physical condition here too - a thermal sensor reads through grass and a curtain and
/// not through stone, water or a cloak's cold outer layer.
/// </summary>
public sealed record DetectorTarget(SpaceId Space, long XMm, long ZMm, long HeightMm, bool Moving, bool Prone, bool Warm, long SolidMm);

/// <summary>What a carrier's detector reports: whether the target is read at all, and which of its senses found it.</summary>
public sealed record DetectionRead(string Key, bool Read, string Sense, string Why);

/// <summary>
/// Canon's two tech detectors (STEALTH.md 10), each with the blind spot that makes it a device and not a wall-hack:
/// a motion sensor sees only what moves, and nothing at all that is still or crawling; a thermal sensor sees warm bodies through light cover and
/// nothing through stone or water, and nothing of a cloaked body's cold outer shell.
/// </summary>
public static class DetectorRules
{
    /// <summary>A motion sensor's cone: six metres, and forty-five degrees either side of the lens.</summary>
    public const long MotionRangeMm = 6_000;

    public const int MotionHalfAngleDeg = 45;

    /// <summary>A thermal sensor sees further than a motion sensor, and only the warm.</summary>
    public const long ThermalRangeMm = 8_000;

    public const int ThermalHalfAngleDeg = 60;

    /// <summary>How much solid matter a thermal eye reads through: grass and curtain are not stone, and 600 mm is where cover stops being light.</summary>
    public const long ThermalSolidLimitMm = 600;

    /// <summary>
    /// The motion sensor's reading of one target (STEALTH.md 10): only movement, only inside the cone, and blind to stillness and to crawling - a body
    /// that keeps still is not there as far as the sensor is concerned, however bright and warm it is.
    /// </summary>
    public static DetectionRead Motion(SpaceId space, long lensXMm, long lensZMm, int facingMdeg, DetectorTarget target)
    {
        if (target.Space != space)
            return new DetectionRead("motion", false, "none", "another space");
        long dx = target.XMm - lensXMm;
        long dz = target.ZMm - lensZMm;
        long square = dx * dx + dz * dz;
        if (square > MotionRangeMm * MotionRangeMm)
            return new DetectionRead("motion", false, "none", "out of range");
        if (!InCone(facingMdeg, dx, dz, MotionHalfAngleDeg))
            return new DetectionRead("motion", false, "none", "outside the cone");
        if (!target.Moving)
            return new DetectionRead("motion", false, "none", "nothing moving: a still body is not read");
        return new DetectionRead("motion", true, "motion", "movement inside the cone");
    }

    /// <summary>
    /// The thermal sensor's reading (STEALTH.md 10): a warm body through light cover, nothing through stone, water or a cold shell, and no requirement
    /// that it move - which is why a still body is exactly what a thermal sensor is for and exactly what a motion sensor misses.
    /// </summary>
    public static DetectionRead Thermal(SpaceId space, long lensXMm, long lensZMm, int facingMdeg, DetectorTarget target)
    {
        if (target.Space != space)
            return new DetectionRead("thermal", false, "none", "another space");
        long dx = target.XMm - lensXMm;
        long dz = target.ZMm - lensZMm;
        long square = dx * dx + dz * dz;
        if (square > ThermalRangeMm * ThermalRangeMm)
            return new DetectionRead("thermal", false, "none", "out of range");
        if (!InCone(facingMdeg, dx, dz, ThermalHalfAngleDeg))
            return new DetectionRead("thermal", false, "none", "outside the cone");
        if (!target.Warm)
            return new DetectionRead("thermal", false, "none", "no warmth to read");
        if (target.SolidMm > ThermalSolidLimitMm)
            return new DetectionRead("thermal", false, "none", "stone or water between them");
        return new DetectionRead("thermal", true, "thermal", "a warm body behind light cover");
    }

    /// <summary>Whether a target lies inside a device's cone: the same reading the eye's field of view uses (STEALTH.md 2.3).</summary>
    public static bool InCone(int facingMdeg, long dx, long dz, int halfAngleDeg)
    {
        double heading = Math.Atan2(-dz, dx) * 180.0 / Math.PI;
        double off = ((heading - facingMdeg) % 360 + 540) % 360 - 180;
        return Math.Abs(off) <= halfAngleDeg;
    }
}

/// <summary>
/// A ward set across a passage (STEALTH.md 10): an alarm when it is crossed, disabled only by the working that answers it, a rune key, or a distraction
/// that crosses it first. The ward is content, its disabling is a rule, and neither is a switch the player can simply turn off.
/// </summary>
public static class WardRules
{
    /// <summary>The one working that quiets a ward, as content names it.</summary>
    public const string Quieting = "spell.quiet_the_ward";

    /// <summary>Crossed: the alarm fires unless something crossed it first (a thrown stone counts) or the ward was quieted.</summary>
    public static bool Alarms(long toWardMm, long radiusMm, bool quieted, bool crossedByDistraction, bool hasRuneKey) =>
        !quieted && !hasRuneKey && !crossedByDistraction && toWardMm <= radiusMm;
}

/// <summary>
/// A ward set across a passage, as content holds it (STEALTH.md 10): where it stands, its radius, the alarm it raises, and the rune key that answers
/// it. A ward is not a trap - it makes a sound, and the sound is what the world reacts to.
/// </summary>
public sealed record WardSite(string Key, long XMm, long ZMm, long RadiusMm)
{
    /// <summary>The world flag the alarm raises, so the creatures and the law both hear the same bell.</summary>
    public string AlarmFlag { get; init; } = "";

    /// <summary>The key that quiets it without the working (a rune key in the pack).</summary>
    public string RuneKey { get; init; } = "";

    /// <summary>What the ward is called where a player reads it.</summary>
    public string DisplayName { get; init; } = "";
}
