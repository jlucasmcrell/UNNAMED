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

    /// <summary>Rigid grip through the closed fingers' channel, evaluated read-only in hand space.</summary>
    public static Transform3D ClosedGrip(Skeleton3D skeleton, string handName, IReadOnlyDictionary<int, Quaternion> rotations)
    {
        int hand = BoneNames.Find(skeleton, handName);
        if (hand < 0) throw new InvalidDataException("Grip hand missing: " + handName);
        string side = BoneNames.Side(handName);
        var posed = new Dictionary<int, Transform3D> { [hand] = Transform3D.Identity };
        Transform3D At(int bone)
        {
            if (posed.TryGetValue(bone, out var cached)) return cached;
            if (bone < 0) throw new InvalidDataException("Grip digit is not a child of " + handName);
            var local = skeleton.GetBoneRest(bone);
            if (rotations.TryGetValue(bone, out var q)) local.Basis = new Basis(q) * Basis.FromScale(local.Basis.Scale);
            return posed[bone] = At(skeleton.GetBoneParent(bone)) * local;
        }
        Vector3 Point(string digit, int joint) => At(BoneNames.Find(skeleton, $"{digit}_0{joint}.{side}")).Origin;
        Vector3 Channel(string digit) => (Point(digit, 1) + Point(digit, 3)) * .5f;
        var centre = (Channel("middle") + Channel("ring")) * .5f;
        var across = Channel("index") - Channel("pinky");
        var rest = skeleton.GetBoneGlobalRest(hand).AffineInverse() * skeleton.GetBoneGlobalRest(BoneNames.Find(skeleton, "middle_01." + side)).Origin;
        return Frame(centre, across, rest);
    }

    /// <summary>The weapon <paramref name="modelId"/> with its grip socket on <paramref name="grip"/>; null without the model or the socket.</summary>
    public static Node3D? Mount(ArtLibrary art, string modelId, Transform3D grip, float scale = 1, Transform3D? authoredSocket = null)
    {
        if (art.Model(modelId) is not { } model)
        {
            art.Coverage.Fallback("weapon", modelId, $"{art.Why(modelId) ?? "not drawn"}: a greybox weapon", modelId);
            return null;
        }
        var socket = model.FindChild(GripSocket, true, false) as Node3D;
        if (socket is null && authoredSocket is null)
        {
            art.Report(modelId, $"no {GripSocket} socket to hold it by", modelId);
            art.Coverage.Fallback("weapon", modelId, $"no {GripSocket} socket to hold it by: a greybox weapon", modelId);
            model.Free();
            return null;
        }
        // Scale around the equipment socket. Scaling the mounted root afterwards
        // displaces an off-origin socket and pushes the handle through the hand.
        model.Transform = grip * new Transform3D(Basis.FromScale(Vector3.One * scale), Vector3.Zero)
            * (authoredSocket ?? ArtLibrary.Relative(model, socket!)).AffineInverse();
        // Equipment attaches after the body receives its character-light layer.
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
                     .Prepend(model as MeshInstance3D).OfType<MeshInstance3D>())
            mesh.Layers |= MHCharacters.MetaHumanFigure.FillLayer;
        art.Coverage.Resolved("weapon", modelId, modelId);
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
