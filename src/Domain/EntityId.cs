// UNNAMED Domain - EntityId (D-04)
// Runtime instance identity: "<prefix>_<ULID>", e.g. itm_01J8ZC4K9P4M2Q7X8B3NDTVW6R
// No Godot references - this is pure C# domain logic

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UNNAMED.Domain;

/// <summary>
/// A runtime instance ID per D-04 and DATA_MODEL.md §2: a lowercase kind prefix, an underscore, and
/// a canonical 26-character Crockford-base32 ULID (48-bit millisecond timestamp + 80 random bits).
/// </summary>
/// <remarks>
/// Instance IDs are runtime identity only and are never derived from a seed. Regenerable baseline
/// entities are identified by slot keys (PERSISTENCE.md I-8) and receive an instance ID only when
/// they diverge from their baseline.
/// </remarks>
[JsonConverter(typeof(EntityIdConverter))]
public sealed class EntityId : IComparable<EntityId>, IEquatable<EntityId>
{
    public const int UlidLength = 26;
    private const int PrefixLength = 3;
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const long MaxTimestamp = (1L << 48) - 1;

    private EntityId(EntityKind kind, string ulid)
    {
        Kind = kind;
        Ulid = ulid;
        Value = EntityKinds.Prefix(kind) + "_" + ulid;
    }

    /// <summary>The full ID, e.g. <c>itm_01J8ZC4K9P4M2Q7X8B3NDTVW6R</c>.</summary>
    public string Value { get; }

    public EntityKind Kind { get; }

    /// <summary>The 26-character canonical ULID without the prefix.</summary>
    public string Ulid { get; }

    /// <summary>Creation time in Unix milliseconds, decoded from the ULID.</summary>
    public long Timestamp => DecodeTimestamp(Ulid);

    public static EntityId NewId(EntityKind kind)
    {
        Span<byte> random = stackalloc byte[10];
        RandomNumberGenerator.Fill(random);
        return Create(kind, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), random);
    }

    /// <summary>
    /// Build an ID from explicit parts. For fixtures and tests; runtime code uses <see cref="NewId"/>.
    /// </summary>
    public static EntityId Create(EntityKind kind, long timestampMs, ReadOnlySpan<byte> random)
    {
        if (timestampMs is < 0 or > MaxTimestamp)
            throw new ArgumentOutOfRangeException(nameof(timestampMs), timestampMs, "ULID timestamps are 48-bit");
        if (random.Length != 10)
            throw new ArgumentException("A ULID carries exactly 10 random bytes", nameof(random));

        Span<byte> bytes = stackalloc byte[16];
        for (int i = 0; i < 6; i++)
            bytes[i] = (byte)(timestampMs >> (40 - 8 * i));
        random.CopyTo(bytes[6..]);
        return new EntityId(kind, Encode(bytes));
    }

    public static EntityId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new FormatException($"Not an instance ID: '{value}'");

    public static bool TryParse(string? value, [NotNullWhen(true)] out EntityId? result)
    {
        result = null;
        if (value is null || value.Length != PrefixLength + 1 + UlidLength || value[PrefixLength] != '_')
            return false;
        if (!EntityKinds.TryFromPrefix(value[..PrefixLength], out var kind))
            return false;

        string ulid = value[(PrefixLength + 1)..].ToUpperInvariant();
        foreach (char c in ulid)
        {
            if (Crockford.IndexOf(c) < 0)
                return false;
        }
        // 26 base32 characters hold 130 bits; a 128-bit ULID leaves the top two clear, so the first
        // character is at most '7'. Anything larger is not a ULID.
        if (ulid[0] > '7')
            return false;

        result = new EntityId(kind, ulid);
        return true;
    }

    // Canonical ULID encoding: the 128-bit value right-aligned in 26 five-bit groups.
    private static string Encode(ReadOnlySpan<byte> bytes)
    {
        UInt128 value = BinaryPrimitives.ReadUInt128BigEndian(bytes);
        Span<char> chars = stackalloc char[UlidLength];
        for (int i = UlidLength - 1; i >= 0; i--)
        {
            chars[i] = Crockford[(int)(value & 31)];
            value >>= 5;
        }
        return new string(chars);
    }

    // The first 10 characters are the top 50 of 130 bits: two zero bits and the 48-bit timestamp.
    private static long DecodeTimestamp(string ulid)
    {
        long value = 0;
        for (int i = 0; i < 10; i++)
            value = (value << 5) | (long)Crockford.IndexOf(ulid[i]);
        return value;
    }

    public override string ToString() => Value;

    public bool Equals(EntityId? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public int CompareTo(EntityId? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    // Value equality, null-safe. Without these the implicit string conversion below was selected for
    // `id == default`, dereferencing a null ID (the M1 PickUpItem NullReferenceException).
    public static bool operator ==(EntityId? left, EntityId? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(EntityId? left, EntityId? right) => !(left == right);

    public static bool operator <(EntityId left, EntityId right) => left.CompareTo(right) < 0;

    public static bool operator >(EntityId left, EntityId right) => left.CompareTo(right) > 0;

    public static implicit operator string?(EntityId? id) => id?.Value;
}

public sealed class EntityIdConverter : JsonConverter<EntityId>
{
    public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected an instance-ID string, got {reader.TokenType}");
        string? value = reader.GetString();
        return EntityId.TryParse(value, out var id)
            ? id
            : throw new JsonException($"Not an instance ID: '{value}'");
    }

    public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
