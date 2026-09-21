// UNNAMED Domain - Entity ID
// This file defines the ULID-based entity identity type
// No Godot references - this is pure C# domain logic

using System.Diagnostics;

namespace UNNAMED.Domain;

/// <summary>
/// A unique identifier for entities in the simulation.
/// Uses ULID (Universally Unique Lexicographically Sortable Identifier)
/// for guaranteed uniqueness and sortable ordering.
/// </summary>
[DebuggerDisplay("{Value}")]
public readonly record struct EntityId
{
    private readonly Guid _value;

    private EntityId(Guid value)
    {
        _value = value;
    }

    /// <summary>
    /// Get the underlying GUID value.
    /// </summary>
    public Guid Value => _value;

    /// <summary>
    /// Create a new EntityId from a GUID.
    /// </summary>
    /// <param name="guid">The GUID value</param>
    public static EntityId FromGuid(Guid guid)
    {
        return new EntityId(guid);
    }

    /// <summary>
    /// Generate a new random EntityId.
    /// </summary>
    public static EntityId NewId()
    {
        return new EntityId(Guid.NewGuid());
    }

    /// <summary>
    /// Parse an EntityId from a string.
    /// </summary>
    /// <param name="value">The string representation (GUID format)</param>
    public static EntityId Parse(string value)
    {
        return new EntityId(Guid.Parse(value));
    }

    /// <summary>
    /// Try to parse an EntityId from a string.
    /// </summary>
    public static bool TryParse(string value, out EntityId result)
    {
        if (Guid.TryParse(value, out var guid))
        {
            result = new EntityId(guid);
            return true;
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Convert to string representation.
    /// </summary>
    public override string ToString()
    {
        return _value.ToString();
    }

    /// <summary>
    /// Implicit conversion from Guid to EntityId.
    /// </summary>
    public static implicit operator EntityId(Guid guid) => new EntityId(guid);

    /// <summary>
    /// Implicit conversion from EntityId to Guid.
    /// </summary>
    public static implicit operator Guid(EntityId id) => id._value;
}
