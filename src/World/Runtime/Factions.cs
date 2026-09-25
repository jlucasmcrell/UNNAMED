// UNNAMED World - factions and reputation (M7 design §5.5)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Factions;

namespace UNNAMED.World.Runtime;

/// <summary>
/// The factions as the running world uses them (M7 design §5.5): the definitions, the one ladder, and the act log's capacity. Built by
/// <c>FactionContent.Build</c>; relevance - which (kind, subject) pairs some faction reacts to - is fixed here, at boot.
/// </summary>
public sealed record FactionSetup
{
    private readonly ImmutableSortedDictionary<string, ImmutableArray<string>> _reactors;

    public FactionSetup(ImmutableSortedDictionary<string, FactionDefinition> factions, StandingLadder ladder, int logCapacity)
    {
        Factions = factions;
        Ladder = ladder;
        LogCapacity = logCapacity;
        _reactors = factions.Values
            .SelectMany(f => f.Reactions.Select(r => (Key: r.Kind + "|" + r.Subject, f.Id)))
            .GroupBy(p => p.Key, StringComparer.Ordinal)
            .ToImmutableSortedDictionary(g => g.Key, g => g.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray(),
                StringComparer.Ordinal);
    }

    /// <summary>No factions, the shipped ladder, and a log of 256 acts.</summary>
    public static FactionSetup Empty { get; } = new(ImmutableSortedDictionary.Create<string, FactionDefinition>(StringComparer.Ordinal),
        StandingLadder.Default, 256);

    public ImmutableSortedDictionary<string, FactionDefinition> Factions { get; }
    public StandingLadder Ladder { get; }
    public int LogCapacity { get; }

    /// <summary>Whether some faction has a reaction row for the pair: an act outside this set is never recorded.</summary>
    public bool IsRelevant(string kind, string subject) => _reactors.ContainsKey(kind + "|" + subject);

    /// <summary>The factions that react to the pair, in ordinal ID order.</summary>
    public ImmutableArray<string> ReactorsTo(string kind, string subject) =>
        _reactors.GetValueOrDefault(kind + "|" + subject, ImmutableArray<string>.Empty);
}

/// <summary>
/// Owns: <see cref="StateSlice.Factions"/> - the player's faction ledger, seeded from the saved player and captured with them. In E2 it
/// only claims the slice; acts, reports and standing arrive in E3. It has no tick and mints nothing.
/// </summary>
internal sealed class FactionSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public FactionSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }
}
