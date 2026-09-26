// UNNAMED Domain - navigation's working memory (M7 design §3.14)
// No Godot references - pure C#, integer only

namespace UNNAMED.Domain.Spatial;

/// <summary>
/// The working memory of searches and floods, one per owner and never static, so two worlds never share it. Its contents never
/// influence a result: every entry is valid only for the generation that wrote it, and a search starts a new generation. Everything is
/// allocated on first use; when the generation counter would wrap, the arrays are cleared and counting restarts at 1.
/// </summary>
public sealed class NavScratch
{
    // Per window node: bits 0-2 the parent direction, bit 3 a parent is set, bit 4 closed, bit 5 walkability known, bit 6 walkable.
    internal const byte ParentSet = 1 << 3;
    internal const byte Closed = 1 << 4;
    internal const byte WalkKnown = 1 << 5;
    internal const byte WalkOk = 1 << 6;

    private int _generation;

    public NavScratch()
    {
    }

    /// <summary>A scratch whose generation counter starts at <paramref name="generation"/>: the wrap test's seam.</summary>
    internal NavScratch(int generation) => _generation = generation;

    internal int[] G { get; private set; } = Array.Empty<int>();
    internal int[] Generation { get; private set; } = Array.Empty<int>();
    internal byte[] Dir { get; private set; } = Array.Empty<byte>();
    internal int[] Queue { get; private set; } = Array.Empty<int>();
    internal int[] HeapF { get; private set; } = Array.Empty<int>();
    internal int[] HeapH { get; private set; } = Array.Empty<int>();
    internal int[] HeapIdx { get; private set; } = Array.Empty<int>();

    /// <summary>The generation now in use; a node's entries are valid only when its <see cref="Generation"/> equals it.</summary>
    internal int Current => _generation;

    /// <summary>Start a new generation over a window of <paramref name="cells"/> nodes, growing the per-node arrays if needed.</summary>
    internal int Begin(long cells)
    {
        if (cells > G.Length)
        {
            G = new int[cells];
            Generation = new int[cells];
            Dir = new byte[cells];
        }
        if (_generation == int.MaxValue)
        {
            Array.Clear(Generation);
            _generation = 0;
        }
        return ++_generation;
    }

    /// <summary>A second flood's marks (the placement check's flood of the grid before an edit), valid when they equal its stamp.</summary>
    internal int[] Before { get; private set; } = Array.Empty<int>();

    private int _beforeStamp;

    /// <summary>A new stamp for <see cref="Before"/> over a window of <paramref name="cells"/> nodes, growing it if needed.</summary>
    internal int BeginBefore(long cells)
    {
        if (cells > Before.Length)
            Before = new int[cells];
        if (_beforeStamp == int.MaxValue)
        {
            Array.Clear(Before);
            _beforeStamp = 0;
        }
        return ++_beforeStamp;
    }

    /// <summary>The flood queue, at least <paramref name="capacity"/> long.</summary>
    internal int[] QueueOf(int capacity)
    {
        if (Queue.Length < capacity)
            Queue = new int[capacity];
        return Queue;
    }

    /// <summary>The heap arrays, room for <paramref name="capacity"/> entries.</summary>
    internal void EnsureHeap(int capacity)
    {
        if (HeapF.Length >= capacity)
            return;
        HeapF = new int[capacity];
        HeapH = new int[capacity];
        HeapIdx = new int[capacity];
    }
}
