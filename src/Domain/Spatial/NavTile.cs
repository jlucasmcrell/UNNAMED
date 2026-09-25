// UNNAMED Domain - one 100 m tile of the navigation lattice (M7 design §3.2, §3.5.3, §3.6; D-13)
// No Godot references - pure C#, integer only

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UNNAMED.Domain.Spatial;

/// <summary>A tile's place: <c>Tx = FloorDiv(x, 100 m)</c>, the same for z. Tiles are ordered by (Tz, Tx).</summary>
public readonly record struct NavTileKey(long Tx, long Tz) : IComparable<NavTileKey>
{
    public int CompareTo(NavTileKey other)
    {
        int c = Tz.CompareTo(other.Tz);
        return c != 0 ? c : Tx.CompareTo(other.Tx);
    }
}

/// <summary>
/// What a grid and its tiles share: the configuration, the walkable bounds, the configuration's digest and the planning radii. Built
/// once per grid; immutable.
/// </summary>
internal sealed class NavLattice
{
    public NavLattice(NavConfig config, NavRect bounds)
    {
        Config = config;
        Bounds = bounds;
        ConfigDigest = config.Digest();
        TileNodes = config.TileNodes;
        Half = config.NodeMm / 2;
        Rp = Enumerable.Range(0, config.Classes.Length).Select(config.RpMm).ToImmutableArray();
        RpMax = Rp.IsEmpty ? 0 : Rp[^1];
    }

    public NavConfig Config { get; }
    public NavRect Bounds { get; }
    public string ConfigDigest { get; }
    public long TileNodes { get; }
    public long Half { get; }
    public ImmutableArray<long> Rp { get; }
    public long RpMax { get; }

    public long Centre(long index) => index * Config.NodeMm + Half;

    /// <summary>How many classes fit between the bounds along one axis at this node centre.</summary>
    public byte AxisFit(long centre, long min, long max)
    {
        int k = 0;
        while (k < Rp.Length && centre - Rp[k] >= min && centre + Rp[k] <= max)
            k++;
        return (byte)k;
    }

    /// <summary>The first and last node index whose centre lies in [min, max]; empty when first > last.</summary>
    public (long First, long Last) NodesWithin(long min, long max) =>
        (NavGeometry.CeilDiv(min - Half, Config.NodeMm), NavGeometry.FloorDiv(max - Half, Config.NodeMm));
}

/// <summary>
/// One tile of the lattice (M7 design §3.2): for each of its nodes, how many classes fit against the solids (<see cref="SolidFit"/>)
/// and against the solids and every gate shut (<see cref="ClosedFit"/>); the inputs within the influence radius of its nodes, the gates
/// among them, and the stamp of those inputs. A tile is a pure function of its key and the inputs: built alone, rebuilt after an edit
/// or built with its neighbours, its bytes are the same.
/// </summary>
public sealed class NavTile
{
    private readonly NavLattice _lattice;

    private NavTile(NavLattice lattice, NavTileKey key, ImmutableArray<byte> solidFit, ImmutableArray<byte> closedFit, ImmutableArray<NavInput> inputs)
    {
        _lattice = lattice;
        Key = key;
        SolidFit = solidFit;
        ClosedFit = closedFit;
        Inputs = inputs;
        Gates = inputs.Where(i => i.IsGate).ToImmutableArray();
        Stamp = StampOf(lattice, key, inputs);
        I0 = key.Tx * lattice.TileNodes;
        J0 = key.Tz * lattice.TileNodes;
    }

    public NavTileKey Key { get; }

    /// <summary>Per node, row by row from the tile's south-west node: the classes that fit against the solids.</summary>
    public ImmutableArray<byte> SolidFit { get; }

    /// <summary>Per node: the classes that fit against the solids and every gate, all shut.</summary>
    public ImmutableArray<byte> ClosedFit { get; }

    /// <summary>The inputs whose bounds, inflated by the influence radius, meet the tile's node centres, in canonical order.</summary>
    public ImmutableArray<NavInput> Inputs { get; }

    /// <summary>The gates among <see cref="Inputs"/>.</summary>
    public ImmutableArray<NavInput> Gates { get; }

    /// <summary>The first 8 bytes of a digest of the configuration, the key and every input. It names no instance.</summary>
    public ulong Stamp { get; }

    /// <summary>The global index of the tile's first node on each axis.</summary>
    public long I0 { get; }
    public long J0 { get; }

    internal static NavTile Build(NavLattice lattice, NavTileKey key, ImmutableArray<NavInput> sortedInputs, out long nodes)
    {
        long n = lattice.TileNodes;
        var solid = new byte[n * n];
        var closed = new byte[n * n];
        var inputs = Relevant(lattice, key, sortedInputs);
        nodes = StampRect(lattice, key, solid, closed, 0, 0, n - 1, n - 1, inputs);
        return new NavTile(lattice, key, ImmutableCollectionsMarshal.AsImmutableArray(solid), ImmutableCollectionsMarshal.AsImmutableArray(closed), inputs);
    }

    /// <summary>
    /// This tile with every node whose centre lies in <paramref name="rect"/> recomputed from all of <paramref name="sortedInputs"/>,
    /// and its input list and stamp brought up to date. The tile itself is unchanged.
    /// </summary>
    public NavTile Restamped(NavRect rect, ImmutableArray<NavInput> sortedInputs) => Restamped(rect, sortedInputs, out _);

    internal NavTile Restamped(NavRect rect, ImmutableArray<NavInput> sortedInputs, out long nodes)
    {
        long n = _lattice.TileNodes;
        var (i0, i1) = _lattice.NodesWithin(rect.MinXMm, rect.MaxXMm);
        var (j0, j1) = _lattice.NodesWithin(rect.MinZMm, rect.MaxZMm);
        long li0 = Math.Max(i0 - I0, 0), li1 = Math.Min(i1 - I0, n - 1);
        long lj0 = Math.Max(j0 - J0, 0), lj1 = Math.Min(j1 - J0, n - 1);
        var solid = SolidFit.ToArray();
        var closed = ClosedFit.ToArray();
        var inputs = Relevant(_lattice, Key, sortedInputs);
        nodes = li0 <= li1 && lj0 <= lj1 ? StampRect(_lattice, Key, solid, closed, li0, lj0, li1, lj1, inputs) : 0;
        return new NavTile(_lattice, Key, ImmutableCollectionsMarshal.AsImmutableArray(solid), ImmutableCollectionsMarshal.AsImmutableArray(closed), inputs);
    }

    /// <summary>The rectangle of the tile's node centres.</summary>
    internal static NavRect NodeCentres(NavLattice lattice, NavTileKey key)
    {
        long s = lattice.Config.NodeMm;
        long x0 = key.Tx * NavConfig.TileMm + lattice.Half, z0 = key.Tz * NavConfig.TileMm + lattice.Half;
        long span = (lattice.TileNodes - 1) * s;
        return new NavRect(x0, z0, x0 + span, z0 + span);
    }

    private static ImmutableArray<NavInput> Relevant(NavLattice lattice, NavTileKey key, ImmutableArray<NavInput> sortedInputs)
    {
        var centres = NodeCentres(lattice, key);
        var builder = ImmutableArray.CreateBuilder<NavInput>();
        foreach (var input in sortedInputs)
        {
            if (input.Bounds.Inflated(NavConfig.InfluenceMm).Meets(centres))
                builder.Add(input);
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// The one stamping function (M7 design §3.5.3): each node of the local rectangle becomes the minimum of its bounds fit and the fit
    /// against every input; solids lower both layers, gates only the closed one. The minimum is order-free, so any input order, tile order
    /// or edit order gives the same bytes. Returns the number of nodes stamped.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long StampRect(NavLattice lattice, NavTileKey key, byte[] solid, byte[] closed, long li0, long lj0, long li1, long lj1,
        ImmutableArray<NavInput> inputs)
    {
        long n = lattice.TileNodes;
        long gi0 = key.Tx * n, gj0 = key.Tz * n;
        var bounds = lattice.Bounds;
        var fitX = new byte[li1 - li0 + 1];
        for (long li = li0; li <= li1; li++)
            fitX[li - li0] = lattice.AxisFit(lattice.Centre(gi0 + li), bounds.MinXMm, bounds.MaxXMm);
        for (long lj = lj0; lj <= lj1; lj++)
        {
            byte fz = lattice.AxisFit(lattice.Centre(gj0 + lj), bounds.MinZMm, bounds.MaxZMm);
            long row = lj * n;
            for (long li = li0; li <= li1; li++)
            {
                byte v = Math.Min(fitX[li - li0], fz);
                solid[row + li] = v;
                closed[row + li] = v;
            }
        }

        var rp = lattice.Rp;
        foreach (var input in inputs)
        {
            var reach = input.Bounds.Inflated(lattice.RpMax);
            var (xi0, xi1) = lattice.NodesWithin(reach.MinXMm, reach.MaxXMm);
            var (zj0, zj1) = lattice.NodesWithin(reach.MinZMm, reach.MaxZMm);
            long a0 = Math.Max(xi0 - gi0, li0), a1 = Math.Min(xi1 - gi0, li1);
            long b0 = Math.Max(zj0 - gj0, lj0), b1 = Math.Min(zj1 - gj0, lj1);
            bool isSolid = !input.IsGate;
            for (long lj = b0; lj <= b1; lj++)
            {
                long cz = lattice.Centre(gj0 + lj);
                long row = lj * n;
                for (long li = a0; li <= a1; li++)
                {
                    long cx = lattice.Centre(gi0 + li);
                    byte f = Fit(input.Shape, cx, cz, rp);
                    long idx = row + li;
                    if (f < closed[idx])
                        closed[idx] = f;
                    if (isSolid && f < solid[idx])
                        solid[idx] = f;
                }
            }
        }
        return (li1 - li0 + 1) * (lj1 - lj0 + 1);
    }

    /// <summary><see cref="NavGeometry.FitAt"/> over precomputed planning radii.</summary>
    private static byte Fit(Blocker shape, long px, long pz, ImmutableArray<long> rp)
    {
        int k = 0;
        if (shape is BoxBlocker b)
        {
            long dx = Math.Max(Math.Max(b.MinXMm - px, 0), px - b.MaxXMm);
            long dz = Math.Max(Math.Max(b.MinZMm - pz, 0), pz - b.MaxZMm);
            long d2 = dx * dx + dz * dz;
            while (k < rp.Length && d2 >= rp[k] * rp[k])
                k++;
            return (byte)k;
        }
        var c = (CircleBlocker)shape;
        long ex = px - c.CenterXMm, ez = pz - c.CenterZMm;
        long e2 = ex * ex + ez * ez;
        while (k < rp.Length && e2 >= (rp[k] + c.RadiusMm) * (rp[k] + c.RadiusMm))
            k++;
        return (byte)k;
    }

    private static ulong StampOf(NavLattice lattice, NavTileKey key, ImmutableArray<NavInput> inputs)
    {
        using var h = new CanonicalHasher();
        h.Add("unnamed.nav-tile/v1").Add(lattice.ConfigDigest).Add(key.Tx).Add(key.Tz).Add(inputs.Length);
        foreach (var input in inputs)
        {
            var (cx, cz, radius) = input.Shape is CircleBlocker c ? (c.CenterXMm, c.CenterZMm, c.RadiusMm) : (0L, 0L, 0L);
            h.Add((int)input.Kind).Add(input.ShapeTag)
                .Add(input.Bounds.MinXMm).Add(input.Bounds.MinZMm).Add(input.Bounds.MaxXMm).Add(input.Bounds.MaxZMm)
                .Add(cx).Add(cz).Add(radius).Add(input.Shape.HeightMm).Add(input.Shape.ClearanceMm).Add(input.Kind == NavInputKind.Door);
        }
        return BinaryPrimitives.ReadUInt64BigEndian(h.FinishBytes());
    }
}
