// UNNAMED Domain - System Writer Interface
// This file defines the write interface to authoritative domain state
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

/// <summary>
/// Write interface to authoritative domain state (ARCHITECTURE.md §5).
/// It is internal to the Domain assembly: Application and Presentation cannot name it, so they
/// cannot write state; only systems receive it, through <see cref="ISystem.Configure"/>, which is
/// itself internal. The architecture tests assert both.
/// </summary>
internal interface IWorldStateWriter
{
    /// <summary>
    /// Write a state record for the given entity ID.
    /// Creates or replaces the record.
    /// </summary>
    /// <typeparam name="T">The state record type (must be a record struct/class)</typeparam>
    /// <param name="entityId">The ULID instance ID of the entity</param>
    /// <param name="value">The state record to store</param>
    void Write<T>(EntityId entityId, T value) where T : struct;

    /// <summary>
    /// Delete a state record for the given entity ID.
    /// No-op if the record does not exist.
    /// </summary>
    /// <typeparam name="T">The state record type</typeparam>
    /// <param name="entityId">The ULID instance ID</param>
    void Delete<T>(EntityId entityId) where T : struct;
}
