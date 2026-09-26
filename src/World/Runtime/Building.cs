// UNNAMED World - building v1 (M7 design §4)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>Building as the running world uses it: the piece catalogue and building's numbers. Built by <c>BuildingContent.Build</c>.</summary>
public sealed record BuildingSetup(BuildingCatalog Catalog, BuildingConstants Constants)
{
    /// <summary>No pieces, and the shipped numbers.</summary>
    public static BuildingSetup Empty { get; } = new(BuildingCatalog.Empty, BuildingConstants.Default);
}

/// <summary>Ground building keeps clear (M7 design §4.16), and what it is kept clear for, as a refusal names it.</summary>
public sealed record ProtectedZone(string What, Blocker Shape);

/// <summary>
/// The protected zones of a region (M7 design §4.16), one reading shared by the placement check (check 11) and the content lint that
/// keeps build areas clear (BLD007): the spawn, each authored NPC's place, each authored container, station and node, each door, switch
/// structure and barrier grown by a margin, and each spawner's disc and patrol legs. A piece's bounds may not strictly meet one.
/// </summary>
public static class ProtectedZones
{
    /// <summary>A patrol leg is sampled this often, its ends included.</summary>
    public const long PatrolSampleMm = 500;

    /// <param name="creatureRadiusMm">A creature definition's body radius.</param>
    /// <param name="nameOf">A definition's display name, for an NPC's place.</param>
    public static ImmutableArray<ProtectedZone> Of(RegionLayout layout, IEnumerable<SpawnSite> spawns, Func<string, long> creatureRadiusMm,
        BuildingProtection protection, Func<string, string> nameOf)
    {
        var zones = new List<ProtectedZone>
        {
            new("the spawn point", Circle("zone.spawn", layout.Spawn.XMm, layout.Spawn.ZMm, protection.SpawnPointMm)),
        };
        zones.AddRange(layout.Npcs.Select(n => new ProtectedZone($"{nameOf(n.NpcId)}'s place", Circle(n.NpcId, n.XMm, n.ZMm, protection.NpcSiteMm))));
        zones.AddRange(layout.Containers.Select(c => new ProtectedZone(c.Key, Circle(c.Key, c.XMm, c.ZMm, protection.SiteMm))));
        zones.AddRange(layout.Stations.Select(s => new ProtectedZone(s.Key, Circle(s.Key, s.XMm, s.ZMm, protection.SiteMm))));
        zones.AddRange(layout.Nodes.Select(n => new ProtectedZone(n.Name, Circle(n.Name, n.XMm, n.ZMm, protection.SiteMm))));
        zones.AddRange(layout.Doors.Select(d => new ProtectedZone(d.Key, Grown(d.ClosedFootprint, protection.StructureMarginMm))));
        zones.AddRange(layout.Switches.Select(s => new ProtectedZone(s.Key, Grown(s.Body, protection.StructureMarginMm))));
        zones.AddRange(layout.Barriers.Select(b => new ProtectedZone(b.Key, Grown(b.Footprint, protection.StructureMarginMm))));
        foreach (var site in spawns)
        {
            long radius = site.RadiusMm + (site.Members.IsEmpty ? 0 : site.Members.Max(m => creatureRadiusMm(m.CreatureId))) + protection.SpawnerMarginMm;
            zones.Add(new ProtectedZone(site.Key, Circle(site.Key, site.XMm, site.ZMm, radius)));
            for (int leg = 0; leg + 1 < site.Route.Length; leg++)
            {
                var (ax, az) = site.Route[leg];
                var (bx, bz) = site.Route[leg + 1];
                long dx = bx - ax, dz = bz - az;
                long steps = Math.Max(1, (long)Math.Ceiling(Math.Sqrt((double)dx * dx + (double)dz * dz) / PatrolSampleMm));
                for (long i = 0; i <= steps; i++)
                    zones.Add(new ProtectedZone(site.Key, Circle(site.Key, ax + dx * i / steps, az + dz * i / steps, radius)));
            }
        }
        return zones.ToImmutableArray();
    }

    /// <summary>The first zone a box strictly meets, or null.</summary>
    public static ProtectedZone? Met(ImmutableArray<ProtectedZone> zones, BoundsMm bounds) =>
        zones.FirstOrDefault(z => BuildingMath.Overlaps(bounds, z.Shape));

    private static CircleBlocker Circle(string id, long xMm, long zMm, long radiusMm) => new(id, xMm, zMm, radiusMm, 0);

    private static Blocker Grown(Blocker shape, long marginMm) => shape switch
    {
        BoxBlocker b => new BoxBlocker(b.Id, b.MinXMm - marginMm, b.MinZMm - marginMm, b.MaxXMm + marginMm, b.MaxZMm + marginMm, b.HeightMm),
        CircleBlocker c => new CircleBlocker(c.Id, c.CenterXMm, c.CenterZMm, c.RadiusMm + marginMm, c.HeightMm),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape.GetType().Name, "Unknown blocker shape"),
    };
}
