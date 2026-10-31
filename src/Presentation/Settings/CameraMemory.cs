// UNNAMED Presentation - the camera's memory of how far back it sits for each kind of work (CMB-147; CAMERA §320-330)
// Godot-free, so the tests compile it as the game does (D-11: nothing here is game state)

using System.Collections.Immutable;

namespace UNNAMED.Presentation.Settings;

/// <summary>
/// What the character is doing, as far as the camera is concerned (CAMERA §320-330): walking about, fighting, talking, working at a bench, or
/// indoors. Each has its own remembered distance, so coming out of a conversation does not leave the camera at the conversational distance for
/// the rest of the walk.
/// </summary>
public enum CameraActivity
{
    Exploring,
    Combat,
    Conversation,
    Crafting,
    Interior,
}

/// <summary>
/// How far back one character likes the camera for each of the five activities (CMB-147). It is a memory, not a setting: nothing here is
/// drawn on a slider. A manual zoom rewrites the entry for the activity the character is in, and changing activity eases the camera back to
/// whatever that activity last chose - so the camera keeps its own habit per kind of work, and a save keeps the habit per character.
/// </summary>
public sealed record CameraMemory(
    float Exploring = 3.5f,
    float Combat = 4.6f,
    float Conversation = 2.1f,
    float Crafting = 1.1f,
    float Interior = 2.4f)
{
    public static CameraMemory Default { get; } = new();

    /// <summary>The distance remembered for one activity.</summary>
    public float For(CameraActivity activity) => activity switch
    {
        CameraActivity.Combat => Combat,
        CameraActivity.Conversation => Conversation,
        CameraActivity.Crafting => Crafting,
        CameraActivity.Interior => Interior,
        _ => Exploring,
    };

    /// <summary>The same memory with one activity rewritten, clamped to what the rig can actually do.</summary>
    public CameraMemory With(CameraActivity activity, float metres) => activity switch
    {
        CameraActivity.Combat => this with { Combat = CameraMemoryRules.Clamp(metres) },
        CameraActivity.Conversation => this with { Conversation = CameraMemoryRules.Clamp(metres) },
        CameraActivity.Crafting => this with { Crafting = CameraMemoryRules.Clamp(metres) },
        CameraActivity.Interior => this with { Interior = CameraMemoryRules.Clamp(metres) },
        _ => this with { Exploring = CameraMemoryRules.Clamp(metres) },
    };

    /// <summary>Every entry inside the rig's range: a prefs file edited by hand cannot put the camera somewhere it cannot go.</summary>
    public CameraMemory Normalised() => new(
        CameraMemoryRules.Clamp(Exploring), CameraMemoryRules.Clamp(Combat), CameraMemoryRules.Clamp(Conversation),
        CameraMemoryRules.Clamp(Crafting), CameraMemoryRules.Clamp(Interior));
}

public static class CameraMemoryRules
{
    /// <summary>The rig's own range: 0 is the eye, and the arm will not extend past six metres.</summary>
    public const float MaxDistance = 6f;

    /// <summary>How fast the camera eases back to an activity's remembered distance. Slower than a mouse wheel on purpose: it is a settle,
    /// not a snap, and the owner feels it rather than sees it.</summary>
    public const float EasePerSecond = 3.5f;

    /// <summary>A remembered distance as it will be written down: finite, and inside the rig's range.</summary>
    public static float Clamp(float metres) => float.IsFinite(metres) ? Math.Clamp(metres, 0f, MaxDistance) : MaxDistance;

    /// <summary>
    /// Which activity the character is in, in precedence order: a conversation is the narrowest frame and wins over a fight joined during it;
    /// a bench wins over a fight because the wheel is on the bench; a fight wins over the room; and everything else is exploring.
    /// </summary>
    public static CameraActivity Classify(bool inConversation, bool crafting, bool inCombat, bool interior)
    {
        if (inConversation)
            return CameraActivity.Conversation;
        if (crafting)
            return CameraActivity.Crafting;
        if (inCombat)
            return CameraActivity.Combat;
        return interior ? CameraActivity.Interior : CameraActivity.Exploring;
    }

    /// <summary>
    /// One frame of the settle: an exponential approach with no overshoot, snapping the last centimetre so it never creeps. Delta is seconds.
    /// </summary>
    public static float Ease(float current, float target, double delta)
    {
        float blend = 1f - MathF.Exp(-EasePerSecond * (float)delta);
        float next = current + (target - current) * blend;
        return MathF.Abs(next - target) < 0.01f ? target : next;
    }

    /// <summary>
    /// A manual zoom: the wheel moves the camera now, and the activity the character is in remembers where it was left. Both halves are the
    /// same fact, so they are one call.
    /// </summary>
    public static (float Distance, CameraMemory Memory) Zoom(CameraMemory memory, CameraActivity activity, float current, float metres)
    {
        float distance = Clamp(current + metres);
        return (distance, memory.With(activity, distance));
    }
}
