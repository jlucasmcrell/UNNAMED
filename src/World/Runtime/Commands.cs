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

/// <summary>Jump (the owner's M6 playtest): from the ground, and from a crouch where there is room to stand.</summary>
public sealed record JumpCommand(EntityId Actor) : GameCommand(Actor);

/// <summary>Crouch, or stand up where there is room to (the owner's M6 playtest).</summary>
public sealed record CrouchCommand(EntityId Actor, bool Crouched) : GameCommand(Actor);

/// <summary>Use an interactable - in Phase 1, open or close a door. Range is checked from the actor's body.</summary>
public sealed record InteractCommand(EntityId Actor, string TargetKey) : GameCommand(Actor);

/// <summary>
/// Enter the space a portal leads to (M8 D10). The body is placed at the portal's interior anchor and the
/// portal is remembered, so leaving returns the character to the anchor they actually used. Refused when
/// the portal is unknown or the actor is not standing at it.
/// </summary>
public sealed record EnterSpaceCommand(EntityId Actor, string PortalKey) : GameCommand(Actor);

/// <summary>
/// Leave the space the character is in (M8 D10), arriving at the exterior anchor of the portal they came in
/// by. Refused when they are already outside, or when the portal they entered by is not declared.
/// </summary>
public sealed record LeaveSpaceCommand(EntityId Actor) : GameCommand(Actor);

/// <summary>A command as the simulation applied it: the tick boundary it applied at, and why it was refused, if it was.</summary>
public sealed record LoggedCommand(long Tick, long Sequence, GameCommand Command, string? RejectedReason);
