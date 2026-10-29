// UNNAMED World - tracking at run time: creatures leave spoor as they move, the ground ages it, and the character reads what is left
// (canon skill.tracking; STEALTH_DETECTION_AND_THREAT.md §2's tracks and scent, §4's last known position, §18's scent and wind)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// ── commands ────────────────────────────────────────────────────────────────

/// <summary>Read the ground at the character's feet: the tracking skill's read, reported as <see cref="SpoorRead"/>.</summary>
public sealed record ReadTrailCommand(EntityId Actor) : GameCommand(Actor);

/// <summary>Read the ground here and follow it. The follow keeps the character's step at a walk's while it lasts (Walk Their Road).</summary>
public sealed record FollowTrailCommand(EntityId Actor) : GameCommand(Actor);

/// <summary>Stop following: the character's step is their own again.</summary>
public sealed record StopFollowingCommand(EntityId Actor) : GameCommand(Actor);

// ── events ──────────────────────────────────────────────────────────────────

/// <summary>
/// What the ground said. <see cref="Readable"/> is whether there was anything to follow at all; bearing, age and count are Read the
/// Ground's "which way, how long ago, how many"; and <see cref="Uncertain"/> is the honest doubt that only What the Trail Refuses to Say
/// admits, worded in <see cref="Reason"/>.
/// </summary>
public sealed record SpoorRead(bool Readable, int BearingMdeg, long AgeTicks, int Count, bool Uncertain, string Reason, long Tick);

/// <summary>The character is following a trail now: which way it goes, and from this tick.</summary>
public sealed record TrailFollowBegan(int BearingMdeg, long Tick);

/// <summary>The follow is over, in the words the HUD shows ("You have stopped following").</summary>
public sealed record TrailFollowEnded(string Reason, long Tick);

// ── the system ──────────────────────────────────────────────────────────────

/// <summary>
/// Owns the spoor itself, which is the one part of the world that is deliberately not saved: a mark is where an animal put its foot a
/// minute ago, and a trail that a load restored from last week would be a lie about the ground. Everything about a mark is derived from
/// the creature that made it and the tick it was made on, so the marks of a session are forgotten with the session and nothing else in
/// the world depends on them.
///
/// Two things read them. The character reads them - <see cref="ReadTrailCommand"/>, through the tracking skill's own rules
/// (<see cref="TrackingRules"/>, <see cref="Tracking.Read"/>) - and the creature awareness model hears the character differently while
/// they follow one: <see cref="FollowingStepMm"/> is what the CreatureSystem caps a footfall at, so a follow is a stealth approach and
/// never a reveal. No mark reveals a creature, moves one, or spawns one (STEALTH §1).
/// </summary>
internal sealed class SpoorSystem
{
    public const string Skill = "skill.tracking";

    /// <summary>How far from the reader a mark can be read: six paces, which is what one look at the ground covers.</summary>
    public const long ReadReachMm = 6_000;

    /// <summary>How many marks the ground holds at once: the oldest is forgotten, and a trail longer than this has left its beginning.</summary>
    public const int MarksHeld = 512;

    /// <summary>How long a following character has to stand still before the follow is over: two seconds.</summary>
    public const int StillTicks = 40;

    /// <summary>One read trains the skill this often at most, as a crouched walk trains stealth (finding 26).</summary>
    public const int PracticeTicks = 60;

    private readonly SystemContext _context;
    private readonly EntityId _player;
    private readonly WeatherSystem _weather;

    /// <summary>The marks on the ground, oldest first: a queue because only the tail of a trail is ever read.</summary>
    private readonly Queue<SpoorMark> _marks = new();

    /// <summary>Where each animal's trail was last marked, by creature key.</summary>
    private readonly Dictionary<string, (long XMm, long ZMm)> _trailOf = new(StringComparer.Ordinal);

    private long? _lastXMm;
    private long? _lastZMm;
    private long _stillSince = -1;
    private bool _following;
    private long _nextPractice;

    public SpoorSystem(SystemContext context, EntityId player, WeatherSystem weather)
    {
        _context = context;
        _player = player;
        _weather = weather;
    }

    private RuntimeState State => _context.State;
    private TrackingRules Rules => _context.Setup.Tracking;

    /// <summary>
    /// How far the character's step carries while they follow a trail, or null when they are not following one. The CreatureSystem asks
    /// for it every tick it makes the character's footfall, so the follow is heard through the awareness model that is already there.
    /// </summary>
    public long? FollowingStepMm() => _following
        ? Tracking.FollowingNoiseMm(_context.Setup.Combat.Noise, StealthLevel(), Tracking.Known(Rules, Reader(), Tracking.WalkTheirRoad))
        : null;

    public string? Handle(ReadTrailCommand command, long tick) => Read(command.Actor, tick, follow: false);

    public string? Handle(FollowTrailCommand command, long tick) => Read(command.Actor, tick, follow: true);

    public string? Handle(StopFollowingCommand command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        End("You have stopped following", tick);
        return null;
    }

    public void Tick(long tick)
    {
        Drop(tick);
        var body = State.Body;
        bool moved = body.XMm != _lastXMm || body.ZMm != _lastZMm;
        _lastXMm = body.XMm;
        _lastZMm = body.ZMm;
        if (!_following)
            return;
        if (State.PlayerCombat.Defeated)
        {
            End("You are down", tick);
            return;
        }
        if (moved)
            _stillSince = -1;
        else if (_stillSince < 0)
            _stillSince = tick;
        if (_stillSince >= 0 && tick - _stillSince >= StillTicks)
            End("You have stopped following", tick);
    }

    /// <summary>The read, and the follow that comes of it: canon's "which way, how long ago, how many".</summary>
    private string? Read(EntityId actor, long tick, bool follow)
    {
        if (actor != _player)
            return $"unknown actor {actor}";
        if (State.PlayerCombat.Defeated)
            return "dead";
        var body = State.Body;
        var site = new TrailSite(body.XMm, body.ZMm, _weather.WindFromMdeg(tick), GroundAt(body.XMm, body.ZMm), tick);
        var read = Tracking.Read(_marks.Where(mark => Within(mark, body)).ToList(), Reader(), site, Rules);
        _context.Events.Publish(new SpoorRead(read.Readable, read.BearingMdeg, read.AgeTicks, read.Count, read.Uncertain, read.Reason, tick));
        if (!read.Readable)
        {
            if (follow)
                return read.Reason.Length > 0 ? read.Reason : "There is nothing here to follow";
            return null;
        }
        Practice(read, tick);
        if (!follow)
            return null;
        // A follow needs a way to go: a trail the reader can see and not read is sign, and standing on it is not following it.
        if (!Tracking.Known(Rules, Reader(), Tracking.ReadTheGround))
            return read.Reason;
        _following = true;
        _stillSince = -1;
        _context.Events.Publish(new TrailFollowBegan(read.BearingMdeg, tick));
        return null;
    }

    private void End(string reason, long tick)
    {
        if (!_following)
            return;
        _following = false;
        _context.Events.Publish(new TrailFollowEnded(reason, tick));
    }

    /// <summary>
    /// The skill levels with use: a read the character could make sense of is practice, harder the older the trail it recovered. One
    /// every few seconds rather than one per command, so looking twice is not a way to train.
    /// </summary>
    private void Practice(TrailRead read, long tick)
    {
        if (tick < _nextPractice || !Tracking.Known(Rules, Reader(), Tracking.ReadTheGround))
            return;
        _nextPractice = tick + PracticeTicks;
        int difficulty = Math.Clamp(4 + (int)(read.AgeTicks * 16 / Math.Max(1, Rules.FadeTicks)), 4, 24);
        _context.Dispatch(new PracticeSkill(new SkillPractice(Skill, difficulty, PracticeOutcome.Success, tick)));
    }

    /// <summary>
    /// Every animal on its feet in the space the character is in leaves its marks as it goes: nothing in another space is worth holding,
    /// because nothing there can be read. Each animal's trail is marked apart, so three who walked the same ground read as three.
    /// </summary>
    private void Drop(long tick)
    {
        foreach (var creature in State.Creatures.Values)
        {
            if (!creature.Alive)
            {
                _trailOf.Remove(creature.Key);
                continue;
            }
            if (creature.Site.Space != State.Space)
                continue;
            var body = creature.Body;
            if (_trailOf.TryGetValue(creature.Key, out var last) && DistanceSquared(last.XMm, last.ZMm, body.XMm, body.ZMm) < Rules.MarkSpacingMm * Rules.MarkSpacingMm)
                continue;
            _trailOf[creature.Key] = (body.XMm, body.ZMm);
            Leave(creature, tick);
        }
    }

    /// <summary>
    /// What one animal left at one place: a mark on the ground where the ground keeps one, and its scent in the air either way, because
    /// the ground's failure to hold a print does not take the smell with it (canon's "age, water, stone" are the ground's limits, and the
    /// wind is not the ground's).
    /// </summary>
    private void Leave(CreatureState creature, long tick)
    {
        var body = creature.Body;
        var ground = GroundAt(body.XMm, body.ZMm);
        // The mark is addressed by where and when it was made, so the same animal crossing the same ground leaves the same sign.
        var channel = RngChannel.Open(State.World.WorldSeed, _context.CellOfSite(State.Space, body.XMm, body.ZMm), "tracking",
            $"{creature.Key}@{body.XMm}/{body.ZMm}");
        if (Rules.Holds(ground))
        {
            int draw = channel.Int(0, 0, 100);
            var kind = creature.Health < creature.ScaledMaxHealth && draw < 40 ? SpoorKind.Blood
                : draw < 8 ? SpoorKind.BrokenStem
                : draw < 12 ? SpoorKind.Droppings
                : SpoorKind.Print;
            Mark(new SpoorMark(kind, ground, body.XMm, body.ZMm, body.FacingMdeg, tick, Strength(ground)) { LeftBy = creature.Key });
        }
        Mark(new SpoorMark(SpoorKind.Scent, ground, body.XMm, body.ZMm, body.FacingMdeg, tick, 100) { LeftBy = creature.Key });
    }

    private void Mark(SpoorMark mark)
    {
        _marks.Enqueue(mark);
        while (_marks.Count > MarksHeld)
            _marks.Dequeue();
    }

    /// <summary>A print starts strong in mud and weak in grass, so the same age reads differently off different ground.</summary>
    private static int Strength(TrackGround ground) => ground switch
    {
        TrackGround.Mud => 100,
        TrackGround.Earth => 90,
        TrackGround.Snow => 80,
        _ => 70,
    };

    /// <summary>
    /// The ground under a point, from the terrain itself rather than a painted surface: a body in the water leaves nothing there, a bank
    /// keeps a print deeper than dry ground, and ground rising as steep as it is wide is rock (canon's water and stone).
    /// </summary>
    private TrackGround GroundAt(long xMm, long zMm)
    {
        var space = _context.Setup.SpaceFor(State.Space);
        if (space.WaterUnder(xMm, zMm) is not null)
            return TrackGround.Water;
        if (space.WaterAt(xMm, zMm) is not null)
            return TrackGround.Mud;
        return Steep(space, xMm, zMm) ? TrackGround.Stone : TrackGround.Earth;
    }

    private static bool Steep(WalkSpace space, long xMm, long zMm) =>
        Math.Abs(space.HeightAtMm(xMm + 1_000, zMm) - space.HeightAtMm(xMm, zMm)) >= 1_000
        || Math.Abs(space.HeightAtMm(xMm, zMm + 1_000) - space.HeightAtMm(xMm, zMm)) >= 1_000;

    /// <summary>The character's competences by the names canon's needs column uses: an attribute's value, or a skill's level.</summary>
    private Tracker Reader()
    {
        var levels = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var attribute in ProgressionKeys.Attributes)
            levels[ProgressionKeys.Key(attribute)] = ProgressionEngine.AttributeValue(State.Progression, attribute, _context.Setup.Progression);
        foreach (var (id, skill) in State.Progression.Skills)
            levels[id["skill.".Length..]] = skill.Level;
        return new Tracker(levels.ToImmutable());
    }

    private int StealthLevel() => State.Progression.Skills.GetValueOrDefault("skill.stealth")?.Level ?? 0;

    private static bool Within(SpoorMark mark, Body body) =>
        DistanceSquared(mark.XMm, mark.ZMm, body.XMm, body.ZMm) <= ReadReachMm * ReadReachMm;

    private static long DistanceSquared(long fromXMm, long fromZMm, long toXMm, long toZMm)
    {
        long dx = toXMm - fromXMm, dz = toZMm - fromZMm;
        return dx * dx + dz * dz;
    }
}
