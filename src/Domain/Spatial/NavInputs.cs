// UNNAMED Domain - what navigation is built from (M7 design §3.4, §3.10, §3.14; D-13)
// No Godot references - pure C#, integer only

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// A solid always blocks. A door and a barrier are gates: read at query time, never baked into the grid's solid layer, so opening one
/// rebuilds nothing.
/// </summary>
public enum NavInputKind { Solid, Door, Barrier }

/// <summary>
/// One footprint navigation is built from: an authored structure, door or barrier, or (from E5) a placed piece's part. A gate carries
/// the key its state is read by.
/// </summary>
public sealed record NavInput(NavInputKind Kind, Blocker Shape, string? GateKey)
{
    /// <summary>The footprint's bounding rectangle.</summary>
    public NavRect Bounds { get; } = NavGeometry.Aabb(Shape);

    public bool IsGate => Kind != NavInputKind.Solid;

    /// <summary>The shape's tag in the canonical order and in tile stamps: 0 a box, 1 a circle.</summary>
    public int ShapeTag => Shape is CircleBlocker ? 1 : 0;
}

/// <summary>
/// The canonical order of inputs (M7 design §3.14): bounds (min x, min z, max x, max z), kind, shape, height, clearance, then the gate
/// key, ordinally. Two inputs it cannot tell apart are the same footprint, so which comes first changes nothing.
/// </summary>
public sealed class NavInputOrder : IComparer<NavInput>
{
    public static NavInputOrder Instance { get; } = new();

    private NavInputOrder()
    {
    }

    public int Compare(NavInput? x, NavInput? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;
        int c = x.Bounds.MinXMm.CompareTo(y.Bounds.MinXMm);
        if (c == 0) c = x.Bounds.MinZMm.CompareTo(y.Bounds.MinZMm);
        if (c == 0) c = x.Bounds.MaxXMm.CompareTo(y.Bounds.MaxXMm);
        if (c == 0) c = x.Bounds.MaxZMm.CompareTo(y.Bounds.MaxZMm);
        if (c == 0) c = x.Kind.CompareTo(y.Kind);
        if (c == 0) c = x.ShapeTag.CompareTo(y.ShapeTag);
        if (c == 0) c = x.Shape.HeightMm.CompareTo(y.Shape.HeightMm);
        if (c == 0) c = x.Shape.ClearanceMm.CompareTo(y.Shape.ClearanceMm);
        if (c == 0) c = string.CompareOrdinal(x.GateKey, y.GateKey);
        return c;
    }
}
