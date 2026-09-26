// UNNAMED Presentation - a person's drawn body, greybox or from the asset library, posed from the simulation each frame
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Player;

/// <summary>
/// A person as drawn - the character, an NPC, a companion: the greybox mannequin (<see cref="Avatar"/>) or the asset library's skinned
/// figure. Everything it shows comes from the simulation each frame; it keeps only what drawing needs between frames.
/// </summary>
public abstract partial class Figure : Node3D
{
    /// <summary>How far into a crouch the body is drawn, 0 standing to 1 crouched.</summary>
    public abstract float Crouch { get; }

    /// <summary>What the body is doing in combat this frame.</summary>
    public abstract void SetStance(CombatStance stance);

    /// <summary>First person: the body only casts its shadow where it would block the eye.</summary>
    public abstract void SetFirstPerson(bool firstPerson);

    /// <summary>Crouched or standing, in the air or not (the simulation's posture).</summary>
    public abstract void SetPosture(bool crouched, bool airborne);

    /// <summary>Place the body where the simulation says, turned towards its facing, and animate its gait.</summary>
    public abstract void Pose(Vector3 feet, float facingRadians, float speedMetresPerSecond, double delta);

    /// <summary>In a conversation.</summary>
    public virtual void SetTalking(bool talking)
    {
    }

    /// <summary>Downed (a companion). False when this figure cannot show it itself (the greybox is laid down by its owner).</summary>
    public virtual bool SetDowned(bool downed) => false;

    /// <summary>The clip state being drawn (a skinned body's), for the state log; null for a figure without clips.</summary>
    public virtual string? ClipState => null;

    /// <summary>What is in the hand, by its item definition ID, or nothing.</summary>
    public virtual void Hold(string? itemDefId)
    {
    }

    /// <summary>Where a working gathers in the casting hand, in world space, or null when this figure has no casting hand to show.</summary>
    public virtual Vector3? CastPoint => null;

    /// <summary>A blow of theirs just landed (a companion's, which the simulation reports only as it lands).</summary>
    public virtual void Strike()
    {
    }

    /// <summary>The body reaches for something: a door, a switch, a node, an item on the ground, a companion to help up.</summary>
    public virtual void Interact()
    {
    }
}
