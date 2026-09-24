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
/// <remarks>
/// Every subscriber is an observer - presentation, a harness, a test; no system of the simulation listens here - but it runs while the
/// tick that published the event is still running. Built with a failure handler, the bus isolates a subscriber that throws: the
/// exception goes to the handler, and the other subscribers and the tick carry on, so a broken view cannot leave a tick half done to be
/// run again (the Phase-1 technical audit, H-02). Built without one, a subscriber's exception propagates, as a test's assertion must.
/// </remarks>
public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, object> _subscribers = new();
    private readonly Action<Exception, object>? _onSubscriberFailed;

    public EventBus()
    {
    }

    /// <param name="onSubscriberFailed">Told of each subscriber that threw, and the event it threw on.</param>
    public EventBus(Action<Exception, object> onSubscriberFailed) => _onSubscriberFailed = onSubscriberFailed;

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
            // A snapshot: a subscriber may subscribe or unsubscribe as it runs.
            foreach (var handler in ((List<Action<T>>)subscriberList).ToArray())
            {
                if (_onSubscriberFailed is null)
                {
                    handler(@event);
                    continue;
                }
                try
                {
                    handler(@event);
                }
                catch (Exception e)
                {
                    _onSubscriberFailed(e, @event!);
                }
            }
        }
    }
}
