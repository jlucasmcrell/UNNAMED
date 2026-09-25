// UNNAMED Domain - the navigation lattice over a region (M7 design §3.2-§3.6; D-13)
// No Godot references - pure C#, integer only

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// The derived navigation lattice (M7 design §3.2): one global integer lattice of nodes, stored as a tile per 100 m cell. It is derived
/// from exactly what <see cref="Kinematics"/> collides with - the authored statics, the doors and barriers as gates, and placed pieces -
/// is never saved, and is rebuilt equal after a load. Node indices are global, so a cell seam is never a boundary in the data.
/// Immutable: an edit gives a new grid sharing every tile it did not touch.
/// </summary>
public sealed class NavGrid
{
    private readonly NavLattice _lattice;

    private NavGrid(NavLattice lattice, ImmutableArray<NavTile> tiles, ImmutableArray<NavInput> inputs)
    {
        _lattice = lattice;
        Tiles = tiles;
        Inputs = inputs;
        if (!tiles.IsEmpty)
        {
            long n = lattice.TileNodes;
            NodeMinI = tiles.Min(t => t.Key.Tx) * n;
            NodeMinJ = tiles.Min(t => t.Key.Tz) * n;
            NodeMaxI = tiles.Max(t => t.Key.Tx) * n + n - 1;
            NodeMaxJ = tiles.Max(t => t.Key.Tz) * n + n - 1;
        }
    }

    public NavConfig Config => _lattice.Config;

    /// <summary>The walkable bounds of the region, in millimetres.</summary>
    public NavRect Bounds => _lattice.Bounds;

    /// <summary>The tiles, in (Tz, Tx) order.</summary>
    public ImmutableArray<NavTile> Tiles { get; }

    /// <summary>Every input the grid was built from, in canonical order.</summary>
    public ImmutableArray<NavInput> Inputs { get; }

    /// <summary>The node-index rectangle that holds every tile.</summary>
    public long NodeMinI { get; }
    public long NodeMinJ { get; }
    public long NodeMaxI { get; }
    public long NodeMaxJ { get; }

    /// <summary>A grid of the given tiles, every node stamped from all the inputs.</summary>
    public static NavGrid Build(NavConfig config, NavRect bounds, IEnumerable<NavTileKey> tileKeys, IEnumerable<NavInput> inputs,
        NavCounterSink? counters = null)
    {
        var lattice = new NavLattice(config, bounds);
        var sorted = Canonical(inputs);
        long nodes = 0;
        var tiles = tileKeys.Distinct().OrderBy(k => k).Select(key =>
        {
            var tile = NavTile.Build(lattice, key, sorted, out long stamped);
            nodes += stamped;
            return tile;
        }).ToImmutableArray();
        counters?.CountFullBuild(tiles.Length, nodes);
        return new NavGrid(lattice, tiles, sorted);
    }

    /// <summary>
    /// This grid after an edit inside <paramref name="changed"/> (M7 design §3.5): every node whose centre lies within the influence
    /// radius of it is recomputed from all of <paramref name="inputs"/>, in each tile it meets, and those tiles' input lists and stamps
    /// follow. It equals <see cref="Build"/> over the same inputs, byte for byte.
    /// </summary>
    public NavGrid With(NavRect changed, IEnumerable<NavInput> inputs, NavCounterSink? counters = null)
    {
        var sorted = Canonical(inputs);
        var dirty = changed.Inflated(NavConfig.InfluenceMm);
        int touched = 0;
        long nodes = 0;
        var tiles = Tiles.Select(tile =>
        {
            if (!NavTile.NodeCentres(_lattice, tile.Key).Meets(dirty))
                return tile;
            touched++;
            var restamped = tile.Restamped(dirty, sorted, out long stamped);
            nodes += stamped;
            return restamped;
        }).ToImmutableArray();
        counters?.CountRectRebuild(touched, nodes);
        return new NavGrid(_lattice, tiles, sorted);
    }

    /// <summary>The node whose cell of the lattice holds this coordinate.</summary>
    public long NodeOf(long mm) => NavGeometry.FloorDiv(mm, _lattice.Config.NodeMm);

    /// <summary>The centre of a node, in millimetres.</summary>
    public long CentreOf(long index) => _lattice.Centre(index);

    /// <summary>The tile holding a node, and the node's index within it; false when no tile of this grid holds it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool TryLocate(long i, long j, out NavTile tile, out int local)
    {
        long n = _lattice.TileNodes;
        var key = new NavTileKey(NavGeometry.FloorDiv(i, n), NavGeometry.FloorDiv(j, n));
        int lo = 0, hi = Tiles.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int c = Tiles[mid].Key.CompareTo(key);
            if (c == 0)
            {
                tile = Tiles[mid];
                local = (int)((j - tile.J0) * n + (i - tile.I0));
                return true;
            }
            if (c < 0)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        tile = null!;
        local = -1;
        return false;
    }

    /// <summary>The classes that fit at a node against the solids; 0 off the grid.</summary>
    public int SolidFitAt(long i, long j) => TryLocate(i, j, out var tile, out int local) ? tile.SolidFit[local] : 0;

    /// <summary>The classes that fit at a node against the solids and every gate shut; 0 off the grid.</summary>
    public int ClosedFitAt(long i, long j) => TryLocate(i, j, out var tile, out int local) ? tile.ClosedFit[local] : 0;

    /// <summary>
    /// Whether an agent may stand at a node (M7 design §3.7.1): its class fits against the solids, and every gate that would not let it
    /// fit there is one it may pass - a barrier only when lifted, a door always for an agent that opens doors.
    /// </summary>
    public bool Walkable(long i, long j, NavAgent agent, Func<NavInput, bool> isGateOpen)
    {
        if (!TryLocate(i, j, out var tile, out int local))
            return false;
        return WalkableAt(tile, local, i, j, agent, isGateOpen);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal bool WalkableAt(NavTile tile, int local, long i, long j, NavAgent agent, Func<NavInput, bool> isGateOpen)
    {
        int k = agent.ClassIndex;
        if (tile.SolidFit[local] <= k)
            return false;
        if (tile.ClosedFit[local] > k)
            return true;
        long cx = _lattice.Centre(i), cz = _lattice.Centre(j);
        foreach (var gate in tile.Gates)
        {
            if (NavGeometry.FitAt(gate.Shape, cx, cz, _lattice.Config) <= k && !Passable(gate, agent, isGateOpen))
                return false;
        }
        return true;
    }

    /// <summary>A barrier lets anyone by only when lifted; a door lets by an agent that opens doors, and anyone else only when open.</summary>
    public static bool Passable(NavInput gate, NavAgent agent, Func<NavInput, bool> isGateOpen) =>
        gate.Kind == NavInputKind.Barrier ? isGateOpen(gate) : agent.OpensDoors || isGateOpen(gate);

    /// <summary>
    /// The first 8 bytes of a digest of every tile meeting the watch rectangle, in (Tz, Tx) order: a route planned inside the window
    /// sees any change of geometry under it as a changed stamp.
    /// </summary>
    public ulong WindowStamp(NavRect watch)
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.nav-window/v1");
        foreach (var tile in Tiles)
        {
            if (TileRect(tile.Key).Meets(watch))
                h.Add(tile.Key.Tx).Add(tile.Key.Tz).Add(tile.Stamp);
        }
        return BinaryPrimitives.ReadUInt64BigEndian(h.FinishBytes());
    }

    /// <summary>A digest of the whole grid - configuration, bounds, every tile's key, stamp and bytes - for tests and views only.</summary>
    public string Digest()
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.nav-grid/v1").Add(_lattice.ConfigDigest)
            .Add(Bounds.MinXMm).Add(Bounds.MinZMm).Add(Bounds.MaxXMm).Add(Bounds.MaxZMm).Add(Tiles.Length);
        foreach (var tile in Tiles)
        {
            h.Add(tile.Key.Tx).Add(tile.Key.Tz).Add(tile.Stamp)
                .Add(Convert.ToHexString(SHA256.HashData(tile.SolidFit.AsSpan())))
                .Add(Convert.ToHexString(SHA256.HashData(tile.ClosedFit.AsSpan())));
        }
        return h.Finish();
    }

    /// <summary>The millimetre rectangle a tile covers.</summary>
    public static NavRect TileRect(NavTileKey key) =>
        new(key.Tx * NavConfig.TileMm, key.Tz * NavConfig.TileMm, key.Tx * NavConfig.TileMm + NavConfig.TileMm - 1,
            key.Tz * NavConfig.TileMm + NavConfig.TileMm - 1);

    private static ImmutableArray<NavInput> Canonical(IEnumerable<NavInput> inputs) =>
        inputs.OrderBy(i => i, NavInputOrder.Instance).ToImmutableArray();
}
