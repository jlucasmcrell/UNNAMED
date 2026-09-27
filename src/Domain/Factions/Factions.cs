// UNNAMED Domain - factions and reputation: what the player's ledger holds (M7 design §5.1)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Factions;

/// <summary>The kinds of act a faction can learn of, and those named but not built (the <c>ObjectiveTypes.NotBuilt</c> pattern).</summary>
public static class ActKinds
{
    /// <summary>A creature killed; the subject is its definition ID.</summary>
    public const string CreatureKilled = "creature_killed";

    /// <summary>A switch set; the subject is the <c>world.*</c> flag it set.</summary>
    public const string SwitchSet = "switch_set";

    public static readonly ImmutableArray<string> Built = ImmutableArray.Create(CreatureKilled, SwitchSet);

    /// <summary>Named and refused, each with what it waits for.</summary>
    public static readonly ImmutableSortedDictionary<string, string> NotBuilt = new Dictionary<string, string>
    {
        ["piece_placed"] = "a repeat rule: placement consumes materials, so a reaction would turn items into standing (E-7); M9",
        ["piece_destroyed"] = "a repeat rule (M9)",
        ["item_taken"] = "ownership (crime, Phase 3)",
        ["npc_harmed"] = "NPCs that can be harmed",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);
}

/// <summary>How a faction came to know of an act. M7 writes only <see cref="Reported"/>; the witnessed channel is M9's.</summary>
public static class KnowledgeSources
{
    public const string Witnessed = "witnessed";
    public const string Reported = "reported";
    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Witnessed, Reported);
}

/// <summary>Whether a faction knows who did an act. M7 writes only <see cref="Identified"/>.</summary>
public static class Identities
{
    public const string Unidentified = "unidentified";
    public const string Identified = "identified";
    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Unidentified, Identified);
}

/// <summary>How one faction regards another: a word, never a number, so no rule can do arithmetic between a relation and a standing.</summary>
public static class Attitudes
{
    public static readonly ImmutableArray<string> All = ImmutableArray.Create("close", "cordial", "indifferent", "strained", "opposed");
}

/// <summary>An act, as the world saw it: what, where (the player's body, in mm, and its cell) and when. The truth, not a belief.</summary>
public sealed record ActRecord(long Seq, string Kind, string Subject, string CellKey, long XMm, long ZMm, long Tick);

/// <summary>
/// What one faction knows of one act (a belief): by which channel and member it learned, whether it knows who did it, when the row last
/// changed, and the standing change the row caused, after the clamp (0 while unidentified). The key is <c>knower</c>, so a later
/// per-NPC row fits the same list.
/// </summary>
public sealed record FactionKnowledge(string Knower, long Act, string Identity, string Source, string? Via, long Tick, int Delta);

/// <summary>The player's standing with one faction, in points. A standing of 0 is never stored.</summary>
public sealed record FactionStanding(string FactionId, int Points);

/// <summary>
/// The player's faction ledger (M7 design §5.1): the acts recorded, what each faction knows of them, and the standing each holds. Saved
/// with the player (schema 15); <c>PlayerRecord</c> validates it. It compares by value and exposes no computed property.
/// </summary>
public sealed record FactionLedger(long NextActSeq, ImmutableArray<ActRecord> Acts, ImmutableArray<FactionKnowledge> Knowledge,
    ImmutableArray<FactionStanding> Standing)
{
    public const int MinPoints = -1000;
    public const int MaxPoints = 1000;

    /// <summary>The lowest standing an ordinary act can bring; below it only a later rule can reach (PROGRESSION §10).</summary>
    public const int OrdinaryFloor = -999;

    /// <summary>No act recorded, no knowledge, no standing: every faction neutral.</summary>
    public static FactionLedger Empty { get; } = new(1, ImmutableArray<ActRecord>.Empty, ImmutableArray<FactionKnowledge>.Empty,
        ImmutableArray<FactionStanding>.Empty);

    /// <summary>The ledger with <paramref name="act"/> appended; the next act takes the sequence after it.</summary>
    public FactionLedger WithAct(ActRecord act) => this with { Acts = Acts.Add(act), NextActSeq = act.Seq + 1 };

    /// <summary>The ledger with the (knower, act) row inserted or replaced, in canonical order: knower ordinal, then act.</summary>
    public FactionLedger WithKnowledge(FactionKnowledge row) => this with
    {
        Knowledge = Knowledge.Where(k => !(string.Equals(k.Knower, row.Knower, StringComparison.Ordinal) && k.Act == row.Act))
            .Append(row)
            .OrderBy(k => k.Knower, StringComparer.Ordinal).ThenBy(k => k.Act)
            .ToImmutableArray(),
    };

    /// <summary>The ledger with a faction's points set; 0 removes the row. Rows stay sorted by faction ID, ordinally.</summary>
    public FactionLedger WithPoints(string factionId, int points) => this with
    {
        Standing = Standing.Where(s => !string.Equals(s.FactionId, factionId, StringComparison.Ordinal))
            .Concat(points == 0 ? Array.Empty<FactionStanding>() : new[] { new FactionStanding(factionId, points) })
            .OrderBy(s => s.FactionId, StringComparer.Ordinal)
            .ToImmutableArray(),
    };

    /// <summary>The ledger without an act and what any faction knew of it. Standing is untouched; the sequence never goes back.</summary>
    public FactionLedger WithoutAct(long seq) => this with
    {
        Acts = Acts.Where(a => a.Seq != seq).ToImmutableArray(),
        Knowledge = Knowledge.Where(k => k.Act != seq).ToImmutableArray(),
    };

    public bool Equals(FactionLedger? other) =>
        other is not null && NextActSeq == other.NextActSeq && Acts.SequenceEqual(other.Acts) && Knowledge.SequenceEqual(other.Knowledge)
        && Standing.SequenceEqual(other.Standing);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(NextActSeq);
        foreach (var act in Acts)
            hash.Add(act);
        foreach (var row in Knowledge)
            hash.Add(row);
        foreach (var row in Standing)
            hash.Add(row);
        return hash.ToHashCode();
    }
}

/// <summary>A faction's reaction to one act: a kind, one exact subject, and the standing change it brings once learned.</summary>
public sealed record Reaction(string Kind, string Subject, int Delta);

/// <summary>A static, directional relation to another faction, as an attitude word. No rule reads it; views show it.</summary>
public sealed record Relation(string FactionId, string Attitude);

/// <summary>
/// A faction as content (M7 design §5.1): its name, its seat (no rule reads it; lint FAC-M1 and the faction view do), what it reacts to,
/// and how it regards the others. Members are the NPCs whose definition names it.
/// </summary>
public sealed record FactionDefinition(string Id, string Name, string SeatLocationId, ImmutableArray<Reaction> Reactions,
    ImmutableArray<Relation> Relations);

/// <summary>One rung of the standing ladder: a key, its level, and the least points it holds.</summary>
public sealed record StandingTier(string Key, int Level, int MinPoints);

/// <summary>
/// PROGRESSION §10's ladder: eleven tiers on points [-1000, 1000], with an ordinary floor of -999. A tier is derived from points when
/// read, never stored.
/// </summary>
public sealed record StandingLadder(ImmutableArray<StandingTier> Tiers, int MinPoints, int MaxPoints, int OrdinaryFloor)
{
    /// <summary>PROGRESSION §10's keys and levels, top down. FAC001 pins <c>config.factions</c> to exactly these.</summary>
    public static readonly ImmutableArray<(string Key, int Level)> Keys = ImmutableArray.Create(("exalted", 5), ("allied", 4),
        ("honoured", 3), ("trusted", 2), ("accepted", 1), ("neutral", 0), ("wary", -1), ("disliked", -2), ("despised", -3),
        ("outcast", -4), ("anathema", -5));

    /// <summary>The shipped numbers (M7 design §5.4.1).</summary>
    public static StandingLadder Default { get; } = new(ImmutableArray.Create(
            new StandingTier("exalted", 5, 1000), new StandingTier("allied", 4, 700), new StandingTier("honoured", 3, 450),
            new StandingTier("trusted", 2, 250), new StandingTier("accepted", 1, 100), new StandingTier("neutral", 0, -99),
            new StandingTier("wary", -1, -249), new StandingTier("disliked", -2, -449), new StandingTier("despised", -3, -699),
            new StandingTier("outcast", -4, -999), new StandingTier("anathema", -5, -1000)),
        FactionLedger.MinPoints, FactionLedger.MaxPoints, FactionLedger.OrdinaryFloor);

    /// <summary>A tier key's level.</summary>
    /// <exception cref="FormatException">The key is not one of the eleven.</exception>
    public static int LevelOf(string tierKey)
    {
        foreach (var (key, level) in Keys)
        {
            if (string.Equals(key, tierKey, StringComparison.Ordinal))
                return level;
        }
        throw new FormatException($"'{tierKey}' is not a standing tier (one of {string.Join(", ", Keys.Select(k => k.Key))})");
    }

    /// <summary>The first tier, top down, whose least points are at or below <paramref name="points"/>. Not <c>TierOf</c>: that is the simulation tier.</summary>
    public StandingTier StandingTierOf(int points)
    {
        foreach (var tier in Tiers)
        {
            if (tier.MinPoints <= points)
                return tier;
        }
        return Tiers[^1];
    }
}

/// <summary>A gated stock row (M7 design §5.7.2): sold only while the player stands at <paramref name="MinLevel"/> or above with the faction.</summary>
public sealed record StandingRequirement(string FactionId, int MinLevel);

/// <summary>What one learning changed: the faction, the act, the channel, whether it upgraded an unidentified row, and the points before and after.</summary>
public sealed record Learned(string FactionId, long ActSeq, string Source, string? Via, string Identity, bool Upgraded, int From, int To);

/// <summary>The faction rules (M7 design §5.4): pure functions of the ledger and the content.</summary>
public static class FactionRules
{
    /// <summary>
    /// A faction learns of an act (§5.4.3). Without a reaction row it stores nothing (K7); an act already known as identified, or known
    /// unidentified and told unidentified again, changes nothing (K8); otherwise the row is written, and an identified row applies the
    /// reaction's delta once, clamped to the ordinary floor and the maximum.
    /// </summary>
    public static (FactionLedger Ledger, Learned? Change) Learn(FactionLedger ledger, FactionDefinition faction, long actSeq, string source,
        string via, string identity, long tick, StandingLadder ladder)
    {
        var act = ledger.Acts.Single(a => a.Seq == actSeq);
        var row = faction.Reactions.SingleOrDefault(r => r.Kind == act.Kind && r.Subject == act.Subject);
        if (row is null)
            return (ledger, null);
        var known = ledger.Knowledge.SingleOrDefault(k => k.Knower == faction.Id && k.Act == actSeq);
        bool upgrade = known is { Identity: Identities.Unidentified } && identity == Identities.Identified;
        if (known is not null && !upgrade)
            return (ledger, null);
        int from = PointsOf(ledger, faction.Id), to = from;
        if (identity == Identities.Identified)
            to = Math.Clamp(from + row.Delta, ladder.OrdinaryFloor, ladder.MaxPoints);
        var knowledge = new FactionKnowledge(faction.Id, actSeq, identity, source, via, tick, to - from);
        return (ledger.WithKnowledge(knowledge).WithPoints(faction.Id, to),
            new Learned(faction.Id, actSeq, source, via, identity, upgrade, from, to));
    }

    /// <summary>
    /// The act log held to its capacity (§5.4.5): while it is over, the lowest-sequence act goes, with what the factions knew of it.
    /// Standing never changes, and the next sequence never goes back.
    /// </summary>
    public static (FactionLedger Ledger, ImmutableArray<long> Evicted) Compact(FactionLedger ledger, int capacity)
    {
        var evicted = ImmutableArray.CreateBuilder<long>();
        while (ledger.Acts.Length > capacity)
        {
            long oldest = ledger.Acts[0].Seq;
            ledger = ledger.WithoutAct(oldest);
            evicted.Add(oldest);
        }
        return (ledger, evicted.ToImmutable());
    }

    /// <summary>A faction's points; 0 when the ledger holds no row for it.</summary>
    public static int PointsOf(FactionLedger ledger, string factionId) =>
        ledger.Standing.FirstOrDefault(s => string.Equals(s.FactionId, factionId, StringComparison.Ordinal))?.Points ?? 0;
}
