// UNNAMED Application - In-Process Event Bus
// This file implements the synchronous in-process event bus
// No Godot references - this is pure C# application orchestration

using System.Collections.Concurrent;
using UNNAMED.Domain;

namespace UNNAMED.Application;

/// <summary>
/// Synchronous in-process event bus implementation.
/// Events are published immediately to all subscribers.
/// This is a simple in-memory bus for single-process use.
/// </summary>
public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, object> _subscribers = new();

    /// <summary>
    /// Subscribe to an event type.
    /// </summary>
    /// <typeparam name="T">The event type</typeparam>
    /// <param name="handler">The handler action</param>
    public void Subscribe<T>(Action<T> handler)
    {
        var eventType = typeof(T);
        var subscriberList = (List<Action<T>>)_subscribers.GetOrAdd(eventType, _ => new List<Action<T>>());
        subscriberList.Add(handler);
    }

    /// <summary>
    /// Unsubscribe from an event type.
    /// </summary>
    /// <typeparam name="T">The event type</typeparam>
    /// <param name="handler">The handler action</param>
    public void Unsubscribe<T>(Action<T> handler)
    {
        var eventType = typeof(T);
        if (_subscribers.TryGetValue(eventType, out var subscriberList))
        {
            ((List<Action<T>>)subscriberList).Remove(handler);
        }
    }

    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
    /// <typeparam name="T">The event type</typeparam>
    /// <param name="event">The event instance to publish</param>
    public void Publish<T>(T @event)
    {
        var eventType = typeof(T);
        if (_subscribers.TryGetValue(eventType, out var subscriberList))
        {
            foreach (var handler in (List<Action<T>>)subscriberList)
            {
                handler(@event);
            }
        }
    }
}
