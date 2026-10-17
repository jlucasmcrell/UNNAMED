// UNNAMED Domain - portals between the exterior and an interior space (WORLD_ARCHITECTURE.md §8; M8 D10)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// One way into a space: an exterior anchor a body stands on, and the interior anchor it arrives at
/// inside. Several portals may lead to the same space - the Barrowworks has a formal entrance and a
/// drain mouth - and they are one interior identity with distinct outward ends.
/// </summary>
/// <remarks>
/// The two anchors are deliberately separate fields rather than one return vector shared by the space.
/// The M8 design calls this out by name: a player who entered by the drain mouth must leave to the drain
/// mouth, and a single stored "where you came in" vector reused across portals snaps them to the formal
/// entrance instead.
/// </remarks>
public sealed record SpacePortal(string Key, SpaceId Space, long ExteriorXMm, long ExteriorZMm, long InteriorXMm, long InteriorZMm)
{
    /// <summary>A reachable destination may advertise an offer before entry, in its own named place's cell.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SiteFlagRequirement? ReachableFlag { get; init; }

    /// <summary>Rejects a portal whose key or space cannot be stored, rather than writing one that cannot be read back.</summary>
    public SpacePortal Validate()
    {
        if (string.IsNullOrWhiteSpace(Key))
            throw new ArgumentException("A portal needs a key", nameof(Key));
        if (Space.IsExterior)
            throw new ArgumentException($"Portal '{Key}' leads to the exterior, which is not entered through a portal", nameof(Space));
        return this;
    }
}

/// <summary>
/// Where a body came into the space it is in, so leaving returns it to the anchor it actually used.
/// Empty means the body is in the exterior, which is entered through nothing.
/// </summary>
/// <remarks>
/// This is the whole of the return rule and it is one field: the portal, not a coordinate. Keeping the
/// coordinate here as well would let the two disagree, and the design is explicit that the stored entry
/// is not a reason to force a particular exit.
/// </remarks>
public readonly record struct SpaceTraversal(string PortalKey)
{
    /// <summary>In the exterior, or otherwise arrived through no portal.</summary>
    public static readonly SpaceTraversal None = default;

    public bool IsNone => string.IsNullOrEmpty(PortalKey);

    public override string ToString() => PortalKey ?? "";
}

/// <summary>
/// Every portal a region declares, and the rules that were worth putting in one place: which portals lead
/// where, that a space is entered at the anchor of the portal used, and that leaving goes back out of it.
/// </summary>
public sealed record SpacePortalSet(ImmutableArray<SpacePortal> Portals)
{
    public static readonly SpacePortalSet Empty = new(ImmutableArray<SpacePortal>.Empty);

    /// <summary>Two portals sharing a key is a content error: the key is what a save records.</summary>
    public SpacePortalSet Validate()
    {
        foreach (var portal in Portals)
            portal.Validate();
        var duplicate = Portals.GroupBy(p => p.Key, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Two portals share the key '{duplicate.Key}'", nameof(Portals));
        return this;
    }

    public SpacePortal? Find(string key) =>
        Portals.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));

    /// <summary>The portals leading into one space, in key order: its entrances.</summary>
    public ImmutableArray<SpacePortal> Into(SpaceId space) =>
        Portals.Where(p => p.Space == space).OrderBy(p => p.Key, StringComparer.Ordinal).ToImmutableArray();

    /// <summary>
    /// Entering <paramref name="portal"/>: the interior anchor to stand on and the traversal to remember.
    /// </summary>
    public (long XMm, long ZMm, SpaceTraversal Traversal) Enter(string portalKey)
    {
        var portal = Find(portalKey) ?? throw new KeyNotFoundException($"No portal '{portalKey}'");
        return (portal.InteriorXMm, portal.InteriorZMm, new SpaceTraversal(portal.Key));
    }

    /// <summary>
    /// The portal whose <em>interior</em> anchor a body at these metres is standing on, or null. Leaving is
    /// standing at a portal and using it, so this is what names the way out - and it answers with nothing rather
    /// than throwing when the body is not at one, because refusing is the caller's job.
    /// </summary>
    /// <remarks>
    /// Resolved by position and not from <see cref="SpaceTraversal"/>, which is why a save whose portal was
    /// renamed, or one loaded with the gate off, still leaves by a real door instead of crashing on a key that is
    /// no longer content.
    /// </remarks>
    public SpacePortal? AtInterior(long xMm, long zMm, long reachMm)
    {
        SpacePortal? nearest = null;
        double best = double.MaxValue;
        foreach (var portal in Portals)
        {
            double away = Math.Sqrt(Math.Pow(portal.InteriorXMm - xMm, 2) + Math.Pow(portal.InteriorZMm - zMm, 2));
            if (away <= reachMm && away < best)
            {
                best = away;
                nearest = portal;
            }
        }
        return nearest;
    }
}
