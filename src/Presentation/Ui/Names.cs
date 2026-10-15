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
public static partial class Names
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

    /// <summary>
    /// A door, container or station key as it reads after "the": a corpse is named for the creature it was, and a key that is an instance
    /// ID (a placed piece's <c>pce_…</c>) for its definition, <paramref name="defId"/> - never the ID read as words.
    /// </summary>
    public static string Place(GameSession session, string key, string? defId = null) =>
        session.Simulation?.Creatures.FirstOrDefault(c => c.CorpseKey == key) is { } dead ? $"{Of(session, dead.DefId)} remains"
        : session.DisplayName(key) is var named && named != key ? named
        : IsInstanceId(key) && defId is not null && defId != key ? Of(session, defId).ToLowerInvariant()
        : Place(key);

    /// <summary>An equipment slot as a player says it: "main hand", never <c>main_hand</c>.</summary>
    public static string Slot(EquipSlot slot) => EquipSlots.Key(slot).Replace('_', ' ');
}
