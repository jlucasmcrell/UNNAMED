// UNNAMED Presentation - a weapon from the asset library held by the grip socket its asset declares
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A weapon model held by its own <c>SOCK_grip_primary</c> (WAVE_0_MODULAR_ASSET_STANDARD.md: the socket's +X is its primary axis,
/// pointing toward the head; its +Y the secondary that fixes the roll), laid on a grip frame - where a hand closes and which way the
/// held thing points. The model keeps its authored size. One without a grip socket is not drawn (the greybox stands in) and is reported.
/// </summary>
public static class HeldWeapon
{
    public const string GripSocket = "SOCK_grip_primary";

    /// <summary>A grip frame: the point the hand closes on, the way the weapon's head points, and a direction fixing its roll.</summary>
    public static Transform3D Frame(Vector3 origin, Vector3 primary, Vector3 secondary)
    {
        var x = primary.Normalized();
        var y = (secondary - x * x.Dot(secondary)).Normalized();
        return new Transform3D(new Basis(x, y, x.Cross(y)), origin);
    }

    /// <summary>The weapon <paramref name="modelId"/> with its grip socket on <paramref name="grip"/>; null without the model or the socket.</summary>
    public static Node3D? Mount(ArtLibrary art, string modelId, Transform3D grip)
    {
        if (art.Model(modelId) is not { } model)
            return null;
        if (model.FindChild(GripSocket, true, false) is not Node3D socket)
        {
            art.Report(modelId, $"no {GripSocket} socket to hold it by", modelId);
            model.Free();
            return null;
        }
        model.Transform = grip * ArtLibrary.Relative(model, socket).AffineInverse();
        return model;
    }

    /// <summary>
    /// The bound weapons in the greybox figure's hands, one per family (the prototype has one weapon of each): the body stays greybox,
    /// the sword, bow and spear are the asset library's.
    /// </summary>
    public static void Arm(Avatar avatar, ArtLibrary art, ArtBindings bindings)
    {
        foreach (var weapon in bindings.Weapons.Values)
        {
            var slot = weapon.Family switch { "sword" => Held.Sword, "bow" => Held.Bow, "polearm" => Held.Spear, _ => Held.Nothing };
            if (slot != Held.Nothing)
                avatar.Arm(slot, grip => Mount(art, weapon.Model, grip));
        }
    }
}
