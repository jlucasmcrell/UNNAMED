// UNNAMED Core Types
// This file brings all domain core types together
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

// Core types:
// - EntityId: ULID-based unique identifier for entities
// - SimulationContext: Tick context passed to systems
// - IWorldState: Read-only interface to domain state
// - IWorldStateWriter: Internal write interface (internal to Domain)
// - ICommandBus: Command routing interface
// - IEventBus: Event publication/subscription interface
// - ISystem: Domain system interface
//
// Design notes:
// - IWorldStateWriter is internal to Domain assembly
// - This ensures Application and Presentation cannot name the writer type
// - Only domain systems receive the writer through Configure()
// - Application layer wires systems but cannot write state directly
