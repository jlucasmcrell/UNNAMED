// UNNAMED Domain - In-Process Command Bus
// This file implements the synchronous in-process command bus
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

/// <summary>
/// Interface for routing commands to their owning systems.
/// Commands carry intent but no behavior; systems handle them.
/// </summary>
public interface ICommandBus
{
    /// <summary>
    /// Dispatch a command to its owning system for handling.
    /// The system validates the command and may mutate its owned slice via the writer
    /// passed to its Configure method, then publish events on success.
    /// </summary>
    /// <typeparam name="T">The command type (must be a record struct/class)</typeparam>
    /// <param name="command">The command instance to dispatch</param>
    /// <param name="context">The simulation context for this tick</param>
    void Dispatch<T>(T command, SimulationContext context) where T : struct;
}
