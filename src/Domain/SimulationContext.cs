// UNNAMED Domain - Simulation Context
// This file defines the simulation context passed toTick methods and command handlers
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

/// <summary>
/// Context passed to Tick methods and command handlers.
/// Contains simulation time information for deterministic execution.
/// </summary>
/// <param name="Tick">The current tick number</param>
/// <param name="DeltaTicks">The number of ticks since the last frame</param>
public readonly record struct SimulationContext(int Tick, int DeltaTicks);
