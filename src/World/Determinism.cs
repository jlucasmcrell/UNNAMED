// UNNAMED World - deterministic hashing and streams (WORLD_ARCHITECTURE.md §3.4, PERSISTENCE.md I-9)
// No Godot references - pure C#

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using UNNAMED.Domain;

namespace UNNAMED.World;

/// <summary>
/// The baseline tuple <c>(world_seed, worldgen_version, content_hash)</c> (PERSISTENCE.md §1.3):
/// everything that determines a regenerated cell.
/// </summary>
public sealed record BaselineTuple
{
    public BaselineTuple(ulong worldSeed, int worldgenVersion, string contentHash)
    {
        if (worldgenVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(worldgenVersion), worldgenVersion, "worldgen_version starts at 1");
        if (!IsDigest(contentHash))
            throw new ArgumentException($"content_hash must be 'sha256:<64 hex>', got '{contentHash}'", nameof(contentHash));
        WorldSeed = worldSeed;
        WorldgenVersion = worldgenVersion;
        ContentHash = contentHash;
    }

    public ulong WorldSeed { get; }
    public int WorldgenVersion { get; }
    public string ContentHash { get; }

    /// <summary>The manifest spelling of a seed, e.g. <c>0x5C1A9E7B4D2F0083</c>.</summary>
    public static string FormatSeed(ulong seed) => "0x" + seed.ToString("X16", CultureInfo.InvariantCulture);

    public static ulong ParseSeed(string text)
    {
        if (!text.StartsWith("0x", StringComparison.Ordinal)
            || !ulong.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong seed))
            throw new FormatException($"Not a world seed: '{text}'");
        return seed;
    }

    public static bool IsDigest(string? value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;
}

/// <summary>
/// A reproducible random stream, <c>PRNG(hash(world_seed, worldgen_version, content_hash, cell_key, purpose))</c>
/// (WORLD_ARCHITECTURE.md §3.4). Every procedural decision draws from a stream keyed by cell and
/// purpose, never from a shared stream in traversal order, so a cell's baseline is a pure function of
/// its key and can be regenerated in any order, on any machine, in any session.
/// </summary>
/// <remarks>
/// The generator is xoshiro256**, implemented here rather than taken from <see cref="Random"/>, whose
/// algorithm .NET does not promise to keep stable across versions. Output is integer-only by design:
/// no floating point enters a baseline, so no platform rounding difference can change one.
/// </remarks>
public sealed class DeterministicStream
{
    private ulong _s0, _s1, _s2, _s3;

    private DeterministicStream(ReadOnlySpan<byte> seed32)
    {
        _s0 = BinaryPrimitives.ReadUInt64BigEndian(seed32[..8]);
        _s1 = BinaryPrimitives.ReadUInt64BigEndian(seed32[8..16]);
        _s2 = BinaryPrimitives.ReadUInt64BigEndian(seed32[16..24]);
        _s3 = BinaryPrimitives.ReadUInt64BigEndian(seed32[24..32]);
        if ((_s0 | _s1 | _s2 | _s3) == 0)
            _s0 = 1;   // the all-zero state is xoshiro's single fixed point
    }

    public static DeterministicStream For(BaselineTuple tuple, CellKey cell, string purpose)
    {
        using var hasher = new CanonicalHasher();
        byte[] seed = hasher.Add("unnamed.stream/v1")
            .Add(tuple.WorldSeed)
            .Add(tuple.WorldgenVersion)
            .Add(tuple.ContentHash)
            .Add(cell.ToString())
            .Add(purpose)
            .FinishBytes();
        return new DeterministicStream(seed);
    }

    public ulong NextUInt64()
    {
        ulong result = RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive), without modulo bias.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Empty range");
        ulong range = (ulong)((long)maxExclusive - minInclusive);
        ulong threshold = (0UL - range) % range;
        while (true)
        {
            ulong r = NextUInt64();
            if (r >= threshold)
                return (int)((long)minInclusive + (long)(r % range));
        }
    }

    private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));
}
