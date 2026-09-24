// UNNAMED Domain - CanonicalHasher
// Shared canonical digest for content identity (content_hash) and world generation.
// No Godot references - pure C#

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace UNNAMED.Domain;

/// <summary>
/// SHA-256 over an unambiguous, length-prefixed field encoding. Two different field sequences can
/// never produce the same byte stream, so digests built with it cannot collide by concatenation.
/// </summary>
public sealed class CanonicalHasher : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public CanonicalHasher Add(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AddLength(bytes.Length);
        _hash.AppendData(bytes);
        return this;
    }

    public CanonicalHasher Add(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        _hash.AppendData(buffer);
        return this;
    }

    public CanonicalHasher Add(ulong value) => Add(unchecked((long)value));

    public CanonicalHasher Add(int value) => Add((long)value);

    public CanonicalHasher Add(bool value) => Add(value ? 1L : 0L);

    public byte[] FinishBytes() => _hash.GetHashAndReset();

    /// <summary>The digest in the manifest's spelling: <c>sha256:&lt;lowercase hex&gt;</c>.</summary>
    public string Finish() => "sha256:" + Convert.ToHexString(FinishBytes()).ToLowerInvariant();

    public void Dispose() => _hash.Dispose();

    private void AddLength(int length)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, length);
        _hash.AppendData(buffer);
    }
}
