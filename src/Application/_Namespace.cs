// UNNAMED Application - Namespace file
// This assembly contains orchestration logic that coordinates domain systems
// It uses domain types but does not contain gameplay state

#pragma warning disable CA1050 // Declare types in namespaces

namespace UNNAMED.Application;

/// <summary>
/// Top-level application namespace for UNNAMED RPG
/// Contains orchestration and coordination logic
/// </summary>
public static class ApplicationMarker
{
    /// <summary>
    /// Marker class to ensure the namespace exists
    /// </summary>
}

// Core application types:
// - CommandBus: In-process command routing (synchronous, in-process)
// - EventBus: In-process event publication/subscription (synchronous)
// - TickScheduler: Deterministic tick execution
//
// Commands:
// - Commands are immutable value types that carry intent
// - Systems handle commands and mutate their owned state slices
//
// NOTE: The architecture ensures that IWorldStateWriter is internal to Domain.
// Application can use ISystem and pass the writer through Configure(), but cannot
// name the internal IWorldStateWriter type at all. This is enforced by C# access modifiers.
// The internal interface ensures external assemblies cannot mutate domain state directly.
