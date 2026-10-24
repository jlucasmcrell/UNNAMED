namespace UNNAMED.Presentation.Ui.Models;

/// <summary>The accepted motion set that a production HumGen payload must provide.</summary>
public static class HumGenMotionPolicy
{
    public static readonly string[] Required = ["idle", "idle_shift", "idle_look", "idle_foldarms", "walk", "run", "sprint",
        "walk_strafe_left", "walk_strafe_right", "strafe_left", "strafe_right", "walk_back", "run_back",
        "crouch_idle", "crouch_walk", "sword_ready", "sword_attack_1", "sword_attack_2", "sword_attack_3",
        "spear_ready", "spear_thrust", "sit", "sit_talk", "lie_to_idle", "get_up", "jump_start", "captive_kneel"];

    public static void Validate(IEnumerable<string> states, IReadOnlyDictionary<string, string> sources, int contactsSchema, int playbackSchema)
    {
        var present = states.ToHashSet(StringComparer.Ordinal);
        var missing = Required.Where(state => !present.Contains(state)).ToArray();
        if (missing.Length > 0) throw new InvalidDataException("HumGen motion is incomplete: " + string.Join(", ", missing));
        foreach (string state in present)
        {
            if (!sources.TryGetValue(state, out string? source) || string.IsNullOrWhiteSpace(source))
                throw new InvalidDataException("HumGen motion has no source receipt: " + state);
            if (source.Contains("kaykit", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("KayKit is forbidden for any animation: " + state);
        }
        if (contactsSchema != 1) throw new InvalidDataException("HumGen motion lacks measured foot contacts");
        if (playbackSchema != 1) throw new InvalidDataException("HumGen motion lacks original source playback timing");
    }
}
