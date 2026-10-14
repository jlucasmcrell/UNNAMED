// UNNAMED Presentation - the one way an ID becomes words on the screen
// Godot presentation only: it reads content's names; nothing here holds state (D-11)

using UNNAMED.Application;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Every name the player reads goes through here (doc 09 §8: "every dotted definition ID is replaced by its display name before it
/// reaches the player"). Content's own name when it has one; otherwise the ID read as words - <c>item.tool.fitting_wedge</c> is
/// "Fitting wedge", <c>cairnwold.forge_anvil</c> the "forge anvil", <c>cw_house05_door</c> the "house door" - never the ID itself.
/// </summary>
public static class Names
{
    /// <summary>A definition's name: content's, or its ID read as words in sentence case.</summary>
    public static string Of(GameSession session, string id)
    {
        string name = session.DisplayName(id);
        if (name != id)
            return name;
        string words = Words(id);
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>An item's name with its quality (M3f): a fine or crude one says so; a standard one is just itself.</summary>
    public static string Item(GameSession session, string defId, int quality) => quality switch
    {
        Quality.Fine => "Fine " + Of(session, defId),
        Quality.Crude => "Crude " + Of(session, defId),
        _ => Of(session, defId),
    };

    /// <summary>A door, container or station key as it reads after "the": a corpse is named for the creature it was.</summary>
    public static string Place(GameSession session, string key) =>
        session.Simulation?.Creatures.FirstOrDefault(c => c.CorpseKey == key) is { } dead ? $"{Of(session, dead.DefId)} remains"
        : session.DisplayName(key) is var named && named != key ? named
        : Place(key);

    /// <summary>A place key read as words: <c>door.forge_shed</c> is the "forge shed door", <c>cw_house05_door</c> the "house door".</summary>
    public static string Place(string key) =>
        key.StartsWith("door.", StringComparison.Ordinal) ? Words(key["door.".Length..]) + " door" : Words(key);

    /// <summary>An equipment slot as a player says it: "main hand", never <c>main_hand</c>.</summary>
    public static string Slot(EquipSlot slot) => EquipSlots.Key(slot).Replace('_', ' ');

    /// <summary>
    /// An ID as lower-case words: its last dotted part, without a short leading area code (<c>cw_</c>) or the numbers that tell copies
    /// apart, its underscores spaces.
    /// </summary>
    public static string Words(string id)
    {
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
