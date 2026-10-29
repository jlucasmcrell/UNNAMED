// UNNAMED Domain - spoor: the marks a creature leaves in passing, and what the tracking skill reads off them (canon: skill.tracking;
// STEALTH_DETECTION_AND_THREAT.md §2's tracks, §4's last known position, §18's scent and wind)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;

namespace UNNAMED.Domain.Creatures;

/// <summary>What a creature left behind: the mark itself, or what hangs off it.</summary>
public enum SpoorKind
{
    /// <summary>A footfall, in ground that keeps one.</summary>
    Print,

    /// <summary>A stem trodden flat or a branch shouldered aside.</summary>
    BrokenStem,

    /// <summary>Drops of blood from a wounded animal - the mark a wound leaves long after it stops running.</summary>
    Blood,

    Droppings,

    /// <summary>Not on the ground at all: a trace in the air that the wind carries, and which only a reader upwind can find.</summary>
    Scent,
}

/// <summary>
/// The ground a mark lies on, as far as reading it goes: what keeps a print, and what does not. Stone and water are the two the canon
/// names as the limit of a read - not because nobody crossed them, but because the crossing leaves nothing to follow.
/// </summary>
public enum TrackGround
{
    Earth,
    Grass,
    Mud,
    Snow,
    Stone,
    Water,
}

/// <summary>
/// One mark a creature left, and where it was left (<c>spoor</c>, canon's tracking). A trail is a list of these: nothing else in the
/// game is told where the animal went - this is all a reader has.
/// </summary>
/// <param name="Kind">What was left.</param>
/// <param name="Ground">The ground it lies on, which decides whether it was there to be left at all.</param>
/// <param name="XMm">Where it lies.</param>
/// <param name="ZMm">Where it lies.</param>
/// <param name="BearingMdeg">Which way the one who left it was going, from +Z towards +X (the facing convention of the world).</param>
/// <param name="LeftTick">When it was left: every mark ages, and nothing reads it back to fresh.</param>
/// <param name="Strength">How strong it was when it was left, 1 to 100: a print in mud outlives one in long grass.</param>
public sealed record SpoorMark(SpoorKind Kind, TrackGround Ground, long XMm, long ZMm, int BearingMdeg, long LeftTick, int Strength)
{
    /// <summary>Who left it: the maker's own identity in this world (a creature's key), so that three who walked the same ground read as
    /// three rather than as one.</summary>
    public string LeftBy { get; init; } = "";

    /// <summary>How long ago it was left.</summary>
    public long Age(long tick) => Math.Max(0, tick - LeftTick);
}

/// <summary>
/// A tracking technique as canon gives it: its grade, its name, and the competences it needs. Canon's table is (grade, name, what it
/// does, needs, cost) and the needs are the gate - a reader whose competences meet them has the technique, with no teacher to find.
/// </summary>
/// <param name="Id">The content definition (<c>ability.tracking.*</c>).</param>
/// <param name="Name">The name canon prints in bold.</param>
/// <param name="Grade">N, P, A or E: novice, practised, adept, expert. The grade is a rung of the skill, never a cost of its own.</param>
/// <param name="Needs">
/// Level by competence name: a skill's own name (<c>tracking</c>, <c>survival</c>) or an attribute's (<c>insight</c>), as the canon
/// writes them.
/// </param>
public sealed record TrackingTechnique(string Id, string Name, string Grade, ImmutableSortedDictionary<string, int> Needs)
{
    /// <summary>Two techniques are the same when their needs agree entry by entry, not when they share a dictionary.</summary>
    public bool Equals(TrackingTechnique? other) =>
        other is not null && Id == other.Id && Name == other.Name && Grade == other.Grade && Needs.SequenceEqual(other.Needs);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Grade);
    public static readonly ImmutableArray<string> Grades = ImmutableArray.Create("N", "P", "A", "E");

    /// <summary>What is wrong with the entry, or null. A technique needs a name, a canon grade, and at least one competence.</summary>
    public string? Problem() =>
        Name.Trim().Length == 0 ? "has no name"
        : !Grades.Contains(Grade) ? $"grade '{Grade}' is none of {string.Join(", ", Grades)}"
        : Needs.Count == 0 ? "needs no competence, so it would come free"
        : Needs.Any(n => n.Key.Trim().Length == 0 || n.Value < 1) ? "names a competence with no level to it"
        : null;
}

/// <summary>
/// What the reader brings: their competence by the names canon's needs column uses, which is a skill's level or an attribute's value.
/// One map, because the needs are content and the caller - a character with skills and attributes - supplies both.
/// </summary>
public sealed record Tracker(ImmutableSortedDictionary<string, int> Levels)
{
    public int Level(string competence) => Levels.GetValueOrDefault(competence);

    /// <summary>Whether the reader's competences meet every leg this technique needs.</summary>
    public bool Meets(TrackingTechnique technique) => technique.Needs.All(need => Level(need.Key) >= need.Value);
}

/// <summary>
/// Where the reading happens: the reader's own feet, the wind over them, the ground they are reading, and the hour. The wind is the
/// weather's, because a scent is only found by whoever is downwind of it (STEALTH §2 scent, §18).
/// </summary>
/// <param name="XMm">The reader's position.</param>
/// <param name="ZMm">The reader's position.</param>
/// <param name="WindFromMdeg">The bearing the wind blows from, so a reader upwind of a scent is the one it reaches.</param>
/// <param name="Ground">The ground the reader is reading, from the terrain rather than from the marks.</param>
/// <param name="Tick">The hour of the read: what every mark's age is measured against.</param>
public sealed record TrailSite(long XMm, long ZMm, int WindFromMdeg, TrackGround Ground, long Tick);

/// <summary>
/// What the ground says. <see cref="Readable"/> is whether there was anything to follow at all; the three numbers are the canon's
/// "which way, how long ago, how many", and are only filled when the reader has Read the Ground; and <see cref="Uncertain"/> is the
/// honest doubt that What the Trail Refuses to Say lets a reader admit, with <see cref="Reason"/> naming which limit it ran into.
/// </summary>
public sealed record TrailRead(bool Readable, int BearingMdeg, long AgeTicks, int Count, bool Uncertain, string Reason)
{
    /// <summary>Nothing here: no mark, or nothing left of one.</summary>
    public static readonly TrailRead Nothing = new(false, 0, 0, 0, false, "");
}

/// <summary>
/// How reading is tuned (<c>config.tracking</c>): how long a mark lasts, how long a scent hangs, how wide the wind spreads it, how far
/// a creature walks between marks, and the technique catalogue itself. The defaults are canon's own table, so a world with no tracking
/// content still reads.
/// </summary>
public sealed record TrackingRules(
    long FreshTicks,
    long FadeTicks,
    long ScentTicks,
    int ScentConeMdeg,
    long MarkSpacingMm,
    ImmutableSortedDictionary<string, TrackingTechnique> Techniques)
{
    public static readonly TrackingRules Default = new(
        FreshTicks: 600,
        FadeTicks: 3_000,
        ScentTicks: 1_200,
        ScentConeMdeg: 90_000,
        MarkSpacingMm: 1_500,
        Canon());

    /// <summary>How long a mark of this kind lasts at all: a scent is the wind's and goes sooner than anything on the ground.</summary>
    public long Life(SpoorKind kind) => kind == SpoorKind.Scent ? ScentTicks : FadeTicks;

    /// <summary>How strong a mark still is: nothing at the end of its life, its left strength at the start, and a straight fade between.</summary>
    public int StrengthAt(SpoorMark mark, long tick)
    {
        long life = Math.Max(1, Life(mark.Kind));
        long age = mark.Age(tick);
        return age >= life ? 0 : (int)(mark.Strength * (life - age) / life);
    }

    /// <summary>
    /// Whether this ground keeps a mark at all. Water and stone do not, which is the limit a trail runs into and cannot be argued with
    /// (canon's "age, water, stone, and honest uncertainty").
    /// </summary>
    public bool Holds(TrackGround ground) => ground is TrackGround.Earth or TrackGround.Grass or TrackGround.Mud or TrackGround.Snow;

    public TrackingRules Validated()
    {
        if (FreshTicks < 1 || FadeTicks <= FreshTicks || ScentTicks < 1)
            throw new FormatException($"tracking needs 1 <= fresh ({FreshTicks}) < fade ({FadeTicks}), and a scent that lasts ({ScentTicks})");
        if (ScentConeMdeg is < 1 or > 360_000)
            throw new FormatException($"a scent cone of {ScentConeMdeg} millidegrees is not an arc");
        if (MarkSpacingMm < 1)
            throw new FormatException("a creature leaves a mark more often than never");
        foreach (var (id, technique) in Techniques)
        {
            if (technique.Problem() is { } problem)
                throw new FormatException($"{id} {problem}");
        }
        return this;
    }

    /// <summary>Canon's seven, at the needs the canon gives them.</summary>
    private static ImmutableSortedDictionary<string, TrackingTechnique> Canon() => new[]
    {
        Technique(Tracking.ReadTheGround, "Read the Ground", "N", ("tracking", 15), ("insight", 12)),
        Technique(Tracking.TrailSign, "Trail Sign", "N", ("tracking", 15), ("leadership", 15)),
        Technique(Tracking.FollowTheQuarry, "Follow the Quarry", "P", ("tracking", 30), ("survival", 30)),
        Technique(Tracking.PredatorsBearing, "Predator's Bearing", "P", ("tracking", 30), ("insight", 20)),
        Technique(Tracking.WalkTheirRoad, "Walk Their Road", "A", ("tracking", 45), ("stealth", 45)),
        Technique(Tracking.ReadTheWake, "Read the Wake", "A", ("tracking", 45), ("otherlore", 30)),
        Technique(Tracking.WhatTheTrailRefusesToSay, "What the Trail Refuses to Say", "E", ("tracking", 60), ("insight", 30)),
    }.ToImmutableSortedDictionary(t => t.Id, t => t, StringComparer.Ordinal);

    private static TrackingTechnique Technique(string id, string name, string grade, params (string Competence, int Level)[] needs) =>
        new(id, name, grade, needs.ToImmutableSortedDictionary(n => n.Competence, n => n.Level, StringComparer.Ordinal));
}

/// <summary>
/// The tracking skill, read off the spoor: canon's "a trail recovered that somebody did not want found", and the skill that makes the
/// world legible. Everything here is information. A read never moves anything, never spawns anything and never tells a creature where
/// the reader is - the only thing a read can do to the world is make the reader quieter by walking where the quarry walked.
/// </summary>
public static class Tracking
{
    public const string ReadTheGround = "ability.tracking.read_the_ground";
    public const string TrailSign = "ability.tracking.trail_sign";
    public const string FollowTheQuarry = "ability.tracking.follow_the_quarry";
    public const string PredatorsBearing = "ability.tracking.predators_bearing";
    public const string WalkTheirRoad = "ability.tracking.walk_their_road";
    public const string ReadTheWake = "ability.tracking.read_the_wake";
    public const string WhatTheTrailRefusesToSay = "ability.tracking.what_the_trail_refuses_to_say";

    /// <summary>Whether the reader's competences meet this technique's needs, and so has it.</summary>
    public static bool Known(TrackingRules rules, Tracker reader, string techniqueId) =>
        rules.Techniques.TryGetValue(techniqueId, out var technique) && reader.Meets(technique);

    /// <summary>
    /// Read the marks at a place (canon's grading of the skill, and Read the Ground's "which way, how long ago, how many"):
    /// <list type="bullet">
    /// <item>a mark has to still be there, and a scent only reaches a reader the wind is carrying it to;</item>
    /// <item>a trail no older than <see cref="TrackingRules.FreshTicks"/> is anyone's to find; an older one is cold, and only Follow the
    /// Quarry keeps hold of it;</item>
    /// <item>direction, age and count are Read the Ground's - below its needs the reader finds the sign and not what it means;</item>
    /// <item>a cold trail or ground that keeps nothing is a read with a hole in it, and only What the Trail Refuses to Say lets a reader
    /// say so instead of guessing.</item>
    /// </list>
    /// </summary>
    public static TrailRead Read(IReadOnlyList<SpoorMark> marks, Tracker reader, TrailSite site, TrackingRules rules)
    {
        bool honest = Known(rules, reader, WhatTheTrailRefusesToSay);
        var held = marks.Where(mark => rules.StrengthAt(mark, site.Tick) > 0 && Reaches(site, mark, rules)).ToList();
        if (held.Count == 0)
            return honest && !rules.Holds(site.Ground)
                ? new TrailRead(false, 0, 0, 0, true, "The ground here keeps nothing")
                : TrailRead.Nothing;

        long age = held.Min(mark => mark.Age(site.Tick));
        bool cold = age > rules.FreshTicks;
        if (cold && !Known(rules, reader, FollowTheQuarry))
            return new TrailRead(false, 0, 0, 0, false, "The trail has gone cold");
        if (!Known(rules, reader, ReadTheGround))
            return new TrailRead(true, 0, 0, 0, false, "Sign, but not what it means");

        // Which way: the freshest marks are the way they went. How many: the trail's own makers, not its marks.
        int bearing = held.Where(mark => mark.Age(site.Tick) == age).Select(mark => mark.BearingMdeg).First();
        int count = held.Select(mark => mark.LeftBy).Distinct(StringComparer.Ordinal).Count();
        string limit = cold ? "Older than I can date"
            : !rules.Holds(site.Ground) ? "The ground here keeps nothing"
            : "";
        // The doubt is the technique's: without it the read comes back as sure as it looks, which is the whole danger canon names.
        bool uncertain = honest && limit.Length > 0;
        return new TrailRead(true, bearing, age, count, uncertain, uncertain ? limit : "");
    }

    /// <summary>
    /// Whether a mark reaches a reader at all. Only a scent is particular about it: the wind has to be carrying it - the mark upwind of
    /// the reader, inside the cone (<see cref="TrackingRules.ScentConeMdeg"/>) - because a scent read from the wrong side is nothing at
    /// all.
    /// </summary>
    public static bool Reaches(TrailSite site, SpoorMark mark, TrackingRules rules)
    {
        if (mark.Kind != SpoorKind.Scent)
            return true;
        int toMark = CombatRules.FacingTowards(site.XMm, site.ZMm, mark.XMm, mark.ZMm);
        int off = Math.Abs(toMark - site.WindFromMdeg) % 360_000;
        return Math.Min(off, 360_000 - off) <= rules.ScentConeMdeg / 2;
    }

    /// <summary>
    /// How far the reader's step carries while they follow a trail. Walk Their Road is what keeps a follow a walk: the reader sets their
    /// feet where the quarry's prints are, and the follow stays inside a walk's <c>noise_m</c> where an ordinary follow has to hurry and
    /// makes a run's. Both take the stealth skill's own reduction, so a follower who is hard to notice is quieter still.
    /// </summary>
    public static long FollowingNoiseMm(NoiseRules noise, int stealthLevel, bool walkTheirRoad) =>
        Math.Max(1, (long)Math.Round((walkTheirRoad ? noise.WalkMm : noise.RunMm) * StealthRules.NoiseFactor(stealthLevel),
            MidpointRounding.AwayFromZero));
}
