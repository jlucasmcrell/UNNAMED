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
