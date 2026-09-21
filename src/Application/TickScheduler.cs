// UNNAMED Application - In-Process Tick Scheduler
// This file implements the deterministic tick scheduler
// No Godot references - this is pure C# application orchestration

using UNNAMED.Domain;

namespace UNNAMED.Application;

/// <summary>
/// Simple deterministic tick scheduler.
/// Runs systems in order, collecting any dirty entities that need saving.
/// In Phase 1, this is single-threaded and runs to completion.
/// </summary>
public class TickScheduler
{
    private readonly string[] _systemNames;
    private readonly ICommandBus _commandBus;
    private readonly IEventBus _eventBus;

    /// <summary>
    /// Create a new TickScheduler.
    /// </summary>
    /// <param name="systemNames">The system names to schedule (by Type name)</param>
    /// <param name="commandBus">Command bus for cross-system communication</param>
    /// <param name="eventBus">Event bus for publishing events</param>
    public TickScheduler(
        string[] systemNames,
        ICommandBus commandBus,
        IEventBus eventBus)
    {
        _systemNames = systemNames ?? throw new ArgumentNullException(nameof(systemNames));
        _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <summary>
    /// Run one simulation tick.
    /// Each system gets a turn to process commands and update state.
    /// </summary>
    /// <param name="tick">The current tick number</param>
    public void RunTick(int tick)
    {
        var context = new SimulationContext(tick, 1);

        // In a full implementation, systems would be instantiated and stored.
        // For Phase 1, this is a placeholder for M1's minimal implementation.
        // The actual system execution would happen here.

        // In a full implementation, we would also:
        // - Process any queued commands
        // - Collect dirty entities for persistence
        // - Run post-tick cleanup
    }

    /// <summary>
    /// Run multiple simulation ticks.
    /// Useful for batch testing or catching up after offline time.
    /// </summary>
    /// <param name="startTick">The starting tick number</param>
    /// <param name="count">The number of ticks to run</param>
    public void RunTicks(int startTick, int count)
    {
        for (var i = 0; i < count; i++)
        {
            RunTick(startTick + i);
        }
    }
}
