// UNNAMED World - carrying a detector, and setting a ward across a passage (STEALTH.md 10, S7 of D23)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Throw something to make a noise somewhere else (STEALTH.md 3.2, 10): the distraction a ward can be beaten with, and every creature within
/// earshot turns to it. It disables no ward by itself - it crosses one first, which is the canon's own counter.
/// </summary>
public sealed record DistractCommand(EntityId Actor, long XMm, long ZMm) : GameCommand(Actor);

/// <summary>A carried detector read something (S7): the sense that found it, its key, and where it stood when it was found.</summary>
public sealed record DetectorContact(string Sense, string CreatureKey, long XMm, long ZMm, long Tick);

/// <summary>A ward was crossed (S7): which ward, and where.</summary>
public sealed record WardTripped(string WardKey, string Space, long XMm, long ZMm, long Tick)
{
    /// <summary>The world flag the ward raises, so a listener can set the alarm the creatures and the law read.</summary>
    public string AlarmFlag { get; init; } = "";
}

/// <summary>
/// The character's own devices (STEALTH.md 10), read from what they carry: a motion sensor or a thermal sensor reports what it can see, and a ward set
/// across a passage reports being crossed. The devices keep no state but a contact memory, because a sensor that re-reports the same body every tick is
/// a HUD that never stops flashing.
/// </summary>
internal sealed class DetectionSystem
{
    private const string MotionSensor = "item.tool.detector_motion";
    private const string ThermalSensor = "item.tool.detector_thermal";

    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly HashSet<string> _contacts = new(StringComparer.Ordinal);

    /// <summary>What the carried sensors read this tick: transient, and empty when nothing is carried.</summary>
    public ImmutableArray<DetectorContact> Contacts { get; private set; } = ImmutableArray<DetectorContact>.Empty;
    private readonly HashSet<string> _tripped = new(StringComparer.Ordinal);

    /// <summary>The wards a thrown noise has already crossed, by key: each is spent once, as a distraction is.</summary>
    private readonly HashSet<string> _distracted = new(StringComparer.Ordinal);

    /// <summary>A thrown lure landed: any ward within its own radius is crossed by the noise before the character is.</summary>
    public void Distract(SpaceId space, long xMm, long zMm)
    {
        foreach (var ward in _context.Setup.LayoutFor(space).Wards)
        {
            long dx = ward.XMm - xMm, dz = ward.ZMm - zMm;
            if (dx * dx + dz * dz <= ward.RadiusMm * ward.RadiusMm * 4)
                _distracted.Add(ward.Key);
        }
    }

    public DetectionSystem(SystemContext context, EntityId player)
    {
        _context = context;
        _player = player;
    }

    private RuntimeState State => _context.State;

    public void Tick(long tick)
    {
        bool motion = Carries(MotionSensor);
        bool thermal = Carries(ThermalSensor);
        if (!motion && !thermal)
        {
            _contacts.Clear();
            Contacts = ImmutableArray<DetectorContact>.Empty;
            return;
        }
        var reading = ImmutableArray.CreateBuilder<DetectorContact>();
        var lens = State.Body;
        var space = State.Space;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var creature in State.Creatures.Values)
        {
            if (!creature.Alive || creature.Site.Space != space)
                continue;
            var target = new DetectorTarget(space, creature.Body.XMm, creature.Body.ZMm, 1_700,
                Moving: creature.Awareness > 0 || creature.Mind == CreatureMind.Engaged, Prone: false, Warm: true,
                SolidMm: SolidBetween(space, lens, creature.Body));
            DetectionRead? read = null;
            if (motion)
            {
                var motionRead = DetectorRules.Motion(space, lens.XMm, lens.ZMm, lens.FacingMdeg, target);
                if (motionRead.Read)
                    read = motionRead;
            }
            if (read is null && thermal)
            {
                var thermalRead = DetectorRules.Thermal(space, lens.XMm, lens.ZMm, lens.FacingMdeg, target);
                if (thermalRead.Read)
                    read = thermalRead;
            }
            if (read is null)
                continue;
            seen.Add(creature.Key);
            reading.Add(new DetectorContact(read.Sense, creature.Key, creature.Body.XMm, creature.Body.ZMm, tick));
            if (_contacts.Add(creature.Key))
                _context.Events.Publish(reading[^1]);
        }
        _contacts.IntersectWith(seen);
        Contacts = reading.ToImmutable();

        foreach (var ward in _context.Setup.LayoutFor(space).Wards)
        {
            if (_tripped.Contains(ward.Key))
                continue;
            long dx = ward.XMm - lens.XMm;
            long dz = ward.ZMm - lens.ZMm;
            if (dx * dx + dz * dz > ward.RadiusMm * ward.RadiusMm)
                continue;
            _tripped.Add(ward.Key);
            if (WardRules.Alarms(toWardMm: 0, radiusMm: ward.RadiusMm, quieted: false, crossedByDistraction: _distracted.Contains(ward.Key),
                    hasRuneKey: State.Progression.Known.ContainsKey(ward.RuneKey)))
            {
                _context.Events.Publish(new WardTripped(ward.Key, space.ToString(), lens.XMm, lens.ZMm, tick));
                // The bell the creatures hear and the law counts: the alarm is a world fact, not a popup.
                // The bell is the event; the alarm flag is set by whoever reads it (the creatures' alarm and the law both subscribe), which keeps the
                // ward a piece of content rather than a second switch system. WardTripped carries the flag so a listener knows which bell rang.
            }
        }
    }

    /// <summary>Whether the character carries one of the given item definitions, equipped or in the pack.</summary>
    private bool Carries(string itemId) =>
        State.Inventory.Any(e => e.DefId == itemId);

    /// <summary>
    /// How much solid matter stands between the lens and a body: a cover in the way with the most sight blocked reads as its height, which is what
    /// canon means by a thermal eye seeing through a curtain and not through stone (STEALTH.md 10).
    /// </summary>
    private long SolidBetween(SpaceId space, Body lens, Body target)
    {
        long solid = 0;
        foreach (var cover in _context.Setup.LayoutFor(space).Covers)
        {
            if (cover.BlocksSightPercent < 100)
                continue;
            if (!OnSegment(lens, target, cover.XMm, cover.ZMm))
                continue;
            solid = Math.Max(solid, cover.HeightMm);
        }
        return solid;
    }

    /// <summary>Whether a point lies on the line between two bodies, inside half a metre of it - close enough to be in the way.</summary>
    private static bool OnSegment(Body from, Body to, long xMm, long zMm)
    {
        long ax = from.XMm;
        long az = from.ZMm;
        long bx = to.XMm;
        long bz = to.ZMm;
        long lengthSquared = (bx - ax) * (bx - ax) + (bz - az) * (bz - az);
        if (lengthSquared == 0)
            return false;
        double t = ((xMm - ax) * (double)(bx - ax) + (zMm - az) * (double)(bz - az)) / lengthSquared;
        if (t is < 0 or > 1)
            return false;
        double closestX = ax + t * (bx - ax);
        double closestZ = az + t * (bz - az);
        return (xMm - closestX) * (xMm - closestX) + (zMm - closestZ) * (zMm - closestZ) <= 1000 * 1000;
    }
}
