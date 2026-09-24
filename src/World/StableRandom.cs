// UNNAMED World - semantic random channels (M2b, WORLD_ARCHITECTURE.md §3.4, PERSISTENCE.md I-9)
// No Godot references - pure C#

using System.Buffers.Binary;
using UNNAMED.Domain;

namespace UNNAMED.World;

/// <summary>
/// Versions of the RNG contract: the encoding from a key to a random value. A change to it moves every
/// baseline, so it is a worldgen contract change (M2b class D) and is recorded in each save.
/// </summary>
public static class RngContract
{
    /// <summary>
    /// M2 (worldgen 1): xoshiro256** streams seeded by (world_seed, worldgen_version, content_hash,
    /// cell, purpose) and drawn in call order. Retired: the whole content pack reseeded every draw, and
    /// an added draw shifted every later one. Kept only to read worldgen-1 saves (see Legacy).
    /// </summary>
    public const int V1 = 1;

    /// <summary>M2b (worldgen 2): semantic channels, <see cref="RngChannel"/>.</summary>
    public const int V2 = 2;
}

/// <summary>
/// One semantic random channel: <c>Random(world_seed, cell, subsystem, semantic_key, sample_index)</c>.
/// Every value is addressed, not drawn in sequence: sample <c>n</c> of a channel is a pure function of
/// the channel key and <c>n</c>, so adding a draw anywhere - a new sample in this channel or a new
/// channel elsewhere - moves nothing that already exists. Content identity is deliberately not an
/// input: changing a creature's hit points cannot move a rock.
/// </summary>
/// <remarks>
/// The channel seed is the first 8 bytes of SHA-256 over the length-prefixed key fields. A sample is
/// SplitMix64 evaluated at a counter built from (sample index, attempt), which is counter-based by
/// construction. Integer-only, with no floating point, so no platform can round a value differently.
/// </remarks>
public readonly struct RngChannel
{
    private const ulong Gamma = 0x9E3779B97F4A7C15;

    private readonly ulong _seed;

    private RngChannel(ulong seed) => _seed = seed;

    public static RngChannel Open(ulong worldSeed, CellKey cell, string subsystem, string semanticKey)
    {
        using var hasher = new CanonicalHasher();
        byte[] digest = hasher.Add("unnamed.rng/2")
            .Add(worldSeed)
            .Add(cell.ToString())
            .Add(subsystem)
            .Add(semanticKey)
            .FinishBytes();
        return new RngChannel(BinaryPrimitives.ReadUInt64BigEndian(digest));
    }

    /// <summary>The raw 64-bit value of one sample.</summary>
    public ulong UInt64(uint sample) => Raw(sample, 0);

    /// <summary>A uniform integer in [minInclusive, maxExclusive), without modulo bias.</summary>
    public int Int(uint sample, int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Empty range");
        ulong range = (ulong)((long)maxExclusive - minInclusive);
        ulong threshold = (0UL - range) % range;
        // Rejection draws a fresh value per attempt from the same sample's own counter space, so a
        // rejected attempt cannot consume a value that belongs to another sample.
        for (uint attempt = 0; ; attempt++)
        {
            ulong r = Raw(sample, attempt);
            if (r >= threshold)
                return (int)((long)minInclusive + (long)(r % range));
        }
    }

    private ulong Raw(uint sample, uint attempt)
    {
        ulong counter = ((ulong)sample << 32) | attempt;
        ulong z = unchecked(_seed + (counter + 1) * Gamma);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EB);
        return z ^ (z >> 31);
    }
}
