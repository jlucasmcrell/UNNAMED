// UNNAMED Content Schema Resolution
// Maps content kind names to C# schema types and content directories
// Follows DATA_MODEL.md section 1.1 CLOSED vocabulary exactly

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UNNAMED.Content;

/// <summary>
/// Closed vocabulary of content kinds from DATA_MODEL.md section 1.1.
/// Every directory maps to exactly one kind, and every kind maps to exactly one directory.
/// </summary>
public static class ContentKindRegistry
{
    private static readonly Dictionary<string, ContentKindDefinition> _kinds;
    private static readonly Dictionary<string, ContentKindDefinition> _kindByDirectory;
    
    /// <summary>
    /// Get all registered kinds.
    /// </summary>
    public static IReadOnlyDictionary<string, ContentKindDefinition> Kinds => _kinds;
    
    /// <summary>
    /// Get kind definition by directory name.
    /// </summary>
    public static IReadOnlyDictionary<string, ContentKindDefinition> KindByDirectory => _kindByDirectory;
    
    /// <summary>
    /// Resolve kind name to definition.
    /// </summary>
    public static ContentKindDefinition ResolveKind(string kindName)
    {
        if (_kinds.TryGetValue(kindName, out var kind))
        {
            return kind;
        }
        throw new ArgumentException($"Unknown content kind: {kindName}", nameof(kindName));
    }
    
    /// <summary>
    /// Resolve directory to kind definition.
    /// </summary>
    public static ContentKindDefinition ResolveDirectory(string directory)
    {
        if (_kindByDirectory.TryGetValue(directory, out var kind))
        {
            return kind;
        }
        throw new ArgumentException($"Unknown content directory: {directory}. Only known directories from DATA_MODEL.md section 1.1 are allowed", nameof(directory));
    }
    
    /// <summary>
    /// Static constructor initializes the closed kind vocabulary from DATA_MODEL.md section 1.1.
    /// </summary>
    static ContentKindRegistry()
    {
        // This is the CLOSED vocabulary from DATA_MODEL.md section 1.1 - no other kinds are allowed
        var kinds = new Dictionary<string, ContentKindDefinition>();
        
        // 1. item (base kind)
        kinds["item"] = new ContentKindDefinition(
            Kind: "item",
            FullKind: "item",
            Directory: "items",
            IdPrefix: "item"
        );
        
        // 2. weapon (sub-kind of item)
        kinds["item.weapon"] = new ContentKindDefinition(
            Kind: "item.weapon",
            FullKind: "item.weapon",
            Directory: "items",
            IdPrefix: "item.weapon"
        );
        
        // 3. armor (sub-kind of item)
        kinds["item.armor"] = new ContentKindDefinition(
            Kind: "item.armor",
            FullKind: "item.armor",
            Directory: "items",
            IdPrefix: "item.armor"
        );
        
        // 4. creature
        kinds["creature"] = new ContentKindDefinition(
            Kind: "creature",
            FullKind: "creature",
            Directory: "creatures",
            IdPrefix: "creature"
        );
        
        // 5. NPC
        kinds["npc"] = new ContentKindDefinition(
            Kind: "npc",
            FullKind: "npc",
            Directory: "npcs",
            IdPrefix: "npc"
        );
        
        // 6. spell
        kinds["spell"] = new ContentKindDefinition(
            Kind: "spell",
            FullKind: "spell",
            Directory: "spells",
            IdPrefix: "spell"
        );
        
        // 7. ability
        kinds["ability"] = new ContentKindDefinition(
            Kind: "ability",
            FullKind: "ability",
            Directory: "abilities",
            IdPrefix: "ability"
        );
        
        // 8. effect (status_effect)
        kinds["effect"] = new ContentKindDefinition(
            Kind: "effect",
            FullKind: "effect",
            Directory: "effects",
            IdPrefix: "effect"
        );
        
        // 9. recipe
        kinds["recipe"] = new ContentKindDefinition(
            Kind: "recipe",
            FullKind: "recipe",
            Directory: "recipes",
            IdPrefix: "recipe"
        );
        
        // 10. resource
        kinds["resource"] = new ContentKindDefinition(
            Kind: "resource",
            FullKind: "resource",
            Directory: "resources",
            IdPrefix: "resource"
        );
        
        // 11. quest
        kinds["quest"] = new ContentKindDefinition(
            Kind: "quest",
            FullKind: "quest",
            Directory: "quests",
            IdPrefix: "quest"
        );
        
        // 12. dialogue
        kinds["dialogue"] = new ContentKindDefinition(
            Kind: "dialogue",
            FullKind: "dialogue",
            Directory: "dialogue",
            IdPrefix: "dialogue"
        );
        
        // 13. faction
        kinds["faction"] = new ContentKindDefinition(
            Kind: "faction",
            FullKind: "faction",
            Directory: "factions",
            IdPrefix: "faction"
        );
        
        // 14. loot
        kinds["loot"] = new ContentKindDefinition(
            Kind: "loot",
            FullKind: "loot",
            Directory: "loot",
            IdPrefix: "loot"
        );
        
        // 15. merchant
        kinds["merchant"] = new ContentKindDefinition(
            Kind: "merchant",
            FullKind: "merchant",
            Directory: "merchants",
            IdPrefix: "merchant"
        );
        
        // Referenced kinds (minimum shape - per section 1.2)
        // 16. affix
        kinds["affix"] = new ContentKindDefinition(
            Kind: "affix",
            FullKind: "affix",
            Directory: "affixes",
            IdPrefix: "affix"
        );
        
        // 17. node
        kinds["node"] = new ContentKindDefinition(
            Kind: "node",
            FullKind: "node",
            Directory: "nodes",
            IdPrefix: "node"
        );
        
        // 18. spawn
        kinds["spawn"] = new ContentKindDefinition(
            Kind: "spawn",
            FullKind: "spawn",
            Directory: "spawns",
            IdPrefix: "spawn"
        );
        
        // 19. location
        kinds["location"] = new ContentKindDefinition(
            Kind: "location",
            FullKind: "location",
            Directory: "locations",
            IdPrefix: "location"
        );
        
        // 20. config
        kinds["config"] = new ContentKindDefinition(
            Kind: "config",
            FullKind: "config",
            Directory: "config",
            IdPrefix: "config"
        );

        // 20b. skill - a discipline of AX-SKL (DATA_MODEL.md §4.21, added by the M2c progression audit)
        kinds["skill"] = new ContentKindDefinition(
            Kind: "skill",
            FullKind: "skill",
            Directory: "skills",
            IdPrefix: "skill"
        );
        
        // Referenced kinds from section 1.2
        // 21. species
        kinds["species"] = new ContentKindDefinition(
            Kind: "species",
            FullKind: "species",
            Directory: "species",
            IdPrefix: "species"
        );
        
        // 22. schedule
        kinds["schedule"] = new ContentKindDefinition(
            Kind: "schedule",
            FullKind: "schedule",
            Directory: "schedules",
            IdPrefix: "schedule"
        );
        
        // 23. set
        kinds["set"] = new ContentKindDefinition(
            Kind: "set",
            FullKind: "set",
            Directory: "sets",
            IdPrefix: "set"
        );
        
        // 24. region
        kinds["region"] = new ContentKindDefinition(
            Kind: "region",
            FullKind: "region",
            Directory: "regions",
            IdPrefix: "region"
        );
        
        // 25. anchor
        kinds["anchor"] = new ContentKindDefinition(
            Kind: "anchor",
            FullKind: "anchor",
            Directory: "anchors",
            IdPrefix: "anchor"
        );
        
        // 26. fact
        kinds["fact"] = new ContentKindDefinition(
            Kind: "fact",
            FullKind: "fact",
            Directory: "facts",
            IdPrefix: "fact"
        );
        
        // 27. world_flag (uses world. prefix)
        kinds["world_flag"] = new ContentKindDefinition(
            Kind: "world_flag",
            FullKind: "world_flag",
            Directory: "world_flags",
            IdPrefix: "world"
        );
        
        // 28. piece (M7): a building piece the player places
        kinds["piece"] = new ContentKindDefinition(
            Kind: "piece",
            FullKind: "piece",
            Directory: "pieces",
            IdPrefix: "piece"
        );
        
        _kinds = kinds;
        
        // Build directory lookup (first occurrence only to avoid duplicates)
        var seenDirectories = new HashSet<string>();
        _kindByDirectory = kinds.Values.Where(k => seenDirectories.Add(k.Directory)).ToDictionary(
            k => k.Directory,
            v => v,
            StringComparer.OrdinalIgnoreCase
        );
    }
    
    /// <summary>
    /// Check if a kind name is valid.
    /// </summary>
    public static bool IsValidKind(string kindName)
    {
        return _kinds.ContainsKey(kindName);
    }
    
    /// <summary>
    /// Check if a directory name is valid.
    /// </summary>
    public static bool IsValidDirectory(string directory)
    {
        return _kindByDirectory.ContainsKey(directory);
    }
    
    /// <summary>
    /// Resolve kind name to definition (returns default if not found).
    /// </summary>
    public static ContentKindDefinition? GetKind(string kindName)
    {
        if (_kinds.TryGetValue(kindName, out var kind))
        {
            return kind;
        }
        return null;
    }
    
    /// <summary>
    /// Check if an ID prefix matches a kind.
    /// </summary>
    public static bool PrefixMatchesKind(string prefix, string kindName)
    {
        if (_kinds.TryGetValue(kindName, out var kind))
        {
            return prefix.StartsWith(kind.IdPrefix);
        }
        return false;
    }
    
    /// <summary>
    /// Get known directories (for validation).
    /// </summary>
    public static HashSet<string> KnownDirectories => new HashSet<string>(_kindByDirectory.Keys, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Definition of a content kind including its schema, directory, and ID prefix.
/// </summary>
/// <param name="Kind">Display name of the kind.</param>
/// <param name="FullKind">Fully qualified kind name.</param>
/// <param name="Directory">Content directory where this kind's files are stored.</param>
/// <param name="IdPrefix">Prefix for definition IDs of this kind.</param>
public record ContentKindDefinition(string Kind, string FullKind, string Directory, string IdPrefix);
