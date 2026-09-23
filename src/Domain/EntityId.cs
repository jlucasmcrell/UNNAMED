// UNNAMED Domain - Entity ID
// ULID-based unique identifier for entities in the simulation
// No Godot references - this is pure C# domain logic

using System.Diagnostics;
using System.Text.Json.Serialization;

namespace UNNAMED.Domain;

/// <summary>
/// A unique identifier for entities in the simulation.
/// Uses ULID (Universally Unique Lexicographically Sortable Identifier)
/// for guaranteed uniqueness and sortable ordering.
/// Format: 26-character Crockford's Base32 string (128-bit ULID).
/// </summary>
[DebuggerDisplay("{Value}")]
[JsonConverter(typeof(EntityIdConverter))]
public sealed class EntityId : IComparable<EntityId>
{
    // Crockford's Base32 alphabet (ULID standard)
    private const string Base32Chars = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    // The 26-character Base32 encoded ULID string
    private readonly string _value;

    // Cached hash code for performance
    private readonly int _hashCode;

    // For JSON deserialization
    private EntityId()
    {
        _value = string.Empty;
        _hashCode = 0;
    }

    internal EntityId(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("ULID string cannot be null or empty", nameof(value));

        if (value.Length != 26)
            throw new ArgumentException($"ULID must be exactly 26 characters, got {value.Length}", nameof(value));

        // Validate all characters are valid Base32
        foreach (char c in value)
        {
            if (Base32Chars.IndexOf(c) == -1)
                throw new ArgumentException($"Invalid character '{c}' in ULID. Only Base32 characters allowed.", nameof(value));
        }

        _value = value.ToUpperInvariant();
        _hashCode = _value.GetHashCode(StringComparison.Ordinal);
    }

    /// <summary>
    /// Get the underlying ULID string value.
    /// </summary>
    public string Value => _value;

    /// <summary>
    /// Get a hash code for this EntityId (immutable, consistent with equality).
    /// </summary>
    public override int GetHashCode() => _hashCode;

    /// <summary>
    /// Extract the timestamp (milliseconds) from this ULID.
    /// The first 10 Base32 characters represent the 48-bit timestamp.
    /// </summary>
    public long Timestamp => DecodeTimestamp(_value);

    /// <summary>
    /// Create a new EntityId from a ULID string.
    /// </summary>
    /// <param name="ulid">The 26-character Base32 ULID string</param>
    public static EntityId FromUlid(string ulid)
    {
        return new EntityId(ulid);
    }

    /// <summary>
    /// Generate a new random ULID.
    /// ULID = timestamp (48-bit) + random (80-bit)
    /// Uses wall-clock timestamp and cryptographic random for runtime uniqueness
    /// </summary>
    public static EntityId NewId()
    {
        long timestamp = GetUnixTimeMilliseconds();
        return FromTimestampAndRandom(timestamp, GenerateRandomBytesImpl());
    }

    /// <summary>
    /// Generate a deterministic ULID from a seed and ordinal (for baseline generation).
    /// This ensures reproducible entity generation from the same baseline parameters.
    /// </summary>
    /// <param name="seed">The world generation seed</param>
    /// <param name="ordinal">The entity ordinal within the generation sequence</param>
    public static EntityId NewDeterministicId(long seed, int ordinal)
    {
        long timestamp = GetUnixTimeMilliseconds();
        return FromTimestampAndRandom(timestamp, GenerateDeterministicRandomBytes(seed, ordinal));
    }

    /// <summary>
    /// Generate a ULID from a timestamp and random bytes.
    /// </summary>
    /// <param name="timestamp">Unix timestamp in milliseconds</param>
    /// <param name="randomBytes">80 bits of random data (10 bytes)</param>
    public static EntityId FromTimestampAndRandom(long timestamp, byte[] randomBytes)
    {
        if (randomBytes == null || randomBytes.Length != 10)
            throw new ArgumentException("Random bytes must be exactly 10 bytes (80 bits)", nameof(randomBytes));

        // Pack timestamp (6 bytes) + random (10 bytes) = 16 bytes total
        var bytes = new byte[16];
        
        // Timestamp in big-endian (MSB first) - bits 47-0
        bytes[0] = (byte)((timestamp >> 40) & 0xFF);
        bytes[1] = (byte)((timestamp >> 32) & 0xFF);
        bytes[2] = (byte)((timestamp >> 24) & 0xFF);
        bytes[3] = (byte)((timestamp >> 16) & 0xFF);
        bytes[4] = (byte)((timestamp >> 8) & 0xFF);
        bytes[5] = (byte)(timestamp & 0xFF);
        
        // Random bytes
        Array.Copy(randomBytes, 0, bytes, 6, 10);
        
        // Debug output only in DEBUG builds
        #if DEBUG
        Console.WriteLine($"Timestamp: {timestamp} = 0x{timestamp:X12}");
        Console.WriteLine($"Timestamp bytes: {BitConverter.ToString(bytes, 0, 6)}");
        #endif
        
        // Encode to Base32
        var ulid = Encode(bytes);
        #if DEBUG
        Console.WriteLine($"Encoded ULID: {ulid}");
        #endif
        
        return new EntityId(ulid);
    }

    /// <summary>
    /// Generate a deterministic random byte sequence from a seed + ordinal pair.
    /// This ensures baseline generation is reproducible across runs.
    /// </summary>
    /// <param name="seed">The world generation seed</param>
    /// <param name="ordinal">The entity ordinal within the generation sequence</param>
    public static byte[] GenerateDeterministicRandomBytes(long seed, int ordinal)
    {
        // Deterministic PRNG using a simple LCG (Linear Congruential Generator)
        // This is NOT cryptographically secure, but it IS deterministic and fast
        // For ULID we need 10 bytes (80 bits) of randomness
        var result = new byte[10];
        var state = (ulong)(seed ^ ((long)ordinal * 2654435761L)); // Knuth's multiplicative hash

        for (int i = 0; i < result.Length; i++)
        {
            // LCG: state = (a * state + c) mod m
            // Using constants from Numerical Recipes
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            result[i] = (byte)((state >> 24) & 0xFF);
        }

        return result;
    }

    /// <summary>
    /// Generate a deterministic random byte sequence from an ordinal (backward compat, deprecated).
    /// </summary>
    /// <param name="ordinal">The ordinal value</param>
    [Obsolete("Use GenerateDeterministicRandomBytes(seed, ordinal) for baseline generation")]
    public static byte[] GenerateDeterministicRandomBytes(int ordinal)
    {
        // Use a fixed seed (0) for simple ordinal-based determinism
        return GenerateDeterministicRandomBytes(0L, ordinal);
    }

    /// <summary>
    /// Generate deterministic random bytes for baseline generation (uses wall-clock fallback for backward compat).
    /// </summary>
    [Obsolete("Use GenerateDeterministicRandomBytes(seed, ordinal) or NewDeterministicId(seed, ordinal)")]
    private static byte[] GenerateDeterministicRandomBytes()
    {
        // For NewId() without explicit ordinal, use a deterministic fallback based on timestamp
        // This still makes IDs unique but not fully reproducible across different runs
        var timestamp = GetUnixTimeMilliseconds();
        return GenerateDeterministicRandomBytes(timestamp, 0);
    }

    /// <summary>
    /// Parse an EntityId from a string.
    /// </summary>
    /// <param name="value">The string representation (26-char Base32 ULID)</param>
    public static EntityId Parse(string value)
    {
        return new EntityId(value);
    }

    /// <summary>
    /// Try to parse an EntityId from a string.
    /// </summary>
    public static bool TryParse(string value, out EntityId result)
    {
        if (value == null || value.Length != 26)
        {
            result = new EntityId();
            return false;
        }

        // Validate all characters
        foreach (char c in value)
        {
            if (Base32Chars.IndexOf(c) == -1)
            {
                result = new EntityId();
                return false;
            }
        }

        result = new EntityId(value.ToUpperInvariant());
        return true;
    }

    /// <summary>
    /// Convert to string representation (26-char Base32 ULID).
    /// </summary>
    public override string ToString()
    {
        return _value;
    }

    /// <summary>
    /// Extract timestamp (milliseconds) from 26-char Base32 ULID string.
    /// First 10 Base32 chars = 48-bit timestamp.
    /// The first 8 chars represent bytes 0-4 (40 bits), occupying bits 49-10.
    /// The next 2 chars (10 bits) represent byte 5 at bits 9-0 of a 50-bit value.
    /// The 48-bit timestamp is at bits 49-2 of this 50-bit value.
    /// </summary>
    private static long DecodeTimestamp(string ulid)
    {
        // First 10 chars = 50 bits
        // We need to extract the 48-bit timestamp (bits 49-2)
        ulong packed = 0;
        for (int i = 0; i < 10; i++)
        {
            int charIndex = Base32Chars.IndexOf(ulid[i]);
            if (charIndex == -1)
                throw new ArgumentException($"Invalid character '{ulid[i]}' in ULID timestamp", nameof(ulid));
            packed = (packed << 5) | (uint)charIndex;
        }
        
        // The 48-bit timestamp is at bits 49-2 of the 50-bit value
        // Shift right by 2 to move bits 49-2 to bits 47-0
        return (long)(packed >> 2);
    }

    /// <summary>
    /// Encode 16 bytes to 26-character Base32 ULID string.
    /// ULID encoding treats the 16 bytes as a 128-bit big-endian integer.
    /// We read 5 bits at a time from MSB to LSB to create 26 Base32 characters.
    /// 128 bits / 5 bits per char = 25.6, so we get 26 chars (2 bits unused).
    /// bytes[0] contains bits 127-120 (MSB), bytes[15] contains bits 7-0 (LSB)
    /// </summary>
    private static string Encode(byte[] bytes)
    {
        if (bytes.Length != 16)
            throw new ArgumentException("Expected 16 bytes", nameof(bytes));

        var sb = new System.Text.StringBuilder(26);
        
        // Treat bytes as a 128-bit big-endian integer
        // Read 5 bits at a time from MSB to LSB
        
        for (int charIdx = 0; charIdx < 26; charIdx++)
        {
            // Each Base32 char represents 5 bits
            // char 0 gets bits 127-123, char 1 gets bits 122-118, etc.
            int bitStart = 127 - (charIdx * 5);
            
            uint value = 0;
            for (int bit = 0; bit < 5; bit++)
            {
                int bitPos = bitStart - bit;
                if (bitPos >= 0)
                {
                    // bytes[0] contains bits 127-120 (byteIdx = 15 - (bitPos/8))
                    // bitInByte is from LSB side (bitPos % 8)
                    int byteIdx = 15 - (bitPos / 8);
                    int bitInByte = bitPos % 8;
                    if (((bytes[byteIdx] >> bitInByte) & 1) != 0)
                    {
                        value = (value << 1) | 1;
                    }
                    else
                    {
                        value <<= 1;
                    }
                }
                else
                {
                    // Beyond the 128 bits, pad with 0
                    value <<= 1;
                }
            }
            
            sb.Append(Base32Chars[(int)value]);
        }

        return sb.ToString(0, 26);
    }

    /// <summary>
    /// Encode 16 bytes to 26-character Base32 ULID string (with byte[] input validation).
    /// </summary>
    private static string Encode(byte[] bytes, int offset)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));
        if (offset < 0 || offset + 16 > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset and 16 bytes must fit within array");

        var sb = new System.Text.StringBuilder(26);
        
        for (int charIdx = 0; charIdx < 26; charIdx++)
        {
            int bitStart = 127 - (charIdx * 5);
            
            uint value = 0;
            for (int bit = 0; bit < 5; bit++)
            {
                int bitPos = bitStart - bit;
                if (bitPos >= 0)
                {
                    int byteIdx = 15 - (bitPos / 8);
                    int bitInByte = bitPos % 8;
                    if (((bytes[offset + byteIdx] >> bitInByte) & 1) != 0)
                    {
                        value = (value << 1) | 1;
                    }
                    else
                    {
                        value <<= 1;
                    }
                }
                else
                {
                    value <<= 1;
                }
            }
            
            sb.Append(Base32Chars[(int)value]);
        }

        return sb.ToString(0, 26);
    }

    /// <summary>
    /// Generate 10 random bytes for ULID entropy (non-deterministic, for runtime).
    /// Uses cryptographic random number generator for guaranteed uniqueness.
    /// </summary>
    private static byte[] GenerateRandomBytesImpl()
    {
        var randomBytes = new byte[10];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        return randomBytes;
    }

    /// <summary>
    /// Get current Unix timestamp in milliseconds.
    /// </summary>
    private static long GetUnixTimeMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Implicit conversion from string to EntityId.
    /// </summary>
    public static implicit operator EntityId(string ulid) => new EntityId(ulid);

    /// <summary>
    /// Implicit conversion from EntityId to string.
    /// </summary>
    public static implicit operator string(EntityId id) => id._value;

    /// <summary>
    /// Check if this EntityId is less than another.
    /// </summary>
    public static bool operator <(EntityId left, EntityId right)
    {
        return string.Compare(left._value, right._value, StringComparison.Ordinal) < 0;
    }

    /// <summary>
    /// Check if this EntityId is greater than another.
    /// </summary>
    public static bool operator >(EntityId left, EntityId right)
    {
        return string.Compare(left._value, right._value, StringComparison.Ordinal) > 0;
    }

    /// <summary>
    /// Compare EntityIds for ordering (required for IComparable).
    /// </summary>
    public int CompareTo(EntityId other)
    {
        return string.Compare(_value, other._value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Check equality with another object.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is EntityId other)
        {
            return _value == other._value;
        }
        return false;
    }

    /// <summary>
    /// Check equality with another EntityId.
    /// </summary>
    public bool Equals(EntityId other)
    {
        return _value == other._value;
    }
}

/// <summary>
/// JSON converter for EntityId deserialization.
/// Handles both the compact string format and the object format with value/timestamp.
/// </summary>
public class EntityIdConverter : System.Text.Json.Serialization.JsonConverter<EntityId>
{
    public override EntityId Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
        {
            string value = reader.GetString() ?? string.Empty;
            return new EntityId(value);
        }
        
        if (reader.TokenType == System.Text.Json.JsonTokenType.StartObject)
        {
            string? value = null;
            // Read properties until we find "value"
            while (reader.Read())
            {
                if (reader.TokenType == System.Text.Json.JsonTokenType.EndObject)
                {
                    break;
                }
                
                if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString() ?? string.Empty;
                    if (propertyName == "value" && reader.Read())
                    {
                        value = reader.GetString();
                    }
                    else
                    {
                        // Skip this property's value
                        reader.Skip();
                    }
                }
            }
            
            if (string.IsNullOrEmpty(value))
            {
                throw new System.Text.Json.JsonException("EntityId object must have a 'value' property");
            }
            
            return new EntityId(value);
        }
        
        throw new System.Text.Json.JsonException($"Unexpected token type {reader.TokenType} when reading EntityId");
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, EntityId value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
