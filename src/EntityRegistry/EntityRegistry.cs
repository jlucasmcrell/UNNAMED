// UNNAMED Entity Registry
// Implements D-10: Entity Registry owns identity (creation, assignment, lookup, lifetime, destruction)
// NO gameplay rules - only entity identity management
// No Godot references

using System.Collections.Immutable;
using UNNAMED.Domain;

namespace UNNAMED.EntityRegistry;

/// <summary>
/// Represents an entity instance in the simulation.
/// Per D-10: Entity Registry owns creation, instance-ID assignment, and lookup for every runtime instance.
/// </summary>
/// <param name="InstanceId">The ULID assigned by Entity Registry (runtime identity)</param>
/// <param name="DefinitionId">The definition this entity is an instance of (content identity)</param>
public readonly record struct EntityInstance(DefinitionId DefinitionId, EntityId InstanceId);

/// <summary>
/// Entity registry that owns entity identity management.
/// Per D-10: Entity Registry owns:
/// - Entity creation identity
/// - Instance-ID assignment
/// - Entity lookup
/// - Existence/lifetime identity
/// - Destruction/tombstone identity
/// 
/// Entity Registry does NOT own:
/// - Health, inventory, combat, movement, quests, crafting, gameplay decisions
/// </summary>
public sealed class EntityRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<EntityId, EntityInstance> _instances = new();
    private readonly Dictionary<DefinitionId, List<EntityId>> _definitionInstances = new();
    private readonly HashSet<EntityId> _deadEntities = new();

    /// <summary>
    /// Create a new entity instance with a unique ULID.
    /// </summary>
    /// <param name="definitionId">The definition this entity is an instance of</param>
    /// <param name="instanceId">The assigned instance ID (ULID)</param>
    /// <returns>The created entity instance</returns>
    public EntityInstance CreateEntity(DefinitionId definitionId, EntityId instanceId)
    {
        if (definitionId.ToString().Length == 0)
            throw new ArgumentException("Definition ID cannot be empty", nameof(definitionId));

        lock (_lock)
        {
            if (_instances.ContainsKey(instanceId))
                throw new InvalidOperationException($"Instance ID {instanceId} already exists");

            var entity = new EntityInstance(definitionId, instanceId);
            _instances[instanceId] = entity;
            
            if (!_definitionInstances.TryGetValue(definitionId, out var list))
            {
                list = new List<EntityId>();
                _definitionInstances[definitionId] = list;
            }
            list.Add(instanceId);

            return entity;
        }
    }

    /// <summary>
    /// Create a new entity instance of an explicit kind, with a fresh instance ID.
    /// </summary>
    public EntityInstance CreateEntity(DefinitionId definitionId, EntityKind kind) =>
        CreateEntity(definitionId, EntityId.NewId(kind));

    /// <summary>
    /// Create a new entity instance whose kind follows from its definition (item, creature, npc,
    /// quest). Any other instance category must be created with an explicit <see cref="EntityKind"/>.
    /// </summary>
    public EntityInstance CreateEntity(DefinitionId definitionId)
    {
        if (!EntityKinds.TryInferFromDefinition(definitionId, out var kind))
            throw new ArgumentException(
                $"Cannot infer an instance kind from '{definitionId}'; pass an EntityKind explicitly",
                nameof(definitionId));
        return CreateEntity(definitionId, kind);
    }

    /// <summary>
    /// Try to get an entity by its instance ID.
    /// </summary>
    /// <param name="instanceId">The ULID of the entity</param>
    /// <param name="entity">The entity instance if found</param>
    /// <returns>True if the entity exists, false otherwise</returns>
    public bool TryGetEntity(EntityId instanceId, out EntityInstance entity)
    {
        lock (_lock)
        {
            return _instances.TryGetValue(instanceId, out entity) && 
                   !_deadEntities.Contains(instanceId);
        }
    }

    /// <summary>
    /// Get an entity by its instance ID (throws if not found).
    /// </summary>
    /// <param name="instanceId">The ULID of the entity</param>
    /// <returns>The entity instance</returns>
    public EntityInstance GetEntity(EntityId instanceId)
    {
        if (TryGetEntity(instanceId, out var entity))
            return entity;
        
        throw new KeyNotFoundException($"Entity with instance ID {instanceId} not found");
    }

    /// <summary>
    /// Check if an entity exists.
    /// </summary>
    /// <param name="instanceId">The ULID of the entity</param>
    /// <returns>True if the entity exists and is not dead, false otherwise</returns>
    public bool Exists(EntityId instanceId)
    {
        lock (_lock)
        {
            return _instances.ContainsKey(instanceId) && 
                   !_deadEntities.Contains(instanceId);
        }
    }

    /// <summary>
    /// Mark an entity as destroyed (tombstone).
    /// </summary>
    /// <param name="instanceId">The ULID of the entity to destroy</param>
    public void DestroyEntity(EntityId instanceId)
    {
        lock (_lock)
        {
            if (!_instances.ContainsKey(instanceId))
                throw new KeyNotFoundException($"Entity with instance ID {instanceId} not found");

            _deadEntities.Add(instanceId);
        }
    }

    /// <summary>
    /// Get all entities of a particular definition.
    /// </summary>
    /// <param name="definitionId">The definition to filter by</param>
    /// <returns>All instances of this definition</returns>
    public IReadOnlyList<EntityId> GetEntitiesByDefinition(DefinitionId definitionId)
    {
        lock (_lock)
        {
            if (_definitionInstances.TryGetValue(definitionId, out var list))
            {
                return list.Where(id => !_deadEntities.Contains(id)).ToImmutableList();
            }
            return ImmutableList<EntityId>.Empty;
        }
    }

    /// <summary>
    /// Get the count of all active (non-dead) entities.
    /// </summary>
    public int EntityCount
    {
        get
        {
            lock (_lock)
            {
                return _instances.Count - _deadEntities.Count;
            }
        }
    }

    /// <summary>
    /// Get all active entity instances.
    /// </summary>
    public IReadOnlyCollection<EntityInstance> GetAllEntities()
    {
        lock (_lock)
        {
            return _instances.Values
                .Where(e => !_deadEntities.Contains(e.InstanceId))
                .ToImmutableList();
        }
    }

    /// <summary>
    /// Clear all entities (for reset/baseline regeneration).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _instances.Clear();
            _definitionInstances.Clear();
            _deadEntities.Clear();
        }
    }
}

/// <summary>
/// Extension methods for EntityRegistry.
/// </summary>
public static class EntityRegistryExtensions
{
    /// <summary>
    /// Create a new entity with a random ULID.
    /// </summary>
    public static EntityInstance CreateEntity(this EntityRegistry registry, string definitionId)
    {
        var defId = DefinitionId.Parse(definitionId);
        return registry.CreateEntity(defId);
    }

    /// <summary>
    /// Try to get an entity by its instance ID string.
    /// </summary>
    public static bool TryGetEntity(this EntityRegistry registry, string instanceId, out EntityInstance entity)
    {
        if (EntityId.TryParse(instanceId, out var id))
        {
            return registry.TryGetEntity(id, out entity);
        }
        entity = default;
        return false;
    }

    /// <summary>
    /// Get an entity by its instance ID string.
    /// </summary>
    public static EntityInstance GetEntity(this EntityRegistry registry, string instanceId)
    {
        if (EntityId.TryParse(instanceId, out var id))
        {
            return registry.GetEntity(id);
        }
        throw new ArgumentException($"Invalid instance ID: {instanceId}", nameof(instanceId));
    }
}
