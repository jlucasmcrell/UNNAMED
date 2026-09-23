// UNNAMED Content Validation - Base Content Definition
// Defines the envelope and common fields for all content kinds
// No Godot references

using YamlDotNet.Serialization;

namespace UNNAMED.Content;

/// <summary>
/// Schema version for content definitions. Bump on breaking changes.
/// </summary>
public static class SchemaVersion
{
    public const int Current = 1;
}

/// <summary>
/// Base interface for all content definitions. All definitions must include
/// schema, id, kind, display_key, and tags.
/// </summary>
public interface IContentDefinition
{
    /// <summary>
    /// Schema revision. Bump on incompatible shape change.
    /// </summary>
    int Schema { get; }

    /// <summary>
    /// Unique definition ID. Format: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]
    /// Example: item.weapon.iron_sword
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The kind of this definition. Must match the directory and prefix.
    /// Example: item.weapon
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Display key for localization. Never raw English in logic.
    /// </summary>
    string DisplayKey { get; }

    /// <summary>
    /// Tags from content/_tags.yaml (validator-enforced closed set).
    /// </summary>
    string[] Tags { get; }
}

/// <summary>
/// Common envelope for all content definitions.
/// Every definition must include these fields at the top level.
/// </summary>
public record ContentEnvelope
{
    /// <summary>
    /// Schema revision. Bump on incompatible shape change.
    /// Default: 1
    /// </summary>
    [YamlMember(Alias = "schema", ApplyNamingConventions = false)]
    public int Schema { get; set; } = SchemaVersion.Current;

    /// <summary>
    /// Unique definition ID. Format: <kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]
    /// Example: item.weapon.iron_sword
    /// </summary>
    [YamlMember(Alias = "id", ApplyNamingConventions = false)]
    public required string Id { get; set; }

    /// <summary>
    /// The kind of this definition. Must match the directory and prefix.
    /// Example: item.weapon
    /// </summary>
    [YamlMember(Alias = "kind", ApplyNamingConventions = false)]
    public required string Kind { get; set; }

    /// <summary>
    /// Display key for localization. Never raw English in logic.
    /// </summary>
    [YamlMember(Alias = "display_key", ApplyNamingConventions = false)]
    public required string DisplayKey { get; set; }

    /// <summary>
    /// Tags from content/_tags.yaml (validator-enforced closed set).
    /// </summary>
    [YamlMember(Alias = "tags", ApplyNamingConventions = false)]
    public required string[] Tags { get; set; }

    /// <summary>
    /// Optional notes describing the intent or design rationale.
    /// </summary>
    [YamlMember(Alias = "notes", ApplyNamingConventions = false)]
    public string? Notes { get; set; }

    /// <summary>
    /// Optional: IDs defined by this definition (for sub-elements).
    /// </summary>
    [YamlMember(Alias = "defines", ApplyNamingConventions = false)]
    public string[]? Defines { get; set; }

    /// <summary>
    /// Optional: Mark as deprecated with replacement info.
    /// </summary>
    [YamlMember(Alias = "deprecated", ApplyNamingConventions = false)]
    public string? Deprecated { get; set; }

    /// <summary>
    /// Optional: If this is an alias/removal record.
    /// </summary>
    [YamlMember(Alias = "alias_of", ApplyNamingConventions = false)]
    public string? AliasOf { get; set; }
    
    /// <summary>
    /// Internal: Raw YAML source (for debugging).
    /// </summary>
    public string? YamlSource { get; set; }
    
    /// <summary>
    /// Internal: Source file path (for error reporting).
    /// </summary>
    public string? SourceFile { get; set; }
    
    /// <summary>
    /// Internal: Line offset within source file.
    /// </summary>
    public int LineOffset { get; set; }
}

/// <summary>
/// Tag vocabulary. Validator must ensure all tags come from this closed set.
/// This is defined in content/_tags.yaml and loaded separately.
/// </summary>
public static class TagVocabulary
{
    // Common tags for items
    public const string Weapon = "weapon";
    public const string Armor = "armor";
    public const string Consumable = "consumable";
    public const string Material = "material";
    public const string Tool = "tool";
    public const string Book = "book";
    public const string QuestItem = "quest_item";
    public const string Container = "container";
    public const string Currency = "currency";
    public const string Misc = "misc";

    // Weapon subtypes
    public const string Sword = "sword";
    public const string Dagger = "dagger";
    public const string Mace = "mace";
    public const string Axe = "axe";
    public const string Spear = "spear";
    public const string Bow = "bow";
    public const string Staff = "staff";

    // Material types
    public const string Metal = "metal";
    public const string Wood = "wood";
    public const string Leather = "leather";
    public const string Cloth = "cloth";
    public const string Stone = "stone";
    public const string Bone = "bone";

    // Rarity
    public const string Common = "common";
    public const string Uncommon = "uncommon";
    public const string Rare = "rare";
    public const string Epic = "epic";
    public const string Legendary = "legendary";
    public const string Artifact = "artifact";

    // Add more as needed for other definition kinds
}

/// <summary>
/// Summary of content validation results.
/// </summary>
public record ContentValidationSummary
{
    /// <summary>
    /// Number of successfully loaded definitions.
    /// </summary>
    public int TotalLoaded { get; set; }
    
    /// <summary>
    /// Number of validation errors.
    /// </summary>
    public int TotalErrors { get; set; }
    
    /// <summary>
    /// List of all validation errors.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; set; } = Array.Empty<ValidationError>();
    
    /// <summary>
    /// Whether there are any validation errors.
    /// </summary>
    public bool HasErrors { get; set; }
}
