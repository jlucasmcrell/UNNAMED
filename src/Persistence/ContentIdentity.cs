// UNNAMED Persistence - content identity and definition-ID resolution (PERSISTENCE.md §6.3, DATA_MODEL.md §2.1)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.World;

namespace UNNAMED.Persistence;

public enum IdOutcome
{
    /// <summary>Defined in the running content.</summary>
    Current,

    /// <summary>Renamed (<c>aliases</c>): rewritten to its current ID.</summary>
    Renamed,

    /// <summary>Removed with a replacement (<c>removed: old: new</c>): converted to the replacement.</summary>
    Replaced,

    /// <summary>Removed with no replacement (<c>removed: old: ~</c>): the referencing record is dropped, as reported loss.</summary>
    Discarded,

    /// <summary>Neither defined nor mapped: a hard error naming the ID. Never a guess.</summary>
    Unresolved,
}

public sealed record IdResolution(string Original, IdOutcome Outcome, string? CurrentId, string Path);

/// <summary>
/// The content the running build has: its exact identity (<c>content_version</c>, <c>content_hash</c>)
/// and the definition IDs it defines, with the append-only alias map from content/_aliases.yaml
/// (DATA_MODEL.md §2.1). A save referencing an ID resolves through it, or the load stops.
/// </summary>
public sealed class ContentIdentity
{
    public ContentIdentity(
        string version,
        string hash,
        IEnumerable<string> definitionIds,
        IReadOnlyDictionary<string, string>? aliases = null,
        IReadOnlyDictionary<string, string>? removed = null,
        IEnumerable<string>? discarded = null)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("content_version is required", nameof(version));
        if (!DigestText.IsSha256(hash))
            throw new ArgumentException($"content_hash must be 'sha256:<64 hex>', got '{hash}'", nameof(hash));
        Version = version;
        Hash = hash;
        DefinitionIds = definitionIds.ToImmutableHashSet(StringComparer.Ordinal);
        Aliases = (aliases ?? ImmutableDictionary<string, string>.Empty).ToImmutableSortedDictionary(StringComparer.Ordinal);
        Removed = (removed ?? ImmutableDictionary<string, string>.Empty).ToImmutableSortedDictionary(StringComparer.Ordinal);
        Discarded = (discarded ?? Array.Empty<string>()).ToImmutableSortedSet(StringComparer.Ordinal);
    }

    public string Version { get; }
    public string Hash { get; }
    public ImmutableHashSet<string> DefinitionIds { get; }
    public ImmutableSortedDictionary<string, string> Aliases { get; }
    public ImmutableSortedDictionary<string, string> Removed { get; }
    public ImmutableSortedSet<string> Discarded { get; }

    /// <summary>
    /// Resolve a stored ID. Renames and replacements may chain (an ID renamed twice); the walk stops at
    /// a defined ID, a discard, or a dead end. A cycle is a content error and resolves to nothing.
    /// </summary>
    public IdResolution Resolve(string id)
    {
        var outcome = IdOutcome.Current;
        var path = new List<string> { id };
        var seen = new HashSet<string>(StringComparer.Ordinal) { id };
        string current = id;
        while (!DefinitionIds.Contains(current))
        {
            string? next;
            if (Aliases.TryGetValue(current, out next))
            {
                if (outcome == IdOutcome.Current)
                    outcome = IdOutcome.Renamed;
            }
            else if (Removed.TryGetValue(current, out next))
            {
                outcome = IdOutcome.Replaced;
            }
            else if (Discarded.Contains(current))
            {
                return new IdResolution(id, IdOutcome.Discarded, null, string.Join(" -> ", path) + " -> (discarded)");
            }
            else
            {
                return new IdResolution(id, IdOutcome.Unresolved, null, string.Join(" -> ", path));
            }

            if (!seen.Add(next))
                return new IdResolution(id, IdOutcome.Unresolved, null, string.Join(" -> ", path.Append(next)) + " (cycle)");
            path.Add(next);
            current = next;
        }
        return new IdResolution(id, outcome, current, string.Join(" -> ", path));
    }
}
