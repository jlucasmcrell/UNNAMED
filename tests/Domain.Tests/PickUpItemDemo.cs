// UNNAMED Domain Tests - PickUpItem Demo (with command bus wiring)
// This file demonstrates the M1 command → system → event → state mutation flow
// No Godot references - headless domain testing

using UNNAMED.Domain;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// Command: Pick up an item from a container or location.
/// This is the core M1 demonstration flow.
/// </summary>
public readonly record struct PickUpItemCommand(
    EntityId ActorId,
    EntityId ContainerId,
    EntityId ItemId,
    int Count);

/// <summary>
/// Event: Item was successfully picked up.
/// </summary>
public readonly record struct ItemPickedUpEvent(
    EntityId ActorId,
    EntityId ContainerId,
    EntityId ItemId,
    int Count);

/// <summary>
/// Example system that handles PickUpItem commands.
/// In a full implementation, this would be a proper Inventory system.
/// For M1, we implement just enough to prove the pattern.
/// </summary>
public class PickupSystem : ISystem
{
    private ICommandBus? _bus;
    private IEventBus? _events;
    private IWorldState? _world;
    private IWorldStateWriter? _writer;

    void ISystem.Configure(
        ICommandBus bus,
        IEventBus events,
        IWorldState world,
        IWorldStateWriter writer)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void Tick(SimulationContext context)
    {
        // For Phase 1, commands are processed immediately via Dispatch.
        // The system does not need to do anything in Tick.
    }

    /// <summary>
    /// Handle a PickUpItem command.
    /// This is the demonstration of the M1 flow:
    /// 1. Validate the command (preconditions)
    /// 2. Mutate state (pick up the item)
    /// 3. Publish an event
    /// </summary>
    public void Handle(PickUpItemCommand command, SimulationContext context)
    {
        var world = _world!; // Assert: world is always set after Configure

        // Precondition: Actor exists
        if (!world.Exists<ItemActorState>(command.ActorId))
        {
            throw new InvalidOperationException($"Actor {command.ActorId} does not exist");
        }

        // Precondition: Container exists
        if (!world.Exists<ItemContainerState>(command.ContainerId))
        {
            throw new InvalidOperationException($"Container {command.ContainerId} does not exist");
        }

        // Precondition: Item exists in container
        var containerState = world.Read<ItemContainerState>(command.ContainerId);
        var itemEntry = containerState.Items.FirstOrDefault(i => i.ItemId == command.ItemId);
        if (itemEntry.ItemId == default)
        {
            throw new InvalidOperationException($"Item {command.ItemId} not found in container {command.ContainerId}");
        }

        if (itemEntry.Count < command.Count)
        {
            throw new InvalidOperationException(
                $"Container {command.ContainerId} only has {itemEntry.Count} of item {command.ItemId}, requested {command.Count}");
        }

        // Mutate state: remove item from container
        var newContainerItems = containerState.Items
            .Where(i => i.ItemId != command.ItemId)
            .Append(new ContainerItemEntry(command.ItemId, itemEntry.Count - command.Count))
            .ToArray();

        _writer!.Write(command.ContainerId, new ItemContainerState(newContainerItems));

        // Mutate state: add item to actor's inventory
        var actorState = world.Read<ItemActorState>(command.ActorId);
        var newActorItems = actorState.Items
            .Where(i => i.ItemId != command.ItemId)
            .Append(new ContainerItemEntry(command.ItemId, command.Count))
            .ToArray();

        _writer!.Write(command.ActorId, new ItemActorState(newActorItems));

        // Publish event
        _events!.Publish(new ItemPickedUpEvent(
            command.ActorId,
            command.ContainerId,
            command.ItemId,
            command.Count));
    }
}

/// <summary>
/// State: Actor with inventory.
/// This is a simplified state for M1 demonstration.
/// </summary>
public readonly record struct ItemActorState(
    ContainerItemEntry[] Items);

/// <summary>
/// State: Container with items.
/// This is a simplified state for M1 demonstration.
/// </summary>
public readonly record struct ItemContainerState(
    ContainerItemEntry[] Items);

/// <summary>
/// Entry: Item in a container/inventory.
/// </summary>
public readonly record struct ContainerItemEntry(
    EntityId ItemId,
    int Count);
