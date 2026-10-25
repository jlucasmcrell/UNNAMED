// UNNAMED Presentation - where the Foldscar's debris may lie: its bands round the heart, in metres (grand scale 2026-10-24)
// Plain data, no Godot: FoldscarDressing places from it and FoldscarRingTests holds it inside the ring

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The Foldscar's ring: every piece of the break's debris, hanging or lying, is placed at a distance from the heart inside one of these
/// bands, so none of it reaches the waystation, the road or the inn however the hollow round it changes.
/// </summary>
public static class FoldscarRing
{
    /// <summary>How far out from the heart the debris may lie; the cracked-ground scar is this across each way.</summary>
    public const float Radius = 38f;

    /// <summary>A band of pieces: their size, how far from the heart (From..To) and how high above its foot (Low..High), and how many.</summary>
    public readonly record struct Band(float Size, float From, float To, float Low, float High, int Count);

    /// <summary>The two halves of the wreck slab, kept off the heart's own column.</summary>
    public static readonly Band[] Wreck = { new(0.7f, 26f, 36f, 26f, 44f, 1), new(0.5f, 22f, 32f, 14f, 30f, 1) };

    /// <summary>The break's debris stopped mid-fall: the biggest highest and furthest out, chips close in and low.</summary>
    public static readonly Band[] Hanging = { new(2.2f, 16f, 38f, 14f, 54f, 10), new(1f, 11f, 32f, 7f, 48f, 18), new(0.3f, 8f, 24f, 3f, 46f, 22) };

    /// <summary>Debris that came down long ago, lying round the ring at a third of its hanging size.</summary>
    public static readonly Band Rubble = new(0.35f, 10f, 38f, 0f, 0f, 10);

    /// <summary>The pebbles resting on nothing close in round the heart.</summary>
    public static readonly Band Pebbles = new(1f, 6.5f, 16f, 0.18f, 0.4f, 26);
}
