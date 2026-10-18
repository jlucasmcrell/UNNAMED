// UNNAMED Presentation - one humanoid vocabulary over two rigs: the MPFB production rig's names and the MetaHuman / UE5 mannequin skeleton's
// Godot presentation only (D-11): how the body's modifiers find a bone; nothing the simulation sees

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The production rig's bone names (<c>hips</c>, <c>thigh.L</c>, <c>upper_arm.R</c> ...) are the vocabulary the modifiers speak; a
/// MetaHuman body (the shared skeleton for every people, owner ruling 2026-10-06: <c>pelvis</c>, <c>thigh_l</c>, <c>upperarm_r</c> ...)
/// answers to the same words here. Find and Resolve return the first name the skeleton actually has, the production rig's first, so an
/// MPFB body binds exactly as before.
/// </summary>
public static class BoneNames
{
    private static readonly Dictionary<string, string[]> Aliases = Build();

    private static Dictionary<string, string[]> Build()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["hips"] = new[] { "hips", "pelvis" },
            ["chest"] = new[] { "chest", "spine_05", "spine_04" },
            ["neck"] = new[] { "neck", "neck_01" },
            ["head"] = new[] { "head" },
        };
        foreach (var (mpfb, ue) in new[] { ("L", "l"), ("R", "r") })
        {
            map[$"thigh.{mpfb}"] = new[] { $"thigh.{mpfb}", $"thigh_{ue}" };
            map[$"shin.{mpfb}"] = new[] { $"shin.{mpfb}", $"calf_{ue}" };
            map[$"foot.{mpfb}"] = new[] { $"foot.{mpfb}", $"foot_{ue}" };
            map[$"upper_arm.{mpfb}"] = new[] { $"upper_arm.{mpfb}", $"upperarm_{ue}" };
            map[$"forearm.{mpfb}"] = new[] { $"forearm.{mpfb}", $"lowerarm_{ue}" };
            map[$"hand.{mpfb}"] = new[] { $"hand.{mpfb}", $"hand_{ue}" };
            map[$"eye.{mpfb}"] = new[] { $"identity_FACIAL_{mpfb}_Eye", $"eye.{mpfb}", $"FACIAL_{mpfb}_Eye" };
            map[$"SOCK_hand.{mpfb}"] = new[] { $"SOCK_hand.{mpfb}", $"SOCK_hand_{ue}" };
            foreach (string finger in new[] { "thumb", "index", "middle", "ring", "pinky" })
            {
                for (int k = 1; k <= 3; k++)
                    map[$"{finger}_0{k}.{mpfb}"] = new[] { $"{finger}_0{k}.{mpfb}", $"{finger}_0{k}_{ue}" };
            }
        }
        return map;
    }

    /// <summary>The skeleton's own name for <paramref name="canonical"/> (a production-rig name), or null when it has none.</summary>
    public static string? Resolve(Skeleton3D skeleton, string canonical)
    {
        foreach (string name in Aliases.TryGetValue(canonical, out var names) ? names : new[] { canonical })
        {
            if (skeleton.FindBone(name) >= 0)
                return name;
        }
        return null;
    }

    /// <summary>The bone index for <paramref name="canonical"/> on this skeleton, -1 when it has none.</summary>
    public static int Find(Skeleton3D skeleton, string canonical) => Resolve(skeleton, canonical) is { } name ? skeleton.FindBone(name) : -1;

    /// <summary>The side of a hand or socket bone in either vocabulary: "R" for hand.R / hand_r / SOCK_hand_r, else "L".</summary>
    public static string Side(string bone) => bone.EndsWith(".R", StringComparison.Ordinal) || bone.EndsWith("_r", StringComparison.Ordinal) ? "R" : "L";

    /// <summary>The same bone on the other side, in the vocabulary it was given in: hand.R to hand.L, hand_r to hand_l.</summary>
    public static string Opposite(string bone) => bone[^2] switch
    {
        '.' => bone[..^1] + (bone[^1] == 'R' ? "L" : "R"),
        '_' => bone[..^1] + (bone[^1] == 'r' ? "l" : "r"),
        _ => bone,
    };
}
