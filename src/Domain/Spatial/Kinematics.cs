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
/// What a winged body's wings are doing (Kal, RACES.md §2): folded along the spine, half-open for a wing-assisted leap, spread in a
/// glide, flared to brake, or folded again while the body drops to the ground.
/// </summary>
public enum WingState { Folded, Leap, Glide, Flare, Fall }

/// <summary>
/// How the body holds itself (the owner's M6 playtest): standing or crouched, and whether it is in the air - and for how long, since
/// the jump that took it there. A body on the ground has <see cref="AirMs"/> 0.
/// </summary>
public readonly record struct Posture(Stance Stance, bool Airborne, int AirMs)
{
    public static Posture Grounded => default;

    /// <summary>A winged body's wings (Kal). Glide, flare and fall mean the body is aloft, its feet at <see cref="FeetMm"/>.</summary>
    public WingState Wing { get; init; }

    /// <summary>Aloft, the feet's absolute height: a glide keeps its height over falling ground rather than following it.</summary>
    public long FeetMm { get; init; }

    public bool Aloft => Airborne && Wing is WingState.Glide or WingState.Flare or WingState.Fall;
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

    /// <summary>
    /// How high a structure's top the feet walk up onto (owner finding 50: small rocks are stepped over, low boulders jumped onto). 0, the
    /// default, keeps the planar rule: nothing is stood on. Above 0, a structure's top is ground: walked onto when it is no higher than
    /// this above the feet, landed on when a jump comes down onto it, and stepped off at its edge.
    /// </summary>
    public long StepUpMm { get; init; }

    /// <summary>
    /// How high above the feet a jump takes off from a ledge it meets can be and still be climbed onto (owner finding 50: the mantle). 0 is
    /// none. Only with <see cref="StepUpMm"/>: a mantled top is stood on like any other.
    /// </summary>
    public long MantleMm { get; init; }

    /// <summary>The body's height standing and crouched: what an overhang is measured against.</summary>
    public long StandHeightMm { get; init; } = 1_800;

    public long CrouchHeightMm { get; init; } = 1_150;

    /// <summary>A crouched body's speed, of the base, whatever the gait.</summary>
    public int CrouchPercent { get; init; } = 50;

    /// <summary>
    /// A climbing body's speed, of the base, whatever the gait: slower than the walk, because a ladder is climbed hand over hand
    /// (the climb loop covers two rungs - about 0.6 m - in 0.8 s, so 30% of the base is the clip's own cadence).
    /// </summary>
    public int ClimbPercent { get; init; } = 30;

    /// <summary>A swimming body's speed, of the walk's, whatever the gait asked: half of it, so a sprint in the water is no faster (D20).</summary>
    public int SwimPercent { get; init; } = 50;

    /// <summary>
    /// The steepest ground a body walks at its own pace, as a grade of rise over run in per cent: about 29 degrees. Up to it the ground costs
    /// nothing; above it the body is scrambling (D21, <c>HOOK.TRAV.SCRAMBLE_SLOPE</c>).
    /// </summary>
    public int WalkableGradePercent { get; init; } = 55;

    /// <summary>
    /// How much of its own pace a body keeps on ground steeper than the walkable grade: a steep face is climbed slowly and crested, never
    /// refused - the body has no grade it cannot pass, only grades it passes badly (D21).
    /// </summary>
    public int ScramblePercent { get; init; } = 45;

    /// <summary>
    /// How high above the feet a grapple anchor must be to be worth a throw (above a mantle's reach, D21) and the furthest the line carries, both
    /// in millimetres: a hook lower than the first is a step, and one beyond the second is out of reach.
    /// </summary>
    public long GrappleMinMm { get; init; } = 2_000;

    public long GrappleMaxMm { get; init; } = 10_000;

    /// <summary>How deep a swimming body floats: the waterline at its chest, so its head and its eye stay above the surface.</summary>
    public long SwimFloatMm { get; init; } = 1_200;

    /// <summary>What wings let the body do; <see cref="WingRules.None"/> for a body without them.</summary>
    public WingRules Wings { get; init; } = WingRules.None;

    /// <summary>The rules a wing-assisted leap flies by: a higher arc, and further for the same time in the air.</summary>
    public MovementRules Leaping() => this with
    {
        JumpApexMm = JumpApexMm + Wings.LeapApexBonusMm,
        BaseSpeedMmPerSecond = BaseSpeedMmPerSecond * Wings.LeapDistancePercent / 100,
    };

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

    /// <summary>How fast the feet take a climbable face, in millimetres a second, whatever the gait asked.</summary>
    public double ClimbSpeedMmPerSecond => BaseSpeedMmPerSecond * ClimbPercent / 100.0;

    /// <summary>
    /// What the body keeps of its pace on a rise: all of it on the walkable grade, falling in a straight line to <see cref="ScramblePercent"/>
    /// at twice that grade, and no further - however steep the face, the body still climbs it, at the scramble's pace (D21). A descent costs
    /// nothing: the cut is what a climb costs, not what a drop does.
    /// </summary>
    public double GradePace(long riseMm, long runMm)
    {
        if (riseMm <= 0 || runMm <= 0 || WalkableGradePercent <= 0)
            return 1;
        double grade = riseMm * 100.0 / runMm;
        if (grade <= WalkableGradePercent)
            return 1;
        double over = Math.Min(1.0, (grade - WalkableGradePercent) / WalkableGradePercent);
        return (100 - over * (100 - ScramblePercent)) / 100.0;
    }

    /// <summary>How far above the ground the feet are this long after take-off: a parabola, whole millimetres, 0 before and after.</summary>
    public long LiftMm(int airMs) =>
        airMs <= 0 || airMs >= AirtimeMs ? 0 : JumpApexMm * airMs * (AirtimeMs - airMs) / ((long)JumpRiseMs * JumpRiseMs);
}

/// <summary>
/// Wings (Kal, RACE_ABILITIES_PROPOSAL.md §3.2): a leap's extra height and reach, a glide's forward speed and sink, a flare's slower sink,
/// and how fast a body comes down with its wings folded. Zero speeds are a body that cannot glide.
/// </summary>
public sealed record WingRules
{
    public static WingRules None { get; } = new();

    public long LeapApexBonusMm { get; init; }
    public int LeapDistancePercent { get; init; } = 100;
    public long GlideSpeedMmPerSecond { get; init; }
    public long GlideSinkMmPerSecond { get; init; }
    public long FlareSinkMmPerSecond { get; init; }
    public long FallSinkMmPerSecond { get; init; } = 9_000;

    public bool CanGlide => GlideSpeedMmPerSecond > 0 && GlideSinkMmPerSecond > 0;
    public bool CanLeap => LeapApexBonusMm > 0 || LeapDistancePercent != 100;
}

/// <summary>An authored water body (D18): a surface at its own level along a line, or standing in one place.</summary>
/// <param name="Key">Its content key, for the dossier and the log.</param>
/// <param name="Kind">creek, stream, river, pond, lake or well - the word the area uses for it.</param>
/// <param name="LevelMm">The water's own surface height, which its channel is cut below.</param>
/// <param name="PointsMm">The line it runs along; one point is a spring, a pond or a well.</param>
/// <param name="WidthMm">How wide the surface is, across the line.</param>
public sealed record WaterSite(string Key, string Kind, long LevelMm, ImmutableArray<(long XMm, long ZMm)> PointsMm, long WidthMm)
{
    /// <summary>How near a body has to be for its water to be reached (a bank, not a swim).</summary>
    public const long ReachMm = 2_000;

    /// <summary>A water that names no depth of its own is a ford: ankle deep, waded, and never swum.</summary>
    public const long FordMm = 150;

    /// <summary>Deeper than this and a water is a body to float in; shallower, it is wading and the feet keep the ground.</summary>
    public const long SwimDepthMm = 900;

    /// <summary>How near the surface the ground under a swimmer comes before it climbs out and walks.</summary>
    public const long ClimbOutMm = 300;

    /// <summary>The loot table a line in this water rolls (<c>catch:</c>), or null for water nothing is caught in.</summary>
    public string? CatchTableId { get; init; }

    /// <summary>
    /// How deep the water's bed lies below its surface at its deepest, from its own <c>depth_m</c>, and <see cref="FordMm"/> when it names
    /// none. This is what makes a water a body rather than a bank: a ford keeps the feet on the ground, a millpond is swam.
    /// </summary>
    public long DepthMm { get; init; } = FordMm;

    /// <summary>
    /// Whether a body at this point stands inside the water's own footprint: within half its width of the line the water runs along. A pond
    /// is one point and a creek a run of them, and two points are a stretch of water, not a spot on it.
    /// </summary>
    public bool Covers(long xMm, long zMm) => NearestMm(xMm, zMm).DistanceMm <= WidthMm / 2;

    /// <summary>The nearest point of the water's own line to a body, and how far off it that is.</summary>
    public (long XMm, long ZMm, double DistanceMm) NearestMm(long xMm, long zMm)
    {
        double Spread(long px, long pz) => Math.Sqrt(Math.Pow(px - xMm, 2) + Math.Pow(pz - zMm, 2));
        var (fx, fz) = PointsMm[0];
        var best = (XMm: fx, ZMm: fz, DistanceMm: Spread(fx, fz));
        for (int i = 1; i < PointsMm.Length; i++)
        {
            var (ax, az) = PointsMm[i - 1];
            var (bx, bz) = PointsMm[i];
            double dx = bx - ax, dz = bz - az, square = dx * dx + dz * dz;
            double t = square == 0 ? 0 : Math.Clamp(((xMm - ax) * dx + (zMm - az) * dz) / square, 0, 1);
            long nx = (long)Math.Round(ax + dx * t, MidpointRounding.AwayFromZero), nz = (long)Math.Round(az + dz * t, MidpointRounding.AwayFromZero);
            double distance = Spread(nx, nz);
            if (distance < best.DistanceMm)
                best = (nx, nz, distance);
        }
        return best;
    }

    /// <summary>
    /// The bank a body in this water is put on: out from the water's own nearest point through the body, a body's radius past the water's
    /// edge, so it comes out on its feet and wading rather than in the water still.
    /// </summary>
    public (long XMm, long ZMm) ShoreFrom(long xMm, long zMm, long marginMm)
    {
        var (nx, nz, distance) = NearestMm(xMm, zMm);
        double dx = distance < 1 ? 1 : (xMm - nx) / distance, dz = distance < 1 ? 0 : (zMm - nz) / distance, outMm = WidthMm / 2.0 + marginMm;
        return ((long)Math.Round(nx + dx * outMm, MidpointRounding.AwayFromZero), (long)Math.Round(nz + dz * outMm, MidpointRounding.AwayFromZero));
    }
}

/// <summary>
/// A climbable face the body is against (D20, the ladder's half of <c>HOOK.TRAV.LADDER</c>): the structure, the height its top is at, and
/// the way the body faces it. Derived from where the body stands and which way it looks, never owned by the body.
/// </summary>
public readonly record struct ClimbFace(string Id, long TopMm, int FacingMdeg);

/// <summary>
/// A grapple anchor the body can hook (D21, <c>HOOK.TRAV.GRAPPLE</c>): the structure's id, the height its line is thrown to, and where the pull
/// puts the body. Derived from where the body stands and what it can reach, never owned by the body.
/// </summary>
public readonly record struct GrappleTarget(string Id, long HookMm, Body Landing);

/// <summary>The static walkable space of a region: its movement bounds, its terrain, and what stands on it.</summary>
public sealed record WalkSpace(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, TerrainGrid Terrain, ImmutableArray<Blocker> Blockers)
{
    /// <summary>Authored prop tops: walking and prediction share the same bounded support height fields.</summary>
    public ImmutableArray<TerrainGrid> WalkableSurfaces { get; init; } = ImmutableArray<TerrainGrid>.Empty;

    /// <summary>Authored water bodies (D18): a creek, pond or river surface at its own level.</summary>
    public ImmutableArray<WaterSite> Waters { get; init; } = ImmutableArray<WaterSite>.Empty;

    /// <summary>The nearest fresh water whose surface the body can reach from where it stands, if any.</summary>
    public WaterSite? WaterAt(long xMm, long zMm)
    {
        foreach (var water in Waters)
            foreach (var (px, pz) in water.PointsMm)
                if (Math.Pow(px - xMm, 2) + Math.Pow(pz - zMm, 2) <= (water.WidthMm / 2 + WaterSite.ReachMm) * (double)(water.WidthMm / 2 + WaterSite.ReachMm))
                    return water;
        return null;
    }

    /// <summary>
    /// The water a body standing at this point is in - the deepest of those whose footprint covers it - or null on dry land. Only where the
    /// body stands, never a bank's reach: a swim is in the water, and whether that water floats it is <see cref="WaterSite.DepthMm"/>'s.
    /// </summary>
    public WaterSite? WaterUnder(long xMm, long zMm)
    {
        WaterSite? deepest = null;
        foreach (var water in Waters)
            if (water.Covers(xMm, zMm) && (deepest is null || water.DepthMm > deepest.DepthMm))
                deepest = water;
        return deepest;
    }

    public long HeightAtMm(long xMm, long zMm)
    {
        long height = Terrain.HeightAtMm(xMm,zMm);
        foreach (var surface in WalkableSurfaces)
            if (xMm >= surface.OriginXMm && xMm <= surface.MaxXMm && zMm >= surface.OriginZMm && zMm <= surface.MaxZMm)
                height = Math.Max(height,surface.HeightAtMm(xMm,zMm));
        return height;
    }
}

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
    /// passed over - landing on one pushes the body off it, the same as walking into it, unless the rules stand on tops
    /// (<see cref="MovementRules.StepUpMm"/>): then the body's feet keep their height above the ground under it, and a top is walked up
    /// onto or landed on. A body of unknown height (a creature, a person, a door) is never jumped or stood on. In deep water it floats
    /// instead (<see cref="SwimWater"/>): at half a walk, its feet at the surface rather than on the ground, and out again as soon as the
    /// ground under it comes up to the bank.
    /// </summary>
    public static (Body Body, Posture Posture) Step(Body body, Posture posture, MoveIntent intent, MovementRules rules, WalkSpace space,
        IReadOnlyList<Blocker> dynamicBlockers, int dtMs)
    {
        if (posture.Aloft)
            return Soar(body, posture, intent, rules, space, dynamicBlockers, dtMs);
        if (posture.Airborne && posture.Wing == WingState.Leap)
            rules = rules.Leaping();
        double x = body.XMm, z = body.ZMm;
        double radius = rules.BodyRadiusMm;
        long height = rules.HeightMm(posture.Stance);
        int airFrom = posture.Airborne ? posture.AirMs : 0;
        int airTo = posture.Airborne ? Math.Min(airFrom + Math.Max(0, dtMs), rules.AirtimeMs) : 0;
        // Standing on a top: how far above the ground under it the feet stand, the jump's lift aside. 0 on the ground.
        bool tops = rules.StepUpMm > 0;
        long raised = tops ? Math.Max(0, body.YMm - rules.LiftMm(airFrom) - space.HeightAtMm(body.XMm, body.ZMm)) : 0;
        bool landed = false;
        double length = Math.Sqrt((double)intent.DirXPermille * intent.DirXPermille + (double)intent.DirZPermille * intent.DirZPermille);
        if (length > 0 && dtMs > 0)
        {
            double deflection = Math.Min(length, MoveIntent.FullDeflection) / MoveIntent.FullDeflection;
            // A body in deep water goes at half a walk whatever the gait asked: the sprint is refused in the water, and the swim's pace is
            // the walk's, not the gait's (D20). On dry ground a rising face is scrambled: the same pace as always on the walkable grade, the
            // scramble's own on anything steeper, so a steep bank costs time and is still crestable (D21).
            double speed = SwimWater(body.XMm, body.ZMm, space) is not null
                ? rules.SpeedMmPerSecond(Gait.Walk) * rules.SwimPercent / 100.0
                : rules.SpeedMmPerSecond(intent.Gait, posture.Stance) * Grade(body, posture, intent, rules, space);
            double travel = speed * dtMs / 1000.0 * deflection;
            double dx = intent.DirXPermille / length * travel;
            double dz = intent.DirZPermille / length * travel;
            // Sub-steps no longer than half the body radius, so a fast step cannot tunnel through a thin wall.
            int steps = Math.Max(1, (int)Math.Ceiling(travel / (radius / 2)));
            long before = rules.LiftMm(airFrom);
            for (int s = 0; s < steps && !landed; s++)
            {
                int air = airFrom + (airTo - airFrom) * (s + 1) / steps;
                (x, z, raised, landed) = SubStep(x, z, x + dx / steps, z + dz / steps, raised, before, air, posture, rules, space,
                    dynamicBlockers, radius, height);
                before = rules.LiftMm(air);
            }
        }
        else if (posture.Airborne)
        {
            (x, z, raised, landed) = SubStep(x, z, x, z, raised, rules.LiftMm(airFrom), airTo, posture, rules, space, dynamicBlockers, radius, height);
        }
        else if (tops)
        {
            (raised, _) = Settle(x, z, raised, 0, 0, 0, false, rules, space);
        }
        var next = posture.Airborne && airTo < rules.AirtimeMs && !landed ? posture with { AirMs = airTo } : posture with { Airborne = false, AirMs = 0, Wing = WingState.Folded };
        long qx = (long)Math.Round(x, MidpointRounding.AwayFromZero);
        long qz = (long)Math.Round(z, MidpointRounding.AwayFromZero);
        if (tops && !next.Airborne && !landed)
            (raised, _) = Settle(qx, qz, raised, 0, 0, 0, false, rules, space);
        // A swim is where the body is, not something it owns: floating, its feet are the surface's, not the ground's, so it sits lower in
        // the water than it stands. Entering and climbing out are the same test, on the point it has come to, so it needs no history.
        var ground = space.HeightAtMm(qx, qz);
        var standing = new Body(qx, ground + raised + rules.LiftMm(next.AirMs), qz, intent.FacingMdeg);
        return (next.Airborne || SwimWater(qx, qz, space) is not { } floats ? standing : standing with { YMm = floats.LevelMm - rules.SwimFloatMm }, next);
    }

    /// <summary>
    /// The body's own cut of its pace on the ground it is about to take (D21, the scramble): the rise a stride ahead of it over that stride, as
    /// a grade of the walk's own pace (<see cref="MovementRules.GradePace"/>). Purely a function of where it stands and where it is going, so
    /// presentation's prediction draws the same slowed step the simulation takes. A body in the air is not slowed by what is under it.
    /// </summary>
    private static double Grade(Body body, Posture posture, MoveIntent intent, MovementRules rules, WalkSpace space)
    {
        if (posture.Airborne)
            return 1;
        double length = Math.Sqrt((double)intent.DirXPermille * intent.DirXPermille + (double)intent.DirZPermille * intent.DirZPermille);
        if (length == 0)
            return 1;
        long aheadX = body.XMm + (long)Math.Round(intent.DirXPermille / length * rules.BodyRadiusMm, MidpointRounding.AwayFromZero);
        long aheadZ = body.ZMm + (long)Math.Round(intent.DirZPermille / length * rules.BodyRadiusMm, MidpointRounding.AwayFromZero);
        return rules.GradePace(space.HeightAtMm(aheadX, aheadZ) - space.HeightAtMm(body.XMm, body.ZMm), rules.BodyRadiusMm);
    }

    /// <summary>
    /// The water that floats a body standing at this point, or null (D20). It is the water the body stands inside the footprint of, whose
    /// bed is deep enough to be a body rather than a bank (<see cref="WaterSite.DepthMm"/>), with the ground under it well below the surface
    /// - and the ground coming up to within <see cref="WaterSite.ClimbOutMm"/> of the surface is the bank it climbs out onto. Where the body
    /// is decides it, purely: the simulation floats it and presentation's prediction draws the same float from the same function.
    /// </summary>
    public static WaterSite? SwimWater(long xMm, long zMm, WalkSpace space) =>
        space.WaterUnder(xMm, zMm) is { } water && water.DepthMm > WaterSite.SwimDepthMm
        && water.LevelMm - space.HeightAtMm(xMm, zMm) > WaterSite.ClimbOutMm
            ? water
            : null;

    /// <summary>
    /// The climbable face the body stands against, or null (D20: <c>HOOK.TRAV.LADDER</c>). Purely from where it is and which way it looks: the
    /// point a body's radius in front of it must lie inside a structure the climbable prefixes made climbable, whose top is higher than the feet
    /// by more than a step - a top within a step is a step up, not a climb - and no part of which is an overhang. The lowest such face wins, so
    /// the ladder in front is the one taken, not the crag behind it.
    /// </summary>
    public static ClimbFace? ClimbFaceAt(Body body, MovementRules rules, WalkSpace space)
    {
        double yaw = body.FacingMdeg * Math.PI / 180_000;
        double px = body.XMm + Math.Sin(yaw) * rules.BodyRadiusMm;
        double pz = body.ZMm + Math.Cos(yaw) * rules.BodyRadiusMm;
        ClimbFace? best = null;
        foreach (var blocker in space.Blockers)
        {
            if (!blocker.Climbable || blocker.HeightMm <= 0 || blocker.ClearanceMm > 0 || blocker.DistanceTo(px, pz) > 0)
                continue;
            var (cx, cz) = Footprints.Center(blocker);
            long top = space.HeightAtMm(cx, cz) + blocker.HeightMm;
            if (top - body.YMm <= rules.StepUpMm || best is { } held && held.TopMm <= top)
                continue;
            best = new ClimbFace(blocker.Id, top, body.FacingMdeg);
        }
        return best;
    }

    /// <summary>
    /// One tick of a climb: the feet rise at <see cref="MovementRules.ClimbSpeedMmPerSecond"/> up the face, no further than its top, with the body
    /// held where it took hold of the face - a climb is vertical, so there is no drifting across the wall - and facing into it.
    /// </summary>
    public static Body Climb(Body body, ClimbFace face, MovementRules rules, int dtMs)
    {
        long rise = (long)Math.Round(rules.ClimbSpeedMmPerSecond * Math.Max(0, dtMs) / 1000.0, MidpointRounding.AwayFromZero);
        return body with { YMm = Math.Min(body.YMm + rise, face.TopMm), FacingMdeg = face.FacingMdeg };
    }

    /// <summary>
    /// Whether the feet have come up to the face's top, within a step of it: the climb has taken the body as far as its chest can haul it and
    /// the mantle over the lip is the next thing.
    /// </summary>
    public static bool AtTop(Body body, ClimbFace face, MovementRules rules) => body.YMm + rules.StepUpMm >= face.TopMm;

    /// <summary>
    /// Where a body that has come up to a face's top stands: on the surface, a body's radius (and up to three) beyond the lip it came over, facing
    /// the way it climbed - whichever of those points is at the lip's own height, within a step. Null when there is nothing that high to stand on:
    /// a face whose top is a narrow rail with the ground falling away behind it has no mantle, and the body lets go where it hangs.
    /// </summary>
    public static Body? Mantled(Body body, ClimbFace face, MovementRules rules, WalkSpace space)
    {
        double yaw = face.FacingMdeg * Math.PI / 180_000;
        for (long ahead = rules.BodyRadiusMm; ahead <= rules.BodyRadiusMm * 3; ahead += rules.BodyRadiusMm)
        {
            long x = body.XMm + (long)Math.Round(Math.Sin(yaw) * ahead, MidpointRounding.AwayFromZero);
            long z = body.ZMm + (long)Math.Round(Math.Cos(yaw) * ahead, MidpointRounding.AwayFromZero);
            long top = TopUnder(x, z, face.TopMm + rules.StepUpMm, space);
            if (Math.Abs(top - face.TopMm) > rules.StepUpMm)
                continue;
            return new Body(x, top, z, face.FacingMdeg);
        }
        return null;
    }

    /// <summary>
    /// The grapple anchor the body can hook, or null (D21, <c>HOOK.TRAV.GRAPPLE</c>): a structure the grapple prefixes made an anchor, whose hook -
    /// its top - stands above the feet by more than a mantle and no further than the line carries, and which has somewhere to stand beyond it, on
    /// the pull's own side (<see cref="Pulled"/>). The lowest such hook wins, so the ring at the lip in front of the body is taken before the rim
    /// further up the cliff. Purely from where the body stands, like the climb's face.
    /// </summary>
    public static GrappleTarget? GrappleAt(Body body, MovementRules rules, WalkSpace space)
    {
        GrappleTarget? best = null;
        foreach (var blocker in space.Blockers)
        {
            if (!blocker.Grapple || blocker.HeightMm <= 0)
                continue;
            var (cx, cz) = Footprints.Center(blocker);
            long hook = space.HeightAtMm(cx, cz) + blocker.HeightMm;
            long rise = hook - body.YMm;
            if (rise < rules.GrappleMinMm || rise > rules.GrappleMaxMm
                || Math.Pow(cx - body.XMm, 2) + Math.Pow(cz - body.ZMm, 2) > (double)rules.GrappleMaxMm * rules.GrappleMaxMm
                || best is { } held && held.HookMm <= hook)
                continue;
            if (Pulled(body, blocker, rules, space) is not { } landing)
                continue;
            best = new GrappleTarget(blocker.Id, hook, landing);
        }
        return best;
    }

    /// <summary>
    /// Where a body pulled up a grapple line comes to rest: on the anchor's own ground, a body's radius past the anchor (and up to three) on the way
    /// the line came, so a hook at a lip puts the body over the lip onto the surface the anchor stands on - the mantle's placement, and a straight
    /// pull rather than a swing. Null when there is nothing at the anchor's height beyond it to stand on: a hook over a drop pulls the body onto
    /// nothing, and is no anchor at all.
    /// </summary>
    public static Body? Pulled(Body body, Blocker anchor, MovementRules rules, WalkSpace space)
    {
        var (cx, cz) = Footprints.Center(anchor);
        long ledge = space.HeightAtMm(cx, cz);
        // Its own base no higher than the feet: the hook is up there but the pull would put the body down again where it stands.
        if (ledge <= body.YMm)
            return null;
        double dx = cx - body.XMm, dz = cz - body.ZMm, length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 1)
            return null;
        for (long ahead = rules.BodyRadiusMm; ahead <= rules.BodyRadiusMm * 3; ahead += rules.BodyRadiusMm)
        {
            long x = cx + (long)Math.Round(dx / length * ahead, MidpointRounding.AwayFromZero);
            long z = cz + (long)Math.Round(dz / length * ahead, MidpointRounding.AwayFromZero);
            long ground = space.HeightAtMm(x, z);
            if (anchor.DistanceTo(x, z) > 0 && Math.Abs(ground - ledge) <= rules.StepUpMm)
                return new Body(x, ground, z, body.FacingMdeg);
        }
        return null;
    }

    /// <summary>
    /// A body aloft on its wings: it goes forward at the glide's speed (none in a flare, a walk in a fall), comes down at the wing state's
    /// sink from its own height rather than the ground's, passes over what is lower than its feet, and lands - wings folded, on its feet -
    /// where the ground or a top it may stand on comes up to meet it. There is no fall damage: the body only comes down.
    /// </summary>
    private static (Body Body, Posture Posture) Soar(Body body, Posture posture, MoveIntent intent, MovementRules rules, WalkSpace space,
        IReadOnlyList<Blocker> dynamicBlockers, int dtMs)
    {
        var wings = rules.Wings;
        long sink = posture.Wing switch
        {
            WingState.Glide => wings.GlideSinkMmPerSecond,
            WingState.Flare => wings.FlareSinkMmPerSecond,
            _ => wings.FallSinkMmPerSecond,
        };
        double speed = posture.Wing switch
        {
            WingState.Glide => wings.GlideSpeedMmPerSecond,
            WingState.Fall => rules.SpeedMmPerSecond(Gait.Walk),
            _ => 0,
        };
        double x = body.XMm, z = body.ZMm, radius = rules.BodyRadiusMm;
        long feet = posture.FeetMm - sink * Math.Max(0, dtMs) / 1000;
        long Floor(double px, double pz) => rules.StepUpMm > 0
            ? TopUnder(px, pz, feet, space)
            : space.HeightAtMm((long)Math.Round(px, MidpointRounding.AwayFromZero), (long)Math.Round(pz, MidpointRounding.AwayFromZero));
        double length = Math.Sqrt((double)intent.DirXPermille * intent.DirXPermille + (double)intent.DirZPermille * intent.DirZPermille);
        if (length > 0 && speed > 0 && dtMs > 0)
        {
            double travel = speed * dtMs / 1000.0 * Math.Min(length, MoveIntent.FullDeflection) / MoveIntent.FullDeflection;
            int steps = Math.Max(1, (int)Math.Ceiling(travel / (radius / 2)));
            for (int s = 0; s < steps; s++)
            {
                x += intent.DirXPermille / length * travel / steps;
                z += intent.DirZPermille / length * travel / steps;
                (x, z) = Resolve(x, z, radius, space, dynamicBlockers, Math.Max(0, feet - Floor(x, z)), rules.StandHeightMm);
            }
        }
        long qx = (long)Math.Round(x, MidpointRounding.AwayFromZero);
        long qz = (long)Math.Round(z, MidpointRounding.AwayFromZero);
        long floor = Floor(qx, qz);
        if (feet <= floor)
            return (new Body(qx, floor, qz, intent.FacingMdeg), Posture.Grounded with { Stance = posture.Stance });
        return (new Body(qx, feet, qz, intent.FacingMdeg), posture with { FeetMm = feet });
    }

    /// <summary>
    /// One sub-step to a wanted point: a falling jump that reaches a top under that point lands on it first, so the top's own footprint does
    /// not push it off; then collision; then, on the ground, the feet settle on what is under them.
    /// </summary>
    private static (double X, double Z, long Raised, bool Landed) SubStep(double fromX, double fromZ, double x, double z, long raised,
        long liftBefore, int airMs, Posture posture, MovementRules rules, WalkSpace space, IReadOnlyList<Blocker> dynamicBlockers,
        double radius, long height)
    {
        bool tops = rules.StepUpMm > 0, landed = false;
        long lift = rules.LiftMm(airMs);
        if (tops && posture.Airborne)
        {
            (raised, landed) = Settle(x, z, raised, liftBefore, lift, airMs, true, rules, space);
            if (landed)
                lift = 0;
            else if (Mantle(fromX, fromZ, x, z, raised, lift, rules, space, radius) is { } ledge)
                return (ledge.X, ledge.Z, ledge.Raised, true);
        }
        bool grounded = !posture.Airborne || landed;
        (x, z) = Resolve(x, z, radius, space, dynamicBlockers, raised + lift, height, grounded ? null : Tuck(posture, rules, lift),
            tops && grounded ? rules.StepUpMm : 0);
        if (tops && grounded)
            (raised, _) = Settle(x, z, raised, 0, 0, 0, false, rules, space);
        return (x, z, raised, landed);
    }

    /// <summary>
    /// Where the feet stand after a sub-step, as height above the ground under them, and whether a falling jump has landed. On the ground
    /// the feet stand on the highest top under the body's centre within a step of where they were, else the ground (stepping off an edge
    /// drops to it). In the air they land on a top once, coming down, they reach it.
    /// </summary>
    private static (long Raised, bool Landed) Settle(double x, double z, long raised, long liftBefore, long lift, int airMs, bool airborne,
        MovementRules rules, WalkSpace space)
    {
        long ground = space.HeightAtMm((long)Math.Round(x, MidpointRounding.AwayFromZero), (long)Math.Round(z, MidpointRounding.AwayFromZero));
        if (!airborne)
            return (TopUnder(x, z, ground + raised + rules.StepUpMm, space) - ground, false);
        long feet = ground + raised + lift;
        long floor = TopUnder(x, z, ground + raised + liftBefore, space);
        return airMs > rules.JumpRiseMs && feet <= floor ? (floor - ground, true) : (raised, false);
    }

    /// <summary>
    /// A jump that meets a ledge it cannot pass over climbs onto it when the top is within the mantle reach of the take-off feet and the
    /// body fits on it a radius in from the edge it met: the body stands there, on the top.
    /// </summary>
    private static (double X, double Z, long Raised)? Mantle(double fromX, double fromZ, double x, double z, long raised, long lift,
        MovementRules rules, WalkSpace space, double radius)
    {
        double dx = x - fromX, dz = z - fromZ, length = Math.Sqrt(dx * dx + dz * dz);
        if (rules.MantleMm <= 0 || length == 0)
            return null;
        double px = x + dx / length * radius, pz = z + dz / length * radius;
        long ground = space.HeightAtMm((long)Math.Round(x, MidpointRounding.AwayFromZero), (long)Math.Round(z, MidpointRounding.AwayFromZero));
        long feet = ground + raised + lift;
        foreach (var blocker in space.Blockers)
        {
            if (!blocker.Climbable || blocker.HeightMm <= 0 || blocker.ClearanceMm > 0 || blocker.DistanceTo(px, pz) > 0)
                continue;
            var (cx, cz) = Footprints.Center(blocker);
            long top = space.HeightAtMm(cx, cz) + blocker.HeightMm;
            if (top <= feet || top > ground + raised + rules.MantleMm || TopUnder(px, pz, long.MaxValue, space) != top)
                continue;
            long under = space.HeightAtMm((long)Math.Round(px, MidpointRounding.AwayFromZero), (long)Math.Round(pz, MidpointRounding.AwayFromZero));
            return (px, pz, top - under);
        }
        return null;
    }

    /// <summary>The highest structure top under a point no higher than <paramref name="limitMm"/> (absolute), else the ground there.</summary>
    public static long TopUnder(double x, double z, long limitMm, WalkSpace space)
    {
        long best = space.HeightAtMm((long)Math.Round(x, MidpointRounding.AwayFromZero), (long)Math.Round(z, MidpointRounding.AwayFromZero));
        foreach (var blocker in space.Blockers)
        {
            if (!blocker.Climbable || blocker.HeightMm <= 0 || blocker.ClearanceMm > 0 || blocker.DistanceTo(x, z) > 0)
                continue;
            var (cx, cz) = Footprints.Center(blocker);
            long top = space.HeightAtMm(cx, cz) + blocker.HeightMm;
            if (top <= limitMm && top > best)
                best = top;
        }
        return best;
    }

    /// <summary>
    /// Whether a blocker stands in a body's way: all of one whose height is unknown (0: a creature, a person); a structure only where its
    /// span, from its clearance to its top, meets the body's, from its feet to its head.
    /// </summary>
    public static bool Blocks(Blocker blocker, long liftMm, long bodyHeightMm) =>
        blocker.HeightMm <= 0 || (liftMm < blocker.HeightMm && liftMm + bodyHeightMm > blocker.ClearanceMm);

    /// <summary>True when the circle overlaps nothing and lies inside the bounds. A supplied body height permits clearance under roofs; zero retains the original full-column query.</summary>
    public static bool IsClear(long xMm, long zMm, long radiusMm, WalkSpace space, IReadOnlyList<Blocker> dynamicBlockers, long bodyHeightMm = 0) =>
        xMm - radiusMm >= space.MinXMm && xMm + radiusMm <= space.MaxXMm
        && zMm - radiusMm >= space.MinZMm && zMm + radiusMm <= space.MaxZMm
        && space.Blockers.Concat(dynamicBlockers).All(b => (bodyHeightMm > 0 && !Blocks(b, 0, bodyHeightMm)) || b.Separation(xMm, zMm, radiusMm) is null);

    /// <summary>Whether a crouched body here has room to stand: no overhang above it lower than a standing head.</summary>
    public static bool CanStand(Body body, MovementRules rules, WalkSpace space) =>
        !space.Blockers.Any(b => b.ClearanceMm > 0 && Blocks(b, 0, rules.StandHeightMm) && b.Separation(body.XMm, body.ZMm, rules.BodyRadiusMm) is not null);

    /// <summary>
    /// The tucked radius and the height below which it applies, while the feet are off the ground; nothing once they touch it - the step
    /// that lands is resolved at the full radius, or a landing against a low structure would end inside it (the Phase-1 technical audit,
    /// L-15).
    /// </summary>
    private static (double Radius, long Below)? Tuck(Posture posture, MovementRules rules, long liftMm) =>
        posture.Airborne && liftMm > 0 ? (rules.JumpTuckRadiusMm, rules.JumpApexMm) : null;

    private static (double X, double Z) Resolve(double x, double z, double radius, WalkSpace space, IReadOnlyList<Blocker> dynamicBlockers,
        long liftMm, long heightMm, (double Radius, long Below)? tuck = null, long stepMm = 0)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            foreach (var blocker in space.Blockers)
            {
                // A structure on the ground within a step of the feet is walked up onto, not into; an overhang is never stepped.
                if (Blocks(blocker, liftMm + (blocker.Climbable ? stepMm : 0), heightMm))
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
