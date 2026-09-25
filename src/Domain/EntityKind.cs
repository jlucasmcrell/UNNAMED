// UNNAMED Domain - EntityKind
// Instance-ID prefixes per D-04 / DATA_MODEL.md §2 ("Prefixes: itm item, npc NPC, ...").
// No Godot references - this is pure C# domain logic

namespace UNNAMED.Domain;

/// <summary>
/// The category of a runtime instance. It selects the instance-ID prefix, and it is a property of
/// the INSTANCE, not of its definition: a corpse (<c>crp</c>) is created from a creature definition,
/// and a chest may be a container instance of an item definition.
/// </summary>
public enum EntityKind
{
    Item,
    Npc,
    Creature,
    Building,
    Container,
    QuestInstance,
    Corpse,
    TravelAnchor,
    Summon,
    FarmPlot,
    WorldEvent,

    /// <summary>
    /// A player character. DATA_MODEL.md §2 defines no prefix for the player; <c>chr</c> is an
    /// assumption recorded in docs/M2_STATUS.md, pending the owner's decision.
    /// </summary>
    Character,

    /// <summary>A player-placed building piece (M7): its identity is derived, never minted (<see cref="EntityId.Derived"/>).</summary>
    Piece,
}

public static class EntityKinds
{
    public static string Prefix(EntityKind kind) => kind switch
    {
        EntityKind.Item => "itm",
        EntityKind.Npc => "npc",
        EntityKind.Creature => "crt",
        EntityKind.Building => "bld",
        EntityKind.Container => "cnt",
        EntityKind.QuestInstance => "qst",
        EntityKind.Corpse => "crp",
        EntityKind.TravelAnchor => "anc",
        EntityKind.Summon => "sum",
        EntityKind.FarmPlot => "plt",
        EntityKind.WorldEvent => "evt",
        EntityKind.Character => "chr",
        EntityKind.Piece => "pce",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entity kind"),
    };

    public static bool TryFromPrefix(string prefix, out EntityKind kind)
    {
        foreach (EntityKind candidate in Enum.GetValues<EntityKind>())
        {
            if (string.Equals(Prefix(candidate), prefix, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }
        kind = default;
        return false;
    }

    /// <summary>
    /// The instance kind a definition's top-level content kind implies, where that is unambiguous.
    /// Only four content kinds map one-to-one onto an instance category; every other instance
    /// (container, corpse, building, summon, ...) must be created with an explicit kind.
    /// </summary>
    public static bool TryInferFromDefinition(DefinitionId definitionId, out EntityKind kind)
    {
        string value = definitionId.Value ?? string.Empty;
        int dot = value.IndexOf('.');
        string contentKind = dot < 0 ? value : value[..dot];
        switch (contentKind)
        {
            case "item": kind = EntityKind.Item; return true;
            case "creature": kind = EntityKind.Creature; return true;
            case "npc": kind = EntityKind.Npc; return true;
            case "quest": kind = EntityKind.QuestInstance; return true;
            default: kind = default; return false;
        }
    }
}
