// UNNAMED Application - Commands Namespace
// This file contains command types for domain interactions
// Commands carry intent but no behavior; systems handle them

using UNNAMED.Domain;

namespace UNNAMED.Application.Commands;

/// <summary>
/// Base type for all domain commands.
/// Commands are immutable value types that carry intent.
/// All commands must inherit from this base or implement ICommandMarker.
/// </summary>
public abstract record Command
{
    /// <summary>
    /// The entity ID this command targets (if applicable).
    /// For entity-scoped commands, this is the target entity.
    /// For global commands, this is null.
    /// </summary>
    public virtual EntityId? TargetId => null;
}

/// <summary>
/// Marker interface for command types.
/// Commands must be record structs or classes.
/// </summary>
public interface ICommandMarker { }
