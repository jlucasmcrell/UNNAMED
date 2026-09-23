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
    public double SpeedMmPerSecond(Gait gait) => gait switch
    {
        Gait.Walk => BaseSpeedMmPerSecond * WalkPercent / 100.0,
        Gait.Sprint => BaseSpeedMmPerSecond * SprintPercent / 100.0,
        _ => BaseSpeedMmPerSecond,
    };
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
        IReadOnlyList<Blocker> dynamicBlockers, int dtMs)
    {
        double x = body.XMm, z = body.ZMm;
        double radius = rules.BodyRadiusMm;
        double length = Math.Sqrt((double)intent.DirXPermille * intent.DirXPermille + (double)intent.DirZPermille * intent.DirZPermille);
        if (length > 0 && dtMs > 0)
        {
            double deflection = Math.Min(length, MoveIntent.FullDeflection) / MoveIntent.FullDeflection;
            double travel = rules.SpeedMmPerSecond(intent.Gait) * dtMs / 1000.0 * deflection;
            double dx = intent.DirXPermille / length * travel;
            double dz = intent.DirZPermille / length * travel;
            // Sub-steps no longer than half the body radius, so a fast step cannot tunnel through a thin wall.
            int steps = Math.Max(1, (int)Math.Ceiling(travel / (radius / 2)));
            for (int s = 0; s < steps; s++)
                (x, z) = Resolve(x + dx / steps, z + dz / steps, radius, space, dynamicBlockers);
        }
        long qx = (long)Math.Round(x, MidpointRounding.AwayFromZero);
        long qz = (long)Math.Round(z, MidpointRounding.AwayFromZero);
        return new Body(qx, space.Terrain.HeightAtMm(qx, qz), qz, intent.FacingMdeg);
    }

    /// <summary>True when a body circle at the point overlaps nothing and lies inside the bounds.</summary>
    public static bool IsClear(long xMm, long zMm, long radiusMm, WalkSpace space, IReadOnlyList<Blocker> dynamicBlockers) =>
        xMm - radiusMm >= space.MinXMm && xMm + radiusMm <= space.MaxXMm
        && zMm - radiusMm >= space.MinZMm && zMm + radiusMm <= space.MaxZMm
        && space.Blockers.Concat(dynamicBlockers).All(b => b.Separation(xMm, zMm, radiusMm) is null);

    private static (double X, double Z) Resolve(double x, double z, double radius, WalkSpace space, IReadOnlyList<Blocker> dynamicBlockers)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            foreach (var blocker in space.Blockers)
                moved |= Push(blocker, ref x, ref z, radius);
            foreach (var blocker in dynamicBlockers)
                moved |= Push(blocker, ref x, ref z, radius);
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
