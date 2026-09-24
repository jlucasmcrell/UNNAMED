// UNNAMED Domain - System Interface
// This file defines the domain system interface
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

/// <summary>
/// A domain system owns a slice of authoritative state and handles commands addressed to it.
/// Systems receive both the reader (IWorldState) and the internal writer (IWorldStateWriter)
/// through Configure. Both the writer and Configure are internal to Domain, so only Domain
/// can hand out write access (ARCHITECTURE.md §5).
/// </summary>
/// <remarks>
/// The interface is public so Application can hold and tick systems; it cannot configure them,
/// because it cannot name the writer. Implementations implement Configure explicitly.
/// </remarks>
public interface ISystem
{
    /// <summary>
    /// Configure this system with its dependencies.
    /// Called once during composition/boot, before any ticks.
    /// </summary>
    /// <param name="bus">Command bus for dispatching commands to other systems</param>
    /// <param name="events">Event bus for publishing events</param>
    /// <param name="world">Read-only interface to authoritative state</param>
    /// <param name="writer">Write interface for mutating this system's owned slice</param>
    internal void Configure(
        ICommandBus bus,
        IEventBus events,
        IWorldState world,
        IWorldStateWriter writer);

    /// <summary>
    /// Tick the system. Called once per simulation frame.
    /// Optional for some systems; systems that don't evolve state over time can be pure.
    /// </summary>
    /// <param name="ctx">The simulation context for this tick</param>
    void Tick(SimulationContext ctx);
}
