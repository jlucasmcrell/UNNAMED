// UNNAMED Presentation - the faces a HumGen creator rig can hold, named: the weights as the rig's own FACS channels describe them.

using System;
using System.Collections.Generic;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A named expression as weights over the face's native channels. The creator rig already carries the shapes (browDown, browInnerUp,
/// mouthFrown, eyeSquint, jawOpen and the rest), so an expression is a pose, not new geometry: the library is the vocabulary and the
/// weights, and <see cref="FaceDriver.Express"/> eases them onto the face under whatever a blink or a viseme is doing.
/// </summary>
public static class FaceExpression
{
    /// <summary>One expression: the channels it moves and how far, 0 to 1.</summary>
    public sealed record Pose(string Name, IReadOnlyDictionary<string, float> Weights);

    private static Pose Make(string name, params (string Channel, float Weight)[] weights)
    {
        var map = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (channel, weight) in weights)
            map[channel] = weight;
        return new Pose(name, map);
    }

    /// <summary>Every expression the game can put on a face, by the name content uses (<c>mood: grief</c>).</summary>
    public static readonly IReadOnlyDictionary<string, Pose> All = new Dictionary<string, Pose>(StringComparer.OrdinalIgnoreCase)
    {
        // Plain, warm and glad: the mouth and the cheeks carry these, the brows little.
        ["neutral"] = Make("neutral"),
        ["warm"] = Make("warm", ("mouthSmileLeft", .45f), ("mouthSmileRight", .45f), ("cheekSquintLeft", .2f), ("cheekSquintRight", .2f)),
        ["cheer"] = Make("cheer", ("mouthSmileLeft", .85f), ("mouthSmileRight", .85f), ("cheekSquintLeft", .45f), ("cheekSquintRight", .45f),
            ("eyeSquintLeft", .35f), ("eyeSquintRight", .35f), ("browInnerUp", .25f)),
        // Sorrow: the inner brows lift and pull together, the mouth corners fall, the lids drop.
        ["grief"] = Make("grief", ("browInnerUp", .8f), ("browDownLeft", .25f), ("browDownRight", .25f), ("mouthFrownLeft", .6f),
            ("mouthFrownRight", .6f), ("eyeSquintLeft", .3f), ("eyeSquintRight", .3f)),
        // Anger: the brows drive down and in, the lids narrow, the jaw sets.
        ["anger"] = Make("anger", ("browDownLeft", .85f), ("browDownRight", .85f), ("browInnerUp", .15f), ("eyeSquintLeft", .45f),
            ("eyeSquintRight", .45f), ("mouthPressLeft", .4f), ("mouthPressRight", .4f), ("noseSneerLeft", .25f), ("noseSneerRight", .25f)),
        // Fear: the brows lift and pull together, the eyes widen, the mouth stretches.
        ["fear"] = Make("fear", ("browInnerUp", .9f), ("eyeWideLeft", .8f), ("eyeWideRight", .8f), ("mouthStretchLeft", .5f),
            ("mouthStretchRight", .5f), ("jawOpen", .18f)),
        // Surprise: brows up, eyes wide, jaw open, no fear in the mouth.
        ["surprise"] = Make("surprise", ("browInnerUp", .95f), ("eyeWideLeft", .85f), ("eyeWideRight", .85f), ("jawOpen", .32f)),
        // Pain: a flinch, one side of the face pulling away.
        ["pain"] = Make("pain", ("browDownLeft", .6f), ("browInnerUp", .5f), ("eyeSquintLeft", .7f), ("eyeSquintRight", .5f),
            ("mouthStretchLeft", .35f), ("noseSneerLeft", .4f), ("jawOpen", .12f)),
        // Focus: a worker looking at what their hands are doing - brows low, eyes narrow, mouth closed.
        ["focus"] = Make("focus", ("browDownLeft", .35f), ("browDownRight", .35f), ("eyeSquintLeft", .3f), ("eyeSquintRight", .3f),
            ("mouthClose", .2f)),
        // Disdain: one brow up is not in the rig, so the mouth and the nose do it.
        ["disdain"] = Make("disdain", ("noseSneerLeft", .55f), ("noseSneerRight", .35f), ("mouthSmileLeft", .2f), ("eyeSquintLeft", .4f),
            ("browDownLeft", .3f)),
        // Concern: the question a steward asks of a stranger.
        ["concern"] = Make("concern", ("browInnerUp", .55f), ("browDownLeft", .2f), ("browDownRight", .2f), ("mouthFrownLeft", .25f),
            ("mouthFrownRight", .25f), ("eyeSquintLeft", .2f), ("eyeSquintRight", .2f)),
    };

    /// <summary>The expression <paramref name="name"/> names, or null when content asks for one this library does not hold.</summary>
    public static Pose? Find(string? name) =>
        name is { Length: > 0 } && All.TryGetValue(name, out var pose) ? pose : null;

    /// <summary>The names, in the order a sheet should show them.</summary>
    public static readonly string[] Sheet =
    {
        "neutral", "warm", "cheer", "grief", "anger", "fear", "surprise", "pain", "focus", "disdain", "concern",
    };
}
