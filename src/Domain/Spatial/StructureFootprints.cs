// UNNAMED Domain - what a placed piece passes to navigation and collision (M7 design §4.4, §4.23, §6.1.8)
// No Godot references - pure C#, integer only

namespace UNNAMED.Domain.Spatial;

/// <summary>How a placed part stops a body: always (a wall, a doorway's jambs, a chest), or only while its door is shut.</summary>
public enum TraversalClass { Solid, Door }

/// <summary>
/// One blocking part of a placed piece, in world millimetres, as navigation reads it. It carries no open or shut state: a door's state
/// is read from its piece's row when a query asks.
/// </summary>
public sealed record NavFootprint(EntityId PieceId, int Part, long MinXMm, long MinZMm, long MaxXMm, long MaxZMm, long HeightMm,
    TraversalClass Class, EntityId Owner)
{
    /// <summary>The part as a blocker, named <c>{pieceId}#{part}</c> as the collision space names it.</summary>
    public BoxBlocker Box() => new($"{PieceId.Value}#{Part}", MinXMm, MinZMm, MaxXMm, MaxZMm, HeightMm);
}

/// <summary>
/// The one order placed parts are listed in (M7 design §4.4): by their box, then their height, and only then by identity - so the
/// collision space, the closed leaves and the footprints depend on which pieces stand, never on the order they were placed in (G10).
/// </summary>
public sealed class StructureOrder : IComparer<NavFootprint>, IComparer<BoxBlocker>
{
    public static StructureOrder Instance { get; } = new();

    private StructureOrder()
    {
    }

    public int Compare(NavFootprint? a, NavFootprint? b)
    {
        if (ReferenceEquals(a, b))
            return 0;
        if (a is null || b is null)
            return a is null ? -1 : 1;
        int c = Geometric(a.MinXMm, a.MinZMm, a.MaxXMm, a.MaxZMm, a.HeightMm, b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm, b.HeightMm);
        if (c == 0)
            c = string.CompareOrdinal(a.PieceId.Value, b.PieceId.Value);
        return c != 0 ? c : a.Part.CompareTo(b.Part);
    }

    public int Compare(BoxBlocker? a, BoxBlocker? b)
    {
        if (ReferenceEquals(a, b))
            return 0;
        if (a is null || b is null)
            return a is null ? -1 : 1;
        int c = Geometric(a.MinXMm, a.MinZMm, a.MaxXMm, a.MaxZMm, a.HeightMm, b.MinXMm, b.MinZMm, b.MaxXMm, b.MaxZMm, b.HeightMm);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    }

    private static int Geometric(long ax0, long az0, long ax1, long az1, long ah, long bx0, long bz0, long bx1, long bz1, long bh) =>
        (ax0, az0, ax1, az1, ah).CompareTo((bx0, bz0, bx1, bz1, bh));
}
