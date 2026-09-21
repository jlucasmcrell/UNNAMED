// UNNAMED Domain Tests - PickUpItem Tests
// This file demonstrates the M1 command → system → event flow
// No Godot references - headless domain testing

using UNNAMED.Domain;
using UNNAMED.Application;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// Test class for PickUpItem command flow.
/// This is the M1 demonstrating: command → system → event → state mutation
/// </summary>
public class PickUpItemTests : IDisposable
{
    private readonly TestWorldState _worldState = new();
    private readonly CommandBus _commandBus;
    private readonly EventBus _eventBus;
    private readonly List<object> _publishedEvents = new();

    public PickUpItemTests()
    {
        // Set up a simple in-memory command/event bus for testing
        _commandBus = new CommandBus([]);
        _eventBus = new EventBus();
        _eventBus.Subscribe<ItemPickedUpEvent>(e => _publishedEvents.Add(e));
    }

    public void Dispose()
    {
        _worldState.Clear();
        _publishedEvents.Clear();
    }

    [Fact]
    public void PickUpItem_ItemsMoved_FromContainerToActor()
    {
        // Arrange: Create test data
        var actorId = EntityId.NewId();
        var containerId = EntityId.NewId();
        var itemId = EntityId.NewId();
        var count = 5;
        var tick = 1;

        // Set up initial state: actor exists, container has items
        _worldState.Write(actorId, new ItemActorState(Array.Empty<ContainerItemEntry>()));
        _worldState.Write(containerId, new ItemContainerState(new[]
        {
            new ContainerItemEntry(itemId, count)
        }));

        // Act: Process the command (in M1, this goes through command bus → system)
        // For M1 demonstration, we call the handler directly
        var handler = new PickupSystem();
        handler.Configure(
            // For testing, we can pass null for bus since we don't cross-system commands
            _commandBus,
            _eventBus,
            _worldState,
            _worldState);

        var command = new PickUpItemCommand(actorId, containerId, itemId, count);
        handler.Handle(command, new SimulationContext(tick, 1));

        // Assert: Item should be in actor's inventory
        var actorState = _worldState.Read<ItemActorState>(actorId);
        Assert.Equal(1, actorState.Items.Length);
        Assert.Equal(itemId, actorState.Items[0].ItemId);
        Assert.Equal(count, actorState.Items[0].Count);

        // Assert: Item should be removed from container
        var containerState = _worldState.Read<ItemContainerState>(containerId);
        Assert.Single(containerState.Items);
        Assert.Equal(itemId, containerState.Items[0].ItemId);
        Assert.Equal(0, containerState.Items[0].Count); // All items moved

        // Assert: Event was published
        Assert.Single(_publishedEvents);
        var @event = Assert.IsType<ItemPickedUpEvent>(_publishedEvents[0]);
        Assert.Equal(actorId, @event.ActorId);
        Assert.Equal(containerId, @event.ContainerId);
        Assert.Equal(itemId, @event.ItemId);
        Assert.Equal(count, @event.Count);
    }

    [Fact]
    public void PickUpItem_ItemsMoved_PartialQuantity()
    {
        // Arrange: Create test data with partial move
        var actorId = EntityId.NewId();
        var containerId = EntityId.NewId();
        var itemId = EntityId.NewId();
        var initialCount = 10;
        var pickedUpCount = 3;
        var tick = 1;

        // Set up initial state
        _worldState.Write(actorId, new ItemActorState(Array.Empty<ContainerItemEntry>()));
        _worldState.Write(containerId, new ItemContainerState(new[]
        {
            new ContainerItemEntry(itemId, initialCount)
        }));

        // Act: Pick up partial quantity
        var handler = new PickupSystem();
        handler.Configure(
            _commandBus,
            _eventBus,
            _worldState,
            _worldState);

        var command = new PickUpItemCommand(actorId, containerId, itemId, pickedUpCount);
        handler.Handle(command, new SimulationContext(tick, 1));

        // Assert: Partial amount should be in actor's inventory
        var actorState = _worldState.Read<ItemActorState>(actorId);
        Assert.Equal(1, actorState.Items.Length);
        Assert.Equal(itemId, actorState.Items[0].ItemId);
        Assert.Equal(pickedUpCount, actorState.Items[0].Count);

        // Assert: Remaining amount should stay in container
        var containerState = _worldState.Read<ItemContainerState>(containerId);
        Assert.Single(containerState.Items);
        Assert.Equal(itemId, containerState.Items[0].ItemId);
        Assert.Equal(initialCount - pickedUpCount, containerState.Items[0].Count);
    }

    [Fact]
    public void PickUpItem_NonExistentItem_ThrowsInvalidOperationException()
    {
        // Arrange
        var actorId = EntityId.NewId();
        var containerId = EntityId.NewId();
        var itemId = EntityId.NewId();
        var tick = 1;

        _worldState.Write(actorId, new ItemActorState(Array.Empty<ContainerItemEntry>()));
        _worldState.Write(containerId, new ItemContainerState(Array.Empty<ContainerItemEntry>()));

        // Act & Assert
        var handler = new PickupSystem();
        handler.Configure(
            _commandBus,
            _eventBus,
            _worldState,
            _worldState);

        var command = new PickUpItemCommand(actorId, containerId, itemId, 1);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            handler.Handle(command, new SimulationContext(tick, 1)));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void PickUpItem_InsufficientQuantity_ThrowsInvalidOperationException()
    {
        // Arrange
        var actorId = EntityId.NewId();
        var containerId = EntityId.NewId();
        var itemId = EntityId.NewId();
        var tick = 1;

        _worldState.Write(actorId, new ItemActorState(Array.Empty<ContainerItemEntry>()));
        _worldState.Write(containerId, new ItemContainerState(new[]
        {
            new ContainerItemEntry(itemId, 2)
        }));

        // Act & Assert
        var handler = new PickupSystem();
        handler.Configure(
            _commandBus,
            _eventBus,
            _worldState,
            _worldState);

        var command = new PickUpItemCommand(actorId, containerId, itemId, 5);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            handler.Handle(command, new SimulationContext(tick, 1)));

        Assert.Contains("only has", ex.Message);
    }
}
