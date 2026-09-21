// UNNAMED Domain - System Interface
// This file defines the domain system interface
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

/// <summary>
/// A domain system owns a slice of authoritative state and handles commands addressed to it.
/// Systems receive both the reader (IWorldState) and internal writer (IWorldStateWriter)
/// through their Configure method. The writer is internal to Domain, so only systems
/// can call Write operations.
/// </summary>
/// <remarks>
/// This interface is public because the Application layer (wiring/composition) needs to
/// create instances and call Configure. The internal IWorldStateWriter is never exposed
/// outside Domain - Application can hold IWorldState but cannot name IWorldStateWriter.
/// 
/// The Configure pattern allows the Application to pass the internal writer through
/// a type-safe boundary that the compiler enforces.
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
    void Configure(
        ICommandBus bus,
        IEventBus events,
        IWorldState world,
        ISystemWriterInternals writer);

    /// <summary>
    /// Tick the system. Called once per simulation frame.
    /// Optional for some systems; systems that don't evolve state over time can be pure.
    /// </summary>
    /// <param name="ctx">The simulation context for this tick</param>
    void Tick(SimulationContext ctx);
}
