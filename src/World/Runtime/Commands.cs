// UNNAMED World - the commands presentation may submit (ARCHITECTURE.md §4.1, D-11)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>
/// An intent from outside the simulation. Commands carry no behaviour and may be rejected; a rejection is an
/// event with a reason, never an exception. These are the only way presentation changes anything (D-11), and
/// they are the same at every camera distance: the simulation never knows where the camera is.
/// </summary>
public abstract record GameCommand(EntityId Actor);

/// <summary>Set the actor's movement intent. The movement system integrates it every tick until the next one.</summary>
public sealed record MoveCommand(EntityId Actor, MoveIntent Intent) : GameCommand(Actor);

/// <summary>Use an interactable - in Phase 1, open or close a door. Range is checked from the actor's body.</summary>
public sealed record InteractCommand(EntityId Actor, string TargetKey) : GameCommand(Actor);

/// <summary>A command as the simulation applied it: the tick boundary it applied at, and why it was refused, if it was.</summary>
public sealed record LoggedCommand(long Tick, long Sequence, GameCommand Command, string? RejectedReason);
