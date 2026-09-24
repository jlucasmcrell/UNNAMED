// UNNAMED Domain Tests - World State Implementation
// This file provides the concrete implementation of IWorldState and IWorldStateWriter
// for testing purposes. This is NOT part of production code, only for tests.
// No Godot references - headless domain testing

using System.Collections.Concurrent;
using UNNAMED.Domain;

namespace UNNAMED.Domain.Tests;

/// <summary>
/// Test implementation of IWorldState and IWorldStateWriter.
/// Uses a concurrent dictionary for state storage.
/// This is for testing only - production would have a different implementation.
/// </summary>
internal class TestWorldState : IWorldState, IWorldStateWriter
{
    private readonly ConcurrentDictionary<(Type Type, EntityId Id), object> _state = new();

    /// <summary>
    /// Read a state record by entity ID.
    /// Returns default(T) if the record does not exist.
    /// </summary>
    public T Read<T>(EntityId entityId) where T : struct
    {
        if (_state.TryGetValue((typeof(T), entityId), out var stored) && stored is T typed)
        {
            return typed;
        }
        return default!;
    }

    /// <summary>
    /// Try to read a state record by entity ID.
    /// </summary>
    public bool TryRead<T>(EntityId entityId, out T value) where T : struct
    {
        if (_state.TryGetValue((typeof(T), entityId), out var stored) && stored is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>
    /// Check if a state record exists for the given entity ID.
    /// </summary>
    public bool Exists<T>(EntityId entityId) where T : struct
    {
        return _state.ContainsKey((typeof(T), entityId));
    }

    /// <summary>
    /// Write a state record for the given entity ID.
    /// Implements IWorldStateWriter.
    /// </summary>
    public void Write<T>(EntityId entityId, T value) where T : struct
    {
        _state[(typeof(T), entityId)] = value;
    }

    /// <summary>
    /// Delete a state record for the given entity ID.
    /// Implements IWorldStateWriter.
    /// </summary>
    public void Delete<T>(EntityId entityId) where T : struct
    {
        _state.TryRemove((typeof(T), entityId), out _);
    }

    /// <summary>
    /// Clear all state (for test cleanup).
    /// </summary>
    public void Clear()
    {
        _state.Clear();
    }
}
