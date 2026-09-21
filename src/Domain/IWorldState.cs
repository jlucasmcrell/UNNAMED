// UNNAMED Domain - World State Interface
// This file defines the read-only interface to authoritative domain state
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

/// <summary>
/// Read-only interface to authoritative domain state.
/// This is the ONLY interface exposed outside the Domain assembly for state access.
/// All mutable operations happen through domain commands → systems → internal writer.
/// </summary>
public interface IWorldState
{
    /// <summary>
    /// Read a state record by entity ID.
    /// Returns default(T) if the record does not exist.
    /// </summary>
    /// <typeparam name="T">The state record type (must be a record struct/class)</typeparam>
    /// <param name="entityId">The ULID instance ID of the entity</param>
    /// <returns>The state record, or default(T) if not found</returns>
    T Read<T>(EntityId entityId) where T : struct;

    /// <summary>
    /// Try to read a state record by entity ID.
    /// </summary>
    /// <typeparam name="T">The state record type</typeparam>
    /// <param name="entityId">The ULID instance ID</param>
    /// <param name="value">The retrieved value if found</param>
    /// <returns>True if found, false otherwise</returns>
    bool TryRead<T>(EntityId entityId, out T value) where T : struct;

    /// <summary>
    /// Check if a state record exists for the given entity ID.
    /// </summary>
    /// <typeparam name="T">The state record type</typeparam>
    /// <param name="entityId">The ULID instance ID</param>
    /// <returns>True if the record exists, false otherwise</returns>
    bool Exists<T>(EntityId entityId) where T : struct;
}
