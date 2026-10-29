// UNNAMED Domain - perception, and the behaviour roles creatures play (STEALTH_DETECTION_AND_THREAT.md §1-§5, §16;
// SYSTEMS.md S-23; M3d)
// No Godot references - pure C#

using UNNAMED.Domain.Spatial;

namespace UNNAMED.Domain.Creatures;

/// <summary>An actor's senses (DATA_MODEL.md §4.4 <c>perception</c>): how far it sees, across what arc, and how far it hears.</summary>
public sealed record Senses(long SightMm, long FieldOfViewMdeg, long HearingMm)
{
    /// <summary>
    /// It hunts by night (STEALTH §2.1, <c>night_sight</c>): the dark it stands in costs it 0.9 rather than 0.35. The wolf, the owl and the
    /// spider read a dark room; a person does not, which is the whole of a stealth player's night.
    /// </summary>
    public bool NightSight { get; init; }
}

/// <summary>
/// How awareness grows and fades (STEALTH §3: awareness is not binary). It runs 0..<see cref="Perception.Full"/>: at
/// <see cref="Suspicious"/> an actor stops what it is doing to look or listen; at full it has found its target.
/// A heard noise or a heard call gives a place to look, never the target itself.
/// </summary>
public sealed record AwarenessRules(int Suspicious, int SightPerSecondAtRange, int SightPerSecondClose, int DecayPerSecond,
    int HeardNoise, int HeardCall, int SearchTicks);

/// <summary>
/// How far each sound carries (STEALTH §7). A walk is quiet, a sprint is not, and a fight is heard across a clearing.
/// <see cref="KillMm"/> is a body going down where it stood - the sound a silent kill does make, and it is a small one, so a
/// neighbour across the room is told nothing (§7.7). <see cref="ImpactFleshMm"/> and <see cref="ImpactSolidMm"/> are the
/// arrow's arrival, heard at the target and never at the shooter (§7.8).
/// </summary>
public sealed record NoiseRules(long WalkMm, long RunMm, long SprintMm, long SwingMm, long BlowMm, long CallMm)
{
    /// <summary>A body falling where it was killed; small enough to carry no further than the room it fell in.</summary>
    public long KillMm { get; init; } = 2_000;

    /// <summary>A shot arriving in flesh.</summary>
    public long ImpactFleshMm { get; init; } = 1_500;

    /// <summary>A shot arriving in wood, stone or ground.</summary>
    public long ImpactSolidMm { get; init; } = 4_000;

    /// <summary>A weapon's own shot, where the weapon gives none of its own (STEALTH §7.8's bow twang).</summary>
    public long ShotMm { get; init; } = 6_000;

    /// <summary>Creeping: the one deliberate gait slower than a crouch-walk (§2.6).</summary>
    public long CreepMm { get; init; } = 500;

    /// <summary>Crouched, a body moves at this radius rather than a walk's (§2.6).</summary>
    public long CrouchMm { get; init; } = 1_500;

    /// <summary>Prone, crawling (§2.6).</summary>
    public long ProneMm { get; init; } = 250;
}

/// <summary>What made a sound (STEALTH §3.1): what a listener hears decides what it does about it.</summary>
public enum SoundSource
{
    /// <summary>A step, a landing, a body's own weight moving.</summary>
    Footfall,

    /// <summary>A weapon sweeping air: a swing, whether or not it lands.</summary>
    Swing,

    /// <summary>The weapon itself at release - a string's twang, a bolt's thunk - heard at the shooter.</summary>
    Shot,

    /// <summary>A shot arriving: into flesh, wood, stone, ground - heard where it landed, never at the shooter.</summary>
    Impact,

    /// <summary>A body going down for good: the last of it, heard at the place it fell.</summary>
    Kill,

    /// <summary>Something breaking: a lock, a pot, a thrown stone on a hard floor.</summary>
    Break,

    /// <summary>A voice: a howl, a shout, a bell.</summary>
    Call,

    /// <summary>A lure, a noisemaker, a device the player placed.</summary>
    Device,
}

/// <summary>
/// A sound made somewhere: a footfall, a blow, a howl. It carries as far as its radius, and no further. A call is a
/// creature's, and only its own kind answer it (<see cref="CallerKind"/>). <see cref="Source"/> says what was heard, so a listener
/// can tell a footfall from a crash (STEALTH §3.1) - it is presentation-and-AI detail, and a save taken between the sound and the
/// listener drops it rather than change the save format for one tick of state.
/// </summary>
public readonly record struct Noise(long XMm, long ZMm, long RadiusMm, bool Call = false, string? CallerKind = null)
{
    /// <summary>What made this sound.</summary>
    public SoundSource Source { get; init; } = SoundSource.Footfall;

    /// <summary>Every sound is a footfall unless it says otherwise, and a call is always a call.</summary>
    public SoundSource HeardAs => Call ? SoundSource.Call : Source;
}

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

    /// <summary>
    /// How far this role's call carries, when the kind's ordinary call (<see cref="NoiseRules.CallMm"/>) is not enough: a den leader's howl
    /// reaches the packs beyond its own (owner finding 29). Zero uses the ordinary call.
    /// </summary>
    public long CallMm { get; init; }

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

    /// <summary>
    /// It picks the weakest of the party it can reach, not the nearest: a predator that reads a fight finishes the hurt one first, and a
    /// party that lets one member get low meets the pack on that member (spec 43 ruling 5).
    /// </summary>
    public bool FocusWeakest { get; init; }

    /// <summary>
    /// An animal savaging it is a leash to whoever set the animal on: it goes past the animal for the handler, which is what makes a pet a
    /// shield and not a solution (spec 43 ruling 5: "enemies that react to pets, attacking handlers").
    /// </summary>
    public bool HuntsHandler { get; init; }

    /// <summary>An ambusher goes straight for a sound it feels inside its territory, rather than stopping to look.</summary>
    public bool PounceOnNoise { get; init; }

    /// <summary>
    /// It never starts a fight (wildlife, the great grazers): what it sees and hears leaves it be. A wound turns it on its attacker, or
    /// sends it running; one that answers calls joins a fight its own kind calls for, so a herd defends together.
    /// </summary>
    public bool Peaceful { get; init; }

    /// <summary>It bolts from anyone it has noticed this near (deer, hares, hens); 0 lets them come.</summary>
    public long FleeWithinMm { get; init; }

    /// <summary>When it rests at home asleep, unless something has its attention: by night, by day, or never.</summary>
    public RestTime Rests { get; init; }
}

public enum RestTime
{
    Never,
    Night,
    Day,
}

/// <summary>
/// The hours creatures keep: night runs from <see cref="NightFromTick"/> to <see cref="DayFromTick"/> ticks into each world day of
/// <see cref="TicksPerDay"/> ticks, wrapping past the day's end.
/// </summary>
public sealed record CreatureHours(long TicksPerDay, long NightFromTick, long DayFromTick)
{
    public bool IsNight(long tick)
    {
        long t = tick % TicksPerDay;
        return NightFromTick < DayFromTick ? t >= NightFromTick && t < DayFromTick : t >= NightFromTick || t < DayFromTick;
    }
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
    /// (STEALTH §3); nothing seen lets it fade. <paramref name="gain"/> scales how fast a sighting registers - the stealth skill's rank
    /// makes the character harder to notice (owner finding 26), never invisible: the number still rises, only slower.
    /// </summary>
    public static int Accrue(int awareness, double? seenAtMm, long sightMm, AwarenessRules rules, int tickMs, double gain = 1.0)
    {
        if (seenAtMm is not { } distance)
            return Math.Max(0, awareness - Math.Max(1, rules.DecayPerSecond * tickMs / 1000));
        double closeness = 1 - Math.Clamp(distance / Math.Max(1, sightMm), 0, 1);
        double perSecond = rules.SightPerSecondAtRange + (rules.SightPerSecondClose - rules.SightPerSecondAtRange) * closeness;
        return Math.Min(Full, awareness + Math.Max(1, (int)Math.Round(perSecond * gain * tickMs / 1000.0, MidpointRounding.AwayFromZero)));
    }
}
