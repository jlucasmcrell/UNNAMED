// UNNAMED Application - In-Process Command Bus
// This file implements the synchronous in-process command bus
// No Godot references - this is pure C# application orchestration

using System.Collections.Concurrent;
using UNNAMED.Domain;

namespace UNNAMED.Application;

/// <summary>
/// Synchronous in-process command bus implementation.
/// Commands are dispatched immediately to their handling systems.
/// This is a simple in-memory bus for single-process use.
/// </summary>
public class CommandBus : ICommandBus
{
    private readonly ConcurrentDictionary<Type, Type> _commandHandlerMap = new();
    private readonly ISystem[] _systems;

    /// <summary>
    /// Create a new CommandBus.
    /// </summary>
    /// <param name="systems">The domain systems to receive commands</param>
    public CommandBus(ISystem[] systems)
    {
        _systems = systems ?? throw new ArgumentNullException(nameof(systems));
    }

    /// <summary>
    /// Map a command type to a system type that handles it.
    /// This should be called during composition before dispatch.
    /// </summary>
    /// <typeparam name="TCommand">The command type</typeparam>
    /// <typeparam name="TSystem">The system type that handles this command</typeparam>
    public void MapCommand<TCommand, TSystem>()
        where TSystem : ISystem
    {
        _commandHandlerMap[typeof(TCommand)] = typeof(TSystem);
    }

    /// <summary>
    /// Dispatch a command to its owning system for handling.
    /// </summary>
    /// <typeparam name="T">The command type</typeparam>
    /// <param name="command">The command instance</param>
    /// <param name="context">The simulation context</param>
    public void Dispatch<T>(T command, SimulationContext context) where T : struct
    {
        var commandType = typeof(T);
        if (!_commandHandlerMap.TryGetValue(commandType, out var systemType))
        {
            throw new InvalidOperationException($"No handler registered for command type {commandType.Name}");
        }

        var system = _systems.FirstOrDefault(s => s.GetType() == systemType);
        if (system == null)
        {
            throw new InvalidOperationException($"System {systemType.Name} not found in registry");
        }

        HandleCommand(system, command, context);
    }

    private void HandleCommand<T>(ISystem system, T command, SimulationContext context)
    {
        // For Phase 1, we use a simple convention: systems implement public methods
        // named "Handle" with the command type as a parameter.
        // In a production system, this would use compiled delegates or a more
        // sophisticated routing mechanism.
        var handleMethod = system.GetType().GetMethod("Handle", [typeof(T), typeof(SimulationContext)]);
        if (handleMethod != null)
        {
            handleMethod.Invoke(system, [command, context]);
        }
        else
        {
            // Fallback: system may handle commands through its Tick method
            // for batch processing. In Phase 1, commands are processed immediately.
            throw new InvalidOperationException(
                $"System {system.GetType().Name} does not have a Handle method for {typeof(T).Name}");
        }
    }
}
