// UNNAMED Domain - In-Process Event Bus
// This file implements the synchronous in-process event bus
// No Godot references - this is pure C# domain logic

using System.Collections.Concurrent;

namespace UNNAMED.Domain;

/// <summary>
/// Interface for event publication and subscription.
/// Events are published after state mutations and can be subscribed to by views.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Subscribe to an event type.
    /// </summary>
    /// <typeparam name="T">The event type</typeparam>
    /// <param name="handler">The handler action</param>
    void Subscribe<T>(Action<T> handler);

    /// <summary>
    /// Unsubscribe from an event type.
    /// </summary>
    /// <typeparam name="T">The event type</typeparam>
    /// <param name="handler">The handler action</param>
    void Unsubscribe<T>(Action<T> handler);

    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
    /// <typeparam name="T">The event type</typeparam>
    /// <param name="event">The event instance to publish</param>
    void Publish<T>(T @event);
}
