// UNNAMED Domain - perception, and the behaviour roles creatures play (STEALTH_DETECTION_AND_THREAT.md §1-§5, §16;
// SYSTEMS.md S-23; M3d)
// No Godot references - pure C#

using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Creatures;

/// <summary>An actor's senses (DATA_MODEL.md §4.4 <c>perception</c>): how far it sees, across what arc, and how far it hears.</summary>
public sealed record Senses(long SightMm, long FieldOfViewMdeg, long HearingMm);

/// <summary>
/// How awareness grows and fades (STEALTH §3: awareness is not binary). It runs 0..<see cref="Perception.Full"/>: at
/// <see cref="Suspicious"/> an actor stops what it is doing to look or listen; at full it has found its target.
/// A heard noise or a heard call gives a place to look, never the target itself.
/// </summary>
public sealed record AwarenessRules(int Suspicious, int SightPerSecondAtRange, int SightPerSecondClose, int DecayPerSecond,
    int HeardNoise, int HeardCall, int SearchTicks);

/// <summary>How far each sound carries (STEALTH §7). A walk is quiet, a sprint is not, and a fight is heard across a clearing.</summary>
public sealed record NoiseRules(long WalkMm, long RunMm, long SprintMm, long SwingMm, long BlowMm, long CallMm);

/// <summary>
/// A sound made somewhere: a footfall, a blow, a howl. It carries as far as its radius, and no further. A call is a
/// creature's, and only its own kind answer it (<see cref="CallerKind"/>).
/// </summary>
public readonly record struct Noise(long XMm, long ZMm, long RadiusMm, bool Call = false, string? CallerKind = null);

public enum UnawareBehaviour
{
    /// <summary>Stays where it was placed, watching.</summary>
    Hold,

    /// <summary>Drifts around its home.</summary>
    Wander,

    /// <summary>Walks its spawner's route, end to end and back.</summary>
    Patrol,

    /// <summary>Lies still: no sight until woken, and hears less.</summary>
    Sleep,
}

/// <summary>
/// A behaviour role (SYSTEMS.md S-23's behaviour profile, as data): how one creature definition acts in one place.
/// The same wolf guards a den, hunts with the pack, sleeps, strays or roams; each role reads its senses the same way
/// and differs in what it does about them.
/// </summary>
public sealed record CreatureRole(string Id, UnawareBehaviour Unaware)
{
    /// <summary>It fights only while the target is this near its home, then turns back; 0 leaves the leash alone to bound a chase.</summary>
    public long TerritoryMm { get; init; }

    public long WanderMm { get; init; }

    /// <summary>It howls when it first finds a target, and packmates in earshot come.</summary>
    public bool CallsForHelp { get; init; }

    public bool AnswersCalls { get; init; }

    /// <summary>A wary role keeps this far from its target, closing to strike only inside <see cref="StrikeWithinMm"/> or once wounded.</summary>
    public long KeepDistanceMm { get; init; }

    public long StrikeWithinMm { get; init; }

    /// <summary>Below this share of its health it runs (STEALTH §16: self-preservation); 0 never runs.</summary>
    public int FleeBelowPercent { get; init; }

    /// <summary>Asleep, it hears this share of what it would awake.</summary>
    public int SleepHearingPercent { get; init; } = 100;

    /// <summary>A pack hunter closes from the side, this far off the straight line.</summary>
    public long FlankMm { get; init; }

    /// <summary>An ambusher goes straight for a sound it feels inside its territory, rather than stopping to look.</summary>
    public bool PounceOnNoise { get; init; }
}

public static class Perception
{
    public const int Full = 100;

    /// <summary>
    /// STEALTH §2 vision: the target is within sight range, inside the field of view around the facing, and no wall
    /// stands between. Returns the distance when seen, or null.
    /// </summary>
    public static double? Sees(Body eye, Senses senses, long targetXMm, long targetZMm, IEnumerable<Blocker> walls)
    {
        double dx = targetXMm - eye.XMm, dz = targetZMm - eye.ZMm;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance > senses.SightMm)
            return null;
        if (distance > 1)
        {
            double facing = eye.FacingMdeg / 1000.0 * Math.PI / 180;
            double cos = (dx * Math.Sin(facing) + dz * Math.Cos(facing)) / distance;
            if (cos < Math.Cos(senses.FieldOfViewMdeg / 2000.0 * Math.PI / 180))
                return null;
        }
        foreach (var wall in walls)
        {
            if (wall.Crosses(eye.XMm, eye.ZMm, targetXMm, targetZMm))
                return null;
        }
        return distance;
    }

    /// <summary>STEALTH §2 sound: a noise reaches a listener inside both the noise's carry and the listener's hearing.</summary>
    public static bool Hears(Body ear, long hearingMm, Noise noise)
    {
        double dx = noise.XMm - ear.XMm, dz = noise.ZMm - ear.ZMm;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        return distance <= noise.RadiusMm && distance <= hearingMm;
    }

    /// <summary>
    /// One tick of sight: a target seen at the edge of range raises awareness slowly, one seen close raises it fast
    /// (STEALTH §3); nothing seen lets it fade.
    /// </summary>
    public static int Accrue(int awareness, double? seenAtMm, long sightMm, AwarenessRules rules, int tickMs)
    {
        if (seenAtMm is not { } distance)
            return Math.Max(0, awareness - Math.Max(1, rules.DecayPerSecond * tickMs / 1000));
        double closeness = 1 - Math.Clamp(distance / Math.Max(1, sightMm), 0, 1);
        double perSecond = rules.SightPerSecondAtRange + (rules.SightPerSecondClose - rules.SightPerSecondAtRange) * closeness;
        return Math.Min(Full, awareness + Math.Max(1, (int)Math.Round(perSecond * tickMs / 1000.0, MidpointRounding.AwayFromZero)));
    }
}
