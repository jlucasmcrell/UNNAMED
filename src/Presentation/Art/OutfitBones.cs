using Godot;

namespace UNNAMED.Presentation.Art;

public static partial class Outfit
{
    internal static void NameSkinBinds(IEnumerable<MeshInstance3D> meshes, Skeleton3D source)
    {
        var converted = new Dictionary<Skin, Skin>();
        foreach (var mesh in meshes)
        {
            if (mesh.Skin is not { } original) continue;
            if (!converted.TryGetValue(original, out var named))
            {
                named = new Skin();
                for (int bind = 0; bind < original.GetBindCount(); bind++)
                {
                    var name = original.GetBindName(bind);
                    if (name.IsEmpty)
                    {
                        int bone = original.GetBindBone(bind);
                        if (bone < 0 || bone >= source.GetBoneCount()) throw new InvalidDataException("Native part has an invalid skin bind");
                        name = source.GetBoneName(bone);
                    }
                    named.AddNamedBind(name, original.GetBindPose(bind));
                }
                converted.Add(original, named);
            }
            mesh.Skin = named;
        }
    }
    // Modular garments may author their own cloth joints. Append only positively
    // weighted cloth bones and their parents. Existing anatomy/rests/poses and
    // skin-bind indices are never replaced. Unworn cloth joints remain unused.
    internal static void GarmentBones(Skeleton3D target, Skeleton3D source, IEnumerable<MeshInstance3D> meshes)
    {
        int Ensure(int bone)
        {
            string name = source.GetBoneName(bone);
            int existing = target.FindBone(name);
            if (existing >= 0)
                return existing;
            if (!name.StartsWith("cloth_", StringComparison.Ordinal) && !name.StartsWith("hair_", StringComparison.Ordinal))
                throw new InvalidDataException($"Garment requires missing body joint {name}; no anatomy is substituted");
            int parent = source.GetBoneParent(bone);
            int above = parent < 0 ? -1 : Ensure(parent);
            int at = target.GetBoneCount();
            target.AddBone(name);
            target.SetBoneParent(at, above);
            target.SetBoneRest(at, source.GetBoneRest(bone));
            target.ResetBonePose(at);
            return at;
        }

        foreach (var mesh in meshes.Where(m => m.Skin is not null))
        {
            var skin = mesh.Skin!;
            var bones = Enumerable.Range(0, skin.GetBindCount()).Select(bind =>
            {
                var name = skin.GetBindName(bind);
                return name.IsEmpty ? skin.GetBindBone(bind) : source.FindBone(name);
            }).ToArray();
            if (bones.All(b => b >= 0 && target.FindBone(source.GetBoneName(b)) >= 0))
                continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(surface);
                var joints = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                foreach (int bind in joints.Where((_, i) => weights[i] > 0).Distinct())
                    Ensure(bones[bind]);
            }
        }
    }
}
