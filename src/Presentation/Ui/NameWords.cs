// UNNAMED Presentation - the part of the display-name path that needs nothing but the ID: reading an ID as words
// Godot presentation only, and Godot-free, so Presentation.Tests compiles it as the game does (D-11)

using System.Text.RegularExpressions;

namespace UNNAMED.Presentation.Ui;

public static partial class Names
{
    /// <summary>An instance ID's shape (D-04): a three-letter kind, an underscore and a 26-character Crockford ULID - <c>itm_01J8…</c>.</summary>
    private static readonly Regex Instance = new("^[a-z]{3}_[0-9a-hjkmnp-tv-z]{26}$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether a key is an instance ID rather than a content or layout key: such a key is never shown, not even as words.</summary>
    public static bool IsInstanceId(string key) => Instance.IsMatch(key);

    /// <summary>
    /// A place key read as words: <c>door.forge_shed</c> is the "forge shed door", <c>cw_house05_door</c> the "house door". An instance ID
    /// says only "thing": the HUD coherence note's "Work at the pce 0000000002vx1t…", which a caller avoids by naming the definition.
    /// </summary>
    public static string Place(string key) =>
        key.StartsWith("door.", StringComparison.Ordinal) ? Words(key["door.".Length..]) + " door"
        : key.StartsWith("container.stash.", StringComparison.Ordinal) ? "stash at " + Words(key)
        : Words(key);

    /// <summary>
    /// An ID as lower-case words: its last dotted part, without a short leading area code (<c>cw_</c>) or the numbers that tell copies
    /// apart, its underscores spaces. An instance ID has no words in it: it reads "thing".
    /// </summary>
    public static string Words(string id)
    {
        if (IsInstanceId(id))
            return "thing";
        string last = id[(id.LastIndexOf('.') + 1)..];
        var parts = last.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(c => !char.IsDigit(c)).ToArray()).ToLowerInvariant())
            .Where(p => p.Length > 0)
            .ToList();
        if (parts.Count > 1 && parts[0].Length <= 2)
            parts.RemoveAt(0);
        return parts.Count == 0 ? "thing" : string.Join(' ', parts);
    }
}
