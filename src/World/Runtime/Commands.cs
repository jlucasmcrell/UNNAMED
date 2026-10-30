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

/// <summary>
/// Crouch, or stand up where there is room to (the owner's M6 playtest). Gliding, a crouch flares the wings to brake and coming out of it
/// spreads them again (Kal).
/// </summary>
public sealed record CrouchCommand(EntityId Actor, bool Crouched) : GameCommand(Actor);

/// <summary>
/// Spread the wings in the air, or fold them (Kal: Wing Glide, RACE_ABILITIES_PROPOSAL.md §3.2): presentation sends it when jump is held past
/// the jump itself, and again when it is let go. A body without wings is refused.
/// </summary>
public sealed record GlideCommand(EntityId Actor, bool Spread) : GameCommand(Actor);

/// <summary>
/// Put a knife into a creature that has not noticed the character (STEALTH §12-§14; owner finding 27). The body nearest and inside the
/// weapon's reach is the target; the outcome is an armour-and-vital check, so an armoured or well-built body survives and turns.
/// </summary>
public sealed record TakedownCommand(EntityId Actor, bool NonLethal = false) : GameCommand(Actor);

/// <summary>
/// Carry a body into cover (STEALTH §6.3): the corpse is laid in the nearest cover that hides it whole, and from then on nobody walking past
/// that ground finds it. No separate hidden flag is needed - a body in the shadow alcove is a body the same geometry hides.
/// </summary>
public sealed record HideBodyCommand(EntityId Actor, EntityId Target) : GameCommand(Actor);

/// <summary>
/// Take hold of a body and pull it along the ground (STEALTH §6.2): it comes with the character instead of being carried away whole, a metre or
/// two at a time, and it stays where the light can find it until the character stops to hide it properly.
/// </summary>
public sealed record DragBodyCommand(EntityId Actor, EntityId Target) : GameCommand(Actor);

/// <summary>
/// Put an authored light out, or light it again (STEALTH §2.2): a lamp the character is standing beside. The stealth player's own lever on the world,
/// and it darkens the same number the sight check reads.
/// </summary>
public sealed record DouseLightCommand(EntityId Actor, string? LightKey, bool Lit, bool ByShot = false) : GameCommand(Actor);

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
