// UNNAMED World - what placed pieces add to what systems share (M7 design §4.4, §4.9, §4.14; re-ported onto humgen-main by WP-1G)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

internal sealed partial class SystemContext
{
    /// <summary>
    /// Where bodies move in a space: its authored ground and walls, and outdoors every placed piece's solid parts too (M7), in
    /// <see cref="StructureOrder"/>. It is the authored space itself while nothing is built, and indoors always.
    /// </summary>
    public WalkSpace SpaceFor(SpaceId space) =>
        space == Setup.Layout.SpaceId && State.StructureSpace is { } built ? built : Setup.SpaceFor(space);

    /// <summary>The placed doors standing shut in a space: outdoors only, where building is.</summary>
    public ImmutableArray<Blocker> PieceLeaves(SpaceId space) =>
        space == Setup.Layout.SpaceId ? State.ClosedPieceLeaves : ImmutableArray<Blocker>.Empty;

    private ImmutableArray<Blocker> WithPieceLeaves(SpaceId space, ImmutableArray<Blocker> doors)
    {
        var leaves = PieceLeaves(space);
        return leaves.IsEmpty ? doors : doors.AddRange(leaves);
    }

    /// <summary>Whether any body - the character, an NPC, a creature - stands where a blocker would be, in the space the character is in (M7 design §4.9).</summary>
    public bool BodyIn(Blocker blocker)
    {
        bool Inside(Body body, long radiusMm) => blocker.DistanceTo(body.XMm, body.ZMm) < radiusMm;
        long person = Setup.Movement.BodyRadiusMm;
        return Inside(State.Body, person)
            || State.Npcs.Values.Any(n => n.Space == State.Space && Inside(n.Body, person))
            || State.Creatures.Values.Any(c => c.Alive && c.Site.Space == State.Space && Inside(c.Body, c.Definition.RadiusMm));
    }

    /// <summary>A placed chest's container key (M7): <c>container.pce_</c> and its piece's ULID in lower case.</summary>
    internal static string PieceChestKey(EntityId pieceId) => "container." + pieceId.Value.ToLowerInvariant();

    /// <summary>A placed bench's station key (M7): <c>station.pce_</c> and its piece's ULID in lower case.</summary>
    internal static string PieceStationKey(EntityId pieceId) => "station." + pieceId.Value.ToLowerInvariant();

    /// <summary>A standing chest's container by key, when the character is outdoors where it stands; null otherwise.</summary>
    private ContainerSite? PieceChest(string key) =>
        key.StartsWith("container.pce_", StringComparison.Ordinal) && State.Space == Setup.Layout.SpaceId
            ? PieceChestSites().FirstOrDefault(s => s.Key == key)
            : null;

    /// <summary>
    /// Every standing chest's container (M7 design §4.14), by piece ID: keyed by its piece, its site the world point of the definition's
    /// <c>at_m</c>, no loot table - a chest starts empty, its record made when it is placed.
    /// </summary>
    public IEnumerable<ContainerSite> PieceChestSites()
    {
        foreach (var row in State.World.Pieces)
        {
            if (Setup.Building.Catalog.Find(row.DefId)?.Container is not { } container)
                continue;
            var (dx, dz) = QuarterTurn.Apply(container.XMm, container.ZMm, row.Rotation);
            yield return new ContainerSite(PieceChestKey(row.InstanceId), string.Empty, row.XMm + dx, row.ZMm + dz, container.StackSlots);
        }
    }

    /// <summary>Each standing bench's station, in <see cref="StructureOrder"/>: keyed by its piece, of its definition's kind, at its first part's centre.</summary>
    public IEnumerable<StationSite> PieceStationSites()
    {
        foreach (var part in State.StructureFootprints)
        {
            if (part.Part != 0 || State.World.Piece(part.PieceId) is not { } row || Setup.Building.Catalog.Find(row.DefId)?.Station is not { } station)
                continue;
            yield return new StationSite(PieceStationKey(part.PieceId), station.Kind, (part.MinXMm + part.MaxXMm) / 2, (part.MinZMm + part.MaxZMm) / 2);
        }
    }

    /// <summary>Every crafting station with the layout it stands in: the authored ones, then the placed benches outdoors (M7 design §4.14).</summary>
    public IEnumerable<(RegionLayout Layout, StationSite Site)> StationSites() =>
        Setup.Sites(l => l.Stations).Concat(PieceStationSites().Select(s => (Setup.Layout, s)));
}
