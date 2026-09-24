// UNNAMED Domain - authoritative body movement (PROTOTYPE.md §6.2, D-11)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

public enum Gait
{
    Walk,
    Run,
    Sprint,
}

public enum Stance
{
    Standing,
    Crouched,
}

/// <summary>
/// How the body holds itself (the owner's M6 playtest): standing or crouched, and whether it is in the air - and for how long, since
/// the jump that took it there. A body on the ground has <see cref="AirMs"/> 0.
/// </summary>
public readonly record struct Posture(Stance Stance, bool Airborne, int AirMs)
{
    public static Posture Grounded => default;
}

/// <summary>
/// Where a body stands and which way it faces: integer millimetres, and millidegrees in [0, 360000).
/// Facing is measured from +Z towards +X.
/// </summary>
public sealed record Body(long XMm, long YMm, long ZMm, int FacingMdeg);

/// <summary>
/// What the player wants to do with their body this tick, in world space. The direction is in per-mille of
/// full stick deflection (a longer vector is clamped to full speed); presentation turns camera-relative
/// input into it. The same intent comes from every camera distance, first person included.
/// </summary>
public readonly record struct MoveIntent(int DirXPermille, int DirZPermille, Gait Gait, int FacingMdeg)
{
    public const int FullDeflection = 1000;
    public const int FullTurnMdeg = 360_000;

    public static MoveIntent Idle(int facingMdeg) => new(0, 0, Gait.Run, facingMdeg);

    public bool IsMoving => DirXPermille != 0 || DirZPermille != 0;

    /// <summary>Null when valid, otherwise why not. An invalid intent is rejected, never clamped into a valid one.</summary>
    public string? Problem() =>
        Math.Abs(DirXPermille) > FullDeflection || Math.Abs(DirZPermille) > FullDeflection
            ? $"direction ({DirXPermille}, {DirZPermille}) exceeds full deflection {FullDeflection}"
            : FacingMdeg is < 0 or >= FullTurnMdeg
                ? $"facing {FacingMdeg} mdeg is outside [0, {FullTurnMdeg})"
                : !Enum.IsDefined(Gait)
                    ? $"unknown gait {(int)Gait}"
                    : null;
}

/// <summary>Movement tuning from <c>config.base_speeds</c> (PROTOTYPE.md §4.4).</summary>
public sealed record MovementRules(long BaseSpeedMmPerSecond, int WalkPercent, int SprintPercent, long BodyRadiusMm, long InteractReachMm)
{
    /// <summary>How high a jump lifts the feet at its top, and how long it takes to get there (the owner's M6 playtest).</summary>
    public long JumpApexMm { get; init; } = 1_150;

    public int JumpRiseMs { get; init; } = 420;

    /// <summary>
    /// In the air, what could meet a structure lower than the jump's top is the tucked legs, narrower than the shoulders: the radius the
    /// body clears such a structure by. Walls, doors, and creatures and people meet the whole body.
    /// </summary>
    public long JumpTuckRadiusMm { get; init; } = 200;

    /// <summary>The body's height standing and crouched: what an overhang is measured against.</summary>
    public long StandHeightMm { get; init; } = 1_800;

    public long CrouchHeightMm { get; init; } = 1_150;

    /// <summary>A crouched body's speed, of the base, whatever the gait.</summary>
    public int CrouchPercent { get; init; } = 50;

    /// <summary>From take-off to landing: the arc rises and falls in the same time.</summary>
    public int AirtimeMs => 2 * JumpRiseMs;

    public double SpeedMmPerSecond(Gait gait) => gait switch
    {
        Gait.Walk => BaseSpeedMmPerSecond * WalkPercent / 100.0,
        Gait.Sprint => BaseSpeedMmPerSecond * SprintPercent / 100.0,
        _ => BaseSpeedMmPerSecond,
    };

    public double SpeedMmPerSecond(Gait gait, Stance stance) =>
        stance == Stance.Crouched ? BaseSpeedMmPerSecond * CrouchPercent / 100.0 : SpeedMmPerSecond(gait);

    public long HeightMm(Stance stance) => stance == Stance.Crouched ? CrouchHeightMm : StandHeightMm;

    /// <summary>How far above the ground the feet are this long after take-off: a parabola, whole millimetres, 0 before and after.</summary>
    public long LiftMm(int airMs) =>
        airMs <= 0 || airMs >= AirtimeMs ? 0 : JumpApexMm * airMs * (AirtimeMs - airMs) / ((long)JumpRiseMs * JumpRiseMs);
}

/// <summary>The static walkable space of a region: its movement bounds, its terrain, and what stands on it.</summary>
public sealed record WalkSpace(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, TerrainGrid Terrain, ImmutableArray<Blocker> Blockers);

/// <summary>
/// The one movement function. The movement system integrates the player with it every tick, and
/// presentation calls the same function to predict between ticks - prediction never writes state (D-11),
/// it only draws where the next authoritative tick will almost certainly put the body.
/// </summary>
public static class Kinematics
{
    /// <summary>
    /// Advance a body by <paramref name="dtMs"/> milliseconds of <paramref name="intent"/>. Collision pushes the
    /// body's footprint circle out of every blocker, which slides it along walls; the bounds stop it at the edge.
    /// Floating point only in sums, products, quotients and square roots, which IEEE 754 rounds exactly, then
    /// quantised to whole millimetres: the result is the same on every machine.
    /// </summary>
    public static Body Step(Body body, MoveIntent intent, MovementRules rules, WalkSpace space,
        IReadOnlyList<Blocker> dynamicBlockers, int dtMs) =>
        Step(body, Posture.Grounded, intent, rules, space, dynamicBlockers, dtMs).Body;

    /// <summary>
    /// The same, for a body with a posture (the owner's M6 playtest). A crouch slows it and lowers it under an overhang; a jump lifts
    /// the feet on an arc (<see cref="MovementRules.LiftMm"/>), and while they are higher than a structure's top the structure is
    /// passed over - landing on one pushes the body off it, the same as walking into it. Nothing is stood on, and a body of unknown
    /// height (a creature, a person, a door) is never jumped.
    /// </summary>
    public static (Body Body, Posture Posture) Step(Body body, Posture posture, MoveIntent intent, MovementRules rules, WalkSpace space,
        IReadOnlyList<Blocker> dynamicBlockers, int dtMs)
    {
        double x = body.XMm, z = body.ZMm;
        double radius = rules.BodyRadiusMm;
        long height = rules.HeightMm(posture.Stance);
        int airFrom = posture.Airborne ? posture.AirMs : 0;
        int airTo = posture.Airborne ? Math.Min(airFrom + Math.Max(0, dtMs), rules.AirtimeMs) : 0;
        double length = Math.Sqrt((double)intent.DirXPermille * intent.DirXPermille + (double)intent.DirZPermille * intent.DirZPermille);
        if (length > 0 && dtMs > 0)
        {
            double deflection = Math.Min(length, MoveIntent.FullDeflection) / MoveIntent.FullDeflection;
            double travel = rules.SpeedMmPerSecond(intent.Gait, posture.Stance) * dtMs / 1000.0 * deflection;
            double dx = intent.DirXPermille / length * travel;
            double dz = intent.DirZPermille / length * travel;
            // Sub-steps no longer than half the body radius, so a fast step cannot tunnel through a thin wall.
            int steps = Math.Max(1, (int)Math.Ceiling(travel / (radius / 2)));
            for (int s = 0; s < steps; s++)
            {
                long lift = rules.LiftMm(airFrom + (airTo - airFrom) * (s + 1) / steps);
                (x, z) = Resolve(x + dx / steps, z + dz / steps, radius, space, dynamicBlockers, lift, height, Tuck(posture, rules));
            }
        }
        else if (posture.Airborne)
        {
            (x, z) = Resolve(x, z, radius, space, dynamicBlockers, rules.LiftMm(airTo), height, Tuck(posture, rules));
        }
        var next = posture.Airborne && airTo < rules.AirtimeMs ? posture with { AirMs = airTo } : posture with { Airborne = false, AirMs = 0 };
        long qx = (long)Math.Round(x, MidpointRounding.AwayFromZero);
        long qz = (long)Math.Round(z, MidpointRounding.AwayFromZero);
        return (new Body(qx, space.Terrain.HeightAtMm(qx, qz) + rules.LiftMm(next.AirMs), qz, intent.FacingMdeg), next);
    }

    /// <summary>
    /// Whether a blocker stands in a body's way: all of one whose height is unknown (0: a creature, a person); a structure only where its
    /// span, from its clearance to its top, meets the body's, from its feet to its head.
    /// </summary>
    public static bool Blocks(Blocker blocker, long liftMm, long bodyHeightMm) =>
        blocker.HeightMm <= 0 || (liftMm < blocker.HeightMm && liftMm + bodyHeightMm > blocker.ClearanceMm);

    /// <summary>True when a body circle at the point overlaps nothing and lies inside the bounds.</summary>
    public static bool IsClear(long xMm, long zMm, long radiusMm, WalkSpace space, IReadOnlyList<Blocker> dynamicBlockers) =>
        xMm - radiusMm >= space.MinXMm && xMm + radiusMm <= space.MaxXMm
        && zMm - radiusMm >= space.MinZMm && zMm + radiusMm <= space.MaxZMm
        && space.Blockers.Concat(dynamicBlockers).All(b => b.Separation(xMm, zMm, radiusMm) is null);

    /// <summary>Whether a crouched body here has room to stand: no overhang above it lower than a standing head.</summary>
    public static bool CanStand(Body body, MovementRules rules, WalkSpace space) =>
        !space.Blockers.Any(b => b.ClearanceMm > 0 && Blocks(b, 0, rules.StandHeightMm) && b.Separation(body.XMm, body.ZMm, rules.BodyRadiusMm) is not null);

    /// <summary>The tucked radius and the height below which it applies, in the air; nothing on the ground.</summary>
    private static (double Radius, long Below)? Tuck(Posture posture, MovementRules rules) =>
        posture.Airborne ? (rules.JumpTuckRadiusMm, rules.JumpApexMm) : null;

    private static (double X, double Z) Resolve(double x, double z, double radius, WalkSpace space, IReadOnlyList<Blocker> dynamicBlockers,
        long liftMm, long heightMm, (double Radius, long Below)? tuck = null)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            foreach (var blocker in space.Blockers)
            {
                if (Blocks(blocker, liftMm, heightMm))
                    moved |= Push(blocker, ref x, ref z, tuck is { } t && blocker.HeightMm <= t.Below ? Math.Min(radius, t.Radius) : radius);
            }
            foreach (var blocker in dynamicBlockers)
            {
                if (Blocks(blocker, liftMm, heightMm))
                    moved |= Push(blocker, ref x, ref z, radius);
            }
            double cx = Math.Clamp(x, space.MinXMm + radius, space.MaxXMm - radius);
            double cz = Math.Clamp(z, space.MinZMm + radius, space.MaxZMm - radius);
            moved |= cx != x || cz != z;
            x = cx;
            z = cz;
            if (!moved)
                break;
        }
        return (x, z);
    }

    private static bool Push(Blocker blocker, ref double x, ref double z, double radius)
    {
        if (blocker.Separation(x, z, radius) is not { } push)
            return false;
        x += push.Dx;
        z += push.Dz;
        return true;
    }
}
