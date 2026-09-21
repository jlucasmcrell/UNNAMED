// UNNAMED Domain Tests - Command Event Test Base
// This file provides base functionality for command → event flow tests
// No Godot references - headless domain testing

using UNNAMED.Domain;
using UNNAMED.Application;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// Test fixture for M1 command → event flow tests.
/// Sets up a minimal world state and systems for testing.
/// </summary>
public class CommandEventTestBase : IDisposable
{
    internal TestWorldState WorldState { get; } = new();
    protected CommandBus? CommandBus { get; private set; }
    protected EventBus? EventBus { get; private set; }
    protected List<object> PublishedEvents { get; } = new();

    public CommandEventTestBase()
    {
        // Set up the test infrastructure
        EventBus = new EventBus();
        
        // Subscribe to events for verification
        EventBus.Subscribe<ItemPickedUpEvent>(e => PublishedEvents.Add(e));
    }

    public void Dispose()
    {
        WorldState.Clear();
        PublishedEvents.Clear();
    }
}
