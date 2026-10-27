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
    /// How high above the feet a ledge a jump meets can be and still be climbed onto (the mantle). 0 is none. Only with
    /// <see cref="StepUpMm"/>: a mantled top is stood on like any other. BRIEF D6 raises this to 3 m - a wall, a cliff, a crate or a rock
    /// face is climbed, and taller faces are climbed where they carry handholds (see <see cref="MovementRules.ClimbPrefixes"/>).
    /// </summary>
    public long MantleMm { get; init; }

    /// <summary>
    /// A running body vaults a top this far above its feet without stopping (BRIEF D6): a fence, a cart, a low wall, fallen timber. 0 is
    /// none. A vault is a mantle taken on the move - no jump, no halt - so it is limited to what a hand can push off.
    /// </summary>
    public long VaultMm { get; init; }

    /// <summary>
    /// Structure id prefixes a body may climb beyond <see cref="MantleMm"/> - a face with handholds (a ladder, a rope, a scaffold, a rock
    /// face). Empty means only the mantle reach is climbed; the deliberate barriers are named in <see cref="UnclimbablePrefixes"/>.
    /// </summary>
    public ImmutableArray<string> ClimbPrefixes { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Structure id prefixes that are never climbed and never vaulted: the fold and the Other's barriers, sealed doors.</summary>
    public ImmutableArray<string> UnclimbablePrefixes { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>The body's height standing and crouched: what an overhang is measured against.</summary>
    public long StandHeightMm { get; init; } = 1_800;

    public long CrouchHeightMm { get; init; } = 1_150;

    /// <summary>A crouched body's speed, of the base, whatever the gait.</summary>
    public int CrouchPercent { get; init; } = 50;

    /// <summary>What wings let the body do; <see cref="WingRules.None"/> for a body without them.</summary>
    public WingRules Wings { get; init; } = WingRules.None;

    /// <summary>The rules a wing-assisted leap flies by: a higher arc, and further for the same time in the air.</summary>
    public MovementRules Leaping() => this with
    {
        JumpApexMm = JumpApexMm + Wings.LeapApexBonusMm,
        BaseSpeedMmPerSecond = BaseSpeedMmPerSecond * Wings.LeapDistancePercent / 100,
    };

    /// <summary>
    /// The horizontal speed the jump carries, before <see cref="JumpDistancePercent"/> (D6): 0 means the body's own gait speed, which is what
    /// a plain jump has always used. A people whose wings carry them sets it to its unscaled base speed, so a jump is never taken at its
    /// walking pace - and a Kal's flutter pushes it a little past a Veth's.
    /// </summary>
    public long JumpSpeedMmPerSecond { get; init; }

    /// <summary>A small distance bonus in the air, as a share of <see cref="JumpSpeedMmPerSecond"/>: the wing flutter (D6). Height is untouched.</summary>
    public int JumpDistancePercent { get; init; } = 100;

    /// <summary>
    /// The grade, in millimetres of rise per metre forward, at which ground stops being a walk and becomes a scramble (BRIEF D6 point 4).
    /// Ordinary hills stay below it; a mountain slope above it is still traversable, hand over hand and slower.
    /// </summary>
    public long ScrambleGradeMmPerM { get; init; } = 650;

    /// <summary>The share of the walking speed a scramble keeps while the grade is above <see cref="ScrambleGradeMmPerM"/>.</summary>
    public int ScramblePercent { get; init; } = 45;

    /// <summary>
    /// The grade above which the ground is a sheer face: it is not walked or scrambled, only climbed. Anything steeper is refused by
    /// <see cref="Step"/> unless the rules climb it.
    /// </summary>
    public long SheerGradeMmPerM { get; init; } = 1_500;

    /// <summary>From take-off to landing: the arc rises and falls in the same time.</summary>
    public int AirtimeMs => 2 * JumpRiseMs;

    /// <summary>The horizontal speed a jump carries: its own if it has one, else the gait's, never slower than the gait on the ground.</summary>
    public double JumpSpeed(Gait gait, Stance stance)
    {
        double ground = SpeedMmPerSecond(gait, stance);
        return JumpSpeedMmPerSecond > 0 ? Math.Max(ground, JumpSpeedMmPerSecond * JumpDistancePercent / 100.0) : ground;
    }

    public double SpeedMmPerSecond(Gait gait) => gait switch
    {
        Gait.Walk => BaseSpeedMmPerSecond * WalkPercent / 100.0,
        Gait.Sprint => BaseSpeedMmPerSecond * SprintPercent / 100.0,
        _ => BaseSpeedMmPerSecond,
    };

    public double SpeedMmPerSecond(Gait gait, Stance stance) =>
        stance == Stance.Crouched ? BaseSpeedMmPerSecond * CrouchPercent / 100.0 : SpeedMmPerSecond(gait);

    public long HeightMm(Stance stance) => stance == Stance.Crouched ? CrouchHeightMm : StandHeightMm;

    /// <summary>The absolute grade between two points, in millimetres of rise per metre of run: what a scramble and a sheer face are read from.</summary>
    public long GradeMmPerM(double fromX, double fromZ, double toX, double toZ, WalkSpace space)
    {
        double run = Math.Sqrt((toX - fromX) * (toX - fromX) + (toZ - fromZ) * (toZ - fromZ));
        if (run <= 0)
            return 0;
        long rise = space.HeightAtMm((long)Math.Round(toX, MidpointRounding.AwayFromZero), (long)Math.Round(toZ, MidpointRounding.AwayFromZero))
                    - space.HeightAtMm((long)Math.Round(fromX, MidpointRounding.AwayFromZero), (long)Math.Round(fromZ, MidpointRounding.AwayFromZero));
        return (long)Math.Round(Math.Abs(rise) * 1_000.0 / run);
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

/// <summary>The static walkable space of a region: its movement bounds, its terrain, and what stands on it.</summary>
public sealed record WalkSpace(long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, TerrainGrid Terrain, ImmutableArray<Blocker> Blockers)
{
    /// <summary>Authored prop tops: walking and prediction share the same bounded support height fields.</summary>
    public ImmutableArray<TerrainGrid> WalkableSurfaces { get; init; } = ImmutableArray<TerrainGrid>.Empty;

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
    /// onto or landed on. A body of unknown height (a creature, a person, a door) is never jumped or stood on.
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
            double pace = rules.JumpSpeed(intent.Gait, posture.Stance);
            // Scramble (BRIEF D6 point 4): across ground, the grade ahead decides the pace. A hill is walked, a mountain slope is
            // scrambled hand over hand and slower, and only a sheer face is refused - that is what the climb is for.
            if (!posture.Airborne && length > 0)
            {
                long grade = rules.GradeMmPerM(x, z, x + intent.DirXPermille / length * 1_000, z + intent.DirZPermille / length * 1_000, space);
                if (grade > rules.SheerGradeMmPerM && rules.MantleMm <= 0)
                    pace = 0;
                else if (grade > rules.ScrambleGradeMmPerM)
                    pace *= rules.ScramblePercent / 100.0;
            }
            double travel = pace * dtMs / 1000.0 * deflection;
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
        return (new Body(qx, space.HeightAtMm(qx, qz) + raised + rules.LiftMm(next.AirMs), qz, intent.FacingMdeg), next);
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
        // On the ground, a body that meets a climbable face within its reach climbs it: a fence or a cart at a run (a vault), a wall or a
        // rock face as a climb-up (BRIEF D6). The reach is the mantle's; the deliberate barriers are not climbable and are excluded there.
        else if (tops && !posture.Airborne && Mantle(fromX, fromZ, x, z, raised, 0, rules, space, radius) is { } climbed)
            return (climbed.X, climbed.Z, climbed.Raised, false);
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
            // A face with handholds - a ladder, a rope, a scaffold - is climbed at any height; everything else only within the reach.
            long reach = rules.ClimbPrefixes.Any(p => blocker.Id.StartsWith(p, StringComparison.Ordinal)) ? long.MaxValue : rules.MantleMm;
            if (top <= feet || top > ground + raised + reach || TopUnder(px, pz, long.MaxValue, space) != top)
                continue;
            long under = space.HeightAtMm((long)Math.Round(px, MidpointRounding.AwayFromZero), (long)Math.Round(pz, MidpointRounding.AwayFromZero));
            return (px, pz, top - under);
        }
        return null;
    }

    /// <summary>The highest structure top under a point no higher than <paramref name="limitMm"/> (absolute), else the ground there.</summary>
    private static long TopUnder(double x, double z, long limitMm, WalkSpace space)
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
