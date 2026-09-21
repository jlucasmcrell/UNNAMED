// UNNAMED Domain - System Writer Interface
// This file defines the write interface to authoritative domain state
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

/// <summary>
/// Write interface to authoritative domain state.
/// This interface is public in the Domain assembly.
/// Only domain systems receive this interface through their Configure method.
/// 
/// IMPORTANT: The implementation of this interface is internal to the Domain assembly.
/// External assemblies (Application, Presentation) cannot create instances or implement
/// this interface outside Domain. This ensures that domain state can only be mutated
/// through the command → system → writer flow defined in the Domain assembly.
/// </summary>
public interface ISystemWriterInternals
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
